 /*
===============================================================================
Topo — BASE v1.76.0
- Source validée : v1.76.1_step2.3_ux_FINAL_fixed_pdfRect
- Objectif v1.76 : repartir sur une base propre et stable.
- Garantie : aucune modification fonctionnelle (import, calculs, PDFs).
- Nettoyage : consolidation des définitions dupliquées (ex: drawHeaderV2).
===============================================================================
*/
window.addEventListener('error', (e)=>{ try{ APP.state.lastError = e.message || e.error || e; APP.uiRefreshDebug && APP.uiRefreshDebug(); }catch(_){} });


/*
===============================================================================
Topo — Rapport d'intervention (v1.76.0) — VERSION REFACTOR "PROPRE"
Objectifs :
- 1 seule source de vérité pour l'état (APP.state)
- 1 seule entrée d'initialisation (APP.init)
- 1 seul header PDF (drawHeaderV2) utilisé partout
- garde 100% des fonctionnalités existantes (import TXT + PDFs)
Organisation :
  [1] APP.state + utilitaires
  [2] Import TXT + parsing
  [3] Calculs topo
  [4] PDF rendering
  [5] UI (bind + refresh)
===============================================================================
*/

/* =====================================================================================
   v1.76 — PDFs mis en page + couleurs + Station libre complet + Ligne de réf format rabattement
===================================================================================== */

// Version applicative (source unique = injection C# window.__NF_BUILD)
// IMPORTANT: ne pas figer ici — sinon le footer PDF peut afficher une ancienne version.
const APP_VERSION = (()=>{
  try{
    const raw = (window.__NF_BUILD || window.APP_BUILD || window.APP_VERSION || "DEV").toString();
    return raw.replace(/\.0$/,'');
  }catch(e){
    return "DEV";
  }
})();

// Reserve a safe area at the bottom so content never overlaps the footer.
// Footer line is drawn around h-14, with text near h-9 / h-5.5.
const PDF_FOOTER_RESERVE = 22;

// Reserve a safe area at the TOP so content never overlaps the header/logo on pages 2+.
// Enforced through AutoTable margins and startY guards.
const PDF_HEADER_SAFE_TOP = 24;
window.PDF_HEADER_SAFE_TOP = PDF_HEADER_SAFE_TOP;

function nfApplyHeaderSafeToAutoTableOpts_(opts){
  try{
    opts = opts || {};
    const m = opts.margin || {};
    const top = Number.isFinite(m.top) ? m.top : 0;
    m.top = Math.max(top, PDF_HEADER_SAFE_TOP);
    opts.margin = m;
    if(Number.isFinite(opts.startY)) opts.startY = Math.max(opts.startY, PDF_HEADER_SAFE_TOP);
  }catch(_){ }
  return opts;
}

function nfAutoTable(doc, opts){
  return doc.autoTable(nfApplyHeaderSafeToAutoTableOpts_(opts));
}
window.nfAutoTable = nfAutoTable;
// LandXML duplicate point guard.
// Import stays neutral; modules call this before exploiting CgPoint names.
window.NF_LANDXML_DUP_POLICY = window.NF_LANDXML_DUP_POLICY || {};

