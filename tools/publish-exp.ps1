#requires -version 7
<#
P3 publish for the WinUI 3 companion (docs/plans/2026-10-11-winui3-v4-migration.md §1.2).

Publishes app/MaxDpsCompanion to dist-exp as unpackaged, framework-dependent
WinUI 3 (WindowsPackageType=None + EnableMsixTooling=true — the pairing is
required so the output carries the XAML resource index, MaxDpsCompanion.pri;
without it the app dies 0xC000027B XamlParseException).

Guarantees:
  * dist-exp\settings.ini is never deleted or rewritten (per-machine file;
    hash-checked before/after).
  * dist-exp\mdc-app.exe / mdc-cli.exe belong to the Rust workstream: hash-checked.
  * writes dist-exp\VERSION.txt from the BUILT exe metadata (FileVersion) plus
    the csproj <Codename> — no hardcoded version string to drift.
  * asserts MaxDpsCompanion.exe + MaxDpsCompanion.pri are present afterwards.
Legacy build.ps1 and dist\ are untouched (frozen 3.7.9 WinForms client).

Run from anywhere:  pwsh -File tools/publish-exp.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'app/MaxDpsCompanion/MaxDpsCompanion.csproj'
$outDir = Join-Path $root 'dist-exp'
$settingsIni = Join-Path $outDir 'settings.ini'
$versionFile = Join-Path $outDir 'VERSION.txt'
$rustExes = @('mdc-app.exe', 'mdc-cli.exe') | ForEach-Object { Join-Path $outDir $_ }
# One-exe rule: the publish must never emit a second top-level exe. Framework-
# dependent output only carries MaxDpsCompanion.exe; RestartAgent.exe appears
# only from a self-contained publish, so its presence is a hard failure.
$restartAgent = Join-Path $outDir 'RestartAgent.exe'

function Get-OptionalHash([string]$Path) {
    if (Test-Path -LiteralPath $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
    return $null
}

if (-not (Test-Path -LiteralPath $project)) { throw "project not found: $project" }
if (-not (Test-Path -LiteralPath $outDir)) { throw "output dir not found: $outDir (create it / check the checkout)" }

# One-exe rule pre-flight: a stale dist-exp\RestartAgent.exe (e.g. from an
# earlier self-contained publish) would survive into the new output and break
# the one-exe rule, so refuse to publish until it is removed. Checked again
# after the publish in case the publish itself ever emits one.
if (Test-Path -LiteralPath $restartAgent) {
    throw "dist-exp\RestartAgent.exe already exists — the one-exe rule forbids a second top-level exe. Delete it, then re-run: $restartAgent"
}

# Pre-flight state (settings.ini + the Rust exes must survive the publish).
$settingsBefore = Get-OptionalHash $settingsIni
$rustBefore = @{}
foreach ($exe in $rustExes) { $rustBefore[$exe] = Get-OptionalHash $exe }

Write-Host "publish-exp: dotnet publish -> dist-exp (unpackaged WinUI 3, framework-dependent)"
$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'false',
    '-p:Platform=x64',
    '-p:EnableMsixTooling=true',
    '-p:WindowsPackageType=None',
    '-p:PublishSingleFile=false',
    '-o', $outDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Assertions on the publish output.
$exe = Join-Path $outDir 'MaxDpsCompanion.exe'
$pri = Join-Path $outDir 'MaxDpsCompanion.pri'
if (-not (Test-Path -LiteralPath $exe)) { throw "missing after publish: $exe" }
if (-not (Test-Path -LiteralPath $pri)) { throw "missing after publish: $pri (EnableMsixTooling/WindowsPackageType pairing broken?)" }
if (Test-Path -LiteralPath $restartAgent) {
    throw "dist-exp\RestartAgent.exe appeared after publish — a second top-level exe breaks the one-exe rule (self-contained publish?): $restartAgent"
}

# settings.ini and the Rust exes must be unchanged (never deleted/replaced).
$settingsAfter = Get-OptionalHash $settingsIni
if ($settingsBefore -ne $settingsAfter) {
    throw "dist-exp\settings.ini changed across publish (before=$settingsBefore after=$settingsAfter)"
}
foreach ($manifest in $rustBefore.GetEnumerator()) {
    $after = Get-OptionalHash $manifest.Key
    if ($manifest.Value -ne $after) {
        throw "dist-exp\$(Split-Path -Leaf $manifest.Key) changed across publish (before=$($manifest.Value) after=$after)"
    }
}
if ($null -eq $settingsBefore) { Write-Warning 'dist-exp\settings.ini not present (nothing deleted; it is created on first app run)' }

# VERSION.txt is derived from the JUST-BUILT exe metadata (FileVersion,
# e.g. 4.0.2.0 -> "4.0.2") plus the csproj <Codename>; nothing hardcodes it,
# so the stamp can never drift from the binary it describes.
$fileVersionRaw = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($fileVersionRaw)) { throw "cannot read FileVersion from $exe" }
$versionLabel = ($fileVersionRaw -split '\.')[0..2] -join '.'
[xml]$csprojXml = Get-Content -LiteralPath $project -Raw
$codename = @($csprojXml.Project.PropertyGroup.Codename | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })[0]
if ([string]::IsNullOrWhiteSpace($codename)) { throw "csproj <Codename> not found in $project" }

Set-Content -LiteralPath $versionFile -Value "$versionLabel $codename" -Encoding utf8

Write-Host "publish-exp: OK"
Write-Host "  exe        : $exe (FileVersion $fileVersionRaw)"
Write-Host "  pri        : $pri"
Write-Host "  VERSION.txt: $versionLabel $codename"
if ($settingsBefore) { Write-Host '  settings.ini: unchanged (hash match)' }
