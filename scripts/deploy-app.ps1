#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes Reorder.Web, zip-deploys it to the App Service, and waits for /health.
    Day 9 replaces this with the GitHub Actions pipeline.
.EXAMPLE
    ./scripts/deploy-app.ps1
#>
param(
    [string] $ResourceGroup = 'rg-reorder-advisor',
    [string] $WebAppName
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/Reorder.Web/Reorder.Web.csproj'
$publishDir = Join-Path $repoRoot 'publish/web'
$zipPath = Join-Path $repoRoot 'publish/web.zip'

if (-not $WebAppName) {
    $WebAppName = az webapp list --resource-group $ResourceGroup --query '[0].name' --output tsv
    if ($LASTEXITCODE -ne 0 -or -not $WebAppName) {
        throw "No web app found in '$ResourceGroup'. Run ./scripts/deploy-infra.ps1 first."
    }
}

Write-Host "Publishing Reorder.Web (Release)..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

dotnet publish $project --configuration Release --output $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# .NET's ZipFile writes forward-slash entry names, which Linux App Service needs.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath)

Write-Host "Deploying to '$WebAppName'..."
az webapp deploy --resource-group $ResourceGroup --name $WebAppName --src-path $zipPath --type zip --output none
if ($LASTEXITCODE -ne 0) { throw 'az webapp deploy failed.' }

$hostName = az webapp show --resource-group $ResourceGroup --name $WebAppName --query defaultHostName --output tsv
$url = "https://$hostName"

Write-Host "Waiting for $url/health (the first request on F1 can take 30-60 seconds)..."
for ($i = 1; $i -le 10; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "$url/health" -TimeoutSec 90 -SkipHttpErrorCheck
        if ($response.StatusCode -eq 200) {
            Write-Host "Healthy: $url" -ForegroundColor Green
            exit 0
        }
        Write-Host "  attempt ${i}: HTTP $($response.StatusCode)"
    }
    catch {
        Write-Host "  attempt ${i}: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds 10
}

throw "The app did not report healthy. Open the web app's 'Log stream' in the Azure portal to see startup errors."
