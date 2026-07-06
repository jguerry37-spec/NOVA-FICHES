// applyDzPolicyClone_ — utilitaire : clone profond + application de la politique Dz
// (évite de modifier les données affichées à l'écran lors des exports PDF)
function applyDzPolicyClone_(data, opts){
  // Clone profond
  let copy;
  try {
    copy = (typeof structuredClone === "function") ? structuredClone(data) : JSON.parse(JSON.stringify(data));
  } catch {
    copy = JSON.parse(JSON.stringify(data));
  }

  const calcDz = (opts && typeof opts.calcDz === "boolean")
    ? opts.calcDz
    : (document.getElementById('calcDzOn')?.checked ?? true);

  if (!calcDz) {
    // même logique que applyDzPolicy() (m01_core.js), mais sans dépendre de l'état UI
    if (copy && copy.residuals) {
      if (Array.isArray(copy.residuals.implantation)) {
        copy.residuals.implantation.forEach(r => { delete r.resi_dz; delete r.dz; });
      }
      if (Array.isArray(copy.residuals.refLine)) {
        copy.residuals.refLine.forEach(r => { delete r.resi_dz; delete r.dz; });
      }
      if (Array.isArray(copy.residuals.station)) {
        copy.residuals.station.forEach(s => {
          if (Array.isArray(s.points)) s.points.forEach(p => { delete p.resi_dz; delete p.dz; });
        });
      }
    }
    // autres champs potentiels
    if (copy && Array.isArray(copy.implantation)) copy.implantation.forEach(r => { delete r.dz; });
    if (copy && Array.isArray(copy.refLine)) copy.refLine.forEach(r => { delete r.dz; });
  }

  // Compat: certaines fonctions utilisent data.stationLibre.results
  // (ancien format). Si seul stationLibreRuns est present, on recopie la 1ere run.
  if (!copy.stationLibre && Array.isArray(copy.stationLibreRuns) && copy.stationLibreRuns.length > 0) {
    copy.stationLibre = copy.stationLibreRuns[0];
  }

  return copy;
}


function getAutoIntervenant(R){
  try{
    const ui = String((R && (R.intervenant || R.operateur || R.opérateur)) ? (R.intervenant || R.operateur || R.opérateur) : "").trim();
    if(ui) return ui;
  }catch(_){ }
  try{
    const m = (typeof lastData !== "undefined" && lastData) ? (lastData.meta || {}) : {};
    const cands = [m.operator, m.operateur, m.creator, m.Creator, m.surveyCreator, m.SurveyCreator];
    for(const c of cands){
      const cc = String(c||"").trim();
      if(cc) return cc;
    }
  }catch(_){ }
  try{
    const raw = String((typeof lastData !== "undefined" && lastData) ? (lastData.rawText || lastData.rawLandXml || lastData.raw || lastData.landxmlRaw || "") : "");
    if(!raw) return "";
    const m = raw.match(/<[^>]*\bSurvey\b[^>]*\bCreator\s*=\s*"([^"]+)"/i) || raw.match(/\bCreator\s*=\s*"([^"]+)"/i);
    return (m && m[1]) ? String(m[1]).trim() : "";
  }catch(_){ }
  return "";
}


// ==== [NF] Export naming (PDF/Excel) ====
// Rule (validated by NOVATLAS):
//   NOVA_VILL_PHASE_TYPE_INDICE_yyyy.MM.dd.ext
// Notes:
// - VILL: first 4 letters of info.Ville (accents removed), uppercased
// - TYPE: comes from the "info dossier" block (info.Type), NOT inferred from the button
// - Missing fields fallback to "NA"
// - Windows-invalid filename chars are removed
if (typeof window.buildExportFileName !== "function") {
  window.buildExportFileName = function buildExportFileName(_ignoredReport, ext){
    const R = (typeof rf === "function") ? (rf() || {}) : {};

    const pick = (...keys) => {
      for(const k of keys){
        const v = (R && R[k] != null) ? String(R[k]).trim() : "";
        if(v) return v;
      }
      return "";
    };

    const sanitize = (s) => {
      s = String(s || "").trim();
      if(!s) return "";
      try{ s = s.normalize("NFD").replace(/[\u0300-\u036f]/g, ""); }catch(_){ }
      // keep alnum + underscore, convert spaces to nothing (separators are our underscores)
      s = s.replace(/[^0-9a-zA-Z_\-\s]/g, "");
      s = s.replace(/[\s\-]+/g, "");
      return s;
    };

    const villeRaw = pick("ville", "Ville", "city", "City");
    const phaseRaw = pick("phase", "Phase");
    const typeRaw  = pick("typeDoc","type","TypeDoc","Type","repType");
    const indiceRaw = pick("indice", "Indice", "index", "Index");

    let vill = sanitize(villeRaw).toUpperCase();
    vill = vill ? vill.substring(0, 4) : "NA";

    let phase = sanitize(phaseRaw).toUpperCase();
    if(!phase) phase = "NA";

    let type = sanitize(typeRaw).toUpperCase();
    if(!type) type = "NA";

    let indice = sanitize(indiceRaw).toUpperCase();
    if(!indice) indice = "NA";

    const d = new Date();
    const yyyy = String(d.getFullYear()).padStart(4, "0");
    const mm = String(d.getMonth()+1).padStart(2, "0");
    const dd = String(d.getDate()).padStart(2, "0");
    const date = `${yyyy}.${mm}.${dd}`;

    let e = String(ext || "PDF").trim().replace(/^\./, "");
    if(!e) e = "PDF";
    e = e.toLowerCase();

    return `NOVA_${vill}_${phase}_${type}_${indice}_${date}.${e}`;
  };
}




// ==== [NF] Footer safe zone: prevent content from colliding with page footer (autoTable)
// Applies a bottom margin to all autoTable calls on this doc instance.
function patchAutoTableFooterSafe(doc, R){
  try{
    if(!doc || typeof doc.autoTable !== "function") return;
    if(doc.__nf_autoTableFooterSafe) return;
    const footerSafe = (R && (R.footerSafe||R.footerSafeMargin)) ? (R.footerSafe||R.footerSafeMargin) : 28; // mm
    const orig = doc.autoTable;
    doc.autoTable = function(opts){
      try{
        opts = opts || {};
        // Normalize margin option
        if(typeof opts.margin === "number"){
          opts.margin = { top: opts.margin, left: opts.margin, right: opts.margin, bottom: footerSafe };
        }else{
          opts.margin = opts.margin || {};
        }
        const b = (opts.margin && typeof opts.margin.bottom === "number") ? opts.margin.bottom : 0;
        opts.margin.bottom = Math.max(b, footerSafe);
      }catch(e){}
      return orig.call(this, opts);
    };
    doc.__nf_autoTableFooterSafe = true;
    // Also protect the header area (logo on pages 2..N). Safe by default even if the logo isn't stamped yet.
    try{ patchAutoTableHeaderSafe(doc, doc.__nf_headerSafeTopMm || 17); }catch(e){}
  }catch(e){}
}

// ==== [NF] Header safe zone: prevent content from colliding with page header/logo (autoTable)
// Applies a top margin + startY floor to all autoTable calls on this doc instance.
function patchAutoTableHeaderSafe(doc, headerTopMm){
  try{
    if(!doc || typeof doc.autoTable !== "function") return;
    if(!headerTopMm || headerTopMm <= 0) return;

    // Chainable patch: keep a pointer to the original or already-patched autoTable.
    if(!doc.__nf_autoTableHeaderSafe){
      const orig = doc.autoTable;
      doc.autoTable = function(opts){
        try{
          opts = opts || {};
          opts.margin = opts.margin || {};
          // Keep the user's top margin, but never go above the safe header.
          const t = Number(opts.margin.top ?? 0) || 0;
          opts.margin.top = Math.max(t, headerTopMm);
          // Also floor startY for first-page tables that don't pass it.
          if(opts.startY == null){
            // no-op
          } else {
            const sy = Number(opts.startY) || 0;
            opts.startY = Math.max(sy, headerTopMm);
          }
        }catch(e){}
        return orig.call(this, opts);
      };
      doc.__nf_autoTableHeaderSafe = { headerTopMm };
    } else {
      // Update the threshold if we discovered a taller header later (bigger logo).
      doc.__nf_autoTableHeaderSafe.headerTopMm = Math.max(doc.__nf_autoTableHeaderSafe.headerTopMm || 0, headerTopMm);
    }
  }catch(e){}
}



// ==== [NF] Logo on all pages (except first) ====
// Uses the same logoDataUrl as the first page. Adds a small logo on pages 2..N without any frame.
function addLogoOnOtherPages(doc){
  try{
    if(!doc || typeof doc.getNumberOfPages !== "function") return;
    if(typeof logoDataUrl !== "string" || !logoDataUrl.startsWith("data:image")) return;
    const n = doc.getNumberOfPages();
    if(n <= 1) return;
    const imgW = 24; // 50% of the first page (48)
    const imgH = 7;  // 50% of the first page (14)
    const x = 10 + (190 - imgW); // right aligned inside margins
    const y = 8;
    for(let p=2; p<=n; p++){
      try{
        doc.setPage(p);
        doc.addImage(logoDataUrl, "PNG", x, y, imgW, imgH, undefined, "FAST");
      }catch(e){}
    }
  }catch(e){}
}

// ==== [NF] Ensure logo is stamped on pages 2..N (AutoTable can create pages after header is drawn) ====
// We call this right before savePdfDoc() for every report.
function nfStampLogoHeaders(doc){
  try{
    // logoDataUrl is the same variable used by drawHeaderV2 (page 1).
    if(typeof logoDataUrl !== "string" || !logoDataUrl.startsWith("data:image")) return;
    const n = (typeof doc.getNumberOfPages === "function") ? doc.getNumberOfPages()
            : (doc.internal && typeof doc.internal.getNumberOfPages === "function") ? doc.internal.getNumberOfPages()
            : 1;
    if(!n || n <= 1) return;

    const pageW = (doc.internal && doc.internal.pageSize && doc.internal.pageSize.getWidth)
      ? doc.internal.pageSize.getWidth()
      : 210;

    // 50% of the first page logo (48x14)
    const imgW = 24;
    const imgH = 7;
    const marginX = 10;
    const x = pageW - marginX - imgW;
    const y = 8;
    // Define a header no-overlap code (used by autoTable on pages 2..N).
    // Keep a small padding under the logo.
    try{ doc.__nf_headerSafeTopMm = Math.max(doc.__nf_headerSafeTopMm || 0, y + imgH + 2); }catch(e){}

    for(let p=2; p<=n; p++){
      try{ doc.setPage(p); }catch(e){}
      try{ doc.addImage(logoDataUrl, "PNG", x, y, imgW, imgH, undefined, "FAST"); }catch(e){}
    }
  }catch(e){}
}

