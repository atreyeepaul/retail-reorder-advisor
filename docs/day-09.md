# Day 9 — CI/CD and reproducibility

Three GitHub Actions workflows plus a from-scratch rebuild drill.

| Workflow | Trigger | What it does |
|---|---|---|
| [`ci.yml`](../.github/workflows/ci.yml) | push to `main`, PRs, manual | restore → build (warnings = errors) → test with coverage → backtest gate (≥ 80%) |
| [`deploy.yml`](../.github/workflows/deploy.yml) | push to `main`, manual | test → `dotnet publish` → deploy to App Service via **publish profile** → smoke-test `/health` (app code only) |
| [`nightly.yml`](../.github/workflows/nightly.yml) | cron `30 20 * * *` (02:00 IST), manual | `POST /api/jobs/run-advisor` with `X-Job-Key`, retried for cold start |

None of the three needs Azure AD / OIDC. `deploy.yml` authenticates with the App Service **publish profile** (a
per-app credential), so it works on this tenant even though app registration is blocked.

## 1. Deploy on push — publish profile (used here)

`deploy.yml` builds, tests, publishes, and deploys to the web app using its **publish profile** — a per-app
credential, no Azure AD identity — so it works on this tenant even though app registration is blocked. Two
one-time prerequisites:

1. **Enable SCM basic auth** on the web app: Portal → the app → *Settings → Configuration → General settings →
   SCM Basic Auth Publishing Credentials = On*. CLI: `az resource update -g rg-reorder-advisor --namespace
   Microsoft.Web --resource-type basicPublishingCredentialsPolicies --name scm --parent sites/reorder-vmtiy7-web
   --set properties.allow=true`.
2. **Add the publish-profile secret:** Portal → the app → *Download publish profile* → paste the entire XML into a
   GitHub secret named **`AZURE_WEBAPP_PUBLISH_PROFILE`**. CLI to fetch it: `az webapp deployment
   list-publishing-profiles -g rg-reorder-advisor -n reorder-vmtiy7-web --xml`.

App code only — infra (Bicep) changes are rare and still applied with `./scripts/deploy-infra.ps1` (Owner).

### Alternative: OIDC (only if app registration is permitted)

The original design used GitHub **OIDC** federated login. On this tenant `az ad app create` returns *Insufficient
privileges*, so the publish profile above is used instead. If an admin grants your account the **Application
Developer** Entra role, you can switch `deploy.yml` back to OIDC with these one-time commands + the five
`AZURE_*` secrets:

```bash
# Identifiers you'll need
SUB_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
REPO="atreyeepaul/retail-reorder-advisor"

# 1. App registration + service principal
APP_ID=$(az ad app create --display-name "reorder-advisor-cicd" --query appId -o tsv)
az ad sp create --id "$APP_ID"

# 2. Federated credential for pushes/dispatches on main
az ad app federated-credential create --id "$APP_ID" --parameters "{
  \"name\": \"gh-main\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:${REPO}:ref:refs/heads/main\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"
# (Optional) add another credential with subject "repo:${REPO}:pull_request" if you ever deploy from a PR.

# 3. Deploy rights. Owner is required because Bicep creates a role assignment
#    (grants the web app's managed identity Storage Blob Data Contributor). Contributor alone cannot.
#    Subscription scope (not RG) so the reproducibility drill can recreate the deleted resource group.
az role assignment create --assignee "$APP_ID" --role Owner --scope "/subscriptions/${SUB_ID}"

echo "AZURE_CLIENT_ID=$APP_ID"; echo "AZURE_TENANT_ID=$TENANT_ID"; echo "AZURE_SUBSCRIPTION_ID=$SUB_ID"
```

> These OIDC commands need permission to create an app registration and assign a subscription role — **not
> available on this tenant** (`az ad app create` → *Insufficient privileges*). The publish-profile deploy above is
> what runs here. If a hands-on deploy is ever needed (or to (re)deploy infra), see §4 below.

## 2. GitHub repository secrets

Set these under **Settings → Secrets and variables → Actions**. For the **publish-profile deploy + nightly** (what
runs here) you need just three:

| Secret | Used by | Value |
|---|---|---|
| `AZURE_WEBAPP_PUBLISH_PROFILE` | `deploy.yml` | the app's downloaded publish-profile XML (whole file) |
| `JOB_KEY` | `nightly.yml` | the `Security__JobKey` value (sent as `X-Job-Key`) |
| `APP_URL` | `nightly.yml` | `https://reorder-vmtiy7-web.azurewebsites.net` |

