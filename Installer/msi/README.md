# Nova-Fiches — paquet MSI (déploiement entreprise / GPO)

Ce dossier contient le paquet `.msi` destiné au **service informatique**, pour un déploiement
managé (GPO Software Installation, Intune, SCCM...) : installation et désinstallation
silencieuses, montée de version automatique. Il est **complémentaire** de l'installeur Inno Setup
existant à la racine du dépôt (`NovaFiches_2.3.0.100.iss`) — celui-ci reste l'installeur "grand
public" (assistant graphique) et n'est pas concerné par ce document. GPO Software Installation
n'accepte que des paquets `.msi` : c'est la seule raison d'être de ce second installeur.

## Générer le MSI

Prérequis : [WiX Toolset](https://wixtoolset.org/) v6 comme outil `dotnet` global (déjà installé
sur ce poste de dev) :

```powershell
dotnet tool install --global wix
```

Étapes, depuis la racine du dépôt :

```powershell
# 1. Publier l'application (même étape que pour l'installeur Inno Setup)
.\BUILD_RELEASE.ps1
# (ou : pwsh src\NovaFiches\build.ps1 -Configuration Release -Version 3.1.28.0)

# 2. Générer le MSI à partir de cette publication
.\Installer\build_msi.ps1 -Version 3.1.28.0
```

Sortie : `Installer\out\NovaFiches_Setup_<version>.msi`.

## Installation / désinstallation silencieuses

```powershell
# Installation silencieuse, sans redémarrage automatique, avec journal
msiexec /i "NovaFiches_Setup_3.1.28.0.msi" /quiet /norestart /l*v install.log

# Désinstallation silencieuse (par le même fichier .msi, ou par ProductCode - voir plus bas)
msiexec /x "NovaFiches_Setup_3.1.28.0.msi" /quiet /norestart
```

Le paquet installe pour **tous les utilisateurs du poste** (`Scope="perMachine"`, `ALLUSERS`
implicite) — installation/désinstallation nécessitent des droits administrateur, ce qui est
automatiquement le cas dans un déploiement GPO (exécuté sous le compte SYSTEM).

## Prérequis : runtime .NET

Aucun. L'application est publiée en mode "self-contained" (`src\NovaFiches\build.ps1`) : le
runtime .NET 8 est embarqué directement dans le MSI (via `Installer\staging\publish`), rien à
détecter ni à déployer séparément sur le poste cible.

## Prérequis : runtime Microsoft Edge WebView2

