# Nova-Fiches - Historique des mises a jour

Ce fichier sert de journal de suivi. Chaque version doit expliquer ce qui change et pourquoi, afin de garder une trace claire des corrections, evolutions et decisions metier.

## 2.3.1.44

- Correction : dans les fiches PDF d'implantation générées à partir d'un LandXML sans cible d'altitude réelle (`DesignPointOrthoHeight` à 0, cas fréquent pour les implantations planimétriques de pieux/poteaux, y compris en GNSS), la colonne "Dz / dA" affichait l'altitude mesurée brute au lieu d'un écart - Leica écrit `0.000000` même quand il n'y a pas de vraie cible, la formule prenait donc ce 0 pour une vraie référence. La colonne reste vide dans ce cas plutôt que d'afficher une valeur trompeuse.
- Station / Levé topo : l'onglet "Station libre" est maintenant grisé/inaccessible quand le fichier importé ne contient aucune mise en station TPS (résection) - notamment les levés purement GNSS, qui n'ont pas de mise en station. Les autres onglets (Implantation, Ligne de référence, Levé topo...) restent disponibles normalement.
- Build : passage de l'application et du moteur PDF en **2.3.1.44**.

## 2.3.1.43

- Nouveau bouton "PDF — Page de garde" dans la carte "Projet" (onglet Projet) : génère un PDF d'une seule page avec l'en-tête + le cartouche NOVATLAS (mêmes infos dossier que le PDF Station : ville, adresse, CHA, intervenant, système de coordonnées, PPM...), corps vide, pied de page standard. Ne nécessite aucun LandXML/AppLog importé - utilisable seul dès que les infos dossier sont saisies.
- Build : passage de l'application et du moteur PDF en **2.3.1.43**.

## 2.3.1.42

- Station / Levé topo, onglet "Plan station" : nouvelle réduction des triangles de station (toujours jugés trop imposants après 2.3.1.39/2.3.1.41) - fond de carte à l'écran (12px → 8px), plan schématique (9px → 6px) et page PDF (2 mm → 1,3 mm), cette fois de façon plus marquée.
- Build : passage de l'application et du moteur PDF en **2.3.1.42**.

## 2.3.1.41

- Station / Levé topo, page "Plan station" du PDF : les triangles des stations étaient toujours trop imposants (la réduction de 2.3.1.39 ne portait que sur le fond de carte affiché à l'écran - la page PDF utilise un dessin séparé, non touché à l'époque). Réduits ici aussi (3,2 mm → 2 mm de demi-hauteur).
- Build : passage de l'application et du moteur PDF en **2.3.1.41**.

## 2.3.1.40

- Correction : "PDF - Station" échouait avec "The process cannot access the file (...) because it is being used by another process" quand le PDF généré précédemment était encore ouvert (dans le lecteur PDF par défaut, ouvert automatiquement après chaque export). Le générateur du PDF Station (et Ligne de référence, même cause) enregistrait le fichier directement, sans le repli "nom de fichier avec suffixe automatique" déjà utilisé par tous les autres PDF de l'application en cas de fichier verrouillé - il utilise maintenant ce même repli.
- Build : passage de l'application et du moteur PDF en **2.3.1.40**.

## 2.3.1.39

- Station / Levé topo, onglet "Plan station" : les triangles des stations sur le fond de carte réel étaient trop imposants par rapport aux points visés. Réduits (18px → 12px).
- Build : passage de l'application et du moteur PDF en **2.3.1.39**.

## 2.3.1.38

- Correction : la génération du PDF Station avec "Envoyer sur la fiche station" coché (fond de carte ajouté en 2.3.1.37) bloquait complètement l'application ("Génération du PDF en cours..." qui ne se terminait jamais). Cause : le téléchargement des tuiles du fond de carte était attendu de façon synchrone directement sur le thread de l'interface, ce qui provoque un blocage classique quand du code asynchrone doit reprendre sur ce même thread une fois celui-ci déjà figé en attente. Le téléchargement s'exécute désormais entièrement sur un thread séparé.
- Build : passage de l'application et du moteur PDF en **2.3.1.38**.

## 2.3.1.37

- Station / Levé topo, PDF Station : la page "Plan station" ajoutée via "Envoyer sur la fiche station" (2.3.1.36) affiche désormais un vrai fond de carte (OpenStreetMap ou satellite Esri, selon le choix fait à l'écran), et non plus seulement le repère E/N local. Les coordonnées sont reprojetées en GPS côté application (mêmes formules que l'export KMZ), puis les tuiles du fond de carte sont récupérées et assemblées directement par l'application (pas une capture de la carte affichée à l'écran) avant de redessiner par-dessus les stations/points/traits de visée. Nécessite une connexion Internet au moment de générer le PDF ; en son absence (ou si la reprojection échoue), la page repasse automatiquement sur le repère local sans fond de carte (comportement de 2.3.1.36), sans jamais faire échouer la génération du PDF.
- Build : passage de l'application et du moteur PDF en **2.3.1.37**.

## 2.3.1.36

- Station / Levé topo, onglet "Plan station" : les numéros des points visés s'affichent désormais à côté de chaque point (comme les libellés de station), sur le plan schématique et sur le fond de carte.
- Deux nouvelles cases à cocher : "Figer la vue" bloque le zoom et le déplacement du fond de carte (utile pour le montrer sans le décaler par mégarde) ; "Envoyer sur la fiche station" ajoute ce plan en toute dernière page du PDF Station, avec le même en-tête/pied de page que les autres pages annexes (photos). Le plan est redessiné en vectoriel à partir des coordonnées (comme la "Vue en plan" du module Récolement), pas capturé en image : rendu net à l'impression, aucune dépendance au réseau/aux tuiles du fond de carte au moment de l'export. L'orientation (portrait, contenu pivoté si l'emprise est plus large que haute) suit la même logique déjà utilisée pour les vues en plan.
- Build : passage de l'application et du moteur PDF en **2.3.1.36**.

## 2.3.1.35

