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
    optiqueDispositionImageDataUrl: ''
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
  }

  // ---------- Tiroirs GNSS/Optique : disposition (photo), matériel, méthodologie ----------

  const GNSS_METHODOLOGY_DEFAULT =
    "La méthodologie suivie est celle du positionnement statique en utilisant les données " +
    "d'antennes de référence (RGP ou stations de référence dédiées). Cette méthode permet " +
    "d'obtenir des coordonnées précises en stationnant chaque point pendant une durée adaptée à " +
    "la précision recherchée.\n" +
    "Les calculs sont faits avec un logiciel de post-traitement intégrant la compensation par les " +
    "moindres carrés.";

  const OPTIQUE_METHODOLOGY_DEFAULT =
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
    "en station.";

  const NIVELLEMENT_METHODOLOGY_DEFAULT =
    "La méthode utilisée pour le nivellement direct est la méthode Cholesky à doubles points de " +
    "mire. Cette méthode consiste à mener simultanément deux cheminements distincts en plaçant la " +
    "mire de nivellement successivement en deux points situés à l'arrière, puis en deux points " +
    "situés à l'avant, et ainsi de suite, permettant un contrôle de marche lors de chaque " +
    "station. On calcule séparément les deux cheminements et on compare les résultats obtenus.\n" +
    "L'appareil Leica propose ce type de cheminement classé sous la dénomination ABBA : une mire " +
    "sur le point A, puis sur le point B, puis B1, puis A1.\n" +
    "Tous les points de la polygonale sont observés par méthode de nivellement direct. La portée " +
    "des mesures ne dépasse pas 40 m, sauf en cas de nécessité.";

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

  function exportReport(){
    const creation = isCreationMode();
    if(creation){
      if(!state.geobase.length){
        setStatus('cpolyExportStatus', "Importe d'abord le fichier GeoBase (coordonnées brutes).", 'err');
        return;
      }
    }else if(!state.hasXlsx){
      setStatus('cpolyExportStatus', "Importe d'abord le classeur de comparaison.", 'err');
      return;
    }
    setStatus('cpolyExportStatus', 'Export en cours…');

    post({
      type: 'cpoly_export',
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
          dateText: el('cpolyGnssDate').value,
          deviceKey: el('cpolyGnssDevice').value,
          methodologyText: el('cpolyGnssMethodology').value,
          dispositionImageDataUrl: state.gnssDispositionImageDataUrl
        },
        nivellement: {
          enabled: !!el('cpolyNivEnabled').checked,
          dateText: el('cpolyNivDate').value,
          deviceKey: el('cpolyNivDevice').value,
          methodologyText: el('cpolyNivMethodology').value
        },
        optique: {
          enabled: !!el('cpolyOptiqueEnabled').checked,
          dateText: el('cpolyOptiqueDate').value,
          deviceKey: el('cpolyOptiqueDevice').value,
          methodologyText: el('cpolyOptiqueMethodology').value,
          dispositionImageDataUrl: state.optiqueDispositionImageDataUrl,
          ppmApplied: !!el('cpolyOptiquePpmApplied').checked,
          ppmValue: el('cpolyOptiquePpmValue').value
        }
      },
      resultTables: creation ? buildResultTablesForCreation() : [],
      xySheets: state.xySheets,
      zSheets: state.zSheets,
      geobase: state.geobase
    });
  }

  function handleExportResult(msg){
    if(msg.ok) setStatus('cpolyExportStatus', `Rapport généré : ${msg.filePath}`);
    else setStatus('cpolyExportStatus', String(msg.error || "Échec de l'export."), 'err');
  }

  function init(){
    el('btnCpolyImportXlsx')?.addEventListener('click', importXlsx);
    el('btnCpolyImportGeobase')?.addEventListener('click', importGeobase);
    el('btnCpolyExport')?.addEventListener('click', exportReport);
    el('btnCpolyAddRevisionRow')?.addEventListener('click', () => addRevisionRow());
    el('btnCpolyAddLevel2Row')?.addEventListener('click', () => addLevel2Row());
    el('btnCpolyAddResultGroupRow')?.addEventListener('click', () => addResultGroupRow());

    ['cpolyGnssEnabled', 'cpolyNivEnabled', 'cpolyOptiqueEnabled'].forEach(id => {
      el(id)?.addEventListener('change', updateTechDateVisibility);
    });
    updateTechDateVisibility();

    el('cpolyReportType')?.addEventListener('change', updateReportTypeVisibility);
    updateReportTypeVisibility();

    wireSelectOrOtherToggle('cpolyDatum', 'cpolyDatumAutre');
    wireSelectOrOtherToggle('cpolyProjection', 'cpolyProjectionAutre');
    wireSelectOrOtherToggle('cpolyAltSystem', 'cpolyAltSystemAutre');

    wireDispositionPhoto('gnssDispositionImageDataUrl', 'cpolyGnssDispositionFile', 'cpolyGnssDispositionPreview', 'cpolyGnssDispositionStatus', 'btnCpolyGnssDispositionClear');
    wireDispositionPhoto('optiqueDispositionImageDataUrl', 'cpolyOptiqueDispositionFile', 'cpolyOptiqueDispositionPreview', 'cpolyOptiqueDispositionStatus', 'btnCpolyOptiqueDispositionClear');
    const gnssMethodo = el('cpolyGnssMethodology');
    if(gnssMethodo && !gnssMethodo.value) gnssMethodo.value = GNSS_METHODOLOGY_DEFAULT;
    const optiqueMethodo = el('cpolyOptiqueMethodology');
    if(optiqueMethodo && !optiqueMethodo.value) optiqueMethodo.value = OPTIQUE_METHODOLOGY_DEFAULT;
    const nivMethodo = el('cpolyNivMethodology');
    if(nivMethodo && !nivMethodo.value) nivMethodo.value = NIVELLEMENT_METHODOLOGY_DEFAULT;

    if(el('cpolyRevisionRows') && !el('cpolyRevisionRows').children.length){
      addRevisionRow({ indice: 'A', date: new Date().toLocaleDateString('fr-FR') });
    }

    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function'){
        window.chrome.webview.addEventListener('message', ev => {
          const msg = (ev && ev.data) ? ev.data : ev;
          if(!msg || !msg.type) return;
          if(msg.type === 'cpoly_xlsx_result') handleXlsxResult(msg);
          if(msg.type === 'cpoly_geobase_result') handleGeobaseResult(msg);
          if(msg.type === 'cpoly_export_result') handleExportResult(msg);
        });
      }
    }catch(_){ }
  }

  if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