function nfHashText_(s){
  s = String(s || '');
  let h = 2166136261;
  for(let i=0;i<s.length;i++){
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return String(h >>> 0) + ':' + String(s.length);
}

function nfXmlNodes_(dom, tagName){
  const a = Array.from(dom.getElementsByTagName(tagName) || []);
  let b = [];
  try{ b = Array.from(dom.getElementsByTagNameNS('*', tagName) || []); }catch(_){ b = []; }
  return Array.from(new Set(a.concat(b)));
}

function nfAnalyseLandXmlDuplicateCgPoints(xmlText){
  const out = { duplicateNames: [], duplicateCount: 0, pointCount: 0 };
  try{
    const dom = new DOMParser().parseFromString(String(xmlText || ''), 'text/xml');
    const pts = nfXmlNodes_(dom, 'CgPoint').filter(p => {
      const n = String(p.getAttribute('oID') || p.getAttribute('name') || '').trim();
      return !!n;
    });
    out.pointCount = pts.length;
    const by = new Map();
    pts.forEach((p, idx)=>{
      const n = String(p.getAttribute('oID') || p.getAttribute('name') || '').trim();
      if(!by.has(n)) by.set(n, []);
      by.get(n).push({ node:p, index:idx });
    });
    for(const [name, arr] of by.entries()){
      if(arr.length > 1){
        out.duplicateNames.push({ name, count: arr.length });
        out.duplicateCount += arr.length;
      }
    }
    out.duplicateNames.sort((a,b)=>a.name.localeCompare(b.name, 'fr', { numeric:true, sensitivity:'base' }));
  }catch(e){ /* ignore */ }
  return out;
}
window.nfAnalyseLandXmlDuplicateCgPoints = nfAnalyseLandXmlDuplicateCgPoints;

function nfFilterLandXmlDuplicateCgPoints(xmlText, policy){
  policy = String(policy || 'keep-all');
  if(policy === 'keep-all') return String(xmlText || '');
  try{
    const dom = new DOMParser().parseFromString(String(xmlText || ''), 'text/xml');
    const pts = nfXmlNodes_(dom, 'CgPoint').filter(p => {
      const n = String(p.getAttribute('name') || p.getAttribute('oID') || '').trim();
      return !!n;
    });
    const by = new Map();
    pts.forEach((p, idx)=>{
      const n = String(p.getAttribute('name') || p.getAttribute('oID') || '').trim();
      if(!by.has(n)) by.set(n, []);
      by.get(n).push({ node:p, index:idx });
    });
    const remove = new Set();
    for(const arr of by.values()){
      if(arr.length <= 1) continue;
      if(policy === 'drop-all'){
        arr.forEach(x => remove.add(x.node));
      }else if(policy === 'keep-first'){
        arr.slice(1).forEach(x => remove.add(x.node));
      }else if(policy === 'keep-last'){
        arr.slice(0, -1).forEach(x => remove.add(x.node));
      }
    }
    remove.forEach(n => { try{ n.parentNode && n.parentNode.removeChild(n); }catch(_){} });
    return new XMLSerializer().serializeToString(dom);
  }catch(e){
    console.warn('[LandXML] Duplicate filtering failed', e);
    return String(xmlText || '');
  }
}
window.nfFilterLandXmlDuplicateCgPoints = nfFilterLandXmlDuplicateCgPoints;

function nfResolveLandXmlPointDuplicates(xmlText, moduleName){
  const raw = String(xmlText || '');
  // Import and module entry stay neutral: keep the full LandXML. Modules that
  // need user arbitration show duplicates directly in their visualisation.
  try{
    const info = nfAnalyseLandXmlDuplicateCgPoints(raw);
    if(info.duplicateNames.length){
      const el = document.getElementById('sbStatus');
      if(el) el.textContent = `Doublons LandXML detectes : ${info.duplicateNames.length} point(s), ${info.duplicateCount} occurrence(s) conservee(s)`;
    }
  }catch(_){ }
  return raw;

  const info = nfAnalyseLandXmlDuplicateCgPoints(raw);
  if(!info.duplicateNames.length) return raw;

  const key = nfHashText_(raw);
  let policy = window.NF_LANDXML_DUP_POLICY[key] || null;
  if(!policy){
    const sample = info.duplicateNames.slice(0, 8).map(d => `${d.name} (${d.count})`).join(', ');
    const more = info.duplicateNames.length > 8 ? `, ...` : '';
    const msg =
      `Attention : plusieurs points LandXML portent le meme numero.\n\n` +
      `Module : ${moduleName || 'analyse'}\n` +
      `Noms concernes : ${info.duplicateNames.length}\n` +
      `Occurrences concernees : ${info.duplicateCount}\n` +
      `Exemples : ${sample}${more}\n\n` +
      `Choix a appliquer pour cette exploitation :\n` +
      `1 = garder tous les points\n` +
      `2 = supprimer tous les points qui ont un numero doublon\n` +
      `3 = garder uniquement la premiere occurrence\n` +
      `4 = garder uniquement la derniere occurrence\n\n` +
      `Saisis 1, 2, 3 ou 4.`;
    const choice = (window.prompt ? window.prompt(msg, '1') : '1') || '1';
    policy = ({ '1':'keep-all', '2':'drop-all', '3':'keep-first', '4':'keep-last' })[String(choice).trim()] || 'keep-all';
    window.NF_LANDXML_DUP_POLICY[key] = policy;
  }

  try{
    const label = ({ 'keep-all':'tous conserves', 'drop-all':'doublons supprimes', 'keep-first':'premiere occurrence conservee', 'keep-last':'derniere occurrence conservee' })[policy] || policy;
    const el = document.getElementById('sbStatus');
    if(el) el.textContent = `Doublons LandXML : ${label}`;
  }catch(_){ }
  return nfFilterLandXmlDuplicateCgPoints(raw, policy);
}
window.nfResolveLandXmlPointDuplicates = nfResolveLandXmlPointDuplicates;

/**
 * Ensures there is enough vertical room on the current page.
 * If not, adds a new page and returns a safe Y start.
 */
function ensurePdfRoom(doc, y, neededHeight, topY = PDF_HEADER_SAFE_TOP){
  try{
    const pageH = doc.internal.pageSize.getHeight();
    if((y + neededHeight) > (pageH - PDF_FOOTER_RESERVE)){
      doc.addPage();
      return topY;
    }
  }catch(e){}
  return y;
}

// Backward-compat alias (some builders still call ensureSpace)
if(typeof ensureSpace !== "function"){
  function ensureSpace(doc, y, neededHeight, topY = PDF_HEADER_SAFE_TOP){
    return ensurePdfRoom(doc, y, neededHeight, topY);
  }
}
// Build marker (UI + PDF footer) — used to verify the correct assets are loaded.
const APP_BUILD = (window.__NF_BUILD || window.APP_BUILD || "DEV").toString();
window.APP_BUILD = APP_BUILD;
const APP_FREEZE_NOTE = "Stabilisation: refreshAll + fingerprint + exports centralisés";

// Footer build/version (UI)
// (géré centralement via window.__NF_BUILD dans inline_03.js)

const BRAND = { r:18, g:103, b:243 };

// ===================== Results tables (thick separators) =====================
// Vertical separators after these column indexes:
//   0: ID | 3: Z théo/calc | 6: Z mes | 9: Dz | STATUT
const RESULTS_VLINES_AFTER = [0,3,6,9];
// One single thickness for ALL thick lines (separators + left/right borders)
// Keep it subtle so it remains readable without overpowering the grid.
const RESULTS_THICK_W = 0.45;
// Fixed column widths so Implantation / Ligne / Complet share EXACT same geometry (prevents "double lines")
// Total width = 190mm (A4 with 10mm left/right margins)
const RESULTS_COLUMN_STYLES = {
  0:{ cellWidth:22 }, // ID point
  1:{ cellWidth:24 }, // X théo
  2:{ cellWidth:24 }, // Y théo
  3:{ cellWidth:16 }, // Z théo
  4:{ cellWidth:24 }, // X mes
  5:{ cellWidth:24 }, // Y mes
  6:{ cellWidth:16 }, // Z mes
  7:{ cellWidth:10 }, // Dx/dL
  8:{ cellWidth:10 }, // Dy/dT
  9:{ cellWidth:10 }, // Dz/dA
  10:{ cellWidth:10 } // STATUT
};


// ===================== STATUT column fit (ALL reports) =====================
// Force the STATUT column (index 10) to stay on a single line without changing column widths.
function applyStatusFitAutoTable(h) {
  try {
    if (!h || !h.column) return;
    if (h.column.index !== 10) return; // STATUT column
    const st = (h.cell && h.cell.styles) ? h.cell.styles : (h.cell.styles = {});
    st.halign = "center";
    st.valign = "middle";
    st.cellPadding = 0.12;
    st.fontStyle = "bold";
    // Slightly smaller to ensure "VALIDÉ/REFUSÉ" fits even in narrow column
    st.fontSize = (h.section === "head") ? 5.0 : 5.2;
    // Prevent odd word-splitting like "STATU T"
    st.overflow = "visible";
  } catch (e) { /* no-op */ }
}




// ===================== AutoTable wrapper for RESULTS tables (v2) =====================
// Guarantees: columnStyles, STATUT fitting.
// Thick structural lines are NOT drawn here anymore (deterministic post-processing).
function autoTableResults(doc, opts) {
  opts = opts || {};

  // Allow callers to add extra hooks without overriding the mandatory ones
  const userParse = opts.didParseCellExtra || opts.didParseCell;
  const userCell  = opts.didDrawCellExtra  || opts.didDrawCell;
  const userDraw  = opts.didDrawPageExtra  || opts.didDrawPage;

  // Optional deterministic thick-lines spec (ONLY measurement, never drawing here)
  // Expected values: "implantation" | "ligne".
  const nfThickSpec = (opts.nfThickSpec != null) ? String(opts.nfThickSpec) : null;

  // Remove custom hook keys so they don't get passed to autoTable
  delete opts.didParseCellExtra;
  delete opts.didDrawCellExtra;
  delete opts.didDrawPageExtra;
  delete opts.nfTag;
  delete opts.nfBoxTag;
  delete opts.nfThickSpec;

  function isLegacyThickHook(fn){
    try{
      if(typeof fn !== 'function') return false;
      const s = Function.prototype.toString.call(fn);
      // Covers inline wrappers such as: (h)=>thickTableLinesDidDrawPage(h, ...)
      return s.indexOf('thickTableLinesDidDrawPage') >= 0 || s.indexOf('thickTableLinesDidDrawCell') >= 0;
    }catch(e){
      return false;
    }
  }


  // LOCKED thick-lines measurement window:
  // - If nfThickSpec is provided, we open a measurement transaction ONLY
  //   for the duration of THIS autoTable call.
  // - No didDrawPage measurement (source of y leaks / cross-block bleed).
  // - Only real cell geometry is recorded.
  const _do = () => nfAutoTable(doc, {
    ...opts,
    columnStyles: opts.columnStyles || RESULTS_COLUMN_STYLES,

    didDrawCell: function (cellData) {
      // Measurement only (deterministic): record real table bounds per page.
      try{
        if(nfThickSpec && window.NF_THICKLINES && typeof window.NF_THICKLINES.measureCell === 'function'){
          window.NF_THICKLINES.measureCell(Object.assign({ doc: doc }, cellData || {}));
        }
      }catch(e){}
      // Never draw thick structural lines via autoTable hooks.
      if (typeof userCell === "function" && !isLegacyThickHook(userCell)) userCell(cellData);
    },

    didParseCell: function (data) {
      applyStatusFitAutoTable(data);
      if (typeof userParse === "function") userParse(data);
    },

    didDrawPage: function (hookData) {
      // Never call legacy thick-line hooks here.
      if (typeof userDraw === "function" && !isLegacyThickHook(userDraw)) userDraw(hookData);
    }
  });

  if(nfThickSpec && window.NF_THICKLINES && typeof window.NF_THICKLINES.begin === 'function'){
    try{ window.NF_THICKLINES.begin(doc, nfThickSpec); }catch(e){}
    try{ return _do(); }
    finally{ try{ window.NF_THICKLINES.end(doc); }catch(e){} }
  }

  return _do();
}



const NOVATLAS_ADDRESS = "NOVATLAS — 24 boulevard Paul Vaillant Couturier — 94200 IVRY SUR SEINE";

let lastData = null;

// ------------------------------------------------------------
// ÉCHANGES (V2) — état des imports menu WinForms
// (TXT points / GSI Leica). Ne touche pas au pipeline PDF existant.
// ------------------------------------------------------------
window.nfExchange = window.nfExchange || {
  landxml: { loaded:false, fileName:null, setupCount:0, obsCount:0, stakeoutCount:0, reflineCount:0 }
};

// ------------------------------------------------------------
// V2 — Bandeau Projet (état global)
// Objectif terrain: comprendre instantanément ce qui est chargé.
// ------------------------------------------------------------
function updateProjectBanner(){
  try{
    const elL = document.getElementById('projLandXml');
    const elReady = document.getElementById('projReady');
    const elHint = document.getElementById('projHint');
    if(!elL || !elReady) return;

    const x = window.nfExchange?.landxml || { loaded:false };
    const hasData = !!(window.__NF_LASTDATA || window.lastData || (typeof lastData !== 'undefined' ? lastData : null));
    const loaded = !!x.loaded || hasData;

    if(loaded){
      elL.textContent = 'LandXML : chargé';
      elL.className = 'pill ok';
    }else{
      elL.textContent = 'LandXML : non';
      elL.className = 'pill err';
    }

    if(hasData){
      elReady.textContent = 'Projet prêt';
      elReady.className = 'pill ok';
      if(elHint) elHint.textContent = 'OK : tu peux générer les PDFs.';

            // V2: Intervenant auto si dispo (sinon saisie manuelle)
      try{ if(typeof syncIntervenantUI==='function') syncIntervenantUI((window.__NF_LASTDATA||window.lastData||lastData)); }catch(_e){}
}else{
      elReady.textContent = 'Projet incomplet';
      elReady.className = 'pill warn';
      if(elHint) elHint.textContent = 'Importer un fichier LandXML pour activer les PDFs.';
    }
  }catch(e){ /* no-op */ }
}
window.updateProjectBanner = updateProjectBanner;

function updateExchangePills(){
  try{
    const x = window.nfExchange?.landxml || { loaded:false };
    const elFile = document.getElementById('fileStatus');
    const elX = document.getElementById('exXmlStatus');

    const extra = [];
    if(x.setupCount) extra.push(`${x.setupCount} station(s)`);
    if(x.obsCount) extra.push(`${x.obsCount} obs`);
    if(x.stakeoutCount) extra.push(`${x.stakeoutCount} impla`);
    if(x.reflineCount) extra.push(`${x.reflineCount} rab`);

    const label = x.loaded
      ? (`LandXML : ${x.fileName||'chargé'}${extra.length?` (${extra.join(' | ')})`:''}`)
      : 'LandXML : aucun';

    if(elFile) elFile.textContent = label;
    if(elX) elX.textContent = label;

    updateProjectBanner();
  }catch(e){ /* no-op */ }
}

// Réception des messages du host WinForms (imports via menu)
try{
  if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function'){
    window.chrome.webview.addEventListener('message', async (ev)=>{
      // --- lazy loader: guarantee parseLandXmlLeica is available ---
      async function __ensureLeicaParserLoaded(){
        try{
          if(typeof window.parseLandXmlLeica === 'function') return true;
          // try load the parser script explicitly (cache-busting)
          return await new Promise((resolve)=>{
            try{
              const s = document.createElement('script');
              s.src = './app/modules/m02_parser_calc.js?ts=' + Date.now();
              s.async = false;
              s.onload = ()=>resolve(typeof window.parseLandXmlLeica === 'function');
              s.onerror = ()=>resolve(false);
              document.head.appendChild(s);
            }catch(e){ resolve(false); }
          });
        }catch(e){ return false; }
      }
      // ----------------------------------------------------------
      try{
        const msg = ev && ev.data ? ev.data : ev;
        if(!msg || !msg.type) return;

                        // LandXML (Leica) : import = conversion en dataset AppLog-compatible
        // -> réutilise les PDFs existants sans modifier le pipeline.
        if(msg.type === 'importLandXml'){
          try{
            // reset dataset (évite de réutiliser un ancien AppLog si parsing échoue)
            try{ lastData = null; }catch(_){ }
            try{ window.__NF_LASTDATA = null; }catch(_){ }
            try{ window.lastData = null; }catch(_){ }
            try{ disableExportButtons(true); }catch(_){ }

            const xmlText = String(msg.xmlText || "");
            const fn = msg.fileName || null;
            if(typeof window.parseLandXmlLeica !== 'function'){
              const ok = await __ensureLeicaParserLoaded();
              if(!ok || typeof window.parseLandXmlLeica !== 'function'){
                throw new Error('parseLandXmlLeica indisponible (script non chargé)');
              }
            }
            const data = window.parseLandXmlLeica(xmlText, fn);
            if(!data){ throw new Error('Parsing LandXML : dataset vide'); }

            // expose LandXML as an "Échanges" dataset (terrain readability)
            try{
              const x = window.nfExchange.landxml;
              x.loaded = true;
              x.fileName = fn || null;
              x.setupCount = Array.isArray(data.stationLibreRuns) ? data.stationLibreRuns.length : 0;
              x.obsCount = (Array.isArray(data.stationLibreRuns) ? data.stationLibreRuns.reduce((a,r)=>a+(r?.observations?.length||0),0) : 0);
              x.stakeoutCount = Array.isArray(data.implantation?.points) ? data.implantation.points.length : 0;
              x.reflineCount = Array.isArray(data.ligneRef)
                ? data.ligneRef.reduce((a,l)=>a+((l?.rabPoints?.length)||0),0)
                : 0;
              updateExchangePills();
            }catch(_){ }

            // activer dataset global
            lastData = data;
            try{ window.__NF_LASTDATA = data; }catch(_){ }
            try{ window.lastData = data; }catch(_){ }

            // V2: Intervenant (auto) — préremplit depuis le LandXML uniquement si vide
            try{
              const elOp = document.getElementById('metaIntervenant');
              if(elOp){
                const op = String((data && data.meta && (data.meta.operator||data.meta.creator||data.meta.author)) ? (data.meta.operator||data.meta.creator||data.meta.author) : '').trim();
                if(op && !String(elOp.value||'').trim()){
                  elOp.value = op;
                  elOp.dataset.mode = 'auto';
                }
              }
            }catch(_){ }

            // rendu + réactivation boutons
            try{
              if(typeof window.renderAll === 'function') window.renderAll(data);
            }catch(_){ }
            try{ disableExportButtons(false); }catch(_){ }
            try{ if(typeof window.applyReportButtonStateFromData === 'function') window.applyReportButtonStateFromData(data); }catch(_){ }
            try{ if(typeof window.updatePdfSharpButtonState === 'function') window.updatePdfSharpButtonState(); }catch(_){ }
            try{ if(typeof window.updateProjectBanner === 'function') window.updateProjectBanner(); }catch(_){ }

            try{ setStatus(`LandXML : chargé (${fn||'fichier.xml'})`); }catch(_){ }
          }catch(e){
            try{ setStatus('Erreur import LandXML : ' + (e?.message || String(e)), true); }catch(_){ }
            try{ disableExportButtons(true); }catch(_){ }
          }
          return;
        }
      }catch(e){
        try{ console.warn('Échanges message handling failed', e); }catch(_){ }
      }
    });
  }
}catch(_){ /* ignore */ }

