#requires -version 7
<#
Ability registry audit (registry §54, v2.7 §5/§6).

Runs the newest built companion against the embedded ability registry, writes
the machine-generated markdown report to docs/research/ABILITY_REGISTRY_AUDIT.md
plus the machine-readable coverage manifest (--ability-coverage), and ENFORCES
the bar: violations 0, warnings 0, missing 0, stale 0. A non-clean audit is a
failure (exit 3); it is no longer enough to print the numbers.

It also checks that the committed addon catalog is byte-identical to the
generated one (--gen-catalog=<temp> + Compare-Object). Drift is REPORTED, never
auto-fixed: the addon file is owned by the addon workstream. Exit code 2 signals
drift; 1 signals a failed audit run; 3 signals a non-clean audit; 0 is clean.

Run from the repository root:
  pwsh -File tools/ability_audit.ps1
  pwsh -File tools/ability_audit.ps1 -SkipCatalogCheck
#>
[CmdletBinding()]
param(
    [switch]$SkipCatalogCheck,
    # Explicit companion exe (CI/temp builds); default = newest bin/Release exe, then dist/.
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$reportPath = Join-Path $root 'docs/research/ABILITY_REGISTRY_AUDIT.md'
$coveragePath = Join-Path $root 'docs/research/ABILITY_COVERAGE.json'
$addonCatalog = Join-Path $root 'addon/MaxDpsBridge/Catalog.lua'

function Resolve-CompanionExe {
    if (-not [string]::IsNullOrWhiteSpace($ExePath)) {
        if (-not (Test-Path -LiteralPath $ExePath)) { throw "ExePath not found: $ExePath" }
        return (Resolve-Path -LiteralPath $ExePath).Path
    }
    $candidates = @(
        Get-ChildItem -Path (Join-Path $root 'app/MaxDpsCompanion/bin/Release') `
            -Recurse -Filter 'MaxDpsCompanion.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'win-x64' } |
            Sort-Object LastWriteTime -Descending
    )
    if ($candidates.Count -gt 0) { return $candidates[0].FullName }

    $dist = Join-Path $root 'dist/MaxDpsCompanion.exe'
    if (Test-Path -LiteralPath $dist) { return $dist }

    throw "No MaxDpsCompanion.exe found under app/MaxDpsCompanion/bin/Release/**/win-x64 or dist/."
}

$exe = Resolve-CompanionExe
Write-Output "exe    : $exe"
Write-Output "report : $reportPath"

if (-not (Test-Path -LiteralPath (Split-Path -Parent $reportPath))) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
}

# The companion is a WinExe (GUI subsystem): a bare &-invocation does not block,
# so run it through Start-Process to capture the real exit code.
function Invoke-Companion {
    param([string[]]$Arguments)
    $proc = Start-Process -FilePath $exe -ArgumentList $Arguments -NoNewWindow -Wait -PassThru
    return $proc.ExitCode
}

# ---- 1. Ability audit + coverage manifest ------------------------------------
function Get-ReportCount {
    param([string]$Pattern)
    $match = Select-String -Path $reportPath -Pattern $Pattern | Select-Object -First 1
    if ($null -eq $match -or $match.Matches.Count -eq 0) { return -1 }
    return [int]$match.Matches[0].Groups[1].Value
}

try {
    $auditExit = Invoke-Companion @("--ability-audit=$reportPath", "--ability-coverage=$coveragePath")
}
catch {
    Write-Error "ability audit run failed: $_"
    exit 1
}
if ($auditExit -ne 0) {
    Write-Error "ability audit exited with code $auditExit"
    exit 1
}
if (-not (Test-Path -LiteralPath $reportPath)) {
    Write-Error "ability audit did not write $reportPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $coveragePath)) {
    Write-Error "ability audit did not write $coveragePath"
    exit 1
}

Select-String -Path $reportPath -Pattern '^\*\*Violations:', '^Warnings:' |
    ForEach-Object { Write-Output $_.Line }

$violations = Get-ReportCount '^\*\*Violations: (\d+)\*\*'
$warnings = Get-ReportCount '^Warnings: (\d+)'
$coverage = Get-Content -LiteralPath $coveragePath -Raw | ConvertFrom-Json
Write-Output ("coverage: discovered={0} registered={1} automatable={2} companion={3} delegated={4} shared={5} manual={6} researchPending={7} liveUnverified={8}" -f `
    $coverage.discovered, $coverage.registered, $coverage.automatable, $coverage.companionGenerated, `
    $coverage.maxDpsDelegated, $coverage.sharedGated, $coverage.manual, $coverage.researchPending, $coverage.liveUnverified)

$auditClean = $true
if ($violations -lt 0 -or $warnings -lt 0) {
    Write-Output "audit: could not parse the report summary lines (violations=$violations warnings=$warnings)"
    $auditClean = $false
}
elseif ($violations -ne 0 -or $warnings -ne 0) {
    Write-Output "audit: NOT CLEAN (violations=$violations warnings=$warnings; both must be 0)"
    $auditClean = $false
}
if (-not $coverage.clean -or $coverage.missing -ne 0 -or $coverage.stale -ne 0) {
    Write-Output "coverage: NOT CLEAN (clean=$($coverage.clean) missing=$($coverage.missing) stale=$($coverage.stale))"
    $auditClean = $false
}
if (-not $auditClean) { exit 3 }
Write-Output "audit: clean (violations 0, warnings 0, missing 0, stale 0)"

# ---- 2. Addon catalog drift check -------------------------------------------
$exitCode = 0
if (-not $SkipCatalogCheck) {
    if (-not (Test-Path -LiteralPath $addonCatalog)) {
        Write-Output "catalog: addon file missing ($addonCatalog)"
        $exitCode = 2
    }
    else {
        $genPath = Join-Path ([System.IO.Path]::GetTempPath()) ("mdb-catalog-{0}.lua" -f [guid]::NewGuid().ToString('N'))
        try {
            $genExit = Invoke-Companion @("--gen-catalog=$genPath")
            if ($genExit -ne 0) {
                Write-Error "--gen-catalog exited with code $genExit"
                exit 1
            }
            $drift = Compare-Object -ReferenceObject (Get-Content -LiteralPath $addonCatalog) `
                                    -DifferenceObject (Get-Content -LiteralPath $genPath)
            if ($null -eq $drift) {
                Write-Output "catalog: committed addon Catalog.lua matches the generated output"
            }
            else {
                Write-Output "catalog: DRIFT (addon vs generated) - regenerate addon/MaxDpsBridge/Catalog.lua"
                foreach ($line in $drift) {
                    $side = if ($line.SideIndicator -eq '<=') { 'addon     ' } else { 'generated ' }
                    Write-Output ("  {0}{1}" -f $side, $line.InputObject)
                }
                $exitCode = 2
            }
        }
        finally {
            Remove-Item -LiteralPath $genPath -Force -ErrorAction SilentlyContinue
        }
    }
}

exit $exitCode
