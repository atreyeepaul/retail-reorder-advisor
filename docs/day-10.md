# Day 10 — Simulation, documentation, demo

The final day: prove seven consecutive clean daily runs, finish the README, and record a short demo.

## 1. Seven-day simulation

[`scripts/simulate-7-days.ps1`](../scripts/simulate-7-days.ps1) replays the seven simulation days of the committed
seed-42 dataset (`2026-08-30` … `2026-09-05`; the eighth folder, `2026-08-29`, is the last *history* day and is
excluded). For each day it uploads the three CSVs, asserts the upload returned `status: "ok"`, then triggers
`run-advisor` and asserts a 200 — a "clean run". Each successful run also posts a Teams digest, so expect about
seven messages.

**Prerequisites:** the app is deployed, and the admin/job keys are available. It targets Azure by default
(resolving the URL from the resource group); pass `-BaseUrl`/`-AdminKey`/`-JobKey` to point elsewhere (e.g. a
local Azurite run).

```powershell
$env:REORDER_ADMIN_KEY = "<admin key>"
$env:REORDER_JOB_KEY   = "<job key>"
./scripts/simulate-7-days.ps1
```

Expected output:

```
Target: https://reorder-vmtiy7-web.azurewebsites.net
Warming up .../health ...

=== 2026-08-30 ===
  upload ok  (sales 14822 accepted / 222 rejected)
  advisor ok (200 SKUs; URGENT 36, STOCKOUT_RISK 16, DEAD_STOCK 5, OVERSTOCK 15, HEALTHY 128)
... (five more days) ...
=== 2026-09-05 ===
  upload ok  (sales 15778 accepted / 232 rejected)
  advisor ok (200 SKUs; URGENT 28, STOCKOUT_RISK 16, DEAD_STOCK 5, OVERSTOCK 15, HEALTHY 136)

7/7 clean runs.
```

Any non-clean day (upload `aborted`, or a 409/404/500 from `run-advisor`) throws and stops the script.

## 2. Three-minute demo outline

1. **Problem (20s)** — a buyer with ~200 SKUs guessing what to reorder; overstock ties up cash, stockouts lose
   sales.
2. **Upload (30s)** — `POST /api/uploads` a day's CSVs; show the validation report (accepted/rejected, quarantine).
3. **Run (20s)** — `POST /api/jobs/run-advisor`; note the blob-lease lock and the structured run summary.
4. **Action list (45s)** — the ranked table at `/`: filter to URGENT, sort by days of cover, read a reason,
   export the CSV.
5. **Health summary (25s)** — the tiles (at-risk, overstock ₹, dead-stock ₹, lost sales avoided) + the flag bar.
6. **Teams digest (20s)** — the Adaptive Card that posts after the run; mention the aborted-ingestion alert.
7. **Cost (20s)** — the Azure **Cost Analysis** screenshot for `rg-reorder-advisor` (≈ ₹0 on free tier).

The recording and the Cost Analysis screenshot are manual steps.

## 3. Definition of done

- [x] 200-SKU nightly run produces ranked recommendations with per-SKU reasons.
- [x] Every formula and flag rule unit-tested; the backtest reports ≥ 80% early-warning coverage.
- [x] Dashboards: action list (filter / sort / export CSV) and health summary (tiles + flag bar).
- [x] Teams digest after each run; alert variant when ingestion aborts.
- [x] Deployed on App Service F1; `/health` green; telemetry in Application Insights.
- [x] CI runs build + tests + backtest on every push/PR; nightly workflow triggers the run.
- [x] Reproducibility: the environment rebuilds from an empty resource group with the same URL.
- [x] Seven consecutive clean simulated daily runs.
- [x] Cost ≈ ₹0 on the free tier.
- [x] README documents architecture, formulas, design decisions, and limitations.
- [ ] OIDC `deploy.yml` — blocked on this tenant (needs an admin to grant the Application Developer role);
  documented, deploys run via the scripts.
- [ ] 3-minute demo recording + Cost Analysis screenshot (manual).
