#requires -version 7
<#
Vendors the bundled Geist typeface (SIL Open Font License) into
app/MaxDpsCompanion/assets/fonts with pinned SHA256 hashes.

Source: https://github.com/vercel/geist-font (fonts/Geist/ttf/*.ttf + OFL.txt).
The companion loads these from embedded resources (Ui/UiFonts.cs); the build
never downloads anything — this script only (re)creates the checked-in assets.

Deterministic: an existing file whose hash matches is left untouched, and a
downloaded file whose hash does not match the pin is rejected and removed.

Run from the repository root:
  pwsh -File tools/Fetch-Geist.ps1
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'app/MaxDpsCompanion/assets/fonts'
$base = 'https://raw.githubusercontent.com/vercel/geist-font/main/fonts/Geist/ttf'
$licenseUrl = 'https://raw.githubusercontent.com/vercel/geist-font/main/OFL.txt'

# Pinned content hashes (Geist v1 static TTFs, fetched 2026-09-28).
$pins = [ordered]@{
    'Geist-Regular.ttf' = '85A1C6B18A6B0A06DFE9FD4F6D6A5D4979F74EC861EAEF4BC7868B5492B8A117'
    'Geist-Medium.ttf'  = '3A3B36F0D0B981F4857F7F00EEEF4A5EE123605575D362AB31EE7C19E3D11F2F'
    'Geist-Bold.ttf'    = '3F21F02B827228C3F0C5FB230D027B5A1A263013618E8052BC1B3C5692812C88'
    'OFL.txt'           = 'C683BFBCC7E087F5D37A54EF628F10387C451A83DDC459B151403A164AC46C90'
}

if (-not (Test-Path -LiteralPath $dest)) {
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
}

$failed = 0
foreach ($name in $pins.Keys) {
    $path = Join-Path $dest $name
    $pin = $pins[$name]

    if (-not $Force -and (Test-Path -LiteralPath $path)) {
        $current = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($current -eq $pin) {
            Write-Output "ok       $name (pinned hash matches)"
            continue
        }
        Write-Output "refresh  $name (local hash differs from the pin)"
    }

    $url = if ($name -eq 'OFL.txt') { $licenseUrl } else { "$base/$name" }
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("geist-{0}-{1}" -f [guid]::NewGuid().ToString('N'), $name)
    try {
        Invoke-WebRequest -Uri $url -OutFile $temp -TimeoutSec 60
        $hash = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash
        if ($hash -ne $pin) {
            Write-Output "REJECT   $name (hash $hash does not match the pin $pin)"
            $failed++
            continue
        }
        Copy-Item -LiteralPath $temp -Destination $path -Force
        Write-Output "fetched  $name ($pin)"
    }
    catch {
        Write-Output "FAILED   ${name}: $($_.Exception.Message)"
        $failed++
    }
    finally {
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
}

if ($failed -gt 0) {
    Write-Error "$failed font asset(s) failed; the bundled Geist faces are incomplete."
    exit 1
}
Write-Output "Geist assets verified in $dest"
