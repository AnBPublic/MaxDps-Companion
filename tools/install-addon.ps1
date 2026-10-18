#requires -version 7
<#
.SYNOPSIS
    Copies the MaxDps Companion Bridge addon into a WoW Interface\AddOns
    folder.

.DESCRIPTION
    Installs the in-game-config bridge (addon/MaxDpsCompanionBridge). It never
    deletes, disables, moves, or edits the legacy MaxDpsBridge, MaxDps, or any
    other addon folder. The legacy bridge (archived in the repo under
    addon/legacy) is mutually exclusive at runtime; the player disables it
    per-character in the AddOns list.

.EXAMPLE
    pwsh -File tools/install-addon.ps1
    pwsh -File tools/install-addon.ps1 -AddOnsPath 'D:\WoW\_retail_\Interface\AddOns'
#>
[CmdletBinding()]
param(
    [string]$AddOnsPath = 'A:\Games\BattleNet\World of Warcraft\_retail_\Interface\AddOns'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'addon\MaxDpsCompanionBridge'
if (-not (Test-Path -LiteralPath $source)) { throw "Addon source not found at $source" }
if (-not (Test-Path -LiteralPath $AddOnsPath)) { throw "AddOns folder not found at $AddOnsPath" }

$target = Join-Path $AddOnsPath 'MaxDpsCompanionBridge'
if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType Directory -Path $target | Out-Null }

Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

$toc = Join-Path $target 'MaxDpsCompanionBridge.toc'
if (-not (Test-Path -LiteralPath $toc)) { throw "install produced no .toc at $toc" }

# Presence check only: never modify other addons.
$legacy = Join-Path $AddOnsPath 'MaxDpsBridge'
$legacyNote = if (Test-Path -LiteralPath $legacy) { 'present (left untouched)' } else { 'not installed' }

Write-Host "Installed MaxDpsCompanionBridge to $target"
Write-Host "Legacy MaxDpsBridge: $legacyNote."
Write-Host "Next steps:"
Write-Host "  1. In game, open the AddOns list and DISABLE 'MaxDpsBridge' per-character (if present)."
Write-Host "  2. ENABLE 'MaxDps Companion Bridge' for that character."
Write-Host "  3. /reload, then '/mcb help' to confirm."
Write-Host "Rollback: re-enable MaxDpsBridge, disable MaxDps Companion Bridge, then run dist-legacy\MaxDpsCompanion.exe."
