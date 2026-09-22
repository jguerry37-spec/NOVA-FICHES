// Module manager "Contrôle de doublons" (gated par data-nf-feature="controle_doublons") :
// fusionne N fichiers de points TXT (entrées) contre 1 fichier de contrôle unique, détecte les
// n° de points présents à la fois dans les entrées et le contrôle (doublons à résoudre), laisse
// choisir une moyenne d'une sélection d'occurrences ou une suppression pure par groupe, puis
// exporte le fichier final + un rapport texte. L'analyse de doublons est calculée ici en local
// (miroir léger de DuplicateControlService.Analyze/Average côté C#) pour un aperçu instantané
// sans aller-retour serveur ; au moment de l'export, le C# refait l'analyse lui-même à partir des
// points bruts et applique les résolutions envoyées ici - il reste la seule source de vérité
// pour le fichier écrit sur disque.
(function initDuplicateControlModule(){
  const state = {
    inputFiles: [],    // [{name, count, rejects}]
    inputPoints: [],   // [{id,x,y,z,hasZ,code,sourceFile,role}]
    controlFile: null, // {name, count, rejects}
    controlPoints: [],
    analysis: null,     // {duplicates:[{id, occurrences:[...]}], orphanInputs:[...], orphanControls:[...]}
    resolutions: {},    // normId -> {id, action:'average'|'delete', selectedSourceFiles:[...]}
    renames: [],         // [{sourceFile, originalId, newId}] - occurrence extraite de son groupe
                          // sous un nouvel ID (cas où l'écart est trop grand pour qu'il s'agisse
                          // du même point physique : moyenne/suppression perdrait des données).
    expandedIds: new Set()  // normId des groupes actuellement dépliés - persiste entre les
                             // re-rendus complets du tableau (chaque renommage/tolérance
                             // reconstruit toute la liste), pour ne pas replier le détail d'un
                             // groupe qu'on est justement en train de traiter occurrence par
                             // occurrence (ex : renommer plusieurs occurrences d'un même groupe).
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
  function normId(id){ return String(id ?? '').trim().toUpperCase(); }
  function fmt(n){ return (n == null) ? '' : Number(n).toFixed(3); }

  function setInputsStatus(text, cls){
    const p = el('dcInputsStatus');
    if(!p) return;
    p.textContent = text || '';
    p.className = 'small' + (cls ? ' ' + cls : '');
  }
  function setControlStatus(text, cls){
    const p = el('dcControlStatus');
    if(!p) return;
    p.textContent = text || '';
    p.className = 'small' + (cls ? ' ' + cls : '');
  }
  function setExportStatus(text, cls){
    const p = el('dcExportStatus');
    if(!p) return;
    p.textContent = text || '';
    p.className = 'small' + (cls ? ' ' + cls : '');
  }

  // ---------- Import ----------

  function importInputs(){
    setInputsStatus('Sélection des fichiers…');
    post({ type: 'dupctrl_import_inputs' });
  }

  function handleInputsResult(msg){
    if(!msg.ok){
      setInputsStatus(String(msg.error || "Échec de l'import."), 'err');
      return;
    }
    // Un nouvel import d'entrées remplace le précédent (l'utilisateur sélectionne d'un coup
    // tous les fichiers voulus dans la boîte de dialogue multi-sélection).
    state.inputFiles = Array.isArray(msg.files) ? msg.files : [];
    state.inputPoints = Array.isArray(msg.points) ? msg.points : [];
    const names = state.inputFiles.map(f => `${esc(f.name)} (${f.count} pt)`).join(', ');
    setInputsStatus(state.inputFiles.length
      ? `${state.inputFiles.length} fichier(s), ${state.inputPoints.length} point(s) : ${names}`
      : 'Aucun fichier d’entrée chargé.');
    maybeRunAnalysis();
  }

  function importControl(){
    setControlStatus('Sélection du fichier…');
    post({ type: 'dupctrl_import_control' });
  }

  function handleControlResult(msg){
    if(!msg.ok){
      setControlStatus(String(msg.error || "Échec de l'import."), 'err');
      return;
    }
    state.controlFile = { name: msg.fileName, count: msg.count, rejects: msg.rejects };
    state.controlPoints = Array.isArray(msg.points) ? msg.points : [];
    setControlStatus(`${esc(msg.fileName)} : ${msg.count} point(s)`);
    maybeRunAnalysis();
  }

  // ---------- Analyse locale (miroir de DuplicateControlService.Analyze) ----------

  function maybeRunAnalysis(){
    if(state.inputPoints.length === 0 || state.controlPoints.length === 0) return;
    runAnalysis();
  }

  // Applique les renommages en cours sur une copie des points bruts (ne mute jamais
  // state.inputPoints/controlPoints, qui restent les données brutes envoyées à l'export).
  function applyRenames(rawPoints){
    if(state.renames.length === 0) return rawPoints;
    return rawPoints.map(p => {
      const r = state.renames.find(x => x.sourceFile === p.sourceFile && x.originalId === p.id);
      return r ? Object.assign({}, p, { id: r.newId }) : p;
    });
  }

  function runAnalysis(){
    const effectiveInputs = applyRenames(state.inputPoints);
    const effectiveControls = applyRenames(state.controlPoints);

    const inputsByNorm = new Map();
    effectiveInputs.forEach(p => {
      const k = normId(p.id);
      if(!inputsByNorm.has(k)) inputsByNorm.set(k, []);
      inputsByNorm.get(k).push(p);
    });
    const controlByNorm = new Map();
    effectiveControls.forEach(p => {
      const k = normId(p.id);
      if(!controlByNorm.has(k)) controlByNorm.set(k, []);
      controlByNorm.get(k).push(p);
    });

    const duplicates = [];
    const orphanInputs = [];
    inputsByNorm.forEach((inputs, k) => {
      if(controlByNorm.has(k)){
        duplicates.push({ id: inputs[0].id, occurrences: controlByNorm.get(k).concat(inputs) });
      }else{
        orphanInputs.push(...inputs);
      }
    });
    const orphanControls = [];
    controlByNorm.forEach((controls, k) => {
      if(!inputsByNorm.has(k)) orphanControls.push(...controls);
    });

    state.analysis = { duplicates, orphanInputs, orphanControls };
    // Ne garde que les résolutions (et l'état de dépliage) encore pertinents - un nouvel import
    // ou un renommage peut faire disparaître un groupe.
    const stillValid = new Set(duplicates.map(g => normId(g.id)));
    Object.keys(state.resolutions).forEach(k => { if(!stillValid.has(k)) delete state.resolutions[k]; });
    Array.from(state.expandedIds).forEach(k => { if(!stillValid.has(k)) state.expandedIds.delete(k); });

    const toleranceRow = el('dcToleranceRow');
    if(toleranceRow) toleranceRow.classList.remove('nf-hidden');

    // applyTolerance() déclenche elle-même renderSummary/renderDuplicates/updateExportButton.
    applyTolerance();
    renderOrphans();
  }

  function renderSummary(){
    const box = el('dcSummary');
    const text = el('dcSummaryText');
    if(!box || !text || !state.analysis) return;
    box.classList.remove('nf-hidden');
    const total = state.analysis.duplicates.length;
    const autoCount = Object.values(state.resolutions).filter(r => r.auto).length;
    const manualCount = Object.values(state.resolutions).filter(r => !r.auto).length;
    const pending = total - autoCount - manualCount;
    text.textContent =
      `${state.inputPoints.length} point(s) en entrée (${state.inputFiles.length} fichier(s)), ` +
      `${state.controlPoints.length} point(s) au contrôle — ` +
      `${total} doublon(s) : ${autoCount} résolu(s) automatiquement, ${manualCount} résolu(s) manuellement, ${pending} à traiter — ` +
      `${state.analysis.orphanInputs.length} orphelin(s) en entrée, ${state.analysis.orphanControls.length} orphelin(s) au contrôle.`;
  }

  function averageOf(points){
    const x = points.reduce((a, p) => a + Number(p.x), 0) / points.length;
    const y = points.reduce((a, p) => a + Number(p.y), 0) / points.length;
    const withZ = points.filter(p => p.hasZ);
    const z = withZ.length ? withZ.reduce((a, p) => a + Number(p.z), 0) / withZ.length : null;
    return { x, y, z };
  }

  // Écart maximal (mètres) entre toutes les paires d'occurrences d'un groupe - distance 3D,
  // Z ignoré pour une paire dès que l'une des deux occurrences n'en a pas.
  function maxPairwiseDistance(occurrences){
    let max = 0;
    for(let i = 0; i < occurrences.length; i++){
      for(let j = i + 1; j < occurrences.length; j++){
        const a = occurrences[i], b = occurrences[j];
        const dx = Number(a.x) - Number(b.x);
        const dy = Number(a.y) - Number(b.y);
        const dz = (a.hasZ && b.hasZ) ? (Number(a.z) - Number(b.z)) : 0;
        const d = Math.sqrt(dx * dx + dy * dy + dz * dz);
        if(d > max) max = d;
      }
    }
    return max;
  }

  // Pré-résout automatiquement (moyenne de toutes les occurrences) tout groupe dont l'écart
  // maximal est sous la tolérance saisie - ne touche jamais un groupe déjà résolu manuellement
  // (resolution.auto absent/false). Un groupe auto-résolu qui dépasse une tolérance resserrée
  // depuis redevient "à traiter". Toujours ré-exécutable (bouton, ou après un nouvel import).
  function applyTolerance(){
    if(!state.analysis) return;
    const raw = el('dcTolerance') ? parseFloat(el('dcTolerance').value) : NaN;
    const toleranceM = (Number.isFinite(raw) && raw >= 0) ? raw / 1000 : 0;

    state.analysis.duplicates.forEach(group => {
      const key = normId(group.id);
      const existing = state.resolutions[key];
      if(existing && !existing.auto) return; // résolution manuelle : jamais touchée

      const maxDist = maxPairwiseDistance(group.occurrences);
      if(maxDist <= toleranceM){
        state.resolutions[key] = {
          id: group.id,
          action: 'average',
          selectedSourceFiles: group.occurrences.map(o => o.sourceFile),
          auto: true
        };
      }else if(existing && existing.auto){
        delete state.resolutions[key];
      }
    });

    renderDuplicates();
    renderSummary();
    updateExportButton();
  }

  // Liste compacte à deux niveaux : une ligne résumé toujours visible par groupe (statut,
  // écart max, bouton Annuler si auto-résolu) + une ligne détail repliée par défaut (le
  // tableau complet des occurrences + actions), dépliable au clic - reste rapide à parcourir
  // même avec 30-40+ groupes, seuls ceux dépliés occupent de la place à l'écran.
  function renderDuplicates(){
    const wrap = el('dcDuplicatesWrap');
    if(!wrap || !state.analysis) return;
    if(state.analysis.duplicates.length === 0){
      wrap.innerHTML = '<div class="small" style="font-weight:700; color:#1a7f37;">Aucun doublon détecté.</div>';
      return;
    }

    // À traiter d'abord, résolus (auto ou manuel) ensuite - ce qui demande une action reste
    // visible sans avoir à parcourir toute la liste.
    const order = state.analysis.duplicates.map((_, gi) => gi).sort((a, b) => {
      const ra = state.resolutions[normId(state.analysis.duplicates[a].id)] ? 1 : 0;
      const rb = state.resolutions[normId(state.analysis.duplicates[b].id)] ? 1 : 0;
      return ra - rb;
    });

    wrap.innerHTML =
      '<table class="dc-groups-table" style="width:100%;">' +
      '<thead><tr><th></th><th>ID</th><th>Occ.</th><th>Écart max</th><th>Statut</th><th></th></tr></thead>' +
      '<tbody>' + order.map(gi => buildGroupRowHtml(gi) + buildGroupDetailHtml(gi)).join('') + '</tbody>' +
      '</table>';

    wrap.querySelectorAll('.dc-toggle-btn').forEach(btn => {
      btn.addEventListener('click', () => toggleDetail(Number(btn.getAttribute('data-group'))));
    });
    wrap.querySelectorAll('.dc-average-btn').forEach(btn => {
      btn.addEventListener('click', () => resolveGroup(Number(btn.getAttribute('data-group')), 'average'));
    });
    // Cocher/décocher applique directement la sélection (moyenne des occurrences cochées) sans
    // exiger un clic séparé sur "Moyenne de la sélection" - éviter le piège où l'utilisateur
    // décoche jusqu'à ne garder qu'un point et pense l'avoir résolu, alors que rien n'était
    // encore enregistré tant que le bouton n'était pas cliqué. L'écart max affiché dans la ligne
    // résumé se recalcule aussi en direct sur la seule sélection cochée (et non plus l'ensemble
    // du groupe), pour visualiser tout de suite l'effet d'une décoche sur la dispersion restante.
    wrap.querySelectorAll('.dc-occ-check').forEach(cb => {
      cb.addEventListener('change', () => {
        const gi = Number(cb.getAttribute('data-group'));
        resolveGroup(gi, 'average');
        updateGroupMaxDist(gi);
      });
    });
    wrap.querySelectorAll('.dc-delete-btn').forEach(btn => {
      btn.addEventListener('click', () => resolveGroup(Number(btn.getAttribute('data-group')), 'delete'));
    });
    wrap.querySelectorAll('.dc-rename-btn').forEach(btn => {
      btn.addEventListener('click', () => renameOccurrence(Number(btn.getAttribute('data-group')), Number(btn.getAttribute('data-idx'))));
    });

    // Reflète l'état de résolution (ou son absence) de chaque groupe dans sa ligne résumé.
    state.analysis.duplicates.forEach((group, gi) => {
      reflectResolution(gi, state.resolutions[normId(group.id)] || null);
    });
  }

  function buildGroupRowHtml(gi){
    const group = state.analysis.duplicates[gi];
    const maxDistMm = maxPairwiseDistance(group.occurrences) * 1000;
    const expanded = state.expandedIds.has(normId(group.id));
    return (
      `<tr class="dc-group-row" id="dcGroupRow_${gi}">` +
      `<td><button type="button" class="dc-toggle-btn" data-group="${gi}" title="Détails">${expanded ? '▾' : '▸'}</button></td>` +
      `<td>${esc(group.id)}</td>` +
      `<td>${group.occurrences.length}</td>` +
      `<td id="dcMaxDist_${gi}">${maxDistMm.toFixed(1)} mm</td>` +
      `<td><span class="pill dc-status" id="dcStatus_${gi}">À traiter</span></td>` +
      `<td id="dcRowAction_${gi}"></td>` +
      `</tr>`
    );
  }

  function buildGroupDetailHtml(gi){
    const group = state.analysis.duplicates[gi];
    const expanded = state.expandedIds.has(normId(group.id));
    const rows = group.occurrences.map((occ, oi) =>
      '<tr>' +
      `<td><input type="checkbox" class="dc-occ-check" data-group="${gi}" data-idx="${oi}" checked /></td>` +
      `<td>${esc(occ.sourceFile)}${occ.role === 'control' ? ' <span class="small">(contrôle)</span>' : ''}</td>` +
      `<td>${esc(occ.id)}</td><td>${fmt(occ.x)}</td><td>${fmt(occ.y)}</td><td>${occ.hasZ ? fmt(occ.z) : ''}</td><td>${esc(occ.code || '')}</td>` +
      `<td class="row nowrap" style="gap:4px;">` +
      `<input type="text" class="box dc-rename-input" data-group="${gi}" data-idx="${oi}" placeholder="nouvel ID" style="width:90px;" />` +
      `<button type="button" class="dc-rename-btn" data-group="${gi}" data-idx="${oi}" title="Écart trop important pour une moyenne/suppression : garder cette occurrence comme point distinct sous un nouvel ID.">Renommer</button>` +
      `</td>` +
      '</tr>'
    ).join('');
    return (
      `<tr class="dc-group-detail${expanded ? '' : ' nf-hidden'}" id="dcDetailRow_${gi}">` +
      `<td colspan="6">` +
      `<table style="width:100%; margin-top:6px;"><thead><tr><th></th><th>Source</th><th>ID</th><th>X</th><th>Y</th><th>Z</th><th>Code</th><th>Séparer</th></tr></thead>` +
      `<tbody>${rows}</tbody></table>` +
      `<div class="small" style="margin-top:4px;">Coche/décoche les occurrences à garder : la moyenne se recalcule et s'applique automatiquement (ne garder qu'une seule occurrence cochée revient à la conserver telle quelle). Si l'écart entre occurrences est trop important pour être la même mesure, utilise « Renommer » pour la garder comme un point à part plutôt que de la fondre ou la perdre.</div>` +
      `<div class="row" style="margin-top:8px;">` +
      `<button type="button" class="dc-average-btn" data-group="${gi}">Moyenne de la sélection</button>` +
      `<button type="button" class="dc-delete-btn" data-group="${gi}">Supprimer ce point</button>` +
      `</div>` +
      `<div class="small" id="dcResult_${gi}" style="margin-top:4px;"></div>` +
      `</td></tr>`
    );
  }

  function toggleDetail(gi){
    const row = el('dcDetailRow_' + gi);
    if(!row) return;
    const hidden = row.classList.toggle('nf-hidden');
    const btn = document.querySelector(`.dc-toggle-btn[data-group="${gi}"]`);
    if(btn) btn.textContent = hidden ? '▸' : '▾';
    const group = state.analysis && state.analysis.duplicates[gi];
    if(group){
      const key = normId(group.id);
      if(hidden) state.expandedIds.delete(key); else state.expandedIds.add(key);
    }
  }

  // Recalcule l'écart max affiché dans la ligne résumé sur les seules occurrences actuellement
  // cochées dans le détail (et non plus l'ensemble figé du groupe) - reflète en direct l'effet
  // d'une décoche sur la dispersion restante, avant même de cliquer "Moyenne de la sélection".
  function updateGroupMaxDist(groupIndex){
    const cell = el('dcMaxDist_' + groupIndex);
    const group = state.analysis.duplicates[groupIndex];
    if(!cell || !group) return;
    const wrap = el('dcDuplicatesWrap');
    const checks = wrap.querySelectorAll(`.dc-occ-check[data-group="${groupIndex}"]`);
    const selected = [];
    checks.forEach(cb => {
      if(cb.checked){
        const occ = group.occurrences[Number(cb.getAttribute('data-idx'))];
        if(occ) selected.push(occ);
      }
    });
    const maxDistMm = maxPairwiseDistance(selected) * 1000;
    cell.textContent = maxDistMm.toFixed(1) + ' mm';
  }

  function resolveGroup(groupIndex, action){
    const group = state.analysis.duplicates[groupIndex];
    if(!group) return;
    const wrap = el('dcDuplicatesWrap');
    const checks = wrap.querySelectorAll(`.dc-occ-check[data-group="${groupIndex}"]`);
    const selected = [];
    checks.forEach(cb => {
      if(cb.checked){
        const occ = group.occurrences[Number(cb.getAttribute('data-idx'))];
        if(occ) selected.push(occ);
      }
    });

    if(action === 'delete'){
      state.resolutions[normId(group.id)] = { id: group.id, action: 'delete', selectedSourceFiles: [] };
    }else{
      if(selected.length === 0){
        const result = el('dcResult_' + groupIndex);
        if(result){ result.textContent = 'Sélectionne au moins une occurrence avant de calculer la moyenne.'; result.className = 'small err'; }
        return;
      }
      state.resolutions[normId(group.id)] = {
        id: group.id, action: 'average', selectedSourceFiles: selected.map(o => o.sourceFile)
      };
    }
    reflectResolution(groupIndex, state.resolutions[normId(group.id)]);
    updateExportButton();
  }

  function reflectResolution(groupIndex, resolution){
    const status = el('dcStatus_' + groupIndex);
    const actionCell = el('dcRowAction_' + groupIndex);
    const result = el('dcResult_' + groupIndex);
    const group = state.analysis.duplicates[groupIndex];
    if(!status || !group) return;

    if(!resolution){
      status.textContent = 'À traiter';
      status.className = 'pill dc-status warn';
      if(actionCell) actionCell.innerHTML = '';
      if(result) result.textContent = '';
    }else if(resolution.action === 'delete'){
      status.textContent = resolution.auto ? 'Auto : supprimé' : 'Supprimé';
      status.className = 'pill dc-status err';
      if(result) result.textContent = 'Ce point sera exclu du fichier final.';
    }else{
      const selected = group.occurrences.filter(o => resolution.selectedSourceFiles.indexOf(o.sourceFile) !== -1);
      const avg = averageOf(selected);
      const label = selected.length === 1 ? 'Conservé (1)' : `Moyenne (${selected.length})`;
      status.textContent = resolution.auto ? `Auto : ${label}` : label;
      status.className = 'pill dc-status';
      if(result) result.textContent = `Résultat : X=${fmt(avg.x)} Y=${fmt(avg.y)}${avg.z != null ? ' Z=' + fmt(avg.z) : ''} (${selected.map(o => o.sourceFile).join(', ')})`;
    }

    if(actionCell){
      actionCell.innerHTML = (resolution && resolution.auto)
        ? `<button type="button" class="dc-undo-btn" data-group="${groupIndex}" title="Reprendre la main manuellement sur ce groupe.">Annuler</button>`
        : '';
      const undoBtn = actionCell.querySelector('.dc-undo-btn');
      if(undoBtn) undoBtn.addEventListener('click', () => undoAutoResolution(groupIndex));
    }
  }

  // Efface une résolution automatique par tolérance et déplie le détail du groupe pour que
  // l'utilisateur reprenne la main dessus tout de suite (moyenne partielle, suppression,
  // renommage). N'affecte que ce groupe - les autres résolutions (auto ou manuelles) restent.
  function undoAutoResolution(groupIndex){
    const group = state.analysis.duplicates[groupIndex];
    if(!group) return;
    delete state.resolutions[normId(group.id)];
    reflectResolution(groupIndex, null);
    updateExportButton();
    renderSummary();
    const row = el('dcDetailRow_' + groupIndex);
    if(row) row.classList.remove('nf-hidden');
    const btn = document.querySelector(`.dc-toggle-btn[data-group="${groupIndex}"]`);
    if(btn) btn.textContent = '▾';
    state.expandedIds.add(normId(group.id));
  }

  // Extrait une occurrence de son groupe sous un nouvel ID : elle devient un point à part
  // (orphelin ou membre d'un nouveau groupe si le nouvel ID entre à son tour en collision),
  // sans passer par une moyenne/suppression qui perdrait sa donnée propre. Relance l'analyse
  // complète depuis les points bruts + la liste de renommages, donc l'affichage se reconstruit
  // automatiquement (le groupe rétrécit, disparaît, ou un nouveau groupe apparaît ailleurs).
  function renameOccurrence(groupIndex, occIndex){
    const group = state.analysis.duplicates[groupIndex];
    if(!group) return;
    const occ = group.occurrences[occIndex];
    if(!occ) return;

    // Message local à la ligne du groupe (et non le statut global d'export, partagé avec
    // "X/Y doublons résolus" - sinon une erreur de saisie ici reste affichée indéfiniment
    // à côté du bouton Export et donne l'impression trompeuse d'une règle d'export bloquante).
    const result = el('dcResult_' + groupIndex);
    const input = document.querySelector(`.dc-rename-input[data-group="${groupIndex}"][data-idx="${occIndex}"]`);
    const newId = (input && input.value ? input.value : '').trim();
    if(!newId){
      if(result){ result.textContent = 'Saisis un nouvel ID avant de renommer.'; result.className = 'small err'; }
      return;
    }
    if(normId(newId) === normId(occ.id)){
      if(result){ result.textContent = 'Le nouvel ID doit être différent de l’ID actuel.'; result.className = 'small err'; }
      return;
    }

    // Un même (sourceFile, id) ne peut avoir qu'un seul renommage actif à la fois.
    state.renames = state.renames.filter(r => !(r.sourceFile === occ.sourceFile && r.originalId === occ.id));
    state.renames.push({ sourceFile: occ.sourceFile, originalId: occ.id, newId });

    // Le groupe reste marqué "déplié" (state.expandedIds) donc runAnalysis() le rouvrira
    // automatiquement s'il subsiste encore - on ramène juste sa ligne à l'écran pour ne pas
    // avoir à la rechercher après le renommage des occurrences suivantes du même groupe.
    const originalGroupKey = normId(group.id);
    runAnalysis();
    const newGi = state.analysis.duplicates.findIndex(g => normId(g.id) === originalGroupKey);
    if(newGi >= 0){
      const groupRow = el('dcGroupRow_' + newGi);
      if(groupRow) groupRow.scrollIntoView({ block: 'center' });
    }
  }

  function renderOrphans(){
    const details = el('dcOrphansDetails');
    const content = el('dcOrphansContent');
    if(!details || !content || !state.analysis) return;
    const { orphanInputs, orphanControls } = state.analysis;
    if(orphanInputs.length === 0 && orphanControls.length === 0){
      details.classList.add('nf-hidden');
      return;
    }
    details.classList.remove('nf-hidden');
    let html = '';
    if(orphanInputs.length){
      html += `<div style="font-weight:700; margin-top:4px;">En entrée, absents du contrôle (${orphanInputs.length})</div>`;
      html += orphanInputs.map(p => `<div>${esc(p.id)} — ${esc(p.sourceFile)}</div>`).join('');
    }
    if(orphanControls.length){
      html += `<div style="font-weight:700; margin-top:8px;">Au contrôle, absents des entrées (${orphanControls.length})</div>`;
      html += orphanControls.map(p => `<div>${esc(p.id)}</div>`).join('');
    }
    content.innerHTML = html;
  }

  function updateExportButton(){
    const btn = el('btnDcExport');
    if(!btn || !state.analysis) return;
    const total = state.analysis.duplicates.length;
    const resolved = Object.keys(state.resolutions).length;
    btn.disabled = total > resolved;
    setExportStatus(total > resolved ? `${resolved}/${total} doublon(s) résolu(s) — résous-les tous avant d'exporter.` : '');
  }

  // ---------- Export ----------

  function exportFinal(){
    if(!state.analysis) return;
    setExportStatus('Export en cours…');
    post({
      type: 'dupctrl_export',
      inputPoints: state.inputPoints,
      controlPoints: state.controlPoints,
      renames: state.renames,
      resolutions: Object.values(state.resolutions)
    });
  }

  function handleExportResult(msg){
    if(msg.ok) setExportStatus(`Fichier final : ${msg.filePath} — rapport : ${msg.reportPath}`);
    else setExportStatus(String(msg.error || "Échec de l'export."), 'err');
  }

  function init(){
    el('btnDcImportInputs')?.addEventListener('click', importInputs);
    el('btnDcImportControl')?.addEventListener('click', importControl);
    el('btnDcApplyTolerance')?.addEventListener('click', applyTolerance);
    el('btnDcExport')?.addEventListener('click', exportFinal);

    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function'){
        window.chrome.webview.addEventListener('message', ev => {
          const msg = (ev && ev.data) ? ev.data : ev;
          if(!msg || !msg.type) return;
          if(msg.type === 'dupctrl_inputs_result') handleInputsResult(msg);
          if(msg.type === 'dupctrl_control_result') handleControlResult(msg);
          if(msg.type === 'dupctrl_export_result') handleExportResult(msg);
        });
      }
    }catch(_){ }
  }

  if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
