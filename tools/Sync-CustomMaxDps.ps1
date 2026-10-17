#requires -version 7
<#
.SYNOPSIS
    Sync the custom MaxDps fork (T2): diff a new upstream drop against the
    pristine baseline, apply declarative patches from custom/patches.json into
    custom/out/, write a SYNC-REPORT.md, and optionally publish to AddOns.

.DESCRIPTION
    Flow (docs/plans/2026-10-03-custom-maxdps-fork.md, T2 scope):
      1. Read TOC versions + hash NewUpstream files, diff vs
         custom/upstream-pristine/MANIFEST.json, report churn.
      2. Copy NewUpstream -> custom/out/ fresh (unless dry/WhatIf).
      3. For each custom/patches.json entry run its `fixedWhen` regex against
         the NEW upstream. A match means upstream already covers it
         (FIXED-UPSTREAM, skipped). Otherwise apply the op
         (uncommentEntry | insertEntry | replaceGuard). A missing or ambiguous
         anchor is a CONFLICT.
      4. If every entry is FIXED-UPSTREAM: print "upstream covers all" and
         write nothing.
      5. Write custom/out/SYNC-REPORT.md with the status counts.
      6. Publish to -PublishTo only when there are zero CONFLICTs, backing up
         any existing target folder to custom/out/_backup/<timestamp>/.

    NewUpstream is ALWAYS read-only (it may be the live AddOns tree or a fresh
    drop). vendor/ is never written. Exit code 1 when any CONFLICT remains.

.PARAMETER NewUpstream
    Directory containing the new upstream MaxDps* folders (read-only).

.PARAMETER PublishTo
    WoW Interface\AddOns folder to publish the patched tree into. Default
    mirrors install-addon.ps1's convention.

.PARAMETER WhatIf
    Compute and report statuses without copying, writing the report, or
    publishing.

.PARAMETER RecapturePristine
    Copy NewUpstream into custom/upstream-pristine/ and rewrite MANIFEST.json
    before syncing (resets the churn baseline).

.PARAMETER CustomRoot
    Override the custom/ root (tests). Default <repo>/custom.

.PARAMETER PatchesPath
    Override custom/patches.json (tests). Default <CustomRoot>/patches.json.

.PARAMETER OutRoot
    Override custom/out (tests). Default <CustomRoot>/out.

.PARAMETER PristineRoot
    Override custom/upstream-pristine (tests). Default <CustomRoot>/upstream-pristine.

.EXAMPLE
    pwsh tools/Sync-CustomMaxDps.ps1 -NewUpstream vendor -PublishTo $env:TEMP\AddOnsTest -WhatIf
    pwsh tools/Sync-CustomMaxDps.ps1 -NewUpstream 'D:\drop\MaxDps*'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$NewUpstream,

    [string]$PublishTo = 'A:\Games\BattleNet\World of Warcraft\_retail_\Interface\AddOns',

    [switch]$WhatIf,

    [switch]$RecapturePristine,

    # --- testability overrides (not part of the product surface) ---
    [string]$CustomRoot,
    [string]$PatchesPath,
    [string]$OutRoot,
    [string]$PristineRoot
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Paths + guards
# ---------------------------------------------------------------------------
$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptRoot

if (-not $CustomRoot)   { $CustomRoot   = Join-Path $repoRoot 'custom' }
if (-not $PatchesPath)  { $PatchesPath  = Join-Path $CustomRoot 'patches.json' }
if (-not $OutRoot)      { $OutRoot      = Join-Path $CustomRoot 'out' }
if (-not $PristineRoot) { $PristineRoot = Join-Path $CustomRoot 'upstream-pristine' }
$pristineManifest = Join-Path $PristineRoot 'MANIFEST.json'

if (-not (Test-Path -LiteralPath $NewUpstream -PathType Container)) {
    throw "NewUpstream not found or not a directory: $NewUpstream"
}
$NewUpstream = (Resolve-Path -LiteralPath $NewUpstream).Path

