<#
Nova-Fiches RF4 Smoke Check (Windows PowerShell)
Runs lightweight checks BEFORE dotnet publish / MSI packaging.

Usage:
  powershell -ExecutionPolicy Bypass -File .\tools\smoke\smoke_check.ps1
#>

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir "..\..")
$assetsApp = Join-Path $repoRoot "src\NovaFiches\assets\app"

Write-Host "Nova-Fiches RF4 smoke check" -ForegroundColor Cyan
Write-Host "RepoRoot: $repoRoot"

# 1) Node presence
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
  Write-Host "❌ Node.js not found in PATH. Install Node to run smoke checks." -ForegroundColor Red
  exit 1
}
Write-Host "✅ Node.js found: $($node.Source)" -ForegroundColor Green

# 2) Syntax check for all app JS (including modules)
$jsFiles = Get-ChildItem -Path $assetsApp -Recurse -Filter *.js | Select-Object -ExpandProperty FullName
if ($jsFiles.Count -eq 0) {
  Write-Host "❌ No JS files found under $assetsApp" -ForegroundColor Red
  exit 1
}
Write-Host "Checking syntax for $($jsFiles.Count) JS files..."
foreach ($f in $jsFiles) {
  & node --check $f | Out-Null
}
Write-Host "✅ Syntax OK for all assets/app JS" -ForegroundColor Green

# 3) Symbol + HTML checks (node script)
& node (Join-Path $repoRoot "tools\smoke\smoke_check.js")
if ($LASTEXITCODE -ne 0) {
  Write-Host "❌ Smoke check script failed." -ForegroundColor Red
  exit $LASTEXITCODE
}

Write-Host "✅ Smoke checks OK" -ForegroundColor Green
