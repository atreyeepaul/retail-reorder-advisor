# Retail Reorder Advisor — ASP.NET Core Implementation Plan

**Solo build · 10 working days · Azure free account · .NET 10 (LTS)**

---

## 1. Goal and scope

A nightly batch web application that tells a retail buyer, for one store and about 200 SKUs, **what to reorder today, how much, and why**. It also flags overstock and dead stock. The whole system is one ASP.NET Core app on App Service Free (F1), with Blob Storage for data and Application Insights for monitoring.

**In scope:** one store, CSV input, a nightly batch run, blob-only storage, two dashboard pages, a Teams/Slack webhook digest, Bicep IaC, and GitHub Actions CI/CD.

**Out of scope:** multi-store, real-time POS integration, ML forecasting, user authentication, ERP writeback, and mobile.

---

## 2. Architecture

```
GitHub Actions (cron 02:00 IST) ──► POST /api/jobs/run-advisor  (X-Job-Key)
                                            │
Buyer / script ──► POST /api/uploads ──►  ASP.NET Core Web App  (App Service F1, Linux)
                   (X-Admin-Key)            │
                                            ├─ Ingestion:  CSV → validate → normalized/ + quarantine/
                                            ├─ Advisor:    velocity, σ, ROP, flags → recommendations/
                                            ├─ Read API:   /api/summary, /api/recommendations, export.csv
                                            ├─ Dashboard:  Razor Pages (Action list, Health summary)
                                            └─ Notifier:   Teams/Slack webhook digest
                                            │
                            Blob Storage (managed identity)  ·  App Insights + Log Analytics
```

### Why the design looks like this

The F1 tier has no "Always On," so the app sleeps after about 20 minutes idle. An in-process timer (`BackgroundService`, Hangfire, Quartz) would not reliably fire at 02:00. A GitHub Actions scheduled workflow calls a protected endpoint instead, which wakes the app and runs the job.

App Service has no blob trigger. Ingestion happens through an upload endpoint that validates synchronously and returns a validation report.

The dashboard is served from the same app, so there is no Static Web App and no CORS configuration.

### Azure resources

| Resource | SKU / setting | Cost basis |
|---|---|---|
| App Service plan | **F1 (Free), Linux** | Free: 60 CPU min/day, 1 GB RAM |
| Web App | .NET 10, system-assigned managed identity, HTTPS only | Included in F1 |
| Storage account | StorageV2, Standard_LRS, Hot, no public blob access | 5 GB free for 12 months |
| Log Analytics workspace | Daily cap 0.1 GB, 30-day retention | Within monthly free ingestion |
| Application Insights | Workspace-based, sampling on | Billed through the workspace |

**Expected cost:** ₹0–₹30/month.

---

## 3. Technology choices

| Concern | Choice |
|---|---|
| Runtime | .NET 10 LTS, C# |
| Web | ASP.NET Core **Minimal APIs** for the JSON API, **Razor Pages** for the dashboard |
| Blob access | `Azure.Storage.Blobs` + `Azure.Identity` (`DefaultAzureCredential`) |
| CSV | `CsvHelper` |
| JSON | `System.Text.Json` with source-generated contexts |
| Telemetry | `Azure.Monitor.OpenTelemetry.AspNetCore` |
| Webhook | `HttpClient` via `IHttpClientFactory` |
| Tests | xUnit (built-in asserts), `Microsoft.AspNetCore.Mvc.Testing` |
| Local storage emulator | Azurite (`UseDevelopmentStorage=true`) |
| IaC | Bicep |
| CI/CD | GitHub Actions with OIDC federated login (no stored Azure secrets) |

---

## 4. Solution structure