function Assert-OutsideSource {
    param([string]$Path, [string]$Label)
    $full = [System.IO.Path]::GetFullPath($Path)
    $sep = [System.IO.Path]::DirectorySeparatorChar
    if ($full.Equals($NewUpstream, [StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($NewUpstream + $sep, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label resolves inside NewUpstream (read-only, never written): $full"
    }
}
Assert-OutsideSource -Path $OutRoot      -Label 'OutRoot'
Assert-OutsideSource -Path $PristineRoot -Label 'PristineRoot'
Assert-OutsideSource -Path $PublishTo    -Label 'PublishTo'

# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------
function Get-RelKey {
    param([string]$Root, [string]$FullName)
    return ($FullName.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/')
}

function Get-FileMap {
    param([string]$Root)
    $map = @{}
    Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
        $key = Get-RelKey -Root $Root -FullName $_.FullName
        $map[$key] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $map
}

function Get-TocInfo {
    param([string]$FolderPath, [string]$FolderName)
    $toc = Join-Path $FolderPath ($FolderName + '.toc')
    if (-not (Test-Path -LiteralPath $toc)) {
        $toc = (Get-ChildItem -LiteralPath $FolderPath -Filter '*.toc' -File -ErrorAction SilentlyContinue |
                Select-Object -First 1).FullName
    }
    $info = [ordered]@{ version = $null; interface = $null; toc = $null }
    if (-not $toc -or -not (Test-Path -LiteralPath $toc)) { return $info }
    $info.toc = $toc
    foreach ($line in Get-Content -LiteralPath $toc) {
        if ($line -match '^##\s*Version:\s*(.+?)\s*$')   { $info.version = $Matches[1] }
        if ($line -match '^##\s*Interface:\s*(\d+)')      { $info.interface = [int]$Matches[1] }
    }
    return $info
}

function Get-TopLevelFolders {
    param([string]$Root)
    return @(Get-ChildItem -LiteralPath $Root -Directory | Where-Object { $_.Name -ne '_backup' })
}

function Get-Manifest {
    param([string]$Root, [string]$Source)
    $files = [ordered]@{}
    $versions = [ordered]@{}
    $interfaces = [ordered]@{}
    Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
        $key = Get-RelKey -Root $Root -FullName $_.FullName
        $files[$key] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    foreach ($folder in Get-TopLevelFolders -Root $Root) {
        $toc = Get-TocInfo -FolderPath $folder.FullName -FolderName $folder.Name
        $versions[$folder.Name] = $toc.version
        $interfaces[$folder.Name] = $toc.interface
    }
    return [ordered]@{
        capturedAt = (Get-Date -Format o)
        source     = $Source
        versions   = $versions
        interfaces = $interfaces
        files      = $files
    }
}

function Read-Manifest {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $files = @{}
    # Real T1 manifest uses `fileSha256`; earlier stub used `files`.
    $filesProp = if ($json.PSObject.Properties.Name -contains 'files') { 'files' }
                 elseif ($json.PSObject.Properties.Name -contains 'fileSha256') { 'fileSha256' }
                 else { $null }
    if ($filesProp) {
        foreach ($p in $json.$filesProp.PSObject.Properties) { $files[$p.Name] = [string]$p.Value }
    }
    else {
        # tolerant fallback: flat { "path": "hash" }
        foreach ($p in $json.PSObject.Properties) {
            if ($p.Value -is [string]) { $files[$p.Name] = [string]$p.Value }
        }
    }
    $versions = if ($json.PSObject.Properties.Name -contains 'versions') { $json.versions }
                elseif ($json.PSObject.Properties.Name -contains 'tocVersions') { $json.tocVersions } else { $null }
    $interfaces = if ($json.PSObject.Properties.Name -contains 'interfaces') { $json.interfaces }
                  elseif ($json.PSObject.Properties.Name -contains 'tocInterfaces') { $json.tocInterfaces } else { $null }
    return [pscustomobject]@{
        raw        = $json
        files      = $files
        versions   = $versions
        interfaces = $interfaces
    }
}

# Preserve the dominant line ending when rewriting a file line-by-line.
function Split-TextLines {
    param([string]$Text, [ref]$Newline)
    $nl = if ($Text -match "`r`n") { "`r`n" } else { "`n" }
    $Newline.Value = $nl
    return [System.Text.RegularExpressions.Regex]::Split($Text, [System.Text.RegularExpressions.Regex]::Escape($nl))
}
function Join-TextLines {
    param([string[]]$Lines, [string]$Newline)
    return ($Lines -join $Newline)
}

# ---------------------------------------------------------------------------
# Recapture pristine (optional)
# ---------------------------------------------------------------------------
if ($RecapturePristine) {
    $manifestObj = Get-Manifest -Root $NewUpstream -Source $NewUpstream
    if ($WhatIf) {
        Write-Host "[WhatIf] would recapture pristine from $NewUpstream -> $PristineRoot"
    }
    else {
        if (Test-Path -LiteralPath $PristineRoot) { Remove-Item -LiteralPath $PristineRoot -Recurse -Force }
        New-Item -ItemType Directory -Path $PristineRoot -Force | Out-Null
        Copy-Item -Path (Join-Path $NewUpstream '*') -Destination $PristineRoot -Recurse -Force
        $manifestObj | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $pristineManifest -Encoding utf8
        Write-Host "Recaptured pristine: $PristineRoot ($($manifestObj.files.Count) files)"
    }
}

# ---------------------------------------------------------------------------
# Step 1 - versions, hashes, churn
# ---------------------------------------------------------------------------
Write-Host "NewUpstream: $NewUpstream"
$newFolders = Get-TopLevelFolders -Root $NewUpstream
$newVersions = [ordered]@{}
$newInterfaces = [ordered]@{}
foreach ($folder in $newFolders) {
    $toc = Get-TocInfo -FolderPath $folder.FullName -FolderName $folder.Name
    $newVersions[$folder.Name] = $toc.version
    $newInterfaces[$folder.Name] = $toc.interface
    Write-Host ("  {0,-26} version={1,-10} interface={2}" -f $folder.Name, $toc.version, $toc.interface)
}

$newFiles = Get-FileMap -Root $NewUpstream
$manifest = Read-Manifest -Path $pristineManifest
$added = @(); $changed = @(); $removed = @()
if ($null -eq $manifest) {
    Write-Warning "No pristine manifest at $pristineManifest; churn unavailable (run -RecapturePristine)."
}
else {
    foreach ($k in $newFiles.Keys) {
        if (-not $manifest.files.ContainsKey($k)) { $added += $k }
        elseif ($manifest.files[$k] -ne $newFiles[$k]) { $changed += $k }
    }
    foreach ($k in $manifest.files.Keys) {
        if (-not $newFiles.ContainsKey($k)) { $removed += $k }
    }
    Write-Host ("Churn vs pristine: {0} added, {1} changed, {2} removed" -f $added.Count, $changed.Count, $removed.Count)

    # Interface drift warning
    if ($manifest.interfaces) {
        foreach ($name in $newInterfaces.Keys) {
            $old = $manifest.interfaces.PSObject.Properties[$name]
            $oldV = if ($old) { [string]$old.Value } else { $null }
            $newV = if ($null -ne $newInterfaces[$name]) { [string]$newInterfaces[$name] } else { $null }
            # Multi-interface TOCs (e.g. "120007, 120100") are not comparable as ints.
            if ($oldV -match '^\d+$' -and $newV -match '^\d+$' -and [int]$oldV -ne [int]$newV) {
                Write-Warning ("Interface changed for {0}: pristine {1} -> new {2}" -f $name, $oldV, $newV)
            }
        }
    }
}

# ---------------------------------------------------------------------------
# Load patches
# ---------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $PatchesPath)) {
    throw "patches manifest not found: $PatchesPath"
}
$patchDoc = Get-Content -LiteralPath $PatchesPath -Raw | ConvertFrom-Json
$entries = @()
if ($patchDoc -is [System.Array]) { $entries = @($patchDoc) }
elseif ($patchDoc.PSObject.Properties.Name -contains 'patches') { $entries = @($patchDoc.patches) }
elseif ($patchDoc.PSObject.Properties.Name -contains 'entries') { $entries = @($patchDoc.entries) }
else { $entries = @($patchDoc) }
$entries = @($entries | Where-Object { $_ -and $_.id })
Write-Host "Patches: $($entries.Count) entr$(if ($entries.Count -eq 1) {'y'} else {'ies'}) from $PatchesPath"

# Original (NewUpstream) + working content caches, keyed by lower-cased target.
$origCache = @{}
$workCache = @{}
function Get-OrigContent {
    param([string]$Target)
    $key = $Target.ToLowerInvariant()
    if (-not $origCache.ContainsKey($key)) {
        $p = Join-Path $NewUpstream ($Target -replace '/', '\')
        $origCache[$key] = if (Test-Path -LiteralPath $p) { Get-Content -LiteralPath $p -Raw } else { $null }
    }
    return $origCache[$key]
}
function Get-WorkContent {
    param([string]$Target)
    $key = $Target.ToLowerInvariant()
    if (-not $workCache.ContainsKey($key)) { $workCache[$key] = Get-OrigContent -Target $Target }
    return $workCache[$key]
}
function Set-WorkContent {
    param([string]$Target, [string]$Content)
    $workCache[$Target.ToLowerInvariant()] = $Content
}

function Invoke-UncommentEntry {
    param([string]$Content, $Entry)
    $pattern = if ($Entry.find) { $Entry.find } elseif ($Entry.anchor) { $Entry.anchor } else { $null }
    if (-not $pattern) { return @{ ok = $false; detail = 'missing find/anchor' } }
    $nl = ''
    $lines = @(Split-TextLines -Text $Content -Newline ([ref]$nl))
    $idx = @()
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $pattern) { $idx += $i } }
    if ($idx.Count -eq 0) { return @{ ok = $false; detail = 'anchor missing' } }
    if ($idx.Count -gt 1) { return @{ ok = $false; detail = 'anchor ambiguous' } }
    $line = $lines[$idx[0]]
    $new = [regex]::Replace($line, '^(\s*)--\s?', '$1', 1)
    if ($new -eq $line) { return @{ ok = $true; new = $Content; detail = 'already uncommented' } }
    $lines[$idx[0]] = $new
    return @{ ok = $true; new = (Join-TextLines -Lines $lines -Newline $nl); detail = 'uncommented' }
}

function Invoke-InsertEntry {
    param([string]$Content, $Entry)
    if (-not $Entry.anchor) { return @{ ok = $false; detail = 'missing anchor' } }
    $insert = if ($Entry.insert) { $Entry.insert } elseif ($Entry.replace) { $Entry.replace } else { $null }
    if (-not $insert) { return @{ ok = $false; detail = 'missing insert/replace text' } }
    $nl = ''
    $lines = @(Split-TextLines -Text $Content -Newline ([ref]$nl))
    $idx = @()
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $Entry.anchor) { $idx += $i } }
    if ($idx.Count -eq 0) { return @{ ok = $false; detail = 'anchor missing' } }
    if ($idx.Count -gt 1) { return @{ ok = $false; detail = 'anchor ambiguous' } }
    if ($Content.Contains($insert)) { return @{ ok = $true; new = $Content; detail = 'already present' } }
    $i = $idx[0]
    $head = @($lines[0..$i])
    $tail = if ($i + 1 -le $lines.Count - 1) { @($lines[($i + 1)..($lines.Count - 1)]) } else { @() }
    $insLines = @($insert -split "`r?`n")
    $newLines = @($head + $insLines + $tail)
    return @{ ok = $true; new = (Join-TextLines -Lines $newLines -Newline $nl); detail = 'inserted' }
}

