<#
.SYNOPSIS
    Dev-time asset pipeline for MaxDpsBridgeExp: downloads pinned upstream
    SVGs/fonts, rasterizes them, and writes validated 32-bit top-left TGA
    icons into addon/MaxDpsBridgeExp/Exp/Assets.

.DESCRIPTION
    OFFLINE-SAFE STUB. Nothing here runs at build time or at addon runtime;
    the installed addon ships the committed .tga files and never downloads.
    This script is run by a maintainer to (re)generate those files.

    Every upstream file is pinned to a tag/commit and verified by SHA256.
    Pins live in tools/asset-pins.json. On the very first use run
    -RecordPins, review the printed hashes, and commit the file; later runs
    install only bytes that match a committed pin.

    TGA contract (see assets/LICENSES.md):
      image type 2 (uncompressed), 32-bit BGRA, power-of-two 64/128/256,
      max 256, top-left origin (descriptor bit 0x20).

.PARAMETER Help
    Print this help and exit (no work).

.PARAMETER RepoRoot
    Repository root. Defaults to the parent of this script's folder.

.PARAMETER MagickPath
    Explicit path to ImageMagick 7 'magick'. Defaults to PATH lookup.

.PARAMETER IncludeFonts
    Also stage the verified Inter/JetBrainsMono TTFs into the companion's
    app/MaxDpsCompanion/assets/fonts. Off by default (download+verify only).

.PARAMETER RecordPins
    Download every upstream file, print its SHA256, write
    tools/asset-pins.json, and install nothing. Run once, review, commit.

.PARAMETER TrustFirstUse
    If a pin is missing from tools/asset-pins.json, use the observed hash
    for this run. Only for a maintainer bootstrapping a new asset.

.PARAMETER Force
    Re-download even when the existing pin already matches.

.EXAMPLE
    pwsh -File tools/fetch_assets.ps1 -WhatIf
    pwsh -File tools/fetch_assets.ps1
    pwsh -File tools/fetch_assets.ps1 -RecordPins
    pwsh -File tools/fetch_assets.ps1 -IncludeFonts
#>
#requires -version 7
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [switch]$Help,
    [string]$RepoRoot,
    [string]$MagickPath,
    [switch]$IncludeFonts,
    [switch]$RecordPins,
    [switch]$TrustFirstUse,
    [switch]$Force
)

if ($Help) {
    Get-Help -Full $PSCommandPath
    return
}

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
if (-not (Test-Path -LiteralPath $RepoRoot)) { throw "RepoRoot not found: $RepoRoot" }

$assetsOut  = Join-Path $RepoRoot 'addon\MaxDpsBridgeExp\Exp\Assets'
$fontsOut   = Join-Path $RepoRoot 'app\MaxDpsCompanion\assets\fonts'
$pinsFile   = Join-Path $PSScriptRoot 'asset-pins.json'
$tempRoot   = Join-Path ([System.IO.Path]::GetTempPath()) ("mdbx-assets-{0}" -f [guid]::NewGuid().ToString('N'))

# --- Pinned upstream manifest -------------------------------------------------
# SHA256 pins live in asset-pins.json (key = asset Name). Empty = unpinned.
$lucideBase   = 'https://raw.githubusercontent.com/lucide-icons/lucide/v0.469.0/icons'
$phosphorBase = 'https://raw.githubusercontent.com/phosphor-icons/core/v2.1.1/assets/regular'
$interBase    = 'https://github.com/rsms/inter/releases/download/v4.1'
$jbBase       = 'https://github.com/JetBrains/JetBrainsMono/releases/download/v2.304'

$icons = @(
    [pscustomobject]@{ Name = 'main';          Set = 'lucide'; File = 'swords.svg' }
    [pscustomobject]@{ Name = 'offensive';     Set = 'lucide'; File = 'sword.svg' }
    [pscustomobject]@{ Name = 'defensive';     Set = 'lucide'; File = 'shield.svg' }
    [pscustomobject]@{ Name = 'consumable';    Set = 'lucide'; File = 'flask-conical.svg' }
    [pscustomobject]@{ Name = 'trinket';       Set = 'lucide'; File = 'gem.svg' }
    [pscustomobject]@{ Name = 'interrupt';     Set = 'lucide'; File = 'octagon-x.svg' }
    [pscustomobject]@{ Name = 'mobility';      Set = 'lucide'; File = 'wind.svg' }
    [pscustomobject]@{ Name = 'selfheal';      Set = 'lucide'; File = 'heart-pulse.svg' }
    [pscustomobject]@{ Name = 'solo';          Set = 'lucide'; File = 'user.svg' }
    [pscustomobject]@{ Name = 'ooc';           Set = 'lucide'; File = 'moon.svg' }
    [pscustomobject]@{ Name = 'autotarget';    Set = 'lucide'; File = 'crosshair.svg' }
    [pscustomobject]@{ Name = 'autointeract';  Set = 'lucide'; File = 'mouse-pointer-click.svg' }
    [pscustomobject]@{ Name = 'ttk';           Set = 'lucide'; File = 'hourglass.svg' }
    [pscustomobject]@{ Name = 'cc';            Set = 'lucide'; File = 'snowflake.svg' }
    [pscustomobject]@{ Name = 'lock';          Set = 'lucide'; File = 'lock.svg' }
    [pscustomobject]@{ Name = 'unlock';        Set = 'lucide'; File = 'lock-open.svg' }
    [pscustomobject]@{ Name = 'gear';          Set = 'lucide'; File = 'settings.svg' }
    [pscustomobject]@{ Name = 'pause';         Set = 'lucide'; File = 'pause.svg' }
    [pscustomobject]@{ Name = 'play';          Set = 'lucide'; File = 'play.svg' }
    [pscustomobject]@{ Name = 'stop';          Set = 'lucide'; File = 'square.svg' }
    [pscustomobject]@{ Name = 'crosshair';     Set = 'lucide'; File = 'crosshair.svg' }
)

