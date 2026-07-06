# Audit Nova-Fiches (2.2.0.13) → Clean 2.2.0.14

Objectif : supprimer les sources de confusion (« build pas bon »), réduire les doublons à risque, et fiabiliser la chaîne **src → publish → MSI**.

## 1) Constat principal : le dossier publish ne doit pas être une source
`src/NovaFiches/build.ps1` supprime puis régénère `Installer/staging/publish` via `dotnet publish`.
Conséquence : toute modification faite « à la main » dans `Installer/staging/publish` est perdue au build et donne l’impression d’un comportement aléatoire.

**Action appliquée :**
- `Installer/staging/publish` est maintenant traité comme **dossier généré** (README_GENERATED.txt).

## 2) Fiabilisation : assets/vendor inclus au publish (point critique)
En 2.2.0.13, les items `assets\**\*.*` et `vendor\**\*.*` n’étaient copiés qu’au runtime (Output) via `CopyToOutputDirectory`.
Selon le SDK et les options, ils peuvent ne pas être inclus correctement dans le publish.

**Action appliquée (2.2.0.14) :**
- Ajout `CopyToPublishDirectory=PreserveNewest` pour :
  - `assets\**\*.*`
  - `vendor\**\*.*`

Résultat : les modules JS (dont `parseLandXmlLeica`) sont toujours présents dans `Installer\staging\publish` après build.

## 3) Versionning
- Version bump : 2.2.0.14 (Version/AssemblyVersion/FileVersion/InformationalVersion)
- `build.ps1` produit déjà `build_info.txt` dans le publish : conservé.

## 4) Doublons / nettoyage recommandé (non destructif)
Pour éviter une régression fonctionnelle, ce package ne supprime pas de code métier.
Mais les points suivants sont des candidats évidents pour une passe de refacto “propre” :

- **Constante prisme** : double champ `constPrisme` vs `prismConst`.
  - Reco : choisir un champ canonique, garder l’autre comme alias.
- **Multiples voies de parsing Leica** (meta obs / obs normales / resection).
  - Reco : centraliser un dictionnaire `pointId → reflectorConstant` une seule fois, puis consommer partout.
- **Scripts utilitaires à la racine** : BUILD_/VERIFY_/CLEAN_.
  - Reco : regrouper dans `/tools` + documenter le parcours standard.

## 5) Checklist de validation (à chaque build)
1. Purge WebView2 :
   `Remove-Item "$env:LOCALAPPDATA\NOVATLAS\Nova-Fiches\WebView2" -Recurse -Force -ErrorAction SilentlyContinue`
2. Build :
   `cd .\src\NovaFiches ; .\build.ps1 -Configuration Release`
3. Vérifier existence dans `Installer\staging\publish` :
   - `assets\topo_app.html`
   - `assets\app\modules\m02_parser_calc.js`
4. DevTools Console :
   - `typeof window.parseLandXmlLeica` → `"function"`
