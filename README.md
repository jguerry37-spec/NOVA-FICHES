# Nova-Fiches â Build & packaging (baseline propre)

## Baseline validÃ©e
- Version applicative : **2.2.0.26**
- Parsing LandXML Leica : **stable (verrouillÃ©)**
- Moteur PDF (NovaFiches.PdfSharpEngine) : **stable (verrouillÃ©)**

## Stack figÃ©e
- Windows uniquement
- .NET 8 (WinForms)
- WebView2
- UI HTML/JS local offline (`src/NovaFiches/assets/topo_app.html`)
- MSI entreprise via **Advanced Installer**
- Pas de WiX / Pas de ClickOnce

## DÃ©veloppement
- Ouvrir `src/NovaFiches/TopoRapportWin.csproj`
- Lancer (F5)

## Build / Publish
Depuis `src/NovaFiches` :

```powershell
./build.ps1 -Configuration Release
```

Sortie : `src/NovaFiches/staging/publish`

## MSI (Advanced Installer)
Dans Advanced Installer :
- Files & Folders -> ajouter le contenu de `src/NovaFiches/staging/publish`
- Dossier cible : `C:\Program Files\NOVATLAS\Nova-Fiches`
- Raccourcis / icones : `packaging/`

## Versioning (IMPORTANT)
Source unique : `src/NovaFiches/TopoRapportWin.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`).

La version affichee dans l’UI et dans le footer PDF provient de `Application.ProductVersion`.

## Notes
- `src/NovaFiches/staging/` est genere (ne pas modifier a la main).