// ------------------------------------------------------------
// disableExportButtons() — utilitaire global
// Certains modules (import TXT / PDF) l'appellent.
// Si absent => les boutons restent bloqués en disabled.
// ------------------------------------------------------------
function disableExportButtons(disabled) {
  const ids = [
    "btnPdfIntervention",
    "btnPdfInterventionPdfSharp",
    "btnPdfLigneRef",
    "btnPdfStation",
    "btnPdfFull"
  ];
  ids.forEach((id) => {
    const el = document.getElementById(id);
    if (el) el.disabled = !!disabled;
  });
}
window.disableExportButtons = disableExportButtons;


// ------------------------------------------------------------
// refreshAll() — utilisé par certains modules (PDF / UI)
// (Il existait avant dans un module optionnel...)
// On le remet ici pour éviter toute régression.
// ------------------------------------------------------------
window.refreshAll = function refreshAll(){
  try{
    if(!lastData) return;
    if(typeof renderAll === "function"){
      renderAll(lastData);
    }
    try{
      if(typeof window.NF_afterRefresh === 'function') window.NF_afterRefresh(lastData);
    }catch(_){ }
  }catch(e){
    try{ console.warn("refreshAll() failed", e); }catch(_){ }
  }
};

// Compat: ancienne API appelait refreshAll() en global
var refreshAll = window.refreshAll;

