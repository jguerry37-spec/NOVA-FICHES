# Audit vérifié — Nova-Fiches v2.3.1.7

**Date :** 06 juillet 2026
**Méthode :** chaque affirmation ci-dessous a été vérifiée directement contre le code source réel
(C# + modules JS), pas seulement contre un rapport d'audit antérieur qui contenait des erreurs
factuelles (voir section 1).

---

## 1. Ce qui était faux dans l'audit précédent (BILAN_AUDIT_NOVA_FICHES_2.3.1.7.md, 03/07/2026)

Ce rapport et le pack de corrections "Option A" qu'il avait généré ont été retirés du dépôt lors du
ménage du 06/07/2026 (voir section 6) — ils sont conservés dans l'historique Git si besoin de les
consulter (commit `f31be20`, baseline initiale). Ce qui avait été identifié comme faux :

- **"Bug critique" BUILD_INSTALLER** : faux. Tous les scripts `BUILD_INSTALLER_2.3.1.x.bat`
  réutilisent volontairement `NovaFiches_2.3.0.100.iss` comme template Inno Setup paramétré par
  `/DMyAppVersion=`. Le fichier `NovaFiches_2.3.1.7.iss` proposé par la "correction" n'existe pas.
- **"Pas de logging centralisé"** : faux. `src/NovaFiches/AppLog.cs` existe, est utilisé à plus de
  50 endroits dans `MainForm.cs`, et `Program.cs` a déjà des handlers globaux d'exceptions
  (`Application.ThreadException`, `AppDomain.CurrentDomain.UnhandledException`) qui logguent dessus.
- **Les 28 tests du pack "Option A"** ne testaient aucun vrai code de production : chaque fichier
  définissait sa propre classe privée réinventée (`CoordinateConverter`, `PieuxGrouper`, etc.) au lieu
  d'appeler `KmzExportService`, `DxfKmzService`, ou les modules JS réels.

## 2. Ce qui était vrai et confirmé

- Absence totale de tests automatisés touchant le vrai code (confirmé, corrigé — voir section 3).
- `m03_pdf_reports.js` était bien monolithique (3698 lignes) — découpé depuis (section 3).
- Conversion LandXML Y X Z → X Y Z et extraction de nom générique de pieu (`T62.1` → `T62`) sont
  **correctement implémentées** dans `m02_parser_calc.js` et `m05_recollement_pieux.js` — vérifié
  directement dans le code, pas supposé.

## 3. Corrections appliquées

| # | Fichier | Correction |
|---|---|---|
| 1 | `src/NovaFiches/MainForm.cs` (12 sites) | `Process.Start(...)` jamais disposé après ouverture d'un fichier/dossier généré (PDF, exports, aide) → `using var process =` sur les 12 occurrences. |
| 2 | `src/NovaFiches/DxfKmzService.cs` | `Load()` forçait Windows-1252 sur tout DXF. Détection de BOM UTF-8 (`DecodeDxfText`) pour ne pas corrompre les DXF ré-exportés en UTF-8 (QGIS, LibreCAD...) contenant des accents. |
| 3 | `src/NovaFiches/assets/app/modules/m02_parser_calc.js` | **Faille XSS réelle** : `cell()` de `tableHtml()` laissait passer tout texte brut commençant par `<span` sans échappement. Un code/ID de point importé pouvait injecter du HTML/JS. Remplacé par l'opt-in explicite `{__html:true, value:...}` partout. |
| 4 | `src/NovaFiches/assets/app/modules/m02_parser_calc.js` | Plafond d'affichage (3000 lignes) dans `tableHtml()` — un LandXML anormalement volumineux ne bloque plus la WebView2 (calcul/export non affectés, seul l'affichage est tronqué). |
| 5 | `src/NovaFiches/assets/app/modules/m02_parser_calc.js` | Détection de coordonnées implausibles (>20 000 km, NaN) après `parseLandXmlLeica`/`parseTxtLeica1200`, journalisée en `console.warn` sans modifier les données. |
| 6 | `src/NovaFiches/assets/app/modules/m02_parser_calc.js` | Remplacement de plusieurs `catch(_){}` silencieux (section "Points topo" des deux parseurs) par des `console.warn` contextualisés. |
| 7 | `src/NovaFiches/assets/app/modules/m03_pdf_reports.js` | Découpé en 4 fichiers (`m03a_pdf_reports_core.js`, `m03b_pdf_reports_zones.js`, `m03c_pdf_reports_export.js`, `m03d_pdf_reports_render.js`) — même contenu, même ordre, aucune logique modifiée. Vérifié par équilibre des accolades identique à l'original et par chargement réel en navigateur (self-check interne : toutes les fonctions principales présentes, 0 erreur console). |
| 8 | `src/NovaFiches.Tests/` (nouveau projet) | Projet xUnit réel référençant `TopoRapportWin.csproj` et `NovaFiches.PdfSharpEngine.csproj`. 19 tests qui appellent le vrai code de production (`KmzExportService`, `DxfKmzService`, `Units`) — pas de réimplémentation. Tous verts. |
| 9 | Racine du dépôt | Dépôt Git initialisé (aucun n'existait avant), `.gitignore` adapté. |

### Bug introduit puis corrigé pendant cette même session

Les garde-fous ajoutés en #5 ont introduit une **récursion infinie** (`window.parseLandXmlLeica`/
`window.parseTxtLeica1200` s'appelaient eux-mêmes via leur identifiant global, réaffecté) —
détecté au premier import réel par l'utilisateur ("Maximum call stack size exceeded"), corrigé en
capturant la référence d'origine avant réaffectation, et revérifié par appel réel (pas seulement
présence) des deux fonctions avant de considérer le correctif validé. Leçon retenue : pour ce genre
de wrapper sur une fonction globale de script classique, toujours capturer `const original = fn;`
avant `window.fn = wrapper`.

