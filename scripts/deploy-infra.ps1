#Requires -Version 7.0
<#
.SYNOPSIS
    Creates the resource group and deploys infra/main.bicep.
.EXAMPLE
    ./scripts/deploy-infra.ps1
    ./scripts/deploy-infra.ps1 -ResourceGroup rg-reorder-advisor -Location southindia
#>
param(
    [string] $ResourceGroup = 'rg-reorder-advisor',
    [string] $Location = 'centralindia'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$paramFile = Join-Path $repoRoot 'infra/main.bicepparam'

Write-Host "Creating resource group '$ResourceGroup' in '$Location'..."
az group create --name $ResourceGroup --location $Location --tags project=retail-reorder-advisor --output none
if ($LASTEXITCODE -ne 0) { throw 'az group create failed.' }

Write-Host 'Deploying infra/main.bicep (usually 2-4 minutes)...'
$outputsJson = az deployment group create `
    --resource-group $ResourceGroup `
    --name "reorder-$(Get-Date -Format 'yyyyMMdd-HHmmss')" `
    --parameters $paramFile `
    --query properties.outputs `
    --output json
if ($LASTEXITCODE -ne 0) { throw 'Bicep deployment failed. See the error above, or the Deployments blade of the resource group.' }

$outputs = ($outputsJson -join "`n") | ConvertFrom-Json

Write-Host ''
Write-Host 'Deployment complete.' -ForegroundColor Green
Write-Host "  Web app ........ $($outputs.webAppName.value)"
Write-Host "  URL ............ $($outputs.webAppUrl.value)"
Write-Host "  Storage ........ $($outputs.storageAccountName.value)"
Write-Host "  App Insights ... $($outputs.appInsightsName.value)"
Write-Host ''
Write-Host 'Next: ./scripts/deploy-app.ps1'