let logoDataUrl = "data:image/png;base64,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";
// UI logo binding (header)
try{
  const uiLogoEl = document.getElementById("uiLogo");
  if(uiLogoEl && (!uiLogoEl.getAttribute("src") || uiLogoEl.getAttribute("src")==="")){
    uiLogoEl.src = logoDataUrl;
  }
}catch(e){}
let sigDataUrl = null;
let sigImageType = "JPEG";
let logoImageType = "JPEG";

// Polices PDF (jsPDF)
const pdfFonts = {
  title: { family: "helvetica", style: "bold" }, 
  body:  { family: "helvetica", style: "normal" }, // HKGrotesk si chargé
  bodyBold: { family:"helvetica", style:"bold" }   // HKGrotesk Bold si chargé
};

function setStatus(text, isErr=false){
  const s = document.getElementById("footerStatus");
  if(!s) return;
  s.textContent = text;
  s.classList.toggle("err", !!isErr);

  // Si erreur : afficher une vraie fenêtre (MessageBox côté WinForms si possible)
  if(isErr){
    try { showErrorDialog(text); } catch(e) { /* no-op */ }
  }
}

// Fenêtre d'erreur "terrain" : MessageBox côté WinForms via WebView2, sinon alert()
let __lastErrText = "";
let __lastErrTs = 0;
function showErrorDialog(message){
  const msg = (message ?? "").toString().trim();
  if(!msg) return;

  // Anti-spam (évite 10 popups identiques en boucle)
  const now = Date.now();
  if(msg === __lastErrText && (now - __lastErrTs) < 1500) return;
  __lastErrText = msg; __lastErrTs = now;

  try{
    if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
      window.chrome.webview.postMessage({ type: 'ui_error', message: msg });
      return;
    }
  }catch(e){ /* fallback */ }

  alert(msg);
}

// ------------------------------------------------------------
// Host → UI messages (WinForms C# -> WebView2)
// ------------------------------------------------------------
(function initHostMessageBridge(){
  try{
    if(!(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === "function")) return;

    window.chrome.webview.addEventListener("message", (ev) => {
      const msg = ev?.data || {};
      if(!msg || typeof msg !== "object") return;

      // PdfSharp report result
      if(String(msg.type||"").toLowerCase() === "pdf_result"){
        const ok = !!msg.ok;
        const report = String(msg.report||"").replace(/_/g," ");
        const filePath = String(msg.filePath||"").trim();
        const err = String(msg.error||"").trim();

        if(ok){
          const fn = filePath ? filePath.split(/[\\/]/).pop() : "";
          setStatus(`PDF OK (${report}) : ${fn}${filePath ? " — " + filePath : ""}`, false);
        }else{
          setStatus(`PDF KO (${report})`, true);
          if(err) showErrorDialog(err);
        }
        return;
      }
    });
  }catch(_){ /* ignore */ }
})();

// --- Save As helper (Option A: handled in HTML/JS) ---

function arrayBufferToBase64(buffer) {
  let binary = '';
  const bytes = new Uint8Array(buffer);
  const len = bytes.byteLength;
  for (let i = 0; i < len; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary);
}

