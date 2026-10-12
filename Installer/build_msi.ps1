<#
.SYNOPSIS
  Compile le paquet MSI Nova-Fiches (Installer\msi\NovaFiches.wxs) pour deploiement entreprise
  (GPO Software Installation, Intune, SCCM...).

.DESCRIPTION
  Ne republie PAS l'application : suppose que Installer\staging\publish est deja a jour pour la
  version demandee (meme etape prealable que l'installeur Inno Setup existant) :

      pwsh src\NovaFiches\build.ps1 -Configuration Release -Version 3.1.28.0
      # ou : .\BUILD_RELEASE.ps1  (equivalent, sans parametre de version explicite)

  Necessite WiX Toolset (outil dotnet global) :

      dotnet tool install --global wix

  Voir Installer\msi\README.md pour le guide de deploiement GPO complet (installation/
  desinstallation silencieuses, gestion des montees de version).

.PARAMETER Version
  Version a quatre segments (ex. 3.1.28.0) - doit correspondre a la version deja publiee dans
  Installer\staging\publish. Sert aussi de nom de fichier de sortie.

.EXAMPLE
  .\Installer\build_msi.ps1 -Version 3.1.28.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "Installer\staging\publish"
$exePath = Join-Path $publishDir "NovaFiches.exe"

if (-not (Test-Path $exePath)) {
    throw "Publish introuvable : $exePath`nLance d'abord la publication Release (src\NovaFiches\build.ps1 -Configuration Release -Version $Version, ou BUILD_RELEASE.ps1) pour peupler Installer\staging\publish avant de generer le MSI."
}

# Chemin absolu obligatoire : <Files Include> du .wxs resout un chemin relatif par rapport au
# dossier du fichier .wxs source (Installer\msi\), pas au repertoire courant du build.
$publishDir = (Resolve-Path $publishDir).Path

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "WiX Toolset introuvable dans le PATH. Installe-le avec : dotnet tool install --global wix"
}

$outDir = Join-Path $repoRoot "Installer\out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outFile = Join-Path $outDir "NovaFiches_Setup_$Version.msi"
$wxsPath = Join-Path $repoRoot "Installer\msi\NovaFiches.wxs"

Write-Host "== Nova-Fiches : build MSI (source publish = $publishDir) =="
& wix build $wxsPath -arch x64 -d "Version=$Version" -d "PublishDir=$publishDir" -out $outFile
if ($LASTEXITCODE -ne 0) {
    throw "wix build a echoue (code $LASTEXITCODE)."
}

Write-Host "== MSI genere : $outFile =="