function Invoke-ReplaceGuard {
    param([string]$Content, $Entry)
    $pattern = if ($Entry.find) { $Entry.find } elseif ($Entry.anchor) { $Entry.anchor } else { $null }
    if (-not $pattern) { return @{ ok = $false; detail = 'missing find/anchor' } }
    $replacement = if ($null -ne $Entry.replace) { [string]$Entry.replace } else { '' }
    $rx = [regex]$pattern
    $count = $rx.Matches($Content).Count
    if ($count -eq 0) { return @{ ok = $false; detail = 'anchor missing' } }
    if ($count -gt 1 -and -not $Entry.global) { return @{ ok = $false; detail = 'anchor ambiguous' } }
    $new = $rx.Replace($Content, [System.Text.RegularExpressions.MatchEvaluator] { param($m) $replacement })
    return @{ ok = $true; new = $new; detail = 'replaced' }
}

# Normalise BOTH schemas into one shape:
#   T1 real: {id,kind,addon,file,anchor:{regex,scope},apply:{op,text},fixedWhen:{regex}}
#   T2 stub: {id,type,target,op,find,anchor,insert,replace,fixedWhen,canary}
function Resolve-PatchEntry {
    param($Entry)
    $names = $Entry.PSObject.Properties.Name

    $kind = ''
    if ($names -contains 'kind' -and $Entry.kind) { $kind = [string]$Entry.kind }
    elseif ($names -contains 'type' -and $Entry.type) { $kind = [string]$Entry.type }

    $target = ''
    if ($names -contains 'target' -and $Entry.target) { $target = [string]$Entry.target }
    elseif ($Entry.addon -and $Entry.file) { $target = "$($Entry.addon)/$($Entry.file)" }

    $fixedWhen = $null
    if ($names -contains 'fixedWhen' -and $null -ne $Entry.fixedWhen) {
        if ($Entry.fixedWhen -is [string]) { $fixedWhen = $Entry.fixedWhen }
        elseif ($Entry.fixedWhen.PSObject.Properties.Name -contains 'regex') { $fixedWhen = [string]$Entry.fixedWhen.regex }
    }

    $anchorPattern = $null; $scopePattern = $null
    if ($names -contains 'anchor' -and $null -ne $Entry.anchor) {
        if ($Entry.anchor -is [string]) { $anchorPattern = [string]$Entry.anchor }
        else {
            if ($Entry.anchor.PSObject.Properties.Name -contains 'regex') { $anchorPattern = [string]$Entry.anchor.regex }
            if ($Entry.anchor.PSObject.Properties.Name -contains 'scope') { $scopePattern = [string]$Entry.anchor.scope }
        }
    }
    if (($null -eq $anchorPattern -or $anchorPattern -eq '') -and ($names -contains 'find') -and $Entry.find) { $anchorPattern = [string]$Entry.find }

    $op = $null; $applyText = $null
    if ($names -contains 'apply' -and $null -ne $Entry.apply) {
        $op = [string]$Entry.apply.op
        if ($Entry.apply.PSObject.Properties.Name -contains 'text') { $applyText = [string]$Entry.apply.text }
    }
    if (($null -eq $op -or $op -eq '') -and ($names -contains 'op') -and $Entry.op) { $op = [string]$Entry.op }
    if (($null -eq $applyText -or $applyText -eq '') -and ($names -contains 'insert') -and $Entry.insert) { $applyText = [string]$Entry.insert }
    if (($null -eq $applyText -or $applyText -eq '') -and ($names -contains 'replace') -and $null -ne $Entry.replace) { $applyText = [string]$Entry.replace }

    $canary = $null
    if (($names -contains 'canary') -and $Entry.canary) { $canary = [string]$Entry.canary }

    return [pscustomobject]@{
        id = [string]$Entry.id; kind = $kind; target = $target
        fixedWhen = $fixedWhen; anchor = $anchorPattern; scope = $scopePattern
        op = $op; applyText = $applyText; canary = $canary
    }
}

