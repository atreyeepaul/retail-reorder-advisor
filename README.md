# Retail Reorder Advisor

A nightly-batch web app that tells a retail buyer, per SKU, **what to reorder today, how much, and why** —
and flags overstock and dead stock. It replaces gut feel with a transparent reorder-point calculation, and is
designed to run at effectively **₹0** on an Azure free account.

**Stack:** ASP.NET Core (.NET 10) · Azure App Service F1 (Linux) · Blob Storage · Application Insights ·
Bicep · GitHub Actions.

The buyer uploads three CSVs (sales, inventory, suppliers), a nightly job computes ranked reorder
recommendations, and the results appear on two dashboards plus a Microsoft Teams digest.

## Architecture

```
GitHub Actions (cron 02:00 IST) ──► POST /api/jobs/run-advisor  (X-Job-Key)
                                            │
Buyer / script ──► POST /api/uploads ──►  ASP.NET Core Web App  (App Service F1, Linux)
                   (X-Admin-Key)            │
                                            ├─ Ingestion:  CSV → validate → normalized/ + quarantine/
                                            ├─ Advisor:    velocity, σ, ROP, flags → recommendations/
                                            ├─ Read API:   /api/summary, /api/recommendations, export.csv
                                            ├─ Dashboard:  Razor Pages (Action list, Health summary)
                                            └─ Notifier:   Teams webhook digest
                                            │
                            Blob Storage (managed identity)  ·  App Insights + Log Analytics
```

**Azure resources** (one resource group, one region — Central India by default; see [`infra/main.bicep`](infra/main.bicep)):

- App Service **F1 (Free, Linux)** plan + web app — system-assigned managed identity, `httpsOnly`, no Always On.
- **Storage account** (Standard_LRS, HTTPS-only, no public blob access) with five containers — `raw`,
  `normalized`, `quarantine`, `recommendations`, `locks`; a 30-day lifecycle rule deletes `raw/`.
- **Log Analytics** (0.1 GB/day cap, 30-day retention) + workspace-based **Application Insights**.
- A role assignment granting the web app **Storage Blob Data Contributor** — the app reads/writes blobs with its
  managed identity, no connection strings.

**Data flow:** an upload stores the raw CSVs, validates them, and writes `normalized/{asOf}.json` +
`quarantine/{asOf}.json`. The advisor job takes a **60-second blob lease** on `locks/advisor.lock` (409 if a run
is already in progress), computes recommendations, writes the dated snapshot then `recommendations/latest.json`,
invalidates a 10-minute in-memory cache, and posts the Teams digest. The dashboards and read API serve from that
cache.

## How the advisor decides

For each SKU, over a trailing **28-day** window of zero-filled daily sales:

| Quantity | Formula |
|---|---|
| Velocity `v` | mean daily units sold |
| Sigma `σ` | sample standard deviation (n−1; 0 with fewer than 2 days of history) |
| Safety stock | `Z · σ · √L`  (Z = 1.65 ≈ 95% service level; L = supplier lead time) |
| Reorder point (ROP) | `v · L + safety stock` |
| Days of cover | `onHand / v`  (blank when `v = 0`) |
| Order-up-to | `ROP + v · CoverDays` |
| Suggested qty | when `v > 0` **and** `onHand ≤ ROP`: `max(MOQ, ceil(orderUpTo − onHand))`, rounded up to pack size; otherwise 0 |

**Flags** — each SKU gets exactly one, evaluated in this **check order**:

