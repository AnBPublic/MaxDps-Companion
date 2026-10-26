#requires -Version 7.0
<#
.SYNOPSIS
    Applies the MaxDps Companion compatibility hotfix to GW2_UI's taint paths.

.DESCRIPTION
    GW2_UI 12.0.0 installs `OverrideActionBar:HookScript("OnShow"/"OnHide", ...)`
    from `Core/API/utils.lua` and calls `ActionButton_UpdateCooldown(button)` from
    its extra-action-bar `AddFrame` hook. Both run under GW2_UI's taint; in
    combat the protected hide is blocked and the cooldown call poisons the
    button, so Blizzard's own `ActionButton_UpdateCooldown -> SetCooldown` starts
    rejecting secret cooldowns thousands of times per session.

    This tool comments those two paths out, backs the original files up once as
    `*.maxdps-backup-4.5.4`, and is idempotent (a second run is a no-op). It only
    touches files under `<AddOnsPath>\GW2_UI`; no other addon is modified.

    If GW2_UI is updated and the patterns return, run it again. `-Revert`
    restores the backups.

.PARAMETER AddOnsPath
    Retail Interface\AddOns folder. Defaults to the local retail install.

.PARAMETER Revert
    Restore the `*.maxdps-backup-4.5.4` backups instead of patching.

.EXAMPLE
    pwsh -File tools/patch-gw2ui-taint.ps1

.EXAMPLE
    pwsh -File tools/patch-gw2ui-taint.ps1 -AddOnsPath 'D:\WoW\_retail_\Interface\AddOns'
#>
[CmdletBinding()]
param(
    [string]$AddOnsPath = 'A:\Games\BattleNet\World of Warcraft\_retail_\Interface\AddOns',
    [switch]$Revert
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) { Write-Host "gw2ui-taint: $Message" }

$gwRoot = Join-Path $AddOnsPath 'GW2_UI'
if (-not (Test-Path -LiteralPath $gwRoot)) {
    throw "GW2_UI not found at $gwRoot"
}

$marker = 'MaxDps Companion taint hotfix'

$targets = @(
    @{
        Rel = 'Core\API\utils.lua'
        Old = '(?m)^    if stateType ~= "petbattle" then\r?\n        OverrideActionBar:HookScript\("OnShow", f_OnShow\)\r?\n        OverrideActionBar:HookScript\("OnHide", f_OnHide\)\r?\n    end'
        New = @'
    if stateType ~= "petbattle" then
        -- MaxDps Companion taint hotfix (2026-10-26): these hooks run under
        -- GW2_UI's taint and call Hide()/Show() on the protected OverrideActionBar
        -- in combat. The blocked call taints every OverrideActionBarButton, which
        -- makes Blizzard's own ActionButton_UpdateCooldown -> SetCooldown reject
        -- secret cooldowns thousands of times per session. The cosmetic pet-battle
        -- overlap this hide avoided is not worth poisoning the bars.
        -- Backup: utils.lua.maxdps-backup-4.5.4.
        -- OverrideActionBar:HookScript("OnShow", f_OnShow)
        -- OverrideActionBar:HookScript("OnHide", f_OnHide)
    end
'@
    },
    @{
        Rel = 'Games\Modern\Actionbar\extraAB_bossAB.lua'
        Old = '(?m)^            local cooldown = name and _G\[name \.\. "Cooldown"\]\r?\n            if cooldown then\r?\n                cooldown:GwSetInside\(\)\r?\n                cooldown:SetDrawEdge\(false\)\r?\n                cooldown:SetSwipeColor\(0, 0, 0, 1\)\r?\n                --GW\.RegisterCooldown\(cooldown\) --secret\r?\n            end\r?\n\r?\n            ActionButton_UpdateCooldown\(button\)\r?\n'
        New = @'
            -- MaxDps Companion taint hotfix (2026-10-26): do not call
            -- ActionButton_UpdateCooldown or poke the cooldown frame from this
            -- hook. It runs under GW2_UI's taint, and Blizzard's SetCooldown
            -- rejects secret cooldowns on a tainted frame. Leave the stock
            -- cooldown widget to Blizzard's own secure update.
            -- Backup: extraAB_bossAB.lua.maxdps-backup-4.5.4.
            -- local cooldown = name and _G[name .. "Cooldown"]
            -- if cooldown then
            --     cooldown:GwSetInside()
            --     cooldown:SetDrawEdge(false)
            --     cooldown:SetSwipeColor(0, 0, 0, 1)
            -- end
            -- ActionButton_UpdateCooldown(button)
'@
    }
)

$changed = 0
foreach ($target in $targets) {
    $path = Join-Path $gwRoot $target.Rel
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Step "skip (missing): $($target.Rel)"
        continue
    }

    $backup = "$path.maxdps-backup-4.5.4"
    $text = [System.IO.File]::ReadAllText($path)

    if ($Revert) {
        if (Test-Path -LiteralPath $backup) {
            Copy-Item -LiteralPath $backup -Destination $path -Force
            Write-Step "reverted: $($target.Rel)"
            $changed++
        } else {
            Write-Step "no backup, cannot revert: $($target.Rel)"
        }
        continue
    }

    if ($text.Contains($marker)) {
        Write-Step "already patched: $($target.Rel)"
        continue
    }

    $newText = [regex]::Replace($text, $target.Old, $target.New)
    if ($newText -eq $text) {
        Write-Step "pattern not found (GW2_UI changed?): $($target.Rel)"
        continue
    }

    if (-not (Test-Path -LiteralPath $backup)) {
        Copy-Item -LiteralPath $path -Destination $backup -Force
    }
    [System.IO.File]::WriteAllText($path, $newText, [System.Text.UTF8Encoding]::new($false))
    Write-Step "patched: $($target.Rel)"
    $changed++
}

if ($changed -gt 0 -and -not $Revert) {
    $luac = Get-Command luac -ErrorAction SilentlyContinue
    if ($luac) {
        foreach ($target in $targets) {
            $path = Join-Path $gwRoot $target.Rel
            if (Test-Path -LiteralPath $path) {
                & $luac.Source -p $path
                if ($LASTEXITCODE -ne 0) { throw "luac parse failed: $($target.Rel)" }
            }
        }
        Write-Step 'luac: parse OK'
    }
    Write-Step 'reload the WoW UI (/reload) so GW2_UI loads the patched files.'
} elseif ($changed -eq 0) {
    Write-Step 'nothing to do.'
}
