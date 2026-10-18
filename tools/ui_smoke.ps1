#requires -version 7
<#
P2-E live UI smoke (docs/plans/2026-10-11-winui3-v4-migration.md §5/§6).

Runs the built companion's hosted smoke mode against a REAL shown WinUI
window: ShellValidation walks the visual tree (title, zero-size interactive
elements, accessible names, hero toggle count, toggle-free contract), the
Advanced/Abilities overlays are opened and closed, and RenderTargetBitmap
renders the window to ui-smoke.png with a non-blank colour-grid assertion
(ui-smoke.txt / ui-smoke-layout.txt sit next to the PNG).

Checks: process exit 0 AND ui-smoke.png exists with real content.
A finding, blank render or crash is a failure (exit 3 from the exe -> this
script throws). Run from the repository root:

  pwsh -File tools/ui_smoke.ps1                       # in-game-config shell (default)
  pwsh -File tools/ui_smoke.ps1 -ExePath <path-to-exe>
#>
[CmdletBinding()]
param(
    # Explicit companion exe; default = newest bin/Release exe.
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Resolve-CompanionExe {
    if (-not [string]::IsNullOrWhiteSpace($ExePath)) {
        if (-not (Test-Path -LiteralPath $ExePath)) { throw "ExePath not found: $ExePath" }
        return (Resolve-Path -LiteralPath $ExePath).Path
    }
    $candidates = @(
        Get-ChildItem -Path (Join-Path $root 'app/MaxDpsCompanion/bin/Release') `
            -Recurse -Filter 'MaxDpsCompanion.exe' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending
    )
    if ($candidates.Count -eq 0) { throw 'No built MaxDpsCompanion.exe under app/MaxDpsCompanion/bin/Release; build first.' }
    return $candidates[0].FullName
}

$exe = Resolve-CompanionExe
$dir = Split-Path -Parent $exe
$png = Join-Path $dir 'ui-smoke.png'
$stdout = Join-Path ([System.IO.Path]::GetTempPath()) "mdb-ui-smoke-$([guid]::NewGuid().ToString('N')).out.txt"
$stderr = Join-Path ([System.IO.Path]::GetTempPath()) "mdb-ui-smoke-$([guid]::NewGuid().ToString('N')).err.txt"

if (Test-Path -LiteralPath $png) { Remove-Item -LiteralPath $png -Force }

$arguments = @('--ui-smoke-test')

try {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr

    if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout | Write-Host }
    if ((Test-Path -LiteralPath $stderr) -and (Get-Item -LiteralPath $stderr).Length -gt 0) {
        Get-Content -LiteralPath $stderr | Write-Warning
    }

    if ($process.ExitCode -ne 0) {
        throw "ui-smoke-test failed: exit $($process.ExitCode) (see $(Join-Path $dir 'ui-smoke.txt') / ui-error.txt)"
    }
    if (-not (Test-Path -LiteralPath $png)) { throw "ui-smoke.png was not written: $png" }
    $pngFile = Get-Item -LiteralPath $png
    if ($pngFile.Length -lt 1024) { throw "ui-smoke.png looks empty: $($pngFile.Length) bytes" }

    Write-Host "ui_smoke: PASS (exit 0, $($pngFile.Length) bytes -> $png)"
}
finally {
    Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
}