$fonts = @(
    [pscustomobject]@{ Name = 'Inter-Regular.ttf';        Url = "$interBase/Inter-Regular.ttf" }
    [pscustomobject]@{ Name = 'Inter-Bold.ttf';           Url = "$interBase/Inter-Bold.ttf" }
    [pscustomobject]@{ Name = 'JetBrainsMono-Regular.ttf'; Url = "$jbBase/JetBrainsMono-Regular.ttf" }
    [pscustomobject]@{ Name = 'JetBrainsMono-Bold.ttf';    Url = "$jbBase/JetBrainsMono-Bold.ttf" }
)

function Get-AssetUrl {
    param([string]$Set, [string]$File)
    switch ($Set) {
        'lucide'   { return "$lucideBase/$File" }
        'phosphor' { return "$phosphorBase/$File" }
        default    { throw "Unknown icon set '$Set'" }
    }
}

function Get-Pins {
    if (Test-Path -LiteralPath $pinsFile) {
        $raw = Get-Content -LiteralPath $pinsFile -Raw | ConvertFrom-Json
        $map = @{}
        foreach ($p in $raw.PSObject.Properties) { $map[$p.Name] = [string]$p.Value }
        return $map
    }
    return @{}
}

function Resolve-Magick {
    param([string]$Explicit)
    if ($Explicit) {
        if (Test-Path -LiteralPath $Explicit) { return $Explicit }
        throw "MagickPath not found: $Explicit"
    }
    foreach ($candidate in @('magick', 'magick.exe')) {
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    throw "ImageMagick 7 ('magick') not found on PATH. Install it or pass -MagickPath."
}

function Invoke-Download {
    param([string]$Url, [string]$Out)
    Invoke-WebRequest -Uri $Url -OutFile $Out -TimeoutSec 60
}

function Assert-Pin {
    param([string]$Path, [string]$Pin, [string]$Name, [hashtable]$Seen)
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    $Seen[$Name] = $hash
    if (-not $Pin) {
        if (-not $TrustFirstUse -and -not $RecordPins) {
            throw "No committed pin for '$Name' (observed $hash). Run -RecordPins once, review, and commit tools/asset-pins.json."
        }
        Write-Output "unpinned $Name -> $hash"
        return
    }
    if ($hash -ne $Pin.ToUpperInvariant()) {
        throw "Hash mismatch for ${Name}: got $hash, pin $Pin. Refusing to install."
    }
}

# --- TGA helpers --------------------------------------------------------------
function Get-TgaHeader {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 18) { throw "TGA too short: $Path" }
    [pscustomobject]@{
        ImageType  = [int]$bytes[2]
        Width      = [int][BitConverter]::ToUInt16($bytes, 12)
        Height     = [int][BitConverter]::ToUInt16($bytes, 14)
        PixelDepth = [int]$bytes[16]
        Descriptor = [int]$bytes[17]
        Length     = $bytes.Length
    }
}

function Assert-Tga {
    param([string]$Path, [int[]]$AllowedSizes)
    $h = Get-TgaHeader -Path $Path
    if ($h.ImageType -ne 2)  { throw "TGA $Path is not uncompressed true-color (type $($h.ImageType))." }
    if ($h.PixelDepth -ne 32) { throw "TGA $Path is not 32-bit (depth $($h.PixelDepth))." }
    if ($h.Width -ne $h.Height) { throw "TGA $Path is not square ($($h.Width)x$($h.Height))." }
    if ($AllowedSizes -notcontains $h.Width) { throw "TGA $Path size $($h.Width) not in $($AllowedSizes -join '/')." }
    if ($h.Width -gt 256) { throw "TGA $Path is larger than 256." }
    if (($h.Descriptor -band 0x20) -eq 0) { throw "TGA $Path has no top-left origin bit (descriptor 0x$('{0:X2}' -f $h.Descriptor))." }
    Write-Output "valid    $(Split-Path -Leaf $Path) ($($h.Width)x$($h.Height) type2 32-bit top-left)"
}

