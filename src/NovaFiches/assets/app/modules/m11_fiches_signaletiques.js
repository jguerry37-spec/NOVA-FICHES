// Module manager "Fiches signalétiques" (gated par data-nf-feature="fiches_signaletiques") :
// import CSV (parsing + reprojection côté C#, voir FicheSignaletiqueService/
// FicheSignaletiqueReprojection - jamais en JS ici, pas de PapaParse/proj4 vendorisés),
// photos associées par nom de fichier (client, comme m09_photos.js/m10_parametres.js), aperçu
// des champs de la fiche sélectionnée (pas de carte à l'écran - voir la note plus bas), export
// PDF via le renderer PdfSharpEngine (FicheSignaletiqueRenderer, qui a lui son propre fond de
// carte via MapTileFetcher côté C#).
(function initFichesSignaletiquesModule(){
  const MAX_PHOTO_SIDE = 1600;
  const JPEG_QUALITY = 0.74;

  const state = {
    rows: [],
    photos: new Map(), // nom de fichier (minuscule) -> dataURL
    selectedIndex: -1,
    mapProvider: 'plan', // fond de carte utilisé par le PDF exporté (pas d'aperçu carte à l'écran)
    defaultZoom: 19,
    geocoding: false
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

  function setImportStatus(text, cls){
    const p = el('fichesImportStatus');
    if(!p) return;
    p.textContent = text;
    p.className = 'pill' + (cls ? ' ' + cls : '');
  }
  function setPhotoStatus(text){ const p = el('fichesPhotoStatus'); if(p) p.textContent = text; }
  function setExportStatus(text, cls){
    const p = el('fichesExportStatus');
    if(!p) return;
    p.textContent = text || '';
    p.className = 'small' + (cls ? ' ' + cls : '');
  }
  // ===== Photos : association par nom de fichier =====

  function loadImageFromFile(file){
    return new Promise((resolve, reject) => {
      const url = URL.createObjectURL(file);
      const img = new Image();
      img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
      img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('format image non pris en charge')); };
      img.src = url;
    });
  }

  async function normalizePhotoFile(file){
    const img = await loadImageFromFile(file);
    let w = img.naturalWidth || img.width;
    let h = img.naturalHeight || img.height;
    const scale = Math.min(1, MAX_PHOTO_SIDE / Math.max(w, h));
    w = Math.max(1, Math.round(w * scale));
    h = Math.max(1, Math.round(h * scale));
    const canvas = document.createElement('canvas');
    canvas.width = w; canvas.height = h;
    const ctx = canvas.getContext('2d', { alpha: false });
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, w, h);
    ctx.drawImage(img, 0, 0, w, h);
    return canvas.toDataURL('image/jpeg', JPEG_QUALITY);
  }

  function photoFieldNames(){ return ['photo', 'photoPoint', 'photoReperage', 'photoCroquis', 'photoSituation']; }

  function resolvePhotoForRow(row){
    for(const key of photoFieldNames()){
      const name = row && row[key];
      if(name && state.photos.has(String(name).trim().toLowerCase())) return state.photos.get(String(name).trim().toLowerCase());
    }
    return null;
  }

  function expectedPhotoNames(){
    const expected = new Set();
    state.rows.forEach(row => {
      photoFieldNames().forEach(key => {
        if(row[key]) expected.add(String(row[key]).trim().toLowerCase());
      });
    });
    return expected;
  }

  function updatePhotoStatus(){
    const count = state.photos.size;
    if(!count){ setPhotoStatus('Aucune photo chargée.'); return; }
    if(!state.rows.length){ setPhotoStatus(`${count} image(s) chargée(s).`); return; }

    const expected = expectedPhotoNames();
    const orphans = Array.from(state.photos.keys()).filter(name => !expected.has(name));
    const matched = count - orphans.length;
    const rowsWithPhoto = state.rows.filter(row => resolvePhotoForRow(row)).length;

    if(!orphans.length){
      setPhotoStatus(`${count} image(s) chargée(s), toutes associées. ${rowsWithPhoto}/${state.rows.length} fiche(s) ont une photo.`);
      return;
    }
    const preview = orphans.slice(0, 5).join(', ');
    const suffix = orphans.length > 5 ? `, +${orphans.length - 5} autre(s)` : '';
    setPhotoStatus(`${count} image(s) chargée(s), ${matched} associée(s) (${rowsWithPhoto}/${state.rows.length} fiche(s)). ${orphans.length} photo(s) sans fiche correspondante : ${preview}${suffix}.`);
  }

  async function onPhotoFilesChange(ev){
    const files = Array.from((ev.target && ev.target.files) || []);
    if(!files.length) return;
    for(const file of files){
      try{
        const dataUrl = await normalizePhotoFile(file);
        state.photos.set(file.name.toLowerCase(), dataUrl);
      }catch(_){ /* fichier illisible : ignoré, ne bloque pas les suivants */ }
    }
    updatePhotoStatus();
    renderTable();
  }

  // ===== Tableau récapitulatif =====

  function renderTable(){
    const body = el('fichesTableBody');
    if(!body) return;
    if(!state.rows.length){
      body.innerHTML = '<tr><td colspan="6" class="small">Aucune fiche importée.</td></tr>';
      return;
    }
    body.innerHTML = state.rows.map((row, i) => {
      const hasPhoto = !!resolvePhotoForRow(row);
      const hasCoords = Number.isFinite(row.lon) && Number.isFinite(row.lat);
      const label = esc(row.point || row.idFiche || `Fiche ${i + 1}`);
      const cls = i === state.selectedIndex ? ' style="background:rgba(18,103,243,.08);"' : '';
      return `<tr data-index="${i}"${cls} class="nf-fiche-row">`
        + `<td>${label}</td>`
        + `<td>${esc(row.dossier)}</td>`
        + `<td>${esc(row.chantier)}</td>`
        + `<td>${esc(row.commune)}</td>`
        + `<td>${hasPhoto ? '✓' : '—'}</td>`
        + `<td>${hasCoords ? '✓' : '—'}</td>`
        + `</tr>`;
    }).join('');

    body.querySelectorAll('tr.nf-fiche-row').forEach(tr => {
      tr.addEventListener('click', () => selectRow(Number(tr.dataset.index)));
    });
  }

  function updateExportButtonsState(){
    const has = state.rows.length > 0;
    const bBatch = el('btnFichesExportBatch');
    const bPer = el('btnFichesExportPerFiche');
    const bGeocode = el('btnFichesGeocode');
    if(bBatch) bBatch.disabled = !has;
    if(bPer) bPer.disabled = !has;
    if(bGeocode) bGeocode.disabled = !has || state.geocoding;
  }

  // ===== Géocodage inverse (Nominatim, côté C# - voir MainForm.GeocodeFichesSignaletiquesAsync) =====

  function setGeocodeStatus(text, cls){
    const p = el('fichesGeocodeStatus');
    if(!p) return;
    p.textContent = text || '';
    p.className = 'small' + (cls ? ' ' + cls : '');
  }

  function startGeocoding(){
    if(state.geocoding) return;
    const targets = [];
    state.rows.forEach((row, index) => {
      const hasAddress = row.adresse && String(row.adresse).trim() !== '';
      const hasCoords = Number.isFinite(row.lon) && Number.isFinite(row.lat);
      if(!hasAddress && hasCoords) targets.push({ index, lat: row.lat, lon: row.lon });
    });
    if(!targets.length){
      setGeocodeStatus('Toutes les fiches ont déjà une adresse (ou aucune coordonnée exploitable).');
      return;
    }
    state.geocoding = true;
    updateExportButtonsState();
    setGeocodeStatus(`Géocodage en cours… 0/${targets.length} (environ ${Math.ceil(targets.length * 1.1)}s)`);
    post({ type: 'fiches_geocode', targets });
  }

  function handleGeocodeProgress(msg){
    setGeocodeStatus(`Géocodage en cours… ${msg.current}/${msg.total}`);
  }

  function handleGeocodeResult(msg){
    state.geocoding = false;
    updateExportButtonsState();
    const updates = Array.isArray(msg.updates) ? msg.updates : [];
    let applied = 0;
    updates.forEach(u => {
      const row = state.rows[u.index];
      if(!row) return;
      if(u.adresse){ row.adresse = u.adresse; applied++; }
      if(u.commune) row.commune = u.commune;
      // Le département fourni par le CSV, s'il existe déjà, n'est jamais écrasé par le
      // géocodage - seule une valeur absente est complétée.
      if(u.departement && (!row.departement || String(row.departement).trim() === '')) row.departement = u.departement;
    });
    renderTable();
    setGeocodeStatus(applied > 0 ? `${applied} adresse(s) trouvée(s) sur ${updates.length ? updates.length : 0}.` : 'Aucune adresse trouvée pour les fiches sans adresse.');
  }

  // ===== Aperçu de la fiche sélectionnée =====
  // Pas de carte à l'écran ici : peu d'intérêt tant que la donnée n'est pas encore vérifiée
  // (le rendu carte "officiel" existe déjà dans le PDF exporté, via MapTileFetcher côté C#).
  // Ce panneau montre plutôt les champs qui apparaîtront réellement sur la fiche PDF, pour
  // repérer en un coup d'oeil une adresse/photo manquante avant d'exporter.

  function previewFieldRow(label, value){
    return `<tr><td style="padding:2px 8px 2px 0; color:var(--mut,#667c8a); white-space:nowrap; vertical-align:top;">${esc(label)}</td>`
         + `<td style="padding:2px 0;">${esc(value || '—')}</td></tr>`;
  }

  function selectRow(index){
    state.selectedIndex = index;
    renderTable();

    const row = state.rows[index];
    const empty = el('fichesPreviewEmpty');
    const content = el('fichesPreviewContent');
    if(!row){
      empty?.classList.remove('nf-hidden');
      content?.classList.add('nf-hidden');
      return;
    }
    empty?.classList.add('nf-hidden');
    content?.classList.remove('nf-hidden');

    const pointEl = el('fichesPreviewPoint');
    if(pointEl) pointEl.textContent = row.point || row.idFiche || `Fiche ${index + 1}`;
    const dossierEl = el('fichesPreviewDossier');
    if(dossierEl) dossierEl.textContent = [row.dossier, row.chantier].filter(Boolean).join(' — ');

    const photo = resolvePhotoForRow(row);
    const photoImg = el('fichesPreviewPhoto');
    if(photoImg){
      if(photo){ photoImg.src = photo; photoImg.classList.remove('nf-hidden'); }
      else { photoImg.removeAttribute('src'); photoImg.classList.add('nf-hidden'); }
    }

    const hasCoords = Number.isFinite(row.lon) && Number.isFinite(row.lat);
    const coordText = hasCoords ? `${row.lat.toFixed(7)}, ${row.lon.toFixed(7)}` : 'Non disponible';
    const hasXyz = [row.x, row.y, row.z].every(Number.isFinite);
    const xyzText = hasXyz ? `${row.x.toFixed(3)} / ${row.y.toFixed(3)} / ${row.z.toFixed(3)}` : '—';

    const fields = el('fichesPreviewFields');
    if(fields) fields.innerHTML = [
      previewFieldRow('Client / MOA', row.client),
      previewFieldRow('Prestataire', row.prestataire || 'NOVATLAS'),
      previewFieldRow('Commune', row.commune),
      previewFieldRow('Département', row.departement),
      previewFieldRow('Adresse', row.adresse),
      previewFieldRow('Site / opération', row.site),
      previewFieldRow('Nature', row.nature),
      previewFieldRow('Système plani', row.systemePlani),
      previewFieldRow('Système alti', row.systemeAlti),
      previewFieldRow('EPSG source', row.epsgSource),
      previewFieldRow('Coord. WGS84', coordText),
      previewFieldRow('X / Y / Z', xyzText)
    ].join('');
  }

  // ===== Export payload =====

  function buildExportPayload(mode){
    return {
      type: 'pdfsharp_fiches_signaletiques',
      mode,
      fileName: 'NOVA_Fiches_Signaletiques.pdf',
      fichesSignaletiques: {
        defaultZoom: state.defaultZoom,
        mapProvider: state.mapProvider,
        rows: state.rows.map(row => Object.assign({}, row, { photoImageData: resolvePhotoForRow(row) || null }))
      }
    };
  }

  // ===== Messages C# =====

  function handleCsvLoaded(msg){
    state.rows = Array.isArray(msg.rows) ? msg.rows : [];
    state.selectedIndex = -1;
    const rejectCount = Number(msg.rejectCount) || 0;
    const rejectSuffix = rejectCount > 0 ? ` (${rejectCount} ligne(s) ignorée(s))` : '';
    setImportStatus(`${state.rows.length} fiche(s) chargée(s)${rejectSuffix}`, state.rows.length ? '' : 'warn');
    setGeocodeStatus('');
    setExportStatus('');
    renderTable();
    updatePhotoStatus();
    updateExportButtonsState();
  }

  function init(){
    el('btnFichesImportCsv')?.addEventListener('click', () => {
      setImportStatus('Import en cours…');
      post({ type: 'fiches_import_csv' });
    });
    el('fichesPhotoFiles')?.addEventListener('change', onPhotoFilesChange);
    el('btnFichesTemplate')?.addEventListener('click', () => post({ type: 'fiches_download_template' }));
    el('btnFichesExportBatch')?.addEventListener('click', () => {
      setExportStatus('Export en cours…');
      post(buildExportPayload('batch'));
    });
    el('btnFichesExportPerFiche')?.addEventListener('click', () => {
      setExportStatus('Export en cours…');
      post(buildExportPayload('perFiche'));
    });
    el('btnFichesGeocode')?.addEventListener('click', startGeocoding);

    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function'){
        window.chrome.webview.addEventListener('message', ev => {
          const msg = (ev && ev.data) ? ev.data : ev;
          if(!msg || !msg.type) return;
          if(msg.type === 'fiches_csv_loaded') handleCsvLoaded(msg);
          if(msg.type === 'fiches_error') setImportStatus(String(msg.message || 'Erreur import CSV'), 'err');
          if(msg.type === 'fiches_export_result'){
            if(msg.ok) setExportStatus(`Export terminé : ${msg.fileName || msg.folder || 'OK'}`);
            else setExportStatus(String(msg.error || "Échec de l'export"), 'err');
          }
          if(msg.type === 'fiches_geocode_progress') handleGeocodeProgress(msg);
          if(msg.type === 'fiches_geocode_result') handleGeocodeResult(msg);
        });
      }
    }catch(_){ }
  }

  if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