- Station / Levé topo : sur le "Plan station" (fond de carte réel comme plan schématique de repli), chaque mise en station a désormais sa propre couleur (triangle + tous ses traits de visée), au lieu d'une seule couleur bleue pour toutes les stations - la lecture était brouillonne dès qu'il y avait plusieurs stations sur le même plan avec des traits qui se croisent. Les points visés restent en vert (inclus) / rouge (exclu), inchangé. Pour ne pas perdre l'information "visée exclue du calcul" qui reposait auparavant sur la couleur du trait, un trait en pointillé remplace désormais le rouge pour une visée exclue (plein sinon).
- Build : passage de l'application et du moteur PDF en **2.3.1.35**.

## 2.3.1.34

- Station / Levé topo : sur le fond de carte réel du "Plan station" (2.3.1.33), les traits de visée reliant chaque station à ses points visés (présents sur le plan schématique de repli, mais absents du fond de carte) sont ajoutés - même code couleur qu'avant (bleu translucide si le point est inclus dans le calcul, rouge translucide si exclu).
- Build : passage de l'application et du moteur PDF en **2.3.1.34**.

## 2.3.1.33

- Station / Levé topo : l'onglet "Plan station" (introduit en 2.3.1.32) affiche désormais un vrai fond de carte (OpenStreetMap ou satellite Esri, au choix) au lieu du seul plan schématique. Les coordonnées de chantier (Lambert-93, CC42-50, NTF...) sont reprojetées en GPS côté application (mêmes formules que l'export KMZ), avec détection automatique du système de coordonnées source ou choix manuel dans une liste déroulante identique à celle de l'export KMZ. Toutes les stations libres du dossier restent affichées sur un même plan, avec le zoom/déplacement standard d'une carte en ligne. Le plan schématique local reste affiché tant que la reprojection n'est pas revenue, et sert de repli si le fond de carte est indisponible (hors-ligne). Lecture seule, inchangé : l'inclusion d'un point se modifie toujours depuis le tableau de l'onglet Station libre.
- Build : passage de l'application et du moteur PDF en **2.3.1.33**.

## 2.3.1.32

