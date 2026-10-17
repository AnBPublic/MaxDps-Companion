#requires -version 7
<#
.SYNOPSIS
    Fixture tests for tools/Sync-CustomMaxDps.ps1 (T2).
.DESCRIPTION
    Self-contained (no Pester). Builds fake upstream drops + patch manifests in
    a temp workspace, invokes the sync tool as a child pwsh process, and asserts
    per-entry statuses, exit codes, and write side effects.

    Run:  pwsh tests/sync/Sync-CustomMaxDps.Tests.ps1
    Exit: 0 all pass, 1 any failure. -Keep leaves the temp workspace.
#>
[CmdletBinding()]
param([switch]$Keep)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$syncTool = Join-Path $repoRoot 'tools/Sync-CustomMaxDps.ps1'
if (-not (Test-Path -LiteralPath $syncTool)) { throw "sync tool not found: $syncTool" }

$work = Join-Path $env:TEMP ('maxdps-sync-tests-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work -Force | Out-Null

$script:failures = 0
$script:checks = 0
function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:checks++
    if ($Condition) { Write-Host "  PASS  $Message" }
    else { Write-Host "  FAIL  $Message" -ForegroundColor Red; $script:failures++ }
}

function Write-FixtureFile {
    param([string]$Path, [string]$Content)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Set-Content -LiteralPath $Path -Value $Content -NoNewline -Encoding utf8
}

function New-Fixture {
    param([string]$Name, [string]$RulesLua, [string]$PatchesJson)
    $root       = Join-Path $work $Name
    $upstream   = Join-Path $root 'upstream'
    $custom     = Join-Path $root 'custom'
    $patches    = Join-Path $root 'patches.json'
    $out        = Join-Path $custom 'out'
    $pristine   = Join-Path $custom 'upstream-pristine'
    $publish    = Join-Path $root 'AddOnsTest'
    Write-FixtureFile (Join-Path $upstream 'MaxDps_Fake/MaxDps_Fake.toc') @"
## Title: MaxDps_Fake
## Version: v9.9.9
## Interface: 120100
Main.lua
"@
    Write-FixtureFile (Join-Path $upstream 'MaxDps_Fake/Rules.lua') $RulesLua
    Write-FixtureFile $patches $PatchesJson
    New-Item -ItemType Directory -Path $custom   -Force | Out-Null
    New-Item -ItemType Directory -Path $pristine -Force | Out-Null
    New-Item -ItemType Directory -Path $publish  -Force | Out-Null
    return [pscustomobject]@{
        Root = $root; Upstream = $upstream; Custom = $custom; Patches = $patches
        Out = $out; Pristine = $pristine; Publish = $publish
    }
}

function Invoke-Sync {
    param([pscustomobject]$Fx, [switch]$WhatIf)
    $a = @(
        '-NoProfile', '-File', $syncTool,
        '-NewUpstream', $Fx.Upstream,
        '-PatchesPath', $Fx.Patches,
        '-CustomRoot',  $Fx.Custom,
        '-OutRoot',     $Fx.Out,
        '-PristineRoot', $Fx.Pristine,
        '-PublishTo',   $Fx.Publish
    )
    if ($WhatIf) { $a += '-WhatIf' }
    $text = (& pwsh @a 2>&1 | Out-String)
    return [pscustomobject]@{ Text = $text; ExitCode = $LASTEXITCODE }
}

$rulesClean = @"
local rules = {}
-- ALREADY_FIXED_TOKEN is handled upstream
-- insert-anchor
-- off-token mode
return rules
"@
$patchesClean = @'
{
  "patches": [
    { "id": "FIXED", "type": "guard", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "ALREADY_FIXED_TOKEN", "op": "insertEntry",
      "anchor": "ALREADY_FIXED_TOKEN", "insert": "-- SHOULD-NOT-APPEAR" },
    { "id": "APPLY-INSERT", "type": "data", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "NO_SUCH_FIXED_TOKEN", "op": "insertEntry",
      "anchor": "-- insert-anchor", "insert": "-- inserted-token", "canary": "inserted-token" },
    { "id": "APPLY-UNCOMMENT", "type": "data", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "NO_SUCH_FIXED_TOKEN_2", "op": "uncommentEntry",
      "find": "^-- off-token", "canary": "off-token" }
  ]
}
'@