async function saveBlobAs(blob, fileName, mimeType) {
  // WebView2 (WinForms) : délègue l’enregistrement au host (fenêtre "Enregistrer sous…")
  try {
    if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
      const ab = await blob.arrayBuffer();
      const b64 = arrayBufferToBase64(ab);
      setStatus('Export: ouverture Enregistrer sous…');
      window.chrome.webview.postMessage({
        type: 'saveAs',
        fileName: fileName,
        mimeType: mimeType || blob.type || '',
        base64: b64
      });
      return;
    }
  } catch (e) {
    setStatus('Export: postMessage saveAs a échoué: ' + (e?.message||e), true);
  }


  try {
    if (window.showSaveFilePicker) {
      const ext = (fileName.split('.').pop() || '').toLowerCase();
      const typeMap = {
        pdf: { description: "PDF", accept: { "application/pdf": [".pdf"] } },
        txt: { description: "Texte", accept: { "text/plain": [".txt"] } },
        json: { description: "JSON", accept: { "application/json": [".json"] } },
        csv: { description: "CSV", accept: { "text/csv": [".csv"] } }
      };
      const pickerTypes = [];
      if (typeMap[ext]) pickerTypes.push(typeMap[ext]);

      const handle = await window.showSaveFilePicker({
        suggestedName: fileName,
        types: pickerTypes.length ? pickerTypes : undefined
      });
      const writable = await handle.createWritable();
      await writable.write(blob);
      await writable.close();
      return true;
    }
  } catch (err) {
    // User cancelled or API not available; fall back to download below
  }

  // Fallback: browser download
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName || "export";
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  return false;
}


// --- WebView2 helper: allow host to override the *next* download suggested name
function setNextDownloadName(fileName){
  try{
    if(!fileName) return;
    if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
      window.chrome.webview.postMessage({ type: 'nextDownloadName', fileName: String(fileName) });
    }
  }catch(e){}
}

// Save a jsPDF document with a consistent filename across WebView2 and browsers.
async function savePdfDoc(doc, fileName){
  const fn = (fileName && String(fileName).trim()) ? String(fileName).trim() : 'export.pdf';
  try{
    if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
      const blob = doc.output('blob');
      await saveBlobAs(blob, fn, 'application/pdf');
      return;
    }
  }catch(e){ }
  try{
    if(typeof doc.save === 'function'){
      doc.save(fn);
      return;
    }
  }catch(e){ }
  try{ setNextDownloadName(fn); }catch(_){ }
  try{ if(typeof doc.save === 'function') doc.save(fn); }catch(_){ }
}

// ---------------------------------------------------------------------------
// Compat PDF (modules m03_pdf_reports)
// ---------------------------------------------------------------------------
// Les modules "reports" utilisent une API plus ancienne: createPdfDoc, setupPdfDocFonts_, savePdfFromDoc.
// On fournit ici des alias robustes pour éviter les régressions.

function createPdfDoc(opts){
  const orientation = (opts && opts.orientation) ? opts.orientation : 'p';
  // jsPDF est fourni via CDN ou bundle local suivant l'environnement
  const JsPDF = (window.jspdf && window.jspdf.jsPDF) ? window.jspdf.jsPDF : (window.jsPDF || null);
  if(!JsPDF) throw new Error('jsPDF introuvable (CDN/bundle manquant)');
  const doc = new JsPDF({ orientation, unit: 'mm', format: 'a4', compress: true });
  try{ setupPdfDocFonts_(doc); }catch(_){ }
  return doc;
}

function setupPdfDocFonts_(doc){
  // Défaut safe: Helvetica, taille 10
  try{ doc.setFont('helvetica', 'normal'); }catch(_){ }
  try{ doc.setFontSize(10); }catch(_){ }
}

async function savePdfFromDoc(doc, filename){
  return savePdfDoc(doc, filename);
}

function esc(s){ return String(s ?? "").replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }
function norm(s){ return (s ?? "").toString().normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase(); }
function includesNorm(line, needle){ return norm(line).includes(norm(needle)); }
function startsWithNorm(line, needle){ return norm(line).startsWith(norm(needle)); }

function numOrNull(s){
  if(s==null) return null;
  const v = Number(String(s).replace(",", "."));
  return Number.isFinite(v) ? v : null;
}
function fmt(n, d=3){
  if(n==null || !Number.isFinite(n)) return "";
  return n.toFixed(d);
}
function formatDateJJMMAAAA(value){
  if(!value) return "";
  const m = String(value).match(/^(\d{4})-(\d{2})-(\d{2})$/);
  return m ? `${m[3]}.${m[2]}.${m[1]}` : value;
}

async function readTextWithFallback(file){
  const buf = await file.arrayBuffer();
  const utf8 = new TextDecoder("utf-8", { fatal:false }).decode(buf);
  const badCount = (utf8.match(/\uFFFD/g) || []).length;
  if(badCount <= 2) return utf8;
  return new TextDecoder("windows-1252", { fatal:false }).decode(buf);
}

function loadAndCompressLogo(file){
  return new Promise((resolve) => {
    const img = new Image();
    const reader = new FileReader();
    reader.onload = () => { img.src = String(reader.result || ""); };
    reader.readAsDataURL(file);
    img.onload = () => {
      const MAX_W = 360;
      const scale = Math.min(1, MAX_W / img.width);
      const w = Math.round(img.width * scale);
      const h = Math.round(img.height * scale);
      const canvas = document.createElement("canvas");
      canvas.width = w; canvas.height = h;
      canvas.getContext("2d").drawImage(img, 0, 0, w, h);
      resolve({ dataUrl: canvas.toDataURL("image/jpeg", 0.55), type:"JPEG" });
    };
    img.onerror = () => resolve({ dataUrl:null, type:"JPEG" });
  });
}

function setTitleFont(doc){
  doc.setFont(pdfFonts.title.family, pdfFonts.title.style);
}
function setBodyFont(doc, bold=false){
  if(bold) doc.setFont(pdfFonts.bodyBold.family, pdfFonts.bodyBold.style);
  else doc.setFont(pdfFonts.body.family, pdfFonts.body.style);
}