```
RetailReorderAdvisor/
├── RetailReorderAdvisor.sln
├── src/
│   ├── Reorder.Core/                 ← pure C#, zero Azure/ASP.NET dependencies
│   │   ├── Models/                   SalesRecord, InventoryItem, Supplier, Recommendation, RunSummary
│   │   ├── Ingestion/                RowValidator, ValidationResult, RejectReason
│   │   ├── Advisor/                  DemandStats, ReorderCalculator, FlagClassifier, ReasonBuilder
│   │   └── AdvisorOptions.cs
│   ├── Reorder.Web/                  ← ASP.NET Core host
│   │   ├── Program.cs
│   │   ├── Endpoints/                UploadEndpoints, JobEndpoints, ReadEndpoints
│   │   ├── Pages/                    Index (action list), Health (summary), Shared/_Layout
│   │   ├── Services/                 IngestionService, AdvisorJob, RecommendationCache
│   │   ├── Infrastructure/           BlobStore, CsvReaders, WebhookNotifier, ApiKeyFilter
│   │   └── wwwroot/                  site.css, table.js (sort/filter)
│   └── Reorder.DataGen/              ← console app: synthetic data + backtest
├── tests/
│   ├── Reorder.Core.Tests/           calculator, flags, validator, edge cases
│   └── Reorder.Web.Tests/            endpoint integration tests (WebApplicationFactory)
├── infra/
│   ├── main.bicep
│   └── main.parameters.json
├── scripts/
│   ├── simulate-7-days.ps1
│   └── teardown.ps1
└── .github/workflows/
    ├── ci.yml
    ├── deploy.yml
    └── nightly.yml
```

**Rule:** `Reorder.Core` must never reference an Azure or ASP.NET package. That keeps the business logic fast to test and easy to explain.

---

## 5. Data contracts

### 5.1 Input CSVs

**sales.csv** holds daily aggregates, one row per `date + sku`.

| Column | Type | Rules |
|---|---|---|
| date | `DateOnly` (ISO `yyyy-MM-dd`) | Required, not in the future relative to `asOf` |
| sku | string | Required, must exist in inventory.csv |
| qty_sold | int | Required, ≥ 0 |
| unit_price | decimal | Required, > 0 |

**inventory.csv** is a snapshot as of the upload date.

| Column | Type | Rules |
|---|---|---|
| sku | string | Required, unique |
| name | string | Required |
| category | string | Optional |
| on_hand | int | Required, ≥ 0 |
| unit_cost | decimal | Required, > 0 |
| supplier_id | string | Required, should exist in suppliers.csv |

**suppliers.csv**

| Column | Type | Rules |
|---|---|---|
| supplier_id | string | Required, unique |
| name | string | Required |
| lead_time_days | int | Optional (defaults from config), 1–120 |
| moq | int | Optional, default 1, ≥ 1 |
| pack_size | int | Optional, default 1, ≥ 1 |

### 5.2 Validation and quarantine rules

A row is rejected into quarantine, with a reason code, for any of these: `MISSING_FIELD`, `BAD_TYPE`, `NEGATIVE_QTY`, `FUTURE_DATE`, `UNKNOWN_SKU`, `DUPLICATE_KEY` (keep the first, reject later ones), or `UNKNOWN_SUPPLIER`.

A **reject threshold** protects the output. If more than 20% of sales rows are rejected, the ingestion fails, the previous `latest.json` stays in place, and the webhook posts an alert instead of a digest.

### 5.3 Blob layout

| Container / path | Written by | Contents |
|---|---|---|
| `raw/{asOf}/sales.csv` etc. | Upload endpoint | Files exactly as uploaded |
| `normalized/{asOf}.json` | Ingestion | Validated, typed records for all three files |
| `quarantine/{asOf}.json` | Ingestion | Rejected rows with line number and reason |
| `recommendations/{asOf}.json` | Advisor job | Dated snapshot |
| `recommendations/latest.json` | Advisor job | Current output, read by API and dashboard |
| `locks/advisor.lock` | Advisor job | Blob lease that prevents overlapping runs |

A lifecycle rule deletes `raw/` blobs older than 30 days.

### 5.4 Output: `latest.json`

```json
{
  "summary": {
    "asOf": "2026-10-07",
    "generatedAtUtc": "2026-10-06T20:30:12Z",
    "skuCount": 200,
    "countsByFlag": { "URGENT": 7, "STOCKOUT_RISK": 12, "OVERSTOCK": 18, "DEAD_STOCK": 5, "HEALTHY": 158 },
    "overstockValue": 84250.00,
    "deadStockValue": 12900.00,
    "estLostSalesAvoided": 31480.00,
    "quarantinedRows": 23,
    "runDurationMs": 412
  },
  "recommendations": [
    {
      "sku": "4471", "name": "Basmati Rice 5kg", "category": "Grocery",
      "supplierId": "SUP-03", "supplierName": "Delta Foods",
      "onHand": 18, "velocity": 6.2, "sigma": 2.1, "leadTimeDays": 5,
      "safetyStock": 7.7, "reorderPoint": 38.7, "daysOfCover": 2.9,
      "suggestedQty": 210, "flag": "URGENT", "confidence": "High",
      "estimatedStockoutDate": "2026-10-09",
      "reason": "On hand 18 covers ~2.9 days but supplier lead time is 5 days. Order 210 now."
    }
  ]
}
```