- Station / Levé topo : nouvel onglet "Plan station" dans la Visualisation, juste après "Station libre". Plan schématique (pas de fond de carte - les points visés depuis une station libre sont trop proches les uns des autres pour qu'un fond OpenStreetMap soit utile à cette échelle) affichant toutes les mises en station du fichier sur un même repère X/Y local, à l'échelle du chantier : triangle bleu pour chaque station, cercle pour chaque point visé (vert si inclus dans le calcul, rouge si exclu - reflète en direct les cases "Incl." de l'onglet Station libre, pas seulement le statut d'origine du fichier). Un trait fin relie chaque station à ses points visés pour distinguer les mises en station quand il y en a plusieurs. Au survol d'un point : coordonnées E/N/H, puis pour chaque station l'ayant visé, les résidus (dHz, dAlti, dDH) et le statut inclus/exclu - un point visé depuis plusieurs stations affiche une ligne par station. Au survol d'une station : coordonnées et écarts-types (σE, σN, σH, σOri). Lecture seule : l'inclusion se modifie toujours depuis le tableau de l'onglet Station libre.
- Build : passage de l'application et du moteur PDF en **2.3.1.32**.

## 2.3.1.31

- Export KMZ : harmonisation des tailles dans le bandeau d'outils. Les listes deroulantes (systeme de coordonnees, fond de carte) utilisaient le meme gabarit que les longs formulaires de saisie (44px de haut) - beaucoup trop imposant a cote des boutons compacts introduits en 2.3.1.29 (~28px). Ramenees a 30px, alignees visuellement avec les boutons. La pastille de distance mesuree (260x54px, police 15px) et le statut des reperes NGF (280x34px) etaient devenus disproportionnes pour la meme raison - reduits a 190x40px et 220x32px (police 12px), remesures pour ne jamais deborder ni faire bouger la carte, quel que soit le texte affiche.
- Export KMZ : corrige le placement du bouton "Recentrer la carte", qui semblait desaligne par rapport au reste de la ligne (l'alignement du bloc utilisait le centre vertical, incoherent avec la premiere ligne du bandeau qui aligne sur le bas). Les deux lignes du bandeau alignent desormais leurs elements sur la meme base, boutons et listes deroulantes confondus.
- Build : passage de l'application et du moteur PDF en **2.3.1.31**.

## 2.3.1.30

- Interface : ajustement du style des boutons introduit en 2.3.1.29 - jugé un peu "fade" (secondaire blanc sur blanc, insuffisamment visible). Nouveau calibrage : les boutons secondaires ont desormais un fond bleu tres pale (au lieu de blanc pur), ce qui les garde visibles sans revenir a un bleu plein partout. Le bouton primaire est legerement moins gras qu'un premier essai pour rester sobre. Comparatif valide sur maquette avant application.
- Interface : harmonisation de l'echelle de marges/paddings des boutons sur toutes les tailles d'ecran (les paliers responsive n'avaient pas suivi le resserrement des boutons en 2.3.1.29, ce qui pouvait donner des boutons plus grands sur petit ecran que sur grand ecran).
- Build : passage de l'application et du moteur PDF en **2.3.1.30**.

## 2.3.1.29

- Interface : refonte des boutons sur tous les onglets pour un rendu plus sobre et professionnel. Avant, chaque bouton (import, export, PDF, tout inclure/exclure, fermer...) avait exactement le meme style plein bleu tres appuye, sans hierarchie - aucun moyen de distinguer l'action principale d'un ecran des actions secondaires ou des simples utilitaires. Desormais : un seul bouton "primaire" par ecran (l'action de sortie principale : Recalculer, Exporter KMZ, PDF - Rapport complet, PDF - Recolement de pieux, PDF - Recolement MNT, PDF - Reportage photo) reste en bleu plein ; les actions de preparation (import, chargement, analyse) passent en style sobre (fond blanc, bordure fine) ; les actions utilitaires (tout inclure/exclure, reinitialiser, fermer, effacer) passent en style tres discret (sans fond). Boutons plus compacts (hauteur reduite) et coins moins arrondis. La barre de navigation laterale passe d'une pile de pavés bleus pleins a une liste fine, avec l'onglet actif indique par un trait vertical plutot qu'un remplissage.
- Build : passage de l'application et du moteur PDF en **2.3.1.29**.

## 2.3.1.28

- Export KMZ : trouve et corrige une troisieme cause, distincte des deux precedentes (glisser de carte en 2.3.1.27, reflow CSS en 2.3.1.26), du "la carte se deplace au premier clic". Au premier clic, Leaflet donne le focus clavier (accessibilite) a son conteneur puis tente lui-meme de restaurer la position de defilement de la page pour compenser le "scroll to focus" automatique du navigateur - mais cette compensation ne regarde que le defilement du corps de la page, alors que Nova-Fiches fait defiler un conteneur interne (la zone de contenu principale). Le defilement de cette zone n'etait donc jamais protege et sautait au moment ou la carte prenait le focus, deplacant la carte et la barre d'outils sous le curseur reste immobile. Verifie par reproduction directe du mecanisme puis par test du correctif : le defilement ne bouge plus du tout au premier clic.
- Build : passage de l'application et du moteur PDF en **2.3.1.28**.

## 2.3.1.27

- Export KMZ : trouve et corrige la veritable cause du "la carte se deplace au premier clic" pour Mesurer une distance et Dessiner une zone (reperes NGF), qui persistait malgre les correctifs precedents (reflow CSS en 2.3.1.26, autoPan en 2.3.1.24). Un clic a la souris comporte quasi toujours quelques pixels de mouvement entre l'appui et le relachement ; Leaflet interprete ca comme un mini-glisser de carte et deplace deja la vue en consequence, meme quand le geste est ensuite reconnu comme un simple clic. Verifie par test direct : un clic avec 8 pixels de tremblement deplacait le centre de la carte d'environ 100 metres. Le glisser de la carte (pas le zoom, deja retire) est desormais desactive le temps de placer un point de mesure ou un coin de zone, puis reactive immediatement apres.
- Build : passage de l'application et du moteur PDF en **2.3.1.27**.

## 2.3.1.26

- Export KMZ : le verrouillage du zoom pendant "Mesurer" et "Dessiner une zone" (introduit en 2.3.1.25) est retire. Il ne corrigeait pas le probleme signale et compliquait l'usage sans necessite.
- Export KMZ : correction de la veritable cause du "deplacement de la carte au premier clic" pour Mesurer/Dessiner une zone. Ce n'etait pas un souci de zoom Leaflet : les pastilles de statut ("Clique un premier coin...", "Distance totale...") changeaient de taille selon leur contenu (texte vide au depart, puis rempli au premier clic), ce qui deplacait toute la ligne d'outils et donc la carte en dessous, pile au moment ou l'utilisateur venait de cliquer avec le curseur a une position ecran fixe — donnant l'impression que la carte "sautait" sous le clic. Ces deux pastilles ont desormais une largeur et une hauteur fixes en toute circonstance (texte vide ou non), ce qui elimine le decalage.
- Build : passage de l'application et du moteur PDF en **2.3.1.26**.

## 2.3.1.25

- Export KMZ : "Mesurer une distance" et "Dessiner une zone" sont desormais mutuellement exclusifs. Si un dessin de zone etait laisse en plan (premier coin clique, jamais termine) puis "Mesurer une distance" etait lance, le clic suivant restait intercepte par le dessin de zone inacheve au lieu de placer un point de mesure — ce qui pouvait redeclencher un chargement de reperes NGF et un comportement de carte inattendu. Chaque outil interrompt desormais proprement l'autre s'il etait en cours.
- Export KMZ : le zoom de la carte est verrouille plus largement pendant "Mesurer" et "Dessiner une zone" — en plus du double-clic, la molette, le pincement tactile et le zoom par selection sont desactives le temps de l'outil, pour eviter tout changement de zoom involontaire (ex. leger contact avec la molette ou le trackpad) pendant qu'on vise un clic precis. Les boutons +/- et "Recentrer la carte" restent disponibles.
- Build : passage de l'application et du moteur PDF en **2.3.1.25**.

## 2.3.1.24

- Export KMZ : corrige le deplacement inattendu de la carte au premier clic en mode "Mesurer une distance" ou "Dessiner une zone". Cause reelle : les popups des marqueurs (points TXT/DXF/NGF) recentrent la carte par defaut (autoPan) quand ils s'ouvrent pres du bord visible ; avec des donnees denses, un clic pres d'un marqueur existant pour poser un point de mesure ou un coin de zone ouvrait son popup et deplacait la vue. Verifie par test direct (le centre de la carte ne bouge plus dans le meme scenario).
- Licence : corrige un cas ou la pastille de licence n'apparaissait pas dans le panneau lateral tant que l'utilisateur n'avait pas change de module apres le demarrage (la synchronisation ne se declenchait qu'au clic sur un module, en concurrence avec le chargement de la licence). Le pied de page et le panneau lateral sont desormais renseignes ensemble, directement au demarrage.
- Build : passage de l'application et du moteur PDF en **2.3.1.24**.

## 2.3.1.23

- Export KMZ : mise en page revue pour tout voir sans avoir a scroller. La ligne "Reperes NGF (IGN)" suit desormais directement le bouton Exporter KMZ (et le statut du fichier), sur la meme ligne. En dessous : Mesurer une distance, Effacer, la distance, le choix du fond de carte et un nouveau bouton "Recentrer la carte", tous sur la meme ligne.
- Export KMZ : correctif d'un comportement genant ou la carte se recadrait automatiquement (zoom + position) a chaque action (coche d'un calque DXF, chargement de reperes NGF, etc.). Le recadrage automatique ne se fait plus qu'au tout premier affichage de la carte ; le bouton "Recentrer la carte" permet de le redeclencher manuellement a la demande.
- Build : passage de l'application et du moteur PDF en **2.3.1.23**.

## 2.3.1.22

- Export KMZ : la fonction "Photos" (annotation de photos, sans rapport avec le KMZ) est retiree de ce module.
- Export KMZ : les blocs "Reperes NGF (IGN)" et "Mesurer une distance" sont deplaces dans la carte du haut (a la suite du bouton Exporter KMZ), a l'endroit ou se trouvait auparavant le bouton Photos.
- Export KMZ : l'affichage de la distance mesuree est mis en valeur (pastille rouge plus grande), au lieu d'un simple texte gris discret.
- Build : passage de l'application et du moteur PDF en **2.3.1.22**.

## 2.3.1.21

- Correctif interne (CI) : l'URL du fond de carte satellite (Esri) ajoutee en 2.3.1.19 etait ecrite en clair dans le code, ce qui violait la verification "offline garanti" (aucune URL http(s) en dur dans les fichiers JS/HTML livres, hors dossier vendor). Corrigee en suivant le meme decoupage que l'URL OpenStreetMap deja presente. Aucun changement visible pour l'utilisateur.
- Build : passage de l'application et du moteur PDF en **2.3.1.21**.

## 2.3.1.20

- Export KMZ : hauteur de la carte de controle legerement reduite (700 -> 580 px), la 2.3.1.19 etait un peu trop haute.
- Correctif : cliquer deux fois pour dessiner une zone de reperes NGF (ou pour la mesure de distance) declenchait parfois le zoom natif de la carte entre les deux clics, deplacant la vue et faisant atterrir le point au mauvais endroit. Le zoom au double-clic est desormais desactive pendant ces deux outils, et reactive une fois termine.
- Build : passage de l'application et du moteur PDF en **2.3.1.20**.

## 2.3.1.19

- Export KMZ : les reperes NGF (IGN) ont desormais un symbole triangulaire sur la carte (symbole cartographique standard des points geodesiques), distinct des points topo TXT/DXF qui restent des ronds. Avant, seule la couleur les differenciait.
- Export KMZ : la fenetre "Carte de controle" est agrandie (430 -> 700 px de hauteur) pour un controle visuel plus confortable.
- Export KMZ : nouveau choix de fond de carte (Plan OpenStreetMap / Satellite Esri), en plus du plan existant.
- Export KMZ : les reperes NGF ne sont plus listes dans le tableau "Points importes" (pas de case a cocher individuelle, coherent avec la selection tout-ou-rien par zone dessinee).
- Build : passage de l'application et du moteur PDF en **2.3.1.19**.

## 2.3.1.18

- Correctif reperes NGF : l'altitude affichait 0.000 m au lieu de la vraie valeur pour certains reperes. Le flux IGN encode l'altitude tantot en nombre, tantot en texte selon les points ; les deux formats sont maintenant acceptes.
- Licence : le pied de page et le panneau lateral affichent desormais l'etat de la licence sous le numero de build (ex. "Licence valable jusqu'au JJ/MM/AAAA"). Une alerte orange apparait 30 jours avant l'expiration, rouge dans les 7 derniers jours.
  - Pourquoi : eviter une expiration surprise en fin de licence, avec un avertissement progressif comme sur les logiciels professionnels.
- Build : passage de l'application et du moteur PDF en **2.3.1.18**.

## 2.3.1.17

- Export KMZ / reperes NGF : le bouton "Charger sur la zone visible" est remplace par "Dessiner une zone a charger". Clique deux coins sur la carte pour delimiter precisement la zone interrogee sur le flux IGN, au lieu de se baser sur le zoom actuel de la carte.
  - Pourquoi : avec la zone visible, dezoomer la carte pouvait ramener beaucoup de reperes d'un coup (jusqu'a la limite de 500) sans possibilite de cibler une emprise precise. Le rectangle dessine donne un controle direct sur la zone, quel que soit le niveau de zoom.
- Build : passage de l'application et du moteur PDF en **2.3.1.17**.

## 2.3.1.16

- Export KMZ / reperes NGF : les reperes charges apparaissent desormais aussi comme lignes dans le tableau "Points importes" (nom, altitude, etat, longitude/latitude), en plus des marqueurs sur la carte.
- Export KMZ / reperes NGF : le popup d'un repere sur la carte propose maintenant un lien "Telecharger la fiche (PDF)" vers la fiche signaletique officielle IGN du point, quand elle est disponible.
  - Pourquoi : retrouver directement le detail officiel d'un repere (fiche PDF IGN) et le voir liste comme les autres points importes, sans repasser par le site de l'IGN.
- Build : passage de l'application et du moteur PDF en **2.3.1.16**.

## 2.3.1.15

- Export KMZ : nouvelle option "Reperes NGF (IGN)" pour charger, sur la zone actuellement visible de la carte, les reperes de nivellement officiels IGN (altitude NGF) et les inclure dans l'export KMZ aux cotes des points TXT/DXF. Interroge le flux public IGN (Geoplateforme, data.geopf.fr) ; necessite une connexion Internet, comme le fond de carte OpenStreetMap deja utilise dans ce module.
- Export KMZ : nouvel outil "Mesurer une distance" sur la carte de controle : clics successifs pour tracer une ligne en plusieurs segments, distance cumulee affichee en direct (metres ou kilometres). Outil d'affichage uniquement, non inclus dans l'export KMZ.
  - Pourquoi : permettre de verifier une altitude NGF de reference et une distance directement sur la carte, sans sortir de l'application.
- Build : passage de l'application et du moteur PDF en **2.3.1.15**.

## 2.3.1.14

- Photos : quand une photo est liee a un point du rapport, ses coordonnees rectangulaires (X/Y/Z) disponibles sont reprises et affichees dans l'annexe photo, juste sous "Point : <ID>". Un point peut n'avoir que X/Y, que Z, les trois, ou aucune coordonnee exploitable : seules les composantes reellement disponibles sont affichees.
  - Pourquoi : retrouver directement sur la photo la position mesuree du point, sans avoir a rouvrir le tableau du rapport.
- Build : passage de l'application et du moteur PDF en **2.3.1.14**.

## 2.3.1.13

- Photos : possibilite de lier une photo a un point du rapport (implantation, ligne de reference, leve, transfert alti), via une liste deroulante "Point lie" dans l'editeur de photo. Sans selection, rien ne change par rapport a avant.
- PDF : dans l'annexe photo, les photos liees a un point affichent desormais "Point : <ID>" et sont regroupees/ordonnees selon l'ordre d'apparition du point dans le rapport. Les photos non liees restent groupees a la fin, dans leur ordre d'ajout habituel.
  - Pourquoi : permettre de retrouver facilement quelle photo correspond a quel point mesure, sans avoir a redigiger une legende manuelle a chaque fois.
- Build : passage de l'application et du moteur PDF en **2.3.1.13**.

## 2.3.1.12

- Bandeau de mise a jour : remplace par une pastille compacte alignee avec le logo dans l'en-tete, au lieu d'une barre pleine largeur qui poussait le logo sur une deuxieme ligne.
  - Pourquoi : meilleure integration visuelle, coherente avec les autres pastilles (pill) deja utilisees dans l'application.
- Build : passage de l'application et du moteur PDF en **2.3.1.12**.

## 2.3.1.11

- Infos dossier : inversion des champs "Intervenant" et "Contact chantier" entre les encadres "Adresse chantier" et "Intervenant" (Intervenant est desormais dans l'encadre Adresse chantier, Contact chantier dans l'encadre Intervenant).
- Build : passage de l'application et du moteur PDF en **2.3.1.11**.

## 2.3.1.10

- Version affichee (PDF, appli, journal) : suppression du suffixe technique "+hash" ajoute automatiquement par l'outil de compilation depuis que le depot est sous Git. Seul le numero de version propre (ex. 2.3.1.10) reste visible.
  - Pourquoi : ces informations internes n'ont rien a faire dans un document remis a un client ou affiche a l'ecran.
- Build : passage de l'application et du moteur PDF en **2.3.1.10**.

## 2.3.1.9

- Recolement de pieux : correction du bouton "PDF - Recolement pieux + plan", qui ne produisait jamais la page "Vue en plan". Cause : le rendu plantait silencieusement des qu'un point theorique du TXT sans numero de pieu (ex. un repere nomme "A", "B"...) etait present dans la liste, a cause d'une lecture de donnee JSON non protegee.
  - Pourquoi : rendre visible l'echec pour la prochaine fois (trace desormais ecrite dans le journal de l'application) et corriger la cause reelle plutot que de contourner le symptome.