# Real `replace` op.  The anchor regex must resolve to EXACTLY ONE target, or
# the entry is a CONFLICT.  When `scope` is present it disambiguates duplicate
# anchors: the scope match(es) are located and, for each, the first anchor at
# or after the scope end is a candidate; the distinct candidate set must be 1.
function Invoke-ReplaceMatch {
    param([string]$Orig, [string]$Content, [string]$Pattern, [string]$Scope, [string]$Text)
    if (-not $Pattern) { return @{ ok = $false; detail = 'missing anchor.regex' } }
    $rx = [regex]$Pattern
    # Resolve WHICH occurrence against the pristine text: earlier same-file data
    # patches append comments after their anchors, which would otherwise break a
    # later entry's scope regex.  The resolved occurrence is carried into the
    # working text by anchor ordinal (no patch removes an anchor).
    if ($Scope) {
        $srx = [regex]$Scope
        $scopeMatches = $srx.Matches($Orig)
        if ($scopeMatches.Count -eq 0) { return @{ ok = $false; detail = 'anchor.scope missing' } }
        $cand = @{}
        foreach ($sm in $scopeMatches) {
            $after = $sm.Index + $sm.Length
            foreach ($am in $rx.Matches($Orig)) {
                if ($am.Index -ge $after) { $cand[$am.Index] = $am; break }
            }
        }
        $cands = @($cand.Values)
        if ($cands.Count -eq 0) { return @{ ok = $false; detail = 'anchor missing in scope' } }
        if ($cands.Count -gt 1) { return @{ ok = $false; detail = 'anchor ambiguous in scope' } }
        $targetIndex = $cands[0].Index
    }
    else {
        $matches = $rx.Matches($Orig)
        if ($matches.Count -eq 0) { return @{ ok = $false; detail = 'anchor missing' } }
        if ($matches.Count -gt 1) { return @{ ok = $false; detail = 'anchor ambiguous' } }
        $targetIndex = $matches[0].Index
    }

    $ordinal = -1; $i = 0
    foreach ($am in $rx.Matches($Orig)) {
        if ($am.Index -eq $targetIndex) { $ordinal = $i; break }
        $i++
    }
    $workMatches = $rx.Matches($Content)
    if ($ordinal -lt 0 -or $ordinal -ge $workMatches.Count) {
        return @{ ok = $false; detail = 'anchor drift after prior edits' }
    }
    $m = $workMatches[$ordinal]
    $new = $Content.Substring(0, $m.Index) + $Text + $Content.Substring($m.Index + $m.Length)
    return @{ ok = $true; new = $new; detail = 'replaced' }
}

