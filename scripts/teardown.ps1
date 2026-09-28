#Requires -Version 7.0
<#
.SYNOPSIS
    Deletes the resource group and every resource in it. Use -Force in scripts/CI.
.EXAMPLE
    ./scripts/teardown.ps1
#>
param(
    [string] $ResourceGroup = 'rg-reorder-advisor',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

if (-not $Force) {
    $answer = Read-Host "This deletes '$ResourceGroup' and EVERYTHING in it. Type the resource group name to confirm"
    if ($answer -ne $ResourceGroup) {
        Write-Host 'Cancelled.'
        exit 1
    }
}

az group delete --name $ResourceGroup --yes --no-wait
if ($LASTEXITCODE -ne 0) { throw 'az group delete failed.' }

Write-Host "Deletion started. Check progress with: az group exists --name $ResourceGroup"
