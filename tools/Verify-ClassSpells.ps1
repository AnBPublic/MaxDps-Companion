#requires -version 7
<#
Verifies the Companion's modeled class-skill ids against the live retail client
DB2 export and resolves their icon slugs, then downloads the missing icons.

Input  : app/MaxDpsCompanion/Knowledge/class-spells.json  (read-only)
Sources: https://wago.tools/db2/SpellName/csv
         https://wago.tools/db2/SpellMisc/csv
         https://wago.tools/db2/ManifestInterfaceData/csv
         https://render.worldofwarcraft.com/us/icons/56/{slug}.jpg
         https://wow.zamimg.com/images/wow/icons/large/{slug}.jpg
Output : app/MaxDpsCompanion/Knowledge/spell-verification.json  (UTF-8 no BOM)
         dist/assets/icons/{id}.jpg
         app/MaxDpsCompanion/bin/Release/net8.0-windows/win-x64/assets/icons/{id}.jpg

IMPORTANT COLUMN FINDING (verified live 2026-09-28):
  SpellMisc's `ID` column is the *misc record* id, NOT the spell id. The actual
  spell id is the trailing `SpellID` column. Joining on `ID` yields wrong icons
  (e.g. spell 53 Backstab -> Ability_Defend). This script joins on `SpellID`,
  which was validated against the six known spells below.

Run from the repository root:
  pwsh -File tools/Verify-ClassSpells.ps1
#>

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Split-Path -Parent $PSScriptRoot
$classSpellsPath = Join-Path $root 'app/MaxDpsCompanion/Knowledge/class-spells.json'
$outPath         = Join-Path $root 'app/MaxDpsCompanion/Knowledge/spell-verification.json'
$iconDirs = @(
    (Join-Path $root 'dist/assets/icons'),
    (Join-Path $root 'app/MaxDpsCompanion/bin/Release/net8.0-windows/win-x64/assets/icons')
)

$ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'
$renderBase = 'https://render.worldofwarcraft.com/us/icons/56/'
$zamBase    = 'https://wow.zamimg.com/images/wow/icons/large/'

# Known spells used as a fail-loud validation of the derived mapping.
# NOTE: a plain [ordered] dictionary has an integer *positional* indexer, so
# $knownExpected[5277] would read element #5277. Use a typed dictionary.
$knownExpected = [System.Collections.Generic.Dictionary[int, string]]::new()
$knownExpected[5277]   = 'spell_shadow_shadowward'          # Evasion
$knownExpected[871]    = 'ability_warrior_shieldwall'       # Shield Wall
$knownExpected[193315] = 'spell_shadow_ritualofsacrifice'   # Sinister Strike
$knownExpected[53]     = 'ability_backstab'                 # Backstab
$knownExpected[100]    = 'ability_warrior_charge'           # Charge
$knownExpected[1766]   = 'ability_kick'                     # Kick
$knownIds = @($knownExpected.Keys)

$deprecatedRegex = '\(\s*(OLD|TEST|DND|UNUSED|DEPRECATED|DO NOT USE)\s*\)\s*$'

