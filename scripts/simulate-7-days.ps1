#Requires -Version 7.0
<#
.SYNOPSIS
    Replays the seven simulation days against a deployed Reorder Advisor: uploads each day's three
    CSVs and triggers the advisor for that as-of date, asserting a clean run for all seven.
    Each successful run posts a Teams digest, so expect about seven messages.
.EXAMPLE
    $env:REORDER_ADMIN_KEY = '...'; $env:REORDER_JOB_KEY = '...'
    ./scripts/simulate-7-days.ps1
.EXAMPLE
    ./scripts/simulate-7-days.ps1 -BaseUrl http://localhost:5080 -AdminKey dev -JobKey dev
#>
param(
    [string] $BaseUrl,
    [string] $ResourceGroup = 'rg-reorder-advisor',
    [string] $WebAppName,
    [string] $AdminKey = $env:REORDER_ADMIN_KEY,
    [string] $JobKey = $env:REORDER_JOB_KEY,
    [string] $DataDir
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $DataDir) { $DataDir = Join-Path $repoRoot 'data' }

# The seven simulation days of the committed seed-42 dataset. The eighth folder on disk,
# 2026-08-29, is the last history day (baseline) and is deliberately excluded.
$asOfDates = @(
    '2026-08-30', '2026-08-31', '2026-09-01', '2026-09-02',
    '2026-09-03', '2026-09-04', '2026-09-05'
)

if (-not $AdminKey -or -not $JobKey) {
    throw "Set the keys first: `$env:REORDER_ADMIN_KEY and `$env:REORDER_JOB_KEY (or pass -AdminKey/-JobKey). Empty keys return 401."
}

# Resolve the target URL from Azure when it is not given explicitly (mirrors deploy-app.ps1).
if (-not $BaseUrl) {
    if (-not $WebAppName) {
        $WebAppName = az webapp list --resource-group $ResourceGroup --query '[0].name' --output tsv
        if ($LASTEXITCODE -ne 0 -or -not $WebAppName) {
            throw "No web app found in '$ResourceGroup'. Pass -BaseUrl, or deploy the app first."
        }
    }
    $hostName = az webapp show --resource-group $ResourceGroup --name $WebAppName --query defaultHostName --output tsv
    if ($LASTEXITCODE -ne 0 -or -not $hostName) { throw "Could not resolve the web app host name." }
    $BaseUrl = "https://$hostName"
}
$BaseUrl = $BaseUrl.TrimEnd('/')

Write-Host "Target: $BaseUrl"
Write-Host "Data:   $DataDir"

# Warm up: the first request to an idle F1 app can take 30-60s, so wake it before day 1.
Write-Host "Warming up $BaseUrl/health ..."
$warm = $false
for ($i = 1; $i -le 10; $i++) {
    try {
        $r = Invoke-WebRequest -Uri "$BaseUrl/health" -TimeoutSec 90 -SkipHttpErrorCheck
        if ($r.StatusCode -eq 200) { $warm = $true; break }
        Write-Host "  attempt ${i}: HTTP $($r.StatusCode)"
    }
    catch { Write-Host "  attempt ${i}: $($_.Exception.Message)" }
    Start-Sleep -Seconds 10
}
if (-not $warm) { throw "The app never reported healthy at $BaseUrl/health." }

$adminHeaders = @{ 'X-Admin-Key' = $AdminKey }
$jobHeaders = @{ 'X-Job-Key' = $JobKey }
$clean = 0

foreach ($asOf in $asOfDates) {
    Write-Host ""
    Write-Host "=== $asOf ===" -ForegroundColor Cyan
    $dayDir = Join-Path $DataDir $asOf
    foreach ($f in 'sales.csv', 'inventory.csv', 'suppliers.csv') {
        $p = Join-Path $dayDir $f
        if (-not (Test-Path $p)) { throw "Missing data file: $p" }
    }

    # Upload always returns 200; a >20% reject aborts with status 'aborted', so check the body.
    $form = @{
        sales     = Get-Item (Join-Path $dayDir 'sales.csv')
        inventory = Get-Item (Join-Path $dayDir 'inventory.csv')
        suppliers = Get-Item (Join-Path $dayDir 'suppliers.csv')
    }
    $upload = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/uploads?asOf=$asOf" -Headers $adminHeaders -Form $form -TimeoutSec 120
    if ($upload.status -ne 'ok') {
        throw "Upload for $asOf was '$($upload.status)' (sales rejected $($upload.sales.rejected)/$($upload.sales.total), threshold $($upload.rejectThreshold))."
    }
    Write-Host ("  upload ok  (sales {0} accepted / {1} rejected)" -f $upload.sales.accepted, $upload.sales.rejected)

    # run-advisor returns 200 with the summary, or throws on 409/404/500.
    $summary = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/jobs/run-advisor?asOf=$asOf" -Headers $jobHeaders -TimeoutSec 120
    $c = $summary.countsByFlag
    Write-Host ("  advisor ok ({0} SKUs; URGENT {1}, STOCKOUT_RISK {2}, DEAD_STOCK {3}, OVERSTOCK {4}, HEALTHY {5})" -f `
            $summary.skuCount, $c.URGENT, $c.STOCKOUT_RISK, $c.DEAD_STOCK, $c.OVERSTOCK, $c.HEALTHY) -ForegroundColor Green
    $clean++
}

Write-Host ""
if ($clean -ne $asOfDates.Count) { throw "Simulation incomplete: $clean/$($asOfDates.Count)." }
Write-Host "$clean/$($asOfDates.Count) clean runs." -ForegroundColor Green
