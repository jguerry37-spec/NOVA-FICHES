// Module manager "Contrôle classe de précision" (gated par data-nf-feature="controle_precision") :
// compare un fichier Levé à un fichier Contrôle (même n° de point des deux côtés, sensible à la
// casse) et vérifie la conformité selon l'arrêté du 16 septembre 2003. Réimplémentation fidèle du
// moteur de l'outil de référence "NOVA_RAPPORT 2003_V7.html" - voir ControlePrecisionService.cs
// pour les mêmes formules côté C#. Comme les autres modules manager : la JS ne sert qu'à
// l'aperçu instantané (import déjà parsé côté C#, stats/conditions recalculées ici en miroir) ;
// à l'export, le C# refait l'analyse depuis les points bruts + la liste des ID inclus envoyés
// ici, et reste la seule source de vérité pour le PDF généré.
(function initControlePrecisionModule(){
  const K_ALTI_1D = 3.23;
  const K_PLANI_2D = 2.42;
  const K_3D = 2.11;

  const state = {
    levePoints: {},      // id -> {id,x,y,z}
    controlePoints: {},  // id -> {id,x,y,z}
    leveFileName: null,
    controleFileName: null,
    matches: [],          // [{id, leve, controle, dX,dY,dZ, planiDev,altiDev,threeDDev}]
    lastAnalysis: null,
    mapImageDataUrl: null // data URL PNG de la carte de situation, si générée avant l'export
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
  function fmt(n){ return (n == null || !isFinite(n)) ? '' : Number(n).toFixed(3); }

  function setStatus(id, text, cls){
    const p = el(id);
    if(!p) return;
    p.textContent = text || '';
    p.className = 'small' + (cls ? ' ' + cls : '');
  }

  // ---------- Statistiques (miroir de ControlePrecisionService.CalculateStats) ----------

  function calculateMean(arr){ return arr.length === 0 ? 0 : arr.reduce((a, b) => a + b, 0) / arr.length; }
  function calculateVariance(arr, mean){ return arr.length < 2 ? 0 : arr.reduce((a, b) => a + Math.pow(b - mean, 2), 0) / (arr.length - 1); }
  function calculateMedian(arr){
    if(!arr || arr.length === 0) return 0;
    const s = [...arr].sort((a, b) => a - b);
    const m = Math.floor(s.length / 2);
    return s.length % 2 !== 0 ? s[m] : (s[m - 1] + s[m]) / 2;
  }
  function quantile(sorted, p){
    if(!sorted || sorted.length === 0) return 0;
    const pos = (sorted.length - 1) * p;
    const base = Math.floor(pos);
    const rest = pos - base;
    return sorted[base + 1] !== undefined ? sorted[base] + rest * (sorted[base + 1] - sorted[base]) : sorted[base];
  }
  function calculateAllStats(arr){
    if(!arr || arr.length === 0) return { min: 0, max: 0, mean: 0, stdDev: 0, median: 0, n: 0, q1: 0, p33: 0, p67: 0, q3: 0 };
    const mean = calculateMean(arr);
    const stdDev = Math.sqrt(calculateVariance(arr, mean));
    const sorted = [...arr].sort((a, b) => a - b);
    return {
      min: sorted[0], max: sorted[sorted.length - 1], mean, stdDev,
      median: calculateMedian(arr), n: arr.length,
      q1: quantile(sorted, 0.25), p33: quantile(sorted, 0.33), p67: quantile(sorted, 0.67), q3: quantile(sorted, 0.75)
    };
  }

  function getColorStyle(value, stats){
    if(!stats || stats.p33 === undefined) return '';
    if(stats.min === stats.max && stats.min !== 0) return ' style="background-color:#eff6ff;"';
    if(value <= stats.p33) return ' style="background-color:#dcfce7;"';
    if(value <= stats.p67) return ' style="background-color:#fef9c3;"';
    return ' style="background-color:#fee2e2;"';
  }

  // ---------- Conditions réglementaires (miroir de ControlePrecisionService.CheckConditions) ----------

  function nPrimeThreshold(n){ return n < 5 ? 0 : Math.floor(0.01 * n + 0.232 * Math.sqrt(n)) + 1; }

  function checkConditions(devs, baseTol, k, n, c){
    if(n === 0) return null;
    const f = 1 + 1 / (2 * c * c);
    const meanDev = calculateMean(devs);
    const s1 = baseTol * f;
    const c1Ok = meanDev <= s1;

    const t1 = k * baseTol * f;
    const nPrime = nPrimeThreshold(n);
    const cOverT1 = devs.filter(d => d > t1).length;
    const c2Ok = cOverT1 <= nPrime;

    const t2 = 1.5 * t1;
    const cOverT2 = devs.filter(d => d > t2).length;
    const c3Ok = cOverT2 === 0;

    return {
      cond1: { ok: c1Ok, meanDev, seuil: s1 },
      cond2: { ok: c2Ok, countOverT1: cOverT1, seuilT1: t1, nPrimeMax: nPrime },
      cond3: { ok: c3Ok, countOverT2: cOverT2, seuilT2: t2 },
      overallOk: c1Ok && c2Ok && c3Ok
    };
  }

  // ---------- Import ----------

  function buildDict(arr){
    const d = {};
    (arr || []).forEach(p => { d[p.id] = p; });
    return d;
  }

  function importLeve(){
    setStatus('cpLeveStatus', 'Sélection du fichier…');
    post({ type: 'cp_import_leve' });
  }
  function importControle(){
    setStatus('cpControleStatus', 'Sélection du fichier…');
    post({ type: 'cp_import_controle' });
  }

  function handleLeveResult(msg){
    if(!msg.ok){ setStatus('cpLeveStatus', String(msg.error || "Échec de l'import."), 'err'); return; }
    state.levePoints = buildDict(msg.points);
    state.leveFileName = msg.fileName;
    setStatus('cpLeveStatus', `${esc(msg.fileName)} : ${msg.count} point(s)`);
    maybeBuildAssembly();
  }
  function handleControleResult(msg){
    if(!msg.ok){ setStatus('cpControleStatus', String(msg.error || "Échec de l'import."), 'err'); return; }
    state.controlePoints = buildDict(msg.points);
    state.controleFileName = msg.fileName;
    setStatus('cpControleStatus', `${esc(msg.fileName)} : ${msg.count} point(s)`);
    maybeBuildAssembly();
  }

  // ---------- Appariement + tableau d'assemblage ----------

  // "I378" -> "378" : repli d'appariement dédié au seul préfixe de rappel/implantation terrain "I"
  // (Leica Captivate), qui survit parfois même sur un fichier déjà réduit à ID/Xmes/Ymes/Zmes.
  // Restreint à cette lettre précise (pas un retrait générique de préfixe) pour ne pas entrer en
  // collision avec de vrais n° de point commençant par une autre lettre - miroir de
  // ControlePrecisionService.StripRecallPrefix côté C#.
  function stripRecallPrefix(id){
    return (id && id.length > 1 && id[0] === 'I') ? id.slice(1) : id;
  }

  function matchPoints(levePts, ctrlPts){
    // Index de repli : ID Contrôle eux-mêmes préfixés "I", dépréfixés - permet de retrouver
    // l'appariement quel que soit celui des 2 fichiers (Levé ou Contrôle) qui porte le préfixe.
    const ctrlByStrippedId = {};
    Object.keys(ctrlPts).forEach(cid => {
      const stripped = stripRecallPrefix(cid);
      if(stripped !== cid) ctrlByStrippedId[stripped] = ctrlPts[cid];
    });

    const matches = [];
    Object.keys(levePts).forEach(id => {
      const c = ctrlPts[id] || ctrlPts[stripRecallPrefix(id)] || ctrlByStrippedId[stripRecallPrefix(id)];
      if(!c) return;
      const s = levePts[id];
      const dX = s.x - c.x, dY = s.y - c.y, dZ = s.z - c.z;
      const planiDev = Math.sqrt(dX * dX + dY * dY);
      const altiDev = Math.abs(dZ);
      const threeDDev = Math.sqrt(dX * dX + dY * dY + dZ * dZ);
      matches.push({ id, leve: s, controle: c, dX, dY, dZ, planiDev, altiDev, threeDDev });
    });
    return matches;
  }

  function maybeBuildAssembly(){
    if(!state.leveFileName || !state.controleFileName) return;
    state.matches = matchPoints(state.levePoints, state.controlePoints);
    if(state.matches.length === 0){
      setStatus('cpAssemblyStatus', 'Aucun point commun trouvé (n° de point sensible à la casse).', 'err');
      el('cpParamsWrap').classList.add('nf-hidden');
      el('cpAssemblyWrap').classList.add('nf-hidden');
      return;
    }
    el('cpParamsWrap').classList.remove('nf-hidden');
    el('cpAssemblyWrap').classList.remove('nf-hidden');
    renderAssemblyTable();
  }

  function renderAssemblyTable(){
    const planiStats = calculateAllStats(state.matches.map(m => m.planiDev));
    const altiStats = calculateAllStats(state.matches.map(m => m.altiDev));
    const threeDStats = calculateAllStats(state.matches.map(m => m.threeDDev));

    const rows = state.matches.map((m, i) =>
      '<tr>' +
      `<td class="px-2 py-1"><input type="checkbox" class="cp-inc-check" data-idx="${i}" checked /></td>` +
      `<td class="px-2 py-1">${esc(m.id)}</td>` +
      `<td class="px-2 py-1">${fmt(m.dX)}</td><td class="px-2 py-1">${fmt(m.dY)}</td><td class="px-2 py-1">${fmt(m.dZ)}</td>` +
      `<td class="px-2 py-1"${getColorStyle(m.planiDev, planiStats)}>${fmt(m.planiDev)}</td>` +
      `<td class="px-2 py-1"${getColorStyle(m.altiDev, altiStats)}>${fmt(m.altiDev)}</td>` +
      `<td class="px-2 py-1"${getColorStyle(m.threeDDev, threeDStats)}>${fmt(m.threeDDev)}</td>` +
      '</tr>'
    ).join('');

    el('cpAssemblyTable').innerHTML =
      '<table style="width:100%; border-collapse:collapse;">' +
      '<thead><tr><th class="px-2 py-1 text-left">Inclure</th><th class="px-2 py-1 text-left">ID</th>' +
      '<th class="px-2 py-1 text-left">dX</th><th class="px-2 py-1 text-left">dY</th><th class="px-2 py-1 text-left">dZ</th>' +
      '<th class="px-2 py-1 text-left">Éc. Plani</th><th class="px-2 py-1 text-left">Éc. Alti</th><th class="px-2 py-1 text-left">Éc. 3D</th></tr></thead>' +
      `<tbody>${rows}</tbody></table>`;

    setStatus('cpAssemblyStatus', `${state.matches.length} point(s) apparié(s). Décoche ceux à exclure de l'analyse.`);
    el('btnCpExport').disabled = true;
    el('cpResultsWrap').classList.add('nf-hidden');
    state.lastAnalysis = null;
  }

  // ---------- Analyse de conformité ----------

  function runAnalysis(){
    const checks = document.querySelectorAll('.cp-inc-check');
    const included = [];
    checks.forEach(cb => { if(cb.checked) included.push(state.matches[Number(cb.getAttribute('data-idx'))]); });

    if(included.length === 0){
      setStatus('cpAssemblyStatus', 'Sélectionne au moins un point avant de lancer l\'analyse.', 'err');
      return;
    }

    const tolXYm = (parseFloat(el('cpTolXY').value) || 0) / 100;
    const tolZm = (parseFloat(el('cpTolZ').value) || 0) / 100;
    const c = parseFloat(el('cpSecurityCoeff').value) || 2;
    const controlType = el('cpControlType').value;
    const n = included.length;

    const fDx = included.map(m => m.dX), fDy = included.map(m => m.dY), fDz = included.map(m => m.dZ);
    const fPlani = included.map(m => m.planiDev), fAlti = included.map(m => m.altiDev), f3D = included.map(m => m.threeDDev);

    const mathStats = {
      dX: calculateAllStats(fDx), dY: calculateAllStats(fDy), dZ: calculateAllStats(fDz),
      Plani: calculateAllStats(fPlani), Alti: calculateAllStats(fAlti), '3D': calculateAllStats(f3D)
    };

    const altiCond = checkConditions(fAlti, tolZm, K_ALTI_1D, n, c);
    const planiCond = checkConditions(fPlani, tolXYm, K_PLANI_2D, n, c);
    const threeDCond = checkConditions(f3D, Math.sqrt(tolXYm * tolXYm + tolZm * tolZm), K_3D, n, c);

    let overall;
    if(controlType === '1D') overall = altiCond ? altiCond.overallOk : false;
    else if(controlType === '2D') overall = planiCond ? planiCond.overallOk : false;
    else if(controlType === '3D') overall = threeDCond ? threeDCond.overallOk : false;
    else overall = (altiCond ? altiCond.overallOk : false) && (planiCond ? planiCond.overallOk : false);

    state.lastAnalysis = { included, mathStats, altiCond, planiCond, threeDCond, overall, controlType, tolXYm, tolZm };

    renderResults();
    el('btnCpExport').disabled = false;
    setStatus('cpAssemblyStatus', `${state.matches.length} point(s) apparié(s). Décoche ceux à exclure de l'analyse.`);
  }

  function formatConditionCard(title, res, unit){
    return (
      '<div class="card" style="margin-bottom:10px;">' +
      `<div style="font-weight:700; font-size:1.05em; margin-bottom:6px;">${esc(title)}</div>` +
      '<div class="small" style="padding:8px; border-left:3px solid #60a5fa; background:#eff6ff; margin-bottom:6px;">' +
      '<div style="font-weight:600;">Condition 1 (Moyenne des écarts) :</div>' +
      `<div>Moyenne observée : ${res.cond1.meanDev.toFixed(3)}${unit}</div>` +
      `<div>Seuil S1 : ${res.cond1.seuil.toFixed(3)}${unit}</div>` +
      `<div>Résultat : <b style="color:${res.cond1.ok ? '#15803d' : '#b91c1c'};">${res.cond1.ok ? 'VALIDE' : 'NON VALIDE'}</b></div>` +
      '</div>' +
      '<div class="small" style="padding:8px; border-left:3px solid #fb923c; background:#fff7ed; margin-bottom:6px;">' +
      '<div style="font-weight:600;">Condition 2 (Dépassement du seuil T1) :</div>' +
      `<div>Nombre d'écarts &gt; T1 : ${res.cond2.countOverT1}</div>` +
      `<div>Nombre limite N' admissible : ${res.cond2.nPrimeMax}</div>` +
      `<div>Seuil T1 : ${res.cond2.seuilT1.toFixed(3)}${unit}</div>` +
      `<div>Résultat : <b style="color:${res.cond2.ok ? '#15803d' : '#b91c1c'};">${res.cond2.ok ? 'VALIDE' : 'NON VALIDE'}</b></div>` +
      '</div>' +
      '<div class="small" style="padding:8px; border-left:3px solid #f87171; background:#fef2f2; margin-bottom:6px;">' +
      '<div style="font-weight:600;">Condition 3 (Dépassement du seuil T2) :</div>' +
      `<div>Nombre d'écarts &gt; T2 : ${res.cond3.countOverT2}</div>` +
      `<div>Seuil T2 : ${res.cond3.seuilT2.toFixed(3)}${unit}</div>` +
      `<div>Résultat : <b style="color:${res.cond3.ok ? '#15803d' : '#b91c1c'};">${res.cond3.ok ? 'VALIDE' : 'NON VALIDE'}</b></div>` +
      '</div>' +
      `<div style="font-weight:700;">Conclusion ${esc(title)} : <span style="color:${res.overallOk ? '#15803d' : '#b91c1c'};">${res.overallOk ? 'CONFORME' : 'NON CONFORME'}</span></div>` +
      '</div>'
    );
  }

  function renderResults(){
    const a = state.lastAnalysis;
    el('cpResultsWrap').classList.remove('nf-hidden');
    el('cpSummary').textContent =
      `Tolérance XY : ${(a.tolXYm * 100).toFixed(1)} cm — Tolérance Z : ${(a.tolZm * 100).toFixed(1)} cm — ` +
      `Type de contrôle : ${a.controlType} — Points sélectionnés : ${a.included.length}`;

    let html = '';
    const type = a.controlType;
    if(type.includes('1D') && a.altiCond) html += formatConditionCard('Contrôle Altimétrique (1D)', a.altiCond, 'm');
    if(type.includes('2D') && a.planiCond) html += formatConditionCard('Contrôle Planimétrique (2D)', a.planiCond, 'm');
    if(type.includes('3D') && a.threeDCond) html += formatConditionCard('Contrôle 3D Isotrope', a.threeDCond, 'm');
    html += '<div style="margin-top:12px; padding:12px; border-top:1px solid #e5e7eb; text-align:center;">' +
            `<div style="font-weight:700; font-size:1.1em;">Conclusion Globale : <span style="color:${a.overall ? '#15803d' : '#b91c1c'};">${a.overall ? 'CONFORME' : 'NON CONFORME'}</span></div>` +
            '</div>';
    el('cpConditions').innerHTML = html;

    renderDetailsTable();
  }

  function renderDetailsTable(){
    const a = state.lastAnalysis;
    const rows = a.included.map(m =>
      '<tr>' +
      `<td class="px-2 py-1">${esc(m.id)}</td>` +
      `<td class="px-2 py-1">${fmt(m.leve.x)}</td><td class="px-2 py-1">${fmt(m.leve.y)}</td><td class="px-2 py-1">${fmt(m.leve.z)}</td>` +
      `<td class="px-2 py-1">${fmt(m.controle.x)}</td><td class="px-2 py-1">${fmt(m.controle.y)}</td><td class="px-2 py-1">${fmt(m.controle.z)}</td>` +
      `<td class="px-2 py-1">${fmt(m.dX)}</td><td class="px-2 py-1">${fmt(m.dY)}</td><td class="px-2 py-1">${fmt(m.dZ)}</td>` +
      `<td class="px-2 py-1"${getColorStyle(m.planiDev, a.mathStats.Plani)}>${fmt(m.planiDev)}</td>` +
      `<td class="px-2 py-1"${getColorStyle(m.altiDev, a.mathStats.Alti)}>${fmt(m.altiDev)}</td>` +
      `<td class="px-2 py-1"${getColorStyle(m.threeDDev, a.mathStats['3D'])}>${fmt(m.threeDDev)}</td>` +
      '</tr>'
    ).join('');

    el('cpDetailsTable').innerHTML =
      '<table style="width:100%; border-collapse:collapse;">' +
      '<thead><tr><th class="px-2 py-1 text-left">ID</th>' +
      '<th class="px-2 py-1 text-left">X Levé</th><th class="px-2 py-1 text-left">Y Levé</th><th class="px-2 py-1 text-left">Z Levé</th>' +
      '<th class="px-2 py-1 text-left">X Contr.</th><th class="px-2 py-1 text-left">Y Contr.</th><th class="px-2 py-1 text-left">Z Contr.</th>' +
      '<th class="px-2 py-1 text-left">dX</th><th class="px-2 py-1 text-left">dY</th><th class="px-2 py-1 text-left">dZ</th>' +
      '<th class="px-2 py-1 text-left">Éc. Plani</th><th class="px-2 py-1 text-left">Éc. Alti</th><th class="px-2 py-1 text-left">Éc. 3D</th></tr></thead>' +
      `<tbody>${rows}</tbody></table>`;
  }

  // ---------- Carte de situation ----------

  function generateMap(){
    const a = state.lastAnalysis;
    if(!a || a.included.length === 0){
      setStatus('cpMapStatus', "Lance une analyse avant de générer la carte.", 'err');
      return;
    }
    setStatus('cpMapStatus', 'Génération de la carte en cours…');
    post({
      type: 'cp_generate_map',
      points: a.included.map(m => ({ id: m.id, x: m.leve.x, y: m.leve.y, z: m.leve.z }))
    });
  }

  function handleMapResult(msg){
    if(!msg.ok){
      setStatus('cpMapStatus', String(msg.error || 'Échec de la génération de la carte.'), 'err');
      return;
    }
    state.mapImageDataUrl = msg.image || null;
    const img = el('cpMapPreview');
    if(img && state.mapImageDataUrl){
      img.src = state.mapImageDataUrl;
      img.classList.remove('nf-hidden');
    }
    setStatus('cpMapStatus', msg.coordinateSystem ? `Carte générée (système détecté : ${msg.coordinateSystem}).` : 'Carte générée.');
  }

  // ---------- Export PDF ----------

  function getTypePlan(){
    const sel = el('cpTypePlan').value;
    if(sel === 'Autre'){
      const other = el('cpTypePlanAutre').value.trim();
      return other || 'Autre (non précisé)';
    }
    return sel;
  }

  function exportReport(){
    const a = state.lastAnalysis;
    if(!a){
      setStatus('cpExportStatus', "Lance une analyse avant d'exporter.", 'err');
      return;
    }
    setStatus('cpExportStatus', 'Export en cours…');

    post({
      type: 'cp_export',
      levePoints: Object.values(state.levePoints),
      controlePoints: Object.values(state.controlePoints),
      includedIds: a.included.map(m => m.id),
      meta: {
        commanditaire: el('cpCommanditaire').value,
        redacteur: el('cpRedacteur').value,
        dateRedaction: el('cpDateRedaction').value,
        prestataireLeve: el('cpPrestataireLeve').value,
        prestataireControle: el('cpPrestataireControle').value,
        commune: el('cpCommune').value,
        adresse: el('cpAdresse').value,
        typePlan: getTypePlan(),
        reference: el('cpReference').value
      },
      params: {
        tolXYcm: parseFloat(el('cpTolXY').value) || 0,
        tolZcm: parseFloat(el('cpTolZ').value) || 0,
        controlType: el('cpControlType').value,
        securityCoefficient: parseFloat(el('cpSecurityCoeff').value) || 2,
        confidenceZ: parseFloat(el('cpConfidence').value) || 1.96
      },
      mapImageDataUrl: state.mapImageDataUrl
    });
  }

  function handleExportResult(msg){
    if(msg.ok) setStatus('cpExportStatus', `Rapport généré : ${msg.filePath}`);
    else setStatus('cpExportStatus', String(msg.error || "Échec de l'export."), 'err');
  }

  function init(){
    el('btnCpImportLeve')?.addEventListener('click', importLeve);
    el('btnCpImportControle')?.addEventListener('click', importControle);
    el('btnCpRunAnalysis')?.addEventListener('click', runAnalysis);
    el('btnCpGenerateMap')?.addEventListener('click', generateMap);
    el('btnCpExport')?.addEventListener('click', exportReport);

    const typePlanSelect = el('cpTypePlan');
    const typePlanAutre = el('cpTypePlanAutre');
    if(typePlanSelect && typePlanAutre){
      typePlanSelect.addEventListener('change', () => {
        typePlanAutre.classList.toggle('nf-hidden', typePlanSelect.value !== 'Autre');
      });
    }

    const dateInput = el('cpDateRedaction');
    if(dateInput && !dateInput.value) dateInput.value = new Date().toISOString().split('T')[0];

    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function'){
        window.chrome.webview.addEventListener('message', ev => {
          const msg = (ev && ev.data) ? ev.data : ev;
          if(!msg || !msg.type) return;
          if(msg.type === 'cp_leve_result') handleLeveResult(msg);
          if(msg.type === 'cp_controle_result') handleControleResult(msg);
          if(msg.type === 'cp_map_result') handleMapResult(msg);
          if(msg.type === 'cp_export_result') handleExportResult(msg);
        });
      }
    }catch(_){ }
  }

  if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