Only if you switch to the **OIDC alternative** do you also add `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID` (and, if that pipeline also runs Bicep, `ADMIN_KEY` / `WEBHOOK_URL`, passed to Bicep as
`REORDER_*`).

## 3. Reproducibility drill (the Day 9 "done when")

Proves the whole environment rebuilds from an empty resource group. Resource names are derived from
`uniqueString(resourceGroup().id)`, so recreating the **same RG name** in the **same subscription** yields the
**same names and URL** — `reorder-vmtiy7-web` is stable.

```powershell
# 1. Tear the environment down
pwsh scripts/teardown.ps1 -Force            # az group delete --yes --no-wait

# 2. Rebuild infra + app from scratch with the scripts (deploy.yml is app-only — see §4)
$env:REORDER_JOB_KEY = "<job key>"; $env:REORDER_ADMIN_KEY = "<admin key>"; $env:REORDER_WEBHOOK_URL = "<webhook>"
./scripts/deploy-infra.ps1
./scripts/deploy-app.ps1

# 3. Re-upload a day of data (PowerShell 7 multipart)
$asOf = "2026-09-05"
$form = @{
  sales     = Get-Item "data/$asOf/sales.csv"
  inventory = Get-Item "data/$asOf/inventory.csv"
  suppliers = Get-Item "data/$asOf/suppliers.csv"
}
Invoke-RestMethod -Method Post `
  -Uri "https://reorder-vmtiy7-web.azurewebsites.net/api/uploads?asOf=$asOf" `
  -Headers @{ "X-Admin-Key" = "<your uploads key>" } -Form $form

# 4. Trigger a run (or run the nightly workflow)
gh workflow run nightly.yml
```

Then confirm `/health` returns 200, the dashboard shows the run, and a digest posts to Teams. Capture the
**Cost Analysis** screenshot for the resource group while you're in the portal.

> A from-empty rebuild recreates the web app, so its **publish profile changes** — re-download it and update the
> `AZURE_WEBAPP_PUBLISH_PROFILE` secret before relying on `deploy.yml` again.

## 4. Manual deploy & infra (by hand)

`deploy.yml` deploys app code automatically (publish profile). Use the scripts by hand when you need to
**(re)deploy infra** (Bicep / app settings), run the **reproducibility drill** (teardown deletes the whole group),
or deploy if the publish-profile path is ever unavailable. Needs `az login` (Owner on the subscription) and
PowerShell 7.

```powershell
# Deploy secrets -> env vars (Bicep reads these; use the values already on the app).
$env:REORDER_JOB_KEY     = "<job key>"
$env:REORDER_ADMIN_KEY   = "<admin key>"
$env:REORDER_WEBHOOK_URL = "<teams workflow url>"

az login
./scripts/deploy-infra.ps1     # az group create + Bicep (sets all app settings)
./scripts/deploy-app.ps1       # publish, zip-deploy, smoke-test /health
```

**Manual reproducibility drill** (the Day 9 "done when", by hand):

```powershell
# 1. Tear down and wait for the delete to finish
./scripts/teardown.ps1 -Force
az group wait --deleted --resource-group rg-reorder-advisor

# 2. Rebuild from empty (env vars from above still set)
./scripts/deploy-infra.ps1
./scripts/deploy-app.ps1

# 3. Re-upload a day and run the advisor
$asOf = "2026-09-05"
$form = @{ sales = Get-Item "data/$asOf/sales.csv"; inventory = Get-Item "data/$asOf/inventory.csv"; suppliers = Get-Item "data/$asOf/suppliers.csv" }
$base = "https://reorder-vmtiy7-web.azurewebsites.net"
Invoke-RestMethod -Method Post -Uri "$base/api/uploads?asOf=$asOf" -Headers @{ "X-Admin-Key" = $env:REORDER_ADMIN_KEY } -Form $form
Invoke-RestMethod -Method Post -Uri "$base/api/jobs/run-advisor?asOf=$asOf" -Headers @{ "X-Job-Key" = $env:REORDER_JOB_KEY }
```

Because resource names come from `uniqueString(resourceGroup().id)`, the rebuilt environment keeps the **same names
and URL**. Confirm `/health` = 200, the dashboard shows the run, and a digest posts to Teams; grab the Cost
Analysis screenshot.

## Scheduler caveats (worth knowing)

- GitHub can start scheduled (`cron`) runs **several minutes late** under load.
- GitHub **disables scheduled workflows after 60 days** of no repository activity — a commit re-enables them.