function rf(){
  const getVal = (id, def="") => {
    const el = document.getElementById(id);
    const v = (el && ("value" in el)) ? (el.value ?? "") : def;
    return String(v);
  };
  const getTrim = (id, def="") => getVal(id, def).trim();

  const dwg = getTrim("repDwg");
  // Champ caché (compat) utilisé comme "texte de référence" côté WinForms.
  const txtRef = getTrim("repLandXml", "");

  // Intervenant :
  // - priorité à la saisie manuelle (metaIntervenant)
  // - sinon fallback sur l'opérateur détecté LandXML (meta.operator/creator)
  const intervenantManual = getTrim("metaIntervenant", "");
  const operatorAuto = String((lastData && lastData.meta && (lastData.meta.operator||lastData.meta.creator||lastData.meta.author)) ? (lastData.meta.operator||lastData.meta.creator||lastData.meta.author) : "").trim();

  // Plan / texte de référence affiché dans les cartouches PDF (cellule "Plan de référence")
  // - si DWG + texte => "DWG — texte"
  // - sinon l'un ou l'autre
  const planRef = (dwg && txtRef) ? `${dwg} — ${txtRef}` : (dwg || txtRef);

  // CHA: l'UI peut contenir "CHA02782". On stocke/retourne uniquement la partie numérique.
  const normalizeCha = (v)=>{
    const s = String(v||"").trim();
    if (!s) return "";
    // Supprime un préfixe "CHA" (casse indifférente) + séparateurs éventuels.
    const t = s.replace(/^\s*CHA\s*[-_:\s]*/i, "");
    // Conserve uniquement chiffres + éventuels caractères (au cas où) mais priorité aux chiffres.
    // Ex: "02782" reste "02782".
    return t.trim();
  };

  return {
    zone: getTrim("repZone"),
    phase: getTrim("repPhase"),
    typeDoc: getTrim("repType"),
    cartoucheZone: getTrim("repCartoucheZone"),
    zoneCartouche: getTrim("repCartoucheZone"),
    indice: getTrim("repIndice"),
    siteAddress: getTrim("repSiteAddress"),
    siteContact: getTrim("repSiteContact"),
    client: getTrim("repClient"),
    // Champs chantier (utilisés par PdfSharp header-right box)
    // On lit d'abord des champs dédiés si présents dans l'UI, sinon on retombe sur les champs existants.
    ville: (()=>{ const v = getTrim("repVille","") || getTrim("repZone",""); return v; })(),
    adresseChantier: (()=>{ const v = getTrim("repAdresseChantier","") || getTrim("repAdresse","") || getTrim("repSiteAddress",""); return v; })(),
    // compat (certaines versions utilisaient "adresse")
    adresse: (()=>{ const v = getTrim("repAdresse","") || getTrim("repAdresseChantier","") || getTrim("repSiteAddress",""); return v; })(),
    cha: normalizeCha(getTrim("repCHA")),
    date: formatDateJJMMAAAA(getVal("repDate", "")),
    elements: getTrim("repElements"),
    axes: getTrim("repAxes"),
    dwg,
    // Compat "texte de référence" (hidden input) : peut être alimenté par WinForms
    txt: txtRef,
    planRef,
    obs: getTrim("repObs"),
    surveyor: getTrim("repSurveyor"),
    planType: getTrim("repPlanType"),
    typePlan: getTrim("repPlanType"),
    // Appareil (type + n° série) : récupéré depuis les données importées (AppLog/TXT)
    instrument: String((lastData && lastData.meta && lastData.meta.instrument) ? lastData.meta.instrument : "").trim(),
    serial: String((lastData && lastData.meta && lastData.meta.serial) ? lastData.meta.serial : "").trim()
      ,coordSystem: (()=>{
      const v = getTrim("metaCoordSys","-----");
      return (v && v !== "-----") ? v : "";
    })()
    ,altimetricSystem: (()=>{
      const v = getTrim("metaAltSys","-----");
      return (v && v !== "-----") ? v : "";
    })()
    ,ppm: (()=>{
      const raw = getVal("metaPPM","").trim();
      return raw;
    })()
    ,operator: operatorAuto
    ,intervenant: intervenantManual || operatorAuto
  };
}





// ------------------------------------------------------------
// Projet .nova — snapshot UI (sans embarquer le contenu LandXML/TXT)
// ------------------------------------------------------------
window.__NF_PROJECT_FILES = window.__NF_PROJECT_FILES || {
  landxmlPath: "",
  pieuxTxtPath: ""
};

window.NOVA_getState = function(){
  const gv = (id, def="") => {
    try{
      const el = document.getElementById(id);
      if(!el) return def;
      if(el.type === 'checkbox') return !!el.checked;
      return ("value" in el) ? String(el.value ?? def) : def;
    }catch(_){ return def; }
  };

  const activeModule = (()=>{
    try{
      const el = document.querySelector('.nf-module.active');
      return el ? String(el.id || '') : '';
    }catch(_){ return ''; }
  })();

  return {
    schema: 'nova-fiches-project',
    version: String(window.APP_BUILD || window.__NF_BUILD || 'DEV'),
    savedAt: new Date().toISOString(),
    infosDossier: {
      repLandXml: gv('repLandXml',''),
      repLandXmlFile: gv('repLandXmlFile',''),
      repElements: gv('repElements',''),
      repPlanType: gv('repPlanType',''),
      repZone: gv('repZone',''),
      repSiteAddress: gv('repSiteAddress',''),
      repSiteContact: gv('repSiteContact',''),
      repCHA: gv('repCHA',''),
      repDate: gv('repDate',''),
      repClient: gv('repClient',''),
      repPhase: gv('repPhase',''),
      repType: gv('repType',''),
      repCartoucheZone: gv('repCartoucheZone',''),
      repIndice: gv('repIndice',''),
      repDwg: gv('repDwg',''),
      metaCoordSys: gv('metaCoordSys',''),
      metaAltSys: gv('metaAltSys',''),
      metaPPM: gv('metaPPM',''),
      metaIntervenant: gv('metaIntervenant',''),
      repObs: gv('repObs',''),
      repSurveyor: gv('repSurveyor','')
    },
    projet: {
      landxmlLoaded: !!(window.lastData || window.__NF_LASTDATA),
      landxmlLabel: gv('repLandXmlFile','')
    },
    tolerances: {
      optTol: !!gv('optTol', false),
      tolXYOn: !!gv('tolXYOn', false),
      tolZOn: !!gv('tolZOn', false),
      tolXY: gv('tolXY',''),
      tolZ: gv('tolZ','')
    },
    fichiers: {
      landxmlPath: String(window.__NF_PROJECT_FILES?.landxmlPath || ''),
      pieuxTxtPath: String(window.__NF_PROJECT_FILES?.pieuxTxtPath || '')
    },
    ui: {
      activeModule,
      pdfGroupByZone: !!gv('pdfGroupByZone', false),
      zoneLabels: (function(){
        try{
          if(window.NF_getZoneRenameMap) return window.NF_getZoneRenameMap();
        }catch(_){ }
        return {};
      })(),
      zonePointCodes: (function(){
        try{
          if(window.NF_getZonePointCodeMap) return window.NF_getZonePointCodeMap();
        }catch(_){ }
        return {};
      })(),
      globalZoneCode: (function(){
        try{
          if(window.NF_getGlobalZoneCode) return window.NF_getGlobalZoneCode();
        }catch(_){ }
        return '';
      })()
    }
  };
};

