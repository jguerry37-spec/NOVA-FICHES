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
