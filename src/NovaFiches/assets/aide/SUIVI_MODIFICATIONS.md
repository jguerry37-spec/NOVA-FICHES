# Nova-Fiches — Suivi des modifications
## 2.3.0.13

- Récolement pieux: import TXT de référence corrigé (WebView2) en plaçant l'input file invisible en overlay sur le bouton (clic utilisateur direct), au lieu d'un click() programmatique.

## 2.3.0.12
- Fix HTML (Récolement pieux) : correction des guillemets dans l’attribut `title` du bouton **PDF — Récolement pieux + plan** (le HTML cassait certains bindings, dont l’import TXT).

## 2.3.0.9
- Récolement pieux : ajout du bouton **PDF — Récolement pieux + plan**.
- PDF Récolement pieux : ajout d’une page **Vue en plan (A4)** : points théoriques TXT (XY) + surlignage du pieu contrôlé + labels anti-chevauchement + barre d’échelle.

## 2.3.0.8
- Stabilisation : retour au comportement d’activation des boutons PDF basé uniquement sur les données réellement parsées (pas de logique “audit/gating” expérimentale).

## 2.3.0.7
- Projet (UI) : restauration des champs **Commentaire** et **Nom / prénom du signataire** (placés en colonne droite sous les Tolérances).

## 2.3.0.6
- UI Implantation : toggle **Calculer Dz** agrandi (x2) et repositionné à côté des boutons PDF (plus aligné, moins “à droite”).

## 2.3.0.6
- UI : déplacement du toggle "Calculer Dz" du module Projet vers le module Implantation.

## 2.3.0.4
- Implantation : mise en avant de **Dz** en UI (valeur plus lisible dans la table Implantation).
- Récolement pieux (PDF PdfSharp) : renommage du bloc **Implantation** -> **Récolement**.
- Récolement pieux (PDF) : calcul et affichage de **dA** (distance XY entre théorique TXT et centre calculé) dans la colonne **Dz / dA**.

## 2.3.0.3
- UX : correction DOM Projet — les blocs **Projet** et **Tolérances** sont bien en colonne droite (frère de `proj-left`, plus imbriqués).

## 2.3.0.2
- UX : module Projet en 2 colonnes (Infos dossier à gauche, Projet + Tolérances à droite).

## 2.3.0.1
- UI : Projet devient un module **contexte/paramétrage** (plus de bloc Visualisation).
- UI : Visualisation répartie par thèmes :
  - **Implantation / ligne de réf** : Station + Implantation + Ligne de référence.
  - **Suivi chantier** : Station + Levé topo + LandXML brut.
- UI : fenêtre **Contrôle pieu** déplacée dans le module **Récolement pieux**.
- UI : sidebar — titre NOVA-FICHES descendu (évite le chevauchement avec le menu WinForms).

## 2.2.0.38
- Récolement pieux : ajout du bouton **PDF — Récolement de pieux** basé sur le renderer **PDF Implantation PdfSharp**.
- Entrées PDF : Théorique = TXT, Mesuré = centres calculés (XY uniquement), filtrage = uniquement pieux LandXML analysables + présents dans le TXT.

## 2.2.0.36
- Récolement pieux : correction inversion XY sur certains exports Leica/Hexagon.
  - Priorité aux coordonnées Grid explicites (e/n) dans HexagonLandXML quand disponibles.
  - Fallback : détection + permutation automatique si CgPoint est en ordre N E Z.

## 2.2.0.35
- Récolement pieux : ouverture manuelle de la fenêtre de contrôle (Review) même si le max résidu est < 4 cm (clic sur une ligne valide).

## 2.2.0.34
- Récolement pieux : correction calcul cercle (stabilité numérique grandes coordonnées).
- Affichage centres / résidus désormais alimentés.