window.NOVA_setState = function(data){
  if(!data || typeof data !== 'object') return false;

  const sv = (id, value, isCheckbox=false) => {
    try{
      const el = document.getElementById(id);
      if(!el) return;
      if(isCheckbox){
        el.checked = !!value;
      }else if(value !== undefined && value !== null){
        el.value = String(value);
      }else{
        el.value = '';
      }
      try{ el.dispatchEvent(new Event('input', { bubbles:true })); }catch(_){ }
      try{ el.dispatchEvent(new Event('change', { bubbles:true })); }catch(_){ }
    }catch(_){ }
  };

  const d = data.infosDossier || {};
  const t = data.tolerances || {};
  const f = data.fichiers || {};
  const ui = data.ui || {};

  sv('repLandXml', d.repLandXml || '');
  sv('repLandXmlFile', d.repLandXmlFile || '');
  sv('repElements', d.repElements || '');
  sv('repPlanType', d.repPlanType || d.planType || d.typePlan || '');
  sv('repZone', d.repZone || d.repCode || '');
  sv('repSiteAddress', d.repSiteAddress || '');
  sv('repSiteContact', d.repSiteContact || '');
  sv('repCHA', d.repCHA || '');
  sv('repDate', d.repDate || '');
  sv('repClient', d.repClient || '');
  sv('repPhase', d.repPhase || '');
  sv('repType', d.repType || '');
  sv('repCartoucheZone', d.repCartoucheZone || d.cartoucheZone || d.zoneCartouche || '');
  sv('repIndice', d.repIndice || '');
  sv('repDwg', d.repDwg || '');
  sv('metaCoordSys', d.metaCoordSys || '');
  sv('metaAltSys', d.metaAltSys || '');
  sv('metaPPM', d.metaPPM || '');
  sv('metaIntervenant', d.metaIntervenant || '');
  sv('repObs', d.repObs || '');
  sv('repSurveyor', d.repSurveyor || '');

  sv('optTol', !!t.optTol, true);
  sv('tolXYOn', !!t.tolXYOn, true);
  sv('tolZOn', !!t.tolZOn, true);
  sv('tolXY', t.tolXY || '');
  sv('tolZ', t.tolZ || '');

  sv('pdfGroupByZone', !!ui.pdfGroupByZone, true);
  try{
    if(window.NF_setZoneRenameMap) window.NF_setZoneRenameMap(ui.zoneLabels || {});
    if(window.NF_setZonePointCodeMap) window.NF_setZonePointCodeMap(ui.zonePointCodes || {});
    if(window.NF_setGlobalZoneCode) window.NF_setGlobalZoneCode(ui.globalZoneCode || '');
  }catch(_){ }

  window.__NF_PROJECT_FILES = {
    landxmlPath: String(f.landxmlPath || ''),
    pieuxTxtPath: String(f.pieuxTxtPath || '')
  };

  try{
    const projLand = document.getElementById('projLandXml');
    if(projLand && d.repLandXmlFile){
      projLand.textContent = 'LandXML : référence projet';
      projLand.className = 'pill warn';
    }
  }catch(_){ }

  if(ui.activeModule){
    try{
      const btn = document.querySelector(`.nf-nav-item[data-target="${ui.activeModule}"]`);
      btn && btn.click();
    }catch(_){ }
  }

  try{ window.updateProjectBanner && window.updateProjectBanner(); }catch(_){ }
  try{ window.APP && window.APP.refresh && window.APP.refresh(); }catch(_){ }
  return true;
};
// ===== PDF filename helper =====
// Format requis: NOVA_(VILLE 4 CAR MAX)_PHASE_TYPE_INDICE  (MAJUSCULE)
// On ajoute un suffixe par type de PDF pour éviter l'écrasement.
function pdfFileBase(R){
  try{
    const rawVille = (R.code || R.ville || "").toString();
    const ville = rawVille
      .normalize("NFD").replace(/[\u0300-\u036f]/g, "")
      .toUpperCase()
      .replace(/[^A-Z0-9]/g, "")
      .slice(0,4) || "XXXX";

    const phase = (R.phase || "").toString().toUpperCase().replace(/[^A-Z0-9]/g, "") || "NA";
    const type  = (R.typeDoc || R.type || "").toString().toUpperCase().replace(/[^A-Z0-9]/g, "") || "NA";
    const indice= (R.indice || "").toString().toUpperCase().replace(/[^A-Z0-9]/g, "") || "NA";

    return `NOVA_${ville}_${phase}_${type}_${indice}`;
  }catch(e){
    return "NOVA_XXXX_NA_NA_NA";
  }
}


// ===== Options (single source of truth for UI toggles) =====
function getOptions(){
  const optTol = document.getElementById("optTol");
  const tolXYOn = document.getElementById("tolXYOn");
  const tolZOn = document.getElementById("tolZOn");
  const tolXY = document.getElementById("tolXY");
  const tolZ  = document.getElementById("tolZ");
  const calcDzOn = document.getElementById("calcDzOn");

  const tolOn = optTol ? !!optTol.checked : false;
  const xyOn  = tolXYOn ? !!tolXYOn.checked : true;
  const zOn   = tolZOn ? !!tolZOn.checked : true;
  const tXY   = tolXY ? Number(tolXY.value) : NaN;
  const tZ    = tolZ  ? Number(tolZ.value)  : NaN;
  const calcDz = calcDzOn ? !!calcDzOn.checked : true;

  return { tolOn, xyOn, zOn: (calcDz ? zOn : false), tXY, tZ, calcDz };
}

function statusFromTol(dx, dy, dz, opts){
  const O = opts || getOptions();
  if(!O.tolOn) return "";

  const checks = [];
  const hasXY = dx!=null && dy!=null && Number.isFinite(Number(dx)) && Number.isFinite(Number(dy)) && Number.isFinite(Number(O.tXY));
  const hasZ = dz!=null && Number.isFinite(Number(dz)) && Number.isFinite(Number(O.tZ));

  // Une fiche peut etre exploitable uniquement en XY si le Z theorique est absent.
  if(O.xyOn && hasXY) checks.push(Math.abs(Number(dx)) <= Number(O.tXY) && Math.abs(Number(dy)) <= Number(O.tXY));
  if(O.zOn && hasZ) checks.push(Math.abs(Number(dz)) <= Number(O.tZ));

  if(!checks.length) return "";

  return checks.every(Boolean) ? "VALIDE" : "REFUSÉ";
}



function refreshTolWarnings(){
  try{
    const warn = document.getElementById("tolWarn");
    if(!warn) return;
    const O = getOptions();
    const bad = (O.xyOn && !(Number.isFinite(O.tXY) && O.tXY>0)) || (O.zOn && !(Number.isFinite(O.tZ) && O.tZ>0));
    warn.style.display = bad ? "inline-block" : "none";
  }catch(e){}
}

function shouldCalcDz(){ return getOptions().calcDz; }

function syncDzUI(){
  try{
    const O = getOptions();
    const cols = document.querySelectorAll('[data-col="dz"]');
    cols.forEach(el => { el.style.display = O.calcDz ? "" : "none"; });
    const zt = document.getElementById("tolZRow");
    if(zt) zt.style.display = O.calcDz ? "" : "none";
  }catch(e){}
}

function applyDzPolicy(data){
  if(!data) return;
  const calcOn = shouldCalcDz();
  if(calcOn) return;

  // Impl. : forcer dz à null
  try{ (data.implantation?.points || []).forEach(p=>{ if(p?.d) p.d.dz = null; }); }catch(e){}

  // Ligne de réf : supprimer H calc + dz
  try{
    (data.ligneRef||[]).forEach(lr=>{
      (lr.rabPoints||[]).forEach(rp=>{
        if(rp?.calc) rp.calc.H = null;
        if(rp?.d) rp.d.dz = null;
      });
    });
  }catch(e){}
}