$rulesConflict = @"
local rules = {}
-- ALREADY_FIXED_TOKEN present
-- insert-anchor
return rules
"@
$patchesConflict = @'
{
  "patches": [
    { "id": "FIXED", "type": "guard", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "ALREADY_FIXED_TOKEN", "op": "insertEntry",
      "anchor": "ALREADY_FIXED_TOKEN", "insert": "-- nope" },
    { "id": "APPLY-INSERT", "type": "data", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "NOPE", "op": "insertEntry",
      "anchor": "-- insert-anchor", "insert": "-- inserted-token", "canary": "inserted-token" },
    { "id": "CONFLICT-MISSING", "type": "guard", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "NOPE-2", "op": "replaceGuard",
      "find": "-- missing-anchor-xyz", "replace": "-- replaced" }
  ]
}
'@

$rulesAllFixed = @"
local rules = {}
-- ALREADY_FIXED_TOKEN present
return rules
"@
$patchesAllFixed = @'
{
  "patches": [
    { "id": "F1", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "ALREADY_FIXED_TOKEN", "op": "insertEntry",
      "anchor": "ALREADY_FIXED_TOKEN", "insert": "-- nope" },
    { "id": "F2", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "local rules", "op": "replaceGuard",
      "find": "local rules", "replace": "local rules" }
  ]
}
'@

$rulesAmbiguous = @"
local rules = {}
-- dup-anchor
-- dup-anchor
return rules
"@
$patchesAmbiguous = @'
{
  "patches": [
    { "id": "AMB", "target": "MaxDps_Fake/Rules.lua",
      "fixedWhen": "NOPE", "op": "insertEntry",
      "anchor": "-- dup-anchor", "insert": "-- inserted" }
  ]
}
'@

# --------------------------------------------------------------------------
Write-Host "`n[1] clean fixture: fixed + unfixed(insert,uncomment) => exit 0, writes out+publish"
$fx = New-Fixture -Name 'clean' -RulesLua $rulesClean -PatchesJson $patchesClean
$r = Invoke-Sync -Fx $fx
Assert-True ($r.ExitCode -eq 0) "exit 0 (got $($r.ExitCode))"
Assert-True ($r.Text -match 'FIXED-UPSTREAM FIXED') "status FIXED-UPSTREAM"
Assert-True ($r.Text -match 'APPLIED\s+APPLY-INSERT') "status APPLIED insert"
Assert-True ($r.Text -match 'APPLIED\s+APPLY-UNCOMMENT') "status APPLIED uncomment"
Assert-True ($r.Text -match 'Sync: APPLIED 2\s+CONFLICT 0') "summary APPLIED 2 CONFLICT 0"
Assert-True (Test-Path (Join-Path $fx.Out 'SYNC-REPORT.md')) "SYNC-REPORT.md written"
$outRules = Get-Content -LiteralPath (Join-Path $fx.Out 'MaxDps_Fake/Rules.lua') -Raw
Assert-True ($outRules -match '-- inserted-token') "insert op landed in out/"
Assert-True ($outRules -notmatch 'SHOULD-NOT-APPEAR') "fixed entry skipped in out/"
Assert-True ($outRules -match '(?m)^\s*off-token mode') "uncomment op landed in out/"
Assert-True (Test-Path (Join-Path $fx.Publish 'MaxDps_Fake/MaxDps_Fake.toc')) "published to AddOnsTest"

# --------------------------------------------------------------------------
Write-Host "`n[2] conflict fixture: fixed + unfixed + missing anchor => exit 1, no publish"
$fx2 = New-Fixture -Name 'conflict' -RulesLua $rulesConflict -PatchesJson $patchesConflict
$r2 = Invoke-Sync -Fx $fx2
Assert-True ($r2.ExitCode -ne 0) "non-zero exit (got $($r2.ExitCode))"
Assert-True ($r2.Text -match 'FIXED-UPSTREAM FIXED') "status FIXED-UPSTREAM"
Assert-True ($r2.Text -match 'APPLIED\s+APPLY-INSERT') "status APPLIED insert"
Assert-True ($r2.Text -match 'CONFLICT\s+CONFLICT-MISSING') "status CONFLICT"
Assert-True ($r2.Text -match 'Sync: APPLIED 1\s+CONFLICT 1') "summary APPLIED 1 CONFLICT 1"
Assert-True (-not (Test-Path (Join-Path $fx2.Publish 'MaxDps_Fake'))) "publish skipped on conflict"

