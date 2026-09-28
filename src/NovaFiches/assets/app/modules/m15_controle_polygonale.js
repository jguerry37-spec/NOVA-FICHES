// Module manager "Contrôle de polygonale" (gated par data-nf-feature="controle_polygonale") :
// compare un classeur Excel de contrôle GNSS/optique (onglets "...GNSS..." = XY, "...Niv..." = Z)
// et un fichier GeoBase (TXT) aux coordonnées théoriques, calcule les écarts et génère un rapport
// PDF. Version "essentiel d'abord" (voir le plan) - portage v1 de l'appli indépendante de
// référence (rapport_polygonale_app, moteur Python python-docx) : organigramme d'équipe, blocs
// matériel/photos/fiches techniques et méthodologie pré-remplie sont hors périmètre.
// Comme ControlePrecision (cp_import_leve/cp_import_controle), l'état importé vit entièrement
// côté C# à la lecture (ControlePolygonaleService) puis est renvoyé déjà parsé au JS, qui le
// retransmet tel quel dans le payload d'export - aucune re-lecture de fichier n'a lieu côté JS.
(function initControlePolygonaleModule(){
  const state = {
    xySheets: [],
    zSheets: [],
    geobase: [],
    hasXlsx: false
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

  function setStatus(id, text, cls){
    const p = el(id);
    if(!p) return;
    p.textContent = text || '';
    p.className = 'small' + (cls ? ' ' + cls : '');
  }

  function updateExportButton(){
    const btn = el('btnCpolyExport');
    if(btn) btn.disabled = !state.hasXlsx;
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
    renderPreview();
  }

  // ---------- Export PDF ----------

  function exportReport(){
    if(!state.hasXlsx){
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
        projectName: el('cpolyProjectName').value,
        client: el('cpolyClient').value,
        siteCode: el('cpolySiteCode').value,
        missionDate: el('cpolyMissionDate').value,
        coordSystem: el('cpolyCoordSystem').value,
        altSystem: el('cpolyAltSystem').value,
        ppmApplied: !!el('cpolyPpmApplied').checked,
        ppmValue: el('cpolyPpmValue').value,
        conclusionText: el('cpolyConclusionText').value
      },
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

    const dateInput = el('cpolyMissionDate');
    if(dateInput && !dateInput.value) dateInput.value = new Date().toISOString().split('T')[0];

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