# Real `assert` op (canary): the sentinel must be present.  Per spec the
# canary fires when the sentinel is absent while GlowCooldown has appeared.
function Invoke-AssertPresent {
    param([string]$Content, [string]$Pattern)
    if ($Pattern -and $Content -match $Pattern) { return @{ ok = $true; new = $Content; detail = 'sentinel present' } }
    if ($Content -match 'GlowCooldown') { return @{ ok = $false; detail = 'canary miss: sentinel absent while GlowCooldown present' } }
    return @{ ok = $false; detail = 'canary miss: sentinel absent' }
}

# ---------------------------------------------------------------------------
# Step 3 - evaluate + apply
# ---------------------------------------------------------------------------
$results = New-Object System.Collections.Generic.List[object]
foreach ($entry in $entries) {
    $n      = Resolve-PatchEntry -Entry $entry
    $id     = $n.id
    $target = $n.target
    $op     = $n.op
    $status = 'APPLIED'
    $detail = ''

    if ([string]::IsNullOrWhiteSpace($target)) {
        $status = 'CONFLICT'; $detail = 'missing target'
    }
    else {
        $orig = Get-OrigContent -Target $target
        if ($null -eq $orig) {
            $status = 'CONFLICT'; $detail = "target not found: $target"
        }
        else {
            $isFixed = $false
            if ($n.fixedWhen) {
                if ($orig -match $n.fixedWhen) { $isFixed = $true }
            }
            if ($isFixed) {
                $status = 'FIXED-UPSTREAM'; $detail = 'fixedWhen matched new upstream'
            }
            elseif (($op -ieq 'replace' -or $op -ieq 'replacematch') -and [string]::IsNullOrEmpty($n.applyText)) {
                $status = 'CONFLICT'; $detail = 'missing apply.text'
            }
            else {
                $work = Get-WorkContent -Target $target
                $res = switch ($op.ToLowerInvariant()) {
                    'uncommententry' { Invoke-UncommentEntry -Content $work -Entry $entry }
                    'insertentry'    { Invoke-InsertEntry    -Content $work -Entry $entry }
                    'replaceguard'   { Invoke-ReplaceGuard   -Content $work -Entry $entry }
                    'replace'        { Invoke-ReplaceMatch   -Orig $orig -Content $work -Pattern $n.anchor -Scope $n.scope -Text $n.applyText }
                    'assert'         { Invoke-AssertPresent  -Content $work -Pattern $n.anchor }
                    default          { @{ ok = $false; detail = "unknown op '$op'" } }
                }
                if (-not $res.ok) { $status = 'CONFLICT'; $detail = $res.detail }
                elseif ($n.canary -and ($res.new -notmatch $n.canary)) {
                    $status = 'CONFLICT'; $detail = 'canary miss after apply'
                }
                else {
                    Set-WorkContent -Target $target -Content $res.new
                    $detail = $res.detail
                }
            }
        }
    }

    $results.Add([pscustomobject]@{ id = $id; op = $op; target = $target; status = $status; detail = $detail })
    Write-Host ("  {0,-8} {1,-22} {2} {3}" -f $status, $id, $target, $detail)
}

