#requires -version 7
<#
.SYNOPSIS
    Copies ONLY the experimental MaxDpsBridgeExp addon into a WoW
    Interface\AddOns folder.

.DESCRIPTION
    Mirror of install-addon.ps1 for the in-game-config fork. It never
    deletes, disables, moves, or edits the stable MaxDpsBridge, MaxDps, or
    any other addon folder. Stable and Exp are mutually exclusive at runtime;
    the player disables the stable bridge per-character in the AddOns list.

.EXAMPLE
    pwsh -File tools/install-addon-exp.ps1
    pwsh -File tools/install-addon-exp.ps1 -AddOnsPath 'D:\WoW\_retail_\Interface\AddOns'
#>
[CmdletBinding()]
param(
    [string]$AddOnsPath = 'A:\Games\BattleNet\World of Warcraft\_retail_\Interface\AddOns'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'addon\MaxDpsBridgeExp'
if (-not (Test-Path -LiteralPath $source)) { throw "Addon source not found at $source" }
if (-not (Test-Path -LiteralPath $AddOnsPath)) { throw "AddOns folder not found at $AddOnsPath" }

$target = Join-Path $AddOnsPath 'MaxDpsBridgeExp'
if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType Directory -Path $target | Out-Null }

Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

$toc = Join-Path $target 'MaxDpsBridgeExp.toc'
if (-not (Test-Path -LiteralPath $toc)) { throw "install produced no .toc at $toc" }

# Presence check only: never modify the stable bridge.
$stable = Join-Path $AddOnsPath 'MaxDpsBridge'
$stableNote = if (Test-Path -LiteralPath $stable) { 'present (left untouched)' } else { 'not installed' }

Write-Host "Installed MaxDpsBridgeExp to $target"
Write-Host "Stable MaxDpsBridge: $stableNote."
Write-Host "Next steps:"
Write-Host "  1. In game, open the AddOns list and DISABLE 'MaxDpsBridge' per-character."
Write-Host "  2. ENABLE 'MaxDpsBridgeExp' for that character."
Write-Host "  3. /reload, then '/mdbx help' to confirm."
Write-Host "Rollback: re-enable MaxDpsBridge, disable MaxDpsBridgeExp, run dist\\."
