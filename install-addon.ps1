<#
.SYNOPSIS
    Copies the MaxDpsBridge addon into a WoW Interface\AddOns folder.
.EXAMPLE
    .\install-addon.ps1
    .\install-addon.ps1 -AddOnsPath 'D:\WoW\_retail_\Interface\AddOns'
#>
[CmdletBinding()]
param(
    [string]$AddOnsPath = 'A:\Games\BattleNet\World of Warcraft\_retail_\Interface\AddOns'
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'addon\MaxDpsBridge'
if (-not (Test-Path $source)) { throw "Addon source not found at $source" }
if (-not (Test-Path $AddOnsPath)) { throw "AddOns folder not found at $AddOnsPath" }

$target = Join-Path $AddOnsPath 'MaxDpsBridge'
if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target | Out-Null }

Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

$toc = Join-Path $target 'MaxDpsBridge.toc'
if (-not (Test-Path $toc)) { throw "install produced no .toc at $toc" }

Write-Host "Installed MaxDpsBridge to $target"
Write-Host "In game: /reload, then '/mdb status' to confirm."
Write-Host "Never touches the upstream MaxDps* folders."
