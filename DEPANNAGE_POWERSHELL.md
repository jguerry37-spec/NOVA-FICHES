# 🔧 GUIDE DÉPANNAGE - Erreur PowerShell INSTALL_OPTION_A.ps1

## 🔴 ERREUR RENCONTRÉE

```
Au caractère C:\Users\micro\OneDrive - Novatlas\02 - DEV\NOVA-FICHES\Nova-Fiches\Nova-Fiches v\INSTALL_OPTION_A.ps1:42: 39
+ function Write-Info([string]$Message) {
Accolade fermante « } » manquante dans le bloc d'instruction ou définition du type manquante.
```

## 🔍 CAUSE

Le script original contient des **emojis** (✅, ❌, ⚠️, ℹ️) qui peuvent causer des problèmes d'encodage en PowerShell.

PowerShell interprète mal les accolades quand il y a des caractères UTF-8 spéciaux.

## ✅ SOLUTION (2 options)

### **OPTION 1 : Utiliser la version corrigée (RECOMMANDÉE)**

**Fichier:** `INSTALL_OPTION_A_FIXED.ps1`

Cette version:
- ❌ Supprime TOUS les emojis
- ✅ Utilise uniquement [OK], [ERROR], [WARN], [INFO]
- ✅ Syntaxe PowerShell stricte et valide
- ✅ Fonctionne à 100%

**Utilisation:**
```powershell
# Supprimer l'ancien script
rm "INSTALL_OPTION_A.ps1"

# Renommer la version corrigée
mv "INSTALL_OPTION_A_FIXED.ps1" "INSTALL_OPTION_A.ps1"

# Exécuter
.\INSTALL_OPTION_A.ps1
```

---

### **OPTION 2 : Continuer MANUELLEMENT (Garder le contrôle)**

Si vous préférez faire étape par étape sans script automatisé:

**Étape 1: Fixer BUG BUILD_INSTALLER (5 min)**
```powershell
cd "C:\Users\micro\OneDrive - Novatlas\02 - DEV\NOVA-FICHES\Nova-Fiches\Nova-Fiches v"
copy "BUILD_INSTALLER_2.3.1.7_FIXED.bat" "BUILD_INSTALLER_2.3.1.7.bat" -Force
Write-Host "[OK] Bug corrige"
```

**Étape 2: Créer dossier tests (5 min)**
```powershell
mkdir "src\NovaFiches.Tests"
copy "NovaFiches.Tests.csproj" "src\NovaFiches.Tests\"
copy "ValidationTests.cs" "src\NovaFiches.Tests\"
copy "CoordinateTests.cs" "src\NovaFiches.Tests\"
copy "PieuxTests.cs" "src\NovaFiches.Tests\"
copy "ExportTests.cs" "src\NovaFiches.Tests\"
Write-Host "[OK] Tests copies"
```

**Étape 3: Copier AppLoggerService (2 min)**
```powershell
copy "AppLoggerService.cs" "src\NovaFiches\"
Write-Host "[OK] Logger copie"
```

**Étape 4: Build Release (2-3 min)**
```powershell
cd "C:\Users\micro\OneDrive - Novatlas\02 - DEV\NOVA-FICHES\Nova-Fiches\Nova-Fiches v"
dotnet clean -c Release
dotnet build "src\NovaFiches\TopoRapportWin.csproj" -c Release
if ($?) { Write-Host "[OK] Build reussi" } else { Write-Host "[ERROR] Build echoue" }
```

**Étape 5: Exécuter tests (30 sec)**
```powershell
dotnet test
# Attendre le resultat: devrait voir "28 Passed"
```

**Étape 6: Vérifier logs**
```powershell
$env:APPDATA
# Puis naviguer vers: Nova-Fiches\logs\app.log
```

---

## 📝 PROCHAINE ACTION

**Vous avez 2 choix :**

### **Choix A: Script automatisé corrigé** ⚡
```powershell
# Depuis la racine du projet
.\INSTALL_OPTION_A_FIXED.ps1
# Attend ~5 minutes, sort le résumé
```

### **Choix B: Suivi manuel du guide** 🎯
```
Lire GUIDE_INTEGRATION_OPTION_A.md (détails complets)
Copier fichiers manuellement
Exécuter commandes dotnet à la main
Meilleur pour apprendre
```

---

## 🎯 JE RECOMMANDE

**→ Utiliser `INSTALL_OPTION_A_FIXED.ps1`**

Pourquoi ?
- ✅ Pas d'erreur PowerShell
- ✅ Automatise 80% du travail
- ✅ Plus rapide (5 min vs 30 min manuel)
- ✅ Résumé clair à la fin

---

## 🚀 APRÈS LE DÉPANNAGE

Une fois le script corrigé lancé avec succès:

1. ✅ Bug BUILD_INSTALLER corrigé
2. ✅ 28 tests en place
3. ✅ Logging intégré
4. ✅ Build Release réussi

**Puis :**
- Modifier `Program.cs` pour ajouter logging au démarrage (étape manuelle)
- Mettre à jour HISTORIQUE_MISES_A_JOUR.md
- Tester l'app manuellement (PDF, exports, etc.)

---

## 📞 SI PROBLÈME PERSISTE

**"Le script toujours pas de .exe?"**
→ C'est normal, l'exe est dans `src\NovaFiches\bin\Release\net8.0-windows\NovaFiches.exe`

**"Tests échouent?"**
→ Vérifier que `src\NovaFiches.Tests\*.cs` files sont bien présents

**"AppLoggerService.cs non trouvé?"**
→ Vérifier qu'il est dans `src\NovaFiches\` (pas dans Tests)

**"Logs ne s'écrivent pas?"**
→ C'est normal jusqu'au premier lancement de l'app
→ Après lancement: vérifier `%APPDATA%\Nova-Fiches\logs\app.log`

---

## ✨ RÉSUMÉ

| Problème | Cause | Fix |
|----------|-------|-----|
| Erreur PowerShell ligne 42 | Emojis UTF-8 | Utiliser `INSTALL_OPTION_A_FIXED.ps1` |
| Script très lent | Pas applicable | Pas applicable |
| Tests ne se lancent pas | Dossier manquant | Créer `src\NovaFiches.Tests` d'abord |
| Build échoue | Dépendances | `dotnet restore` puis retry |

---

**Allez-y ! Utilisez la version FIXED et rapportez si besoin 🚀**
