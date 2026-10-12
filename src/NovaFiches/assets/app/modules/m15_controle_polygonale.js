// Module manager "Contrôle de polygonale" (gated par data-nf-feature="controle_polygonale") :
// compare un classeur Excel de contrôle GNSS/optique (onglets "...GNSS..." = XY, "...Niv..." = Z)
// et un fichier GeoBase (TXT) aux coordonnées théoriques, calcule les écarts et génère un rapport
// PDF. Restructuré thème par thème sur un vrai rapport de référence (voir le plan) : page de
// garde, objectifs, déroulé de mission (dates par technique + organigramme d'équipe),
// référentiels, tiroirs GNSS/Optique/Nivellement (matériel + méthodologie, révélés par technique
// activée), résultat, conclusion.
// Comme ControlePrecision (cp_import_leve/cp_import_controle), l'état importé (classeur/GeoBase)
// vit entièrement côté C# à la lecture (ControlePolygonaleService) puis est renvoyé déjà parsé au
// JS, qui le retransmet tel quel dans le payload d'export - aucune re-lecture de fichier n'a lieu
// côté JS pour ça. Les photos de disposition (GNSS/Optique), en revanche, sont lues en pur JS
// (FileReader -> data URL, même principe que le logo de Paramètres) : pas besoin d'aller-retour
// C# pour un simple fichier image local.
(function initControlePolygonaleModule(){
  const state = {
    xySheets: [],
    zSheets: [],
    geobase: [],
    hasXlsx: false,
    gnssDispositionImageDataUrl: '',
    optiqueDispositionImageDataUrl: '',
    mapImageDataUrl: ''
  };

  function el(id){ return document.getElementById(id); }
  function post(payload){
    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function'){
        window.chrome.webview.postMessage(payload);
      }
    }catch(_){ }
  }
  function esc(s){ return String(s ?? '').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }
  function fmt(n, d){ return (n == null || !isFinite(n)) ? '—' : Number(n).toFixed(d ?? 4); }

  // Listes déroulantes "valeur courante + Autre (préciser)" (Référentiels du projet) : même
  // principe que cpTypePlan/cpTypePlanAutre (Contrôle classe de précision) - un select, et si
  // "Autre" est choisi, la valeur vient du champ texte associé à la place.
  function getSelectOrOther(selectId, otherId){
    const sel = el(selectId);
    if(!sel) return '';
    if(sel.value === 'Autre'){
      const other = el(otherId)?.value.trim() || '';
      return other;
    }
    return sel.value;
  }
  function wireSelectOrOtherToggle(selectId, otherId){
    const sel = el(selectId), other = el(otherId);
    if(!sel || !other) return;
    sel.addEventListener('change', () => other.classList.toggle('nf-hidden', sel.value !== 'Autre'));
  }

  function setStatus(id, text, cls){
    const p = el(id);
    if(!p) return;
    p.textContent = text || '';
    p.className = 'small' + (cls ? ' ' + cls : '');
  }

  function isCreationMode(){
    return el('cpolyReportType')?.value === 'creation';
  }

  // Les deux natures de rapport n'ont pas la même source de données pour RESULTAT : Contrôle a
  // besoin du classeur de comparaison (écarts théo/mesuré) ; Création n'a rien à comparer (points
  // nouvellement créés) et s'appuie sur le GeoBase (coordonnées brutes) à la place.
  function updateExportButton(){
    const btn = el('btnCpolyExport');
    if(!btn) return;
    btn.disabled = isCreationMode() ? state.geobase.length === 0 : !state.hasXlsx;
  }

  function updateReportTypeVisibility(){
    el('cpolyResultGroupsWrap')?.classList.toggle('nf-hidden', !isCreationMode());
    updateExportButton();
  }

  // ---------- Tableau de révision (Indice / Date / Établi / Vérifié / Approuvé) ----------
  // Pas d'état JS séparé : les lignes sont lues directement dans le DOM au moment de l'export
  // (comme les autres formulaires de l'appli), le tableau lui-même se construit/détruit via ce
  // module ("fonction à tiroir" la plus simple : ajouter/retirer une ligne).

  function addRevisionRow(values){
    const wrap = el('cpolyRevisionRows');
    if(!wrap) return;
    const row = document.createElement('div');
    row.className = 'row cpoly-revision-row';
    row.style.cssText = 'gap:6px; margin-bottom:6px; align-items:center;';
    row.innerHTML = `
      <input type="text" class="box" style="width:60px;" placeholder="Indice" data-field="indice" />
      <input type="text" class="box" style="width:110px;" placeholder="Date" data-field="date" />
      <input type="text" class="box" style="flex:1;" placeholder="Établi par" data-field="etabli" />
      <input type="text" class="box" style="flex:1;" placeholder="Vérifié par" data-field="verifie" />
      <input type="text" class="box" style="flex:1;" placeholder="Approuvé par" data-field="approuve" />
      <button type="button" title="Supprimer cette ligne">✕</button>
    `;
    const v = values || {};
    row.querySelector('[data-field="indice"]').value = v.indice || '';
    row.querySelector('[data-field="date"]').value = v.date || '';
    row.querySelector('[data-field="etabli"]').value = v.etabli || '';
    row.querySelector('[data-field="verifie"]').value = v.verifie || '';
    row.querySelector('[data-field="approuve"]').value = v.approuve || '';
    row.querySelector('button').addEventListener('click', () => row.remove());
    wrap.appendChild(row);
  }

  function collectRevisionRows(){
    return Array.from(document.querySelectorAll('#cpolyRevisionRows .cpoly-revision-row')).map(row => ({
      indice: row.querySelector('[data-field="indice"]').value,
      date: row.querySelector('[data-field="date"]').value,
      etabli: row.querySelector('[data-field="etabli"]').value,
      verifie: row.querySelector('[data-field="verifie"]').value,
      approuve: row.querySelector('[data-field="approuve"]').value
    }));
  }

  // ---------- Équipe mobilisée : encadrement (niveau 2, 1..N personnes) ----------
  // Même principe "ajouter une ligne" que le tableau de révision - le responsable (niveau 1) et
  // les opérateurs/géomètres (niveau 3) sont chacun un champ unique, seul l'encadrement
  // intermédiaire varie en nombre (chef de mission, éventuel responsable d'agence, etc.).

  function addLevel2Row(values){
    const wrap = el('cpolyLevel2Rows');
    if(!wrap) return;
    const row = document.createElement('div');
    row.className = 'row cpoly-level2-row';
    row.style.cssText = 'gap:6px; margin-bottom:6px; align-items:center;';
    row.innerHTML = `
      <input type="text" class="box" style="flex:1;" placeholder="Nom" data-field="name" />
      <input type="text" class="box" style="flex:1;" placeholder="Rôle" data-field="role" />
      <button type="button" title="Supprimer cette personne">✕</button>
    `;
    const v = values || {};
    row.querySelector('[data-field="name"]').value = v.name || '';
    row.querySelector('[data-field="role"]').value = v.role || '';
    row.querySelector('button').addEventListener('click', () => row.remove());
    wrap.appendChild(row);
  }

  function collectLevel2Rows(){
    return Array.from(document.querySelectorAll('#cpolyLevel2Rows .cpoly-level2-row')).map(row => ({
      name: row.querySelector('[data-field="name"]').value,
      role: row.querySelector('[data-field="role"]').value
    }));
  }

  // ---------- GNSS Statique Post-traitement : antennes ayant servi aux calculs ----------
  // Même principe "ajouter une ligne" que l'encadrement (addLevel2Row) - un nom d'antenne par
  // ligne (numéro de série, nom de station RGP...), pas de contrainte de format.

  function addGnssPpAntenneRow(values){
    const wrap = el('cpolyGnssPpAntenneRows');
    if(!wrap) return;
    const row = document.createElement('div');
    row.className = 'row cpoly-gnss-pp-antenne-row';
    row.style.cssText = 'gap:6px; margin-bottom:6px; align-items:center;';
    row.innerHTML = `
      <input type="text" class="box" style="flex:1;" placeholder="Antenne (ex. n° de série, nom de station RGP)" data-field="nom" />
      <button type="button" title="Supprimer cette antenne">✕</button>
    `;
    const v = values || {};
    row.querySelector('[data-field="nom"]').value = v.nom || '';
    row.querySelector('button').addEventListener('click', () => row.remove());
    wrap.appendChild(row);
  }

  function collectGnssPpAntenneRows(){
    return Array.from(document.querySelectorAll('#cpolyGnssPpAntenneRows .cpoly-gnss-pp-antenne-row')).map(row => ({
      nom: row.querySelector('[data-field="nom"]').value
    }));
  }

  // ---------- Nivellement : repère(s) NGF de rattachement (saisie manuelle d'identifiant) ----------
  // Même principe "ajouter une ligne" que les antennes GNSS - un identifiant de repère par ligne,
  // pas de recherche/sélection interactive (décision utilisateur : saisie manuelle uniquement).

  function addNivNgfRow(values){
    const wrap = el('cpolyNivNgfRows');
    if(!wrap) return;
    const row = document.createElement('div');
    row.className = 'row cpoly-niv-ngf-row';
    row.style.cssText = 'gap:6px; margin-bottom:6px; align-items:center;';
    row.innerHTML = `
      <input type="text" class="box" style="flex:1;" placeholder="Identifiant du repère NGF (ex. 94-1234-B)" data-field="id" />
      <button type="button" title="Supprimer ce repère">✕</button>
    `;
    const v = values || {};
    row.querySelector('[data-field="id"]').value = v.id || '';
    row.querySelector('button').addEventListener('click', () => row.remove());
    wrap.appendChild(row);
  }

  function collectNivNgfRows(){
    return Array.from(document.querySelectorAll('#cpolyNivNgfRows .cpoly-niv-ngf-row')).map(row => ({
      id: row.querySelector('[data-field="id"]').value
    }));
  }

  // ---------- Annexes (PDF) : fichiers PDF libres joints (pages fusionnées) en fin de rapport ----------
  // Lecture pure JS (FileReader -> data URL, même principe que les photos de disposition), plusieurs
  // fichiers possibles en un seul choix (input multiple) - chaque fichier devient une ligne retirable.
  // Le data URL est gardé sur la ligne elle-même (dataset.dataUrl) plutôt que dans un data-field
  // affiché : potentiellement volumineux, pas destiné à être relu/édité comme du texte.
  function addAnnexeRow(values){
    const wrap = el('cpolyAnnexeRows');
    if(!wrap) return null;
    const row = document.createElement('div');
    row.className = 'row cpoly-annexe-row';
    row.style.cssText = 'gap:6px; margin-bottom:6px; align-items:center;';
    const v = values || {};
    row.dataset.dataUrl = v.dataUrl || '';
    row.innerHTML = `
      <span class="small" style="flex:1;" data-field="name"></span>
      <button type="button" title="Retirer cette annexe">✕</button>
    `;
    row.querySelector('[data-field="name"]').textContent = v.name || '';
    row.querySelector('button').addEventListener('click', () => row.remove());
    wrap.appendChild(row);
    return row;
  }

  function collectAnnexeRows(){
    return Array.from(document.querySelectorAll('#cpolyAnnexeRows .cpoly-annexe-row'))
      .map(row => ({ name: row.querySelector('[data-field="name"]').textContent, dataUrl: row.dataset.dataUrl || '' }))
      .filter(a => a.dataUrl);
  }

  function wireAnnexeFileInput(){
    const input = el('cpolyAnnexeFile');
    if(!input) return;
    input.addEventListener('change', ev => {
      Array.from(ev.target.files || []).forEach(file => {
        const row = addAnnexeRow({ name: file.name + ' (chargement…)' });
        const reader = new FileReader();
        reader.onload = function(){
          if(row) row.dataset.dataUrl = String(reader.result || '');
          const nameEl = row && row.querySelector('[data-field="name"]');
          if(nameEl) nameEl.textContent = file.name;
        };
        reader.onerror = function(){
          const nameEl = row && row.querySelector('[data-field="name"]');
          if(nameEl) nameEl.textContent = file.name + ' (illisible)';
        };
        reader.readAsDataURL(file);
      });
      input.value = '';
    });
  }

  // ---------- RESULTAT (mode Création) : groupes de points par préfixe de nom ----------
  // Le GeoBase importé est une liste à plat (Point/X/Y/Z, sans notion de groupe). Le document de
  // référence affiche plusieurs tableaux nommés ("Couples GNSS", "Les stations au sol"...) - on
  // laisse l'utilisateur définir ces groupes par préfixe de nom de point (ex. "BO" -> Les stations
  // au sol), plutôt que d'imposer un classement automatique ou une case à cocher point par point.

  function addResultGroupRow(values){
    const wrap = el('cpolyResultGroupRows');
    if(!wrap) return;
    const row = document.createElement('div');
    row.className = 'row cpoly-resultgroup-row';
    row.style.cssText = 'gap:6px; margin-bottom:6px; align-items:center;';
    row.innerHTML = `
      <input type="text" class="box" style="flex:1;" placeholder="Nom du groupe (ex. Les stations au sol)" data-field="name" />
      <input type="text" class="box" style="flex:1;" placeholder="Préfixes de point, séparés par une virgule (ex. BO)" data-field="prefixes" />
      <button type="button" title="Supprimer ce groupe">✕</button>
    `;
    const v = values || {};
    row.querySelector('[data-field="name"]').value = v.name || '';
    row.querySelector('[data-field="prefixes"]').value = v.prefixes || '';
    row.querySelector('button').addEventListener('click', () => row.remove());
    wrap.appendChild(row);
  }

  function collectResultGroups(){
    return Array.from(document.querySelectorAll('#cpolyResultGroupRows .cpoly-resultgroup-row'))
      .map(row => ({
        name: row.querySelector('[data-field="name"]').value.trim(),
        prefixes: row.querySelector('[data-field="prefixes"]').value.split(',').map(s => s.trim()).filter(Boolean)
      }))
      .filter(g => g.name && g.prefixes.length);
  }

  // Répartit le GeoBase importé selon les groupes définis (premier préfixe correspondant gagne),
  // les points non classés vont dans un groupe "Autres" ajouté seulement s'il n'est pas vide. Sans
  // aucun groupe défini, le GeoBase entier devient un unique tableau "Coordonnées" (cas simple : un
  // seul groupe de points, pas la peine de configurer quoi que ce soit).
  function buildResultTablesForCreation(){
    const groups = collectResultGroups();
    if(!groups.length){
      return state.geobase.length ? [{ title: 'Coordonnées', rows: state.geobase.slice() }] : [];
    }
    const buckets = groups.map(g => ({ title: g.name, rows: [] }));
    const others = [];
    state.geobase.forEach(pt => {
      const name = String(pt.point ?? '').toUpperCase();
      const idx = groups.findIndex(g => g.prefixes.some(p => p && name.startsWith(p.toUpperCase())));
      if(idx >= 0) buckets[idx].rows.push(pt);
      else others.push(pt);
    });
    const result = buckets.filter(b => b.rows.length);
    if(others.length) result.push({ title: 'Autres', rows: others });
    return result;
  }

  // ---------- Déroulé de la mission : dates par technique + tiroirs "Mise en place" ----------
  // Une seule case à cocher par technique pilote à la fois sa date (Déroulé de la mission) et son
  // tiroir matériel/méthodologie (Mise en place de la polygonale) - une technique activée pour la
  // mission doit forcément avoir les deux, pas la peine de dupliquer l'état.

  function updateTechDateVisibility(){
    [
      ['cpolyGnssEnabled', 'cpolyGnssDateWrap'], ['cpolyGnssEnabled', 'cpolyGnssDrawerWrap'],
      ['cpolyNivEnabled', 'cpolyNivDateWrap'], ['cpolyNivEnabled', 'cpolyNivDrawerWrap'],
      ['cpolyOptiqueEnabled', 'cpolyOptiqueDateWrap'], ['cpolyOptiqueEnabled', 'cpolyOptiqueDrawerWrap']
    ].forEach(([chkId, wrapId]) => {
        const chk = el(chkId), wrap = el(wrapId);
        if(chk && wrap) wrap.classList.toggle('nf-hidden', !chk.checked);
      });
    // Rappel affiché dans l'étape de l'assistant (thème 5) quand la technique correspondante
    // n'est pas activée - le tiroir lui-même (wrap ci-dessus) reste alors masqué, seule l'étape
    // du menu latéral doit expliquer pourquoi la page est vide plutôt que la laisser muette.
    [
      ['cpolyGnssEnabled', 'cpolyGnssDisabledHint'],
      ['cpolyNivEnabled', 'cpolyNivDisabledHint'],
      ['cpolyOptiqueEnabled', 'cpolyOptiqueDisabledHint']
    ].forEach(([chkId, hintId]) => {
        const chk = el(chkId), hint = el(hintId);
        if(chk && hint) hint.classList.toggle('nf-hidden', chk.checked);
      });
  }

  // Un seul tiroir de méthode GNSS visible selon le choix du select - options propres à chaque
  // méthode (Statique Post-traitement / Statique RTK / Temps réel RTK) pas encore définies, juste
  // le cadre pour l'instant.
  function updateGnssMethodeVisibility(){
    const val = el('cpolyGnssMethode')?.value || '';
    [
      ['statique_pp', 'cpolyGnssMethodeStatiquePpWrap'],
      ['statique_pp_pivot', 'cpolyGnssMethodeStatiquePpPivotWrap'],
      ['statique_rtk', 'cpolyGnssMethodeStatiqueRtkWrap'],
      ['temps_reel_rtk', 'cpolyGnssMethodeTempsReelRtkWrap'],
      ['autre', 'cpolyGnssMethodeAutreWrap']
    ].forEach(([optVal, wrapId]) => {
      el(wrapId)?.classList.toggle('nf-hidden', val !== optVal);
    });
  }

  // Même principe que updateGnssMethodeVisibility(), pour les deux sélecteurs Optique (méthode
  // d'implantation : 3 trépieds/Station libre ; méthode de calcul : bloc/polygonale/ajustement) -
  // deux choix indépendants, chacun avec ses propres tiroirs.
  function updateOptiqueMethodeVisibility(){
    const val = el('cpolyOptiqueMethode')?.value || '';
    [
      ['trois_trepieds', 'cpolyOptiqueMethodeTroisTrepiedsWrap'],
      ['station_libre', 'cpolyOptiqueMethodeStationLibreWrap'],
      ['autre', 'cpolyOptiqueMethodeAutreWrap']
    ].forEach(([optVal, wrapId]) => {
      el(wrapId)?.classList.toggle('nf-hidden', val !== optVal);
    });
  }
  function updateOptiqueCalculVisibility(){
    const val = el('cpolyOptiqueMethodeCalcul')?.value || '';
    [
      ['bloc', 'cpolyOptiqueCalculBlocWrap'],
      ['polygonale', 'cpolyOptiqueCalculPolygonaleWrap'],
      ['ajustement', 'cpolyOptiqueCalculAjustementWrap']
    ].forEach(([optVal, wrapId]) => {
      el(wrapId)?.classList.toggle('nf-hidden', val !== optVal);
    });
  }

  // Même principe pour le Nivellement (un seul sélecteur, contrairement à Optique qui en a deux) -
  // Cholesky/ABBA (nivellement direct) contre nivellement indirect (trigonométrique, via la station
  // totale plutôt que le niveau numérique).
  function updateNivMethodeVisibility(){
    const val = el('cpolyNivMethode')?.value || '';
    [
      ['nivellement_simple', 'cpolyNivMethodeSimpleWrap'],
      ['cholesky', 'cpolyNivMethodeCholeskyWrap'],
      ['nivellement_indirect', 'cpolyNivMethodeIndirectWrap'],
      ['autre', 'cpolyNivMethodeAutreWrap']
    ].forEach(([optVal, wrapId]) => {
      el(wrapId)?.classList.toggle('nf-hidden', val !== optVal);
    });
  }

  // ---------- Assistant par étape ----------
  // Un seul panneau visible à la fois (nf-hidden sur les autres), navigation libre par la barre
  // latérale (pas de verrouillage séquentiel) + boutons Suivant/Précédent qui cyclent dans l'ordre
  // du rapport. Aucun champ n'est déplacé hors du module - showStep() bascule juste la visibilité.
  const CPOLY_STEPS = ['garde', 'objectifs', 'deroule', 'referentiels', 'gnss', 'optique', 'nivellement', 'resultat', 'conclusion', 'annexes'];

  function showStep(stepId){
    if(CPOLY_STEPS.indexOf(stepId) < 0) return;
    document.querySelectorAll('.cpoly-step[data-cpoly-step-panel]').forEach(panel => {
      panel.classList.toggle('nf-hidden', panel.getAttribute('data-cpoly-step-panel') !== stepId);
    });
    document.querySelectorAll('.cpoly-step-nav[data-cpoly-step]').forEach(btn => {
      btn.classList.toggle('active', btn.getAttribute('data-cpoly-step') === stepId);
    });
    positionWizardFooter();
  }

  // La barre d'actions (.cpoly-wizard-footer, position:fixed en CSS) doit rester ancrée juste
  // au-dessus du pied de page NOVATLAS (#pageFooter) et alignée avec la largeur réelle de la carte
  // du module - ni l'un ni l'autre n'est une valeur CSS fixe fiable (largeur de la barre latérale
  // variable selon la largeur de fenêtre, hauteur du pied de page non garantie constante), donc
  // calculé ici à partir des rectangles réels. Rappelé au changement d'étape (showStep) par prudence
  // (ex. apparition/disparition d'une barre de défilement qui décalerait légèrement la largeur de la
  // carte), même si en pratique la hauteur de main.nf-content est bornée par le layout flex et ne
  // varie pas avec le contenu de l'étape active.
  function positionWizardFooter(){
    const footer = document.querySelector('.cpoly-wizard-footer');
    const pageFooter = document.getElementById('pageFooter');
    const card = document.querySelector('#module-controle-polygonale .card');
    if(!footer || !pageFooter || !card) return;
    const pfRect = pageFooter.getBoundingClientRect();
    const cardRect = card.getBoundingClientRect();
    footer.style.left = Math.round(cardRect.left) + 'px';
    footer.style.width = Math.round(cardRect.width) + 'px';
    footer.style.bottom = Math.max(0, Math.round(window.innerHeight - pfRect.top)) + 'px';
  }

  function currentStepId(){
    const active = document.querySelector('.cpoly-step-nav[data-cpoly-step].active');
    return active ? active.getAttribute('data-cpoly-step') : CPOLY_STEPS[0];
  }

  function stepBy(delta){
    const idx = CPOLY_STEPS.indexOf(currentStepId());
    const next = Math.min(Math.max(idx + delta, 0), CPOLY_STEPS.length - 1);
    showStep(CPOLY_STEPS[next]);
  }

  // Un calendrier par technique (1 jour ou une plage) plutôt qu'un texte libre - reproduit
  // exactement le format déjà consommé par le renderer PDF ("10.07.26" pour un seul jour,
  // "du 13.07.26 au 15.07.26" pour une plage), donc aucun changement côté
  // ControlePolygonaleRenderer.cs : il continue de recevoir un simple `dateText`.
  function formatDateShort(isoDate){
    if(!isoDate) return '';
    const d = new Date(isoDate + 'T00:00:00');
    if(isNaN(d.getTime())) return '';
    const dd = String(d.getDate()).padStart(2, '0');
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const yy = String(d.getFullYear()).slice(-2);
    return `${dd}.${mm}.${yy}`;
  }
  function formatDateRange(startId, endId, rangeCheckboxId){
    const start = formatDateShort(el(startId)?.value || '');
    if(!start) return '';
    const isRange = !!el(rangeCheckboxId)?.checked;
    const end = isRange ? formatDateShort(el(endId)?.value || '') : '';
    return end ? `du ${start} au ${end}` : start;
  }
  function wireDateRangeToggle(rangeCheckboxId, endId){
    const chk = el(rangeCheckboxId), end = el(endId);
    if(!chk || !end) return;
    chk.addEventListener('change', () => end.classList.toggle('nf-hidden', !chk.checked));
  }

  // ---------- Tiroirs GNSS/Optique : disposition (photo), matériel, méthodologie ----------

  // Un texte pré-rempli par méthode GNSS (Statique Post-traitement / Statique RTK / Temps réel
  // RTK), proposé dans le champ méthodologie partagé au changement de méthode - textes rédigés et
  // validés avec l'utilisateur avant intégration. "statique_pp" sert aussi de texte par défaut tant
  // qu'aucune méthode n'est choisie (comportement historique de ce champ, avant l'ajout du
  // sélecteur de méthode).
  const GNSS_METHODOLOGY_BY_METHODE = {
    statique_pp:
      "La méthodologie suivie est celle du positionnement statique post-traité : chaque point est " +
      "stationné pendant une durée adaptée à la précision recherchée, en utilisant les données " +
      "d'antennes de référence (RGP ou stations de référence dédiées).\n" +
      "Les observations brutes sont ensuite calculées en post-traitement à l'aide d'un logiciel " +
      "intégrant la compensation par moindres carrés, ce qui permet d'obtenir la meilleure " +
      "précision possible, au prix d'un délai de calcul après l'intervention terrain.",
    statique_rtk:
      "La méthodologie suivie est celle du statique RTK : chaque point est stationné en position " +
      "fixe pendant une durée adaptée (de quelques dizaines de secondes à quelques minutes selon " +
      "la précision recherchée), la solution RTK étant moyennée sur toute la durée de " +
      "l'observation plutôt que relevée instantanément.\n" +
      "Cette approche améliore la fiabilité et la précision par rapport à un relevé RTK ponctuel, " +
      "tout en restant disponible immédiatement sur le terrain (pas de post-traitement différé).",
    temps_reel_rtk:
      "La méthodologie suivie est celle du RTK en temps réel : chaque point est relevé de manière " +
      "instantanée dès l'obtention d'une solution fixe, les corrections différentielles étant " +
      "reçues en continu depuis un réseau de stations de référence (RGP) ou une station de base " +
      "dédiée.\n" +
      "Cette méthode permet un rendement élevé sur le terrain, la précision et la validité de " +
      "chaque mesure étant contrôlées en direct (indicateur de qualité de la solution) avant de " +
      "passer au point suivant.",
  };
  const GNSS_METHODOLOGY_DEFAULT = GNSS_METHODOLOGY_BY_METHODE.statique_pp;

  // Propose le texte pré-rempli de la méthode choisie dans le champ méthodologie partagé - mais
  // seulement si ce champ est vide ou contient encore l'un des textes pré-remplis connus (pas de
  // saisie manuelle en cours), pour ne jamais écraser une modification de l'utilisateur en changeant
  // simplement de méthode. Toujours modifiable ensuite, comme les autres méthodologies du module.
  function applyGnssMethodologyDefault(){
    const methode = el('cpolyGnssMethode')?.value || '';
    const text = GNSS_METHODOLOGY_BY_METHODE[methode];
    const field = el('cpolyGnssMethodology');
    if(!field || !text) return;
    const knownDefaults = Object.values(GNSS_METHODOLOGY_BY_METHODE);
    if(!field.value || knownDefaults.includes(field.value)) field.value = text;
  }

  // Même principe que GNSS_METHODOLOGY_BY_METHODE : un texte pré-rempli par méthode
  // d'implantation, proposé dans le champ méthodologie partagé au changement de méthode - textes
  // rédigés et validés avec l'utilisateur avant intégration. "trois_trepieds" reprend tel quel le
  // texte déjà en place (issu du rapport de référence) et sert de texte par défaut tant qu'aucune
  // méthode n'est choisie (comportement historique de ce champ).
  const OPTIQUE_METHODOLOGY_BY_METHODE = {
    trois_trepieds:
      "La mise en place de la polygonale est effectuée par la méthode des 3 trépieds et Tour d'Horizon.\n" +
      "Le principe de cette méthode consiste à mettre en place des trépieds équipés d'embases et de " +
      "prismes sur tous les points visibles depuis la station à déterminer. Chaque point est " +
      "stationné en centrage forcé.\n" +
      "Nous utilisons l'applicatif Tour d'Horizon qui permet de mesurer en double retournement les " +
      "points de références sur plusieurs séquences. Ces manipulations permettent de réduire et de " +
      "détecter les éventuelles erreurs de mesure.\n" +
      "Lors des déplacements pour la réalisation du cheminement polygonal, le trépied et l'embase " +
      "restent en place sur le point. Seul l'appareil est déplacé sur le nouveau point à mesurer.\n" +
      "Les conditions météorologiques sont entrées avant chaque intervention (matin et après-midi) " +
      "de façon à appliquer les corrections nécessaires et réduire les erreurs.\n\n" +
      "Le mode opératoire est le suivant :\n" +
      "Préparation bureau : étude des éléments, organisation, optimisation depuis les points de " +
      "références.\n" +
      "Intervention terrain : mise en station, relevé de tous les éléments, déplacement de station " +
      "en station.",
    station_libre:
      "La mise en place de la polygonale est effectuée par la méthode de la station libre " +
      "(relèvement).\n" +
      "Le principe de cette méthode consiste à stationner librement l'appareil en un point " +
      "quelconque du chantier, sans nécessité de centrage sur un point connu, puis à déterminer ses " +
      "coordonnées et son orientation par relèvement sur plusieurs points de référence déjà connus " +
      "(au moins deux, trois recommandés pour un contrôle de fermeture).\n" +
      "Cette méthode offre une grande souplesse d'implantation des stations, en s'affranchissant des " +
      "contraintes de visibilité entre points de cheminement successifs, et permet de contrôler " +
      "immédiatement la qualité du relèvement (résidus) avant de poursuivre les mesures.",
  };
  const OPTIQUE_METHODOLOGY_DEFAULT = OPTIQUE_METHODOLOGY_BY_METHODE.trois_trepieds;

  function applyOptiqueMethodologyDefault(){
    const methode = el('cpolyOptiqueMethode')?.value || '';
    const text = OPTIQUE_METHODOLOGY_BY_METHODE[methode];
    const field = el('cpolyOptiqueMethodology');
    if(!field || !text) return;
    const knownDefaults = Object.values(OPTIQUE_METHODOLOGY_BY_METHODE);
    if(!field.value || knownDefaults.includes(field.value)) field.value = text;
  }

  // Même principe pour la méthode de calcul (En bloc / Polygonale / Ajustement), sur le champ texte
  // dédié cpolyOptiqueCalculText plutôt que le champ méthodologie partagé - c'est un texte
  // conceptuellement distinct (comment les coordonnées sont réduites, pas comment la station est
  // implantée).
  const OPTIQUE_CALCUL_BY_METHODE = {
    bloc:
      "Le calcul est effectué par transformation de Helmert (similitude plane à 4 paramètres : " +
      "translation, rotation, échelle) entre le système de coordonnées local du levé et le système " +
      "de coordonnées du projet, à partir des points communs disposant de coordonnées dans les deux " +
      "systèmes.\n" +
      "Cette méthode permet de recaler en bloc l'ensemble des points levés sans en modifier la " +
      "géométrie relative, et donne directement un résidu par point commun permettant de contrôler " +
      "la qualité du recalage.",
    polygonale:
      "Le calcul est effectué par cheminement polygonal : les coordonnées de chaque station sont " +
      "déterminées de proche en proche à partir de la station précédente (gisement et distance), " +
      "avec fermeture angulaire et planimétrique sur les points de référence encadrant le " +
      "cheminement.\n" +
      "Les écarts de fermeture constatés sont répartis sur l'ensemble des stations du cheminement " +
      "(compensation proportionnelle), ce qui permet de contrôler la cohérence globale avant " +
      "validation des coordonnées définitives.",
    ajustement:
      "Le calcul est effectué par ajustement du réseau par compensation par moindres carrés, " +
      "intégrant l'ensemble des observations (angles, distances) et leur redondance pour déterminer " +
      "la position la plus probable de chaque point.\n" +
      "Cette méthode permet d'exploiter toutes les observations disponibles, y compris les mesures " +
      "surabondantes, et fournit pour chaque point des indicateurs de précision (ellipses d'erreur) " +
      "permettant de qualifier objectivement le résultat du calcul.",
  };

  function applyOptiqueCalculDefault(){
    const methode = el('cpolyOptiqueMethodeCalcul')?.value || '';
    const text = OPTIQUE_CALCUL_BY_METHODE[methode];
    const field = el('cpolyOptiqueCalculText');
    if(!field || !text) return;
    const knownDefaults = Object.values(OPTIQUE_CALCUL_BY_METHODE);
    if(!field.value || knownDefaults.includes(field.value)) field.value = text;
  }

  // Même principe que GNSS/OPTIQUE_METHODOLOGY_BY_METHODE - "cholesky" reprend tel quel le texte
  // déjà en place et sert de texte par défaut tant qu'aucune méthode n'est choisie.
  const NIVELLEMENT_METHODOLOGY_BY_METHODE = {
    cholesky:
      "La méthode utilisée pour le nivellement direct est la méthode Cholesky à doubles points de " +
      "mire. Cette méthode consiste à mener simultanément deux cheminements distincts en plaçant la " +
      "mire de nivellement successivement en deux points situés à l'arrière, puis en deux points " +
      "situés à l'avant, et ainsi de suite, permettant un contrôle de marche lors de chaque " +
      "station. On calcule séparément les deux cheminements et on compare les résultats obtenus.\n" +
      "L'appareil Leica propose ce type de cheminement classé sous la dénomination ABBA : une mire " +
      "sur le point A, puis sur le point B, puis B1, puis A1.\n" +
      "Tous les points de la polygonale sont observés par méthode de nivellement direct. La portée " +
      "des mesures ne dépasse pas 40 m, sauf en cas de nécessité.",
    nivellement_indirect:
      "La méthode utilisée est celle du nivellement indirect (trigonométrique) : la dénivelée entre " +
      "deux points est déterminée à partir de la distance inclinée mesurée à la station totale et de " +
      "l'angle zénithal (ou vertical) observé, plutôt que par lecture directe sur une mire graduée.\n" +
      "Cette méthode permet de mesurer des dénivelées importantes ou des points difficilement " +
      "accessibles en nivellement direct (fort dénivelé, obstacles), au prix d'une précision " +
      "généralement inférieure au nivellement direct pour de courtes portées.\n" +
      "Les mesures sont effectuées en double retournement (double face de l'appareil) afin de " +
      "s'affranchir des principales erreurs systématiques (erreur d'index du zénithal, défaut de " +
      "collimation verticale) et de contrôler la cohérence des dénivelées obtenues.",
  };
  const NIVELLEMENT_METHODOLOGY_DEFAULT = NIVELLEMENT_METHODOLOGY_BY_METHODE.cholesky;

  function applyNivMethodologyDefault(){
    const methode = el('cpolyNivMethode')?.value || '';
    const text = NIVELLEMENT_METHODOLOGY_BY_METHODE[methode];
    const field = el('cpolyNivMethodology');
    if(!field || !text) return;
    const knownDefaults = Object.values(NIVELLEMENT_METHODOLOGY_BY_METHODE);
    if(!field.value || knownDefaults.includes(field.value)) field.value = text;
  }

  // Une case à cocher "disposition" par technique (GNSS, Optique) : photo optionnelle lue en pur
  // JS (FileReader -> data URL, même principe que le logo de Paramètres), stockée dans
  // state[stateKey] et envoyée telle quelle dans le payload d'export.
  function wireDispositionPhoto(stateKey, fileInputId, previewId, statusId, clearBtnId){
    const input = el(fileInputId);
    if(input) input.addEventListener('change', ev => {
      const file = ev.target.files && ev.target.files[0];
      if(!file) return;
      const reader = new FileReader();
      reader.onload = function(){
        state[stateKey] = String(reader.result || '');
        const preview = el(previewId);
        if(preview){ preview.src = state[stateKey]; preview.classList.remove('nf-hidden'); }
        setStatus(statusId, file.name);
        el(clearBtnId)?.classList.remove('nf-hidden');
      };
      reader.onerror = function(){ setStatus(statusId, 'Photo illisible.', 'err'); };
      reader.readAsDataURL(file);
    });
    el(clearBtnId)?.addEventListener('click', () => {
      state[stateKey] = '';
      const preview = el(previewId);
      if(preview){ preview.src = ''; preview.classList.add('nf-hidden'); }
      setStatus(statusId, 'Aucune photo');
      el(clearBtnId)?.classList.add('nf-hidden');
      if(input) input.value = '';
    });
  }

  // ---------- Carte de situation auto-générée ----------
  // Une seule carte (pas une par technique) : les tiroirs GNSS/Optique ne distinguent pas les
  // points par technique dans ce module - même logique que ControlePrecisionMapService (voir
  // MainForm.cs GenerateCpMapFromPointsAsync). Système de coordonnées auto-détecté côté C# à partir
  // des valeurs X/Y elles-mêmes (KmzExportService.DetectCoordinateSystem) plutôt que dépendant du
  // champ Projection des Référentiels : ce champ est un texte déclaratif pour le rapport (peut
  // valoir "Autre (préciser)" ou toute valeur libre), pas forcément une valeur que ToWgs84
  // reconnaît littéralement - le faire dépendre de ce champ faisait donc échouer la génération de
  // carte sans rapport avec un vrai problème de coordonnées (retour utilisateur direct).
  // Utilisée en repli dans les DEUX tiroirs GNSS et Optique (photo manuelle toujours prioritaire).
  // Uniquement le GeoBase (liste de points nommés) comme source, quel que soit le mode
  // Contrôle/Création - le classeur de comparaison (écarts théo/mesuré XY/Z) n'a pas vocation à
  // fournir des points de carte, retour utilisateur explicite après le déplacement du bouton
  // d'import GeoBase dans "Référentiels" (thème 4).
  function collectMapPoints(){
    return state.geobase
      .filter(p => isFinite(p.x) && isFinite(p.y))
      .map(p => ({ id: p.point, x: p.x, y: p.y }));
  }

  function generateMap(){
    const points = collectMapPoints();
    if(!points.length){
      setStatus('cpolyMapStatus', "Importe d'abord le fichier GeoBase (TXT).", 'err');
      return;
    }
    setStatus('cpolyMapStatus', 'Génération de la carte en cours…');
    const zoomRaw = el('cpolyMapZoom')?.value || '';
    const payload = { type: 'cpoly_generate_map', points };
    if(zoomRaw) payload.maxZoom = parseInt(zoomRaw, 10);
    post(payload);
  }

  function handleMapResult(msg){
    if(!msg.ok){
      setStatus('cpolyMapStatus', String(msg.error || 'Échec de la génération de la carte.'), 'err');
      return;
    }
    state.mapImageDataUrl = msg.image || '';
    showMapPreview();
    const parts = [];
    if(msg.coordinateSystem) parts.push(`système détecté : ${msg.coordinateSystem}`);
    if(msg.zoom != null) parts.push(`zoom ${msg.zoom}`);
    setStatus('cpolyMapStatus', parts.length ? `Carte générée (${parts.join(', ')}).` : 'Carte générée.');
  }

  function showMapPreview(){
    const img = el('cpolyMapPreview');
    if(!img) return;
    if(state.mapImageDataUrl){ img.src = state.mapImageDataUrl; img.classList.remove('nf-hidden'); }
    else{ img.src = ''; img.classList.add('nf-hidden'); }
  }

  function renderPreview(){
    const wrap = el('cpolyPreview');
    const content = el('cpolyPreviewContent');
    if(!wrap || !content) return;

    const parts = [];
    state.xySheets.forEach(s => {
      const st = s.stats || {};
      parts.push(`<div><b>${esc(s.name)}</b> (XY) : ${st.complete ?? 0}/${st.count ?? 0} point(s) contrôlé(s) — écart max ${fmt(st.maxDeltaXy)} m${st.maxDeltaXyPoint ? ' (' + esc(st.maxDeltaXyPoint) + ')' : ''}, moyen ${fmt(st.meanDeltaXy)} m.</div>`);
    });
    state.zSheets.forEach(s => {
      const st = s.stats || {};
      parts.push(`<div><b>${esc(s.name)}</b> (Z) : ${st.complete ?? 0}/${st.count ?? 0} point(s) contrôlé(s) — écart max ${fmt(st.maxAbsDz)} m${st.maxAbsDzPoint ? ' (' + esc(st.maxAbsDzPoint) + ')' : ''}, moyen ${fmt(st.meanAbsDz)} m.</div>`);
    });
    if(state.geobase.length) parts.push(`<div><b>GeoBase</b> : ${state.geobase.length} point(s).</div>`);

    if(!parts.length){
      wrap.classList.add('nf-hidden');
      return;
    }
    content.innerHTML = parts.join('');
    wrap.classList.remove('nf-hidden');
  }

  // ---------- Sauvegarde / rechargement du projet ("Fichier > Enregistrer/Ouvrir le projet") ----------
  // Mécanisme générique déjà en place côté C# (SaveProjectAsync/OpenProjectAsync -> fichier .nova)
  // qui appelle window.NOVA_getState()/NOVA_setState(data) côté JS. On s'y raccorde sans toucher
  // m01_core.js, par le même principe d'enrobage que m09_photos.js (wrapState) : on sauve/relit les
  // valeurs brutes des champs (pas le texte déjà mis en forme pour le PDF), pour que le rechargement
  // reproduise exactement l'état affiché - selects "Autre" et cases "Plage de dates" comprises.

  function fv(id){ return el(id)?.value ?? ''; }
  function fc(id){ return !!el(id)?.checked; }

  function collectResultGroupRowsRaw(){
    return Array.from(document.querySelectorAll('#cpolyResultGroupRows .cpoly-resultgroup-row')).map(row => ({
      name: row.querySelector('[data-field="name"]').value,
      prefixes: row.querySelector('[data-field="prefixes"]').value
    }));
  }

  function buildProjectState(){
    return {
      reportType: fv('cpolyReportType'),
      level: fv('cpolyLevel'),
      missionObjectiveText: fv('cpolyMissionObjectiveText'),
      conclusionText: fv('cpolyConclusionText'),
      projectName: fv('cpolyProjectName'),
      client: fv('cpolyClient'),
      siteCode: fv('cpolySiteCode'),
      ville: fv('cpolyVille'),
      datum: fv('cpolyDatum'), datumAutre: fv('cpolyDatumAutre'),
      projection: fv('cpolyProjection'), projectionAutre: fv('cpolyProjectionAutre'),
      altSystem: fv('cpolyAltSystem'), altSystemAutre: fv('cpolyAltSystemAutre'),
      revisionRows: collectRevisionRows(),
      team: {
        responsibleName: fv('cpolyRespName'),
        responsibleRole: fv('cpolyRespRole'),
        level2: collectLevel2Rows(),
        surveyorsText: fv('cpolySurveyorsText'),
        surveyorRole: fv('cpolySurveyorRole')
      },
      techniques: {
        gnss: {
          enabled: fc('cpolyGnssEnabled'),
          dateStart: fv('cpolyGnssDateStart'), dateEnd: fv('cpolyGnssDateEnd'), dateRange: fc('cpolyGnssDateRange'),
          deviceKey: fv('cpolyGnssDevice'), methode: fv('cpolyGnssMethode'), methodeAutreText: fv('cpolyGnssMethodeAutreText'), methodologyText: fv('cpolyGnssMethodology'),
          ppAntennesCount: fv('cpolyGnssPpAntennesCount'), ppAntennes: collectGnssPpAntenneRows(),
          dispositionImageDataUrl: state.gnssDispositionImageDataUrl
        },
        nivellement: {
          enabled: fc('cpolyNivEnabled'),
          dateStart: fv('cpolyNivDateStart'), dateEnd: fv('cpolyNivDateEnd'), dateRange: fc('cpolyNivDateRange'),
          deviceKey: fv('cpolyNivDevice'), methode: fv('cpolyNivMethode'), methodeAutreText: fv('cpolyNivMethodeAutreText'), methodologyText: fv('cpolyNivMethodology'),
          ngfEnabled: fc('cpolyNivNgfEnabled'), ngfRepereIds: collectNivNgfRows()
        },
        optique: {
          enabled: fc('cpolyOptiqueEnabled'),
          dateStart: fv('cpolyOptiqueDateStart'), dateEnd: fv('cpolyOptiqueDateEnd'), dateRange: fc('cpolyOptiqueDateRange'),
          deviceKey: fv('cpolyOptiqueDevice'), methode: fv('cpolyOptiqueMethode'), methodeAutreText: fv('cpolyOptiqueMethodeAutreText'), methodologyText: fv('cpolyOptiqueMethodology'),
          methodeCalcul: fv('cpolyOptiqueMethodeCalcul'), calculText: fv('cpolyOptiqueCalculText'),
          dispositionImageDataUrl: state.optiqueDispositionImageDataUrl,
          ppmApplied: fc('cpolyOptiquePpmApplied'), ppmValue: fv('cpolyOptiquePpmValue')
        }
      },
      resultGroups: collectResultGroupRowsRaw(),
      xySheets: state.xySheets,
      zSheets: state.zSheets,
      hasXlsx: state.hasXlsx,
      geobase: state.geobase,
      mapImageDataUrl: state.mapImageDataUrl,
      annexes: collectAnnexeRows()
    };
  }

  function applyDispositionPreview(dataUrl, previewId, statusId, clearBtnId){
    const preview = el(previewId);
    if(dataUrl){
      if(preview){ preview.src = dataUrl; preview.classList.remove('nf-hidden'); }
      setStatus(statusId, 'Photo restaurée depuis le projet.');
      el(clearBtnId)?.classList.remove('nf-hidden');
    }else{
      if(preview){ preview.src = ''; preview.classList.add('nf-hidden'); }
      setStatus(statusId, 'Aucune photo');
      el(clearBtnId)?.classList.add('nf-hidden');
    }
  }

  // Restauration manuelle (pas via le sv() générique de NOVA_setState, qui ne connaît pas les
  // lignes dynamiques de ce module) : on vide puis re-remplit les tableaux de lignes via les
  // fonctions add*Row déjà utilisées par les boutons "+", puis on redéclenche à la main les mises à
  // jour de visibilité/aperçu qu'un simple événement `change` aurait déclenchées sur une saisie
  // manuelle normale.
  function restoreProjectState(data){
    const sv = (id, value) => { const e = el(id); if(e) e.value = value ?? ''; };
    const sc = (id, checked) => { const e = el(id); if(e) e.checked = !!checked; };

    sv('cpolyReportType', data.reportType);
    sv('cpolyLevel', data.level);
    sv('cpolyMissionObjectiveText', data.missionObjectiveText);
    sv('cpolyConclusionText', data.conclusionText);
    sv('cpolyProjectName', data.projectName);
    sv('cpolyClient', data.client);
    sv('cpolySiteCode', data.siteCode);
    sv('cpolyVille', data.ville);

    sv('cpolyDatum', data.datum); sv('cpolyDatumAutre', data.datumAutre);
    sv('cpolyProjection', data.projection); sv('cpolyProjectionAutre', data.projectionAutre);
    sv('cpolyAltSystem', data.altSystem); sv('cpolyAltSystemAutre', data.altSystemAutre);
    ['cpolyDatum', 'cpolyProjection', 'cpolyAltSystem'].forEach(id => {
      el(id + 'Autre')?.classList.toggle('nf-hidden', fv(id) !== 'Autre');
    });

    const revWrap = el('cpolyRevisionRows');
    if(revWrap) revWrap.innerHTML = '';
    const revisionRows = data.revisionRows || [];
    if(revisionRows.length) revisionRows.forEach(r => addRevisionRow(r));
    else addRevisionRow({ indice: 'A', date: new Date().toLocaleDateString('fr-FR') });

    const t = data.team || {};
    sv('cpolyRespName', t.responsibleName);
    sv('cpolyRespRole', t.responsibleRole);
    const l2Wrap = el('cpolyLevel2Rows');
    if(l2Wrap) l2Wrap.innerHTML = '';
    (t.level2 || []).forEach(r => addLevel2Row(r));
    sv('cpolySurveyorsText', t.surveyorsText);
    sv('cpolySurveyorRole', t.surveyorRole);

    const tech = data.techniques || {};
    const applyTech = (prefix, d) => {
      d = d || {};
      sc(prefix + 'Enabled', d.enabled);
      sv(prefix + 'DateStart', d.dateStart);
      sv(prefix + 'DateEnd', d.dateEnd);
      sc(prefix + 'DateRange', d.dateRange);
      sv(prefix + 'Device', d.deviceKey);
      sv(prefix + 'Methode', d.methode); // n'existe que pour GNSS (cpolyGnssMethode) - sv() no-op sinon
      sv(prefix + 'MethodeAutreText', d.methodeAutreText);
      sv(prefix + 'Methodology', d.methodologyText);
      el(prefix + 'DateEnd')?.classList.toggle('nf-hidden', !d.dateRange);
    };
    applyTech('cpolyGnss', tech.gnss);
    applyTech('cpolyNiv', tech.nivellement);
    applyTech('cpolyOptique', tech.optique);

    state.gnssDispositionImageDataUrl = tech.gnss?.dispositionImageDataUrl || '';
    applyDispositionPreview(state.gnssDispositionImageDataUrl, 'cpolyGnssDispositionPreview', 'cpolyGnssDispositionStatus', 'btnCpolyGnssDispositionClear');
    state.optiqueDispositionImageDataUrl = tech.optique?.dispositionImageDataUrl || '';
    applyDispositionPreview(state.optiqueDispositionImageDataUrl, 'cpolyOptiqueDispositionPreview', 'cpolyOptiqueDispositionStatus', 'btnCpolyOptiqueDispositionClear');

    sc('cpolyOptiquePpmApplied', tech.optique?.ppmApplied);
    sv('cpolyOptiquePpmValue', tech.optique?.ppmValue);
    sv('cpolyOptiqueMethodeCalcul', tech.optique?.methodeCalcul);
    sv('cpolyOptiqueCalculText', tech.optique?.calculText);

    sv('cpolyGnssPpAntennesCount', tech.gnss?.ppAntennesCount);
    const antenneWrap = el('cpolyGnssPpAntenneRows');
    if(antenneWrap) antenneWrap.innerHTML = '';
    (tech.gnss?.ppAntennes || []).forEach(a => addGnssPpAntenneRow(a));

    sc('cpolyNivNgfEnabled', tech.nivellement?.ngfEnabled);
    const ngfWrap = el('cpolyNivNgfRows');
    if(ngfWrap) ngfWrap.innerHTML = '';
    (tech.nivellement?.ngfRepereIds || []).forEach(r => addNivNgfRow(r));
    el('cpolyNivNgfWrap')?.classList.toggle('nf-hidden', !tech.nivellement?.ngfEnabled);

    const rgWrap = el('cpolyResultGroupRows');
    if(rgWrap) rgWrap.innerHTML = '';
    (data.resultGroups || []).forEach(r => addResultGroupRow(r));

    const annexeWrap = el('cpolyAnnexeRows');
    if(annexeWrap) annexeWrap.innerHTML = '';
    (data.annexes || []).forEach(a => addAnnexeRow(a));

    state.xySheets = Array.isArray(data.xySheets) ? data.xySheets : [];
    state.zSheets = Array.isArray(data.zSheets) ? data.zSheets : [];
    state.hasXlsx = !!data.hasXlsx;
    state.geobase = Array.isArray(data.geobase) ? data.geobase : [];
    state.mapImageDataUrl = data.mapImageDataUrl || '';
    showMapPreview();
    if(state.hasXlsx || state.xySheets.length || state.zSheets.length){
      setStatus('cpolyXlsxStatus', `Classeur restauré depuis le projet (${state.xySheets.length} onglet(s) XY, ${state.zSheets.length} onglet(s) Z).`);
    }
    if(state.geobase.length){
      setStatus('cpolyGeobaseStatus', `GeoBase restaurée depuis le projet (${state.geobase.length} point(s)).`);
    }

    updateTechDateVisibility();
    updateGnssMethodeVisibility();
    updateOptiqueMethodeVisibility();
    updateOptiqueCalculVisibility();
    updateNivMethodeVisibility();
    updateReportTypeVisibility();
    updateExportButton();
    renderPreview();
  }

  function wrapState(){
    const oldGet = window.NOVA_getState;
    if(typeof oldGet === 'function' && !oldGet.__nfCpolyWrapped){
      const wrapped = function(){
        const st = oldGet();
        st.cpoly = buildProjectState();
        return st;
      };
      wrapped.__nfCpolyWrapped = true;
      window.NOVA_getState = wrapped;
    }
    const oldSet = window.NOVA_setState;
    if(typeof oldSet === 'function' && !oldSet.__nfCpolyWrapped){
      const wrapped = function(st){
        oldSet(st);
        if(st && st.cpoly && typeof st.cpoly === 'object') restoreProjectState(st.cpoly);
      };
      wrapped.__nfCpolyWrapped = true;
      window.NOVA_setState = wrapped;
    }
  }

  // ---------- Import ----------

  function importXlsx(){
    setStatus('cpolyXlsxStatus', 'Import en cours…');
    post({ type: 'cpoly_import_xlsx' });
  }

  function handleXlsxResult(msg){
    if(!msg.ok){
      setStatus('cpolyXlsxStatus', String(msg.error || "Échec de l'import."), 'err');
      return;
    }
    state.xySheets = Array.isArray(msg.xySheets) ? msg.xySheets : [];
    state.zSheets = Array.isArray(msg.zSheets) ? msg.zSheets : [];
    state.hasXlsx = (state.xySheets.length + state.zSheets.length) > 0;

    if(!state.hasXlsx){
      setStatus('cpolyXlsxStatus', `Fichier chargé (${esc(msg.fileName || '')}) mais aucun onglet "GNSS"/"Niv" détecté.`, 'err');
    }else{
      setStatus('cpolyXlsxStatus', `Fichier chargé : ${msg.fileName} (${state.xySheets.length} onglet(s) XY, ${state.zSheets.length} onglet(s) Z).`);
    }
    updateExportButton();
    renderPreview();
  }

  function importGeobase(){
    setStatus('cpolyGeobaseStatus', 'Import en cours…');
    post({ type: 'cpoly_import_geobase' });
  }

  function handleGeobaseResult(msg){
    if(!msg.ok){
      setStatus('cpolyGeobaseStatus', String(msg.error || "Échec de l'import."), 'err');
      return;
    }
    state.geobase = Array.isArray(msg.rows) ? msg.rows : [];
    setStatus('cpolyGeobaseStatus', `Fichier chargé : ${msg.fileName} (${state.geobase.length} point(s)).`);
    updateExportButton();
    renderPreview();
  }

  // ---------- Export PDF ----------

  // Payload partagé entre l'export final (cpoly_export) et l'aperçu PDF en direct (cpoly_preview,
  // thème 5B) - même contenu, seul le type de message change (le C# écrit vers l'emplacement
  // choisi par l'utilisateur pour l'un, vers un fichier temporaire jetable pour l'autre).
  function buildExportPayload(){
    const creation = isCreationMode();
    return {
      project: {
        reportType: el('cpolyReportType').value,
        level: el('cpolyLevel').value,
        revisionRows: collectRevisionRows(),
        missionObjectiveText: el('cpolyMissionObjectiveText').value,
        projectName: el('cpolyProjectName').value,
        client: el('cpolyClient').value,
        siteCode: el('cpolySiteCode').value,
        ville: el('cpolyVille').value,
        datum: getSelectOrOther('cpolyDatum', 'cpolyDatumAutre'),
        projection: getSelectOrOther('cpolyProjection', 'cpolyProjectionAutre'),
        altSystem: getSelectOrOther('cpolyAltSystem', 'cpolyAltSystemAutre'),
        conclusionText: el('cpolyConclusionText').value,
        team: {
          responsibleName: el('cpolyRespName').value,
          responsibleRole: el('cpolyRespRole').value,
          level2: collectLevel2Rows(),
          surveyorsText: el('cpolySurveyorsText').value,
          surveyorRole: el('cpolySurveyorRole').value
        }
      },
      techniques: {
        gnss: {
          enabled: !!el('cpolyGnssEnabled').checked,
          dateText: formatDateRange('cpolyGnssDateStart', 'cpolyGnssDateEnd', 'cpolyGnssDateRange'),
          deviceKey: el('cpolyGnssDevice').value,
          methode: el('cpolyGnssMethode').value,
          methodeAutreText: el('cpolyGnssMethodeAutreText').value,
          methodologyText: el('cpolyGnssMethodology').value,
          ppAntennesCount: el('cpolyGnssPpAntennesCount').value,
          ppAntennes: collectGnssPpAntenneRows(),
          dispositionImageDataUrl: state.gnssDispositionImageDataUrl,
          mapImageDataUrl: state.mapImageDataUrl
        },
        nivellement: {
          enabled: !!el('cpolyNivEnabled').checked,
          dateText: formatDateRange('cpolyNivDateStart', 'cpolyNivDateEnd', 'cpolyNivDateRange'),
          deviceKey: el('cpolyNivDevice').value,
          methode: el('cpolyNivMethode').value,
          methodeAutreText: el('cpolyNivMethodeAutreText').value,
          methodologyText: el('cpolyNivMethodology').value,
          ngfEnabled: !!el('cpolyNivNgfEnabled').checked,
          ngfRepereIds: collectNivNgfRows()
        },
        optique: {
          enabled: !!el('cpolyOptiqueEnabled').checked,
          dateText: formatDateRange('cpolyOptiqueDateStart', 'cpolyOptiqueDateEnd', 'cpolyOptiqueDateRange'),
          deviceKey: el('cpolyOptiqueDevice').value,
          methode: el('cpolyOptiqueMethode').value,
          methodeAutreText: el('cpolyOptiqueMethodeAutreText').value,
          methodologyText: el('cpolyOptiqueMethodology').value,
          methodeCalcul: el('cpolyOptiqueMethodeCalcul').value,
          calculText: el('cpolyOptiqueCalculText').value,
          dispositionImageDataUrl: state.optiqueDispositionImageDataUrl,
          mapImageDataUrl: state.mapImageDataUrl,
          ppmApplied: !!el('cpolyOptiquePpmApplied').checked,
          ppmValue: el('cpolyOptiquePpmValue').value
        }
      },
      resultTables: creation ? buildResultTablesForCreation() : [],
      xySheets: state.xySheets,
      zSheets: state.zSheets,
      geobase: state.geobase,
      annexes: collectAnnexeRows()
    };
  }

  function hasImportedData(){
    return isCreationMode() ? state.geobase.length > 0 : state.hasXlsx;
  }

  function exportReport(){
    if(!hasImportedData()){
      setStatus('cpolyExportStatus', isCreationMode()
        ? "Importe d'abord le fichier GeoBase (coordonnées brutes)."
        : "Importe d'abord le classeur de comparaison.", 'err');
      return;
    }
    setStatus('cpolyExportStatus', 'Export en cours…');
    post(Object.assign({ type: 'cpoly_export' }, buildExportPayload()));
  }

  function handleExportResult(msg){
    if(msg.ok) setStatus('cpolyExportStatus', `Rapport généré : ${msg.filePath}`);
    else setStatus('cpolyExportStatus', String(msg.error || "Échec de l'export."), 'err');
  }

  // ---------- Aperçu PDF en direct (thème 5B) ----------
  // Bouton persistant dans le chrome de l'assistant (pas dupliqué par étape) - génère le PDF dans
  // son état actuel et l'affiche dans une fenêtre à part (CpolyPreviewForm, WebView2), réutilisée
  // d'un clic à l'autre. Pas de régénération automatique à la frappe (coût des tuiles de carte +
  // relecture des fichiers importés à chaque caractère serait disproportionné) : à la demande.
  // Contrairement à exportReport() (l'export final requiert réellement les données - classeur en
  // Contrôle, GeoBase en Création, sans quoi le rapport livré serait incomplet), l'aperçu n'exige
  // rien : il sert justement à voir la mise en page au fur et à mesure de la saisie, avant même
  // d'avoir importé quoi que ce soit. Le renderer gère déjà nativement l'absence de données
  // (section RESULTAT/GeoBase simplement omise si vide - voir ControlePolygonaleRenderer.cs).
  function previewReport(){
    setStatus('cpolyPreviewPdfStatus', 'Génération de l\'aperçu…');
    post(Object.assign({ type: 'cpoly_preview' }, buildExportPayload()));
  }

  function handlePreviewResult(msg){
    if(msg.ok) setStatus('cpolyPreviewPdfStatus', 'Aperçu à jour.');
    else setStatus('cpolyPreviewPdfStatus', String(msg.error || "Échec de l'aperçu."), 'err');
  }

  function init(){
    el('btnCpolyImportXlsx')?.addEventListener('click', importXlsx);
    el('btnCpolyImportGeobase')?.addEventListener('click', importGeobase);
    el('btnCpolyExport')?.addEventListener('click', exportReport);
    el('btnCpolyAddRevisionRow')?.addEventListener('click', () => addRevisionRow());
    el('btnCpolyAddLevel2Row')?.addEventListener('click', () => addLevel2Row());
    el('btnCpolyAddGnssPpAntenneRow')?.addEventListener('click', () => addGnssPpAntenneRow());
    el('btnCpolyAddNivNgfRow')?.addEventListener('click', () => addNivNgfRow());
    el('btnCpolyAddResultGroupRow')?.addEventListener('click', () => addResultGroupRow());

    ['cpolyGnssEnabled', 'cpolyNivEnabled', 'cpolyOptiqueEnabled'].forEach(id => {
      el(id)?.addEventListener('change', updateTechDateVisibility);
    });
    updateTechDateVisibility();

    el('cpolyGnssMethode')?.addEventListener('change', () => { updateGnssMethodeVisibility(); applyGnssMethodologyDefault(); });
    updateGnssMethodeVisibility();

    el('cpolyOptiqueMethode')?.addEventListener('change', () => { updateOptiqueMethodeVisibility(); applyOptiqueMethodologyDefault(); });
    updateOptiqueMethodeVisibility();
    el('cpolyOptiqueMethodeCalcul')?.addEventListener('change', () => { updateOptiqueCalculVisibility(); applyOptiqueCalculDefault(); });
    updateOptiqueCalculVisibility();

    el('cpolyNivMethode')?.addEventListener('change', () => { updateNivMethodeVisibility(); applyNivMethodologyDefault(); });
    updateNivMethodeVisibility();

    el('cpolyNivNgfEnabled')?.addEventListener('change', () => {
      el('cpolyNivNgfWrap')?.classList.toggle('nf-hidden', !el('cpolyNivNgfEnabled').checked);
    });

    el('cpolyReportType')?.addEventListener('change', updateReportTypeVisibility);
    updateReportTypeVisibility();

    wireSelectOrOtherToggle('cpolyDatum', 'cpolyDatumAutre');
    wireSelectOrOtherToggle('cpolyProjection', 'cpolyProjectionAutre');
    wireSelectOrOtherToggle('cpolyAltSystem', 'cpolyAltSystemAutre');

    wireDateRangeToggle('cpolyGnssDateRange', 'cpolyGnssDateEnd');
    wireDateRangeToggle('cpolyNivDateRange', 'cpolyNivDateEnd');
    wireDateRangeToggle('cpolyOptiqueDateRange', 'cpolyOptiqueDateEnd');

    wireDispositionPhoto('gnssDispositionImageDataUrl', 'cpolyGnssDispositionFile', 'cpolyGnssDispositionPreview', 'cpolyGnssDispositionStatus', 'btnCpolyGnssDispositionClear');
    wireDispositionPhoto('optiqueDispositionImageDataUrl', 'cpolyOptiqueDispositionFile', 'cpolyOptiqueDispositionPreview', 'cpolyOptiqueDispositionStatus', 'btnCpolyOptiqueDispositionClear');
    wireAnnexeFileInput();
    el('btnCpolyGenerateMap')?.addEventListener('click', generateMap);
    // Changer le niveau de zoom ne rafraîchit rien tant qu'une carte n'a pas déjà été générée au
    // moins une fois (rien à régénérer) ; une fois une carte présente, le changement de zoom la
    // régénère automatiquement - sinon le réglage semble "ne pas marcher" tant qu'on ne pense pas à
    // recliquer sur "Générer la carte" (retour utilisateur).
    el('cpolyMapZoom')?.addEventListener('change', () => { if(state.mapImageDataUrl) generateMap(); });

    document.querySelectorAll('.cpoly-step-nav[data-cpoly-step]').forEach(btn => {
      btn.addEventListener('click', () => showStep(btn.getAttribute('data-cpoly-step')));
    });
    el('btnCpolyStepPrev')?.addEventListener('click', () => stepBy(-1));
    el('btnCpolyStepNext')?.addEventListener('click', () => stepBy(1));
    el('btnCpolyPreviewPdf')?.addEventListener('click', previewReport);
    // Raccourci vers le menu natif "Fichier > Enregistrer le projet/Ouvrir projet…" (déjà câblé à
    // NOVA_getState/NOVA_setState via wrapState() ci-dessus) - ajouté ici car les utilisateurs ne
    // pensent pas toujours à aller chercher ce menu depuis un module particulier.
    el('btnCpolySaveProject')?.addEventListener('click', () => post({ type: 'project_save' }));
    el('btnCpolyOpenProject')?.addEventListener('click', () => post({ type: 'project_open' }));
    const gnssMethodo = el('cpolyGnssMethodology');
    if(gnssMethodo && !gnssMethodo.value) gnssMethodo.value = GNSS_METHODOLOGY_DEFAULT;
    const optiqueMethodo = el('cpolyOptiqueMethodology');
    if(optiqueMethodo && !optiqueMethodo.value) optiqueMethodo.value = OPTIQUE_METHODOLOGY_DEFAULT;
    const nivMethodo = el('cpolyNivMethodology');
    if(nivMethodo && !nivMethodo.value) nivMethodo.value = NIVELLEMENT_METHODOLOGY_DEFAULT;

    if(el('cpolyRevisionRows') && !el('cpolyRevisionRows').children.length){
      addRevisionRow({ indice: 'A', date: new Date().toLocaleDateString('fr-FR') });
    }

    wrapState();
    setInterval(wrapState, 1200);

    positionWizardFooter();
    window.addEventListener('resize', positionWizardFooter);
    // positionWizardFooter() mesure les rectangles réels (card, #pageFooter) - tant que le module
    // est masqué (nf-module sans .active, display:none), ces rectangles sont nuls. Le changement de
    // module actif est géré par un handler délégué sur `document` (topo_app.html, ~ligne 1551,
    // ev.target.closest('.nf-nav-item')) : un listener attaché directement au bouton se déclenche
    // pendant la phase de "target" de l'événement, donc AVANT tout listener posé sur `document`
    // (phase de bubbling, ancêtre) - quel que soit l'ordre d'enregistrement. Il faut donc déléguer
    // sur `document` nous aussi, après ce script (chargé après l'inline de topo_app.html), pour que
    // l'ordre d'enregistrement place notre recalcul après le changement de module. Couvre aussi
    // l'activation programmatique du module au rechargement d'un projet (NOVA_setState simule un
    // clic sur ce même bouton, voir m01_core.js).
    document.addEventListener('click', ev => {
      if(ev.target.closest && ev.target.closest('.nf-nav-item[data-target="module-controle-polygonale"]')) positionWizardFooter();
    });

    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function'){
        window.chrome.webview.addEventListener('message', ev => {
          const msg = (ev && ev.data) ? ev.data : ev;
          if(!msg || !msg.type) return;
          if(msg.type === 'cpoly_xlsx_result') handleXlsxResult(msg);
          if(msg.type === 'cpoly_geobase_result') handleGeobaseResult(msg);
          if(msg.type === 'cpoly_export_result') handleExportResult(msg);
          if(msg.type === 'cpoly_map_result') handleMapResult(msg);
          if(msg.type === 'cpoly_preview_result') handlePreviewResult(msg);
        });
      }
    }catch(_){ }
  }

  if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
