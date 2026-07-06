# Audit vérifié — Nova-Fiches v2.3.1.7

**Date :** 06 juillet 2026
**Méthode :** chaque affirmation ci-dessous a été vérifiée directement contre le code source réel
(C# + modules JS), pas seulement contre `BILAN_AUDIT_NOVA_FICHES_2.3.1.7.md` (rapport précédent,
03 juillet 2026) qui contenait des erreurs factuelles — voir `_OBSOLETE_OPTION_A/POURQUOI_OBSOLETE.md`
pour le détail de ces erreurs et pourquoi le pack de corrections qu'il avait généré ne devait pas
être intégré tel quel.

---

## 1. Ce qui était faux dans l'audit précédent

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

- Absence totale de tests automatisés touchant le vrai code (confirmé).
- `m03_pdf_reports.js` est bien monolithique (3698 lignes).
- Conversion LandXML Y X Z → X Y Z et extraction de nom générique de pieu (`T62.1` → `T62`) sont
  **correctement implémentées** dans `m02_parser_calc.js` (lignes ~243-248, ~482-485) et
  `m05_recollement_pieux.js` (lignes 82-137) — vérifié directement dans le code, pas supposé.

## 3. Corrections appliquées dans cette passe

| # | Fichier | Correction |
|---|---|---|
| 1 | `src/NovaFiches/MainForm.cs` (12 sites) | `Process.Start(...)` n'était jamais disposé après ouverture d'un fichier/dossier généré (PDF, exports, aide) → ajout de `using var process =` sur les 12 occurrences. Fuite de handle mineure mais réelle sur une session longue avec beaucoup d'exports. |
| 2 | `src/NovaFiches/DxfKmzService.cs` | `Load()` forçait l'encodage Windows-1252 sur tout DXF. Ajout d'une détection de BOM UTF-8 (`DecodeDxfText`) pour ne pas corrompre les DXF ré-exportés en UTF-8 (QGIS, LibreCAD...) contenant des accents, sans changer le comportement par défaut pour les DXF existants. |
| 3 | `src/NovaFiches/assets/app/modules/m02_parser_calc.js` | **Faille XSS réelle** : la fonction `cell()` de `tableHtml()` laissait passer tout texte brut commençant par `<span` sans échappement (heuristique de confiance basée sur le contenu de la chaîne). Un code/ID de point importé d'un fichier LandXML/TXT malveillant construit pour commencer par `<span` pouvait injecter du HTML/JS dans la vue WebView2. Remplacé par le mécanisme d'opt-in explicite déjà utilisé ailleurs (`{__html:true, value:...}`), et converti les deux badges de statut qui reposaient sur ce contournement (lignes ~1727 et ~1769) vers ce même mécanisme sûr. |
| 4 | `src/NovaFiches.Tests/` (nouveau projet) | Projet xUnit réel référençant `TopoRapportWin.csproj` et `NovaFiches.PdfSharpEngine.csproj`. 19 tests qui appellent le vrai code de production (`KmzExportService`, `DxfKmzService`, `Units`) — pas de réimplémentation. Tous verts. Voir section 4. |
| 5 | Racine du dépôt | Pack "Option A" (tests fictifs, logger redondant, fix incorrect) déplacé dans `_OBSOLETE_OPTION_A/` avec explication, pour ne pas induire un futur mainteneur en erreur. |
| 6 | Racine du dépôt | Dépôt Git initialisé (aucun n'existait avant) avec `.gitignore` adapté (bin/obj, `Installer/out`/`staging` générés, exécutables). |

## 4. Le nouveau projet de tests — ce qu'il couvre réellement

`src/NovaFiches.Tests/` (19 tests, tous verts au 06/07/2026) :

- **`KmzExportServiceTests.cs`** : détection de système de coordonnées depuis le nom de fichier et
  les métadonnées, projection WGS84 (passthrough vérifié exactement), projection Lambert-93 → WGS84
  (vérifiée par plage de cohérence géographique France métropolitaine, faute de disposer d'un outil
  de référence externe pour valider une valeur exacte au mm près — voir commentaire dans le test),
  export KMZ réel (ZIP valide + contenu KML avec le bon Placemark), rejet sur liste vide ou CRS inconnu.
- **`DxfKmzServiceTests.cs`** : parsing d'un DXF minimal réel (POINT + LINE), et surtout un test
  dédié à la régression corrigée (DXF UTF-8 avec BOM et calque accentué).
- **`UnitsTests.cs`** : conversion mm↔pt.

**Non couvert dans cette passe** (recommandation pour la suite) : `AutoCadExportService.cs`
(dépend d'un schéma JSON `stateJson`/`payloadJson` non entièrement documenté — nécessiterait de
clarifier le contrat avec le JS avant d'écrire des tests fiables), le rendu PDF (`PdfSharpEngine`,
testable mais nécessiterait une comparaison d'image ou de structure PDF, hors scope de cette passe),
et les modules JS (non testables par Node.js en l'état car ce sont des scripts globaux sans
`export`/`import` — migrer vers des modules ES6 exportés serait un prérequis si des tests JS
automatisés sont souhaités).

## 5. Recommandations restantes (non traitées dans cette passe, par priorité)

1. **Validation d'entrée côté JS** : pas de limite de taille de fichier LandXML/TXT, pas de
   validation de plage de coordonnées, pas de limite du nombre de points — risque de blocage
   navigateur/mémoire sur un fichier anormalement gros ou corrompu.
2. **`catch(_){}` silencieux dans les modules JS** (ex. `m02_parser_calc.js` ~ligne 275-351) :
   une erreur de parsing d'un point isolé échoue sans aucune trace exploitable.
3. **Refactoriser `m03_pdf_reports.js`** (3698 lignes, mélange génération PDF + calculs + UI).
4. **Vendors JS non versionnés** (jsPDF, Leaflet) : pas de `package.json`, versions figées sans
   traçabilité — impossible de corriger une vulnérabilité connue sans resnapshotter à la main.
5. **`AutoCadExportService.Sanitize`** accepte un fallback silencieux (`"NO_CHA"`) si tous les
   champs attendus sont vides/nuls côté JSON — pas d'avertissement remonté à l'utilisateur.

---

*Ce rapport remplace `BILAN_AUDIT_NOVA_FICHES_2.3.1.7.md` comme référence pour les décisions futures
sur ce dépôt. L'ancien rapport reste conservé pour historique mais ne doit plus être utilisé comme
source de vérité sans revérification.*