$fixedCount    = @($results | Where-Object status -eq 'FIXED-UPSTREAM').Count
$appliedCount  = @($results | Where-Object status -eq 'APPLIED').Count
$conflictList  = @($results | Where-Object status -eq 'CONFLICT')
$conflictCount = $conflictList.Count

Write-Output ("Sync: APPLIED {0}  CONFLICT {1}  FIXED-UPSTREAM {2}" -f $appliedCount, $conflictCount, $fixedCount)

# Step 4 - upstream already covers everything.
if ($results.Count -eq $fixedCount) {
    Write-Host 'upstream covers all'
    if ($conflictCount -eq 0) { exit 0 } else { exit 1 }
}

# ---------------------------------------------------------------------------
# Steps 2/5/6 - materialise out/, report, publish
# ---------------------------------------------------------------------------
if ($WhatIf) {
    Write-Host "[WhatIf] would write $OutRoot and SYNC-REPORT.md"
    if ($conflictCount -eq 0) { Write-Host "[WhatIf] would publish to $PublishTo" }
    else { Write-Host "[WhatIf] CONFLICT present: publish would be skipped" }
    if ($conflictCount -gt 0) { exit 1 } else { exit 0 }
}

# 2. fresh copy of NewUpstream -> out
if (Test-Path -LiteralPath $OutRoot) { Remove-Item -LiteralPath $OutRoot -Recurse -Force }
New-Item -ItemType Directory -Path $OutRoot -Force | Out-Null
Copy-Item -Path (Join-Path $NewUpstream '*') -Destination $OutRoot -Recurse -Force