Nova-Fiches héberge son interface dans un contrôle WebView2 et ne peut pas fonctionner sans le
runtime **Microsoft Edge WebView2** installé sur le poste. Contrairement à l'installeur Inno
Setup (qui télécharge et installe ce runtime automatiquement pendant l'installation), **ce MSI ne
le fait volontairement pas** : un déploiement GPO s'exécute sous le compte SYSTEM, en général au
démarrage de la machine, sans session utilisateur ni garantie d'accès réseau — un Custom Action
qui lancerait un exécutable de téléchargement à ce moment-là serait fragile et échouerait
silencieusement sur certains postes.

À la place, le MSI **vérifie ce prérequis et bloque l'installation avec un message clair** si le
runtime est absent (voir la condition de lancement dans `NovaFiches.wxs`).

Dans la pratique, ce runtime est déjà présent nativement sur la quasi-totalité des postes Windows
10/11 à jour (installé avec Microsoft Edge, ou via Windows Update). S'il faut néanmoins le
déployer explicitement au préalable dans votre parc :

- Runtime officiel autonome (Evergreen Bootstrapper ou Standalone Installer), téléchargeable
  depuis la page développeur Microsoft WebView2 — cherchez "Microsoft Edge WebView2 Runtime
  download" sur le site officiel Microsoft.
- Il peut être déployé par GPO / Intune / SCCM exactement comme n'importe quel autre paquet
  d'entreprise, en amont de Nova-Fiches.

## Déploiement via Stratégie de groupe (GPO)

1. **Partage réseau accessible aux postes cibles** : copiez `NovaFiches_Setup_<version>.msi` sur
   un partage réseau lisible par les comptes ordinateur du domaine (ex.
   `\\serveur\logiciels\NovaFiches\NovaFiches_Setup_3.1.28.0.msi`). Le chemin doit être un chemin
   UNC, pas un lecteur mappé (le contexte SYSTEM ne voit pas les lecteurs mappés utilisateur).

2. **Console de gestion des stratégies de groupe** (`gpmc.msc`) : créez ou modifiez une GPO liée
   à l'OU contenant les postes cibles.

3. **Ordinateur > Stratégies > Paramètres du logiciel > Installation de logiciel** (Computer
   Configuration, pas User Configuration — c'est ce qui garantit une installation silencieuse,
   machine entière, sans interaction utilisateur, appliquée au démarrage) :
   - Clic droit → Nouveau → Package...
   - Sélectionnez le `.msi` via son chemin UNC (pas un chemin local).
   - Méthode de déploiement : **Attribué** (Assigned) — c'est le seul mode utilisable en
     Computer Configuration ; il installe automatiquement au prochain démarrage de la machine.

4. Les postes appliquent la GPO à leur prochain redémarrage (ou via `gpupdate /force` suivi d'un
   redémarrage — l'installation logicielle via GPO ne s'applique qu'au démarrage de la machine,
   jamais à chaud pendant une session).

## Gestion des montées de version

Chaque build du MSI porte le **même `UpgradeCode`** (dérivé de manière stable de l'identifiant de
paquet `NOVATLAS.NovaFiches` dans `NovaFiches.wxs` — ne changez jamais cet identifiant, sous
peine de casser la chaîne de montées de version pour tous les postes déjà déployés) et un
`ProductVersion` qui reflète la version Nova-Fiches (ex. `3.1.28.0`). Le paquet embarque une
**montée de version majeure** (`MajorUpgrade`) : à l'installation d'une version plus récente,
Windows Installer désinstalle automatiquement l'ancienne avant d'installer la nouvelle - aucune
action manuelle de désinstallation préalable n'est nécessaire.

Pour publier une nouvelle version via la même GPO :

1. Générez le nouveau MSI (`Installer\build_msi.ps1 -Version <nouvelle version>`) et copiez-le
   sur le même partage réseau.
2. Dans `gpmc.msc`, ouvrez le package existant → onglet **Montées de version (Upgrades)** →
   Ajoutez le nouveau package, cochez "Désinstaller l'ancien package, puis installer le nouveau
   package" (ou "Ce package peut mettre à niveau" selon la version de Windows Server).
   Alternativement, il est courant de simplement créer un nouveau package pointant vers le
   nouveau `.msi` dans la même GPO et de le lier explicitement comme montée de version du
   précédent via cet onglet — dans les deux cas, c'est le même `UpgradeCode` qui garantit que
   Windows Installer reconnaît qu'il s'agit de la même application.
3. Les postes déjà équipés de l'ancienne version la remplacent automatiquement au prochain
   démarrage ; les nouveaux postes ciblés par la GPO installent directement la dernière version.

## Désinstallation ciblée (scripting / inventaire)

Le `ProductCode` change à chaque build (normal — c'est le `UpgradeCode` qui reste stable), donc
pour désinstaller par script sans avoir le fichier `.msi` sous la main, préférez retrouver le
produit par son nom :

```powershell
Get-CimInstance -ClassName Win32_Product -Filter "Name = 'Nova-Fiches'" |
    ForEach-Object { $_.Uninstall() }
```

(`Win32_Product` est lent sur un grand parc et régénère les inventaires logiciels à chaque
requête — pour un désinstallation en masse via GPO, préférez plutôt le mécanisme natif "Retirer
ce logiciel" (Remove) sur le package GPO existant, ou `msiexec /x {UpgradeCode-résolu}` scripté à
partir de l'inventaire déjà relevé sur le parc.)

## Icône / raccourcis

Le MSI crée un raccourci dans le menu Démarrer (`NOVATLAS\Nova-Fiches`), avec un raccourci de
désinstallation à côté — **pas de raccourci Bureau** (choix délibéré : conforme aux pratiques
courantes de déploiement en environnement managé, où le Bureau des postes est généralement tenu
propre). Contactez l'équipe de dev si un raccourci Bureau est finalement souhaité pour ce mode de
déploiement.