# --------------------------------------------------------------------------
Write-Host "`n[3] all fixed => 'upstream covers all', writes nothing, exit 0"
$fx3 = New-Fixture -Name 'allfixed' -RulesLua $rulesAllFixed -PatchesJson $patchesAllFixed
$r3 = Invoke-Sync -Fx $fx3
Assert-True ($r3.ExitCode -eq 0) "exit 0 (got $($r3.ExitCode))"
Assert-True ($r3.Text -match 'upstream covers all') "prints 'upstream covers all'"
Assert-True ($r3.Text -match 'Sync: APPLIED 0\s+CONFLICT 0') "summary APPLIED 0 CONFLICT 0"
Assert-True (-not (Test-Path $fx3.Out)) "no out/ written"

# --------------------------------------------------------------------------
Write-Host "`n[4] ambiguous anchor => CONFLICT"
$fx4 = New-Fixture -Name 'ambiguous' -RulesLua $rulesAmbiguous -PatchesJson $patchesAmbiguous
$r4 = Invoke-Sync -Fx $fx4
Assert-True ($r4.ExitCode -ne 0) "non-zero exit (got $($r4.ExitCode))"
Assert-True ($r4.Text -match 'CONFLICT\s+AMB') "status CONFLICT ambiguous"
Assert-True ($r4.Text -match 'anchor ambiguous') "reason 'anchor ambiguous'"

# --------------------------------------------------------------------------
Write-Host "`n[5] -WhatIf: statuses reported, no writes"
$fx5 = New-Fixture -Name 'whatif' -RulesLua $rulesClean -PatchesJson $patchesClean
$r5 = Invoke-Sync -Fx $fx5 -WhatIf
Assert-True ($r5.ExitCode -eq 0) "exit 0 (got $($r5.ExitCode))"
Assert-True ($r5.Text -match 'Sync: APPLIED 2\s+CONFLICT 0') "summary computed under -WhatIf"
Assert-True (-not (Test-Path $fx5.Out)) "no out/ under -WhatIf"
Assert-True (-not (Test-Path (Join-Path $fx5.Publish 'MaxDps_Fake'))) "no publish under -WhatIf"

# --------------------------------------------------------------------------
# Real T1 schema (kind/addon/file/anchor.regex/apply.op/apply.text/fixedWhen.regex)
$rulesReal = @"
local rules = {}
-- ALREADY_FIXED_TOKEN present
-- scope-marker
local dup-anchor
local other
local dup-anchor
local off-token
if not ACS then
return rules
"@
$patchesReal = @'
{
  "patches": [
    { "id": "R-CANARY", "kind": "canary", "addon": "MaxDps_Fake", "file": "Rules.lua",
      "anchor": { "regex": "-- ALREADY_FIXED_TOKEN", "scope": null },
      "apply": { "op": "assert", "text": "-- ALREADY_FIXED_TOKEN" },
      "fixedWhen": { "regex": "-- ALREADY_FIXED_TOKEN" } },
    { "id": "R-DATA", "kind": "data", "addon": "MaxDps_Fake", "file": "Rules.lua",
      "anchor": { "regex": "local off-token", "scope": null },
      "apply": { "op": "replace", "text": "local off-token -- REAL-DATA" },
      "fixedWhen": { "regex": "REAL-DATA" } },
    { "id": "R-SCOPED", "kind": "data", "addon": "MaxDps_Fake", "file": "Rules.lua",
      "anchor": { "regex": "local dup-anchor", "scope": "-- scope-marker" },
      "apply": { "op": "replace", "text": "local dup-anchor -- REAL-SCOPED" },
      "fixedWhen": { "regex": "REAL-SCOPED" } },
    { "id": "R-GUARD", "kind": "guard", "addon": "MaxDps_Fake", "file": "Rules.lua",
      "anchor": { "regex": "if not ACS then", "scope": null },
      "apply": { "op": "replace", "text": "if true then -- REAL-GUARD" },
      "fixedWhen": { "regex": "REAL-GUARD" } }
  ]
}
'@
Write-Host "`n[6] real T1 schema: canary fixed + data/scoped/guard replace => exit 0"
$fx6 = New-Fixture -Name 'real' -RulesLua $rulesReal -PatchesJson $patchesReal
$r6 = Invoke-Sync -Fx $fx6
Assert-True ($r6.ExitCode -eq 0) "exit 0 (got $($r6.ExitCode))"
Assert-True ($r6.Text -match 'FIXED-UPSTREAM R-CANARY') "real canary FIXED-UPSTREAM"
Assert-True ($r6.Text -match 'APPLIED\s+R-DATA') "real data APPLIED"
Assert-True ($r6.Text -match 'APPLIED\s+R-SCOPED') "real scoped data APPLIED"
Assert-True ($r6.Text -match 'APPLIED\s+R-GUARD') "real guard APPLIED"
Assert-True ($r6.Text -match 'Sync: APPLIED 3\s+CONFLICT 0') "real summary APPLIED 3 CONFLICT 0"
$outReal = Get-Content -LiteralPath (Join-Path $fx6.Out 'MaxDps_Fake/Rules.lua') -Raw
Assert-True ($outReal -match 'local off-token -- REAL-DATA') "real data replace landed"
Assert-True ($outReal -match 'local dup-anchor -- REAL-SCOPED') "real scoped replace landed"
Assert-True (([regex]::Matches($outReal, 'REAL-SCOPED')).Count -eq 1) "scoped anchor hit exactly once"
Assert-True ($outReal -match 'if true then -- REAL-GUARD') "real guard replace landed"

