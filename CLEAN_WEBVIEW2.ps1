# Purge WebView2 cache for Nova-Fiches
Set-StrictMode -Version Latest
$ErrorActionPreference = "SilentlyContinue"

$dir = Join-Path $env:LOCALAPPDATA "NOVATLAS\Nova-Fiches\WebView2"
Write-Host "Deleting: $dir"
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Done."