---

## 6. Core business logic (`Reorder.Core`)

### 6.1 Configuration (`AdvisorOptions`)

| Setting | Default | Meaning |
|---|---|---|
| `WindowDays` | 28 | Trailing window for velocity and σ |
| `ServiceLevelZ` | 1.65 | About a 95% service level |
| `CoverDays` | 30 | Target days of cover after reordering |
| `OverstockDays` | 90 | Cover above this is overstock |
| `DeadStockDays` | 60 | No sales for this long is dead stock |
| `DefaultLeadTimeDays` | 7 | Used when a supplier's lead time is missing |
| `LowConfidenceHistoryDays` | 14 | Less history than this marks the result low confidence |
| `MaxRejectRatio` | 0.20 | Ingestion abort threshold |

### 6.2 Calculations

**Build the daily series.** For each SKU, build one value per day in the window ending at `asOf`. **Missing days count as zero sales.** Skipping them would overstate velocity. For a SKU whose first sale is inside the window, the window starts at the first sale date.

| Quantity | Formula |
|---|---|
| Velocity `v` | Mean of the daily series |
| Variability `σ` | Sample standard deviation (n − 1); 0 when n < 2 |
| Lead time `L` | Supplier value, else `DefaultLeadTimeDays` |
| Safety stock `SS` | `Z × σ × √L` |
| Reorder point `ROP` | `v × L + SS` |
| Days of cover | `onHand / v`; `null` when v = 0 |
| Order-up-to level | `ROP + v × CoverDays` |
| Suggested qty | If `onHand ≤ ROP`: `max(MOQ, ceil(orderUpTo − onHand))`, rounded up to a multiple of `pack_size`. Otherwise 0. |
| Estimated stockout date | `asOf + floor(daysOfCover)` when v > 0 |

The suggested quantity here subtracts stock already on hand, which refines the original "30 days × velocity" rule so that you don't over-order.

### 6.3 Flag rules

Each SKU gets exactly one flag, checked in this order:

1. `DEAD_STOCK`: on hand > 0 and no sales in the last `DeadStockDays`.
2. `URGENT`: v > 0 and on hand < `v × L`. The stock will run out before a new order can arrive.
3. `STOCKOUT_RISK`: on hand < ROP.
4. `OVERSTOCK`: days of cover > `OverstockDays`.
5. `HEALTHY`: everything else.

**Ranking:** sort by flag severity (URGENT, STOCKOUT_RISK, DEAD_STOCK, OVERSTOCK, HEALTHY), then by days of cover ascending.

### 6.4 Summary metrics

| Metric | Definition |
|---|---|
| Overstock value | Σ over OVERSTOCK SKUs of `(onHand − v × OverstockDays) × unit_cost` |
| Dead-stock value | Σ over DEAD_STOCK SKUs of `onHand × unit_cost` |
| Est. lost sales avoided | Σ over URGENT SKUs of `max(0, L − daysOfCover) × v × unit_price` |

The dashboard labels the last metric as an estimate and shows its formula in a tooltip.

### 6.5 Edge cases (each one gets a unit test)

| Case | Behaviour |
|---|---|
| Zero velocity, stock on hand, sales within 60 days | `HEALTHY`, days of cover `null`, suggested qty 0 |
| Zero velocity, no sales in 60+ days | `DEAD_STOCK` |
| Zero velocity and zero on hand | `HEALTHY`, low confidence, reason notes "no recent demand" |
| Brand-new SKU with no sales history | `HEALTHY`, low confidence, no order suggested |
| Single day of history | σ = 0, SS = 0, low confidence |
| Missing supplier lead time | Default used; the reason text says so |
| MOQ larger than computed need | Suggested qty = MOQ |
| Pack size rounding | 43 units with pack size 12 becomes 48 |
| On hand exactly equals ROP | Order suggested (the boundary is `≤`) |

### 6.6 Known limitation (put this in the README)

When a shelf is empty, recorded sales are zero even though customers wanted the item. This is called **censored demand**, and it makes velocity look lower than true demand for SKUs that recently stocked out. Fixing it properly needs daily stock history. Documenting the limitation shows you understand the model's boundaries.

