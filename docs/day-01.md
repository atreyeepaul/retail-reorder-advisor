# Day 1 — Guardrails, skeleton, infrastructure

**Goal:** by the end of today the empty web app answers `200 OK` at its Azure URL,
the synthetic data generator produces 97 days of data, and cost guardrails are in place.

Time budget: about 5–6 hours. Steps 1 and 2 must happen **before** anything is deployed.

---

## 0. Prerequisites (once)

Open **PowerShell 7** (`pwsh`), not Windows PowerShell 5.1.

| Tool | Check | Install (winget) |
|---|---|---|
| .NET 10 SDK | `dotnet --list-sdks` shows `10.0.x` | `winget install Microsoft.DotNet.SDK.10` |
| Azure CLI | `az version` | `winget install Microsoft.AzureCLI` |
| Bicep | `az bicep version` | `az bicep install` (or `az bicep upgrade`) |
| PowerShell 7 | `pwsh --version` | `winget install Microsoft.PowerShell` |
| Git | `git --version` | `winget install Git.Git` |
| IDE | — | Visual Studio 2026, VS Code + C# Dev Kit, or Rider |

Close and reopen the terminal after installing anything so `PATH` updates.

---

## 1. Cost guardrails — first, before any resource exists

### 1a. Confirm the spending limit is ON

1. Azure portal → **Subscriptions** → your subscription → **Overview**.
2. You should see a notice that the spending limit is on.
3. **Never** choose "Remove spending limit". Removing it moves the subscription to pay-as-you-go billing.

### 1b. Create a ₹500 budget with alerts

1. Portal → **Cost Management + Billing** → **Cost Management** → **Budgets** → **+ Add**.
2. Scope: your subscription. Name: `reorder-advisor-budget`. Reset period: Monthly. Amount: `500`.
3. Alert conditions:
   - Actual, 50%
   - Actual, 80%
   - Actual, 100%
   - Forecasted, 100%
4. Alert recipients: your email. Save.

> 📸 Screenshot the budget page now. It goes in the README on Day 10.

---

## 2. Put the project in `C:\Projects`

1. Extract the downloaded zip into `C:\Projects`, giving `C:\Projects\RetailReorderAdvisor`.
2. Windows marks files from downloaded zips as untrusted, which blocks the `.ps1` scripts. Unblock them:

```powershell
cd C:\Projects\RetailReorderAdvisor
Get-ChildItem -Recurse | Unblock-File
```

3. Start the Git history:

```powershell
git init -b main
git add .
git commit -m "Day 1: solution skeleton, Bicep infrastructure, data generator"
```

---

## 3. Build and test locally

```powershell
dotnet restore
dotnet build
dotnet test
```

Expected: build succeeds with **0 warnings**, and **7 tests pass**
(5 in `Reorder.Core.Tests`, 2 in `Reorder.Web.Tests`).

Warnings are treated as errors (see `Directory.Build.props`), so the codebase stays clean from the start.

---

## 4. Run the web app locally

```powershell
dotnet run --project src/Reorder.Web --launch-profile http
```

- <http://localhost:5080> shows the placeholder action list page.
- <http://localhost:5080/health> returns `Healthy`.

Stop it with `Ctrl+C`.

---

## 5. Generate the synthetic dataset

```powershell
dotnet run --project src/Reorder.DataGen -- generate --out data
```

You should see a summary like this (exact numbers depend on the seed):

```
Retail Reorder Advisor — synthetic data generated
  Seed ............... 42
  SKUs ............... 200 (dead 5, overstock 15, planned stockout 10, demand spike 3)
  History ............ 2026-06-01 to 2026-08-29 (90 days)
  Simulation ......... 2026-08-30 to 2026-09-05 (7 days)
  As-of folders ...... 8 in C:\Projects\RetailReorderAdvisor\data
  Latest sales.csv ... ~15,000 clean + ~230 dirty rows (~1.5 %)
  ...
```

### What you get

```
data/
├── 2026-08-29/          ← end of history: the first upload
│   ├── sales.csv        full sales history up to this date (plus dirty rows)
│   ├── inventory.csv    end-of-day stock snapshot
│   └── suppliers.csv
├── 2026-08-30/ … 2026-09-05/   ← 7 simulation days, one complete upload each
└── ground-truth.json    seeded scenarios + every stockout, for the Day 5 backtest
```

### What's seeded in the data