## 2.2.0.33
- Récolement de pieux (Patch 2)
  - Calcul du centre XY (cercle moyen) sur 3 à 10 points
  - Calcul des résidus (|d - R|) et statut **À contrôler** si max résidu > 4 cm
  - Fenêtre de contrôle interactive : inclusion/exclusion des points + recalcul en direct
  - Toujours sans toucher à m02_parser_calc ni au moteur PDF (PdfSharpEngine)

## 2.2.0.32
- UI : ajout du panneau **Récolement de pieux** (Patch 1)
  - Import TXT de référence (tabulation) : ID flexible (P / PI / Pi / Pieu / num) -> base numérique
  - Analyse LandXML (levé) : regroupement par N° (base.idx) + exclusion automatique si N° présent dans **ApplStakeout**
  - Garde-fous (affichage) : min 3 points, max 10 points
  - Aucune modification du parsing m02_parser_calc ni des PDFs existants

## 2.2.0.31
- PDF Points topo (LEVÉ) : exclusion des points présents dans un programme Stakeout (ApplicationStakeout) du LandXML
  - Pas de filtre par préfixe/numéro
  - Comparaison sur l'ID "base" (suppression du suffixe Leica "@NN" uniquement pour la règle d'exclusion)
- Correctif : aucun impact sur le PDF Implantation (bouton et contenu inchangés)

## 2.2.0.26
- PDF (tous rapports) : suppression des suffixes Leica "@NN" dans les IDs **des observations** (affichage), pour coherence avec les residus

## 2.2.0.25 (Baseline figee validee)
- Import LandXML Leica : OK (stable)
- Constante prisme : OK
- PDF Leve / Station : OK
- Affichage : suppression des suffixes "@..." dans les IDs (affichage seulement)
- Rattachement : repartition des points d'implantation par station via mapping point -> RawObservation(TargetPoint/pntRef) -> setupID -> InstrumentSetup.stationName
- Residus : colonne "Utilise" alimentee (fallback useKind -> 2D/3D)
- Notice PDF : integree via bouton Aide : OK

---