### 6.7 Reason text

`ReasonBuilder` generates one plain-English sentence per SKU from the numbers, for example:
*"On hand 18 covers ~2.9 days but supplier lead time is 5 days. Order 210 now."*
Reasons are deterministic and unit-tested, so the same inputs always produce the same text.

---

## 7. Web application (`Reorder.Web`)

### 7.1 Endpoints

| Method & route | Auth | Purpose |
|---|---|---|
| `POST /api/uploads?asOf=yyyy-MM-dd` | `X-Admin-Key` | Multipart upload of the 3 CSVs (10 MB limit). Stores raw files, runs ingestion, returns accepted/rejected counts and the first 50 rejects. |
| `POST /api/jobs/run-advisor?asOf=` | `X-Job-Key` | Runs the advisor on the latest (or given) normalized data, writes outputs, sends the digest. Returns the summary. |
| `GET /api/summary` | None | Summary block from `latest.json` |
| `GET /api/recommendations?flag=&sort=` | None | Ranked list, optionally filtered |
| `GET /api/recommendations/export.csv?flag=` | None | CSV download |
| `GET /health` | None | Health check including a blob reachability probe |

The dashboard and read API are unauthenticated and read-only, since authentication is out of scope. The two write endpoints use an `IEndpointFilter` that compares the key with `CryptographicOperations.FixedTimeEquals`.

### 7.2 Services

`IngestionService` parses the CSVs with CsvHelper, calls the pure `RowValidator`, writes normalized and quarantine blobs, and enforces the reject threshold.

`AdvisorJob` acquires a 60-second blob lease on `locks/advisor.lock` (returns 409 if a run is already in progress), loads normalized data, calls the core calculator, writes the dated snapshot and then `latest.json`, invalidates the cache, and calls the notifier.

`RecommendationCache` holds the parsed `latest.json` in `IMemoryCache` for 10 minutes and is cleared after each job run.

`WebhookNotifier` posts a short digest: flag counts, the top 5 urgent SKUs, and a link to the dashboard. A webhook failure is logged but does not fail the job.

### 7.3 Dashboard (Razor Pages)

**Action list (`/`)** is a server-rendered table with SKU, name, on hand, days of cover, suggested qty, supplier, flag badge, and reason. Flag filter chips use a query string, so the page works without JavaScript. About 40 lines of vanilla JS add column sorting, and an "Export CSV" button links to the export endpoint.

**Health summary (`/health-summary`)** shows five tiles (at-risk SKUs, overstock value, dead-stock count and value, estimated lost sales avoided, last run time and status) and a simple CSS bar showing the flag distribution.

Both pages read from `RecommendationCache`, so there is no client-side state management or front-end build step.

### 7.4 Configuration

```json
{
  "Advisor": { "WindowDays": 28, "ServiceLevelZ": 1.65, "CoverDays": 30,
               "OverstockDays": 90, "DeadStockDays": 60, "DefaultLeadTimeDays": 7 },
  "Storage": { "BlobServiceUri": "https://<account>.blob.core.windows.net" },
  "Security": { "JobKey": "", "AdminKey": "" },
  "Notifications": { "WebhookUrl": "" }
}
```

Locally, `Storage:ConnectionString = UseDevelopmentStorage=true` points at Azurite, and secrets go in `dotnet user-secrets`. In Azure, blob access uses the managed identity and the keys and webhook URL are App Settings set by the pipeline. No secrets are committed.

### 7.5 Observability

OpenTelemetry sends requests, dependencies, and logs to Application Insights. The advisor job logs one structured event per run (`AdvisorRunCompleted` with asOf, SKU count, counts by flag, duration, and quarantined rows). Sampling is on, and Live Metrics is off.

---

## 8. Synthetic data and backtest (`Reorder.DataGen`)

**`generate` command:** produces 97 days of data for 200 SKUs with a fixed random seed. Days 1–90 are history. Days 91–97 are seven simulation days, each with its own inventory snapshot.

- Baseline demand per SKU drawn from a log-normal distribution, so most items are slow and a few are fast.
- Weekend uplift of 20–40% and mild monthly seasonality.
- The inventory simulation replenishes stock using supplier lead times.
- Seeded scenarios so every flag fires: about 10 upcoming stockouts, 5 dead-stock SKUs, 15 overstocked SKUs, and 3 demand spikes.
- About 1–2% deliberately dirty rows: negative quantities, unknown SKUs, missing and malformed dates, and duplicates.
- A `ground-truth.json` file listing each seeded stockout date.