1. **DEAD_STOCK** — on hand but no sale in ≥ 60 days (a never-sold new SKU is *not* dead).
2. **URGENT** — `v > 0` and `onHand < v · L` (won't survive the lead time).
3. **STOCKOUT_RISK** — `onHand < ROP`.
4. **OVERSTOCK** — days of cover > 90.
5. **HEALTHY** — everything else.

The action list **ranks** by severity (URGENT → STOCKOUT_RISK → DEAD_STOCK → OVERSTOCK → HEALTHY), then by days
of cover ascending. A recommendation is **low confidence** when history < 14 days or velocity is 0. Every row
carries a plain-English reason.

**Tunables** (the `Advisor` section of `appsettings.json`):

| Setting | Default | Meaning |
|---|---|---|
| `WindowDays` | 28 | trailing window for velocity and σ |
| `ServiceLevelZ` | 1.65 | Z-score for the target service level (~95%) |
| `CoverDays` | 30 | target days of cover after a reorder arrives |
| `OverstockDays` | 90 | days of cover above which a SKU is OVERSTOCK |
| `DeadStockDays` | 60 | days without a sale after which a SKU is DEAD_STOCK |
| `DefaultLeadTimeDays` | 7 | lead time used when a supplier record has none |
| `LowConfidenceHistoryDays` | 14 | history shorter than this marks a recommendation low confidence |
| `MaxRejectRatio` | 0.20 | share of rejected sales rows above which ingestion aborts |

## Repository layout

```
src/Reorder.Core        Pure business logic (zero Azure/ASP.NET dependencies)
src/Reorder.Web         ASP.NET Core host: API, dashboard, jobs, notifier
src/Reorder.DataGen     Synthetic data generator + backtest
tests/                  xUnit test projects (127 tests)
infra/                  Bicep templates
scripts/                Deploy, teardown, and simulation scripts (PowerShell 7)
docs/                   Plan and daily guides
data/                   Committed synthetic dataset (seed 42)
```

## Run locally

Requires the **.NET 10 SDK** (pinned in `global.json`) and **Azurite** for blob storage.

```powershell
# 1. Blob storage emulator (separate terminal)
azurite --silent

# 2. Local API keys (empty keys reject every write with 401)
dotnet user-secrets --project src/Reorder.Web set "Security:AdminKey" "dev"
dotnet user-secrets --project src/Reorder.Web set "Security:JobKey"   "dev"

# 3. Build, test, run
dotnet build
dotnet test
dotnet run --project src/Reorder.Web --launch-profile http   # http://localhost:5080
```

`appsettings.Development.json` points storage at Azurite (`UseDevelopmentStorage=true`). The synthetic dataset is
already committed under `data/`; regenerate it only to change parameters:
`dotnet run --project src/Reorder.DataGen -- generate --out data`. Then upload a day and run the advisor with the
endpoints below, or with `./scripts/simulate-7-days.ps1 -BaseUrl http://localhost:5080 -AdminKey dev -JobKey dev`.

### Endpoints

| Method & route | Auth | Purpose |
|---|---|---|
| `POST /api/uploads?asOf=yyyy-MM-dd` | `X-Admin-Key` | Upload the 3 CSVs; validate; store normalized + quarantine |
| `POST /api/jobs/run-advisor?asOf=` | `X-Job-Key` | Run the advisor; write `latest.json`; post the digest |
| `GET /api/summary` · `/api/recommendations?flag=&sort=` · `/api/recommendations/export.csv?flag=` | none | Read the latest run |
| `GET /health` | none | Health + blob-reachability probe |

The dashboards are the **action list** (`/`) and the **health summary** (`/health-summary`).

## Deploy and tear down

```powershell
az login
$env:REORDER_JOB_KEY = "..."; $env:REORDER_ADMIN_KEY = "..."; $env:REORDER_WEBHOOK_URL = "..."
./scripts/deploy-infra.ps1     # resource group + Bicep (sets all app settings)
./scripts/deploy-app.ps1       # publish, zip-deploy, smoke-test /health
./scripts/teardown.ps1         # deletes the whole resource group
```

Resource names derive from `uniqueString(resourceGroup().id)`, so redeploying into the same resource group and
subscription reproduces the **same names and URL**. The reproducibility drill (rebuild from an empty resource
group) is in [`docs/day-09.md`](docs/day-09.md).

## CI/CD

Three GitHub Actions workflows:

- **CI** ([`ci.yml`](.github/workflows/ci.yml)) — restore, build (warnings as errors), test with coverage, and
  the backtest gate, on every push and PR.
- **Deploy** ([`deploy.yml`](.github/workflows/deploy.yml)) — on push to `main`, `azure/login` via OIDC → Bicep +
  app deploy → smoke-test `/health`.
- **Nightly** ([`nightly.yml`](.github/workflows/nightly.yml)) — `cron: '30 20 * * *'` (02:00 IST) triggers a run
  against the live app, with retries for the F1 cold start.

> **OIDC note:** `deploy.yml` needs an Entra app registration, which is blocked on the tenant this project runs in,
> so deploys are done with the scripts above. `ci.yml` and `nightly.yml` are unaffected. How to enable OIDC:
> [`docs/day-09.md`](docs/day-09.md) §1.

> **Scheduler caveats:** GitHub can start scheduled runs several minutes late, and disables scheduled workflows
> after 60 days of repository inactivity (a commit re-enables them).

## Simulation

Replay the seven simulated days against the deployed app — uploads each day and runs the advisor, asserting seven
clean runs:

```powershell
$env:REORDER_ADMIN_KEY = "..."; $env:REORDER_JOB_KEY = "..."
./scripts/simulate-7-days.ps1
```

## Design decisions

- **F1 + GitHub cron, not an in-process timer.** F1 has no Always On, so the app sleeps when idle and a
  `BackgroundService`/Quartz timer wouldn't reliably fire at 02:00. A scheduled GitHub workflow POSTs a
  key-protected endpoint, which wakes the app and runs the job.
- **Blob-only, no database.** ~200 SKUs and one nightly write don't need a database; JSON blobs keep cost near
  zero and let `Reorder.Core` stay pure (zero Azure/ASP.NET references), so the logic is fast to test and easy to
  explain.
- **Blob-lease lock.** The advisor takes a 60-second lease on `locks/advisor.lock`; a second concurrent run gets a
  409 instead of corrupting the output.
- **Reject threshold.** If more than 20% of sales rows are rejected, ingestion aborts, the previous `latest.json`
  stays in place, and the webhook posts an alert instead of a digest — bad data can't silently poison the results.
- **Managed identity + OIDC.** The app reads blobs with its managed identity (no connection strings), and the CI
  deploy is designed for OIDC federated login (no stored Azure secrets).

## Known limitations

- **Censored demand.** When a shelf is empty, recorded sales are 0 even though customers wanted the item, so
  velocity understates true demand for recently stocked-out SKUs. Fixing it properly needs daily stock history.
- **Cold start.** F1 sleeps after ~20 minutes idle; the first request can take 30–60 seconds (the nightly `curl`
  retries to cover this).
- **OIDC blocked on this tenant** — deploys run via the scripts rather than `deploy.yml` (see the CI/CD note).
- **Scope:** single store, CSV input, no authentication on the read side, no ML forecasting, no ERP writeback.

## Cost

Designed for **₹0** on an Azure free account: App Service **F1** (free), storage well within the 5 GB free tier,
and Log Analytics capped at **0.1 GB/day**. A 200-SKU run uses under a second of CPU.

## Status

The 10-day build is complete. See [`docs/implementation-plan.md`](docs/implementation-plan.md) for the full plan
and the daily guides ([`docs/day-01.md`](docs/day-01.md), [`docs/day-09.md`](docs/day-09.md),
[`docs/day-10.md`](docs/day-10.md)).
