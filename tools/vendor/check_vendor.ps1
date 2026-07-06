Write-Host "=== Vendor / Offline check (RF5) ===" -ForegroundColor Cyan

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$assetRoot = Join-Path $repoRoot "src\NovaFiches\assets"
if (!(Test-Path $assetRoot)) {
  $assetRoot = Join-Path $repoRoot "assets"
}

# 1) Block any external URLs in assets (offline guarantee)
$bad = Get-ChildItem $assetRoot -Recurse -Include *.js,*.html -ErrorAction SilentlyContinue |
       Where-Object { $_.FullName -notmatch "\\vendor\\" } |
       Select-String -Pattern "http://|https://"

if ($bad) {
  Write-Host "ERROR: External URL detected in assets." -ForegroundColor Red
  $bad | ForEach-Object { Write-Host ("  - " + $_.Path) }
  exit 1
}

# 2) Check VERSIONS.json exists
$versions = Join-Path $assetRoot "vendor\VERSIONS.json"
if (!(Test-Path $versions)) {
  Write-Host "ERROR: $versions missing." -ForegroundColor Red
  exit 1
}

# 3) Verify vendor files exist (and optional SHA256 match)
try {
  $json = Get-Content $versions -Raw | ConvertFrom-Json
  foreach ($k in $json.files.PSObject.Properties.Name) {
    $p = Join-Path (Join-Path $assetRoot "vendor") $k
    if (!(Test-Path $p)) {
      Write-Host "ERROR: Missing vendor file: $p" -ForegroundColor Red
      exit 1
    }
    $expected = $json.files.$k
    if ($expected -and $expected.Length -gt 0) {
      $actual = (Get-FileHash $p -Algorithm SHA256).Hash.ToLower()
      if ($actual -ne $expected.ToLower()) {
        Write-Host "ERROR: Vendor hash differs: $k" -ForegroundColor Red
        Write-Host "   expected: $expected"
        Write-Host "   actual  : $actual"
        exit 1
      }
    }
  }
} catch {
  Write-Host "ERROR: Unable to read VERSIONS.json: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}

Write-Host "Vendor OK - offline guaranteed" -ForegroundColor Green
exit 0