**`backtest` command:** runs `Reorder.Core` directly for every simulation day and checks the ground truth. It reports the percentage of seeded stockouts that were flagged URGENT or STOCKOUT_RISK at least 3 days ahead. The target is 80% or higher.

---

## 9. Infrastructure as code (`infra/main.bicep`)

The Bicep file defines:

- A storage account (`Standard_LRS`, `allowBlobPublicAccess: false`, `minimumTlsVersion: 'TLS1_2'`), five containers, and the lifecycle rule for `raw/`.
- A Log Analytics workspace with `workspaceCapping.dailyQuotaGb: 0.1` and 30-day retention, plus a workspace-based Application Insights component.
- An App Service plan with `sku: { name: 'F1' }`, `kind: 'linux'`, `reserved: true`.
- A web app with `linuxFxVersion: 'DOTNETCORE|10.0'`, **`alwaysOn: false`** (F1 rejects `true` and the deployment fails), `httpsOnly: true`, `ftpsState: 'Disabled'`, a system-assigned identity, and app settings.
- A role assignment granting the web app's identity **Storage Blob Data Contributor** on the storage account.

All resources go in one resource group in **Central India**. If F1 quota is unavailable there, pick one other region and use it for everything.

---

## 10. CI/CD (`.github/workflows`)

| Workflow | Trigger | Steps |
|---|---|---|
| `ci.yml` | Pull requests, pushes | `dotnet restore`, `build`, `test` with coverage; fails on any test failure |
| `deploy.yml` | Push to `main`, manual | Test, `dotnet publish`, `azure/login` (OIDC), `az deployment group create` for Bicep, set app settings from GitHub secrets, `azure/webapps-deploy`, smoke test `GET /health` with retries |
| `nightly.yml` | `cron: '30 20 * * *'` (02:00 IST), manual | `curl -X POST` to `/api/jobs/run-advisor` with `X-Job-Key`, `--retry 5 --max-time 120` to allow for cold start; fail the workflow on a non-2xx response |