function computeTolStats(points){
  // Simple compteur (utile UI). Ne calcule pas les écarts-type.
  let total=0, ok=0, ko=0;
  for(const p of (points||[])){
    if(!p || !p.d) continue;
    total++;
    const st = statusFromTol(p.d.dx, p.d.dy, p.d.dz);
    if(st === "VALIDE") ok++;
    else if(st === "REFUSÉ") ko++;
  }
  return { total, ok, ko };
}

function computeStdDev(values){
  const vals = (values||[]).filter(v => (v!=null) && Number.isFinite(v));
  const n = vals.length;
  if(n < 2) return null;
  const mean = vals.reduce((a,b)=>a+b,0)/n;
  const varPop = vals.reduce((a,b)=>a+Math.pow(b-mean,2),0)/n;
  return Math.sqrt(varPop);
}

// Contrôles: compte tous les points ayant un ID, dans tolérance / hors tolérance (si tol activé),
// + écart-type (population) sur Dx/Dy/Dz. Si pas d'info => champ vide.
function computeControlStats(points, opts){
  const O = opts || getOptions();

  let total = 0, ok = 0, ko = 0;
  const dxs = [], dys = [], dzs = [];

  const toNum = (v) => {
    if(v==null) return null;
    if(typeof v === "number") return Number.isFinite(v) ? v : null;
    const n = Number(String(v).replace(",", "."));
    return Number.isFinite(n) ? n : null;
  };

  for(const p of (points||[])){
    if(!p) continue;
    const id = (p?.id ?? p?.ID ?? p?.Id ?? "");
    if(!String(id).trim()) continue;
    total++;

    const d = p?.d || p?.delta || {};
    const dx = toNum(d.dx ?? d.dX ?? d.Dx);
    const dy = toNum(d.dy ?? d.dY ?? d.Dy);
    const dz = toNum(d.dz ?? d.dZ ?? d.Dz);

    if(dx!=null) dxs.push(dx);
    if(dy!=null) dys.push(dy);
    if(dz!=null) dzs.push(dz);

    if(O.tolOn){
      const st = statusFromTol(dx, dy, dz, O);
      if(st === "VALIDE") ok++;
      else if(st === "REFUSÉ") ko++;
    }
  }

  return {
    total,
    ok: O.tolOn ? ok : null,
    ko: O.tolOn ? ko : null,
    ne: O.tolOn ? (total - ok - ko) : null,
    sdDx: computeStdDev(dxs),
    sdDy: computeStdDev(dys),
    sdDz: computeStdDev(dzs),
  };
}

window.computeControlStats = computeControlStats;

/* =========================
   TXT parsing (Leica System 1200)
========================= */
function emptyData(){
  return {
    meta: { instrument:null, serial:null, job:null },
    stationLibre: {
      observations: [], // {id,hz,vz,dp,hr,constPrisme}
      residuals: [],    // {id,dHz,dAlti,dDH,used}
      results: { idStation:null, E:null, N:null, H:null, Hi:null, corrOrient:null, azOrient:null, devE:null, devN:null, devH:null, devOri:null }
    },
    stationLibreRuns: [], // snapshots of each setup (AppLog order)
    heightTransfers: [],          // Leica Captivate heightTransfer setups
    topoStations: [],             // LandXML topo survey setups
    implantation: { points: [] }, // {id, theo:{E,N,H}, mes:{E,N,H}, d:{dx,dy,dz}}
    ligneRef: [],                 // {start,end, lineId, rabPoints:[{id, mes:{E,N,H}, ec:{dL,dT,dA}, calc:{E,N,H}, d:{dx,dy,dz}}]}
    rawText: ""
  };
}


// ========================= [B] IMPORT TXT + PARSING =============================

function syncIntervenantUI(data){
  const el = document.getElementById('metaIntervenant');
  if(!el) return;
  const auto = String((data && data.meta && (data.meta.operator||data.meta.creator||data.meta.author)) || '').trim();
  const current = String(el.value||'').trim();
  const prevMode = el.dataset.mode || '';

  // Priorité à la saisie manuelle. On ne force jamais le champ en lecture seule.
  if(auto){
    if(!current || prevMode === 'auto'){
      el.value = auto;
      el.dataset.mode = 'auto';
    }
    el.readOnly = false;
    el.placeholder = '(auto ou manuel)';
  }else{
    if(prevMode === 'auto' && !current) el.value = '';
    el.readOnly = false;
    if(!current) el.dataset.mode = 'manual';
    el.placeholder = '(à renseigner)';
  }

  if(!el.__nfManualHook){
    el.addEventListener('input', () => {
      el.dataset.mode = 'manual';
    });
    el.__nfManualHook = '1';
  }
}

async function exportTxtXYZC(autoTriggered=false){
  try{
    const data = (window.__NF_LASTDATA || window.lastData || (typeof lastData!=='undefined'?lastData:null));
    if(!data) throw new Error('Aucune donnée (import LandXML requis)');
    const cg = data.cgPoints || {};
    const names = Object.keys(cg||{}).sort((a,b)=>a.localeCompare(b, 'fr', {numeric:true, sensitivity:'base'}));
    const sep = '\t';
    const header = ['N','X','Y','Z','C'].join(sep);
    const lines = [header];
    for(const name of names){
      const p = cg[name];
      if(!p) continue;
      const X = (p.E!=null && Number.isFinite(p.E)) ? p.E.toFixed(3) : '';
      const Y = (p.N!=null && Number.isFinite(p.N)) ? p.N.toFixed(3) : '';
      const Z = (p.H!=null && Number.isFinite(p.H)) ? p.H.toFixed(3) : '';
      const C = ''; // code non disponible de façon fiable dans les LandXML Leica fournis
      lines.push([String(name), X, Y, Z, C].join(sep));
    }
    const content = lines.join('\r\n');
    const blob = new Blob([content], {type:'text/plain;charset=utf-8'});

    const baseName = String((window.nfExchange && window.nfExchange.landxml && window.nfExchange.landxml.fileName) || data.fileName || 'export').replace(/\.[^.]+$/,'');
    const fn = baseName + '_XYZC.txt';

    try{ setNextDownloadName(fn); }catch(_){ }
    await saveBlobAs(blob, fn, 'text/plain');

    // UI status (optional)
    try{
      const el = document.getElementById('exTxtStatus');
      if(el) el.textContent = 'Export TXT : OK';
    }catch(_){}
    return true;
  }catch(e){
    try{ console.error(e); }catch(_){}
    setStatus('Export TXT : ' + (e && e.message ? e.message : String(e)), true);
    return false;
  }
}


// Host -> WebView messages (WinForms menu actions)
try{
  window.chrome?.webview?.addEventListener('message', async (ev)=>{
    try{
      const msg = ev?.data || {};
      if(!msg || typeof msg !== 'object') return;
      if(msg.type === 'exportTxtXYZC'){
        await exportTxtXYZC(false);
      }
    }catch(e){ console.error(e); }
  });
}catch(_){}