# overwrite files touched by successfully applied patches
foreach ($key in $workCache.Keys) {
    $orig = if ($origCache.ContainsKey($key)) { $origCache[$key] } else { $null }
    $work = $workCache[$key]
    if ($null -ne $work -and $work -ne $orig) {
        $rel = $key -replace '/', '\'
        $dest = Join-Path $OutRoot $rel
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
        Set-Content -LiteralPath $dest -Value $work -NoNewline -Encoding utf8
    }
}

# 5. report
$reportPath = Join-Path $OutRoot 'SYNC-REPORT.md'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# Custom MaxDps SYNC-REPORT')
[void]$sb.AppendLine('')
[void]$sb.AppendLine("- Generated: $(Get-Date -Format o)")
[void]$sb.AppendLine("- NewUpstream: ``$NewUpstream``")
[void]$sb.AppendLine("- Entries: $($results.Count)")
[void]$sb.AppendLine("- FIXED-UPSTREAM: $fixedCount")
[void]$sb.AppendLine("- APPLIED: $appliedCount")
[void]$sb.AppendLine("- CONFLICT: $conflictCount")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Churn vs pristine')
[void]$sb.AppendLine('')
if ($null -eq $manifest) {
    [void]$sb.AppendLine('- no pristine manifest (churn unavailable)')
} else {
    [void]$sb.AppendLine("- added: $($added.Count)")
    [void]$sb.AppendLine("- changed: $($changed.Count)")
    [void]$sb.AppendLine("- removed: $($removed.Count)")
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Entries')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| id | status | op | target | detail |')
[void]$sb.AppendLine('|---|---|---|---|---|')
foreach ($r in $results) {
    [void]$sb.AppendLine("| $($r.id) | $($r.status) | $($r.op) | $($r.target) | $($r.detail -replace '\|','\|') |")
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Versions')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| folder | version | interface |')
[void]$sb.AppendLine('|---|---|---|')
foreach ($name in $newVersions.Keys) {
    [void]$sb.AppendLine("| $name | $($newVersions[$name]) | $($newInterfaces[$name]) |")
}
Set-Content -LiteralPath $reportPath -Value $sb.ToString() -Encoding utf8
Write-Host "Wrote report: $reportPath"

# 6. publish only on a clean run
if ($conflictCount -eq 0) {
    if (-not (Test-Path -LiteralPath $PublishTo)) {
        Write-Warning "PublishTo does not exist, skipping publish: $PublishTo"
    }
    else {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backupRoot = Join-Path $OutRoot (Join-Path '_backup' $stamp)
        foreach ($d in Get-ChildItem -LiteralPath $OutRoot -Directory | Where-Object { $_.Name -ne '_backup' }) {
            $target = Join-Path $PublishTo $d.Name
            if (Test-Path -LiteralPath $target) {
                if (-not (Test-Path -LiteralPath $backupRoot)) { New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null }
                Move-Item -LiteralPath $target -Destination (Join-Path $backupRoot $d.Name) -Force
            }
            Copy-Item -LiteralPath $d.FullName -Destination $target -Recurse -Force
        }
        Write-Host "Published $($newFolders.Count) folder(s) to $PublishTo (backup: $backupRoot)"
    }
}
else {
    Write-Warning "CONFLICT present ($conflictCount): out/ and report written, publish skipped."
}

if ($conflictCount -gt 0) { exit 1 } else { exit 0 }