**GitHub secrets:** `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `JOB_KEY`, `ADMIN_KEY`, `WEBHOOK_URL`, `APP_URL`.

**Two scheduler caveats to note in the README:** GitHub can start scheduled runs several minutes late, and it disables scheduled workflows in public repos after 60 days without repo activity.

---

## 11. Ten-day execution plan

### Day 1 — Guardrails, skeleton, infrastructure

- **First task:** budget alert at ₹500 with notifications at 50%, 80%, and 100%. Confirm the spending limit is ON.
- Create the repo, solution, and all projects with references. Add `.editorconfig`, `.gitignore`, and `Directory.Build.props` (nullable on, warnings as errors).
- Write `main.bicep` and deploy it manually with `az deployment group create`.
- Build the `DataGen generate` command and commit a sample dataset.

**Done when:** the empty web app returns 200 at its Azure URL, and DataGen produces 97 days of CSVs.

### Day 2 — Ingestion

- Write the core models and the pure `RowValidator`, test-first.
- Build the CsvHelper readers, `BlobStore`, and `IngestionService`.
- Add `POST /api/uploads` with the admin key filter. Set up Azurite locally.

**Done when:** uploading the dirty sample data produces correct `normalized/` and `quarantine/` blobs, and the reject threshold test passes.

### Day 3 — Advisor core (critical path, protect this day)

- `DemandStats` (zero-filled daily series, mean, sample σ), `ReorderCalculator`, `FlagClassifier`, and `ReasonBuilder`, all test-first.
- Include hand-calculated test cases, such as the SKU 4471 example, checked to two decimal places.

**Done when:** `Reorder.Core` has green tests for every formula and flag, with no Azure code involved.

### Day 4 — Advisor job end to end

- `AdvisorJob` with blob lease locking, snapshot then `latest.json`, and `POST /api/jobs/run-advisor`.
- Run the full flow locally against Azurite, then in Azure, triggered manually with curl.

**Done when:** upload, then run, produces a correct `latest.json` in Azure.

### Day 5 — Edge cases, telemetry, backtest

- Write all edge-case tests from section 6.5.
- Add OpenTelemetry and App Insights, including the structured `AdvisorRunCompleted` event.
- Build `DataGen backtest` and tune only the seeded data, not the formulas, if the backtest reveals generator bugs.

**Done when:** every edge-case test passes, the backtest reports ≥80%, and runs are visible in App Insights.

### Day 6 — Read API

- `/api/summary`, `/api/recommendations` with filter and sort, CSV export, `RecommendationCache`, and `/health`.
- Integration tests with `WebApplicationFactory`, including 401 responses for missing or wrong keys.

**Done when:** all endpoints return correct data in Azure and integration tests pass.

### Day 7 — Action list page (flex day)

- `_Layout`, the action list table, flag filter chips, sort script, export button, and responsive CSS.

**Done when:** a buyer can filter to URGENT, sort by days of cover, and download a CSV.

### Day 8 — Health summary and webhook (flex day)

- Summary tiles and flag distribution bar.
- `WebhookNotifier` with the digest format and an alert variant for failed ingestion.

**Done when:** a manual job run posts a readable digest to Teams or Slack.

### Day 9 — CI/CD and reproducibility

- `ci.yml`, `deploy.yml` with OIDC, and `nightly.yml`.
- **Reproducibility drill:** delete the resource group, run `deploy.yml` from scratch, re-upload data, trigger a run.
- Take a Cost Analysis screenshot.

**Done when:** one pipeline run rebuilds the whole environment from an empty resource group, and the nightly workflow succeeds.

### Day 10 — Simulation, documentation, demo

- Run `scripts/simulate-7-days.ps1`, which uploads each simulation day and triggers the advisor with that `asOf`. Confirm 7 clean runs.
- Write the README: architecture diagram, formulas, design decisions (why F1 plus cron, why blob-only), known limitations, run-locally steps, and teardown.
- Record a 3-minute demo: problem, upload, nightly run, dashboard, webhook, cost screenshot.
- Keep the rest of the day as buffer.

---

## 12. Testing strategy

| Layer | Tool | What it covers |
|---|---|---|
| Unit (`Reorder.Core.Tests`) | xUnit | Every formula, every flag rule and boundary, every edge case, the validator, reason text |
| Integration (`Reorder.Web.Tests`) | `WebApplicationFactory` with an in-memory `IBlobStore` fake | Endpoint contracts, key authorization, upload-to-recommendation flow |
| Backtest | `DataGen backtest` | The ≥80% early-warning success criterion |
| Smoke | `deploy.yml` | `/health` responds after every deployment |

Target: 90% or higher line coverage on `Reorder.Core`. Coverage of the web project is not a goal.

---

## 13. Cost guardrails

1. Budget alert at ₹500 (50/80/100%), created on Day 1 before any resources.
2. Spending limit stays ON. Turning it off converts the account to pay-as-you-go.
3. Log Analytics daily cap of 0.1 GB, 30-day retention, sampling on, Live Metrics off.
4. Lifecycle rule deletes `raw/` after 30 days.
5. A single region for all resources, to avoid egress charges.
6. `scripts/teardown.ps1` runs `az group delete`, and the README documents it.

---

## 14. Risks and mitigations

| Risk | Mitigation |
|---|---|
| F1 cold start makes the cron call time out | curl retries with a 120-second timeout; `/health` warm-up call first |
| F1 quota unavailable in Central India | Pick one other region on Day 1 and use it everywhere |
| Scheduled workflow runs late or gets disabled | Acceptable for a nightly batch; manual `workflow_dispatch` as a fallback; noted in README |
| 60 CPU min/day limit | A 200-SKU run takes under a second of CPU; cache reads |
| Dirty data breaks a run | Validation and quarantine on Day 2; reject threshold keeps the last good output |
| Overlapping runs | Blob lease lock returns 409 |
| Front-end rabbit hole | Server-rendered Razor Pages; Days 7–8 can shrink to a plain table |
| Scope creep into forecasting or multi-store | Listed as explicit non-goals |

---

## 15. Definition of done

- ≥80% of seeded stockouts flagged at least 3 days in advance (backtest report).
- Seven consecutive simulated daily runs complete with no manual fixes.
- One `deploy.yml` run rebuilds the environment from an empty resource group.
- Cost Analysis shows effectively zero spend.
- All formulas and edge cases in `Reorder.Core` are unit-tested and green in CI.
- README and a 3-minute demo video are published.
