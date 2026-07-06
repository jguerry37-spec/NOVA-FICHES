# Audit & gel de version — Nova-Fiches 2.2.0.25 (Baseline)

## Contexte
Baseline entreprise NOVATLAS.
- Version : **2.2.0.25**
- Parsing LandXML Leica : **verrouille** (pas de modification sans GO)
- Moteur PDF : **verrouille** (NovaFiches.PdfSharpEngine)

## Objectif
Figer une version stable, reproductible et "anti-regression".

---

## 1) Points valides (fonctionnels)
- Import LandXML Leica : OK
- Constante prisme : OK
- PDF Leve : OK
- PDF Station : OK
- Notice PDF accessible via bouton Aide : OK

---

## 2) Correctifs integres dans 2.2.0.25
### 2.1 Suppression des suffixes "@..." (affichage)
- IDs de points normalises a l'affichage (ex: `IPI206@4` -> `IPI206`).
- Impact : uniquement presentation (pas de changement de calcul/parsing).

### 2.2 Repartition des points d'implantation par station (multi-stations)
Regle deterministe appliquee :
- Point d'implantation -> recherche observation (TargetPoint/pntRef) -> `setupID` -> `InstrumentSetup.stationName`
- Objectif : ne plus dependre de l'ordre du XML ni d'une heuristique temps fragile.

### 2.3 Colonne "Utilise" dans Residus (Station)
- Alimentation via cle principale existante + fallback `useKind` (2D/3D) lorsque necessaire.

---

## 3) Controle versioning (build partout)
Source unique de version :
- `src/NovaFiches/TopoRapportWin.csproj`
  - `<Version>`
  - `<AssemblyVersion>`
  - `<FileVersion>`
  - `<InformationalVersion>`

Affichage UI + footer PDF :
- derive de `Application.ProductVersion` (WinForms).

---

## 4) Check-list "gel" (a refaire a chaque patch)
### 4.1 Pre-requis
- Purge cache WebView2 (recommande)
- Publish release
- Test sur 2-3 LandXML representatifs

### 4.2 Scenarios de test minimal (non-regression)
1) Import LandXML
   - stations detectees (>=1)
   - observations visibles
   - implantation visible si presente
2) PDF Station
   - 2 stations : chaque station liste ses points (aucun vol)
   - colonne "Utilise" remplie lorsque disponible
   - aucun "@" affiche
3) PDF Leve / Points topo
   - generation OK
4) Aide
   - ouverture notice PDF OK

---

## 5) Commandes (workflow NOVATLAS)
```powershell
# 1) purge cache WebView2 (evite anciens assets)
Remove-Item "$env:LOCALAPPDATA\NOVATLAS\Nova-Fiches\WebView2" -Recurse -Force -ErrorAction SilentlyContinue

# 2) publish
cd .\src\NovaFiches
.\build.ps1 -Configuration Release
```

Puis MSI via Advanced Installer (INSTALLFOLDER -> staging/publish).
