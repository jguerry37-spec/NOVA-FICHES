param(
  [string]$ProjectRoot = (Get-Location).Path,
  [string]$PublishDir = ".\bin\Release\net8.0-windows\win-x64\publish"
)

Write-Host "=== Nova-Fiches Preflight (RF6) ===" -ForegroundColor Cyan
Set-Location $ProjectRoot

# 1) RF4 Smoke checks
Write-Host "`n[1/3] Smoke checks (RF4)..." -ForegroundColor Yellow
powershell -ExecutionPolicy Bypass -File .\tools\smoke\smoke_check.ps1
if ($LASTEXITCODE -ne 0) { exit 1 }

# 2) RF5 Vendor/offline checks
Write-Host "`n[2/3] Vendor/offline checks (RF5)..." -ForegroundColor Yellow
powershell -ExecutionPolicy Bypass -File .\tools\vendor\check_vendor.ps1
if ($LASTEXITCODE -ne 0) { exit 1 }

# 3) Publish hygiene + source/publish assets coherence
Write-Host "`n[3/3] Publish hygiene..." -ForegroundColor Yellow

if (!(Test-Path $PublishDir)) {
  Write-Host "INFO: Publish folder absent -> OK before publish." -ForegroundColor DarkYellow
  Write-Host "`nPRE-FLIGHT OK (RF6)" -ForegroundColor Green
  exit 0
}

# 3a) refuse bin/obj/.vs inside publish
$badDirs = Get-ChildItem $PublishDir -Recurse -Directory -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -in @("bin","obj",".vs") }
if ($badDirs) {
  Write-Host "ERROR: Publish contains bin/obj/.vs directories." -ForegroundColor Red
  $badDirs | ForEach-Object { Write-Host ("  - " + $_.FullName) }
  exit 1
}

# 3b) require assets
$pubAssets = Join-Path $PublishDir "assets"
if (!(Test-Path $pubAssets)) {
  Write-Host "ERROR: Publish has no assets folder." -ForegroundColor Red
  exit 1
}

# 3c) refuse external urls inside publish assets
$urls = Get-ChildItem $pubAssets -Recurse -Include *.js,*.html -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "\\vendor\\" } |
        Select-String -Pattern "http://|https://"
if ($urls) {
  Write-Host "ERROR: External URL detected in publish/assets." -ForegroundColor Red
  $urls | ForEach-Object { Write-Host ("  - " + $_.Path) }
  exit 1
}

# 3d) coherence check (source assets vs publish assets) on common files
$srcAssets = ".\src\NovaFiches\assets"
if (Test-Path $srcAssets) {

  function Get-HashMap($root) {
    $dict = @{}
    $files = Get-ChildItem $root -Recurse -File -ErrorAction SilentlyContinue |
             Where-Object { $_.Name -notmatch "\.(pdb|map)$" }
    foreach ($f in $files) {
      $rel = $f.FullName.Substring($root.Length).TrimStart([char[]]@('\','/'))
      $dict[$rel] = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
    }
    return $dict
  }

  $src = Get-HashMap $srcAssets
  $pub = Get-HashMap $pubAssets

  $mismatch = @()
  foreach ($k in $src.Keys) {
    if ($pub.ContainsKey($k) -and $pub[$k] -ne $src[$k]) { $mismatch += $k }
  }

  if ($mismatch.Count -gt 0) {
    Write-Host "ERROR: Source assets differ from publish assets." -ForegroundColor Red
    $mismatch | Select-Object -First 30 | ForEach-Object { Write-Host ("  - " + $_) }
    Write-Host "Solution: delete publish/assets and run dotnet publish again." -ForegroundColor DarkYellow
    exit 1
  }
}

Write-Host "Publish hygiene OK" -ForegroundColor Green
Write-Host "`nPRE-FLIGHT OK (RF6)" -ForegroundColor Green
exit 0
