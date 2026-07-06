# Pourquoi ce dossier est obsolète

Les fichiers de ce dossier constituaient un pack de corrections ("Option A") préparé le
03 juillet 2026, jamais intégré au code réel. Une vérification ligne par ligne (06 juillet 2026)
contre le code source effectif a montré que **les 3 corrections proposées sont invalides** :

## 1. `BUILD_INSTALLER_2.3.1.7_FIXED.bat` — NE PAS UTILISER

Le "bug" identifié (référence à `NovaFiches_2.3.0.100.iss` au lieu de `NovaFiches_2.3.1.7.iss`)
**n'est pas un bug**. Tous les scripts `BUILD_INSTALLER_2.3.1.x.bat` de 2.3.1.0 à 2.3.1.7 réutilisent
volontairement `NovaFiches_2.3.0.100.iss` comme template Inno Setup paramétré via
`/DMyAppVersion=%VERSION%` en ligne de commande (voir `#ifndef MyAppVersion` dans le `.iss`).
Le fichier `NovaFiches_2.3.1.7.iss` que ce script "corrigé" référence **n'existe pas sur disque**.
L'appliquer aurait cassé un script de build qui fonctionne (preuve : `Installer/out/NovaFiches_Setup_2.3.1.7.exe`
existe bel et bien, généré par le script "non corrigé").

## 2. `AppLoggerService.cs` — NE PAS INTÉGRER

Un logger centralisé existe déjà et fonctionne : `src/NovaFiches/AppLog.cs`, utilisé à plus de
50 endroits dans `MainForm.cs` et déjà branché sur les handlers d'exceptions globaux dans
`Program.cs` (`Application.ThreadException`, `AppDomain.CurrentDomain.UnhandledException`).
Intégrer `AppLoggerService.cs` créerait un second système de logging concurrent, avec un
dossier de logs différent — confusion pure pour la maintenance future.

## 3. `ValidationTests.cs`, `CoordinateTests.cs`, `PieuxTests.cs`, `ExportTests.cs` — NE PAS INTÉGRER TELS QUELS

Chacun de ces 4 fichiers définit sa propre classe privée réinventée (`CoordinateValidator`,
`CoordinateConverter`, `PieuxGrouper`, `TxtExportValidator`/`KmzExportValidator`) au lieu
d'appeler le vrai code de production. Ces 28 tests passent tous au vert par construction,
mais ne testent que leur propre logique jouet — aucune protection réelle contre une régression
du vrai logiciel (le vrai code de conversion/parsing vit dans les modules JS `m02_parser_calc.js`
et `m05_recollement_pieux.js`, et dans les services C# `KmzExportService.cs`/`DxfKmzService.cs`).

## Ce qui remplace ce pack

Voir `AUDIT_VERIFIE_2.3.1.7.md` à la racine du dépôt : audit relu et vérifié directement contre
le code réel, avec un vrai projet `src/NovaFiches.Tests` qui teste les services publics existants.
