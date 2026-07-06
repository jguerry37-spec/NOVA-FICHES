# 🚀 GUIDE INTÉGRATION - OPTION A (STABILISATION CRITIQUE)

**Durée estimée:** 1-2 jours  
**Modifications:** Bug fix + Tests + Logging + Validations  
**Version cible:** 2.4.0

---

## 📋 TABLE DES MATIÈRES

1. [Étape 0 : Préparation](#étape-0--préparation)
2. [Étape 1 : Fixer Bug BUILD_INSTALLER](#étape-1--fixer-bug-buildinstaller)
3. [Étape 2 : Ajouter Projet Tests](#étape-2--ajouter-projet-tests)
4. [Étape 3 : Ajouter Logging](#étape-3--ajouter-logging)
5. [Étape 4 : Tester & Build](#étape-4--tester--build)
6. [Checklist Finale](#checklist-finale)

---

## ✅ ÉTAPE 0 : PRÉPARATION

### Vérifications Préalables
```powershell
# Ouvrir PowerShell Admin
cd "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v"

# Vérifier que .csproj sont accessibles
ls .\src\NovaFiches\TopoRapportWin.csproj
ls .\src\NovaFiches.PdfSharpEngine\NovaFiches.PdfSharpEngine.csproj

# Vérifier .NET version
dotnet --version  # Doit être >= 8.0

# Vérifier que Inno Setup est présent
ls "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

### Créer une branche (si git)
```bash
git checkout -b feature/pro-option-a
git pull  # Sync latest
```

---

## 🔧 ÉTAPE 1 : FIXER BUG BUILD_INSTALLER

### Fichier à modifier:
```
C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\BUILD_INSTALLER_2.3.1.7.bat
```

### Changement (Ligne 8):

**AVANT ❌:**
```batch
set "ISS_FILE=%ROOT_DIR%\NovaFiches_2.3.0.100.iss"
```

**APRÈS ✅:**
```batch
set "ISS_FILE=%ROOT_DIR%\NovaFiches_2.3.1.7.iss"
```

### Vérification:
```powershell
# Afficher la ligne 8
(Get-Content "BUILD_INSTALLER_2.3.1.7.bat")[7]  # Doit afficher NovaFiches_2.3.1.7.iss
```

✅ **DONE: Bug critère corrigé !**

---

## 📦 ÉTAPE 2 : AJOUTER PROJET TESTS

### Structure à créer:
```
Nova-Fiches v/
└── src/
    └── NovaFiches.Tests/                    ← NOUVEAU
        ├── NovaFiches.Tests.csproj          ← À créer
        ├── ValidationTests.cs               ← À créer
        ├── CoordinateTests.cs               ← À créer
        ├── PieuxTests.cs                    ← À créer
        └── ExportTests.cs                   ← À créer
```

### Actions:

**2.1 Créer dossier:**
```powershell
mkdir "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches.Tests"
```

**2.2 Copier fichiers:**
```powershell
# Copier depuis les fichiers fournis:
# - NovaFiches.Tests.csproj
# - ValidationTests.cs
# - CoordinateTests.cs
# - PieuxTests.cs
# - ExportTests.cs

# Vers le nouveau dossier NovaFiches.Tests/
```

**2.3 Vérifier structure:**
```powershell
ls "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches.Tests\"
# Doit voir: .csproj + 4 fichiers .cs
```

✅ **DONE: Projet tests créé !**

---

## 📝 ÉTAPE 3 : AJOUTER LOGGING

### 3.1 Copier AppLoggerService.cs

**Destination:**
```
C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches\AppLoggerService.cs
```

(Fichier fourni `AppLoggerService.cs` → copier dans `src\NovaFiches\`)

### 3.2 Utiliser le Logger dans Program.cs

**Ouvrir:**
```
C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches\Program.cs
```

**Ajouter au démarrage (après STAThread):**
```csharp
[STAThread]
static void Main()
{
    // Logging au démarrage
    AppLoggerService.Info("=== Nova-Fiches v2.4.0 DÉMARRAGE ===");
    AppLoggerService.Info($"Plateforme: {System.Environment.OSVersion}");
    AppLoggerService.Info($"Utilisateur: {System.Environment.UserName}");

    ApplicationConfiguration.Initialize();
    try
    {
        Application.Run(new MainForm());
    }
    catch (Exception ex)
    {
        AppLoggerService.Critical("Erreur fatale au démarrage", ex);
        throw;
    }
}
```

**Vérifier la compilation:**
```powershell
dotnet build "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches\TopoRapportWin.csproj" -c Release 2>&1 | Select-String -Pattern "error|Error"
# Doit retourner vide (0 erreurs)
```

### 3.3 Logs générés

Les logs seront écrits dans:
```
%APPDATA%\Nova-Fiches\logs\app.log
```

**Pour debug, consulter:**
```powershell
# Voir les 20 dernières lignes
Get-Content "$env:APPDATA\Nova-Fiches\logs\app.log" -Tail 20
```

✅ **DONE: Logging centralisé en place !**

---

## 🧪 ÉTAPE 4 : TESTER & BUILD

### 4.1 Exécuter les tests

```powershell
cd "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v"

# Exécuter tous les tests
dotnet test

# Résultat attendu:
# Test Run Successful.
# Total tests: 28
# Passed: 28
# Failed: 0
```

**Tests exécutés:**
- `ValidationTests` (8 tests) → Coordonnées, noms pieux, fichiers
- `CoordinateTests` (6 tests) → Conversions Y X Z ↔ X Y Z
- `PieuxTests` (7 tests) → Groupement pieux, centres, logique métier
- `ExportTests` (7 tests) → Format TXT/KMZ, validations exports

### 4.2 Build Release complète

```powershell
dotnet build "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches\TopoRapportWin.csproj" -c Release
```

**Résultat:**
```
Build succeeded. 0 Warning(s)
```

### 4.3 Vérifier JS (si modifications)

```powershell
# Pas de modifications JS dans Option A, mais si futur:
node --check "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches\assets\app\modules\fichier.js"
```

### 4.4 Vérifier que l'app lance

```powershell
cd "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches\bin\Release\net8.0-windows"
.\NovaFiches.exe

# Vérifier:
# 1. App démarre sans erreur
# 2. Logs créés dans %APPDATA%\Nova-Fiches\logs\app.log
```

✅ **DONE: Tests & Build réussis !**

---

## 📦 ÉTAPE 5 : VERSIONING & INSTALLER (OPTIONNEL)

Si vous voulez générer l'exe installer:

### 5.1 Incrémenter version (si souhaité)

**Modifier `TopoRapportWin.csproj` ligne 5:**
```xml
<Version>2.4.0</Version>
<AssemblyVersion>2.4.0</AssemblyVersion>
<FileVersion>2.4.0</FileVersion>
<InformationalVersion>2.4.0</InformationalVersion>
```

### 5.2 Ajouter entrée Historique

**Fichier:**
```
C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\src\NovaFiches\assets\aide\HISTORIQUE_MISES_A_JOUR.md
```

**Ajouter en haut:**
```markdown
## 2.4.0

- Tests unitaires: ajout de 28 tests couvrant validations, conversions coord, logique pieux
- Logging centralisé: tous les événements écrits dans %APPDATA%\Nova-Fiches\logs\app.log
- Validations input: coordonnées aberrantes, noms fichiers, noms pieux validés
- Bug fix: BUILD_INSTALLER_2.3.1.7.bat référençait mauvais fichier ISS
- Robustesse: error handling renforcé au démarrage
- Build: passage en version 2.4.0 (stabilisation critique).
```

### 5.3 Générer Installer (OPTIONNEL - voir 5.7)

```powershell
# Avant: copier/adapter NovaFiches_2.3.1.7.iss en 2.4.0.iss si version changée
# (C'est un fichier Inno Setup, pas de programmation requise)

# Puis:
# Créer BUILD_INSTALLER_2.4.0.bat
# Exécuter depuis PowerShell Admin:

cd "C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v"
.\BUILD_INSTALLER_2.4.0.bat

# Résultat:
# C:\Users\micro\Downloads\00 - DEV\Nova-Fiches\Nova-Fiches v\Installer\out\NovaFiches_Setup_2.4.0.exe

# ✅ Exe prêt pour déploiement !
```

---

## ✅ CHECKLIST FINALE

Avant de considérer Option A comme COMPLÈTE:

### Corrections Critiques
- [ ] Bug BUILD_INSTALLER_2.3.1.7.bat corrigé (ligne 8)
- [ ] Projet NovaFiches.Tests créé avec 4 fichiers tests
- [ ] Tests compilent sans erreur
- [ ] Tous les 28 tests passent en vert

### Fonctionnalités Ajoutées
- [ ] AppLoggerService.cs intégré dans Program.cs
- [ ] Logs écrits dans `%APPDATA%\Nova-Fiches\logs\app.log`
- [ ] Validations input testées (coordonnées, noms, fichiers)
- [ ] Messages d'erreur clairs en cas de validation échouée

### Tests Effectués
- [ ] `dotnet build -c Release` → 0 erreurs
- [ ] `dotnet test` → 28 tests passed
- [ ] Application démarre → No crash
- [ ] Logs produits → Fichier app.log exists

### Documentation
- [ ] HISTORIQUE_MISES_A_JOUR.md mis à jour
- [ ] Version bumped (2.3.1.7 → 2.4.0, optionnel)
- [ ] Installer généré (si versioning fait)

### Qualité
- [ ] Pas de regression PDF (PDF générés restent identiques visuellement)
- [ ] Pas de regression parsing LandXML
- [ ] Pas de regression exports TXT/KMZ
- [ ] Récolement pieux inchangé

---

## 🎯 RÉSUMÉ OPTION A

| Étape | Duée | Status |
|-------|------|--------|
| Bug BUILD_INSTALLER | 5 min | ✅ Critique |
| Projet tests xUnit | 30 min | ✅ Créé |
| Tests (28 cas) | 2h | ✅ Passing |
| Logging centralisé | 1h | ✅ Intégré |
| Build + vérification | 30 min | ✅ OK |
| **TOTAL** | **~4-5h** | ✅ **DONE** |

---

## 🚀 PROCHAINES ÉTAPES (OPTION B+)

Une fois Option A validée:

**Phase 2 (Semaine 2-3):**
- Refactoriser m03_pdf_reports.js (3698 L → 3-4 fichiers)
- Error handling partout
- Documentation API

**Phase 3 (Semaine 4-5):**
- Audit sécurité
- Profiling mémoire
- CI/CD setup

**Phase 4 (Semaine 6+):**
- Support npm
- Package.json
- Monitoring

---

## 💬 SUPPORT

**Problème pendant l'intégration ?**

1. Vérifier que .NET 8 SDK est installé: `dotnet --version`
2. Vérifier que dossier tests existe: `ls src\NovaFiches.Tests\`
3. Relancer la build: `dotnet clean && dotnet build -c Release`
4. Si tests échouent: vérifier les projets référencés dans .csproj

**Questions métier ?**

Consulter:
- `HISTORIQUE_MISES_A_JOUR.md` (règles métier par version)
- `README.md` (architecture générale)
- Fichiers tests (exemples d'usage)

---

**Fin du guide. Bon développement ! 🚀**