- Build : passage de l'application et du moteur PDF en **2.3.1.9**.

## 2.3.1.8

- Refonte ergonomique complete de l'interface : design maison (moins "generique IA"), formulaire "Infos dossier" reorganise en sous-blocs, grille a 6 colonnes avec champs extensibles au clic, reduction des doubles ascenseurs, feedback visuel pendant les exports PDF/KMZ et confirmation de validation dans le module Pieux.
  - Pourquoi : rendre l'interface plus rapide a utiliser sur le terrain et plus proche d'une presentation professionnelle NOVATLAS.
- Activation par licence hors-ligne (ECDSA) au demarrage de l'application.
  - Pourquoi : reserver l'usage de Nova-Fiches aux postes NOVATLAS autorises, sans dependance reseau.
- Ajout d'une verification de mise a jour non bloquante (bandeau discret, jamais de fenetre bloquante, echec silencieux si pas de reseau).
  - Pourquoi : prevenir qu'une nouvelle version est disponible sans jamais gener le travail terrain.
- Suite de tests automatises et pipeline d'integration continue (GitHub Actions) mis en place sur le depot.
  - Pourquoi : detecter une regression avant qu'elle n'atteigne un poste de travail.
- Rapport complet (implantation + ligne de reference) : correction de l'etiquette d'intervention qui affichait a tort "POINTS TOPO (LEVE)" au lieu du champ "Elements" du dossier.
- Implantation : quand un point est reimplante sur le terrain (2e passage Leica, ex. `IPt_337@96`), la coordonnee mesuree de la 1re tentative est desormais reconstituee (theorique + ecart d'origine) au lieu de rester vide dans le tableau et le PDF.
- Recolement de pieux : correction de l'attribution de chaque pieu a sa station de mesure lorsque l'appareil a ete stationne plusieurs fois sur le meme point (ex. `ST1`, `ST1 (2)`, `ST1 (3)`) ; auparavant, tous les pieux retombaient a tort sur la 1re station.
- Interface : correction d'une incoherence visuelle sur les cases "Tolerance XY / Tolerance Z" (poids de police et couleur desormais identiques).
- Pourquoi : fiabiliser les rapports terrain generes a partir de fichiers Leica avec reimplantations et changements de station multiples.
- Build : passage de l'application et du moteur PDF en **2.3.1.8**.

## 2.3.1.7

- Export KMZ : lecture des polylignes DXF `POLYLINE` 3D et `LWPOLYLINE`, converties en segments exportables.
- Export KMZ : conservation des altitudes des polylignes 3D afin qu'elles restent visibles en 3D dans Google Earth.
- Recolement de pieux : reconnaissance des noms de points generiques du type `T62.1`, `ABC-12.3` ou `Z3-P12.2`.
- Recolement de pieux : correspondance TXT/LandXML basee sur le nom complet avant le dernier indice de mesure.
- Recolement de pieux : lecture correcte des `CgPoint` LandXML en ordre `Y X Z` puis conversion interne en `X Y Z`.
- Leve topo : correction de l'affichage et de l'export TXT pour sortir `X=E`, `Y=N`, `Z=H`.
- Pourquoi : fiabiliser les exports terrain lorsque les fichiers Leica utilisent des noms alphanumeriques et des systemes CC, sans imposer une nomenclature unique.
- Build : passage de l'application et du moteur PDF en **2.3.1.7**.

## 2.3.1.6

- Reportage photo : ajout du choix du nombre de photos par page, uniquement dans ce module.
- PDF reportage photo : adaptation automatique de la mise en page pour 1, 2, 3 ou 4 photos par page.
- Photos : ajout des outils `Trait` et `Texte` dans la fenetre d'annotation.
- Photos : le texte peut etre colore, dimensionne et oriente par cliquer-glisser sur l'image.
- Projet `.nova` : conservation du choix de photos par page du reportage photo.
- Pourquoi : permettre des reportages photo plus souples, du format grande photo pleine page au format quatre photos par page, sans modifier les annexes photos des autres modules.
- Build : passage de l'application et du moteur PDF en **2.3.1.6**.

## 2.3.1.5

- Reportage photo : ajout d'un module dedie sous Export KMZ pour generer un rapport photo autonome.
- Reportage photo : reprise des parametres du projet et conservation de la presentation PDF standard Nova-Fiches.
- PDF reportage photo : conservation de l'entete, du nom d'intervention et de l'encadre cartouche standard avant les photos.
- Photos : ajout de l'outil `Point`, du choix de couleur et du choix d'epaisseur de trait pour les annotations.
- Pourquoi : produire un rapport photo seul, sans LandXML/TXT/DXF, tout en gardant exactement la mise en page des autres fiches.
- Build : passage de l'application et du moteur PDF en **2.3.1.5**.

## 2.3.1.4

- Photos PDF : ajout et stabilisation des pages photos en annexe des rapports.
- Photos : ajout des legendes et des outils de dessin simples sur image.
- Photos : ajout des boutons `Valider`, `Fermer` et `Effacer`, avec placement corrige pour eviter les chevauchements dans la fenetre.
- PDF : conservation de l'entete, du pied de page et de la numerotation lors de l'ajout des pages photos.
- Pourquoi : permettre l'ajout de photos terrain annotees dans les fiches tout en gardant une presentation PDF propre et legere.
- Build : passage de l'application et du moteur PDF en **2.3.1.4**.

## 2.3.1.3

- Export AutoCAD : ajout de `PHASE` et `PRESTA` dans le fichier cartouche genere par Nova-Fiches.
- Cartouches AutoCAD : l'attribut `PRESTA` des blocs `_N_Cartouche_A0/A1/A2/A3` est alimente par la phase choisie dans le projet.
- Projets `.nova` : au chargement dans AutoCAD, `repPhase` alimente aussi `PRESTA` afin de remplacer correctement l'ancienne valeur du DWG.
- Outil AutoCAD : installation de `cartouche_nova.lsp` version **1.0.11**.
- Pourquoi : fiabiliser la synchronisation de la prestation entre Nova-Fiches et les cartouches DWG.
- Build : passage de l'application et du moteur PDF en **2.3.1.3**.

## 2.3.1.2

- Module Pieux : restauration de la visualisation metier propre au recolement des pieux.
- Recolement des pieux : affichage retabli du centre X/Y calcule pour chaque pieu.
- Controle des mesures : acces retabli aux cases permettant d'activer ou de desactiver chaque point dans le calcul du centre.
- Navigation : rafraichissement automatique de l'analyse lors de l'ouverture de l'onglet Recolement.
- Pourquoi : corriger le remplacement involontaire du tableau de recolement par la visualisation generique des points topo.
- Build : passage de l'application et du moteur PDF en **2.3.1.2**.

## 2.3.1.1

- Export KMZ : les libelles des points TXT et DXF sont maintenant affiches en blanc.
- Textes DXF : application du meme style blanc dans Google Earth.
- Lisibilite : reduction legere de l'echelle des libelles pour obtenir un rendu visuellement plus fin sur l'imagerie.
- Pourquoi : remplacer les textes noirs peu lisibles sur les fonds sombres et colores de Google Earth.
- Build : passage de l'application et du moteur PDF en **2.3.1.1**.

## 2.3.1.0

- Module Pieux : transformation du module en **Pieux - Implantation et recolement**, avec deux fonctions clairement separees.
- Implantation de pieux : reprise du calcul et des selections du module Implantation depuis le LandXML.
- Implantation de pieux : ajout d'un TXT theorique utilise uniquement pour la representation graphique.
- Plan PDF implantation pieux : affichage de tous les points du TXT, avec cercle et matricule uniquement pour les points inclus dans le rapport.
- Recolement de pieux : conservation du fonctionnement metier existant.
- Plans PDF pieux : suppression du double cercle concentrique, deduplication des points et adaptation automatique du rayon pour limiter les chevauchements.
- Cartouches PDF : uniformisation du modele de l'appareil et du numero de serie depuis `InstrumentDetails` du LandXML dans tous les rapports.
- Pourquoi : identifier cette evolution importante du module Pieux et garantir des cartouches coherents entre toutes les fiches.
- Build : passage de l'application et du moteur PDF en **2.3.1.0**.

## 2.3.0.117

- Export KMZ : les lignes DXF dont les altitudes sont nulles sont plaquees sur le terrain afin de rester visibles dans Google Earth.
- Export AutoCAD : ajout effectif de l'intervenant dans le fichier cartouche.
- Projets `.nova` : ecriture UTF-8 sans BOM et conservation directe des caracteres accentues.
- Cartouches Novatlas : synchronisation de `DATE` et de la date de revision correspondant a l'indice courant.
- Cartouches Novatlas : l'intervenant alimente l'attribut `EMETTEUR.<indice>` sans modifier les revisions precedentes.
- Cartouches Novatlas : protection totale des attributs automatiques `ECH` et `ECHELLE`.
- Systeme altimetrique : ajout de **IGN 78** dans Nova-Fiches et dans l'outil AutoCAD.
- Outil AutoCAD : installation de `cartouche_nova.lsp` version **1.0.4** dans le sous-dossier `AutoCAD`.
- Build : passage de l'application et du moteur PDF en **2.3.0.117**.

## 2.3.0.116

- Import TXT KMZ : tous les points d'un nouveau fichier sont maintenant selectionnes par defaut.
- Selection utilisateur : un changement de systeme de coordonnees conserve les choix effectues dans le tableau.
- Carte de controle : centrage et zoom automatiques fiabilises sur l'ensemble des donnees importees.
- Leaflet : bibliotheque cartographique integree localement dans Nova-Fiches.
  - Pourquoi : eviter le mode de secours lorsque le CDN externe est inaccessible, tout en conservant le fond OpenStreetMap connecte a Internet.
- Validation : les 85 points du fichier `SEV_POL_CC49_PPM-94_20260330.txt` sont reconnus et selectionnes.
- Build : passage de l'application et du moteur PDF en **2.3.0.116**.

## 2.3.0.115

- Export KMZ : remplacement des symboles variables par un repere circulaire uniforme.
- Identification visuelle : points TXT affiches en bleu et points DXF affiches en orange.
- Noms des points : affichage active dans Google Earth pour tous les points TXT et DXF.
- Donnees KMZ : ajout de la source `TXT` ou `DXF` dans les informations de chaque point.
- Pourquoi : eviter l'alternance confuse entre punaises jaunes et points bleus, tout en rendant chaque point directement identifiable.
- Build : passage de l'application et du moteur PDF en **2.3.0.115**.

## 2.3.0.114

- Export KMZ : chargement simultane d'un fichier TXT et d'un fichier DXF dans le meme projet.
  - Pourquoi : combiner les points terrain, les points topo DXF, les traits et les textes dans un seul fichier Google Earth.
- Selection : ajout des cases individuelles pour inclure ou exclure les points TXT, avec la meme logique que les points DXF.
- Interface : liste unique des points importes avec identification claire de la source `TXT` ou `DXF / calque`.
- Export combine : seuls les points coches et les calques DXF inclus sont ecrits dans le KMZ.
- Fichier produit : priorite au nom et au dossier du TXT lorsqu'il est charge, sinon utilisation du DXF.
- Build : passage de l'application et du moteur PDF en **2.3.0.114**.

## 2.3.0.113

- Export KMZ : utilisation automatique du Z en altitude absolue lorsqu'il est fourni.
  - Pourquoi : restituer les points, traits et textes en 3D dans Google Earth.
- Donnees sans Z : maintien de l'affichage 2D plaque au sol avec `clampToGround`.
- TXT : prise en charge des formats `ID X Y` et `ID X Y Z`, avec conservation d'un vrai `Z = 0.000` comme valeur 3D.
- DXF : distinction entre un code altitude present et un Z absent pour les points, les deux extremites des traits et les textes.
- Validation : controle d'un KMZ mixte contenant simultanement des objets 2D, 3D et un Z nul valide.
- Build : passage de l'application et du moteur PDF en **2.3.0.113**.

## 2.3.0.112

- Detection des systemes : les abreviations `L1`, `L2` et `L3` doivent maintenant etre isolees dans le nom du fichier.
  - Pourquoi : eviter que des noms de chantier comme `L15`, `LOT1` ou `L2A` soient confondus avec les anciens systemes NTF Lambert.
- TXT : en l'absence d'un nom de projection explicite, priorite effective a l'analyse des plages de coordonnees.
- Validation : `COMPLMENT JL- L15 23PDS MTECH - CHA02744 -06.03.2025.txt` est correctement detecte en RGF93 / CC49.
- Build : passage de l'application et du moteur PDF en **2.3.0.112**.

## 2.3.0.111

- Export KMZ DXF : lecture des entites texte `TEXT` et `MTEXT`.
  - Pourquoi : conserver dans Google Earth les reperes, numeros d'axes et annotations georeferencees du dessin.
- Carte de controle : affichage permanent des textes DXF, avec activation par calque comme les traits.
- Nettoyage AutoCAD : suppression des commandes internes de mise en forme avant l'affichage et l'export.
- Import DXF : lecture autorisee lorsque le fichier reste ouvert dans un autre logiciel compatible avec le partage Windows.
- Validation : test realise sur `Dessin9.dxf`, avec 30 textes exportes et detection automatique RGF93 / CC47.
- Build : passage de l'application et du moteur PDF en **2.3.0.111**.

## 2.3.0.110

- Export KMZ DXF : suppression de la detection et du rattachement de repere local.
  - Pourquoi : la regle de detection pouvait confondre des coordonnees Lambert 1 valides avec un repere local et bloquer a tort l'export.
- Detection DXF : reconnaissance des metadonnees `Projection Lambert Zone I`, `II` et `III`.
  - Pourquoi : identifier directement les anciens systemes Lambert depuis le contenu AutoCAD, meme si le fichier est renomme.
- Validation : controle realise sur `NOVA_POL_MGA_LAMBERT1_PPM-62.7_2026.01.29_F.dxf`.
- Build : passage de l'application et du moteur PDF en **2.3.0.110**.

## 2.3.0.109

- Export KMZ : ajout de l'import DXF pour les points topo `POINT` ou blocs avec attributs, ainsi que les entites `LINE`.
  - Pourquoi : produire un KMZ a partir d'un dessin contenant simultanement des cibles topographiques et des traits.
- DXF : ajout de la selection des calques et de la selection individuelle des points topo.
  - Pourquoi : permettre a l'utilisateur de choisir exactement les donnees visibles dans Google Earth.
- Carte : affichage des points et des traits DXF sur le fond OpenStreetMap avant export.
- DXF local : detection des coordonnees locales et ajout d'un rattachement X/Y obligatoire avant export.
  - Pourquoi : eviter de placer silencieusement un dessin local au mauvais endroit malgre la presence d'un systeme `GEODATA`.
- Validation : test realise sur `NOVA_VIT_CIBLES_RDC_04.06.26.dxf` avec 11 points `AF.*`, 18 traits et detection RGF93 / CC49.
- Build : passage de l'application et du moteur PDF en **2.3.0.109**.

## 2.3.0.108

- Export KMZ : ajout du choix **Detection automatique** comme valeur par defaut, tout en conservant le choix manuel du systeme de coordonnees.
  - Pourquoi : eviter de devoir selectionner le systeme lorsque le fichier contient deja une information exploitable.
- TXT : detection par nom de fichier puis, si necessaire, par plage de coordonnees.
  - Pourquoi : reconnaitre notamment WGS84, Lambert-93 et les zones CC42 a CC50.
- DXF : ajout du detecteur de projection par `GEODATA`, EPSG et propriete geographique active.
  - Pourquoi : preparer l'import DXF sans confondre le systeme actif avec les libelles presents dans les blocs AutoCAD.
- Interface : affichage du systeme detecte et de la methode de detection, avec possibilite de correction manuelle.
- Build : passage de l'application et du moteur PDF en **2.3.0.108**.

## 2.3.0.107

- Export KMZ Google Earth : correction effective de la liste deroulante des systemes de coordonnees dans l'interface.
  - Pourquoi : la liste C# etait correcte, mais l'ecran gardait les anciennes options HTML et ne reconstruisait pas le select apres import TXT.
- Export KMZ Google Earth : CC49 reste selectionnable automatiquement, mais apparait maintenant a sa place entre CC48 et CC50.
  - Pourquoi : conserver l'auto-detection CC49 sans casser l'ordre logique de lecture.
- Build : passage de l'application et du moteur PDF en **2.3.0.107**.
  - Pourquoi : livrer un installeur distinct contenant bien la correction visible dans l'application.

## 2.3.0.106

- Export KMZ Google Earth : correction de l'ordre d'affichage des coniques conformes, avec CC49 place entre CC48 et CC50.
  - Pourquoi : rendre le choix du systeme de coordonnees plus logique et limiter les erreurs de selection.
- Export KMZ Google Earth : ajout des projections NTF Lambert 1, Lambert 2, Lambert 3 et Lambert 2 etendu.
  - Pourquoi : permettre l'export KMZ depuis des fichiers TXT produits dans les anciens systemes Lambert encore rencontres sur certains dossiers.
- Build : passage de l'application et du moteur PDF en **2.3.0.106**.
  - Pourquoi : tracer la correction du choix de projection KMZ.

## 2.3.0.105

- Export KMZ Google Earth : ajout d'un module dedie sous Recolement MNT pour importer un TXT, choisir le systeme de coordonnees, controler les points sur fond de plan en ligne et exporter un KMZ au meme emplacement que le TXT.
  - Pourquoi : produire rapidement des fichiers Google Earth a partir des points terrain, avec controle visuel avant export.
- Coordonnees : prise en charge initiale de WGS84, Lambert-93 et RGF93 CC42 a CC50, avec detection automatique du CC depuis le nom du fichier.
  - Pourquoi : couvrir les usages NOVATLAS courants et eviter les erreurs de projection sur les fichiers nommes en conique conforme.
- Interface : ajout de l'icone globe dans le menu du module KMZ.
  - Pourquoi : identifier visuellement la fonction Google Earth comme les autres modules.
- Build : passage de l'application et du moteur PDF en **2.3.0.105**.
  - Pourquoi : tracer la version validee du module export KMZ.

## 2.3.0.104

- Station / Leve topo : finalisation du module **Transfert alti**.
  - Pourquoi : separer clairement les transferts d'altitude des stations libres et conserver une lecture fiable des donnees `heightTransfer`.
- Visualisation : correction de l'affichage des onglets **Station libre** et **Transfert alti**.
  - Pourquoi : ne plus afficher de tableau station libre vide quand le LandXML contient uniquement des transferts d'altitude.
- PDF Transfert d'altitude : reprise de l'entete et du cartouche sur le gabarit des autres rapports NOVATLAS.
  - Pourquoi : supprimer l'entete tasse et harmoniser la mise en page avec les fiches deja validees.
- PDF Transfert d'altitude : correction du logo sur les pages suivantes.
  - Pourquoi : reserver l'espace du logo avant les tableaux et eviter qu'il se superpose au contenu.
- Build : passage de l'application et du moteur PDF en **2.3.0.104**.
  - Pourquoi : tracer la version finale validee du module transfert altitude.

## 2.3.0.103

- Station / Leve topo : ajout de la lecture des transferts d'altitude Leica (`heightTransfer`).
  - Pourquoi : exploiter les mises en station altimetriques et leurs controles 1D sans les confondre avec une station libre classique.
- Visualisation : ajout de l'onglet **Transfert alti**.
  - Pourquoi : afficher les references altimetriques, les ecarts `deltaHgt`, l'altitude calculee et les points mesures apres transfert.
- PDF : ajout de la fiche **Transfert d'altitude** avec analyse et controles.
  - Pourquoi : documenter ce qui a ete fait, station par station, dans le meme esprit que les autres fiches NOVATLAS.
- Build : passage de l'application et du moteur PDF en **2.3.0.103**.
  - Pourquoi : tracer cette evolution fonctionnelle.

## 2.3.0.102

- Implantation : correction du statut des points quand le Z theorique est absent.
  - Pourquoi : les points avec ecarts XY exploitables ne doivent plus etre comptes en `Non eval.` uniquement parce que le Dz est vide.
- LandXML : conservation des noms de mises en station quand un `InstrumentSetup` portant le meme ID revient plus loin sans `stationName`.
  - Pourquoi : eviter de perdre la station reelle dans les rapports et les regroupements PDF.
- Build : passage de l'application et du moteur PDF en **2.3.0.102**.
  - Pourquoi : identifier clairement cette correction de comptage et de mise en station.

## 2.3.0.101

- Aide : ajout du menu **Historique des mises a jour**.
  - Pourquoi : consulter directement depuis Nova-Fiches la liste des corrections, evolutions et raisons de chaque mise a jour.
- Build : passage de l'application et du moteur PDF en **2.3.0.101**.
  - Pourquoi : distinguer cette version de la 2.3.0.100 et tracer l'ajout de l'historique.
- Qualite : conservation des controles ajoutes sur les doublons LandXML.
  - Pourquoi : garder le choix utilisateur par occurrence via les cases d'inclusion, avec coloration des groupes dans les visualisations.

## 2.3.0.100

- LandXML : detection des doublons metier via `oID` et suffixes Leica de type `@NN`.
  - Pourquoi : un meme point peut etre mesure plusieurs fois sous des occurrences differentes.
- Implantation : ajout des colonnes de visualisation `Point metier`, `Occurrence`, `Horodatage`, `Station`.
  - Pourquoi : rendre les doublons compréhensibles avant generation des fiches.
- Implantation et ligne de reference : choix des occurrences par les cases `Incl.` existantes.
  - Pourquoi : permettre de garder 1, 2, 3 ou aucune occurrence sans ajouter une logique confuse.
- Recolement pieux et recolement MNT : coloration visuelle des groupes.
  - Pourquoi : reperer rapidement les groupes de points ou lignes associes.
- PDF : pas de changement de colonnes pour cette fonction.
  - Pourquoi : conserver le format des fiches existantes; seule la visualisation applicative aide au choix.
- Recolement MNT : module ajoute avec lecture des resultats Leica MNT/DTM et comparaison DXF 3D optionnelle.
  - Pourquoi : produire une fiche de recolement MNT avec X/Y/Z releve, Z theorique et Dz.

## Historique precedent

L'ancien journal technique reste disponible dans `SUIVI_MODIFICATIONS.md`.
