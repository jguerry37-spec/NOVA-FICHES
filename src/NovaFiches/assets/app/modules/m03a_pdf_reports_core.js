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
