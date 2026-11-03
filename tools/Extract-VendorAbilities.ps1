# Extracts the authoritative ability categories from the pinned MaxDps vendor
# snapshot into JSON for the Companion ability knowledge catalog.
#
#   upstream kaminaris/MaxDps v11.3.58 Cooldowns.lua (supplied via -SourcePath)
#     ns.classCooldowns[CLASS][SPEC] = { offensive = {...}, defensive = {...} }
#     ns.classInterrupts[CLASS][SPEC] = { ["Name"] = id, ... }
#
# Output: app/MaxDpsCompanion.Core/Knowledge/vendor-abilities.json
#
# The upstream source is READ-ONLY input. This script never writes to vendor/.
# Reproduction (obtain Cooldowns.lua from the trusted v11.3.58 Git tag first):
#   pwsh -File tools/Extract-VendorAbilities.ps1 -SourcePath PATH_TO_COOLDOWNS_LUA
# Provenance is recorded separately from the older 12.1 curated-policy layer.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [string]$SourceVersion = '11.3.58',
    [int]$SourceInterface = 120105
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$luaPath = [System.IO.Path]::GetFullPath($SourcePath)
$outPath = Join-Path $root 'app/MaxDpsCompanion.Core/Knowledge/vendor-abilities.json'

if (-not (Test-Path -LiteralPath $luaPath -PathType Leaf)) {
    throw "upstream Cooldowns.lua not found at $luaPath"
}
if ($SourceVersion -ne '11.3.58' -or $SourceInterface -ne 120105) {
    throw 'This release pins MaxDps v11.3.58 / interface 120105. Do not rewrite the embedded table with another version.'
}

# Verify the exact upstream Git blob, not merely a matching row count.
# Git SHA-1 hashes "blob <byte length>\0" followed by the raw file bytes.
$expectedBlobSha = '3640261ac4c3b31692434f26ce60d48978e3da03'
$fileBytes = [System.IO.File]::ReadAllBytes($luaPath)
$headerBytes = [System.Text.Encoding]::ASCII.GetBytes("blob $($fileBytes.Length)`0")
$blobBytes = [byte[]]::new($headerBytes.Length + $fileBytes.Length)
[System.Buffer]::BlockCopy($headerBytes, 0, $blobBytes, 0, $headerBytes.Length)
[System.Buffer]::BlockCopy($fileBytes, 0, $blobBytes, $headerBytes.Length, $fileBytes.Length)
$SourceGitBlobSha = [Convert]::ToHexString(
    [System.Security.Cryptography.SHA1]::HashData($blobBytes)).ToLowerInvariant()
if ($SourceGitBlobSha -ne $expectedBlobSha) {
    throw "Source blob differs from pinned v11.3.58 Cooldowns.lua: got $SourceGitBlobSha, expected $expectedBlobSha"
}

$lines = Get-Content -LiteralPath $luaPath
$entries = New-Object System.Collections.Generic.List[object]

$mode = ''       # '', 'cooldowns', 'interrupts'
$class = ''
$spec = ''
$bucket = ''

foreach ($raw in $lines) {
    $line = $raw.TrimEnd()

    if ($line -match '^ns\.classCooldowns\s*=\s*\{\s*$') { $mode = 'cooldowns'; $class=''; $spec=''; $bucket=''; continue }
    if ($line -match '^ns\.classInterrupts\s*=\s*\{\s*$') { $mode = 'interrupts'; $class=''; $spec=''; $bucket=''; continue }
    if ($mode -eq '' -and $line -match '^\}\s*$') { continue }

    # class: 4-space indent, UPPERCASE token, ends with `= {`
    if ($line -match '^\s{4}([A-Z]+)\s*=\s*\{\s*$') { $class = $Matches[1]; $spec=''; $bucket=''; continue }
    # bucket keys (cooldowns only): 12-space indent
    if ($line -match '^\s{12}(offensive|defensive)\s*=\s*\{\s*$') { $bucket = $Matches[1]; continue }
    # spec keys: 8-space indent, either bare or ["Quoted Name"]
    if ($line -match '^\s{8}(\["(?<q>[^"]+)"\]|(?<b>[A-Za-z_][A-Za-z0-9_]*))\s*=\s*\{\s*$') {
        if ($Matches['q']) { $spec = $Matches['q'] } else { $spec = $Matches['b'] }
        $bucket = ''
        continue
    }
    # ability entry: 16-space indent in classCooldowns, 12-space in classInterrupts
    if ($mode -ne '' -and $line -match '^\s{12,16}\["(?<name>[^"]+)"\]\s*=\s*(?<id>\d+)\s*,') {
        $category =
            if ($mode -eq 'interrupts') { 'Interrupt' }
            elseif ($bucket -eq 'defensive') { 'Defensive' }
            elseif ($bucket -eq 'offensive') { 'Offensive' }
            else { '' }
        if ($category -ne '' -and $class -ne '' -and $spec -ne '') {
            $entries.Add([pscustomobject]@{
                class = $class
                spec = $spec
                category = $category
                name = $Matches['name']
                id = [int]$Matches['id']
            })
        }
        continue
    }
}

# Fail before write if the upstream revision does not match the known v11.3.58
# 368-row source or reintroduces the old Frost DK spell id.
if ($entries.Count -ne 368) {
    throw "Unexpected 12.1.5 MaxDps Cooldowns row count: $($entries.Count), expected 368"
}
$breath = @($entries | Where-Object {
    $_.class -eq 'DEATHKNIGHT' -and $_.spec -eq 'Frost' -and
    $_.category -eq 'Offensive' -and $_.name -eq 'Breath of Sindragosa'
})
if ($breath.Count -ne 1 -or $breath[0].id -ne 1249658) {
    throw 'Upstream Frost Breath of Sindragosa is not 1249658; stop and re-audit the source.'
}

# Deduplicate on (class, spec, category, name) keeping the lowest id.
$dedup = @{}
$order = New-Object System.Collections.Generic.List[string]
foreach ($e in $entries) {
    $key = "$($e.class)|$($e.spec)|$($e.category)|$($e.name)"
    if ($dedup.ContainsKey($key)) {
        if ($e.id -lt $dedup[$key].id) { $dedup[$key] = $e }
    } else {
        $dedup[$key] = $e
        $order.Add($key)
    }
}

$sorted = $order | ForEach-Object { $dedup[$_] } | Sort-Object class, spec, category, name

$doc = [pscustomobject]@{
    generatedFrom = 'kaminaris/MaxDps v11.3.58 (Cooldowns.lua, Retail 12.1.5)'
    sourceVersion = $SourceVersion
    sourceInterface = $SourceInterface
    sourceGitBlobSha = $SourceGitBlobSha
    note = 'Generated by tools/Extract-VendorAbilities.ps1 from explicitly supplied upstream source. Never edit vendor/.'
    entries = @($sorted)
}

$dir = Split-Path -Parent $outPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$json = $doc | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($outPath, $json)

$byCategory = $entries | Group-Object category | Sort-Object Name
"Wrote $($sorted.Count) entries to $outPath"
$byCategory | ForEach-Object { "  $($_.Name): $($_.Count)" }
$byClass = ($entries | Select-Object -ExpandProperty class -Unique).Count
"Classes: $byClass"