## 4. Le projet de tests — ce qu'il couvre réellement

`src/NovaFiches.Tests/` (19 tests, tous verts) :

- **`KmzExportServiceTests.cs`** : détection de système de coordonnées, projection WGS84
  (passthrough exact), projection Lambert-93 → WGS84 (cohérence géographique France métropolitaine),
  export KMZ réel (ZIP valide + contenu KML), rejet sur liste vide ou CRS inconnu.
- **`DxfKmzServiceTests.cs`** : parsing DXF minimal réel, régression UTF-8 BOM + calque accentué.
- **`UnitsTests.cs`** : conversion mm↔pt.

**Non couvert** (recommandation pour la suite) : `AutoCadExportService.cs` (schéma JSON
`stateJson`/`payloadJson` non entièrement documenté), le rendu PDF (`PdfSharpEngine`, nécessiterait
une comparaison d'image/structure), les modules JS (scripts globaux sans `export`/`import` — migrer
vers des modules ES6 serait un prérequis pour des tests Node.js).

## 5. Recommandations restantes (non traitées, par priorité)

1. Étendre la traçabilité aux ~140 autres `catch(_){}` silencieux du dossier JS (seule la section
   "Points topo" des deux parseurs a été traitée dans cette passe).
2. Vendors JS non versionnés (jsPDF, Leaflet) : pas de `package.json`, versions figées sans
   traçabilité — impossible de corriger une vulnérabilité connue sans resnapshotter à la main.
3. `AutoCadExportService.Sanitize` accepte un fallback silencieux (`"NO_CHA"`) si tous les champs
   attendus sont vides/nuls côté JSON — pas d'avertissement remonté à l'utilisateur.
4. Étendre `NovaFiches.Tests` à `AutoCadExportService` une fois le contrat JSON stabilisé/documenté.

## 6. Ménage du 06/07/2026

Supprimés (préservés dans l'historique Git, commit `f31be20` et suivants, si besoin de les
retrouver) : les 3 anciens audits (`AUDIT_2.2.0.13_to_2.2.0.14.md`, `AUDIT_BASELINE_2.2.0.25/26.md`),
`BILAN_AUDIT_NOVA_FICHES_2.3.1.7.md` (superseded, contenait des erreurs — voir section 1), le dossier
`_OBSOLETE_OPTION_A/` (pack de corrections invalide déjà expliqué et neutralisé), les scripts
`BUILD_INSTALLER_*.bat` et `.iss` de versions antérieures à 2.3.1.7 (un seul script/template actif
suffit, le `.iss` est un template partagé paramétré), `patch_infos_dossier.pl` et `_new_cartouche.txt`
(aucune référence trouvée dans le code actif), les dossiers vides `files/` et `out/` (racine), et
30 des 31 exécutables archivés dans `Installer/out/` (ne conservant que `NovaFiches_Setup_2.3.1.7.exe`
comme référence de production actuelle — décision explicite de l'utilisateur, pas automatique).