| Scenario | Count | What it exercises |
|---|---|---|
| Dead stock | 5 | No sales for 70 days while stock sits on the shelf → `DEAD_STOCK` |
| Overstock | 15 | 130–220 days of cover → `OVERSTOCK` |
| Planned stockouts | 10 | Run out 3–6 days into the simulation → must be flagged early |
| Demand spikes | 3 | 3× demand over the last two weeks → tests velocity reacting to trends |
| Dirty rows | ~1.5% | Negative qty, unknown SKU, missing/malformed date, bad qty, duplicate key, missing price, future date |
| Missing lead time | 1 supplier | `Polar Frozen` has a blank lead time → default of 7 days |

Days with no sales have **no row** in `sales.csv`, just like a real POS export.
Day 3's velocity calculation must treat those missing days as zero.

The same seed always produces identical files, so it's fine to commit `data/`
(about 5 MB) or to regenerate it on any machine.

```powershell
git add data
git commit -m "Day 1: synthetic dataset (seed 42)"
```

---

## 6. Deploy the infrastructure

```powershell
az login
az account show --output table          # confirm the right subscription
az webapp list-runtimes --os linux --output table | Select-String "DOTNETCORE"
```

The last command should list `DOTNETCORE:10.0`. Then deploy:

```powershell
./scripts/deploy-infra.ps1
```

This creates `rg-reorder-advisor` in Central India with:

| Resource | Notes |
|---|---|
| Storage account | 5 containers (`raw`, `normalized`, `quarantine`, `recommendations`, `locks`), no public access, `raw/` deleted after 30 days |
| Log Analytics workspace | **0.1 GB/day cap**, 30-day retention |
| Application Insights | Workspace-based |
| App Service plan | **F1 (Free), Linux** |
| Web app | .NET 10, managed identity, HTTPS only, Always On off |
| Role assignment | Web app identity → Storage Blob Data Contributor |

If deployment fails with a quota or SKU error for F1 in Central India, pick one other
region and use it for everything: `./scripts/deploy-infra.ps1 -Location southindia`.

---

## 7. Deploy the app

```powershell
./scripts/deploy-app.ps1
```

The script publishes `Reorder.Web`, zip-deploys it, and polls `/health` until it answers.
The first request after a deploy on F1 can take 30–60 seconds (cold start).

Open the printed URL. You should see the placeholder page, and `/health` should return `Healthy`.

---

## 8. Verify the guardrails in the portal

- [ ] **Log Analytics workspace** `reorder-logs` → Usage and estimated costs → Daily cap shows 0.1 GB.
- [ ] **Storage account** → Data management → Lifecycle management shows `delete-raw-after-30-days`.
- [ ] **Storage account** → Containers shows the 5 containers, all private.
- [ ] **App Service plan** `reorder-plan` shows pricing tier **F1**.
- [ ] **Web app** → Identity → System assigned is **On**.
- [ ] Budget alert exists (step 1b).

---

## 9. Push to GitHub

Create an empty GitHub repository (no README or .gitignore), then:

```powershell
git remote add origin https://github.com/<you>/retail-reorder-advisor.git
git push -u origin main
```

---

## ✅ Day 1 done when

- [ ] Spending limit confirmed ON and ₹500 budget alert created
- [ ] `dotnet build` shows 0 warnings and `dotnet test` shows 7 passing tests
- [ ] `data/` contains 8 as-of folders and `ground-truth.json`
- [ ] The Azure URL returns the placeholder page and `/health` returns `Healthy`
- [ ] Code pushed to GitHub

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `A compatible .NET SDK was not found` | Install the .NET 10 SDK; `global.json` requires 10.0.100 or later |
| `...ps1 cannot be loaded... not digitally signed` | Run `Get-ChildItem -Recurse \| Unblock-File` in the repo folder (step 2) |
| `#Requires -Version 7.0` error | You're in Windows PowerShell 5.1; open `pwsh` instead |
| Bicep: `AlwaysOn cannot be set for this site as the plan does not allow it` | Make sure `alwaysOn: false` is still in `main.bicep` |
| Bicep: `The parameter LinuxFxVersion has an invalid value` | Run `az webapp list-runtimes --os linux` and use the exact .NET 10 value it lists |
| Bicep: authorization failed on the role assignment | Your account needs Owner (the default for a personal free account) |
| App shows "Application Error" | Portal → web app → **Log stream**; usually a startup exception |
| `/health` times out on the first call | F1 cold start; wait a minute and retry |

---

## Tomorrow (Day 2)

Core models, the pure `RowValidator` (test-first), CsvHelper parsing, the `BlobStore`,
and `POST /api/uploads` — with Azurite for local blob storage.