async function buildPdfIntervention(data){
  syncDzUI();
  data = applyDzPolicyClone_(data, getOptions());
  const jsPDF = window.jspdf?.jsPDF;
  if(!jsPDF){ setStatus('Erreur PDF: jsPDF non chargé (CDN bloqué ?)', true); throw new Error('jsPDF non chargé (CDN bloqué ?)'); }
  const doc = new jsPDF({ unit:"mm", format:"a4", compress:true });
  try{ if(typeof nfPostBeginPdf==="function") nfPostBeginPdf(doc); }catch(e){};
  const R = rf();
  const O = getOptions();

  patchAutoTableFooterSafe(doc, R);
  let y = drawHeaderV2(doc, R);
  // ===== MULTI-STATIONS (ordre AppLog) =====
  // L'AppLog peut contenir plusieurs mises en station. Tous les rapports PDF doivent
  // exporter les stations dans l'ordre du fichier.
  const runs = (data.stationLibreRuns && data.stationLibreRuns.length)
    ? data.stationLibreRuns
    : (data.stationLibre ? [data.stationLibre] : []);

  const allImpPts = Array.isArray(data?.implantation?.points) ? data.implantation.points : [];

  if(!runs.length){
    // Fallback: aucune station détectée -> on conserve un rendu minimal.
    setBodyFont(doc);
    doc.setFontSize(10);
    doc.text("Aucune station trouvée dans l'AppLog.", 10, y+8);
    y += 14;
    y = pdfBar(doc, y, "IMPLANTATION");
  }
  // TOL bar (identique pour chaque station)
  const tolOn = document.getElementById("optTol").checked;
  const tXY = Number(document.getElementById("tolXY").value);
  const tZ = Number(document.getElementById("tolZ").value);
  const xyOn = document.getElementById("tolXYOn").checked;
  const zOn = document.getElementById("tolZOn").checked;

  const tolTxt = tolOn
    ? `Tolérances : X=${xyOn ? tXY : "—"} ; Y=${xyOn ? tXY : "—"} ; Z=${zOn ? tZ : "—"}`
    : `Tolérances désactivées`;

  const renderImplantTable = (pts) => {
    const body = (pts || []).map(p=>[
      p.id ?? "",
      fmt(p.theo?.E), fmt(p.theo?.N), fmt(p.theo?.H),
      fmt(p.mes?.E), fmt(p.mes?.N), fmt(p.mes?.H),
      fmt(p.d?.dx), fmt(p.d?.dy), fmt(p.d?.dz),
      statusFromTol(p.d?.dx, p.d?.dy, p.d?.dz, O)
    ]);

    if(!body.length){
      setBodyFont(doc);
      doc.setFontSize(9);
      doc.text("Aucun point d'implantation.", 10, y+5);
      y += 9;
      return;
    }

    autoTableResults(doc, {
      startY: y + 1,
      nfThickSpec: "implantation",
      head: [["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"]],
      body,
      margin:{ left:10, right:10 },
      columnStyles: RESULTS_COLUMN_STYLES,
      styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
      headStyles:{ fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
      alternateRowStyles:null,
      didParseCell: function(dataCell){
        applyStatusFitAutoTable(dataCell);
        if(dataCell && dataCell.section === "head"){
          try{ dataCell.cell.styles.fontStyle = "bold"; }catch(e){}
          return;
        }
      },
      // IMPORTANT: no thick lines via autoTable hooks anymore (deterministic post-process)
    });
    y = (doc.lastAutoTable && doc.lastAutoTable.finalY) ? (doc.lastAutoTable.finalY + 6) : (y + 10);
  };

  if(runs.length){
    for(let si=0; si<runs.length; si++){
      const run = runs[si];
      const setupId = run?.results?.idStation || null;

      // Station bloc
      y = pdfStationLibreFullRun(doc, y, run);

      // Implantation bloc
      y = pdfBar(doc, y, "IMPLANTATION");
      y = pdfTolBar(doc, y, tolTxt);

      const pts = allImpPts.filter(p => (p?.stationId || null) === setupId || (p?.stationId == null && si===0));
      renderImplantTable(pts);

      // Séparation entre stations
      if(si < runs.length-1){
        y = ensureSpace(doc, y, 18);
      }
    }
  } else {
    // Pas de station : on sort tous les points
    y = pdfTolBar(doc, y, tolTxt);
    renderImplantTable(allImpPts);
  }

  // Last-page box only (ensure the final page count is known BEFORE stamping pagination)
  y = ensureRoomForLastFooter(doc, 70);
  const stats = computeControlStats(data.implantation.points);
  pdfFooterLastPageBox(doc, R, stats);

  // Address + pagination all pages (ONCE, at the very end to avoid overlaps)
  pdfFooterAllPagesAddress(doc);

  nfStampLogoHeaders(doc);

  try{ if(typeof nfPostDrawThickLines==="function") nfPostDrawThickLines(doc); }catch(e){}
  // Nomination standardisée NOVATLAS (info dossier)
  savePdfDoc(doc, (typeof buildExportFileName === "function") ? buildExportFileName("", "pdf") : `${pdfFileBase(R)}.pdf`);
}

// ===== PDF STATION UNIQUEMENT =====
// Option C : copie du bloc Station libre du rapport complet (sans aucune autre section)
// IMPORTANT : ce PDF n'est JAMAIS injecté dans le rapport complet.
function buildPdfStationOnly(data){
  syncDzUI();
  data = applyDzPolicyClone_(data, getOptions());
  const jsPDF = window.jspdf?.jsPDF;
  if(!jsPDF){ setStatus('Erreur PDF: jsPDF non chargé (CDN bloqué ?)', true); throw new Error('jsPDF non chargé (CDN bloqué ?)'); }
  const doc = new jsPDF({ unit:"mm", format:"a4", compress:true });
  try{ if(typeof nfPostBeginPdf==="function") nfPostBeginPdf(doc); }catch(e){};
  const R = rf();

  let y = drawHeaderV2(doc, R);

  // IMPORTANT: un AppLog peut contenir plusieurs mises en station (L1, L2, ...).
  // Le PDF "Station uniquement" doit exporter TOUTES les stations, dans l'ordre AppLog.
  const runs = (data.stationLibreRuns && data.stationLibreRuns.length)
    ? data.stationLibreRuns
    : (data.stationLibre ? [data.stationLibre] : []);

  if(!runs.length){
    setBodyFont(doc);
    doc.setFontSize(10);
    doc.text("Aucune station trouvée dans l'AppLog.", 10, y+8);
    y += 14;
  } else {
    for(let si=0; si<runs.length; si++){
      const run = runs[si];
      y = pdfStationLibreFullRun(doc, y, run);
      // Séparation légère entre stations (sans ajouter de section au rapport)
      if(si < runs.length-1){
        y += 2;
        y = ensureSpace(doc, y, 18);
      }
    }
  }


  // ===== Bloc final (contrôles / observations / signatures) — identique IMP/LR =====
  // On garde ce bloc même si le report "Station uniquement" n'a pas de points d'implantation/ligne :
  // stats = 0, mais le cartouche de validation + signatures doit exister.
  y = ensureRoomForLastFooter(doc, 95, y);
  const stats = computeControlStats([], getOptions());
  pdfFooterLastPageBox(doc, R, stats);

  // Address + pagination all pages (ONCE)
  pdfFooterAllPagesAddress(doc);

  nfStampLogoHeaders(doc);

  try{ if(typeof nfPostDrawThickLines==="function") nfPostDrawThickLines(doc); }catch(e){}
  savePdfDoc(doc, (typeof buildExportFileName === "function") ? buildExportFileName("", "pdf") : `${pdfFileBase(R)}.pdf`);
}

// ===== PDF STATION + POINTS (TXT / GSI) =====
// Nouveau PDF V2 : reprend EXACTEMENT le look "Station uniquement" + ajoute une table Points.
function buildPdfStationPlusPoints(data, pts, sourceLabel){
  syncDzUI();
  data = applyDzPolicyClone_(data, getOptions());
  const jsPDF = window.jspdf?.jsPDF;
  if(!jsPDF){ setStatus('Erreur PDF: jsPDF non chargé (CDN bloqué ?)', true); throw new Error('jsPDF non chargé (CDN bloqué ?)'); }
  const doc = new jsPDF({ unit:"mm", format:"a4", compress:true });
  try{ if(typeof nfPostBeginPdf==="function") nfPostBeginPdf(doc); }catch(e){};
  const R = rf();

  let y = drawHeaderV2(doc, R);

  const runs = (data.stationLibreRuns && data.stationLibreRuns.length)
    ? data.stationLibreRuns
    : (data.stationLibre ? [data.stationLibre] : []);

  if(!runs.length){
    setBodyFont(doc);
    doc.setFontSize(10);
    doc.text("Aucune station trouvée dans l'AppLog.", 10, y+8);
    y += 14;
  } else {
    for(let si=0; si<runs.length; si++){
      const run = runs[si];
      y = pdfStationLibreFullRun(doc, y, run);
      if(si < runs.length-1){
        y += 2;
        y = ensureSpace(doc, y, 18);
      }
    }
  }

  // Points section
  y = ensureSpace(doc, y, 22);
  y = pdfBar(doc, y, `POINTS (${(sourceLabel||'TXT').toUpperCase()})`);

  const points = Array.isArray(pts) ? pts : [];
  if(!points.length){
    setBodyFont(doc);
    doc.setFontSize(9);
    doc.text("Aucun point importé.", 10, y+6);
    y += 10;
  } else {
    const body = points.map(p=>[
      p.id ?? p.Id ?? "",
      fmt(p.x ?? p.X),
      fmt(p.y ?? p.Y),
      fmt(p.z ?? p.Z),
      (p.code ?? p.Code) ?? ""
    ]);

    nfAutoTable(doc, {
      startY: y + 1,
      head: [["ID point","X","Y","Z","Code"]],
      body,
      margin:{ left:10, right:10 },
      columnStyles:{
        0:{ cellWidth:32 },
        1:{ cellWidth:40 },
        2:{ cellWidth:40 },
        3:{ cellWidth:28 },
        4:{ cellWidth:50 }
      },
      styles: { fontSize: 7.5, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
      headStyles:{ fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
      alternateRowStyles:null,
      didParseCell: function(dataCell){
        if(dataCell && dataCell.section === "head"){
          try{ dataCell.cell.styles.fontStyle = "bold"; }catch(e){}
        }
      }
    });

    y = (doc.lastAutoTable && doc.lastAutoTable.finalY) ? (doc.lastAutoTable.finalY + 6) : (y + 10);
  }

  pdfFooterAllPagesAddress(doc);

  const suffix = (sourceLabel||'TXT').toUpperCase() === 'GSI' ? '_STATION_GSI' : '_STATION_TXT';

  nfStampLogoHeaders(doc);

  try{ if(typeof nfPostDrawThickLines==="function") nfPostDrawThickLines(doc); }catch(e){}
  // Règle: pas de suffixe par type de rapport -> tous les PDF partagent la même base.
  // (Le navigateur ajoutera (1), (2) en cas de doublon.)
  savePdfDoc(doc, (typeof buildExportFileName === "function") ? buildExportFileName("", "pdf") : `${pdfFileBase(R)}${suffix}.pdf`);
}

// ===== PDF STATION + OBSERVATIONS (GSI) =====
// V2 Étape 4.2 (sans calcul): affiche les observations polaires brutes (Hz/V/SD + hauteurs)
// dans un PDF, en conservant le bloc "Station uniquement" (si AppLog chargé).
function buildPdfStationPlusObservations(data, observations){
  syncDzUI();
  data = applyDzPolicyClone_(data, getOptions());
  const jsPDF = window.jspdf?.jsPDF;
  if(!jsPDF){ setStatus('Erreur PDF: jsPDF non chargé (CDN bloqué ?)', true); throw new Error('jsPDF non chargé (CDN bloqué ?)'); }
  const doc = new jsPDF({ unit:"mm", format:"a4", compress:true });
  try{ if(typeof nfPostBeginPdf==="function") nfPostBeginPdf(doc); }catch(e){};
  const R = rf();

  let y = drawHeaderV2(doc, R);

  const runs = (data && data.stationLibreRuns && data.stationLibreRuns.length)
    ? data.stationLibreRuns
    : (data && data.stationLibre ? [data.stationLibre] : []);

  if(!runs.length){
    setBodyFont(doc);
    doc.setFontSize(10);
    doc.text("Aucune station trouvée dans l'AppLog.", 10, y+8);
    y += 14;
  } else {
    for(let si=0; si<runs.length; si++){
      const run = runs[si];
      y = pdfStationLibreFullRun(doc, y, run);
      if(si < runs.length-1){
        y += 2;
        y = ensureSpace(doc, y, 18);
      }
    }
  }

  // Observations section
  y = ensureSpace(doc, y, 22);
  y = pdfBar(doc, y, "OBSERVATIONS POLAIRES (GSI)");

  const obs = Array.isArray(observations) ? observations : [];
  if(!obs.length){
    setBodyFont(doc);
    doc.setFontSize(9);
    doc.text("Aucune observation détectée.", 10, y+6);
    y += 10;
  } else {
    const body = obs.map(o=>[
      ((o.id ?? o.Id ?? "").toString().split("@")[0]),
      fmt(o.hz ?? o.Hz),
      fmt(o.v ?? o.V),
      fmt(o.sd ?? o.Sd),
      fmt(o.prismH ?? o.PrismH),
      fmt(o.instH ?? o.InstH),
      (o.code ?? o.Code) ?? ""
    ]);

    nfAutoTable(doc, {
      startY: y + 1,
      head: [["ID","Hz","V","Dist (SD)","H prisme","H inst","Code"]],
      body,
      margin:{ left:10, right:10 },
      columnStyles:{
        0:{ cellWidth:24 },
        1:{ cellWidth:20 },
        2:{ cellWidth:20 },
        3:{ cellWidth:26 },
        4:{ cellWidth:24 },
        5:{ cellWidth:24 },
        6:{ cellWidth:52 }
      },
      styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
      headStyles:{ fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
      alternateRowStyles:null,
      didParseCell: function(dataCell){
        if(dataCell && dataCell.section === "head"){
          try{ dataCell.cell.styles.fontStyle = "bold"; }catch(e){}
        }
      }
    });

    y = (doc.lastAutoTable && doc.lastAutoTable.finalY) ? (doc.lastAutoTable.finalY + 6) : (y + 10);
  }

  pdfFooterAllPagesAddress(doc);

  nfStampLogoHeaders(doc);

  try{ if(typeof nfPostDrawThickLines==="function") nfPostDrawThickLines(doc); }catch(e){}
  savePdfDoc(doc, (typeof buildExportFileName === "function") ? buildExportFileName("", "pdf") : `${pdfFileBase(R)}_STATION_GSI_OBS.pdf`);
}

// Rapport complet : implantation + rabattement (ligne de référence)
function buildPdfComplet(data){
  syncDzUI();
  data = applyDzPolicyClone_(data, getOptions());

  // Normalisation: évite des erreurs du type "Cannot read properties of undefined (reading 'results')"
  // quand stationLibreRuns est présent mais contient des entrées vides (import / cache / évolution data-model).
  try{
    if(data){
      if(Array.isArray(data.stationLibreRuns)) data.stationLibreRuns = data.stationLibreRuns.filter(Boolean);
      if((!Array.isArray(data.stationLibreRuns) || data.stationLibreRuns.length===0) && data.stationLibre){
        data.stationLibreRuns = [data.stationLibre];
      }
    }
  }catch(_){/* no-op */}

  const jsPDF = window.jspdf?.jsPDF;
  if(!jsPDF){
    setStatus('Erreur PDF : jsPDF indisponible (lib jspdf absente).', true);
    showErrorDialog("Erreur PDF : jsPDF indisponible");
    return;
  }

  // IMPORTANT: éviter une régression liée à un helper manquant (createPdfDoc)
  // suivant l'ordre de chargement et/ou le cache WebView2.
  // On crée ici explicitement le document jsPDF.
  const doc = new jsPDF({ unit:"mm", format:"a4", compress:true });
  try{ if(typeof nfPostBeginPdf==="function") nfPostBeginPdf(doc); }catch(e){};
  const R = rf();

  // Construit toutes les sections dans un seul document
  buildPdfFullInto(doc, data);

  nfStampLogoHeaders(doc);

  try{ if(typeof nfPostDrawThickLines==="function") nfPostDrawThickLines(doc); }catch(e){}
  savePdfDoc(doc, (typeof buildExportFileName === "function") ? buildExportFileName("", "pdf") : `${pdfFileBase(R)}.pdf`);
}

function buildPdfLigneRef(data){
  syncDzUI();
  data = applyDzPolicyClone_(data, getOptions());
  const jsPDF = window.jspdf?.jsPDF;
  if(!jsPDF){ setStatus('Erreur PDF: jsPDF non chargé (CDN bloqué ?)', true); throw new Error('jsPDF non chargé (CDN bloqué ?)'); }
  const doc = new jsPDF({ unit:"mm", format:"a4", compress:true });
  try{ if(typeof nfPostBeginPdf==="function") nfPostBeginPdf(doc); }catch(e){};
  const R = rf();

  
  const stats = computeControlStats(data.ligneRef.flatMap(lr => lr.rabPoints));
let y = drawHeaderV2(doc, R);

  const tolOn = document.getElementById("optTol").checked;
  const tXY = Number(document.getElementById("tolXY").value);
  const tZ = Number(document.getElementById("tolZ").value);
  const xyOn = document.getElementById("tolXYOn").checked;
  const zOn = document.getElementById("tolZOn").checked;

  const tolTxt = tolOn
    ? `Tolérances : X=${xyOn ? tXY : "—"} ; Y=${xyOn ? tXY : "—"} ; Z=${zOn ? tZ : "—"}`
    : `Tolérances désactivées`;
  y = pdfTolBar(doc, y, tolTxt);

  const runs = (data.stationLibreRuns && data.stationLibreRuns.length)
    ? data.stationLibreRuns
    : [data.stationLibre];

  const lrs = (data.ligneRef || []);

  // For each setup (AppLog order) -> Station block + residuals + its points (line-ref)
  for(let si=0; si<runs.length; si++){
    const run = runs[si];
    const setupId = run?.results?.idStation || null;

    // Station block (Type de station + Observations + Résidus)
    y = pdfStationLibreFullRun(doc, y, run);

    // Points for this setup
    y = pdfBar(doc, y, "MESURE SUR LIGNE");

    const lrPoints = lrs
      .filter(lr => (lr?.stationId || null) === setupId || (!setupId && !lr?.stationId))
      .flatMap(lr => lr?.rabPoints || [])
      .filter(p => (p?.id || "").toString().trim().length);

    if(!lrPoints.length){
      setBodyFont(doc);
      doc.setFontSize(9);
      doc.text("Aucun point mesuré sur ligne pour cette station.", 10, y+5);
      y += 9;
      continue;
    }

    const body = [];
    let __ptIndex = 0;
    lrPoints.forEach((p)=>{
      const id = (p.id || "").toString().trim();
      if(!id) return;

      const __grp = (__ptIndex % 2);

      const xMes = (p.mes?.E ?? p.mes?.X ?? p.E_mes ?? p.X_mes ?? p.Xmes ?? p.xMes ?? p.x_mes ?? p.Xmesure ?? p.X_mesure);
      const yMes = (p.mes?.N ?? p.mes?.Y ?? p.N_mes ?? p.Y_mes ?? p.Ymes ?? p.yMes ?? p.y_mes ?? p.Ymesure ?? p.Y_mesure);
      const zMes = (p.mes?.H ?? p.mes?.Z ?? p.H_mes ?? p.Z_mes ?? p.Zmes ?? p.zMes ?? p.z_mes ?? p.Zmesure ?? p.Z_mesure);

      // 1) POINT : XYZ mesurés uniquement
      body.push([
        {content:id, __kind:"MEAS", __grp:__grp, styles:{halign:"left"}}, "", "", "",
        "", "", "",
        "", "", "", ""
      ]);

      // 2) RABATTEMENT
      const dx = p.d?.dx ?? p.d?.dX ?? p.dx ?? p.dX ?? p.Dx ?? null;
      const dyv = p.d?.dy ?? p.d?.dY ?? p.dy ?? p.dY ?? p.Dy ?? null;
      const dz = p.d?.dz ?? p.d?.dZ ?? p.dz ?? p.dZ ?? p.Dz ?? null;

      const xCalc = p.calc?.E ?? p.calc?.X ?? p.E_calc ?? p.X_calc ?? p.Xcalc ?? p.xCalc ?? p.x_calc ?? p.Xcalcule ?? p.X_calcule;
      const yCalc = p.calc?.N ?? p.calc?.Y ?? p.N_calc ?? p.Y_calc ?? p.Ycalc ?? p.yCalc ?? p.y_calc ?? p.Ycalcule ?? p.Y_calcule;
      const zCalc = p.calc?.H ?? p.calc?.Z ?? p.H_calc ?? p.Z_calc ?? p.Zcalc ?? p.zCalc ?? p.z_calc ?? p.Zcalcule ?? p.Z_calcule;

      const stTxt = tolOn ? statusFromTol(dx, dyv, dz) : "";

      body.push([
        {content:"Point théorique", __kind:"RABPT", __grp:__grp, styles:{halign:"left"}}, fmt(xCalc), fmt(yCalc), fmt(zCalc),
        fmt(xMes), fmt(yMes), fmt(zMes),
        fmt(dx), fmt(dyv), fmt(dz),
        stTxt
      ]);

      // 3) LIGNE : dL/dT/dA
      const ec = p.ecarts || p.ec || p.line || {};
      const toNum = (v) => {
        if(v==null) return null;
        if(typeof v === "number") return Number.isFinite(v) ? v : null;
        const n = Number(String(v).replace(",", "."));
        return Number.isFinite(n) ? n : null;
      };
      const dL = toNum(p?.line?.dL ?? p?.dL ?? p?.dl ?? ec.dL ?? ec.dl ?? ec.DL ?? null);
      const dT = toNum(p?.line?.dT ?? p?.dT ?? p?.dt ?? ec.dT ?? ec.dt ?? ec.DT ?? null);
      const dA = toNum(p?.line?.dA ?? p?.dA ?? p?.da ?? ec.dA ?? ec.da ?? ec.DA ?? null);

      body.push([
        {content:"Delta ligne", __kind:"LINE", __grp:__grp, styles:{halign:"left"}}, "", "", "",
        "", "", "",
        fmt(dL), fmt(dT), fmt(dA),
        ""
      ]);

      __ptIndex++;
    });

    autoTableResults(doc, {
      startY: y + 1,
      nfThickSpec: "ligne",
      head: [["ID point","X calc","Y calc","Z calc","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","Statut"]],
      body,
      margin:{ left:10, right:10 },
    columnStyles: RESULTS_COLUMN_STYLES,
      styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
      headStyles:{ fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
      didParseCell: function (dataCell) { applyStatusFitAutoTable(dataCell);
              // 1.7.5 : en-têtes en gras (ne pas écraser le style du header)
      if(dataCell && dataCell.section === "head"){
        try{ dataCell.cell.styles.fontStyle = "bold"; }catch(e){}
        return;
      }
try{
          dataCell.cell.styles.textColor = [0,0,0];
          dataCell.cell.styles.fontStyle = "normal";
          const row = dataCell.row?.raw;
          if(!row) return;
          const kind = row[0]?.__kind;
          const grp = row[0]?.__grp;
          if(grp===0 || grp===1){
            const bg = (grp===1) ? [235,235,235] : [255,255,255];
            dataCell.cell.styles.fillColor = bg;
            return;
          }
          if(kind === "RAB" || kind === "SEC_RAB" || kind === "RABPT"){
            dataCell.cell.styles.fillColor = [240,240,240];
          }else if(kind === "LINE" || kind === "SEC_LINE"){
            dataCell.cell.styles.fillColor = [255,255,255];
          }
        }catch(e){}
      },
      alternateRowStyles:null,
    // IMPORTANT: no thick lines via autoTable hooks anymore (deterministic post-process)
    });

    y = doc.lastAutoTable.finalY + 3;
  }

  // Last-page box only (ensure the final page count is known BEFORE stamping pagination)
  // NOTE: this report may end with manual text blocks (no autoTable). Pass current Y to prevent
  // footer overlap in "Ligne de référence".
  y = ensureRoomForLastFooter(doc, 95, y);
  pdfFooterLastPageBox(doc, R, stats);

  // Address + pagination all pages (ONCE, at the very end to avoid overlaps)
  pdfFooterAllPagesAddress(doc);

  nfStampLogoHeaders(doc);

  try{ if(typeof nfPostDrawThickLines==="function") nfPostDrawThickLines(doc); }catch(e){}
  savePdfDoc(doc, (typeof buildExportFileName === "function") ? buildExportFileName("", "pdf") : `${pdfFileBase(R)}.pdf`);
}

/* =========================
   Events
========================= */
document.getElementById("landXmlInput")?.addEventListener("change", async (ev) => {
  try{
    const f = ev.target.files?.[0];
    if(!f) return;

    // Reset previous dataset BEFORE reading/parsing (avoid stale exports)
    try{
      lastData = null; try{ window.lastData = lastData; }catch(_){ }
      ["btnPdfIntervention","btnPdfInterventionPdfSharp","btnPdfCompletPdfSharp","btnPdfLigneRef","btnPdfStation","btnPdfPointsTopo","btnPdfFull","btnRecalc"].forEach(id=>{
        const b = document.getElementById(id);
        if(b) b.disabled = true;
      });
      const st = document.getElementById("stationContainer"); if(st) st.innerHTML = "";
      const im = document.getElementById("implantContainer"); if(im) im.innerHTML = "";
      const lr = document.getElementById("linerefContainer"); if(lr) lr.innerHTML = "";
      const tp = document.getElementById("topoContainer"); if(tp) tp.innerHTML = "";
    }catch(_){/* ignore */}

    document.getElementById("fileStatus").textContent = "LandXML : lecture…";
    // IMPORTANT: repLandXml est utilisé comme "texte de référence" (compat WinForms).
    // Ne pas le polluer avec le nom du fichier XML, sinon la cellule "Plan de référence" peut afficher *.xml.
    const elXml = document.getElementById("repLandXmlFile");
    if(elXml) elXml.value = f.name || "fichier.xml";

    setStatus("Lecture LandXML…");
    const xmlText = await readTextWithFallback(f);

    setStatus("Analyse LandXML…");
    if(typeof window.parseLandXmlLeica !== "function"){
      throw new Error("parseLandXmlLeica indisponible");
    }
    lastData = window.parseLandXmlLeica(xmlText, f.name || null);
    // Keep raw LandXML text for downstream PdfSharp payload mapping (operator/appareil fallback)
    try{
      if(lastData){
        lastData.rawText = xmlText;
        lastData.meta = lastData.meta || {};
        // Try to extract Survey Creator as operator if missing
        if(!lastData.meta.operator){
          try{
            const dom = new DOMParser().parseFromString(xmlText, "text/xml");
            const s = dom.querySelector("Survey");
            const c = s ? (s.getAttribute("Creator") || "") : "";
            const cc = String(c||"").trim();
            if(cc) lastData.meta.operator = cc;
          }catch(_){
            const m = String(xmlText||"").match(/\bCreator\s*=\s*"([^"]+)"/i);
            if(m && m[1]) lastData.meta.operator = String(m[1]).trim();
          }
        }
      }
    }catch(_){/* ignore */}
    if(!lastData){ throw new Error("Parsing LandXML : dataset vide"); }

    // Expose globally (compat pipeline PDF)
    try { window.__NF_LASTDATA = lastData; } catch (_) {}
    try { window.lastData = lastData; } catch (_) {}


// Expose LandXML in "Échanges" state (for pills + filename)
try{
  window.nfExchange = window.nfExchange || {};
  window.nfExchange.landxml = window.nfExchange.landxml || {};
  window.nfExchange.landxml.loaded = true;
  window.nfExchange.landxml.fileName = f.name || null;
  window.nfExchange.landxml.setupCount = Array.isArray(lastData.stationLibreRuns) ? lastData.stationLibreRuns.length : 0;
}catch(_){}

// Enable TXT export button
try{ const b=document.getElementById('btnExportTxt'); if(b) b.disabled=false; }catch(_){}
    // Render + enable buttons
    // Update PROJET pills *before* renderAll, so a UI rendering error can't keep it red
    try{ if (typeof window.updateProjectBanner === "function") window.updateProjectBanner(); }catch(_){}

    try{
      if (typeof window.renderAll === "function") window.renderAll(lastData);
    }catch(e){
      console.error("renderAll failed", e);
    }

    // Re-apply pills after render attempt (safe)
    try{ if (typeof window.updateProjectBanner === "function") window.updateProjectBanner(); }catch(_){}
    try{ if (typeof window.updateExchangePills === "function") window.updateExchangePills(); }catch(_){}
    // Enable exports ONLY if data exists (avoid generating empty PDFs).
    // Content-based disabling is computed from the parsed dataset.
    try{
      if(typeof window.applyReportButtonStateFromData === "function") window.applyReportButtonStateFromData(lastData);
      if(typeof window.updatePdfSharpButtonState === "function") window.updatePdfSharpButtonState();
    }catch(_){/* ignore */}

    document.getElementById("fileStatus").textContent = "LandXML : chargé";
    setStatus(`LandXML : chargé (${f.name || 'fichier.xml'})`);
  }catch(err){
    console.error(err);
    try{
      lastData = null; try{ window.lastData = lastData; }catch(_){ }
      ["btnPdfIntervention","btnPdfInterventionPdfSharp","btnPdfCompletPdfSharp","btnPdfLigneRef","btnPdfStation","btnPdfPointsTopo","btnPdfHeightTransfer","btnPdfFull","btnRecalc"].forEach(id=>{
        const b = document.getElementById(id);
        if(b) b.disabled = true;
      });
    }catch(_){/* ignore */}
    try{ document.getElementById("fileStatus").textContent = "LandXML : erreur"; }catch(_){}
    setStatus("Erreur LandXML: " + (err?.message || String(err)), true);
  }
});





document.getElementById("sigInput")?.addEventListener("change", async (ev) => {
  try{
    const f = ev.target.files?.[0];
    if(!f) return;
    const res = await loadAndCompressLogo(f);
    sigDataUrl = res.dataUrl;
    sigImageType = res.type || "JPEG";
    document.getElementById("sigStatus").textContent = sigDataUrl ? "Signature : chargée (compressée)" : "Signature : erreur";
  }catch(err){
    console.error(err);
    setStatus("Erreur signature: " + (err?.message || String(err)), true);
  }
});




// Ensure the Station button exists in the DOM (robust against older HTML assets)
function ensureStationButton(){
  try{
    if(document.getElementById("btnPdfStation")) return;
    const lr = document.getElementById("btnPdfLigneRef");
    if(!lr) return;
    const b = document.createElement("button");
    b.id = "btnPdfStation";
    b.disabled = true;
    b.title = "Génère le PDF Station uniquement (bloc station du rapport complet).";
    b.textContent = "PDF — Station (station uniquement)";
    // inherit style/classes from the neighbouring PDF button
    b.className = lr.className;
    lr.insertAdjacentElement("afterend", b);
  }catch(_){/* ignore */}
}

// ---- PdfSharp TEST support ----
function isPdfSharpAvailable(){
  try{ return !!window.__NF_PDFSHARP_AVAILABLE; }catch(_){ return false; }
}

function nfPdfInstrumentMeta_(data, reportFields){
  const d = data || window.__NF_LASTDATA || window.lastData || null;
  const r = reportFields || {};
  const model = String(
    d?.meta?.instrument
    || r.appareilModel
    || r.model
    || r.appareil
    || r.instrument
    || r.app
    || ""
  ).trim();
  const serial = String(
    d?.meta?.serial
    || r.serialNumber
    || r.appareilSerial
    || r.serial
    || r.numeroSerie
    || r["numero_série"]
    || ""
  ).trim();
  return {
    model,
    appareil: model,
    appareilModel: model,
    serialNumber: serial,
    appareilSerial: serial
  };
}
window.nfPdfInstrumentMeta = nfPdfInstrumentMeta_;

// ---- Availability / UX : enable only relevant report buttons after LandXML analysis ----
function computeHasStation(data){
  try{
    const runs = (data?.stationLibreRuns && Array.isArray(data.stationLibreRuns) && data.stationLibreRuns.length)
      ? data.stationLibreRuns
      : (data?.stationLibre ? [data.stationLibre] : []);
    return runs.some(r => {
      const SR = r?.results || {};
      const obs = Array.isArray(r?.observations) ? r.observations : [];
      return !!(SR.idStation || SR.E!=null || SR.N!=null || SR.H!=null || obs.length);
    });
  }catch(_){ return false; }
}


// -------------------------------
// Patch 3 — Exclusions (points décochés) appliquées aux exports PDF
// Key = ID point (string trim)
// -------------------------------
function nfPid_(id){ return String(id ?? "").trim(); }

function nfExcludedSet_(){
  try{
    const arr = (typeof window.nfGetExcludedPointIds === "function") ? window.nfGetExcludedPointIds() : [];
    return new Set((arr||[]).map(nfPid_).filter(Boolean));
  }catch(_){
    return new Set();
  }
}

function nfRefAltiSet_(){
  try{
    const arr = (typeof window.nfGetRefAltiPointIds === "function") ? window.nfGetRefAltiPointIds() : [];
    return new Set((arr||[]).map(nfPid_).filter(Boolean));
  }catch(_){
    return new Set();
  }
}

function nfExcludedForImplantLr_(){
  const out = nfExcludedSet_();
  try{
    for(const id of nfRefAltiSet_()) out.add(id);
  }catch(_){ /* ignore */ }
  return out;
}

function nfCollectRefAltiPoints_(data){
  try{
    const selected = nfRefAltiSet_();
    if(!selected.size) return [];
    const out = [];
    const seen = new Set();

    const pick = (...vals) => {
      for(const v of vals){
        if(v !== undefined && v !== null && String(v).trim() !== '') return v;
      }
      return '';
    };

    const push = (source, id, E, N, H) => {
      id = nfPid_(id);
      if(!id || !selected.has(id) || seen.has(id)) return;
      seen.add(id);
      out.push({ source, id, E: pick(E), N: pick(N), H: pick(H) });
    };

    // Réf alti sélectionnée depuis Implantation
    for(const p of (Array.isArray(data?.implantation?.points) ? data.implantation.points : [])){
      push(
        'Implantation',
        p?.id,
        p?.mes?.E ?? p?.mes?.X ?? p?.theo?.E ?? p?.theo?.X,
        p?.mes?.N ?? p?.mes?.Y ?? p?.theo?.N ?? p?.theo?.Y,
        p?.mes?.H ?? p?.mes?.Z ?? p?.theo?.H ?? p?.theo?.Z
      );
    }

    // Réf alti sélectionnée depuis Ligne de référence
    for(const lr of (Array.isArray(data?.ligneRef) ? data.ligneRef : [])){
      for(const p of (Array.isArray(lr?.rabPoints) ? lr.rabPoints : [])){
        push(
          'Ligne de référence',
          p?.id,
          p?.mes?.E ?? p?.mes?.X ?? p?.calc?.E ?? p?.calc?.X,
          p?.mes?.N ?? p?.mes?.Y ?? p?.calc?.N ?? p?.calc?.Y,
          p?.mes?.H ?? p?.mes?.Z ?? p?.calc?.H ?? p?.calc?.Z
        );
      }
    }

    // Réf alti sélectionnée depuis Levé topo.
    // Important : la sélection doit alimenter tous les PDF, quel que soit le module d'origine.
    for(const st of (Array.isArray(data?.topoStations) ? data.topoStations : [])){
      for(const p of (Array.isArray(st?.results) ? st.results : [])){
        push('Levé topo', p?.id, p?.E ?? p?.X, p?.N ?? p?.Y, p?.H ?? p?.Z);
      }
      for(const p of (Array.isArray(st?.rectangulaires) ? st.rectangulaires : [])){
        push('Levé topo', p?.id, p?.E ?? p?.X, p?.N ?? p?.Y, p?.H ?? p?.Z);
      }
      for(const p of (Array.isArray(st?.rect) ? st.rect : [])){
        push('Levé topo', p?.id, p?.E ?? p?.X, p?.N ?? p?.Y, p?.H ?? p?.Z);
      }
    }

    return out;
  }catch(_){ return []; }
}


function nfZoneLegacyPointKey_(p){
  try{
    // Old/non-unique key kept only for backward compatibility with already-saved .nova projects.
    const id = String(p?.id ?? p?.name ?? p?.pointId ?? p?.no ?? '').trim();
    const sid = String(p?.stationId ?? p?.station ?? '').trim();
    return `${sid}|${id}`;
  }catch(_){ return ''; }
}

function nfZonePointKey_(p){
  try{
    // Stable unique key for code assignment. The source is mandatory to avoid collisions
    // between Implantation and Ligne de réf points that share the same point id.
    const id = String(p?.id ?? p?.name ?? p?.pointId ?? p?.no ?? '').trim();
    const sid = String(p?.stationId ?? p?.station ?? '').trim();
    const src = String(p?.__nfSource ?? '').trim();
    return `${src}|${sid}|${id}`;
  }catch(_){ return ''; }
}

const NF_MANUAL_ZONE_PREFIX = '__MANUAL__:';
function nfIsManualZoneKey_(key){
  try{ return String(key || '').startsWith(NF_MANUAL_ZONE_PREFIX); }catch(_){ return false; }
}
function nfManualZoneKeyFromInput_(raw){
  const s = String(raw || '').trim();
  if(!s) return '';
  return nfIsManualZoneKey_(s) ? s : (NF_MANUAL_ZONE_PREFIX + s);
}
function nfManualZoneDisplayFromKey_(key){
  const s = String(key || '').trim();
  return nfIsManualZoneKey_(s) ? s.slice(NF_MANUAL_ZONE_PREFIX.length) : s;
}

function nfGetZonePointCodeMap_(){
  try{
    return Object.assign({}, window.__NF_ZONE_POINT_CODE_MAP || {});
  }catch(_){ return {}; }
}
window.NF_getZonePointCodeMap = nfGetZonePointCodeMap_;

function nfSetZonePointCodeMap(map){
  try{
    window.__NF_ZONE_POINT_CODE_MAP = Object.assign({}, map || {});
    if(typeof nfRenderZoneLabelsEditor_ === 'function') nfRenderZoneLabelsEditor_();
  }catch(_){ }
}
window.NF_setZonePointCodeMap = nfSetZonePointCodeMap;

function nfGetGlobalZoneCode(){
  try{
    return String(window.__NF_GLOBAL_ZONE_CODE || '').trim();
  }catch(_){ return ''; }
}
window.NF_getGlobalZoneCode = nfGetGlobalZoneCode;

function nfSetGlobalZoneCode(code){
  try{
    window.__NF_GLOBAL_ZONE_CODE = String(code || '').trim();
    if(typeof nfRenderZoneLabelsEditor_ === 'function') nfRenderZoneLabelsEditor_();
  }catch(_){ }
}
window.NF_setGlobalZoneCode = nfSetGlobalZoneCode;

function nfGetZoneRenameMap(){
  try{
    return Object.assign({}, window.__NF_ZONE_RENAME_MAP || {});
  }catch(_){ return {}; }
}
window.NF_getZoneRenameMap = nfGetZoneRenameMap;

function nfSetZoneRenameMap(map){
  try{
    window.__NF_ZONE_RENAME_MAP = Object.assign({}, map || {});
    if(typeof nfRenderZoneLabelsEditor_ === 'function') nfRenderZoneLabelsEditor_();
  }catch(_){ }
}
window.NF_setZoneRenameMap = nfSetZoneRenameMap;

function nfZoneCodeOf_(p){
  try{
    const map = nfGetZonePointCodeMap_();
    const key = nfZonePointKey_(p);
    if(key && map[key]) return String(map[key]).trim();
    const legacy = nfZoneLegacyPointKey_(p);
    if(legacy && map[legacy]) return String(map[legacy]).trim();

    const globalCode = nfGetGlobalZoneCode();
    if(globalCode) return globalCode;

    const direct = p?.code ?? p?.Code ?? '';
    const raw = String(direct || '').trim();
    if(raw) return raw;
    return '';
  }catch(_){ return ''; }
}

function nfZoneLabelOf_(code){
  const raw = String(code || '').trim();
  if(!raw) return '';
  const map = nfGetZoneRenameMap();
  if(nfIsManualZoneKey_(raw)) return String(map[raw] || nfManualZoneDisplayFromKey_(raw) || '');
  return String(map[raw] || raw);
}

function nfAllImplantLrZoneCodes_(){
  const out = new Set();
  try{
    const ex = nfExcludedForImplantLr_();
    const imp = nfFilterPointsById_(collectAllImplantPoints(lastData), ex);
    for(const p of (imp||[])){
      const direct = String(p?.code ?? p?.Code ?? p?.code ?? p?.Code ?? '').trim();
      if(direct) out.add(direct);
    }
    const lrs = nfFilterLigneRef_(Array.isArray(lastData?.ligneRef) ? lastData.ligneRef : [], ex);
    for(const lr of lrs){
      for(const p of (Array.isArray(lr?.rabPoints) ? lr.rabPoints : [])){
        const direct = String(p?.code ?? p?.Code ?? p?.code ?? p?.Code ?? '').trim();
        if(direct) out.add(direct);
      }
    }
    const pointMap = nfGetZonePointCodeMap_();
    for(const v of Object.values(pointMap||{})){
      const z = String(v || '').trim();
      if(z) out.add(z);
    }
    const labelMap = nfGetZoneRenameMap();
    for(const k of Object.keys(labelMap||{})){
      const z = String(k || '').trim();
      if(z) out.add(z);
    }
  }catch(_){ }
  return Array.from(out).sort((a,b)=>nfZoneLabelOf_(a).localeCompare(nfZoneLabelOf_(b), 'fr', {numeric:true, sensitivity:'base'}));
}

function nfAllImplantLrPointsWithoutCode_(){
  const out = [];
  const seen = new Set();
  try{
    const ex = nfExcludedForImplantLr_();
    const imp = nfFilterPointsById_(collectAllImplantPoints(lastData), ex);
    for(const p of (imp||[])){
      const direct = String(p?.code ?? p?.Code ?? p?.code ?? p?.Code ?? '').trim();
      const manual = nfZoneCodeOf_(p);
      if(direct || manual) continue;
      const pp = Object.assign({__nfSource:'IMP'}, p || {});
      const key = nfZonePointKey_(pp);
      if(!key || seen.has(key)) continue;
      seen.add(key);
      out.push(pp);
    }
    const lrs = nfFilterLigneRef_(Array.isArray(lastData?.ligneRef) ? lastData.ligneRef : [], ex);
    for(const lr of lrs){
      for(const p of (Array.isArray(lr?.rabPoints) ? lr.rabPoints : [])){
        const direct = String(p?.code ?? p?.Code ?? p?.code ?? p?.Code ?? '').trim();
        if(direct) continue;
        const pp = Object.assign({__nfSource:'LR'}, p || {});
        const key = nfZonePointKey_(pp);
        if(!key || seen.has(key)) continue;
        seen.add(key);
        out.push(pp);
      }
    }
  }catch(_){ }
  return out.sort((a,b)=> String(a?.id||'').localeCompare(String(b?.id||''), 'fr', {numeric:true, sensitivity:'base'}));
}


function nfRefreshZoneUi_(){
  try{
    if(typeof renderAll === 'function' && lastData) renderAll(lastData);
    else if(typeof nfRenderZoneLabelsEditor_ === 'function') nfRenderZoneLabelsEditor_();
  }catch(_){
    try{ if(typeof nfRenderZoneLabelsEditor_ === 'function') nfRenderZoneLabelsEditor_(); }catch(__){ }
  }
}

function nfRenderZoneLabelsEditor_(){
  try{
    const host = document.getElementById('zoneLabelsWrap');
    if(!host) return;
    const codes = nfAllImplantLrZoneCodes_();
    const map = nfGetZoneRenameMap();

    const escapeHtml = (s) => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    const zoneOptions = ['<option value="">(aucune)</option>']
      .concat(codes.map(code => {
        const val = escapeHtml(code);
        const txt = escapeHtml(nfZoneLabelOf_(code) || (nfIsManualZoneKey_(code) ? nfManualZoneDisplayFromKey_(code) : code));
        return `<option value="${val}">${txt}</option>`;
      }))
      .join('');

    const htmlCodes = `
      <div class="small" style="margin-bottom:4px; font-weight:700;">Codes PDF</div>
      <div class="small" style="margin-bottom:8px; color:var(--mut);">Ajout de codes manuels + renommage PDF. Les affectations ligne par ligne se font directement dans les tables de visualisation.</div>
      ${codes.length ? codes.map(code=>{
        const safeCode = escapeHtml(code);
        const displayCode = escapeHtml(nfIsManualZoneKey_(code) ? nfManualZoneDisplayFromKey_(code) : code);
        const val = escapeHtml(String(map[code] || nfZoneLabelOf_(code) || displayCode));
        return `<div style="display:grid; grid-template-columns:100px 1fr; gap:8px; align-items:center; margin:4px 0;">
          <div class="mono small">${displayCode}</div>
          <input class="box nf-zone-label-input" data-zone-code="${safeCode}" value="${val}" style="min-width:0;" />
        </div>`;
      }).join('') : `<div class="small" style="color:var(--mut); margin:0 0 8px;">Aucun code détecté. Ajoute un code manuel ci-dessous pour pouvoir l'affecter dans les tableaux.</div>`}
      <div style="display:grid; grid-template-columns:100px 1fr auto; gap:8px; align-items:center; margin:10px 0 0 0;">
        <input id="nfNewZoneCode" class="box" placeholder="Code" style="min-width:0;" />
        <input id="nfNewZoneLabel" class="box" placeholder="Libellé PDF (optionnel)" style="min-width:0;" />
        <button type="button" id="nfAddZoneBtn">+ Ajouter</button>
      </div>`;

    const globalCodeCurrent = String(nfGetGlobalZoneCode() || '').trim();
    let globalOptions = zoneOptions;
    if(globalCodeCurrent){
      const escGlobal = escapeHtml(globalCodeCurrent);
      globalOptions = globalOptions.replace(`value="${escGlobal}"`, `value="${escGlobal}" selected`);
    }else{
      globalOptions = globalOptions.replace('value=""', 'value="" selected');
    }

    const htmlGlobal = `
      <div style="margin-top:12px; display:grid; gap:6px;">
        <div class="small" style="color:var(--mut);">Le code global PDF sert de valeur par défaut si aucune affectation ligne n'est définie dans les tableaux.</div>
        <div style="display:grid; grid-template-columns:220px 1fr auto; gap:8px; align-items:center; margin-bottom:6px;">
          <div class="small" style="color:var(--mut);">Code global PDF</div>
          <select id="nfBulkZoneAssign" class="box" style="min-width:0;">${globalOptions}</select>
          <button type="button" id="nfBulkZoneApply">Enregistrer</button>
        </div>
      </div>`;

    host.innerHTML = htmlCodes + htmlGlobal;

    host.querySelectorAll('.nf-zone-label-input').forEach(inp=>{
      inp.addEventListener('change', ()=>{
        const code = String(inp.getAttribute('data-zone-code') || '').trim();
        if(!code) return;
        const next = nfGetZoneRenameMap();
        next[code] = String(inp.value || code).trim() || code;
        window.__NF_ZONE_RENAME_MAP = next;
        nfRefreshZoneUi_();
      });
    });

    host.querySelector('#nfAddZoneBtn')?.addEventListener('click', ()=>{
      const codeEl = host.querySelector('#nfNewZoneCode');
      const labelEl = host.querySelector('#nfNewZoneLabel');
      const rawCode = String(codeEl?.value || '').trim();
      const label = String(labelEl?.value || '').trim();
      if(!rawCode) return;
      const code = nfManualZoneKeyFromInput_(rawCode);
      const next = nfGetZoneRenameMap();
      next[code] = label || rawCode;
      window.__NF_ZONE_RENAME_MAP = next;
      nfRefreshZoneUi_();
    });

    host.querySelector('#nfBulkZoneApply')?.addEventListener('click', ()=>{
      const val = String(host.querySelector('#nfBulkZoneAssign')?.value || '').trim();
      nfSetGlobalZoneCode(val);
      nfRefreshZoneUi_();
    });
  }catch(_){ }
}


window.NF_resolvePointCode = function(p){ return nfZoneCodeOf_(p); };
window.NF_buildPointAssignKey = function(p){ return nfZonePointKey_(p); };
window.NF_getPointInlineCodeMap = function(){ return nfGetZonePointCodeMap_(); };
window.NF_setInlinePointCode = function(pointKey, code){
  try{
    const key = String(pointKey || '').trim();
    if(!key) return;
    const next = nfGetZonePointCodeMap_();
    const val = String(code || '').trim();
    if(val) next[key] = val;
    else delete next[key];
    window.__NF_ZONE_POINT_CODE_MAP = next;
    if(typeof nfRenderZoneLabelsEditor_ === 'function') nfRenderZoneLabelsEditor_();
  }catch(_){ }
};
window.NF_buildCodeOptionsHtml = function(selectedValue){
  try{
    const esc = (s) => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    const selected = String(selectedValue || '').trim();
    const codes = nfAllImplantLrZoneCodes_();
    let html = '<option value="">(aucune)</option>';
    for(const code of codes){
      const val = esc(code);
      const txt = esc(nfZoneLabelOf_(code) || (nfIsManualZoneKey_(code) ? nfManualZoneDisplayFromKey_(code) : code));
      html += `<option value="${val}"${selected===String(code)?' selected':''}>${txt}</option>`;
    }
    return html;
  }catch(_){ return '<option value="">(aucune)</option>'; }
};

function nfGroupRowsByZoneMarker_(points, rowBuilder){
  const groups = new Map();
  for(const p of (Array.isArray(points) ? points : [])){
    const code = nfZoneCodeOf_(p) || '';
    if(!groups.has(code)) groups.set(code, []);
    groups.get(code).push(rowBuilder(p));
  }
  const keys = Array.from(groups.keys()).sort((a,b)=>a.localeCompare(b, 'fr', {numeric:true, sensitivity:'base'}));
  const out = [];
  for(const code of keys){
    const label = nfZoneLabelOf_(code);
    if(label) out.push([`__ZONE__:${label}`]);
    for(const row of (groups.get(code) || [])) out.push(row);
  }
  return out;
}

function nfPdfGroupByZoneEnabled_(){
  try{ return !!document.getElementById('pdfGroupByZone')?.checked; }catch(_){ return false; }
}

function nfBuildImplantationByStationPayload_(allImpPts, data){
  const implantationByStation = [];
  try{
    const by = {};
    for(const p of (allImpPts||[])){
      const sid = (p && p.stationId) ? String(p.stationId) : "";
      if(!by[sid]) by[sid] = [];
      by[sid].push(p);
    }
    const runs = (data && Array.isArray(data.stationLibreRuns)) ? data.stationLibreRuns : [];
    const orderedIds = [];
    for(const r of runs){
      const id = r?.results?.idStation ? String(r.results.idStation) : "";
      if(id && !orderedIds.includes(id)) orderedIds.push(id);
    }
    const extra = Object.keys(by).filter(k => !!k && !orderedIds.includes(k)).sort((a,b)=>a.localeCompare(b));
    const buildRows = pts => pts.map(rowFromPoint);
    const buildZoneGroups = pts => {
      const groups = new Map();
      for(const p of (pts||[])){
        const code = nfZoneCodeOf_(p) || '';
        if(!groups.has(code)) groups.set(code, []);
        groups.get(code).push(rowFromPoint(p));
      }
      return Array.from(groups.keys())
        .sort((a,b)=>a.localeCompare(b, 'fr', {numeric:true, sensitivity:'base'}))
        .map(code=>({ code, label: nfZoneLabelOf_(code), rows: groups.get(code) || [] }));
    };
    for(const id of orderedIds.concat(extra)){
      const pts = by[id] || [];
      implantationByStation.push({ stationId: id, rows: buildRows(pts), zoneGroups: buildZoneGroups(pts) });
    }
    if(by[""] && by[""].length){
      const pts = by[""];
      implantationByStation.push({ stationId: "", rows: buildRows(pts), zoneGroups: buildZoneGroups(pts) });
    }
  }catch(_){ /* ignore */ }
  return implantationByStation;
}

function nfBuildLigneRefRowsByStationPayload_(ligneRef, data){
  const out = [];
  try{
    const by = {};
    for(const lr of nfNormalizeLigneRefStationIds_(ligneRef, data)){
      const sid = lr?.stationId ? String(lr.stationId) : '';
      if(!by[sid]) by[sid] = [];
      const pts = Array.isArray(lr?.rabPoints) ? lr.rabPoints : [];
      by[sid].push(...pts);
    }
    const runs = (data && Array.isArray(data.stationLibreRuns)) ? data.stationLibreRuns : [];
    const orderedIds = [];
    for(const r of runs){
      const id = r?.results?.idStation ? String(r.results.idStation) : '';
      if(id && !orderedIds.includes(id)) orderedIds.push(id);
    }
    const extra = Object.keys(by).filter(k => !!k && !orderedIds.includes(k)).sort((a,b)=>a.localeCompare(b));
    const buildRows = pts => pts.map(rowFromLineEC);
    const buildZoneGroups = pts => {
      const groups = new Map();
      for(const p of (pts||[])){
        const code = nfZoneCodeOf_(p) || '';
        if(!groups.has(code)) groups.set(code, []);
        groups.get(code).push(p);
      }
      return Array.from(groups.keys())
        .sort((a,b)=>a.localeCompare(b, 'fr', {numeric:true, sensitivity:'base'}))
        .map(code=>({ code, label: nfZoneLabelOf_(code), points: groups.get(code) || [] }));
    };
    for(const id of orderedIds.concat(extra)){
      const pts = by[id] || [];
      const rows = buildRows(pts);
      if(!rows.length) continue;
      out.push({ stationId:id, rows, zoneGroups: buildZoneGroups(pts) });
    }
    if(by[''] && by[''].length){
      const pts = by[''];
      const rows = buildRows(pts);
      if(rows.length) out.push({ stationId:'', rows, zoneGroups: buildZoneGroups(pts) });
    }
  }catch(_){ }
  return out;
}

function nfCollectUsedStationIdsFromPayload_(rowsByStation){
  const used = new Set();
  try{
    for(const entry of (Array.isArray(rowsByStation) ? rowsByStation : [])){
      const sid = (entry && entry.stationId != null) ? String(entry.stationId) : '';
      const rows = Array.isArray(entry?.rows) ? entry.rows : [];
      if(sid && rows.length) used.add(sid);
    }
  }catch(_){ /* ignore */ }
  return used;
}

function nfResolveStationPdfKey_(stationId, data){
  try{
    const sid = stationId == null ? '' : String(stationId);
    if(!sid) return '';
    const runs = Array.isArray(data?.stationLibreRuns) ? data.stationLibreRuns : [];
    for(const run of runs){
      const displayId = run?.results?.idStation != null ? String(run.results.idStation) : '';
      const setupId = run?.setupId != null ? String(run.setupId) : (run?.results?.setupId != null ? String(run.results.setupId) : '');
      const stationName = run?.stationName != null ? String(run.stationName) : (run?.results?.stationName != null ? String(run.results.stationName) : '');
      if(sid && (sid === displayId || sid === setupId || sid === stationName)){
        return displayId || stationName || setupId || sid;
      }
    }
    return sid;
  }catch(_){
    return stationId == null ? '' : String(stationId);
  }
}

function nfNormalizeLigneRefStationIds_(ligneRef, data){
  try{
    return (Array.isArray(ligneRef) ? ligneRef : []).map(lr=>{
      const stationId = nfResolveStationPdfKey_(lr?.stationId, data);
      const stationName = lr?.stationName || stationId || '';
      const rabPoints = (Array.isArray(lr?.rabPoints) ? lr.rabPoints : []).map(p=>({
        ...(p || {}),
        stationId,
        stationName: p?.stationName || stationName || ''
      }));
      return {
        ...(lr || {}),
        stationId,
        stationName,
        rabPoints
      };
    });
  }catch(_){
    return Array.isArray(ligneRef) ? ligneRef : [];
  }
}

function nfFilterStationLibreRunsByStationIds_(stationLibreRuns, usedStationIds){
  try{
    const runs = Array.isArray(stationLibreRuns) ? stationLibreRuns : [];
    const used = (usedStationIds instanceof Set) ? usedStationIds : new Set();
    if(!used.size) return [];
    return runs.filter(run=>{
      const sid = run?.results?.idStation ? String(run.results.idStation) : '';
      return !!sid && used.has(sid);
    });
  }catch(_){
    return Array.isArray(stationLibreRuns) ? stationLibreRuns : [];
  }
}


function nfSanitizeStationRunsForPdf_(stationLibreRuns){
  try{
    const runs = Array.isArray(stationLibreRuns) ? stationLibreRuns : [];
    const looksSetupId = (v)=>/^TPSSetupID_/i.test(String(v||""));
    const chooseStationName = (a,b,c)=>{
      const vals = [a,b,c].map(v => v == null ? "" : String(v).trim()).filter(Boolean);
      return vals.find(v => !looksSetupId(v)) || vals[0] || "";
    };
    return runs
      .filter(r => r && typeof r === 'object')
      .map(r => {
        const rr = (r.results && typeof r.results === 'object') ? r.results : {};
        const setupId = (r.setupId != null && String(r.setupId).trim())
          ? String(r.setupId).trim()
          : ((rr.setupId != null && String(rr.setupId).trim()) ? String(rr.setupId).trim() : '');
        const stationName = chooseStationName(r.stationName, rr.stationName, r.name);
        const idStation = (rr.idStation != null && String(rr.idStation).trim())
          ? String(rr.idStation).trim()
          : (setupId || stationName || '');
        const stripAt = (v)=>{ const s = (v==null) ? "" : String(v); const i = s.indexOf("@"); return i >= 0 ? s.substring(0, i) : s; };
        const observations = Array.isArray(r.observations) ? r.observations.filter(o => o && typeof o === 'object').map(o => ({ ...o, id: stripAt(o.id) })) : [];
        const residuals = Array.isArray(r.residuals) ? r.residuals.filter(o => o && typeof o === 'object').map(o => ({ ...o, id: stripAt(o.id) })) : [];
        return {
          ...r,
          setupId: setupId || idStation || null,
          stationId: idStation || setupId || null,
          stationName: stationName || idStation || setupId || null,
          observations,
          residuals,
          results: {
            ...rr,
            setupId: setupId || idStation || null,
            idStation: idStation || setupId || null,
            stationName: stationName || idStation || setupId || null
          }
        };
      })
      .filter(r => {
        const rr = r?.results || {};
        return !!(rr.idStation || rr.E != null || rr.N != null || rr.H != null || (r.observations||[]).length || (r.residuals||[]).length);
      });
  }catch(_){
    return Array.isArray(stationLibreRuns) ? stationLibreRuns : [];
  }
}

function nfFilterPointsById_(points, excludedSet){
  try{
    const ex = excludedSet || nfExcludedSet_();
    return (Array.isArray(points)?points:[]).filter(p=>{
      const ids = [
        nfPid_(p?.__nfPid),
        nfPid_(p?.stakedId),
        nfPid_(p?.occurrenceId),
        nfPid_(p?.id ?? p?.Id ?? p?.ID ?? p?.name ?? p?.pntRef)
      ].filter(Boolean);
      if(!ids.length) return true;
      return !ids.some(id => ex.has(id));
    });
  }catch(_){ return Array.isArray(points)?points:[]; }
}

function nfFilterTopoStations_(topoStations, excludedSet){
  const ex = excludedSet || nfExcludedSet_();
  const out = [];
  for(const st of (Array.isArray(topoStations)?topoStations:[])){
    const obs = Array.isArray(st?.observations) ? st.observations.filter(o=>{ const id = nfPid_(o?.id); return !id || !ex.has(id); }) : [];
    const res = Array.isArray(st?.results) ? st.results.filter(r=>{ const id = nfPid_(r?.id); return !id || !ex.has(id); }) : [];
    if((obs.length + res.length) === 0) continue;
    out.push({ ...st, observations: obs.map(o => ({ ...o, id: nfBasePid_(o?.id) || nfPid_(o?.id) })), results: res.map(r => ({ ...r, id: nfBasePid_(r?.id) || nfPid_(r?.id) })) });
  }
  return out;
}

// -------------------------------
// Points topo (LEVÉ) — règle métier NOVATLAS
// Si un point apparaît dans un programme Stakeout (implantation),
// il doit apparaître uniquement dans le rapport Implantation.
// => on l'exclut du rapport Points topo (LEVÉ).
//
// IMPORTANT:
// - pas de filtre par numéro/prefixe (IPI, etc.)
// - exclusion basée sur le LandXML (ApplicationStakeout)
// - compare sur l'ID "base" (sans suffixe Leica @NN)
// -------------------------------
function nfBasePid_(id){
  const s = nfPid_(id);
  if(!s) return "";
  const i = s.indexOf("@");
  return (i > 0 ? s.slice(0, i) : s).trim();
}

function nfStakeoutIdSetFromRawText_(rawText){
  const out = new Set();
  try{
    const txt = (typeof rawText === 'string') ? rawText : '';
    if(!txt) return out;

    // Match both StakedPointID and DesignPointID in <ApplicationStakeout ...>
    // Handles both double and single quotes.
    const re = /<ApplicationStakeout\b[^>]*?\b(?:StakedPointID|DesignPointID)\s*=\s*(?:"([^"]+)"|'([^']+)')/g;
    let m;
    while((m = re.exec(txt)) !== null){
      const v = (m[1] || m[2] || '').trim();
      const b = nfBasePid_(v);
      if(b) out.add(b);
    }
  }catch(_){ /* ignore */ }
  return out;
}

function nfFilterTopoStationsForLeve_(data){
  const ex = nfExcludedSet_();
  const stake = nfStakeoutIdSetFromRawText_(data?.rawText);
  const out = [];
  for(const st of (Array.isArray(data?.topoStations) ? data.topoStations : [])){
    const obs = Array.isArray(st?.observations)
      ? st.observations.filter(o=>{
          const id = nfPid_(o?.id);
          const b = nfBasePid_(id);
          return (!id || !ex.has(id)) && (!b || !stake.has(b));
        })
      : [];
    const res = Array.isArray(st?.results)
      ? st.results.filter(r=>{
          const id = nfPid_(r?.id);
          const b = nfBasePid_(id);
          return (!id || !ex.has(id)) && (!b || !stake.has(b));
        })
      : [];
    if((obs.length + res.length) === 0) continue;
    out.push({ ...st, observations: obs.map(o => ({ ...o, id: nfBasePid_(o?.id) || nfPid_(o?.id) })), results: res.map(r => ({ ...r, id: nfBasePid_(r?.id) || nfPid_(r?.id) })) });
  }
  return out;
}

function nfFilterLigneRef_(ligneRef, excludedSet){
  const ex = excludedSet || nfExcludedSet_();
  const out = [];
  for(const lr of (Array.isArray(ligneRef)?ligneRef:[])){
    const pts = nfFilterPointsById_(Array.isArray(lr?.rabPoints)?lr.rabPoints:[], ex).map(p=>Object.assign({__nfSource:'LR'}, p || {}));
    if(!pts.length) continue;
    out.push({ ...lr, rabPoints: pts });
  }
  return out;
}

function applyReportButtonStateFromData(data){
  try{
    const exImplr = nfExcludedForImplantLr_();
    const ex = nfExcludedSet_();
    const impCount = nfFilterPointsById_(Array.isArray(data?.implantation?.points) ? data.implantation.points : [], exImplr).length;
    const lrFlat = Array.isArray(data?.ligneRef)
      ? data.ligneRef.flatMap(lr => Array.isArray(lr?.rabPoints) ? lr.rabPoints : [])
      : [];
    const lrCount = nfFilterPointsById_(lrFlat, exImplr).length;

    const topoStationsFiltered = nfFilterTopoStationsForLeve_(data);
    const topoCount = topoStationsFiltered.reduce((acc, st)=> acc + (st?.observations?.length||0) + (st?.results?.length||0), 0);
    const hasStation = computeHasStation(data);

    const bImp = document.getElementById("btnPdfIntervention");
    if(bImp) bImp.disabled = impCount === 0;
    const bImpPs = document.getElementById("btnPdfInterventionPdfSharp");
    if(bImpPs) bImpPs.disabled = impCount === 0;

    const bLr = document.getElementById("btnPdfLigneRef");
    if(bLr) bLr.disabled = lrCount === 0;

    const bSta = document.getElementById("btnPdfStation");
    if(bSta) bSta.disabled = !hasStation;

    const bPts = document.getElementById("btnPdfPointsTopo");
    if(bPts) bPts.disabled = topoCount === 0;
    const bHt = document.getElementById("btnPdfHeightTransfer");
    if(bHt) bHt.disabled = !(Array.isArray(data?.heightTransfers) && data.heightTransfers.length > 0);

    // TXT exports (points)
    const bExpImp = document.getElementById("btnExportImplantTxt");
    if(bExpImp) bExpImp.disabled = (impCount + lrCount) === 0;
    const bExpLeve = document.getElementById("btnExportLeveTxt");
    if(bExpLeve) bExpLeve.disabled = topoCount === 0;

    // Full report requires a station + at least one data block (Implantation or Ligne de référence)
    const fullOk = hasStation && (impCount > 0 || lrCount > 0);
    const bFull = document.getElementById("btnPdfFull");
    if(bFull) bFull.disabled = !fullOk;
    const bFullPs = document.getElementById("btnPdfCompletPdfSharp");
    if(bFullPs) bFullPs.disabled = !fullOk;

    const bRecalc = document.getElementById("btnRecalc");
    if(bRecalc) bRecalc.disabled = false;
  }catch(_){/* ignore */}
}
window.applyReportButtonStateFromData = applyReportButtonStateFromData;

function nfDlTextFile_(fileName, content){
  try{
    let name = String(fileName || "export.txt").replace(/[\\/:*?\"<>|]+/g, "_").trim();
    if(!name.toLowerCase().endsWith(".txt")) name = name.replace(/\.[^.]+$/,"") + ".txt";
    const blob = new Blob([String(content||"")], { type: "text/plain;charset=utf-8" });

    // Preferred: go through the shared save helper (WebView2-friendly)
    if(typeof window.saveBlobAs === "function"){
      return window.saveBlobAs(blob, name, "text/plain");
    }

    // Fallback: browser download
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    document.body.appendChild(a);
    a.click();
    setTimeout(()=>{ try{ URL.revokeObjectURL(a.href); }catch(_){ } try{ a.remove(); }catch(_){ } }, 0);
  }catch(err){
    console.error(err);
    setStatus("Export TXT impossible", true);
  }
}


function nfFmt3_(v){
  const n = Number(v);
  return Number.isFinite(n) ? n.toFixed(3) : "";
}

function nfToday_(){
  try{
    const d = new Date();
    const y = d.getFullYear();
    const m = String(d.getMonth()+1).padStart(2,'0');
    const da = String(d.getDate()).padStart(2,'0');
    return `${y}.${m}.${da}`;
  }catch(_){
    return "";
  }
}


function nfBuildPointsExportName_(token){
  try{
    const R = (typeof rf === "function") ? (rf() || {}) : {};
    const pick = (...keys) => {
      for(const k of keys){
        const v = (R && R[k] != null) ? String(R[k]).trim() : "";
        if(v) return v;
      }
      return "";
    };
    const sanitize = (s) => {
      s = String(s || "").trim();
      if(!s) return "";
      try{ s = s.normalize("NFD").replace(/[\u0300-\u036f]/g, ""); }catch(_){ }
      s = s.replace(/[^0-9a-zA-Z_\-\s]/g, "");
      s = s.replace(/[\s\-]+/g, "");
      return s;
    };

    let vill = sanitize(pick("ville","Ville","city","City")).toUpperCase();
    vill = vill ? vill.substring(0,4) : "NA";

    let phase = sanitize(pick("phase","Phase")).toUpperCase();
    if(!phase) phase = "NA";

    let indice = sanitize(pick("indice","Indice","index","Index")).toUpperCase();
    if(!indice) indice = "NA";

    let tok = sanitize(token).toUpperCase();
    if(!tok) tok = "POINTS";

    const date = nfToday_() || "";
    return `NOVA_${vill}_${phase}_${tok}_${indice}_${date}.txt`;
  }catch(_){
    return `NOVA_POINTS_${nfToday_()}.txt`;
  }
}

function nfPointNo_(p){
  return String(p?.no ?? p?.numero ?? p?.id ?? p?.name ?? p?.pointId ?? "").trim();
}
function nfPointXYZ_(p){
  // Normalise XYZ across various internal shapes:
  // - Implantation points: { mes:{E,N,H}, theo:{E,N,H} }
  // - LigneRef rabPoints: { mes:{E,N,H}, calc:{E,N,H} }
  // - Levé rows: often direct {E,N,H} or {east,north,height} or {x,y,z}
  const pickENH = (o) => {
    if(!o) return {x:null,y:null,z:null};
    const x = (o.E ?? o.e ?? o.east ?? o.East ?? o.X ?? o.x ?? o.cx ?? o.centreX);
    const y = (o.N ?? o.n ?? o.north ?? o.North ?? o.Y ?? o.y ?? o.cy ?? o.centreY);
    const z = (o.H ?? o.h ?? o.Z ?? o.z ?? o.height ?? o.Height ?? o.cz ?? o.centreZ);
    return { x, y, z };
  };

  // Priority: measured -> computed -> theoretical -> direct
  let xyz = pickENH(p?.mes);
  if(xyz.x==null && xyz.y==null) xyz = pickENH(p?.calc);
  if(xyz.x==null && xyz.y==null) xyz = pickENH(p?.theo);
  if(xyz.x==null && xyz.y==null){
    xyz = pickENH(p);
  }
  return xyz;
}


function exportPointsTxt(kind){
  try{
    const data = (window.__NF_LASTDATA || window.lastData || (typeof lastData !== 'undefined' ? lastData : null));
    if(!data){ setStatus("Aucune donnée chargée", true); return; }

    const k = String(kind||"").toLowerCase();
    const ex = nfExcludedForImplantLr_();
    const lines = [];

    if(k === 'implant_lr'){
      const impPts = nfFilterPointsById_(Array.isArray(data?.implantation?.points) ? data.implantation.points : [], ex);
      const lrFlat = Array.isArray(data?.ligneRef)
        ? data.ligneRef.flatMap(lr => Array.isArray(lr?.rabPoints) ? lr.rabPoints : [])
        : [];
      const lrPts = nfFilterPointsById_(lrFlat, ex);

      const seen = new Set();
      for(const p of [...impPts, ...lrPts]){
        const no = nfPointNo_(p);
        if(!no || seen.has(no)) continue;
        const xyz = nfPointXYZ_(p);
        lines.push([no, nfFmt3_(xyz.x), nfFmt3_(xyz.y), nfFmt3_(xyz.z)].join('\t'));
        seen.add(no);
      }

      const fn = (typeof buildExportFileName === 'function')
        ? nfBuildPointsExportName_('PTS_IMP_LR')
        : ('NOVA_Points_Implant_LR_' + nfToday_() + '.txt');
      nfDlTextFile_(fn, lines.join('\n'));
      return;
    }

    if(k === 'leve'){
      const topoStationsLeve = nfFilterTopoStationsForLeve_(data);
      const seen = new Set();
      for(const st of topoStationsLeve){
        const rows = Array.isArray(st?.results) ? st.results : [];
        for(const r of rows){
          const no = nfPointNo_(r);
          if(!no || seen.has(no)) continue;
          const xyz = nfPointXYZ_(r);
          // require at least X/Y
          if(!Number.isFinite(Number(xyz.x)) || !Number.isFinite(Number(xyz.y))) continue;
          lines.push([no, nfFmt3_(xyz.x), nfFmt3_(xyz.y), nfFmt3_(xyz.z)].join('\t'));
          seen.add(no);
        }
      }
      const fn = (typeof buildExportFileName === 'function')
        ? nfBuildPointsExportName_('PTS_LEVE_TOPO')
        : ('NOVA_Points_Leve_' + nfToday_() + '.txt');
      nfDlTextFile_(fn, lines.join('\n'));
      return;
    }

    setStatus("Export TXT: type inconnu", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur export TXT", true);
  }
}
window.exportPointsTxt = exportPointsTxt;

function collectAllImplantPoints(data){
  try{
    const arr = Array.isArray(data?.implantation?.points) ? data.implantation.points : [];
    return arr.map(p => Object.assign({__nfSource:'IMP'}, p || {}));
  }catch(_){ return []; }
}

// Some LandXML files do not provide a reliable station identifier for stakeout points
// (e.g. missing timestamps on points, or ApplicationStartDateTime not matching inferred station runs).
// Our PdfSharp implantation renderer groups rows per stationId; if stationId is missing we can end up
// with a non-empty UI list but an empty PDF table for each station. To keep legacy behavior, if we
// have a single station run we attach orphan stakeout points to that station.
function nfFixMissingImplantStationIds_(points, data){
  try{
    const arr = Array.isArray(points) ? points : [];
    if(arr.length === 0) return arr;
    const runs = (data && Array.isArray(data.stationLibreRuns)) ? data.stationLibreRuns : [];
    // Some LandXML exports (e.g. 260130...) provide stationLibre (single) but no stationLibreRuns.
    // PdfSharp implantation report groups by stationId, so we must ensure points are attached.
    const stSingle = (data && data.stationLibre && typeof data.stationLibre === 'object') ? data.stationLibre : null;

    const firstId = (runs.length === 1 && runs[0]?.results?.idStation) ? String(runs[0].results.idStation)
                 : (runs.length === 1 && runs[0]?.idStation) ? String(runs[0].idStation)
                 : (runs.length === 0 && stSingle?.results?.idStation) ? String(stSingle.results.idStation)
                 : (runs.length === 0 && stSingle?.idStation) ? String(stSingle.idStation)
                 : "";
    if(!firstId) return arr;
    // shallow-clone the objects we touch to avoid side effects on UI
    return arr.map(p => {
      const sid = p?.stationId;
      if(sid != null && String(sid).trim() !== "") return p;
      const cp = (p && typeof p === 'object') ? { ...p } : p;
      if(cp && typeof cp === 'object') cp.stationId = firstId;
      return cp;
    });
  }catch(_){ return Array.isArray(points) ? points : []; }
}

function updatePdfSharpButtonState(){
  try{
    const okHost = !!lastData && isPdfSharpAvailable();
    const b1 = document.getElementById("btnPdfInterventionPdfSharp");
    if(b1){
      // Preserve content-based disabling (set elsewhere) and only enforce host capability.
      b1.disabled = b1.disabled || !okHost;
      b1.title = (!okHost)
        ? (!isPdfSharpAvailable() ? "PdfSharp indisponible (moteur non chargé côté C#)." : "Import LandXML requis.")
        : (b1.disabled ? "Aucune donnée Implantation détectée dans ce LandXML." : "Génère le PDF Implantation via PdfSharp (moteur C#)");
    }
    const b2 = document.getElementById("btnPdfCompletPdfSharp");
    if(b2){
      b2.disabled = b2.disabled || !okHost;
      b2.title = (!okHost)
        ? (!isPdfSharpAvailable() ? "PdfSharp indisponible (moteur non chargé côté C#)." : "Import LandXML requis.")
        : (b2.disabled ? "Rapport complet indisponible : données insuffisantes (Station + Implantation/Ligne de réf)." : "Génère le Rapport complet via PdfSharp (moteur C#)");
    }

    // Points topo is also generated via PdfSharp host
    const b3 = document.getElementById("btnPdfPointsTopo");
    if(b3){
      b3.disabled = b3.disabled || !okHost;
      b3.title = (!okHost)
        ? (!isPdfSharpAvailable() ? "PdfSharp indisponible (moteur non chargé côté C#)." : "Import LandXML requis.")
        : (b3.disabled ? "Aucun point topo (levé) détecté dans ce LandXML." : "Génère le PDF Points topo (levé) via PdfSharp (moteur C#)");
    }
  }catch(_){/* ignore */}
}
window.updatePdfSharpButtonState = updatePdfSharpButtonState;

document.getElementById("btnPdfInterventionPdfSharp")?.addEventListener("click", async () => {
  try{
    const pdfOverride = (window.__NF_IMPLANTATION_PDF_OVERRIDE && typeof window.__NF_IMPLANTATION_PDF_OVERRIDE === "object")
      ? window.__NF_IMPLANTATION_PDF_OVERRIDE
      : null;
    try{ delete window.__NF_IMPLANTATION_PDF_OVERRIDE; }catch(_){ window.__NF_IMPLANTATION_PDF_OVERRIDE = null; }
    if(!lastData){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }
    if(!isPdfSharpAvailable()){ setStatus("PdfSharp indisponible côté C#.", true); return; }

    const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";
    const R = (typeof rf === "function") ? rf() : {};
    const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };
    const ex = nfExcludedForImplantLr_();
    const allImpPtsRaw = collectAllImplantPoints(lastData);
    const allImpPtsFixed = nfFixMissingImplantStationIds_(allImpPtsRaw, lastData);
    const allImpPts = nfFilterPointsById_(allImpPtsFixed, ex);
    const bodyImp = allImpPts.map(rowFromPoint);

    const implantationByStation = nfBuildImplantationByStationPayload_(allImpPts, lastData);
    const implantationStationIds = nfCollectUsedStationIdsFromPayload_(implantationByStation);
    const implantationRuns = nfFilterStationLibreRunsByStationIds_(lastData?.stationLibreRuns, implantationStationIds);

    const payload = {
      type: "pdfsharp_implantation",
      title: pdfOverride?.title || "IMPLANTATION",
      planView: pdfOverride?.planView || null,
      subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
      header: ["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"],
      rows: bodyImp,
      fileName: (typeof buildExportFileName === "function")
        ? buildExportFileName(pdfOverride?.fileNameType || "IMPLANTATION", "PDF")
        : "NOVA_Implantation_PdfSharp.pdf"
,
// Extra fields for PdfSharp full report (best-effort)
elements: (R && R.elements) ? R.elements : "",
entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : "",
ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
intervenant: getAutoIntervenant(R),
systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      // Instrument model + serial (LandXML: InstrumentDetails model="..." serialNumber="...")
      model: String((lastData && lastData.meta && lastData.meta.instrument) ? lastData.meta.instrument : "").trim(),
      serialNumber: String((lastData && lastData.meta && lastData.meta.serial) ? lastData.meta.serial : "").trim(),
      // Backward compatible field used by older PdfSharp renderers
      appareil: (()=>{
        const r = (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "";
        if(r) return r;
        const inst = String((lastData && lastData.meta && lastData.meta.instrument) ? lastData.meta.instrument : "").trim();
        return inst;
      })(),
      ...nfPdfInstrumentMeta_(lastData, R),
      ville: (R && R.ville) ? R.ville : "",
adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : "",
cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : "",

      // Validation / tolérances / observations (pour bloc bas de page PDF)
      validation: {
        statutOn: !!O.tolOn,
        tolXYOn: !!O.xyOn,
        tolZOn: !!O.zOn,
        tolXY: isFinite(O.tXY) ? O.tXY : null,
        tolZ: isFinite(O.tZ) ? O.tZ : null,
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
      },

      // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
      // NOTE: Les renderers PdfSharp lisent ces valeurs au niveau racine (compat).
      obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Intervenant: getAutoIntervenant(R),

      // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
      signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
      signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

      // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
      signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
      signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

stationLibre: (lastData && lastData.stationLibre) ? lastData.stationLibre : null,
      stationLibreRuns: nfSanitizeStationRunsForPdf_(implantationRuns),
      implantationByStation,
      groupByZone: nfPdfGroupByZoneEnabled_(),
      zoneLabels: nfGetZoneRenameMap(),
      refAltiPoints: nfCollectRefAltiPoints_(lastData)
    };

    if(pdfOverride && Object.prototype.hasOwnProperty.call(pdfOverride, "subTitle")){
      payload.subTitle = String(pdfOverride.subTitle || "");
    }

    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
      window.chrome.webview.postMessage(payload);
    else
      setStatus("WebView2 host indisponible (postMessage).", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF PdfSharp (implantation).", true);
    try{ if(window.chrome?.webview?.postMessage) window.chrome.webview.postMessage({type:"ui_error", message: String(err)}); }catch(_){ }
  }
});

// ===== PdfSharp TEST: Rapport complet (cover + tables) =====
document.getElementById("btnPdfCompletPdfSharp")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }
    const R = (typeof rf === "function") ? rf() : {};
    const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };
    const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";

    // ---------- Build Implantation payload (same structure as btnPdfInterventionPdfSharp) ----------
    const ex = nfExcludedForImplantLr_();
    const allImpPtsRaw = collectAllImplantPoints(lastData);
    const allImpPtsFixed = nfFixMissingImplantStationIds_(allImpPtsRaw, lastData);
    const allImpPts = nfFilterPointsById_(allImpPtsFixed, ex);
    const bodyImp = allImpPts.map(p => rowFromPoint(p, lastData));

    const implantationByStation = nfBuildImplantationByStationPayload_(allImpPts, lastData);
    const implantationStationIds = nfCollectUsedStationIdsFromPayload_(implantationByStation);
    const implantationRuns = nfFilterStationLibreRunsByStationIds_(lastData?.stationLibreRuns, implantationStationIds);

    const impPayload = {
      type: "pdfsharp_implantation",
      title: "IMPLANTATION",
      subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
      header: ["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"],
      rows: bodyImp,
      implantationByStation,
      groupByZone: nfPdfGroupByZoneEnabled_(),
      zoneLabels: nfGetZoneRenameMap(),
      stationLibreRuns: nfSanitizeStationRunsForPdf_(implantationRuns),
      refAltiPoints: nfCollectRefAltiPoints_(lastData),

      // Cartouche / meta (same keys as other PdfSharp reports)
      // En PDF levé, on conserve le cartouche commun mais le libellé central doit être
      // celui du rapport, pas l'intitulé Implantation du projet.
      elements: "POINTS TOPO (LEVÉ)",
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : "",
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
      intervenant: getAutoIntervenant(R),
      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      // Instrument model + serial (LandXML: InstrumentDetails model="..." serialNumber="...")
      model: String(lastData?.meta?.instrument || "").trim(),
      serialNumber: String(lastData?.meta?.serial || "").trim(),
      appareil: (()=>{
        const r = (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "";
        if(r) return r;
        const inst = String(lastData?.meta?.instrument || "").trim();
        return inst;
      })(),
      ...nfPdfInstrumentMeta_(lastData, R),
      ville: (R && R.ville) ? R.ville : "",
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : "",
      cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : "",

      validation: {
        statutOn: !!O.tolOn,
        tolXYOn: !!O.xyOn,
        tolZOn: !!O.zOn,
        tolXY: isFinite(O.tXY) ? O.tXY : null,
        tolZ: isFinite(O.tZ) ? O.tZ : null,
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
      },
    };

    // ---------- Build Ligne de référence payload (same as btnPdfLigneRef) ----------
    const filteredLrForFull = nfFilterLigneRef_(Array.isArray(lastData?.ligneRef) ? lastData.ligneRef : [], nfExcludedSet_());
    const ligneRefRowsByStation = nfBuildLigneRefRowsByStationPayload_(filteredLrForFull, lastData);
    const ligneRefStationIds = nfCollectUsedStationIdsFromPayload_(ligneRefRowsByStation);
    const ligneRefRuns = nfFilterStationLibreRunsByStationIds_(lastData?.stationLibreRuns, ligneRefStationIds);

    const lrPayload = {
      type: "pdfsharp_ligne_reference",
      title: "LIGNE DE RÉFÉRENCE",
      subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
      elements: (R && R.elements) ? R.elements : "",
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem) : "",
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
      intervenant: getAutoIntervenant(R),

      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      appareil: (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "",
      ...nfPdfInstrumentMeta_(lastData, R),
      ville: (R && R.ville) ? R.ville : "",
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.address)) ? (R.adresseChantier || R.adresse || R.address) : "",
      cha: (R && (R.cha || R.numCha || R.noCha)) ? (R.cha || R.numCha || R.noCha) : "",
      refAltiPoints: nfCollectRefAltiPoints_(lastData),

      validation: {
        statutOn: !!O?.tolOn ? !!document.getElementById("optStatut")?.checked : !!document.getElementById("optStatut")?.checked,
        tolXYOn: !!document.getElementById("tolXYOn")?.checked,
        tolZOn: !!document.getElementById("tolZOn")?.checked,
        tolXY: Number(document.getElementById("tolXY")?.value || 0),
        tolZ: Number(document.getElementById("tolZ")?.value || 0),
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
      },

      // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
      obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Intervenant: getAutoIntervenant(R),

      // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
      signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
      signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

      stationLibreRuns: nfSanitizeStationRunsForPdf_(ligneRefRuns),
      ligneRef: filteredLrForFull,
      ligneRefRowsByStation,
      groupByZone: nfPdfGroupByZoneEnabled_(),
      zoneLabels: nfGetZoneRenameMap(),
    };

    // Cover info block (key/value)
    const info = {
      "Nom intervention": R.nomIntervention || "",
      "Ville": R.ville || "",
      "Adresse": R.adresse || "",
      "CHA": R.cha || "",
      "Phase": R.phase || "",
      "Type": (R.typeDoc || R.type || ""),
      "Indice": R.indice || "",
      "Entreprise": (R.client || R.entreprise || ""),
      "Client": R.client || "",
      "Intervenant": R.intervenant || "",
      "Date": R.dateIntervention || ""
    };

    const fileName = (typeof buildExportFileName === "function")
      ? buildExportFileName("", "pdf")
      : `${pdfFileBase(R)}_RapportComplet_PdfSharp.pdf`;

    if(window.chrome?.webview){
      window.chrome.webview.postMessage({
        type: "pdfsharp_rapport_complet_v2",
        fileName,
        info,
        implantationPayload: impPayload,
        ligneRefPayload: lrPayload
      });
      setStatus("Génération PDF (PdfSharp) …");
    } else {
      setStatus("Mode PdfSharp indisponible (WebView2 host)", true);
      showErrorDialog("Mode PdfSharp indisponible (WebView2 host)");
    }
  }catch(e){
    console.error(e);
    setStatus("Erreur PdfSharp : " + (e?.message || String(e)), true);
    showErrorDialog("Erreur PdfSharp : " + (e?.message || String(e)));
  }
});
// =====================================================
// exportPdf — point d'entrée unique (appelé par les boutons)
// =====================================================
// NOTE: lastData + setStatus() sont définis dans m01_core.js.

async function buildPdfPointsTopo(data){
  try{
    if(!data) throw new Error("Aucune donnée (import LandXML requis)");
    if(!isPdfSharpAvailable()) throw new Error("PdfSharp indisponible côté C#.");

    const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";
    const R = (typeof rf === "function") ? rf() : {};
    const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };

    // Règle métier NOVATLAS:
    // - pas de points issus d'un programme Stakeout (implantation)
    // - conserver les observations polaires + résultats rectangulaires
    //   (data.topoStations) pour le LEVÉ.
    const topoStationsLeve = nfFilterTopoStationsForLeve_(data);

    const payload = {
      type: "pdfsharp_points_topo",
      title: "LEVÉ",
      // The PdfSharp renderer expects this key and will default to IMPLANTATION if not provided.
      sectionImplantationTitle: "LEVÉ",
      // Observations polaires + résultats rectangulaires (topoStations) filtrés (sans Stakeout)
      topoStations: topoStationsLeve,
      subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
      header: ["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"],
      // IMPORTANT: ne pas injecter de points d'implantation dans le LEVÉ.
      rows: [],
      fileName: (typeof buildExportFileName === "function")
        ? buildExportFileName("LEVE", "PDF")
        : "NOVA_Leve_PdfSharp.pdf",

      // Cartouche / meta (same keys as Implantation PdfSharp)
      elements: (R && R.elements) ? R.elements : "",
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : "",
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
      intervenant: getAutoIntervenant(R),
      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      model: String((data && data.meta && data.meta.instrument) ? data.meta.instrument : "").trim(),
      serialNumber: String((data && data.meta && data.meta.serial) ? data.meta.serial : "").trim(),
      appareil: (()=>{
        const r = (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "";
        if(r) return r;
        const inst = String((data && data.meta && data.meta.instrument) ? data.meta.instrument : "").trim();
        return inst;
      })(),
      ...nfPdfInstrumentMeta_(data, R),
      ville: (R && R.ville) ? R.ville : "",
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : "",
      cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : "",

      validation: {
        statutOn: !!O.tolOn,
        tolXYOn: !!O.xyOn,
        tolZOn: !!O.zOn,
        tolXY: isFinite(O.tXY) ? O.tXY : null,
        tolZ: isFinite(O.tZ) ? O.tZ : null,
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
      },

      // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
      // NOTE: Les renderers PdfSharp lisent ces valeurs au niveau racine (compat).
      obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Intervenant: getAutoIntervenant(R),

      stationLibre: (data && data.stationLibre) ? data.stationLibre : null,
      stationLibreRuns: nfSanitizeStationRunsForPdf_((data && Array.isArray(data.stationLibreRuns)) ? data.stationLibreRuns : []),
      implantationByStation: [],
      refAltiPoints: nfCollectRefAltiPoints_(data)
    };

    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
      window.chrome.webview.postMessage(payload);
    else
      setStatus("WebView2 host indisponible (postMessage).", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF Points topo : " + (err?.message || err), true);
  }
}

async function buildPdfHeightTransfer(data){
  try{
    if(!data) throw new Error("Aucune donnée (import LandXML requis)");
    if(!isPdfSharpAvailable()) throw new Error("PdfSharp indisponible côté C#.");
    const transfers = Array.isArray(data?.heightTransfers) ? data.heightTransfers : [];
    if(!transfers.length) throw new Error("Aucun transfert d'altitude détecté.");
    const R = (typeof rf === "function") ? rf() : {};
    const payload = {
      type: "pdfsharp_height_transfer",
      title: "TRANSFERT D'ALTITUDE",
      fileName: (typeof buildExportFileName === "function") ? buildExportFileName("TRANSFERT_ALTITUDE", "PDF") : "NOVA_Transfert_Altitude.pdf",
      heightTransfers: transfers,
      elements: (R && R.elements) ? R.elements : "",
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : "",
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
      intervenant: getAutoIntervenant(R),
      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.reference || R.plan) : "",
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      model: String(data?.meta?.instrument || "").trim(),
      serialNumber: String(data?.meta?.serial || "").trim(),
      appareil: (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : String(data?.meta?.instrument || "").trim(),
      ...nfPdfInstrumentMeta_(data, R),
      ville: (R && R.ville) ? R.ville : "",
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : "",
      cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : "",
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Intervenant: getAutoIntervenant(R)
    };
    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
      window.chrome.webview.postMessage(payload);
    else
      setStatus("WebView2 host indisponible (postMessage).", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF Transfert altitude : " + (err?.message || err), true);
  }
}

async function exportPdf(kind){
  try{
    // Robustesse: selon l'ordre de chargement / refactor, les donnees peuvent etre stockees
    // dans `lastData` (var globale) OU dans `window.lastData` OU `window.__NF_LASTDATA`.
    const data = (window.__NF_LASTDATA || window.lastData || (typeof lastData !== 'undefined' ? lastData : null));

    if(!data){
      setStatus("Aucune donnée à exporter (import TXT requis).", true);
      showErrorDialog("Aucune donnée à exporter (import TXT requis).");
      return;
    }

    const k = String(kind || "").toLowerCase();
    if(k === "implantation" || k === "intervention") return await buildPdfIntervention(data);
    if(k === "ligne")        return await buildPdfLigneRef(data);
    if(k === "station"){
      // PdfSharp is now the official engine for Station as well. If available, use it.
      if(isPdfSharpAvailable()){
        const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";
        const R = (typeof rf === "function") ? rf() : {};
        const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };

        const payload = {
          type: "pdfsharp_station",
          title: "STATION",
          subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
          fileName: (typeof buildExportFileName === "function")
            ? buildExportFileName("STATION", "PDF")
            : "NOVA_Station_PdfSharp.pdf",

          // Cartouche / meta
          nomIntervention: "STATION",
          entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
          contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
          systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem) : "",
          ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
          intervenant: getAutoIntervenant(R),
          systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
          planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
          date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
          appareil: (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "",
          serialNumber: (R && (R.serialNumber || R.numeroSerie || R.serial)) ? (R.serialNumber || R.numeroSerie || R.serial) : "",
          ...nfPdfInstrumentMeta_(data, R),
          ville: (R && R.ville) ? R.ville : "",
          adresse: (R && (R.adresseChantier || R.adresse || R.address)) ? (R.adresseChantier || R.adresse || R.address) : "",
          cha: (R && (R.cha || R.numCha || R.noCha)) ? (R.cha || R.numCha || R.noCha) : "",

          validation: {
            statutOn: !!document.getElementById("optStatut")?.checked,
            tolXYOn: !!document.getElementById("tolXYOn")?.checked,
            tolZOn: !!document.getElementById("tolZOn")?.checked,
            tolXY: Number(document.getElementById("tolXY")?.value || 0),
            tolZ: Number(document.getElementById("tolZ")?.value || 0),
            observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
          },

          // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
          obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
          observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
          Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
          surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Intervenant: getAutoIntervenant(R),

          // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
          signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
          signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

          // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
          signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
          signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

          // Core data
          stationLibreRuns: nfSanitizeStationRunsForPdf_(Array.isArray(data?.stationLibreRuns) ? data.stationLibreRuns : (Array.isArray(lastData?.stationLibreRuns) ? lastData.stationLibreRuns : [])),
          refAltiPoints: nfCollectRefAltiPoints_(data),
        };
        payload.stationLibre = Array.isArray(payload.stationLibreRuns) && payload.stationLibreRuns.length ? payload.stationLibreRuns[0] : null;

        if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
          window.chrome.webview.postMessage(payload);

        return;
      }
      


return await buildPdfStationOnly(data);
    }
    if(k === "station_txt")  return await buildPdfStationPlusPoints(data, (window.nfExchange&&window.nfExchange.txt?window.nfExchange.txt.points:[]), "TXT");
    if(k === "station_gsi"){
      const gsi = (window.nfExchange && window.nfExchange.gsi) ? window.nfExchange.gsi : null;
      const gPts = gsi && Array.isArray(gsi.points) ? gsi.points : [];
      const gObs = gsi && Array.isArray(gsi.observations) ? gsi.observations : [];
      const mode = gsi ? (gsi.gsiMode || null) : null;

      // Étape 4.2 (sans calcul) : si le GSI est en mode observations, on sort un PDF avec la table d'observations.
      if(mode === 'obs' && gObs.length){
        return await buildPdfStationPlusObservations(data, gObs);
      }
      return await buildPdfStationPlusPoints(data, gPts, "GSI");
    }
    if(k === "pointstopo")      return await buildPdfPointsTopo(data);
    if(k === "heighttransfer")  return await buildPdfHeightTransfer(data);
    if(k === "complet")      return await buildPdfComplet(data);

    setStatus("Type de PDF inconnu : " + kind, true);
    showErrorDialog("Type de PDF inconnu : " + kind);
  }catch(e){
    try{ setStatus("Erreur PDF : " + (e?.message || e)); }catch(_){ }
    console.error(e);
    setStatus("Erreur PDF : " + (e?.message || e), true);
    showErrorDialog("Erreur PDF : " + (e?.message || e));
  }
}

// exposer pour les appels depuis d'autres scripts / debug console
window.exportPdf = exportPdf;

// call once before wiring listeners
ensureStationButton();
document.getElementById("btnPdfIntervention")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import TXT)", true); return; }
    await exportPdf("intervention");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});

document.getElementById("btnPdfLigneRef")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }

    // PdfSharp is now the official engine. If available, use it.
    if(isPdfSharpAvailable()){
      const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";
      const R = (typeof rf === "function") ? rf() : {};
      const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };

      const filteredLr = nfNormalizeLigneRefStationIds_(nfFilterLigneRef_(Array.isArray(lastData?.ligneRef) ? lastData.ligneRef : [], nfExcludedForImplantLr_()), lastData);
      const ligneRefRowsByStation = nfBuildLigneRefRowsByStationPayload_(filteredLr, lastData);
      const ligneRefStationIds = nfCollectUsedStationIdsFromPayload_(ligneRefRowsByStation);
      const ligneRefRuns = nfFilterStationLibreRunsByStationIds_(lastData?.stationLibreRuns, ligneRefStationIds);

      // Keep only station runs réellement liés à la fiche ligne de réf (source of truth is JSON -> C#)
      const payload = {
        type: "pdfsharp_ligne_reference",
        title: "LIGNE DE RÉFÉRENCE",
        subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
        fileName: (typeof buildExportFileName === "function")
          ? buildExportFileName("LIGNEREF", "PDF")
          : "NOVA_LigneDeReference_PdfSharp.pdf",

        // Cartouche / meta
        elements: (R && R.elements) ? R.elements : "",
        entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
        contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
        systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem) : "",
        ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
        intervenant: getAutoIntervenant(R),

        systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
        planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
        date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
        appareil: (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "",
        ...nfPdfInstrumentMeta_(lastData, R),
        ville: (R && R.ville) ? R.ville : "",
        adresseChantier: (R && (R.adresseChantier || R.adresse || R.address)) ? (R.adresseChantier || R.adresse || R.address) : "",
        cha: (R && (R.cha || R.numCha || R.noCha)) ? (R.cha || R.numCha || R.noCha) : "",

        // Validation bloc (same keys as IMP)
        validation: {
          statutOn: !!O?.tolOn ? !!document.getElementById("optStatut")?.checked : !!document.getElementById("optStatut")?.checked,
          tolXYOn: !!document.getElementById("tolXYOn")?.checked,
          tolZOn: !!document.getElementById("tolZOn")?.checked,
          tolXY: Number(document.getElementById("tolXY")?.value || 0),
          tolZ: Number(document.getElementById("tolZ")?.value || 0),
          observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
        },

        // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
        obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
        surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
        geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Intervenant: getAutoIntervenant(R),

          // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
          signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
          signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

        // Core data
        stationLibreRuns: nfSanitizeStationRunsForPdf_(ligneRefRuns),
        refAltiPoints: nfCollectRefAltiPoints_(lastData),
        ligneRef: filteredLr,
        ligneRefRowsByStation,
        groupByZone: nfPdfGroupByZoneEnabled_(),
        zoneLabels: nfGetZoneRenameMap(),
      };

      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
        window.chrome.webview.postMessage(payload);

      return;
    }

    // Fallback (should not be used anymore, but keep a message)
    setStatus("PdfSharp indisponible côté C#. Impossible de générer le PDF.", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF", true);
    setStatus(String(err?.message || err), true);
    showErrorDialog(String(err?.message || err));
  }
});

document.getElementById("btnPdfStation")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import TXT)", true); return; }
    await exportPdf("station");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});



document.getElementById("btnPdfPointsTopo")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }
    await exportPdf("pointstopo");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});
document.getElementById("btnPdfHeightTransfer")?.addEventListener("click", async () => {
  try{
    const data = (window.__NF_LASTDATA || window.lastData || (typeof lastData !== 'undefined' ? lastData : null));
    if(!data){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }
    await exportPdf("heighttransfer");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF Transfert altitude: " + (err?.message || String(err)), true);
  }
});

document.getElementById("btnExportImplantTxt")?.addEventListener("click", () => {
  exportPointsTxt('implant_lr');
});

document.getElementById("btnExportLeveTxt")?.addEventListener("click", () => {
  exportPointsTxt('leve');
});

document.getElementById("btnPdfStationPlusTxt")?.addEventListener("click", async () => {
  try{
    const tPts = Array.isArray(window.nfExchange?.txt?.points) ? window.nfExchange.txt.points : [];
    if(!lastData){ setStatus("Veuillez importer un fichier AppLog avant de générer ce PDF.", true); return; }
    if(tPts.length===0){ setStatus("Aucun fichier TXT n’est chargé. Importez un fichier TXT (points XYZC).", true); return; }
    await exportPdf("station_txt");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});

document.getElementById("btnPdfStationPlusGsi")?.addEventListener("click", async () => {
  try{
    const gPts = Array.isArray(window.nfExchange?.gsi?.points) ? window.nfExchange.gsi.points : [];
    const gObs = Array.isArray(window.nfExchange?.gsi?.observations) ? window.nfExchange.gsi.observations : [];
    if(!lastData){ setStatus("Veuillez importer un fichier AppLog avant de générer ce PDF.", true); return; }
    if(gPts.length===0 && gObs.length===0){ setStatus("Aucun fichier GSI n’est chargé. Importez un fichier GSI Leica.", true); return; }
    await exportPdf("station_gsi");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});



document.getElementById("btnPdfFull")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée", true); return; }
    if(typeof ensureFontsRegistered==="function"){ ensureFontsRegistered(); }
    // await exportPdf("complet") gère la création du document + le save()
    await exportPdf("complet");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});
refreshTolWarnings();
document.getElementById("btnRecalc")?.addEventListener("click", async () => {
  if(!lastData){ setStatus("Aucune donnée chargée (import TXT)", true); return; }
  refreshAll();
});

// Tabs
document.querySelectorAll(".tab[data-view]").forEach(el => {
  el.addEventListener("click", async () => {
    document.querySelectorAll(".tab[data-view]").forEach(t => t.classList.remove("active"));
    el.classList.add("active");
    const view = el.getAttribute("data-view");
    ["station","implant","lineref","refalti","topo","heighttransfer","raw"].forEach(v => {
      const node = document.getElementById("view_"+v);
      if(node) node.style.display = (v === view) ? "block" : "none";
    });
  });
});

window.NF_afterRefresh = function(){
  try{ nfRenderZoneLabelsEditor_(); }catch(_){ }
};

document.getElementById('pdfGroupByZone')?.addEventListener('change', ()=>{
  try{ nfRenderZoneLabelsEditor_(); }catch(_){ }
});

try{ nfRenderZoneLabelsEditor_(); }catch(_){ }

["tolXYOn","tolZOn","tolXY","tolZ","optTol","calcDzOn"].forEach(id=>{ const el=document.getElementById(id); if(el){ el.addEventListener("change", ()=>{ refreshTolWarnings(); syncDzUI(); if(lastData){ refreshAll(); } }); el.addEventListener("input", ()=>{ refreshTolWarnings(); }); }});
refreshTolWarnings();
syncDzUI();

setStatus("Prêt");


/* ===== PDF COMBINÉ (implantation + rabattement) ===== */
function buildPdfInterventionInto(doc, data){
  syncDzUI();
  applyDzPolicy(data);

  const R = rf();
  let y = drawHeaderV2(doc, R);
y = pdfStationLibreFull(doc, y, data);
  y = pdfBar(doc, y, "IMPLANTATION");
  const tolOn = document.getElementById("optTol").checked;
  const xyOn = document.getElementById("tolXYOn").checked;
  const zOn  = document.getElementById("tolZOn").checked;
  const tXY = Number(document.getElementById("tolXY").value);
  const tZ  = Number(document.getElementById("tolZ").value);
  const tolTxt = tolOn ? `Tolérances : X=${xyOn ? tXY : "—"} ; Y=${xyOn ? tXY : "—"} ; Z=${zOn ? tZ : "—"}` : `Tolérances désactivées`;
  y = pdfTolBar(doc, y, tolTxt);

  const body = (data.implantation?.points || []).map(rowFromPoint);

  autoTableResults(doc, {
    startY: y + 1,
    nfThickSpec: "implantation",
    head: [["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"]],
    body,
    margin: { left:10, right:10 },
    columnStyles: RESULTS_COLUMN_STYLES,
    styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
    headStyles: { fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
    didParseCell: function (dataCell) { applyStatusFitAutoTable(dataCell);
            // 1.7.5 : en-têtes en gras (ne pas écraser le style du header)
      if(dataCell && dataCell.section === "head"){
        try{ dataCell.cell.styles.fontStyle = "bold"; }catch(e){}
        return;
      }
try{
        const row = dataCell.row?.raw;
        if(!row) return;
        const xyOn = document.getElementById("tolXYOn").checked;
        const zOn  = document.getElementById("tolZOn").checked;
        const tXY = Number(document.getElementById("tolXY").value);
        const tZ  = Number(document.getElementById("tolZ").value);
        const dx = Number(String(row[7]).replace(",", "."));
        const dy = Number(String(row[8]).replace(",", "."));
        const dz = Number(String(row[9]).replace(",", "."));
        const badX = xyOn && Number.isFinite(dx) && Math.abs(dx) > tXY;
        const badY = xyOn && Number.isFinite(dy) && Math.abs(dy) > tXY;
        const badZ = zOn  && Number.isFinite(dz) && Math.abs(dz) > tZ;
        const col = dataCell.column.index;
        if((col===7 && badX) || (col===8 && badY) || (col===9 && badZ)){
          dataCell.cell.styles.fontStyle = "bold";
        }
      }catch(e){}
    },
    // IMPORTANT: no thick lines via autoTable hooks anymore (deterministic post-process)
  });

  // Last-page box only (ensure the final page count is known BEFORE stamping pagination)
  // This report may end with manual text blocks (no autoTable). Pass the current cursor Y
  // to avoid footer overlap.
  y = ensureRoomForLastFooter(doc, 95, y);
  const stats = computeControlStats(data.implantation.points);
  pdfFooterLastPageBox(doc, R, stats);

  // Address + pagination all pages (ONCE, at the very end to avoid overlaps)
  pdfFooterAllPagesAddress(doc);
}

function buildPdfLigneRefInto(doc, data){
  syncDzUI();
  applyDzPolicy(data);

  const R = rf();
  let y = drawHeaderV2(doc, R);
y = pdfStationLibreFull(doc, y, data);

  y = pdfBar(doc, y, "MESURE SUR LIGNE");
  const tolOn = document.getElementById("optTol").checked;
  const xyOn = document.getElementById("tolXYOn").checked;
  const zOn  = document.getElementById("tolZOn").checked;
  const tXY = Number(document.getElementById("tolXY").value);
  const tZ  = Number(document.getElementById("tolZ").value);
  const tolTxt = tolOn ? `Tolérances : X=${xyOn ? tXY : "—"} ; Y=${xyOn ? tXY : "—"} ; Z=${zOn ? tZ : "—"}` : `Tolérances désactivées`;
  y = pdfTolBar(doc, y, tolTxt);
let __ptIndex = 0;
const lrPoints = (data.ligneRef||[]).flatMap(lr=>lr.rabPoints||[]);


// Corps : pour chaque point -> N) (mesures), Rabattement (calc+delta), Ligne (dL/dT/dA)
const bodyLR = [];
lrPoints.forEach((p, idxPoint)=>{
  const id = (p.id || "").toString().trim();
  if(!id) return;

  // Alternance par point : même fond pour les 3 lignes (ID / Rabattement / Ligne)
  const __grp = (__ptIndex % 2);

  const xMes = (p.mes?.E ?? p.mes?.X ?? p.E_mes ?? p.X_mes ?? p.Xmes ?? p.xMes ?? p.x_mes ?? p.Xmesure ?? p.X_mesure);
  const yMes = (p.mes?.N ?? p.mes?.Y ?? p.N_mes ?? p.Y_mes ?? p.Ymes ?? p.yMes ?? p.y_mes ?? p.Ymesure ?? p.Y_mesure);
  const zMes = (p.mes?.H ?? p.mes?.Z ?? p.H_mes ?? p.Z_mes ?? p.Zmes ?? p.zMes ?? p.z_mes ?? p.Zmesure ?? p.Z_mesure);

  // 1) POINT : XYZ mesurés uniquement
  bodyLR.push([
    {content:id, __kind:"MEAS", __grp:__grp, styles:{halign:"left"}}, "", "", "",
    "", "", "",
    "", "", "", ""
  ]);

  // 2) RABATTEMENT : valeurs (avec XYZ mes + statut)
  const dx = p.d?.dx ?? p.d?.dX ?? p.dx ?? p.dX ?? p.Dx ?? null;
  const dyv = p.d?.dy ?? p.d?.dY ?? p.dy ?? p.dY ?? p.Dy ?? null;
  const dz = p.d?.dz ?? p.d?.dZ ?? p.dz ?? p.dZ ?? p.Dz ?? null;

  const xCalc = p.calc?.E ?? p.calc?.X ?? p.E_calc ?? p.X_calc ?? p.Xcalc ?? p.xCalc ?? p.x_calc ?? p.Xcalcule ?? p.X_calcule;
  const yCalc = p.calc?.N ?? p.calc?.Y ?? p.N_calc ?? p.Y_calc ?? p.Ycalc ?? p.yCalc ?? p.y_calc ?? p.Ycalcule ?? p.Y_calcule;
  const zCalc = p.calc?.H ?? p.calc?.Z ?? p.H_calc ?? p.Z_calc ?? p.Zcalc ?? p.zCalc ?? p.z_calc ?? p.Zcalcule ?? p.Z_calcule;

  const stTxt = tolOn ? statusFromTol(dx, dyv, dz) : "";

  bodyLR.push([
    {content:"Point théorique", __kind:"RABPT", __grp:__grp, styles:{halign:"left"}}, fmt(xCalc), fmt(yCalc), fmt(zCalc),
    fmt(xMes), fmt(yMes), fmt(zMes),
    fmt(dx), fmt(dyv), fmt(dz),
    stTxt
  ]);

  // 3) LIGNE : valeurs dL/dT/dA (sans statut)
  const ec = p.ecarts || p.ec || p.line || {};
  const toNum = (v) => {
    if(v==null) return null;
    if(typeof v === "number") return Number.isFinite(v) ? v : null;
    const n = Number(String(v).replace(",", "."));
    return Number.isFinite(n) ? n : null;
  };
  const dL = toNum(p?.line?.dL ?? p?.dL ?? p?.dl ?? ec.dL ?? ec.dl ?? ec.DL ?? null);
  const dT = toNum(p?.line?.dT ?? p?.dT ?? p?.dt ?? ec.dT ?? ec.dt ?? ec.DT ?? null);
  const dA = toNum(p?.line?.dA ?? p?.dA ?? p?.da ?? ec.dA ?? ec.da ?? ec.DA ?? null);

  bodyLR.push([
    {content:"Delta ligne", __kind:"LINE", __grp:__grp, styles:{halign:"left"}}, "", "", "",
    "", "", "",
    fmt(dL), fmt(dT), fmt(dA),
    ""
  ]);

  __ptIndex++;

});


autoTableResults(doc, {
  startY: y + 1,
  nfTag: "ligne",
  head: [["ID point","X calc","Y calc","Z calc","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","Statut"]],
  bodyLR,
  margin: { left:10, right:10 },
    columnStyles: RESULTS_COLUMN_STYLES,
  styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
  headStyles: { fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
  didParseCell: function (dataCell) { applyStatusFitAutoTable(dataCell);
          // 1.7.5 : en-têtes en gras (ne pas écraser le style du header)
      if(dataCell && dataCell.section === "head"){
        try{ dataCell.cell.styles.fontStyle = "bold"; }catch(e){}
        return;
      }
// Alternance de fond PAR POINT (les 3 lignes ID/Rabattement/Ligne ont le même fond)
    try{
      dataCell.cell.styles.textColor = [0,0,0];
      dataCell.cell.styles.fontStyle = "normal";

      const row = dataCell.row?.raw;
      if(!row) return;

      const grp = row[0]?.__grp;
      dataCell.cell.styles.fillColor = (grp===1) ? [235,235,235] : [255,255,255];
    }catch(e){}
  },
    // IMPORTANT: no thick lines via autoTable hooks anymore (deterministic post-process)
});

  // Last-page box only (ensure the final page count is known BEFORE stamping pagination)
  y = ensureRoomForLastFooter(doc, 95, y);
  const stats = computeControlStats(lrPoints);
  pdfFooterLastPageBox(doc, R, stats);

  // Address + pagination all pages (ONCE, at the very end to avoid overlaps)
  pdfFooterAllPagesAddress(doc);
}
/* ===== PDF COMPLET (implantation + mesure sur ligne) — ENCHAÎNEMENT SANS RE-HEADER =====
   - Un seul cartouche au début (drawHeaderV2)
   - Station libre affichée une seule fois
   - Ensuite: tableaux Implantation puis Ligne, séparés uniquement par le titre bleu (pdfBar)
   - Un seul footer/pagination sur toutes les pages + un seul bloc final (contrôles/observations/signatures)
*/
function buildPdfFullInto(doc, data){
  // Defensive normalization (avoid crashes on partial/empty datasets)
  data = data || {};
  try{
    if(!data.implantation) data.implantation = {};
    if(!Array.isArray(data.implantation.points)) data.implantation.points = [];
    if(!Array.isArray(data.ligneRef)) data.ligneRef = [];
    // stationLibreRuns is optional; keep as-is for other reports
  }catch(_){ /* ignore */ }

  const R = rf();
  const O = getOptions();

  patchAutoTableFooterSafe(doc, R);
  // Header unique
  let y = drawHeaderV2(doc, R);

  // ===== MULTI-STATIONS (ordre AppLog) =====
  const runs = (data.stationLibreRuns && data.stationLibreRuns.length)
    ? data.stationLibreRuns
    : (data.stationLibre ? [data.stationLibre] : []);

  const tolOn = document.getElementById("optTol").checked;
  const xyOn = document.getElementById("tolXYOn").checked;
  const zOn  = document.getElementById("tolZOn").checked;
  const tXY = Number(document.getElementById("tolXY").value);
  const tZ  = Number(document.getElementById("tolZ").value);

  const tolTxt = tolOn
    ? `Tolérances : X=${xyOn ? tXY : "—"} ; Y=${xyOn ? tXY : "—"} ; Z=${zOn ? tZ : "—"}`
    : `Tolérances désactivées`;

  const allImpPts = Array.isArray(data?.implantation?.points) ? data.implantation.points : [];
  const allLrPointsForStats = (data.ligneRef||[]).flatMap(lr=>lr?.rabPoints||[]);

  if(!runs.length){
    // Aucune station détectée : rendu minimal mais robuste
    setBodyFont(doc);
    doc.setFontSize(10);
    doc.text("Aucune station trouvée dans l'AppLog.", 10, y+8);
    y += 14;

    // Implantation (tous points)
    y = pdfBar(doc, y, "IMPLANTATION");
    y = pdfTolBar(doc, y, tolTxt);
    const bodyImp = allImpPts.map(rowFromPoint);
    if(!bodyImp.length){
      setBodyFont(doc); doc.setFontSize(9);
      doc.text("Aucun point d'implantation.", 10, y+5);
      y += 9;
    } else {
      autoTableResults(doc, { startY: y+1,
        nfThickSpec: "implantation",
        head: [["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"]],
        body: bodyImp,
        margin: { left:10, right:10 },
        columnStyles: RESULTS_COLUMN_STYLES,
        styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
        headStyles: { fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
        didParseCell: function (dataCell){ applyStatusFitAutoTable(dataCell); if(dataCell && dataCell.section==="head"){ try{ dataCell.cell.styles.fontStyle="bold"; }catch(e){} return; } },
	      // IMPORTANT: no thick lines via autoTable hooks anymore (deterministic post-process)
	      });
      y = (doc.lastAutoTable && doc.lastAutoTable.finalY) ? (doc.lastAutoTable.finalY + 6) : (y + 10);
    }

    // Ligne (tous points)
    y = pdfBar(doc, y, "MESURE SUR LIGNE");
    y = pdfTolBar(doc, y, tolTxt);
    const lrPoints = allLrPointsForStats.filter(p=> (p?.id||"").toString().trim().length);
    if(!lrPoints.length){
      setBodyFont(doc); doc.setFontSize(9);
      doc.text("Aucun point de mesure sur ligne.", 10, y+5);
      y += 9;
    } else {
      // réutilise la même construction de lignes que ci-dessous (multi-stations)
      let __ptIndex = 0;
      const bodyLR = [];
      lrPoints.forEach((p)=>{
        const id = (p?.id ?? p?.Id ?? p?.ID ?? "").toString().trim();
        if(!id) return;
        const __grp = (__ptIndex % 2);
        bodyLR.push([{content:id, __kind:"MEAS", __grp:__grp, styles:{halign:"left"}}, "", "", "", "", "", "", "", "", "", ""]);
        const c = getCalc(p) || {}; const m = getMes(p) || {};
        const cE = getCoord(c,"E","X"), cN = getCoord(c,"N","Y"), cH = getCoord(c,"H","Z");
        const mE = getCoord(m,"E","X"), mN = getCoord(m,"N","Y"), mH = getCoord(m,"H","Z");
        const d = p?.d || p?.delta || {};
        const dx = d.dx ?? d.dX ?? d.Dx ?? d.X ?? d.x;
        const dyv = d.dy ?? d.dY ?? d.Dy ?? d.Y ?? d.y;
        const dz = d.dz ?? d.dZ ?? d.Dz ?? d.Z ?? d.z;
        const st = statusFromTol(dx, dyv, dz);
        bodyLR.push([{content:"Point théorique", __kind:"RAB", __grp:__grp, styles:{halign:"left"}}, fmt(cE), fmt(cN), fmt(cH), fmt(mE), fmt(mN), fmt(mH), fmt(dx), fmt(dyv), fmt(dz), st]);
        const ec = p.ecarts || p.ec || p.line || {};
        const toNum = (v) => { if(v==null) return null; if(typeof v==="number") return Number.isFinite(v)?v:null; const n=Number(String(v).replace(",",".")); return Number.isFinite(n)?n:null; };
        const dL = toNum(p?.line?.dL ?? p?.dL ?? p?.dl ?? ec.dL ?? ec.dl ?? ec.DL ?? null);
        const dT = toNum(p?.line?.dT ?? p?.dT ?? p?.dt ?? ec.dT ?? ec.dt ?? ec.DT ?? null);
        const dA = toNum(p?.line?.dA ?? p?.dA ?? p?.da ?? ec.dA ?? ec.da ?? ec.DA ?? null);
        bodyLR.push([{content:"Delta ligne", __kind:"LINE", __grp:__grp, styles:{halign:"left"}}, "", "", "", "", "", "", fmt(dL), fmt(dT), fmt(dA), ""]);
        __ptIndex++;
      });
      autoTableResults(doc, { startY: y+1,
        nfThickSpec: "ligne",
        head: [["ID point","X calc","Y calc","Z calc","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","Statut"]],
        body: bodyLR,
        margin: { left:10, right:10 },
        columnStyles: RESULTS_COLUMN_STYLES,
        styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
        headStyles: { fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
        didParseCell: function (dataCell){ applyStatusFitAutoTable(dataCell); if(dataCell && dataCell.section==="head"){ try{ dataCell.cell.styles.fontStyle="bold"; }catch(e){} return; } try{ const row=dataCell.row?.raw; if(!row) return; const grp=row[0]?.__grp; if(grp===0||grp===1){ dataCell.cell.styles.fillColor=(grp===1)?[235,235,235]:[255,255,255]; return; } const kind=row[0]?.__kind; if(kind==="RAB") dataCell.cell.styles.fillColor=[240,240,240]; else if(kind==="LINE") dataCell.cell.styles.fillColor=[255,255,255]; }catch(e){} },
	      // IMPORTANT: no thick lines via autoTable hooks anymore (deterministic post-process)
	      });
    }
  } else {
    // Stations détectées : on imprime station -> implantation -> ligne, dans l'ordre AppLog
    for(let si=0; si<runs.length; si++){
      const run = runs[si];
      const setupId = run?.results?.idStation || null;

      // --- STATION ---
      y = pdfStationLibreFullRun(doc, y, run);

      // --- IMPLANTATION (rattachée à cette station) ---
      y = pdfBar(doc, y, "IMPLANTATION");
      y = pdfTolBar(doc, y, tolTxt);

      const impPts = allImpPts
        .filter(p => (p?.stationId || null) === setupId || (p?.stationId == null && si===0));

      const bodyImp = impPts.map(rowFromPoint);
      if(!bodyImp.length){
        setBodyFont(doc); doc.setFontSize(9);
        doc.text("Aucun point d'implantation pour cette station.", 10, y+5);
        y += 9;
      } else {
        autoTableResults(doc, { startY: y+1,
          nfThickSpec: "implantation",
          head: [["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"]],
          body: bodyImp,
          margin: { left:10, right:10 },
          columnStyles: RESULTS_COLUMN_STYLES,
          styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
          headStyles: { fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
          didParseCell: function (dataCell){ applyStatusFitAutoTable(dataCell); if(dataCell && dataCell.section==="head"){ try{ dataCell.cell.styles.fontStyle="bold"; }catch(e){} return; } },
	      // IMPORTANT: no thick lines via autoTable hooks anymore (deterministic post-process)
	      });
        y = (doc.lastAutoTable && doc.lastAutoTable.finalY) ? (doc.lastAutoTable.finalY + 6) : (y + 10);
      }

      // --- LIGNE (rattachée à cette station) ---
      y = pdfBar(doc, y, "MESURE SUR LIGNE");
      y = pdfTolBar(doc, y, tolTxt);

      const lrPoints = (data.ligneRef||[])
        .filter(lr => (lr?.stationId || null) === setupId || (!setupId && !lr?.stationId))
        .flatMap(lr => lr?.rabPoints || [])
        .filter(p => (p?.id || "").toString().trim().length);

      if(!lrPoints.length){
        setBodyFont(doc); doc.setFontSize(9);
        doc.text("Aucun point de mesure sur ligne pour cette station.", 10, y+5);
        y += 9;
      } else {
        let __ptIndex = 0;
        const bodyLR = [];
        lrPoints.forEach((p)=>{
          const id = (p?.id ?? p?.Id ?? p?.ID ?? "").toString().trim();
          if(!id) return;
          const __grp = (__ptIndex % 2);
          bodyLR.push([{content:id, __kind:"MEAS", __grp:__grp, styles:{halign:"left"}}, "", "", "", "", "", "", "", "", "", ""]);
          const c = getCalc(p) || {}; const m = getMes(p) || {};
          const cE = getCoord(c,"E","X"), cN = getCoord(c,"N","Y"), cH = getCoord(c,"H","Z");
          const mE = getCoord(m,"E","X"), mN = getCoord(m,"N","Y"), mH = getCoord(m,"H","Z");
          const d = p?.d || p?.delta || {};
          const dx = d.dx ?? d.dX ?? d.Dx ?? d.X ?? d.x;
          const dyv = d.dy ?? d.dY ?? d.Dy ?? d.Y ?? d.y;
          const dz = d.dz ?? d.dZ ?? d.Dz ?? d.Z ?? d.z;
          const st = statusFromTol(dx, dyv, dz);
          bodyLR.push([{content:"Point théorique", __kind:"RAB", __grp:__grp, styles:{halign:"left"}}, fmt(cE), fmt(cN), fmt(cH), fmt(mE), fmt(mN), fmt(mH), fmt(dx), fmt(dyv), fmt(dz), st]);
          const ec = p.ecarts || p.ec || p.line || {};
          const toNum = (v) => { if(v==null) return null; if(typeof v==="number") return Number.isFinite(v)?v:null; const n=Number(String(v).replace(",",".")); return Number.isFinite(n)?n:null; };
          const dL = toNum(p?.line?.dL ?? p?.dL ?? p?.dl ?? ec.dL ?? ec.dl ?? ec.DL ?? null);
          const dT = toNum(p?.line?.dT ?? p?.dT ?? p?.dt ?? ec.dT ?? ec.dt ?? ec.DT ?? null);
          const dA = toNum(p?.line?.dA ?? p?.dA ?? p?.da ?? ec.dA ?? ec.da ?? ec.DA ?? null);
          bodyLR.push([{content:"Delta ligne", __kind:"LINE", __grp:__grp, styles:{halign:"left"}}, "", "", "", "", "", "", fmt(dL), fmt(dT), fmt(dA), ""]);
          __ptIndex++;
        });

        autoTableResults(doc, { startY: y+1,
          nfThickSpec: "ligne",
          head: [["ID point","X calc","Y calc","Z calc","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","Statut"]],
          body: bodyLR,
          margin: { left:10, right:10 },
          columnStyles: RESULTS_COLUMN_STYLES,
          styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.6, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
          headStyles: { fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
          didParseCell: function (dataCell){ applyStatusFitAutoTable(dataCell); if(dataCell && dataCell.section==="head"){ try{ dataCell.cell.styles.fontStyle="bold"; }catch(e){} return; } try{ const row=dataCell.row?.raw; if(!row) return; const grp=row[0]?.__grp; if(grp===0||grp===1){ dataCell.cell.styles.fillColor=(grp===1)?[235,235,235]:[255,255,255]; return; } const kind=row[0]?.__kind; if(kind==="RAB") dataCell.cell.styles.fillColor=[240,240,240]; else if(kind==="LINE") dataCell.cell.styles.fillColor=[255,255,255]; }catch(e){} },
	      // IMPORTANT: no thick lines via autoTable hooks anymore (deterministic post-process)
	      });
        y = (doc.lastAutoTable && doc.lastAutoTable.finalY) ? (doc.lastAutoTable.finalY + 6) : (y + 10);
      }

      // Petite séparation entre stations
      if(si < runs.length-1){
        y = ensureSpace(doc, y, 18);
      }
    }
  }

  // Pour les stats de fin de document, on conserve une liste globale des points ligne
  const lrPoints = allLrPointsForStats;

  // Bloc final unique (contrôles + observations + visas)
  // IMPORTANT: stamp footer/pagination ONCE, after all pages exist, to avoid "Page 2/3" over "Page 2/4" overlaps.
  y = ensureRoomForLastFooter(doc, 95);

  // Stats combinées (implantation + ligne)
  // IMPORTANT: selon les variantes d'AppLog, les objets "points" peuvent être hétérogènes entre Implantation et Ligne.
  // Le PDF COMPLET ne doit jamais bloquer l'export : on calcule en mode "safe" avec fallback.
  let stats = {};
  try{
    stats = computeControlStats([...(data.implantation?.points||[]), ...lrPoints]);
  }catch(e){
    try{ stats = computeControlStats(data.implantation?.points || []); }catch(_){ stats = {}; }
  }
  pdfFooterLastPageBox(doc, R, stats);

  // Footer / pagination sur toutes pages (ONCE, at the very end)
  pdfFooterAllPagesAddress(doc);
}



// ===== pdfDocTitle désactivé : l'en-tête est géré par pdfHeaderNew =====
function pdfDocTitle(doc, y, R){
  return y;
}



// ===== Désactivation forcée de l'ancien cartouche (même si défini en const) =====
window.pdfDocTitle = function(doc, y, R){ return y; };



// ===== Fallback sécurité =====
if(typeof ensureFontsRegistered !== "function"){
  function ensureFontsRegistered(){ /* no-op */ }
}



// ===== Helpers coords robustes =====
function getCoord(obj, primary, fallback){
  if(!obj) return undefined;
  if(obj[primary] != null) return obj[primary];
  if(fallback && obj[fallback] != null) return obj[fallback];
  return undefined;
}
function getCalc(p){
  return p?.calc || p?.cal || p?.theo || p?.theorique || p?.xyzCalc || null;
}
function getMes(p){
  return p?.mes || p?.mesure || p?.xyzMes || p?.impl || p?.implante || null;
}
function rowFromPoint(p){
  const stripAt = (s)=>{ s = (s==null)?"":String(s); const i=s.indexOf('@'); return (i>=0)?s.substring(0,i):s; };
  const c = getCalc(p) || {};
  const m = getMes(p) || {};
  const cE = getCoord(c,"E","X"), cN = getCoord(c,"N","Y"), cH = getCoord(c,"H","Z");
  const mE = getCoord(m,"E","X"), mN = getCoord(m,"N","Y"), mH = getCoord(m,"H","Z");
  const d = p?.d || p?.delta || {};
  const dx = d.dx ?? d.dX ?? d.Dx ?? d.X ?? d.x;
  const dy = d.dy ?? d.dY ?? d.Dy ?? d.Y ?? d.y;
  const dz = d.dz ?? d.dZ ?? d.Dz ?? d.Z ?? d.z;
  return [
    stripAt(p?.id ?? p?.Id ?? p?.ID ?? ""),
    fmt(cE), fmt(cN), fmt(cH),
    fmt(mE), fmt(mN), fmt(mH),
    fmt(dx), fmt(dy), fmt(dz),
    statusFromTol(dx,dy,dz)
  ];
}

function rowFromLineEC(p){
  const ec = p?.ec || p?.ecarts || p?.Ecart || {};
  const dL = ec.dL ?? ec.dl ?? ec.DL;
  const dT = ec.dT ?? ec.dt ?? ec.DT;
  const dA = ec.dA ?? ec.da ?? ec.DA;
  const stripAt = (s)=>{ s = (s==null)?"":String(s); const i=s.indexOf('@'); return (i>=0)?s.substring(0,i):s; };
  const pid = stripAt(p?.id ?? p?.Id ?? p?.ID ?? "");
  return [
    {content: pid, __kind:"LINE", styles:{halign:"left"}},
    "", "", "",
    "", "", "",
    fmt(dL), fmt(dT), fmt(dA),
    "" // pas de statut pour la ligne
  ];
}






// ===== Helper: Ligne (dL/dT/dA) pour tableaux "Mesure sur ligne" =====

// ===== Fallbacks PDF (sécurité pour le PDF complet) =====
if(typeof pdfFooterAllPages !== "function"){
  
// ===== V2 : Logo NOVATLAS sur toutes les pages (pages 2..n) =====
// On réutilise le même logoDataUrl que la première page (déjà chargé) et on l'ajoute en en-tête sans encadré.
function stampLogoOtherPages(doc){
  try{
    if(!(typeof logoDataUrl === "string" && logoDataUrl.startsWith("data:image"))) return;
    const n = (typeof doc.getNumberOfPages === "function") ? doc.getNumberOfPages()
            : (doc.internal && typeof doc.internal.getNumberOfPages === "function") ? doc.internal.getNumberOfPages()
            : 1;
    if(!n || n <= 1) return;

    const pageW = (doc.internal && doc.internal.pageSize && doc.internal.pageSize.getWidth) ? doc.internal.pageSize.getWidth() : 210;
    const imgW = 24; // 50% de 48
    const imgH = 7;  // 50% de 14
    const marginX = 10;
    const y = 8;

    for(let p=2; p<=n; p++){
      try{ doc.setPage(p); }catch(e){}
      const x = pageW - marginX - imgW;
      try{ doc.addImage(logoDataUrl, "PNG", x, y, imgW, imgH, undefined, "FAST"); }catch(e){}
    }
  }catch(e){}
}

function pdfFooterAllPages(doc, R){
  // Footer commun à toutes les pages (adresse + build)
  try{ pdfFooterAllPagesAddress(doc); }catch(e){}
  try{
		const v = (typeof APP_VERSION!=="undefined" && APP_VERSION) ? String(APP_VERSION) : "";
		const sha = (typeof window!=="undefined" && window.__NF_ASSETS_SHA)
		  ? String(window.__NF_ASSETS_SHA).slice(0,8)
		  : "";
    if(v){
      const t = "Version " + v;
      doc.setFont("helvetica","normal");
      doc.setFontSize(8);
      doc.setTextColor(120);
      // aligné bas-droit, léger retrait
      const x = (R?.pageW ?? doc.internal.pageSize.getWidth()) - (R?.mR ?? 10);
      const y = (R?.pageH ?? doc.internal.pageSize.getHeight()) - 4;
      doc.text(t, x, y, { align:"right" });
    }
  }catch(e){}
}

}
if(typeof pdfFooterLastPageBox !== "function"){
  function pdfFooterLastPageBox(doc, R, stats){ /* no-op */ }
}
if(typeof ensureRoomForLastFooter !== "function"){
  function ensureRoomForLastFooter(doc, needed, currentY){
  // Hard rule: never allow last-page boxes / footer area to overlap content.
  // If we are too low, jump to next page BEFORE drawing the last-page box.
  try{
    const pageH = (doc?.internal?.pageSize?.getHeight) ? doc.internal.pageSize.getHeight() : doc.internal.pageSize.height;
    const guard = 32; // mm reserved for footer + bottom boxes
    let y = (typeof currentY === "number" && !isNaN(currentY)) ? currentY
          : (doc?.lastAutoTable?.finalY ? doc.lastAutoTable.finalY + 2 : 18);
    const need = (typeof needed === "number" && needed>0) ? needed : 70;
    if ((y + need) > (pageH - guard)){
      doc.addPage();
      return 18;
    }
    return y;
  }catch(e){
    return (typeof currentY === "number") ? currentY : 18;
  }
}
}



// ============================================================================
// HEADER v2 (reparti de zéro) — UNE SEULE source de vérité
// - Logo gauche (image si dispo) + cadre
// - Bloc Ville / Adresse chantier à droite + cadre
// - Bandeau titre (rectangle du milieu) : fond bleu, texte noir
// - Lignes : Intervention / Client, Plan de référence, Phase/Type/Indice/CHA
// ============================================================================

function drawHeaderV2(doc, R){
  const x0 = 10;
  const pageW = 190;
  const gap = 4;

  // Dimensions
  const topY = 10;
  const topH = 20;
  const titleH = 16;

  const leftW = 60;
  const rightW = pageW - leftW - gap;

  // --- Bloc haut : logo + chantier (Ville / Adresse / CHA)
  pdfRect(doc, x0, topY, leftW, topH, null);
  pdfRect(doc, x0 + leftW + gap, topY, rightW, topH, null);

  // Logo (centré)
  try{
    if(typeof logoDataUrl === "string" && logoDataUrl.startsWith("data:image")){
      const imgH = 14;
      const imgW = 48;
      const imgX = x0 + (leftW - imgW)/2;
      const imgY = topY + (topH - imgH)/2;
      doc.addImage(logoDataUrl, "PNG", imgX, imgY, imgW, imgH, undefined, "FAST");
    }
  }catch(e){}

  // Chantier : Ville / Adresse / CHA
  try{
    const cx = x0 + leftW + gap + rightW/2;

    setTitleFont(doc);
    doc.setFontSize(12);
    doc.setTextColor(0,0,0);
    doc.text(String((R.ville||R.zone||"")||"").toUpperCase(), cx, topY + 7.5, {align:"center"});

    setBodyFont(doc);
    doc.setFontSize(9.5);
    const addr = String(R.siteAddress||"").trim();
    if(addr){
      doc.text(addr, cx, topY + 13.2, {align:"center"});
    }

	    // CHA (dans le même cadre)
	    // Saisie : uniquement chiffres (ex: 02782). PDF : afficher "CHA02782".
	    const chaDigits = String(R.cha||R.CHA||"")
	      .trim()
	      .replace(/^\s*CHA\s*[-_:\s]*/i, "");
	    const chaFull = chaDigits ? ("CHA" + chaDigits) : "";
	    if(chaDigits){
	      setBodyFont(doc);
	      doc.setFontSize(9);
	      doc.text(`${chaFull}`, cx, topY + 18.0, {align:"center"});
	      doc.setFontSize(8);
	      doc.setTextColor(0,0,0);
	    }
  }catch(e){}

  // --- Bandeau TITRE : rectangle sous logo/adresse
  const titleY = topY + topH + 2;
  doc.setFillColor(BRAND.r, BRAND.g, BRAND.b);
  doc.rect(x0, titleY, pageW, titleH, "F");
  doc.setTextColor(255,255,255);
  setTitleFont(doc);
  doc.setFontSize(12);
  doc.text("RAPPORT D\'INTERVENTION", x0 + pageW/2, titleY + (titleH/2), {align:"center", baseline:"middle"});
  doc.setTextColor(0,0,0);

  // --- Cadre dédié : NOM DE L'INTERVENTION (juste sous le titre)
  let y = titleY + titleH + 2;
  const interH = 10;
  pdfRect(doc, x0, y, pageW, interH, null);

  setBodyFont(doc);
  doc.setFontSize(8.5);
  doc.text("INTERVENTION", x0 + 3, y + 4.2);

  setTitleFont(doc);
  doc.setFontSize(11);
  doc.text(String(R.elements||"").toUpperCase(), x0 + pageW/2, y + 7.0, {align:"center"});

  y += interH + 2;

  // --- Cartouche infos (V2) : 3 lignes
// L1 : [Intervention du] [Entreprise] [Contact client]
// L2 : [Système de coordonnées] [PPM] [Intervenant]
// L3 : [Système altimétrique] [Entreprise] [Plan de référence]

const c_dateW = 56;
const c_midW  = 70;
const c_rightW  = pageW - c_dateW - c_midW;

const l2_coordW = c_dateW;
const l2_ppmW   = 38;
const l2_intW   = pageW - l2_coordW - l2_ppmW;

const l3_altW   = c_dateW;
const l3_entW   = 60;
const l3_planW  = pageW - l3_altW - l3_entW;

const c_padX = 3.5;
const c_lineH = 4.2;
const c_cellTitleH = 4.2;
const c_gap = 1.0;
const c_padY = 2.2;

const wrapV = (value, maxW) => {
  const v = String(value || "").trim();
  if(!v) return [""];
  try{
    if(doc.splitTextToSize && maxW){
      return doc.splitTextToSize(v, maxW);
    }
  }catch(e){}
  return [v];
};

const toNum = (v) => {
  if(v==null) return null;
  const s = String(v).trim();
  if(!s) return null;
  const n = Number(s.replace(',', '.'));
  return Number.isFinite(n) ? n : null;
};
const fmt3 = (v) => {
  const n = toNum(v);
  if(n==null) return String(v||"").trim();
  return (Math.round(n*1000)/1000).toFixed(3);
};

const inst = String(R.instrument || "").trim();
const ser  = String(R.serial || "").trim();
const appInfo = (inst && ser) ? `${inst} – ${ser}` : (inst || ser || "");

const vDate = wrapV((R.date||""), c_dateW - c_padX*2);

// Labels remplacés:
const vEntreprise = wrapV((R.client||""), c_midW - c_padX*2);               // Entreprise (EXE)
const vContactClient = wrapV((R.siteContact||""), c_rightW - c_padX*2);     // Contact client (EXE)

const vCoord = wrapV((R.coordSystem||""), l2_coordW - c_padX*2);
const ppmRaw = String(R.ppm||"").trim();
const vPpmTxt = ppmRaw ? `${ppmRaw} mm/km` : "";
const vPPM  = wrapV(vPpmTxt, l2_ppmW - c_padX*2);

// Swap : L2 right is now Plan de référence (fit 1 line)
const planRef = String(R.dwg || "").trim();
const vPlanL2 = wrapV((planRef||""), l2_intW - c_padX*2);

// L3
const vAlt = wrapV((R.altimetricSystem||""), l3_altW - c_padX*2);
const vApp = wrapV((appInfo||""), l3_entW - c_padX*2);
const vInterv3 = wrapV((R.operator||""), l3_planW - c_padX*2);


// Hauteurs dynamiques
const row1ValLines = Math.max(vDate.length, vEntreprise.length, vContactClient.length);
const row2ValLines = Math.max(vCoord.length, vPPM.length, vPlanL2.length);
const row3ValLines = Math.max(vAlt.length, vApp.length, vInterv3.length);

const rowH = (valLines) => (c_padY + c_cellTitleH + c_gap + (valLines * c_lineH) + c_padY);

const row1H = rowH(row1ValLines);
const row2H = rowH(row2ValLines);
const row3H = rowH(row3ValLines);
const cartH = row1H + row2H + row3H;

// Cadre cartouche + séparateurs horizontaux
pdfRect(doc, x0, y, pageW, cartH, null);
try{ doc.line(x0, y + row1H, x0 + pageW, y + row1H); }catch(e){}
try{ doc.line(x0, y + row1H + row2H, x0 + pageW, y + row1H + row2H); }catch(e){}

// Helper dessin centré : titre (normal) + valeur (gras, dessous)
const drawCell = (x, w, yTop, title, valueLines, boldSize=null) => {
  const cx = x + (w/2);
  try{ doc.setFont(undefined, "normal"); }catch(e){}
  doc.text(String(title||""), cx, yTop + c_padY + 3.0, { align:"center" });

  // Valeur (centrée, en gras)
  try{ doc.setFont(undefined, "bold"); }catch(e){}
  const prevSize = doc.getFontSize ? doc.getFontSize() : null;
  if(boldSize) doc.setFontSize(boldSize);
  const yVal = yTop + c_padY + c_cellTitleH + c_gap + 3.0;
  doc.text(valueLines, cx, yVal, { align:"center" });
  if(boldSize && prevSize) doc.setFontSize(prevSize);
  try{ doc.setFont(undefined, "normal"); }catch(e){}
};

// Helper : fit one-line (Plan de référence)
const drawCellFitOneLine = (x, w, yTop, title, value) => {
  const cx = x + (w/2);
  try{ doc.setFont(undefined, "normal"); }catch(e){}
  doc.text(String(title||""), cx, yTop + c_padY + 3.0, { align:"center" });

  let txt = String(value||"").trim();
  try{ doc.setFont(undefined, "bold"); }catch(e){}
  const yVal = yTop + c_padY + c_cellTitleH + c_gap + 3.0;
  const maxW = w - c_padX*2;
  let fs = 10;
  try{ fs = doc.getFontSize() || 10; }catch(e){ fs = 10; }
  let use = fs;
  for(let s=fs; s>=7; s-=0.5){
    try{
      doc.setFontSize(s);
      const wTxt = doc.getTextWidth ? doc.getTextWidth(txt) : 0;
      if(!wTxt || wTxt <= maxW){ use = s; break; }
    }catch(e){}
  }
  try{ doc.setFontSize(use); }catch(e){}
  doc.text(txt, cx, yVal, { align:"center" });
  // restore
  try{ doc.setFontSize(fs); }catch(e){}
  try{ doc.setFont(undefined, "normal"); }catch(e){}
};

// ===== LIGNE 1 =====
// séparateurs verticaux (ligne 1)
try{ doc.line(x0 + c_dateW, y, x0 + c_dateW, y + row1H); }catch(e){}
try{ doc.line(x0 + c_dateW + c_midW, y, x0 + c_dateW + c_midW, y + row1H); }catch(e){}

drawCell(x0,                 c_dateW,  y, "Intervention du", vDate);
drawCell(x0 + c_dateW,       c_midW,   y, "Entreprise",      vEntreprise);
drawCell(x0 + c_dateW+c_midW, c_rightW, y, "Contact client",  vContactClient);

// ===== LIGNE 2 =====
const y2 = y + row1H;
// séparateurs verticaux (ligne 2)
try{ doc.line(x0 + l2_coordW, y2, x0 + l2_coordW, y2 + row2H); }catch(e){}
try{ doc.line(x0 + l2_coordW + l2_ppmW, y2, x0 + l2_coordW + l2_ppmW, y2 + row2H); }catch(e){}

drawCell(x0,                   l2_coordW, y2, "Système de coordonnées", vCoord);
drawCell(x0 + l2_coordW,       l2_ppmW,   y2, "PPM",                   vPPM);
drawCellFitOneLine(x0 + l2_coordW+l2_ppmW, l2_intW, y2, "Plan de référence", planRef);

// ===== LIGNE 3 =====
const y3 = y + row1H + row2H;
// séparateurs verticaux (ligne 3)
try{ doc.line(x0 + l3_altW, y3, x0 + l3_altW, y3 + row3H); }catch(e){}
try{ doc.line(x0 + l3_altW + l3_entW, y3, x0 + l3_altW + l3_entW, y3 + row3H); }catch(e){}

drawCell(x0,             l3_altW, y3, "Système altimétrique", vAlt);
drawCell(x0 + l3_altW,   l3_entW, y3, "Appareil",             vApp);
drawCell(x0 + l3_altW + l3_entW, l3_planW, y3, "Intervenant", vInterv3);

y += cartH + 2;
return y;
}



// Override : tous les PDF utilisent pdfHeader() => drawHeaderV2()
window.pdfHeader = function(doc, R){
  return drawHeaderV2(doc, R);
};



// ===== Override définitif : tout appel à pdfHeader passe sur drawHeaderV2 =====
try{ pdfHeader = drawHeaderV2; }catch(e){}



// ===== Override définitif : anciens headers => drawHeaderV2 =====
try{ pdfHeader = drawHeaderV2; }catch(e){}
try{ pdfHeaderNew = drawHeaderV2; }catch(e){}




/* =========================
   [v1.76] SAFETY: computeControlStats global
   - Some environments scope function declarations inside blocks.
   - We ensure a global callable identifier used by PDF builders.
========================= */
if(typeof window.computeControlStats !== "function"){
  window.computeControlStats = function(points){
    try{ console.warn("[Topo] Fallback computeControlStats used — please keep main definition above."); }catch(e){}
    const list = Array.isArray(points) ? points : [];
    // count points having an ID
    const total = list.filter(p => p && String(p.id ?? p.ID ?? p.Id ?? "").trim() !== "").length;

    const tolOn = !!document.getElementById("optTol")?.checked;
    let ok = null, ko = null;

    // Extract numeric deltas
    const dxs=[], dys=[], dzs=[];
    for(const p of list){
      if(!p) continue;
      const d = p.d || p.delta || {};
      const dx = Number(String(d.dx ?? d.dX ?? d.Dx ?? "").replace(",", "."));
      const dy = Number(String(d.dy ?? d.dY ?? d.Dy ?? "").replace(",", "."));
      const dz = Number(String(d.dz ?? d.dZ ?? d.Dz ?? "").replace(",", "."));
      if(Number.isFinite(dx)) dxs.push(dx);
      if(Number.isFinite(dy)) dys.push(dy);
      if(Number.isFinite(dz)) dzs.push(dz);
    }

    // ok/ko only when tolerances are enabled
    if(tolOn){
      ok = 0; ko = 0;
      for(const p of list){
        if(!p || !p.d) continue;
        const st = statusFromTol(p.d.dx, p.d.dy, p.d.dz);
        if(st === "VALIDE") ok++;
        else if(st === "REFUSÉ") ko++;
      }
    }

    // population standard deviation
    function sd(arr){
      if(!arr || arr.length < 2) return null;
      const n = arr.length;
      const mean = arr.reduce((a,b)=>a+b,0)/n;
      const v = arr.reduce((a,b)=>a+(b-mean)*(b-mean),0)/n;
      return Math.sqrt(v);
    }

    const sdDx = sd(dxs);
    const sdDy = sd(dys);
    const sdDz = sd(dzs);

    const ne = (tolOn && ok!=null && ko!=null) ? (total - ok - ko) : null;

    return {
      total,
      ok,
      ko,
      ne,
      sdDx,
      sdDy,
      sdDz
    };
  };
}
// Ensure the identifier exists for code paths that call computeControlStats(...)
try{ if(typeof computeControlStats !== "function"){ var computeControlStats = window.computeControlStats; } }catch(e){}


  
// ===== N3.1 - Self-check léger (console) =====
(function topoSelfCheck(){
  try{
    const required = ["parseTxtLeica1200","exportPdf","buildPdfIntervention","buildPdfLigneRef","buildPdfComplet","buildPdfFullInto","getOptions","computeControlStats","statusFromTol","drawHeaderV2"];
    const missing = required.filter(n => typeof window[n] !== "function");
    if(missing.length){
      console.warn("[Topo][SelfCheck] Fonctions manquantes:", missing);
try{ if(typeof window.__OFFLINE_VENDOR_HINT__==="function") window.__OFFLINE_VENDOR_HINT__(); }catch(e){}
    }else{
      console.info("[Topo][SelfCheck] OK — fonctions principales présentes.");
    }
  }catch(e){}
})();


// Export TXT button (XYZC) - enabled after LandXML import
try{
  document.getElementById('btnExportTxt')?.addEventListener('click', async ()=>{
    try{ await exportTxtXYZC(false); }catch(e){ console.error(e); }
  });
}catch(_){ }