## 1.7.47
- Fix WebView2 cache: dossier user data versionné (évite UI/JS d'une ancienne version)
- Settings conservés (settings.json dans AppData)
- Versions assembly/file alignées (À propos cohérent)

## 1.7.48

- Ajout import **LandXML (Leica)** via menu **Échanges**.
- Conversion LandXML -> dataset AppLog-compatible côté HTML/JS (réutilise les PDFs existants sans modifier le pipeline).

## 1.7.50

- Correctif import **LandXML** :
  - Affichage clair "Échanges XML" (stations/obs/implantation/rabattement) dans l’écran d’import.
  - Correction extraction des IDs cibles (TargetPoint) dans les observations.
  - Alimentation de la vue "Station libre" via la première mise en station (compat UI).

Ce fichier sert de journal de suivi des évolutions (dev / améliorations / corrections).
Objectif : traçabilité "pro" et anti-régression.

---

## 1.7.37 (V2 — Étape 3.2)
- Menu **Échanges** : implémentation des actions réelles :
  - Import TXT (points XYZC) via boîte de dialogue
  - Import GSI Leica via boîte de dialogue (lecture des points par WI81/82/83 lorsque présents)
  - Export TXT (XYZC) depuis les points importés
- UI : ajout de 2 indicateurs "Échanges TXT" / "Échanges GSI" dans l’écran d’import.
- Aucune modification du pipeline PDF existant.

## 1.7.38 (V2 — Étape 3.3)
- PDF : ajout de 2 boutons **PDF — Station + TXT** et **PDF — Station + GSI**.
- Nouveau rendu PDF : reprend le PDF "Station uniquement" (mises en station AppLog) + ajoute une table "Points" issue des imports Échanges.
- UI : activation automatique des boutons uniquement si **AppLog chargé** + **points importés**.
- Aucune modification des 4 PDFs existants (implantation / ligne de référence / station uniquement / complet).

## 1.7.39 (V2 — Étape 3.4 — UX messages)
- UI : ajout d'un indicateur **⚠ Import AppLog requis pour PDF Station+** lorsque l'utilisateur importe un TXT/GSI via **Échanges** sans AppLog chargé.
- UI : l'indicateur disparaît automatiquement dès qu'un AppLog est chargé.
- Aucun changement sur le pipeline PDF existant.

## 1.7.40 (V2 — Étape 3.5 — Messages d’erreur au clic)
- UI : si l’utilisateur clique sur **PDF — Station + TXT/GSI** sans AppLog, affichage d’un message clair "Veuillez importer un fichier AppLog...".
- UI : si l’utilisateur clique sur **PDF — Station + TXT** sans TXT chargé (ou **Station + GSI** sans GSI), affichage d’un message explicite.
- UX : les boutons Station+ deviennent cliquables dès que des points sont importés via Échanges (TXT/GSI), même si l’AppLog n’est pas encore chargé.
- Aucun changement sur le pipeline PDF existant.

## 1.7.42 (V2 — Étape 3.7 — Bandeau "Projet")
- UI : ajout d’un bandeau **Projet** affichant l’état global : AppLog chargé/absent, TXT chargé/absent (nb points), GSI chargé/absent (nb points).
- UI : statut global "Projet prêt (PDF Station+)" / "Projet incomplet" avec consigne terrain claire.
- Mise à jour automatique après import AppLog et après import via **Échanges**.
- Aucun changement sur le pipeline PDF existant.

## 1.7.43 (V2 — Étape 3.7a — Placement bandeau "Projet" + scroll)
- UI : déplacement du bandeau **Projet** dans la colonne droite, au-dessus des **Tolérances**.
- UX : tolérances alignées en bas avec la carte "Infos dossier" (réduction de l’ascenseur inutile).
- Aucune modification du pipeline PDF existant.

## 1.7.41 (V2 — Étape 3.6 — Fenêtres d’erreur)
- UX : tous les messages d’erreur affichés via `setStatus(..., true)` déclenchent désormais une **fenêtre** (MessageBox WinForms) pour une meilleure lisibilité terrain.
- Fallback : si la communication WebView2 n’est pas disponible, affichage via `alert()`.
- Aucun changement sur le pipeline PDF existant.

## 1.7.36 (V2 — Étape 3.1)
- Ajout du menu **Échanges** entre **Fichier** et **Aide**.
- Déplacement de **Ouvrir Exports** dans **Échanges**.
- Stubs initiaux pour Import TXT / Import GSI / Export TXT.

## 1.7.35 (Base figée validée)
- Base validée : import AppLog, multi-stations, génération PDF stable.
- Excel supprimé.

## 1.7.44 (V2 — Étape 4.1 — Import GSI “observations brutes” : détection + mode + stockage)
- Import GSI : détection automatique du type de données : coordonnées WI81/82/83 / coordonnées masque WI21/22/31 / observations Hz-V-D.
- UI : affichage du mode GSI dans la pill (coords ou obs + nombre d'observations).
- Préparation étape 4.2 : stockage des observations pour calcul de coordonnées.

## 1.7.45 (V2 — Hotfix compilation)
- Correctif : parsing TXT/GSI — correction des séparateurs de fin de ligne ("\r\n" / "\n") qui avaient été injectés incorrectement dans `MainForm.cs`.
- Impact : compilation/publish OK. Aucun changement fonctionnel.

## 1.7.46 (V2 — Étape 4.2 — PDF observations polaires GSI, sans calcul)
- Import GSI : envoi des **observations polaires** (Hz / V / SD / hauteurs) à la UI.
- PDF : si le GSI est détecté en mode **observations**, le PDF **Station + GSI** génère une table "OBSERVATIONS POLAIRES (GSI)" (sans calcul de coordonnées).
- UX : le bouton **PDF — Station + GSI** s'active aussi quand il n'y a pas de points XYZ mais des observations.

- 2.3.0.12 — Récolement pieux: import TXT rétabli (input file masqué via position/opacity au lieu de display:none pour compat WebView2).
