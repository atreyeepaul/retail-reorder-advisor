# Day 9 — CI/CD and reproducibility

Three GitHub Actions workflows plus a from-scratch rebuild drill.

| Workflow | Trigger | What it does |
|---|---|---|
| [`ci.yml`](../.github/workflows/ci.yml) | push to `main`, PRs, manual | restore → build (warnings = errors) → test with coverage → backtest gate (≥ 80%) |
| [`deploy.yml`](../.github/workflows/deploy.yml) | push to `main`, manual | test → `azure/login` (OIDC) → `deploy-infra.ps1` (Bicep) → `deploy-app.ps1` → smoke-test `/health` |
| [`nightly.yml`](../.github/workflows/nightly.yml) | cron `30 20 * * *` (02:00 IST), manual | `POST /api/jobs/run-advisor` with `X-Job-Key`, retried for cold start |

`ci.yml` and `nightly.yml` need **no** Azure identity. Only `deploy.yml` uses OIDC.

## 1. One-time Azure setup for OIDC (`deploy.yml`)

`deploy.yml` signs in to Azure with a **federated credential** (no stored client secret). You create an Entra
app registration, add a federated credential trusting this repo, and grant it deploy rights. Run these once
(you need permission to create app registrations and to assign roles).

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

> Managed-tenant note: if app-registration creation or the subscription-scope Owner assignment is blocked by
> your organization, `deploy.yml` can't run — keep deploying with the manual scripts (`./scripts/deploy-infra.ps1`
> then `./scripts/deploy-app.ps1`). `ci.yml` and `nightly.yml` are unaffected.

## 2. GitHub repository secrets

Set these under **Settings → Secrets and variables → Actions**, or with the `gh` CLI:

```bash
gh secret set AZURE_CLIENT_ID       --body "<appId from step 1>"
gh secret set AZURE_TENANT_ID       --body "<tenantId>"
gh secret set AZURE_SUBSCRIPTION_ID --body "<subscriptionId>"
gh secret set JOB_KEY               --body "<your run-advisor key>"      # -> Security__JobKey (X-Job-Key)
gh secret set ADMIN_KEY             --body "<your uploads key>"          # -> Security__AdminKey (X-Admin-Key)
gh secret set WEBHOOK_URL           --body "<your Teams workflow URL>"   # -> Notifications__WebhookUrl
gh secret set APP_URL               --body "https://reorder-vmtiy7-web.azurewebsites.net"
```

`JOB_KEY` / `ADMIN_KEY` / `WEBHOOK_URL` are passed to Bicep (via `REORDER_*` env vars) and become the web app's
app settings on every deploy. `APP_URL` is used by the deploy smoke test and the nightly trigger.

## 3. Reproducibility drill (the Day 9 "done when")

Proves the whole environment rebuilds from an empty resource group. Resource names are derived from
`uniqueString(resourceGroup().id)`, so recreating the **same RG name** in the **same subscription** yields the
**same names and URL** — `reorder-vmtiy7-web` is stable.

```powershell
# 1. Tear the environment down
pwsh scripts/teardown.ps1 -Force            # az group delete --yes --no-wait

# 2. Rebuild from scratch via the pipeline
gh workflow run deploy.yml                   # or push to main; watch: gh run watch

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

## Scheduler caveats (worth knowing)

- GitHub can start scheduled (`cron`) runs **several minutes late** under load.
- GitHub **disables scheduled workflows after 60 days** of no repository activity — a commit re-enables them.
