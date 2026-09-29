# Retail Reorder Advisor

A nightly batch web app that tells a retail buyer, per SKU, **what to reorder today,
how much, and why**, and flags overstock and dead stock. It uses a transparent
reorder-point calculation instead of gut feel.

**Stack:** ASP.NET Core (.NET 10) · Azure App Service F1 · Blob Storage · Application Insights ·
Bicep · GitHub Actions. Designed to run at effectively zero cost on an Azure free account.

> 🚧 In progress: 10-day build. See [`docs/implementation-plan.md`](docs/implementation-plan.md)
> for the full plan and [`docs/day-09.md`](docs/day-09.md) for the CI/CD setup.

## Repository layout

```
src/Reorder.Core        Pure business logic (no Azure dependencies)
src/Reorder.Web         ASP.NET Core host: API, dashboard, jobs
src/Reorder.DataGen     Synthetic data generator and backtest
tests/                  xUnit test projects
infra/                  Bicep templates
scripts/                Deploy and teardown scripts (PowerShell 7)
docs/                   Plan and daily guides
```

## Quick start

```powershell
dotnet build
dotnet test
dotnet run --project src/Reorder.DataGen -- generate --out data
dotnet run --project src/Reorder.Web --launch-profile http   # http://localhost:5080
```

## Deploy and tear down

```powershell
az login
./scripts/deploy-infra.ps1
./scripts/deploy-app.ps1
./scripts/teardown.ps1        # deletes everything
```

## CI/CD

Three GitHub Actions workflows (setup in [`docs/day-09.md`](docs/day-09.md)):

- **CI** — restore, build (warnings as errors), test with coverage, and the backtest gate on every push and PR.
- **Deploy** — on push to `main`, signs in to Azure with OIDC (no stored secrets), runs the Bicep + app deploy, and smoke-tests `/health`.
- **Nightly** — a scheduled run at 02:00 IST triggers the advisor.

Two scheduler caveats: GitHub can start scheduled runs several minutes late, and it disables scheduled workflows after 60 days of repository inactivity.

## Progress

| Day | Focus | Status |
|---|---|---|
| 1 | Guardrails, skeleton, infrastructure, data generator | ✅ |
| 2 | Ingestion and validation | ✅ |
| 3 | Advisor core (test-first) | ✅ |
| 4 | Advisor job end to end | ✅ |
| 5 | Edge cases, telemetry, backtest | ✅ |
| 6 | Read API | ✅ |
| 7 | Action list page | ✅ |
| 8 | Health summary and webhook | ✅ |
| 9 | CI/CD and reproducibility drill | ✅ |
| 10 | Simulation, docs, demo | ⬜ |