function Set-TgaTopLeft {
    param([string]$Path)
    $h = Get-TgaHeader -Path $Path
    if (($h.Descriptor -band 0x20) -ne 0) { return }
    $bytes  = [System.IO.File]::ReadAllBytes($Path)
    $bpp    = 4
    $stride = $h.Width * $bpp
    $offset = 18
    $out    = [byte[]]::new($bytes.Length)
    [Array]::Copy($bytes, 0, $out, 0, 18)
    for ($y = 0; $y -lt $h.Height; $y++) {
        $src = $offset + ($h.Height - 1 - $y) * $stride
        $dst = $offset + $y * $stride
        [Array]::Copy($bytes, $src, $out, $dst, $stride)
    }
    $out[17] = [byte]($h.Descriptor -bor 0x20)
    [System.IO.File]::WriteAllBytes($Path, $out)
    Write-Output "origin   flipped $(Split-Path -Leaf $Path) to top-left"
}

function Convert-SvgToPng {
    param([string]$Magick, [string]$Svg, [string]$Png, [int]$Size)
    & $Magick $Svg -background none -density 384 -resize "${Size}x${Size}" -define png:color-type=6 "PNG32:$Png"
    if ($LASTEXITCODE -ne 0) { throw "magick SVG rasterization failed for $Svg at $Size." }
}

function Convert-PngToTga {
    param([string]$Magick, [string]$Png, [string]$Tga)
    & $Magick $Png -depth 8 -define tga:bits-per-pixel=32 -define tga:rle=false "TGA:$Tga"
    if ($LASTEXITCODE -ne 0) { throw "magick TGA conversion failed for $Png." }
}

# --- Run ----------------------------------------------------------------------
$sizes = @(64, 128)
$pins = Get-Pins
$seen = @{}
$magick = $null

Write-Output "MaxDpsBridgeExp asset fetch (dev-time; addon never downloads)"
Write-Output "  repo:   $RepoRoot"
Write-Output "  output: $assetsOut"
Write-Output "  pins:   $pinsFile"
if ($WhatIfPreference) { Write-Output "  mode:   -WhatIf (no files will be written)" }

if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
if (-not $WhatIfPreference) { New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null }

try {
    if (-not (Test-Path -LiteralPath $assetsOut)) {
        $assetsParent = Split-Path -Parent $assetsOut
        if (-not (Test-Path -LiteralPath $assetsParent)) {
            if ($PSCmdlet.ShouldProcess($assetsParent, 'create parent directory')) {
                New-Item -ItemType Directory -Path $assetsParent -Force | Out-Null
            }
        }
        if ($PSCmdlet.ShouldProcess($assetsOut, 'create asset directory')) {
            New-Item -ItemType Directory -Path $assetsOut -Force | Out-Null
        }
    }

    # Under -WhatIf nothing is written, so image tooling is not required.
    if (-not $WhatIfPreference) { $magick = Resolve-Magick -Explicit $MagickPath }

    foreach ($icon in $icons) {
        $url  = Get-AssetUrl -Set $icon.Set -File $icon.File
        $svg  = Join-Path $tempRoot "$($icon.Name).svg"
        if (-not $PSCmdlet.ShouldProcess($url, "download and build '$($icon.Name)'")) { continue }

        Invoke-Download -Url $url -Out $svg
        Assert-Pin -Path $svg -Pin $pins[$icon.Name] -Name $icon.Name -Seen $seen

        foreach ($size in $sizes) {
            $png = Join-Path $tempRoot "$($icon.Name)-$size.png"
            $tga = Join-Path $assetsOut "$($icon.Name)-$size.tga"
            Convert-SvgToPng -Magick $magick -Svg $svg -Png $png -Size $size
            Convert-PngToTga -Magick $magick -Png $png -Tga $tga
            Set-TgaTopLeft -Path $tga
            Assert-Tga -Path $tga -AllowedSizes @(64, 128, 256)
        }
    }

    foreach ($font in $fonts) {
        $ttf = Join-Path $tempRoot $font.Name
        if (-not $PSCmdlet.ShouldProcess($font.Url, "download font '$($font.Name)'")) { continue }
        Invoke-Download -Url $font.Url -Out $ttf
        Assert-Pin -Path $ttf -Pin $pins[$font.Name] -Name $font.Name -Seen $seen
        if ($IncludeFonts) {
            $dest = Join-Path $fontsOut $font.Name
            if (-not (Test-Path -LiteralPath (Split-Path -Parent $dest))) {
                New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
            }
            Copy-Item -LiteralPath $ttf -Destination $dest -Force
            Write-Output "staged   $($font.Name)"
        }
    }

    if ($RecordPins) {
        if ($PSCmdlet.ShouldProcess($pinsFile, 'write asset pins')) {
            $record = [ordered]@{}
            foreach ($k in ($seen.Keys | Sort-Object)) { $record[$k] = $seen[$k] }
            ($record | ConvertTo-Json) | Set-Content -LiteralPath $pinsFile -Encoding utf8
            Write-Output "pins     wrote $($seen.Count) hashes to $pinsFile (review + commit)"
        }
    }
}
finally {
    if (-not $WhatIfPreference -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Output "Done. Commit addon/MaxDpsBridgeExp/Exp/Assets/*.tga and, on first run, tools/asset-pins.json."