# Exact byte counter around the network stream (Content-Length is chunked/absent
# on wago.tools, so we measure what we actually read).
$countingSrc = @'
using System;
using System.IO;
public sealed class CountingStream : Stream {
    private readonly Stream _inner;
    public long BytesRead { get; private set; }
    public CountingStream(Stream inner) { _inner = inner; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) {
        int n = _inner.Read(buffer, offset, count);
        BytesRead += n;
        return n;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
'@
Add-Type -TypeDefinition $countingSrc -Language CSharp

function ConvertTo-JsonString([string]$s) {
    if ($null -eq $s) { return '""' }
    $r = $s.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    return '"' + $r + '"'
}

$totalWatch = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------------------
# 1. Load wanted ids (distinct class-spell ids + validation ids)
# ---------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $classSpellsPath)) { throw "class-spells.json not found at $classSpellsPath" }
$classRaw = Get-Content -LiteralPath $classSpellsPath -Raw | ConvertFrom-Json
$classIds = [System.Collections.Generic.HashSet[int]]::new()
foreach ($e in $classRaw.entries) { [void]$classIds.Add([int]$e.id) }
$classIdList = @($classIds) | Sort-Object

$wanted = [System.Collections.Generic.HashSet[int]]::new()
foreach ($id in $classIdList) { [void]$wanted.Add($id) }
foreach ($id in $knownIds) { [void]$wanted.Add($id) }

"Class-spell rows: $(@($classRaw.entries).Count); distinct ids: $($classIdList.Count)"

$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.Add('User-Agent', $ua)
$client.Timeout = [System.TimeSpan]::FromMinutes(10)

$nameDict = [System.Collections.Generic.Dictionary[int, string]]::new()
$miscFd   = [System.Collections.Generic.Dictionary[int, int]]::new()
$miscDiff = [System.Collections.Generic.Dictionary[int, int]]::new()
$fdSlug   = [System.Collections.Generic.Dictionary[int, string]]::new()
$fdPath   = [System.Collections.Generic.Dictionary[int, string]]::new()

function Get-NameField([string]$line, [int]$firstComma) {
    $field = $line.Substring($firstComma + 1)
    if ($field.StartsWith('"')) {
        $field = $field.Substring(1)
        if ($field.EndsWith('"')) { $field = $field.Substring(0, $field.Length - 1) }
        $field = $field.Replace('""', '"')
    }
    return $field
}

# ---------------------------------------------------------------------------
# 2a. SpellName -> id -> official Name_lang
# ---------------------------------------------------------------------------
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$spellNameBytes = 0L
$spellNameRows = 0
do {
    $resp = $client.GetAsync('https://wago.tools/db2/SpellName/csv', [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    if (-not $resp.IsSuccessStatusCode) { throw "SpellName/csv -> $($resp.StatusCode)" }
    $netStream = $resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $cs = [CountingStream]::new($netStream)
    $sr = [System.IO.StreamReader]::new($cs)
    $header = $sr.ReadLine()
    if ($header -notmatch '^ID,Name_lang') { throw "Unexpected SpellName header: $header" }
    while ($null -ne ($line = $sr.ReadLine())) {
        $spellNameRows++
        if ($line.EndsWith("`r")) { $line = $line.Substring(0, $line.Length - 1) }
        $i = $line.IndexOf(',')
        if ($i -lt 0) { continue }
        $id = [int]$line.Substring(0, $i)
        if ($wanted.Contains($id)) { $nameDict[$id] = Get-NameField $line $i }
    }
    $spellNameBytes = $cs.BytesRead
    $sr.Dispose(); $cs.Dispose(); $resp.Dispose()
} while ($false)
$sw.Stop()
"SpellName/csv: rows=$spellNameRows bytes=$spellNameBytes elapsed=$($sw.Elapsed.TotalSeconds.ToString('F1'))s"

# ---------------------------------------------------------------------------
# 2b. SpellMisc -> SpellID -> SpellIconFileDataID  (join on SpellID column!)
# ---------------------------------------------------------------------------
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$spellMiscBytes = 0L
$spellMiscRows = 0
do {
    $resp = $client.GetAsync('https://wago.tools/db2/SpellMisc/csv', [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    if (-not $resp.IsSuccessStatusCode) { throw "SpellMisc/csv -> $($resp.StatusCode)" }
    $netStream = $resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $cs = [CountingStream]::new($netStream)
    $sr = [System.IO.StreamReader]::new($cs)
    $header = $sr.ReadLine()
    $cols = $header.Split(',')
    $idIdx    = [Array]::IndexOf($cols, 'ID')
    $iconIdx  = [Array]::IndexOf($cols, 'SpellIconFileDataID')
    $spellIdx = [Array]::IndexOf($cols, 'SpellID')
    $diffIdx  = [Array]::IndexOf($cols, 'DifficultyID')
    if ($idIdx -lt 0 -or $iconIdx -lt 0 -or $spellIdx -lt 0) {
        throw "SpellMisc header missing expected columns. Found: $header"
    }
    $useLast = ($spellIdx -eq ($cols.Count - 1))
    "  SpellMisc columns: ID=$idIdx SpellIconFileDataID=$iconIdx DifficultyID=$diffIdx SpellID=$spellIdx (last=$useLast); ncols=$($cols.Count)"
    while ($null -ne ($line = $sr.ReadLine())) {
        $spellMiscRows++
        if ($line.EndsWith("`r")) { $line = $line.Substring(0, $line.Length - 1) }
        $sid = 0
        if ($useLast) {
            $lc = $line.LastIndexOf(',')
            if ($lc -lt 0) { continue }
            if (-not [int]::TryParse($line.Substring($lc + 1), [ref]$sid)) { continue }
        } else {
            $partsProbe = $line.Split(',')
            if (-not [int]::TryParse($partsProbe[$spellIdx], [ref]$sid)) { continue }
        }
        if (-not $wanted.Contains($sid)) { continue }
        $p = $line.Split(',')
        $fd = 0; $diff = 0
        [void][int]::TryParse($p[$iconIdx], [ref]$fd)
        if ($diffIdx -ge 0) { [void][int]::TryParse($p[$diffIdx], [ref]$diff) }
        if (-not $miscFd.ContainsKey($sid)) {
            $miscFd[$sid] = $fd
            $miscDiff[$sid] = $diff
        } elseif ($diff -eq 0 -and $miscDiff[$sid] -ne 0) {
            $miscFd[$sid] = $fd
            $miscDiff[$sid] = $diff
        }
    }
    $spellMiscBytes = $cs.BytesRead
    $sr.Dispose(); $cs.Dispose(); $resp.Dispose()
} while ($false)
$sw.Stop()
"SpellMisc/csv: rows=$spellMiscRows bytes=$spellMiscBytes elapsed=$($sw.Elapsed.TotalSeconds.ToString('F1'))s; icon mappings=$($miscFd.Count)"

# ---------------------------------------------------------------------------
# 2c. ManifestInterfaceData -> fileDataID -> slug (ICONS only)
# ---------------------------------------------------------------------------
$neededFd = [System.Collections.Generic.HashSet[int]]::new()
foreach ($v in $miscFd.Values) { [void]$neededFd.Add([int]$v) }

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$manifestBytes = 0L
$manifestRows = 0
do {
    $resp = $client.GetAsync('https://wago.tools/db2/ManifestInterfaceData/csv', [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    if (-not $resp.IsSuccessStatusCode) { throw "ManifestInterfaceData/csv -> $($resp.StatusCode)" }
    $netStream = $resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $cs = [CountingStream]::new($netStream)
    $sr = [System.IO.StreamReader]::new($cs)
    $header = $sr.ReadLine()
    if ($header -notmatch '^ID,FilePath,FileName') { throw "Unexpected ManifestInterfaceData header: $header" }
    while ($null -ne ($line = $sr.ReadLine())) {
        $manifestRows++
        if ($line.EndsWith("`r")) { $line = $line.Substring(0, $line.Length - 1) }
        $i = $line.IndexOf(',')
        if ($i -lt 0) { continue }
        $fid = [int]$line.Substring(0, $i)
        if (-not $neededFd.Contains($fid)) { continue }
        $rest = $line.Substring($i + 1)
        $j = $rest.IndexOf(',')
        if ($j -lt 0) { continue }
        $fp = $rest.Substring(0, $j)
        $fn = $rest.Substring($j + 1)
        if ($fn.StartsWith('"')) { $fn = $fn.Substring(1); if ($fn.EndsWith('"')) { $fn = $fn.Substring(0, $fn.Length - 1) } }
        $joined = "$fp$fn"
        if ($joined -match 'ICONS') {
            $base = $fn
            if ($base -match '(?i)\.blp$') { $base = $base.Substring(0, $base.Length - 4) }
            if (-not $fdSlug.ContainsKey($fid)) {
                $fdSlug[$fid] = $base.ToLowerInvariant()
                $fdPath[$fid] = $joined
            }
        }
    }
    $manifestBytes = $cs.BytesRead
    $sr.Dispose(); $cs.Dispose(); $resp.Dispose()
} while ($false)
$sw.Stop()
$neededNonZero = @($neededFd | Where-Object { $_ -gt 0 }).Count
"ManifestInterfaceData/csv: rows=$manifestRows bytes=$manifestBytes elapsed=$($sw.Elapsed.TotalSeconds.ToString('F1'))s; needed fds=$($neededFd.Count) (nonzero=$neededNonZero); icon slugs resolved=$($fdSlug.Count)"

# ---------------------------------------------------------------------------
# 3. Validation: known spells must derive the expected slugs
# ---------------------------------------------------------------------------
"`nKnown-spell derivations:"
$validationFailures = New-Object System.Collections.Generic.List[string]
foreach ($id in $knownIds) {
    $nm = if ($nameDict.ContainsKey([int]$id)) { $nameDict[[int]$id] } else { '' }
    $fd = if ($miscFd.ContainsKey([int]$id)) { $miscFd[[int]$id] } else { 0 }
    $slug = if ($fd -gt 0 -and $fdSlug.ContainsKey($fd)) { $fdSlug[$fd] } else { '' }
    $path = if ($fd -gt 0 -and $fdPath.ContainsKey($fd)) { $fdPath[$fd] } else { '' }
    $exp = $knownExpected[$id]
    $ok = ($slug -eq $exp)
    "  id=$id name='$nm' fd=$fd slug='$slug' path='$path' expected='$exp' $(if ($ok) { 'OK' } else { 'MISMATCH' })"
    if (-not $ok) { $validationFailures.Add("id $id expected '$exp' got '$slug' (name='$nm' fd=$fd)") }
}
if ($validationFailures.Count -gt 0) {
    throw "Known-spell validation FAILED (SpellMisc join is wrong): $($validationFailures -join '; ')"
}
"  All $($knownIds.Count) known-spell derivations match."

# ---------------------------------------------------------------------------
# 4. Build one entry per distinct class-spell id
# ---------------------------------------------------------------------------
$entries = New-Object System.Collections.Generic.List[object]
foreach ($id in $classIdList) {
    $hasName = $nameDict.ContainsKey([int]$id)
    $nm = if ($hasName) { $nameDict[[int]$id] } else { '' }
    $fd = if ($miscFd.ContainsKey([int]$id)) { $miscFd[[int]$id] } else { 0 }
    $slug = if ($fd -gt 0 -and $fdSlug.ContainsKey($fd)) { $fdSlug[$fd] } else { '' }
    $verified = ($hasName -and -not [regex]::IsMatch($nm, $deprecatedRegex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase))
    $entries.Add([ordered]@{
        id       = [int]$id
        name     = $nm
        icon     = $slug
        verified = [bool]$verified
    })
}
$entriesSorted = @($entries | Sort-Object { $_.id })
$verifiedCount = @($entriesSorted | Where-Object { $_.verified }).Count
$unverifiedCount = $entriesSorted.Count - $verifiedCount
$withSlug = @($entriesSorted | Where-Object { $_.icon -ne '' }).Count

# ---------------------------------------------------------------------------
# 5. Write spell-verification.json (UTF-8 no BOM, 2-space indent, sorted by id)
# ---------------------------------------------------------------------------
$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append("{`n")
[void]$sb.Append("  `"generatedFrom`": `"wago.tools DB2 CSV (SpellName / SpellMisc / ManifestInterfaceData), live build`",`n")
[void]$sb.Append("  `"note`": `"Generated by tools/Verify-ClassSpells.ps1 - do not hand-edit; regenerate when the vendor pin changes.`",`n")
[void]$sb.Append("  `"entries`": [`n")
for ($k = 0; $k -lt $entriesSorted.Count; $k++) {
    $e = $entriesSorted[$k]
    [void]$sb.Append("    {`n")
    [void]$sb.Append("      `"id`": $([int]$e.id),`n")
    [void]$sb.Append("      `"name`": $(ConvertTo-JsonString $e.name),`n")
    [void]$sb.Append("      `"icon`": $(ConvertTo-JsonString $e.icon),`n")
    [void]$sb.Append("      `"verified`": $(if ($e.verified) { 'true' } else { 'false' })`n")
    [void]$sb.Append("    }")
    if ($k -lt $entriesSorted.Count - 1) { [void]$sb.Append(',') }
    [void]$sb.Append("`n")
}
[void]$sb.Append("  ]`n")
[void]$sb.Append("}`n")
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
$jsonBytes = (Get-Item -LiteralPath $outPath).Length
$jsonHash = (Get-FileHash -LiteralPath $outPath -Algorithm SHA256).Hash
"`nWrote $outPath ($jsonBytes bytes, sha256=$jsonHash)"

# ---------------------------------------------------------------------------
# 6. Icon downloads (verified entries with a slug), 12 concurrent max
# ---------------------------------------------------------------------------
$iconWork = New-Object System.Collections.Generic.List[object]
foreach ($dir in $iconDirs) {
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
}
foreach ($e in $entriesSorted) {
    if (-not $e.verified -or $e.icon -eq '') { continue }
    foreach ($dir in $iconDirs) {
        $p = Join-Path $dir ("$($e.id).jpg")
        if (Test-Path -LiteralPath $p) { continue }
        $iconWork.Add([pscustomobject]@{ id = [int]$e.id; slug = $e.icon; dir = $dir; path = $p })
    }
}

"`nIcon downloads queued: $($iconWork.Count) file(s) across $($iconDirs.Count) dir(s)"

$renderOk = 0; $zamOk = 0; $dlFail = 0; $dlDone = 0
$successIds = [System.Collections.Generic.HashSet[int]]::new()
$iconWatch = [System.Diagnostics.Stopwatch]::StartNew()
if ($iconWork.Count -gt 0) {
    [object[]]$workArr = $iconWork.ToArray()
    $chunkSize = 250
    for ($start = 0; $start -lt $workArr.Count; $start += $chunkSize) {
        $end = [Math]::Min($start + $chunkSize - 1, $workArr.Count - 1)
        $chunk = $workArr[$start..$end]
        $res = $chunk | ForEach-Object -Parallel {
            $client = $using:client
            $renderBase = $using:renderBase
            $zamBase = $using:zamBase
            $id = $_.id; $slug = $_.slug; $path = $_.path
            # The icon CDNs strip the spaces that appear in some .blp file names
            # (e.g. "Warlock_ Healthstone" -> warlock_healthstone). The JSON keeps
            # the verbatim file-derived slug; the request tries both spellings.
            $slugs = @($slug)
            if ($slug.Contains(' ')) { $slugs += $slug.Replace(' ', '') }
            $ok = $false; $src = ''
            foreach ($base in @($renderBase, $zamBase)) {
                foreach ($trySlug in $slugs) {
                    if ($ok) { break }
                    $url = "$base$trySlug.jpg"
                    for ($a = 0; $a -lt 2 -and -not $ok; $a++) {
                        $resp = $null
                        try { $resp = $client.GetAsync($url).GetAwaiter().GetResult() } catch { $resp = $null }
                        if ($null -ne $resp) {
                            if ($resp.IsSuccessStatusCode) {
                                try {
                                    $bytes = $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                                    [System.IO.File]::WriteAllBytes($path, $bytes)
                                    $ok = $true
                                    $src = if ($base -eq $renderBase) { 'render' } else { 'zamimg' }
                                } catch { }
                            } else {
                                $code = [int]$resp.StatusCode
                                if ($code -eq 403 -or $code -eq 404) { $a = 2 }
                            }
                            $resp.Dispose()
                        }
                        if (-not $ok -and $a -lt 1) { Start-Sleep -Milliseconds 200 }
                    }
                }
                if ($ok) { break }
            }
            [pscustomobject]@{ id = $id; src = $src }
        } -ThrottleLimit 12
        foreach ($r in $res) {
            $dlDone++
            switch ($r.src) {
                'render' { $renderOk++; [void]$successIds.Add([int]$r.id) }
                'zamimg' { $zamOk++; [void]$successIds.Add([int]$r.id) }
                default  { $dlFail++ }
            }
        }
        Write-Host ("  progress {0}/{1} (render={2} zamimg={3} fail={4})" -f $dlDone, $workArr.Count, $renderOk, $zamOk, $dlFail)
    }
}
$iconWatch.Stop()
"Downloaded files: render=$renderOk zamimg=$zamOk fail=$dlFail; distinct icons downloaded=$($successIds.Count) in $($iconWatch.Elapsed.TotalSeconds.ToString('F1'))s"

$totalWatch.Stop()

# ---------------------------------------------------------------------------
# 7. Final report
# ---------------------------------------------------------------------------
"`n================ SUMMARY ================"
"CSV byte sizes/rows:"
"  SpellName              : $spellNameBytes bytes, $spellNameRows rows"
"  SpellMisc              : $spellMiscBytes bytes, $spellMiscRows rows"
"  ManifestInterfaceData  : $manifestBytes bytes, $manifestRows rows"
"Class-spell distinct ids : $($classIdList.Count)"
"Verified                 : $verifiedCount"
"Unverified               : $unverifiedCount"
"Entries with icon slug   : $withSlug"
"Downloads render/zam/fail: $renderOk / $zamOk / $dlFail"
"JSON bytes/sha256        : $jsonBytes / $jsonHash"
"Total runtime            : $($totalWatch.Elapsed.TotalSeconds.ToString('F1'))s"

"`nTop 20 unverified ids:"
$entriesSorted | Where-Object { -not $_.verified } | Select-Object -First 20 | ForEach-Object {
    "  id=$($_.id) name='$($_.name)' icon='$($_.icon)'"
}

"`nCache dir file counts / bytes:"
foreach ($dir in $iconDirs) {
    $files = @(Get-ChildItem -LiteralPath $dir -Filter '*.jpg' -File -ErrorAction SilentlyContinue)
    $sum = ($files | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) { $sum = 0 }
    "  $dir : $($files.Count) files, $sum bytes"
}