# --------------------------------------------------------------------------
# Real schema conflicts: ambiguous scoped anchor + canary fires when sentinel
# is absent while GlowCooldown has appeared.
$rulesRealConflict = @"
local rules = {}
local amb
local amb
-- scope-A
local one
local two
-- scope-A
local one
MaxDps:GlowCooldownMidnight(1)
return rules
"@
$patchesRealConflict = @'
{
  "patches": [
    { "id": "R-AMB", "kind": "data", "addon": "MaxDps_Fake", "file": "Rules.lua",
      "anchor": { "regex": "local amb", "scope": null },
      "apply": { "op": "replace", "text": "local amb -- nope" },
      "fixedWhen": { "regex": "NEVER-FIXED" } },
    { "id": "R-SCOPE-AMB", "kind": "data", "addon": "MaxDps_Fake", "file": "Rules.lua",
      "anchor": { "regex": "local one", "scope": "-- scope-A" },
      "apply": { "op": "replace", "text": "local one -- nope" },
      "fixedWhen": { "regex": "NEVER-FIXED-2" } },
    { "id": "R-CANARY-FIRE", "kind": "canary", "addon": "MaxDps_Fake", "file": "Rules.lua",
      "anchor": { "regex": "local setSpell", "scope": null },
      "apply": { "op": "assert", "text": "local setSpell" },
      "fixedWhen": { "regex": "-- SENTINEL-ABSENT" } }
  ]
}
'@
Write-Host "`n[7] real schema: ambiguous anchor/scope + canary fire => exit 1"
$fx7 = New-Fixture -Name 'realconflict' -RulesLua $rulesRealConflict -PatchesJson $patchesRealConflict
$r7 = Invoke-Sync -Fx $fx7
Assert-True ($r7.ExitCode -ne 0) "non-zero exit (got $($r7.ExitCode))"
Assert-True ($r7.Text -match 'CONFLICT\s+R-AMB') "ambiguous unscoped anchor CONFLICT"
Assert-True ($r7.Text -match 'R-AMB\s+\S+\s+anchor ambiguous') "reason 'anchor ambiguous'"
Assert-True ($r7.Text -match 'CONFLICT\s+R-SCOPE-AMB') "ambiguous scoped anchor CONFLICT"
Assert-True ($r7.Text -match 'R-SCOPE-AMB\s+\S+\s+anchor ambiguous in scope') "reason 'anchor ambiguous in scope'"
Assert-True ($r7.Text -match 'CONFLICT\s+R-CANARY-FIRE') "canary CONFLICT"
Assert-True ($r7.Text -match 'GlowCooldown present') "canary reason mentions GlowCooldown present"
Assert-True ($r7.Text -match 'Sync: APPLIED 0\s+CONFLICT 3') "real conflict summary APPLIED 0 CONFLICT 3"

# --------------------------------------------------------------------------
Write-Host "`n$($script:checks - $script:failures)/$($script:checks) checks passed"
if (-not $Keep) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
else { Write-Host "workspace kept: $work" }
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
