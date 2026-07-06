# Nova-Fiches one-liner build (Release) - no copy/paste of console transcripts needed.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "== Nova-Fiches: BUILD_RELEASE =="

# Build from src\NovaFiches using existing build.ps1
Set-Location (Join-Path $root "src\NovaFiches")
.\build.ps1 -Configuration Release
Write-Host "== Build completed. Publish output: Installer\staging\publish =="
