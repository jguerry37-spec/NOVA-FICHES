param(
  [ValidateSet("Release","Debug")]
  [string]$Configuration = "Release",

  # Optionnel : version injectée (ex: 1.0.2). N'écrit pas dans le csproj.
  [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "TopoRapportWin.csproj"
$outDir  = Join-Path $PSScriptRoot "..\..\Installer\staging\publish"
$assetsHtml = Join-Path $outDir "assets\topo_app.html"

Write-Host "== Nova-Fiches build =="

if (-not (Test-Path $project)) {
  throw "CSProj introuvable: $project"
}

# Propriétés MSBuild (sans modifier le csproj)
$msbuildProps = @()
$verLine = ""

if ($Version.Trim().Length -gt 0) {
  Write-Host "== Inject version: $Version =="

  # AssemblyVersion/FileVersion must have 4 numeric parts max.
  $parts = $Version.Split('.')
  if ($parts.Length -ge 4) {
    $assemblyVer = ($parts[0..3] -join '.')
    $fileVer     = ($parts[0..3] -join '.')
  } elseif ($parts.Length -eq 3) {
    $assemblyVer = "$Version.0"
    $fileVer     = "$Version.0"
  } elseif ($parts.Length -eq 2) {
    $assemblyVer = "$Version.0.0"
    $fileVer     = "$Version.0.0"
  } else {
    $assemblyVer = "$Version.0.0.0"
    $fileVer     = "$Version.0.0.0"
  }

  $msbuildProps += "-p:Version=$Version"
  $msbuildProps += "-p:InformationalVersion=$Version"
  $msbuildProps += "-p:AssemblyVersion=$assemblyVer"
  $msbuildProps += "-p:FileVersion=$fileVer"
  $verLine = $Version
}

Write-Host "== Clean publish dir =="
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

Write-Host "== Publish ($Configuration, win-x64, self-contained) =="
# Self-contained (runtime .NET 8 embarque dans la publication) plutot que framework-dependent :
# ni l'installeur Inno Setup ni le MSI entreprise ne verifient/installent le runtime .NET (seul
# WebView2 l'est, voir Installer\msi\NovaFiches.wxs) - un poste sans le runtime .NET installait
# donc Nova-Fiches avec succes puis echouait silencieusement au premier lancement. Self-contained
# elimine ce risque completement (plus rien a detecter ni a deployer separement), au prix d'un
# publish plus volumineux (runtime .NET embarque, ~60-150 Mo au lieu de quelques Mo).
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $outDir @msbuildProps
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish a échoué (code=$LASTEXITCODE)."
}

Write-Host "== Validate assets =="
if (-not (Test-Path $assetsHtml)) {
  throw "Asset manquant après publish: $assetsHtml`nVérifie que ton .csproj copie bien assets\**\* en publish."
}

# Petit fichier d’info pour traçabilité
$infoPath = Join-Path $outDir "build_info.txt"

if ($verLine -eq "") {
  # Essai lecture version csproj si pas injectée
  try {
    $csprojText = Get-Content $project -Raw
    if ($csprojText -match "<Version>(.*?)</Version>") { $verLine = $Matches[1] }
  } catch {}
}

@"
Nova-Fiches publish OK
Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Configuration: $Configuration
Runtime: win-x64
SelfContained: true
CSProj: $project
Version: $verLine
Output: $outDir
Assets: OK (assets\topo_app.html)
"@ | Set-Content -Path $infoPath -Encoding UTF8

Write-Host ""
Write-Host "✅ Publish OK -> $outDir"
Write-Host "Next: Build MSI in Advanced Installer (Files & Folders -> Installer\staging\publish)."
Write-Host ""

Start-Process $outDir
