// === NOVA-FICHES PDF: Guard footer code (anti chevauchement) ===
// Règle: aucun titre de section ne doit être imprimé dans la code footer.
// Si y + needed dépasse (pageHeight - 32mm) => nouvelle page + y=18
function nfPageH(doc){
  try { return doc.internal.pageSize.getHeight(); } catch(e) { return doc.internal.pageSize.height; }
}
function nfGuardFooter(doc, y, needed){
  try{
    const h = nfPageH(doc);
    const guard = 32;
    const need = (typeof needed==="number" && needed>0) ? needed : 28;
    if ((y + need) > (h - guard)){
      doc.addPage();
      return 18;
    }
    return y;
  }catch(e){ return y; }
}

function parseTxtLeica1200(text){
  const out = emptyData();
  out.rawText = text;
  out.timeline = [];
  const lines = text.replace(/\r\n/g, "\n").split("\n");

  let section = null;
  let i = 0;
  // Station courante (ID) pour les blocs Implantation (afin de rattacher chaque point à sa station)
  // Un AppLog peut contenir plusieurs stations (STL1, STL2...).
  let currentImplStationId = null;

  function parseENH(line){
    return {
      E: numOrNull((line.match(/\bE=\s*([-+]?\d+(?:[.,]\d+)?)/i)||[])[1]),
      N: numOrNull((line.match(/\bN=\s*([-+]?\d+(?:[.,]\d+)?)/i)||[])[1]),
      H: numOrNull((line.match(/\bH=\s*([-+]?\d+(?:[.,]\d+)?)/i)||[])[1]),
    };
  }

  while(i < lines.length){
    const l = lines[i];

    if(!out.meta.instrument && includesNorm(l, "Type d'instrument")) out.meta.instrument = (l.split(":")[1]||"").trim()||null;
    if(!out.meta.serial && (includesNorm(l, "Numro de srie") || includesNorm(l, "Numéro de série"))) out.meta.serial = (l.split(":")[1]||"").trim()||null;
    if(!out.meta.job && includesNorm(l, "Job de travail")) out.meta.job = (l.split(":")[1]||"").trim()||null;

    if(
      includesNorm(l, 'Méthode "Station libre"') ||
      includesNorm(l, 'Methode "Station libre"') ||
      includesNorm(l, 'Mthode "Station libre"')
    ){
      section = "stationLibre";

      // IMPORTANT: each setup must be isolated (AppLog order). Reset per-setup accumulators here.
      out.stationLibre.observations = [];
      out.stationLibre.residuals = [];
      out.stationLibre.results = { idStation:null, E:null, N:null, H:null, corrOrient:null, azOrient:null, devE:null, devN:null, devH:null, devOri:null };

      try{ out.timeline.push({type:"SETUP_SECTION", line:i, raw:l}); }catch(e){}
      i++; 
      continue; 
    }
    if(includesNorm(l, "Leica System 1200 Implantation")){
      section="implantation";
      // Reset: une section Implantation peut apparaitre plusieurs fois (une par station)
      currentImplStationId = null;
      try{ out.timeline.push({type:"IMPLANT_SECTION", line:i, raw:l}); }catch(e){}
      i++; continue;
    }
    if(includesNorm(l, "Leica System 1200 Ligne de rfrence") || includesNorm(l, "Leica System 1200 Ligne de référence")){
      section="ligneRef";
      out.ligneRef.push({
        stationId:null,
        lineId:null,
        start:{id:null,E:null,N:null,H:null},
        end:{id:null,E:null,N:null,H:null},
        rabPoints:[]
      });
      try{ out.timeline.push({type:"LINEREF_SECTION", line:i, raw:l}); }catch(e){}
      i++; continue;
    }

    // Station libre
    if(section === "stationLibre"){
      if(startsWithNorm(l, "Observations")){
        i += 3;
        while(i < lines.length && lines[i].trim() !== ""){
          const row = lines[i].trim();
          const m = row.match(/^(\S+)\s+([-+]?\d+(?:[.,]\d+)?)\s+([-+]?\d+(?:[.,]\d+)?)\s+([-+]?\d+(?:[.,]\d+)?)\s+([-+]?\d+(?:[.,]\d+)?)\s+([-+]?\d+(?:[.,]\d+)?)/);
          if(m){
            out.stationLibre.observations.push({
              id: m[1],
              hz: numOrNull(m[2]),
              vz: numOrNull(m[3]),
              dp: numOrNull(m[4]),
              hr: numOrNull(m[5]),
              constPrisme: numOrNull(m[6]),
              prismConst: numOrNull(m[6])
            });
          }
          i++;
        }
        continue;
      }

      if(startsWithNorm(l, "Rs idus du point") || startsWithNorm(l, "Résidus du point") || startsWithNorm(l, "Residus du point")){
        const m = l.match(/du point\s+(\S+)\s*:.*dHz=\s*([-+]?\d+(?:[.,]\d+)?).*dAlti=\s*([-+]?\d+(?:[.,]\d+)?).*dDH=\s*([-+]?\d+(?:[.,]\d+)?).*Utilis.?:\s*([A-Za-z0-9]+)/i);
        if(m){
          out.stationLibre.residuals.push({ id:m[1], dHz:numOrNull(m[2]), dAlti:numOrNull(m[3]), dDH:numOrNull(m[4]), used:m[5] });
          try{ out.timeline.push({type:"SETUP_RESIDUAL", line:i, id:m[1]}); }catch(e){}
        }
        i++; continue;
      }

      if(startsWithNorm(l, "Rsultats") || startsWithNorm(l, "Résultats") || startsWithNorm(l, "Resultats")){
        i++;
        while(i < lines.length && !includesNorm(lines[i], "ID station") && lines[i].trim() !== "") i++;
        if(i < lines.length && includesNorm(lines[i], "ID station")){
          out.stationLibre.results.idStation = (lines[i].split(":")[1]||"").trim()||null;
          if(i+1 < lines.length){
            const en = parseENH(lines[i+1]);
            out.stationLibre.results.E = en.E;
            out.stationLibre.results.N = en.N;
            out.stationLibre.results.H = en.H;
            const him = lines[i+1].match(/\bHi=\s*([-+]?\d+(?:[.,]\d+)?)/i);
            out.stationLibre.results.Hi = him ? numOrNull(him[1]) : null;
          }
        }
        for(let k=i; k<Math.min(i+18, lines.length); k++){
          const s = lines[k];
          const grab = (pat) => { const m = s.match(pat); return m ? numOrNull(m[1]) : null; };
          if(includesNorm(s, "Corr. orientat")) out.stationLibre.results.corrOrient = grab(/:\s*([-+]?\d+(?:[.,]\d+)?)/) ?? out.stationLibre.results.corrOrient;
          if(includesNorm(s, "Az. orientat") || includesNorm(s,"Az orientat")) out.stationLibre.results.azOrient = grab(/:\s*([-+]?\d+(?:[.,]\d+)?)/) ?? out.stationLibre.results.azOrient;
          // "Facteur d'chelle" est parfois vide : on conserve la chaine brute si présente.
          if((includesNorm(s, "Facteur") && includesNorm(s, "chelle"))){
            const raw = (s.split(":")[1]||"").trim();
            if(raw) out.stationLibre.results.scaleFactor = raw;
            else if(out.stationLibre.results.scaleFactor == null) out.stationLibre.results.scaleFactor = "";
          }
          if(includesNorm(s, "Dev. std Est")) out.stationLibre.results.devE = grab(/:\s*([-+]?\d+(?:[.,]\d+)?)/) ?? out.stationLibre.results.devE;
          if(includesNorm(s, "Dev. std Nord")) out.stationLibre.results.devN = grab(/:\s*([-+]?\d+(?:[.,]\d+)?)/) ?? out.stationLibre.results.devN;
          if(includesNorm(s, "Dev. std Alti")) out.stationLibre.results.devH = grab(/:\s*([-+]?\d+(?:[.,]\d+)?)/) ?? out.stationLibre.results.devH;
          if(includesNorm(s, "Dev. std Orientat")) out.stationLibre.results.devOri = grab(/:\s*([-+]?\d+(?:[.,]\d+)?)/) ?? out.stationLibre.results.devOri;
        }
        try{ out.timeline.push({type:"SETUP_RESULT", line:i, id: out.stationLibre.results.idStation}); }catch(e){}
        try{ out.stationLibreRuns.push(JSON.parse(JSON.stringify(out.stationLibre))); }catch(e){}
        i++; continue;
      }
    }

    // Implantation
    if(section === "implantation"){
      // Exemple AppLog: "Station TPS : STL1 E= ..." (on récupère le 1er token après ":")
      if(includesNorm(l, "Station") && includesNorm(l, "TPS") && l.indexOf(":")>=0){
        const after = (l.split(":")[1]||"").trim();
        currentImplStationId = (after.split(/\s+/)[0]||null);
        try{ out.timeline.push({type:"IMPLANT_STATION", line:i, id: currentImplStationId}); }catch(e){}
        i++; continue;
      }
      if(includesNorm(l, "ID du point")){
        const pt = {
          id:(l.split(":")[1]||"").trim()||null,
          stationId: currentImplStationId || null,
          theo:{E:null,N:null,H:null},
          mes:{E:null,N:null,H:null},
          d:{dx:null,dy:null,dz:null}
        };
        if(i+1 < lines.length && includesNorm(lines[i+1], "Point vis")) pt.theo = { ...pt.theo, ...parseENH(lines[i+1]) };
        if(i+2 < lines.length && includesNorm(lines[i+2], "Point implant")) pt.mes = { ...pt.mes, ...parseENH(lines[i+2]) };
        if(i+3 < lines.length && includesNorm(lines[i+3], "Ecarts")){
          const de=lines[i+3].match(/\bdE=\s*([-+]?\d+(?:[.,]\d+)?)/i);
          const dn=lines[i+3].match(/\bdN=\s*([-+]?\d+(?:[.,]\d+)?)/i);
          const dh=lines[i+3].match(/\bdH=\s*([-+]?\d+(?:[.,]\d+)?)/i);
          pt.d = { dx:de?numOrNull(de[1]):null, dy:dn?numOrNull(dn[1]):null, dz:dh?numOrNull(dh[1]):null };
        }
        out.implantation.points.push(pt);
        i += 4; continue;
      }
    }

    // Ligne de référence / rabattement
    if(section === "ligneRef" && out.ligneRef.length){
      const cur = out.ligneRef[out.ligneRef.length-1];

      // Station: link this line-ref block to the current setup id
      if(startsWithNorm(l,"Station") && includesNorm(l,"E=")){
        const after = (l.split(":")[1]||"").trim();
        cur.stationId = after.split(/\s+/)[0] || null;
        i++; continue;
      }

      if(includesNorm(l,"ID de la ligne")){ cur.lineId=(l.split(":")[1]||"").trim()||null; i++; continue; }
      if(includesNorm(l,"Point de dbut") || includesNorm(l,"Point de début") || includesNorm(l,"Point de debut")){
        cur.start.id=(l.split(":")[1]||"").trim().split(/\s+/)[0]||null;
        const en=parseENH(l); cur.start.E=en.E; cur.start.N=en.N; cur.start.H=en.H;
        i++; continue;
      }
      if(includesNorm(l,"Point de fin")){
        cur.end.id=(l.split(":")[1]||"").trim().split(/\s+/)[0]||null;
        const en=parseENH(l); cur.end.E=en.E; cur.end.N=en.N; cur.end.H=en.H;
        i++; continue;
      }

      if(includesNorm(l,"ID Point")){
        const rp={ id:(l.split(":")[1]||"").trim()||null, stationId:(cur.stationId||null),
          mes:{E:null,N:null,H:null},
          ec:{dL:null,dT:null,dA:null},
          calc:{E:null,N:null,H:null},
          d:{dx:null,dy:null,dz:null}
        };

        // "Mesur : E= ... N= ... H= ..."
        if(i+1 < lines.length && (includesNorm(lines[i+1],"Mesur") || includesNorm(lines[i+1],"Mesuré"))){
          rp.mes = { ...rp.mes, ...parseENH(lines[i+1]) };
        }
        if(i+2 < lines.length && includesNorm(lines[i+2],"Ecarts")){
          const dl=lines[i+2].match(/\bdL=\s*([-+]?\d+(?:[.,]\d+)?)/i);
          const dt=lines[i+2].match(/\bdT=\s*([-+]?\d+(?:[.,]\d+)?)/i);
          const da=lines[i+2].match(/\bdA=\s*([-+]?\d+(?:[.,]\d+)?)/i);
          rp.ec = { dL:dl?numOrNull(dl[1]):null, dT:dt?numOrNull(dt[1]):null, dA:da?numOrNull(da[1]):null };
        }

        cur.rabPoints.push(rp);
        try{ out.timeline.push({type:"LINEREF_POINT", line:i, id: rp.id, stationId: rp.stationId}); }catch(e){}
        i += 3; continue;
      }
    }

    i++;
  }

  // Post-calc (macro RABBATEMENT) :
  // uE,uN,uH = vecteur unitaire de la ligne (début -> fin)
  // Xth = E0 + dL*uE - dT*uN
  // Yth = N0 + dL*uN + dT*uE
  // Zth = H0 + dL*uH + dA
  // Dx = Xmes - Xth, Dy = Ymes - Yth, Dz = Zmes - Zth
  for(const lr of out.ligneRef){
    const E0 = lr.start.E, N0 = lr.start.N, H0 = lr.start.H;
    const E1 = lr.end.E,   N1 = lr.end.N,   H1 = lr.end.H;

    const dE = (E1!=null && E0!=null) ? (E1 - E0) : null;
    const dN = (N1!=null && N0!=null) ? (N1 - N0) : null;
    const dH = (H1!=null && H0!=null) ? (H1 - H0) : null;

    const L = (dE!=null && dN!=null) ? Math.hypot(dE, dN) : null;
    const uE = (L && L>0) ? dE / L : null;
    const uN = (L && L>0) ? dN / L : null;
    const uH = (L && L>0 && dH!=null) ? dH / L : null;

    for(const rp of lr.rabPoints){
      const dL = rp.ec.dL, dT = rp.ec.dT, dA = rp.ec.dA;
      const Xm = rp.mes.E, Ym = rp.mes.N, Zm = rp.mes.H;

      if(E0!=null && N0!=null && uE!=null && uN!=null && dL!=null && dT!=null){
        const Xth = E0 + dL*uE - dT*uN;
        const Yth = N0 + dL*uN + dT*uE;
        let   Zth = null;
        if(shouldCalcDz() && H0!=null && uH!=null && dA!=null) Zth = H0 + dL*uH + dA;

        rp.calc.E = Xth;
        rp.calc.N = Yth;
        rp.calc.H = (shouldCalcDz() ? Zth : null);

        rp.d.dx = (Xm!=null) ? (Xm - Xth) : null;
        rp.d.dy = (Ym!=null) ? (Ym - Yth) : null;
        rp.d.dz = (shouldCalcDz() && Zm!=null && Zth!=null) ? (Zm - Zth) : null;
      }
    }
  }

  try{ const tl = out.timeline||[]; const c = tl.reduce((a,e)=>{a[e.type]=(a[e.type]||0)+1; return a;},{}); console.info("[AppLog][timeline]", c); }catch(e){}
  
// ---- Points topo (levé) : InstrumentSetup + RawObservation (hors IMP / LigneRef) ----
try{
  // Points topo (levé): keep ALL observed targets, even if they also appear in
  // Implantation or Ligne de référence sections.
  const excluded = new Set();

  const setups = [];
  const setupById = {};
  const setupNodes = Array.from(doc.getElementsByTagName('*')).filter(n=>ln(n)==='InstrumentSetup');
  setupNodes.forEach((n, idx)=>{
    const sid = String(n.getAttribute('id')||'').trim();
    const name = String(n.getAttribute('stationName')||'').trim() || sid;
    let E=null,N=null,H=null;
    try{
      const ip = Array.from(n.childNodes||[]).find(x=>ln(x)==='InstrumentPoint');
      if(ip && ip.textContent){
        const parts = String(ip.textContent).trim().split(/\s+/);
        if(parts.length>=3){ N=num(parts[0]); E=num(parts[1]); H=num(parts[2]); }
      }
    }catch(e){ console.warn('[Nova-Fiches][TXT] Points topo : InstrumentPoint illisible pour la station', sid || '(sans id)', e); }
    const stationKey = nfNormalizeStationKey(sid);
    const o = { setupId:stationKey, stationId:stationKey, idStation:stationKey, stationName:name, __xmlOrder: idx, station:{E,N,H}, observations:[], results:[] };
    setups.push(o);
    if(sid) setupById[sid]=o;
  });
  const obsNodes = Array.from(doc.getElementsByTagName('*')).filter(n=>ln(n)==='RawObservation');
  obsNodes.forEach(n=>{
    const sid = String(n.getAttribute('setupID')||'').trim();
    const st = setupById[sid];
    if(!st) return;

    let pid = "";
    let tE=null,tN=null,tH=null;
    try{
      const tp = Array.from(n.childNodes||[]).find(x=>ln(x)==='TargetPoint');
      if(tp){
        pid = String(tp.getAttribute('name')||tp.getAttribute('pntRef')||'').trim();
        if(!pid && tp.textContent) pid = String(tp.textContent).trim().split(/\s+/)[0]||"";
        if(tp.textContent){
          // LandXML TargetPoint utilise l'ordre N E H (même convention que CgPoint/InstrumentPoint,
          // cf. ligne ~483) - ce site avait N et E inversés, faussant tous les points de "Levé topo".
          const parts = String(tp.textContent).trim().split(/\s+/);
          if(parts.length>=3){ tN=num(parts[0]); tE=num(parts[1]); tH=num(parts[2]); }
        }
      }
    }catch(e){ console.warn('[Nova-Fiches][TXT] Points topo : TargetPoint illisible pour la station', sid || '(sans id)', e); }
    if(!pid) return;
    if(excluded.has(pid)) return;
    if(pid === st.stationName) return;

    const hz = num(n.getAttribute('horizAngle'));
    const vz = num(n.getAttribute('zenithAngle'));
    const dh = num(n.getAttribute('horizDistance'));
    const di = num(n.getAttribute('slopeDistance'));
    const th = num(n.getAttribute('targetHeight'));
    const ts = n.getAttribute('timeStamp') || '';

    st.observations.push({
      __xmlOrder: globalObsOrder++,
      id: pid,
      hz,
      vz,
      // Station-style keys (Option A)
      dp: di,
      hr: th,
      constPrisme: (typeof prismByPoint !== 'undefined' && prismByPoint[String(pid)]!=null) ? prismByPoint[String(pid)] : null,
              
      prismConst: (typeof prismByPoint !== 'undefined' && prismByPoint[String(pid)]!=null) ? prismByPoint[String(pid)] : null,
// legacy keys kept (do not remove)
      dh,
      di,
      th,
      timeStamp: ts
    });
    // Keep results in same order as observations (XML order)
    st.results.push({ id: pid, E: tE, N: tN, H: tH });
  });

  out.topoStations = setups.filter(st => (st.observations?.length||0)>0 || (st.results?.length||0)>0);
}catch(e){ console.warn('[Nova-Fiches][TXT] Section "Points topo" ignorée suite à une erreur de parsing (out.topoStations reste vide).', e); }

// Expose CgPoints for TXT exports (name => {E,N,H,t})
  try{ out.cgPoints = cg; }catch(e){ console.warn('[Nova-Fiches][TXT] Exposition de cgPoints impossible.', e); }

  return out;
}

/* =========================
   LandXML parsing (Leica Captivate / Hexagon LandXML)
   Objectif : convertir un export LandXML en structure "emptyData()" AppLog-compatible,
   pour réutiliser les PDFs existants (Implantation / Ligne de référence / Station / Complet)
   sans toucher au pipeline PDF.

   Données exploitées (si présentes) :
   - InstrumentDetails (instrument/serial)
   - InstrumentSetup (stationName + InstrumentPoint)
   - TPSSetupResult / ResectionResults (stdDev, orientationCorrection, backsight residuals)
   - RawObservation (Hz/V/Dist/hauteurs)
   - ApplicationStakeout (implantation)
   - ApplicationReflineMeasure (ligne de référence)
   - CgPoints (points mesurés, stations, points de profil) avec timeStamp
========================= */
function parseLandXmlLeica(xmlText, fileName){
  // Preserve LandXML (document) order for stations/observations/results
  let stationOrder = 0;
  let globalObsOrder = 0;
  let globalResOrder = 0;

  const out = emptyData();
  // Ensure meta bag exists (used by PdfSharp payload mapping)
  out.meta = out.meta || {};
  out.rawText = (typeof xmlText === 'string') ? xmlText : '';

  const num = (v) => {
    if(v == null) return null;
    const s = String(v).trim();
    if(!s) return null;
    if(s.toLowerCase() === 'nan') return null;
    const n = Number(s.replace(',', '.'));
    return Number.isFinite(n) ? n : null;
  };

  const ln = (node) => {
    try{ return (node && (node.localName || node.tagName)) ? String(node.localName || node.tagName) : ''; }catch(_){ return ''; }
  };

  let doc = null;
  try{
    doc = new DOMParser().parseFromString(String(xmlText||''), 'text/xml');
  }catch(e){
    return out;
  }
  if(!doc || !doc.documentElement) return out;

  // ---- Meta instrument / serial ----
  try{
    const inst = Array.from(doc.getElementsByTagName('*')).find(n => ln(n) === 'InstrumentDetails');
    if(inst){
      out.meta.instrument = inst.getAttribute('model') || inst.getAttribute('instrumentName') || inst.getAttribute('instrumentModel') || inst.getAttribute('instrumentType') || out.meta.instrument;
      out.meta.serial = inst.getAttribute('serialNumber') || inst.getAttribute('instrumentSerialNumber') || out.meta.serial;
    }
  }catch(_){ }
  try{
    // job name sometimes available in ApplicationReflineMeasure.RefLineControlJobName
    const job = Array.from(doc.getElementsByTagName('*')).find(n => ln(n) === 'ApplicationReflineMeasure' && n.getAttribute('RefLineControlJobName'));
    if(job) out.meta.job = job.getAttribute('RefLineControlJobName') || out.meta.job;
  }catch(_){ }
  if(!out.meta.job && fileName) out.meta.job = String(fileName);

  // ---- Operator (creator/author) ----
  try{
    let op = "";
    // 1) <Surveyor name="...">
    const surveyor = Array.from(doc.getElementsByTagName('*')).find(n => ln(n) === 'Surveyor' && (n.getAttribute('name')||'').trim());
    if(surveyor) op = String(surveyor.getAttribute('name')||'').trim();

    

    // 1bis) <Survey ... Creator="..."> (Leica LandXML)
    if(!op){
      const survey = Array.from(doc.getElementsByTagName('*')).find(n => ln(n) === 'Survey' && (n.getAttribute('Creator')||n.getAttribute('creator')||'').trim());
      if(survey) op = String((survey.getAttribute('Creator')||survey.getAttribute('creator')||'')).trim();
    }

// 1ter) <DataSource creator="..."> (Leica LandXML export)
if(!op){
  const ds = Array.from(doc.getElementsByTagName('*'))
    .find(n => ln(n) === 'DataSource' && (n.getAttribute('creator')||n.getAttribute('Creator')||'').trim());
  if(ds) op = String((ds.getAttribute('creator')||ds.getAttribute('Creator')||'')).trim();
}

// 2) <CreatedBy>...</CreatedBy>
    if(!op){
      const createdBy = Array.from(doc.getElementsByTagName('*')).find(n => ln(n) === 'CreatedBy' && (n.textContent||'').trim());
      if(createdBy) op = String(createdBy.textContent||'').trim();
    }

    // 3) root attribute creator="..."
    if(!op){
      const root = doc.documentElement;
      const cAttr = root ? (root.getAttribute('creator') || root.getAttribute('Creator') || '') : '';
      if(cAttr) op = String(cAttr).trim();
    }

    if(op) out.meta.operator = op;
  }catch(_){ }

  // ---- Collect point codes (fallback when CgPoint.@code is missing) ----
  const pointCodeById = {}; // point id => code
  try{
    const pts = Array.from(doc.getElementsByTagName('*')).filter(n => ln(n) === 'Point');
    pts.forEach(p => {
      const uid = p.getAttribute('uniqueID') || p.getAttribute('id') || null;
      if(!uid) return;
      const pc = Array.from(p.childNodes || []).find(ch => ch && ch.nodeType === 1 && ln(ch) === 'PointCode');
      if(!pc) return;
      const code = (pc.getAttribute('code') || '').trim();
      if(code) pointCodeById[uid] = code;
    });
  }catch(_){ }

  // ---- Collect CgPoints (measured points + stations) ----
  const cg = {}; // name => {E,N,H,t,code,oID,role}
  try{
    const nodes = Array.from(doc.getElementsByTagName('*')).filter(n => ln(n) === 'CgPoint');
    nodes.forEach(n => {
      const name = n.getAttribute('name') || n.getAttribute('oID') || n.getAttribute('id');
      const txt = (n.textContent||'').trim();
      if(!name || !txt) return;
      const parts = txt.split(/\s+/).map(x => num(x)).filter(x => x!=null);
      // LandXML CgPoint uses N E H order
      const N = parts.length>=1 ? parts[0] : null;
      const E = parts.length>=2 ? parts[1] : null;
      const H = parts.length>=3 ? parts[2] : null;
      if(E==null || N==null) return;
      const t = n.getAttribute('timeStamp') || null;
      const code = (n.getAttribute('code') || pointCodeById[name] || '').trim() || null;
      const oid = (n.getAttribute('oID') || '').trim() || null;
      const role = (n.getAttribute('role') || '').trim() || null;
      cg[name] = { E, N, H, t, code, oID: oid || String(name), role, name: String(name) };
    });
  }catch(_){ }

  const parseIso = (s) => {
    if(!s) return null;
    const ms = Date.parse(s);
    return Number.isFinite(ms) ? ms : null;
  };

  // ---- Station markers (for stationId inference of stakeout/refline) ----
  const stationTimeline = []; // {id, ms}
  try{
    Object.keys(cg).forEach(k => {
      if(/^STL\d+/i.test(k)){
        const ms = parseIso(cg[k].t);
        if(ms!=null) stationTimeline.push({ id:k, ms });
      }
    });
    stationTimeline.sort((a,b)=>a.ms-b.ms);
  }catch(_){ }
  const inferStationId = (ts) => {
    const ms = parseIso(ts);
    if(ms==null || !stationTimeline.length) return null;
    let best = null;
    for(const s of stationTimeline){ if(s.ms <= ms) best = s; else break; }
    return best ? best.id : stationTimeline[0].id;
  };

  // ---- InstrumentSetup base (stationName + InstrumentPoint) ----
  const baseSetups = {}; // id => {...}
  try{
    const nodes = Array.from(doc.getElementsByTagName('*')).filter(n => ln(n) === 'InstrumentSetup' && n.getAttribute('id'));
    nodes.forEach(n => {
      const id = n.getAttribute('id');
      const stationName = n.getAttribute('stationName') || null;
      const Hi = num(n.getAttribute('instrumentHeight')) ?? null;
      let E=null,N=null,H=null;
      const ip = Array.from(n.childNodes||[]).find(c => ln(c) === 'InstrumentPoint');
      if(ip){
        const t = (ip.textContent||'').trim();
        const p = t.split(/\s+/).map(x=>num(x)).filter(x=>x!=null);
        // InstrumentPoint uses N E H order in LandXML; but in sample it was "8194.. 1665.. 61.." = N E H
        if(p.length>=2){ N=p[0]; E=p[1]; H=p.length>=3?p[2]:null; }
      }
      if(stationName && cg[stationName]){ // prefer CgPoint station coords if present
        E = cg[stationName].E; N = cg[stationName].N; H = cg[stationName].H;
      }
      const prev = baseSetups[id] || null;
      if(prev){
        const currentHasName = !!stationName;
        const prevHasName = !!prev.stationName;
        baseSetups[id] = {
          id,
          stationName: prevHasName ? prev.stationName : stationName,
          Hi: (currentHasName && !prevHasName) ? (Hi ?? prev.Hi) : (prev.Hi ?? Hi),
          E: (currentHasName && !prevHasName) ? (E ?? prev.E) : (prev.E ?? E),
          N: (currentHasName && !prevHasName) ? (N ?? prev.N) : (prev.N ?? N),
          H: (currentHasName && !prevHasName) ? (H ?? prev.H) : (prev.H ?? H)
        };
      }else{
        baseSetups[id] = { id, stationName, Hi, E, N, H };
      }
    });
  }catch(_){ }

  // ---- Setup timeline (prefer setupID over station name for all downstream links) ----
  const setupTimeline = [];
  try{
    Object.values(baseSetups).forEach(st => {
      const ts = (st && st.stationName && cg[st.stationName] && cg[st.stationName].t) ? parseIso(cg[st.stationName].t) : null;
      if(ts!=null) setupTimeline.push({ id: st.id, name: st.stationName || st.id, ms: ts });
    });
    setupTimeline.sort((a,b)=>a.ms-b.ms);
  }catch(_){ }

  const inferSetupId = (ts) => {
    const ms = parseIso(ts);
    if(ms==null || !setupTimeline.length) return null;
    let best = null;
    for(const s of setupTimeline){ if(s.ms <= ms) best = s; else break; }
    return best ? best.id : setupTimeline[0].id;
  };

  const inferStationName = (ts) => {
    const ms = parseIso(ts);
    if(ms==null || !setupTimeline.length) return null;
    let best = null;
    for(const s of setupTimeline){ if(s.ms <= ms) best = s; else break; }
    return best ? (best.name || best.id) : (setupTimeline[0].name || setupTimeline[0].id);
  };

  const nfNormalizeStationKey = (setupId) => {
    const sid = setupId == null ? '' : String(setupId).trim();
    return sid || null;
  };

  // ---- InstrumentSetup detail (RawObservation + TPSSetupResult/ResectionResults) ----

  // ---- RawObservation global index (Leica Captivate LandXML) ----
  // Leica exporte souvent les RawObservation en dehors des TPSSetupResult (référence par setupID).
  // On construit donc un index setupID -> observations[] pour récupérer Hz/V/Dist/HR.
  const rawObsBySetup = {};
  const rawConstByTarget = {}; // targetPntRef -> reflector/prism constant (for meta obs without setupID)
  const prismByPoint = {};      // pointId -> reflector/prism constant (global, regardless of setupID)
  try{
    const ros = Array.from(doc.getElementsByTagName('*')).filter(n=>ln(n)==='RawObservation');
    ros.forEach(ro=>{
      // Leica Captivate exports RawObservation in multiple schemas/namespaces.
      // Sometimes setupID is missing; in that case infer it from the ancestor InstrumentSetup (uniqueID/id).
      let sid = ro.getAttribute('setupID') || ro.getAttribute('setupId') || ro.getAttribute('setup') || ro.getAttribute('setupRef') || null;
      if(!sid){
        try{
          let p = ro.parentNode;
          while(p && p.nodeType===1){
            if(ln(p)==='InstrumentSetup'){
              sid = p.getAttribute('uniqueID') || p.getAttribute('id') || null;
              break;
            }
            p = p.parentNode;
          }
        }catch(_){ }
      }
      if(!sid){
        // Meta observations (HeXML) can omit setupID but carry reflectorConstant.
        let target0 = ro.getAttribute('targetPntRef') || ro.getAttribute('targetPointRef') || ro.getAttribute('targetID') || ro.getAttribute('targetId') || ro.getAttribute('targetPoint') || null;
        if(!target0){
          try{
            const tp0 = Array.from(ro.childNodes||[]).find(c => ln(c) === 'TargetPoint');
            if(tp0) target0 = tp0.getAttribute('pntRef') || tp0.getAttribute('name') || null;
          }catch(_){ }
        }
        const c0 = num(ro.getAttribute('prismConstant')) ?? num(ro.getAttribute('reflectorConstant'));
        if(target0 && c0!=null){
          const k0 = String(target0);
          if(rawConstByTarget[k0]==null) rawConstByTarget[k0] = c0;
          // also store by name without @suffix (common in Leica exports)
          const kShort = k0.includes('@') ? k0.split('@')[0] : k0;
          if(kShort && rawConstByTarget[kShort]==null) rawConstByTarget[kShort] = c0;

          // global map (used by levé/points topo)
          if(prismByPoint[k0]==null) prismByPoint[k0] = c0;
          if(kShort && prismByPoint[kShort]==null) prismByPoint[kShort] = c0;
        }
        return;
      }

      let target = ro.getAttribute('targetPntRef') || ro.getAttribute('targetPointRef') || ro.getAttribute('targetID') || ro.getAttribute('targetId') || ro.getAttribute('targetPoint') || null;
      if(!target){
        try{
          const tp = Array.from(ro.childNodes||[]).find(c => ln(c) === 'TargetPoint');
          if(tp) target = tp.getAttribute('pntRef') || tp.getAttribute('name') || null;
        }catch(_){ }
      }
      const item = {
        id: target || null,
        purpose: ro.getAttribute('purpose') || null,
        hz: num(ro.getAttribute('horizAngle')),
        vz: num(ro.getAttribute('zenithAngle')),
        dp: num(ro.getAttribute('slopeDistance')) ?? num(ro.getAttribute('horizDistance')),
        hr: num(ro.getAttribute('targetHeight')),
        // Prism/reflector constant can be stored as prismConstant or reflectorConstant depending on schema.
        constPrisme: num(ro.getAttribute('prismConstant')) ?? num(ro.getAttribute('reflectorConstant')),
        prismConst: num(ro.getAttribute('prismConstant')) ?? num(ro.getAttribute('reflectorConstant')),
// HeXML RawObservation lines often carry only reflector info (no angles). Mark them so we can merge.
        hasAngles: (num(ro.getAttribute('horizAngle'))!=null) || (num(ro.getAttribute('zenithAngle'))!=null) ||
                  (num(ro.getAttribute('slopeDistance'))!=null) || (num(ro.getAttribute('horizDistance'))!=null) ||
                  (num(ro.getAttribute('targetHeight'))!=null)
      };
      // global map (also when setupID exists)
      try{
        if(item && item.id && item.constPrisme!=null){
          const kAny = String(item.id);
          const kAnyShort = kAny.includes('@') ? kAny.split('@')[0] : kAny;
          if(prismByPoint[kAny]==null) prismByPoint[kAny] = item.constPrisme;
          if(kAnyShort && prismByPoint[kAnyShort]==null) prismByPoint[kAnyShort] = item.constPrisme;
        }
      }catch(_){ }

      if(!rawObsBySetup[sid]) rawObsBySetup[sid] = [];
      rawObsBySetup[sid].push(item);
    });
  }catch(_){ }
  const detailSetups = {}; // (uniqueID|id) => {...}
  try{
    // Leica Captivate / HeXML can use either "uniqueID" or "id" depending on schema.
    // We support both and index the same setup by both keys when available.
    const nodes = Array.from(doc.getElementsByTagName('*')).filter(n => ln(n) === 'InstrumentSetup' && (n.getAttribute('uniqueID') || n.getAttribute('id')));
    nodes.forEach(setup => {
      const uid = setup.getAttribute('uniqueID') || setup.getAttribute('id');
      const uid2 = setup.getAttribute('uniqueID');
      const id2  = setup.getAttribute('id');
      let obs = [];
      const residuals = [];
      let corrOrient=null, devOri=null, devE=null, devN=null, devH=null;

      // Raw observations (Leica: often global, indexed by setupID)
      try{
        const all = rawObsBySetup[uid] || rawObsBySetup[String(uid)] || [];
        // Split LandXML obs (angles/dist) from HeXML "meta" obs (reflectorConstant only)
        const primary = all.filter(o => o && o.hasAngles);
        const meta = all.filter(o => o && !o.hasAngles && o.constPrisme!=null);
        const constById = {};
        try{
          meta.forEach(m=>{
            const k = m && m.id ? String(m.id) : '';
            if(!k) return;
            // keep first (or overwrite, same value in practice)
            if(constById[k]==null) constById[k] = m.constPrisme;
            const kBase = k.indexOf('@')>=0 ? k.split('@')[0] : k;
            if(kBase && constById[kBase]==null) constById[kBase] = m.constPrisme;
          });
        }catch(_){ }

        const pref = primary.filter(o=>{
          const p = String(o?.purpose||'').toLowerCase();
          return p==='resection' || p==='backsight' || p==='resectionobs';
        });
        const use = (pref.length>0) ? pref : primary;
        use.forEach(o=>{
          obs.push({
            id: o?.id ?? null,
            hz: o?.hz ?? null,
            vz: o?.vz ?? null,
            dp: o?.dp ?? null,
            hr: o?.hr ?? null,
            constPrisme: (o?.constPrisme ?? null) ?? (o?.id && constById[String(o.id)]!=null ? constById[String(o.id)] : (o?.id && prismByPoint[String(o.id)]!=null ? prismByPoint[String(o.id)] : (o?.id && rawConstByTarget[String(o.id)]!=null ? rawConstByTarget[String(o.id)] : null))),
            prismConst:  (o?.prismConst ?? o?.constPrisme ?? null) ?? (o?.id && constById[String(o.id)]!=null ? constById[String(o.id)] : (o?.id && prismByPoint[String(o.id)]!=null ? prismByPoint[String(o.id)] : (o?.id && rawConstByTarget[String(o.id)]!=null ? rawConstByTarget[String(o.id)] : null))),
purpose: o?.purpose ?? null
          });
        });
      // Index par ID (pour aligner strictement sur les résidus)
      const obsResectionById = {};
      try{
        obs.forEach(o=>{
          const k = o && o.id ? String(o.id) : '';
          if(!k) return;
          const kBase = k.indexOf('@')>=0 ? k.split('@')[0] : k;
          // lookup const from meta/raw stores if missing on observation
          const kConst2 =
            (o.constPrisme!=null) ? o.constPrisme
            : (constById && constById[k]!=null) ? constById[k]
            : (constById && constById[kBase]!=null) ? constById[kBase]
            : (rawConstByTarget && rawConstByTarget[k]!=null) ? rawConstByTarget[k]
            : (rawConstByTarget && rawConstByTarget[kBase]!=null) ? rawConstByTarget[kBase]
            : null;
          // On conserve en priorité les obs de resection / backsight si renseigné
          const p = String(o.purpose||'').toLowerCase();
          if(!obsResectionById[k] || p==='resection' || p==='backsight' || p==='resectionobs'){
            obsResectionById[k] = {
              id: o.id ?? null,
              hz: o.hz ?? null,
              vz: o.vz ?? null,
              dp: o.dp ?? null,
              hr: o.hr ?? null,
              constPrisme: (o.constPrisme ?? kConst2) ?? null,
              prismConst:  (o.prismConst  ?? kConst2) ?? null,
purpose: o.purpose ?? null
            };
          }
        });
      }catch(_){ }

      }catch(_){ }

// Resection results
      const orient = Array.from(setup.getElementsByTagName('*')).find(n=>ln(n)==='OrientationResults');
      if(orient){
        corrOrient = num(orient.getAttribute('orientationCorrection')) ?? corrOrient;
        devOri = num(orient.getAttribute('stdDevHzOrientation')) ?? devOri;
      }
      const stRes = Array.from(setup.getElementsByTagName('*')).find(n=>ln(n)==='StationResults');
      if(stRes){
        devE = num(stRes.getAttribute('stdDevEast')) ?? devE;
        devN = num(stRes.getAttribute('stdDevNorth')) ?? devN;
        devH = num(stRes.getAttribute('stdDevHgt')) ?? devH;
      }

      // Backsight residuals
      Array.from(setup.getElementsByTagName('*')).filter(n=>ln(n)==='BacksightResults').forEach(br => {
        const usedNode = Array.from(br.getElementsByTagName('*')).find(x=>ln(x)==='UseObs');
        let used = null;
        try{
          if(usedNode){
            const bHz = String(usedNode.getAttribute('bUseHz')||'').toLowerCase()==='true';
            const bVz = String(usedNode.getAttribute('bUseVz')||'').toLowerCase()==='true';
            used = (bHz || bVz) ? 'Oui' : 'Non';
          }
        }catch(_){ }
        // Leica often provides "useKind" (e.g. "3d") instead of <UseObs>.
        // Keep backward compatibility: if UseObs is missing, useKind becomes the displayed value (3D/2D).
        try{
          if(!used){
            const uk = br.getAttribute('useKind') || br.getAttribute('UseKind') || br.getAttribute('usekind') || null;
            if(uk){
              const uks = String(uk).trim();
              if(uks) used = uks.toUpperCase();
            }
          }
        }catch(_){ }
        residuals.push({
          id: br.getAttribute('id') || null,
          dHz: num(br.getAttribute('deltaHz')),
          dAlti: num(br.getAttribute('deltaHgt')),
          dDH: num(br.getAttribute('deltaHzDist')),
          used: used
        });
      });

      
      // Aligner le tableau "Observations" sur le tableau "Résidus" :
      // si on a 3 résidus (3 points), on doit avoir 3 observations correspondantes (mêmes IDs).
      try{
        if(Array.isArray(residuals) && residuals.length > 0){
          const residualsUsed = residuals.filter(r => String(r?.used||'').toLowerCase() !== 'non');
          obs = residualsUsed.map(r => {
            const key = r && r.id ? r.id : null;
            // Constante prisme: priorité au mapping par ID (constById) puis fallback par target (rawConstByTarget)
            const kStr = (key!=null) ? String(key) : null;
            const kBase = (kStr && kStr.includes('@')) ? kStr.split('@')[0] : null;
            const kConst = (kStr && constById[kStr]!=null) ? constById[kStr]
              : (kBase && constById[kBase]!=null) ? constById[kBase]
              : (kStr && rawConstByTarget[kStr]!=null) ? rawConstByTarget[kStr]
              : (kBase && rawConstByTarget[kBase]!=null) ? rawConstByTarget[kBase]
              : null;
            return (key && obsResectionById[key]) ? obsResectionById[key]
              : { id: key, hz:null, vz:null, dp:null, hr:null, constPrisme: kConst, prismConst: kConst };
          });
        }else{
          obs = Object.values(obsResectionById);
        }
      }catch(_){ }
      let method = null, calcHgt = null, stdDevHgt = null, diffHgt = null, previousHgt = null;
      try{
        method = setup.getAttribute('tpsSetupMethod') || setup.getAttribute('method') || null;
        const hRes = Array.from(setup.getElementsByTagName('*')).find(n=>ln(n)==='HeightResults');
        if(hRes){
          calcHgt = num(hRes.getAttribute('calcHgt'));
          stdDevHgt = num(hRes.getAttribute('stdDevHgt'));
          diffHgt = num(hRes.getAttribute('diffHgt'));
          previousHgt = num(hRes.getAttribute('previousHgt'));
        }
      }catch(_){ }
      const obj = { uid, obs, residuals, corrOrient, devOri, devE, devN, devH, method, calcHgt, stdDevHgt, diffHgt, previousHgt };
      // Index by primary key + also by both explicit keys if present.
      detailSetups[uid] = obj;
      if(uid2) detailSetups[uid2] = obj;
      if(id2)  detailSetups[id2]  = obj;
    });
  }catch(_){ }

  // ---- Index dédié Transfert d'altitude ----
  // Leica/Infinity exporte souvent le transfert en deux morceaux portant le même
  // TPSSetupID : le LandXML classique contient les observations, le bloc HeXML
  // contient tpsSetupMethod="heightTransfer" + HeightResults. On fusionne ici
  // explicitement ces deux sources pour éviter de les confondre avec une station libre.
  const heightTransferDetails = {};
  try{
    const htNodes = Array.from(doc.getElementsByTagName('*')).filter(n =>
      ln(n) === 'InstrumentSetup' &&
      String(n.getAttribute('tpsSetupMethod') || n.getAttribute('method') || '').toLowerCase() === 'heighttransfer'
    );
    htNodes.forEach(setup => {
      const sid = setup.getAttribute('uniqueID') || setup.getAttribute('id') || null;
      if(!sid) return;
      const residuals = [];
      Array.from(setup.getElementsByTagName('*')).filter(n=>ln(n)==='BacksightResults').forEach(br=>{
        const uk = br.getAttribute('useKind') || br.getAttribute('UseKind') || '1d';
        residuals.push({
          id: br.getAttribute('id') || null,
          dHz: num(br.getAttribute('deltaHz')),
          dAlti: num(br.getAttribute('deltaHgt')),
          dDH: num(br.getAttribute('deltaHzDist')),
          used: String(uk || '1d').toUpperCase()
        });
      });
      let calcHgt = null, stdDevHgt = null, diffHgt = null, previousHgt = null;
      const hRes = Array.from(setup.getElementsByTagName('*')).find(n=>ln(n)==='HeightResults');
      if(hRes){
        calcHgt = num(hRes.getAttribute('calcHgt'));
        stdDevHgt = num(hRes.getAttribute('stdDevHgt'));
        diffHgt = num(hRes.getAttribute('diffHgt'));
        previousHgt = num(hRes.getAttribute('previousHgt'));
      }
      const metaObs = Array.from(setup.getElementsByTagName('*')).filter(n=>ln(n)==='RawObservation').map(ro=>({
        id: ro.getAttribute('targetPntRef') || ro.getAttribute('targetPointRef') || null,
        constPrisme: num(ro.getAttribute('prismConstant')) ?? num(ro.getAttribute('reflectorConstant')),
        prismConst: num(ro.getAttribute('prismConstant')) ?? num(ro.getAttribute('reflectorConstant')),
        reflectorName: ro.getAttribute('reflectorName') || '',
        edmKind: ro.getAttribute('edmKind') || '',
        hasAngles: false
      }));
      const prev = detailSetups[sid] || {};
      const merged = {
        ...prev,
        uid: sid,
        method: 'heightTransfer',
        calcHgt,
        stdDevHgt,
        diffHgt,
        previousHgt,
        residuals,
        metaObs
      };
      heightTransferDetails[sid] = merged;
      detailSetups[sid] = merged;
    });
  }catch(_){ }

  // ---- Build stationLibreRuns (order from base setups stationName when available; else timeline) ----
  const runs = [];
  try{
    const baseList = Object.values(baseSetups)
      .filter(s => !!s.stationName)
      .sort((a,b)=>{
        const ta = cg[a.stationName]?.t ? parseIso(cg[a.stationName].t) : null;
        const tb = cg[b.stationName]?.t ? parseIso(cg[b.stationName].t) : null;
        if(ta!=null && tb!=null) return ta-tb;
        return String(a.stationName||'').localeCompare(String(b.stationName||''));
      });

    baseList.forEach(s => {
      const det = detailSetups[s.id] || detailSetups[s.id.replace('id','')] || detailSetups[s.stationName] || null;
      const isHeightTransfer = !!heightTransferDetails[s.id] || String(det?.method || "").toLowerCase() === "heighttransfer";
      if(isHeightTransfer) return;
      const stationKey = nfNormalizeStationKey(s.id || null);
      const run = {
        setupId: stationKey,
        stationId: stationKey,
        stationName: s.stationName || null,
        method: "Station libre",
        observations: Array.isArray(det?.obs) ? det.obs : [],
        residuals: Array.isArray(det?.residuals) ? det.residuals : [],
        results: {
          idStation: stationKey,
          setupId: stationKey,
          stationName: s.stationName || null,
          method: "Station libre",
          E: s.E, N: s.N, H: s.H,
          Hi: s.Hi,
          corrOrient: det?.corrOrient ?? null,
          azOrient: null,
          devE: det?.devE ?? null,
          devN: det?.devN ?? null,
          devH: det?.devH ?? null,
          devOri: det?.devOri ?? null
        }
      };
      runs.push(run);
    });
  }catch(_){ }

  // ---- GNSS (RTK) : pas de mise en station TPS - un fix par point mesuré, pas de résection.
  // Si aucune station TPS n'a été trouvée mais que le fichier contient des GPSSetup, on
  // construit un run de synthèse (récepteur + référence RTK) pour que la fiche "Station"
  // affiche ces infos au lieu de rester vide. Le GPSSetup sans GPSReceiverDetailsID est la
  // référence RTK (correction réseau/RTCM) ; les autres sont les fix du rover, un par point.
  if(runs.length === 0){
    try{
      const gpsSetupNodes = Array.from(doc.getElementsByTagName('*')).filter(n => ln(n) === 'GPSSetup');
      if(gpsSetupNodes.length){
        const receiverById = {};
        Array.from(doc.getElementsByTagName('*')).filter(n => ln(n) === 'GPSReceiverDetails').forEach(n => {
          const id = n.getAttribute('id');
          if(id) receiverById[id] = {
            manufacturer: (n.getAttribute('manufacturer') || '').trim(),
            model: (n.getAttribute('model') || '').trim(),
            serialNumber: (n.getAttribute('serialNumber') || '').trim()
          };
        });

        const refNode = gpsSetupNodes.find(n => !n.getAttribute('GPSReceiverDetailsID'));
        const roverNode = gpsSetupNodes.find(n => !!n.getAttribute('GPSReceiverDetailsID'));

        let rtkRef = null;
        if(refNode){
          const tgtNode = Array.from(refNode.getElementsByTagName('*')).find(n => ln(n) === 'TargetPoint');
          let E = null, N = null, H = null;
          if(tgtNode){
            const parts = (tgtNode.textContent || '').trim().split(/\s+/).map(x => num(x)).filter(x => x != null);
            // Meme convention que CgPoint : N E H
            if(parts.length >= 1) N = parts[0];
            if(parts.length >= 2) E = parts[1];
            if(parts.length >= 3) H = parts[2];
          }
          rtkRef = { name: refNode.getAttribute('stationName') || null, E, N, H };
        }

        let receiver = null;
        let antennaHeight = null;
        if(roverNode){
          const rec = receiverById[roverNode.getAttribute('GPSReceiverDetailsID')];
          if(rec) receiver = [rec.manufacturer, rec.model].filter(Boolean).join(' ') + (rec.serialNumber ? ` (série ${rec.serialNumber})` : '');
          antennaHeight = num(roverNode.getAttribute('antennaHeight'));
        }

        const gnssRun = {
          setupId: 'GNSS',
          stationId: null,
          stationName: null,
          method: 'GNSS',
          observations: [],
          residuals: [],
          results: { idStation: null, method: 'GNSS', receiver, antennaHeight, rtkRef }
        };
        runs.push(gnssRun);
      }
    }catch(_){ }
  }

  out.stationLibreRuns = runs;

  // ---- Transfert d'altitude (Leica heightTransfer) ----
  try{
    const stripAtLocal = (s)=>{
      s = (s==null) ? "" : String(s);
      const i = s.indexOf("@");
      return i >= 0 ? s.substring(0, i) : s;
    };
    const pointInfo = (id)=>{
      const k = id == null ? "" : String(id);
      const b = stripAtLocal(k);
      const p = cg[k] || cg[b] || null;
      return p ? { id: b || k, E:p.E, N:p.N, H:p.H, t:p.t, role:p.role || "", code:p.code || "" } : { id: b || k, E:null, N:null, H:null, t:null, role:"", code:"" };
    };
    const ht = [];
    Object.keys(heightTransferDetails).forEach(sid=>{
      const s = baseSetups[sid] || { id:sid, stationName:sid, E:null, N:null, H:null, Hi:null };
      const det = heightTransferDetails[sid] || detailSetups[sid] || {};
      const allObs = (rawObsBySetup[sid] || []).filter(o=>o && o.hasAngles);
      const refs = (Array.isArray(det?.residuals) ? det.residuals : []).map(r=>{
        const info = pointInfo(r?.id);
        const obs = allObs.find(o => stripAtLocal(o?.id) === stripAtLocal(r?.id)) || null;
        return {
          id: stripAtLocal(r?.id),
          useKind: r?.used || "1D",
          deltaHgt: r?.dAlti ?? null,
          point: info,
          observation: obs ? { hz:obs.hz, vz:obs.vz, dp:obs.dp, hr:obs.hr, constPrisme:obs.constPrisme, purpose:obs.purpose } : null
        };
      });
      const refIds = new Set(refs.map(r=>stripAtLocal(r.id)).filter(Boolean));
      const measured = allObs
        .filter(o => String(o?.purpose||"").toLowerCase() === "normal" && !refIds.has(stripAtLocal(o?.id)))
        .map(o => ({ id: stripAtLocal(o.id), point: pointInfo(o.id), observation: { hz:o.hz, vz:o.vz, dp:o.dp, hr:o.hr, constPrisme:o.constPrisme, purpose:o.purpose } }));
      ht.push({
        setupId: sid,
        stationId: sid,
        stationName: s.stationName || s.id,
        method: "Transfert d'altitude",
        E: s.E, N: s.N, H: s.H, Hi: s.Hi,
        stationHOriginal: s.H,
        calcHgt: det?.calcHgt ?? null,
        stdDevHgt: det?.stdDevHgt ?? null,
        diffHgt: det?.diffHgt ?? null,
        previousHgt: det?.previousHgt ?? null,
        references: refs,
        measuredPoints: measured,
        analysis: {
          referenceCount: refs.length,
          measuredCount: measured.length,
          maxDeltaHgt: refs.reduce((m,r)=> (r.deltaHgt==null ? m : Math.max(m, Math.abs(Number(r.deltaHgt)))), 0)
        }
      });
    });
    out.heightTransfers = ht;
  }catch(_){ out.heightTransfers = []; }

  // UI compat: la vue "Station libre" affiche data.stationLibre (historique),
  // donc on copie la première run si disponible.
  if(runs.length>0) out.stationLibre = runs[0];
  else out.stationLibre = { observations: [], residuals: [], results: {} };


  // ---- Mapping réel point -> setup (prioritaire, partagé implantation / ligne de réf) ----
  const stripAt = (s)=>{
    s = (s==null)?"":String(s);
    const i = s.indexOf('@');
    return (i>=0)?s.substring(0,i):s;
  };
  const pointToSetup = {};
  try{
    const obsNodes = Array.from(doc.getElementsByTagName('*')).filter(n=>ln(n)==='RawObservation');
    obsNodes.forEach(ro=>{
      const setupId = ro.getAttribute('setupID') || ro.getAttribute('SetupID') || null;
      if(!setupId) return;
      const mappedSetupId = (baseSetups && baseSetups[setupId] && baseSetups[setupId].id) ? baseSetups[setupId].id : setupId;
      if(!mappedSetupId) return;
      const tp = Array.from(ro.getElementsByTagName('*')).find(x=>ln(x)==='TargetPoint');
      if(!tp) return;
      const pref = tp.getAttribute('pntRef') || tp.getAttribute('name') || null;
      if(!pref) return;
      pointToSetup[String(pref)] = mappedSetupId;
      pointToSetup[stripAt(pref)] = mappedSetupId;
    });
  }catch(_){ }

  // ---- ApplicationStakeout -> Implantation points ----
  // Priorité aux blocs <ApplicationStakeout> si présents.
  // IMPORTANT: ne pas casser le fallback (heuristiques existantes) => on ne remplace l'implantation QUE si on a au moins 1 node.
  try{
    const nodes = Array.from(doc.getElementsByTagName('*')).filter(n=>ln(n)==='ApplicationStakeout');
    if(nodes.length>0){
      const points = []; // ordre d'apparition LandXML, sans dedoublonnage force

      nodes.forEach(n => {
        const stakedId = n.getAttribute('StakedPointID') || null;
        const designId = n.getAttribute('DesignPointID') || null;
        const key = (designId || stakedId || '').trim();
        if(!key) return;

        const theoE = num(n.getAttribute('DesignPointEasting'));
        const theoN = num(n.getAttribute('DesignPointNorthing'));
        const theoH = num(n.getAttribute('DesignPointOrthoHeight'));

        const mes = stakedId && cg[stakedId] ? cg[stakedId] : null;
        let mesE = mes ? mes.E : null;
        let mesN = mes ? mes.N : null;
        let mesH = mes ? mes.H : null;

        let dx = (mesE!=null && theoE!=null) ? (mesE-theoE) : num(n.getAttribute('StakeoutEastingDiff'));
        let dy = (mesN!=null && theoN!=null) ? (mesN-theoN) : num(n.getAttribute('StakeoutNorthingDiff'));
        // DesignPointOrthoHeight="0.000000" chez Leica signifie "pas de cible d'altitude"
        // (implantation planimétrique seule, cas courant pour des pieux/poteaux), pas une
        // vraie cible à l'altitude zéro. Sans ce garde-fou, dz = mesH - 0 = mesH : la colonne
        // "Dz / dA" du PDF affichait l'altitude mesurée brute comme si c'était un écart de
        // contrôle. StakeoutHeightDiff (repli Leica) est tout aussi faux dans ce cas précis,
        // donc on laisse dz à null plutôt que d'essayer les deux.
        let dz = (theoH === 0) ? null
          : (mesH!=null && theoH!=null) ? (mesH-theoH) : num(n.getAttribute('StakeoutHeightDiff'));

        // Reprise d'implantation (ex: "IPt_337@96" généré par Leica quand un point est
        // réimplanté) : la position mesurée n'est pas conservée sous ce nom exact dans le
        // LandXML (seul l'écart Stakeout*Diff de cette tentative l'est). On la reconstruit
        // depuis théo + écart plutôt que de laisser les cellules "mesuré" vides.
        if(mesE==null && theoE!=null && dx!=null) mesE = theoE + dx;
        if(mesN==null && theoN!=null && dy!=null) mesN = theoN + dy;
        if(mesH==null && theoH!=null && dz!=null) mesH = theoH + dz;

        const appTime = n.getAttribute('ApplicationStartDateTime') || null;
        // Deterministic station attribution:
        // 1) by measured point id (stakedId) present in observations (TargetPoint@pntRef)
        // 2) by design point id (designId)
        // 3) fallback to timestamp heuristic
        const stId = nfNormalizeStationKey(
          (stakedId && (pointToSetup[stakedId] || pointToSetup[stripAt(stakedId)]))
          || (designId && (pointToSetup[designId] || pointToSetup[stripAt(designId)]))
          || inferSetupId(appTime)
          || null
        );
        const businessPointId = (mes && mes.oID) ? String(mes.oID) : stripAt(stakedId || designId || '');
        const stationName = (stId && baseSetups && baseSetups[stId] && baseSetups[stId].stationName) ? baseSetups[stId].stationName : inferStationName(appTime);
        const rowPid = stakedId || `${designId || key}#${points.length + 1}`;
        const candidate = {
          // id affiché (théorique) : DesignPointID si dispo, sinon StakedPointID
          id: designId || stakedId || '',
          code: (stakedId && cg[stakedId]?.code) || (designId && cg[designId]?.code) || null,
          // identifiant mesuré (utile pour filtrer le levé topo si l'export utilise StakedPointID côté mesures)
          stakedId: stakedId,
          occurrenceId: stakedId || '',
          businessPointId,
          timeStamp: appTime || (mes && mes.t) || null,
          stationId: stId,
          stationName: stationName || null,
          theo: { E: theoE, N: theoN, H: theoH },
          mes: { E: mesE, N: mesN, H: mesH },
          d: { dx, dy, dz },
          __nfPid: rowPid
        };

        points.push(candidate);
      });

      // Remplacement effectif uniquement ici (=> fallback intact si nodes.length==0)
      out.implantation.points = points.filter(Boolean);
    }
  }catch(_){ }


  
// ---- ApplicationReflineMeasure -> Ligne de référence ----
try{
  const nodes = Array.from(doc.getElementsByTagName('*')).filter(n=>{
    const name = String(ln(n)||'').toLowerCase();
    return name === 'applicationreflinemeasure' || name === 'applicationreflinemeasure';
  });

  const ptMap = pointToSetup || {};
  const byLine = {};

  const normLineVec = (sE,sN,eE,eN) => {
    const vx = (eE-sE), vy=(eN-sN);
    const L = Math.sqrt(vx*vx+vy*vy);
    if(!Number.isFinite(L) || L<=0) return null;
    return { ux:vx/L, uy:vy/L, L };
  };

  nodes.forEach(n => {
    const lineId = n.getAttribute('RefLine_ID') || n.getAttribute('RefLineID') || n.getAttribute('RefLineControlJobName') || 'Ligne';
    const appTs = n.getAttribute('ApplicationStartDateTime') || n.getAttribute('ApplicationEndDateTime') || null;
    const groupKey = String(lineId) + '|' + String(appTs||'');

    const sId = n.getAttribute('RefLineStartPointID') || null;
    const eId = n.getAttribute('RefLineEndPointID') || null;
    const sE = num(n.getAttribute('RefLineStartPointEast'));
    const sN = num(n.getAttribute('RefLineStartPointNorth'));
    const sH = num(n.getAttribute('RefLineStartPointOrthoHeight'));
    const eE = num(n.getAttribute('RefLineEndPointEast'));
    const eN = num(n.getAttribute('RefLineEndPointNorth'));
    const eH = num(n.getAttribute('RefLineEndPointOrthoHt')) ?? num(n.getAttribute('RefLineEndPointOrthoHeight'));

    const measuredPointId = n.getAttribute('RefLineMeasPointID') || null;
    const strippedPointId = (typeof stripAt === 'function') ? stripAt(measuredPointId) : measuredPointId;

    const stationSetupId = nfNormalizeStationKey(
        (measuredPointId && (ptMap[measuredPointId] || ptMap[stripAt(measuredPointId)]))
        || inferSetupId(appTs)
        || null
    );

    const stationName =
      (stationSetupId && baseSetups && baseSetups[stationSetupId] && baseSetups[stationSetupId].stationName) ?
        baseSetups[stationSetupId].stationName :
        ((typeof inferStationName === 'function' ? inferStationName(appTs) : null) ||
         (runs.length>0 ? ((runs[0]?.stationName ?? runs[0]?.results?.stationName ?? runs[0]?.results?.idStation) ?? null) : null));

    if(!byLine[groupKey]){
      // 2.3.0.93: priorité mapping réel -> fallback horodatage
      byLine[groupKey] = {
        stationId: stationSetupId || null,
        stationName: stationName || stationSetupId || null,
        lineId: lineId,
        start:{ id:sId, E:sE, N:sN, H:sH },
        end:{ id:eId, E:eE, N:eN, H:eH },
        rabPoints:[]
      };
    }

    const chain = num(n.getAttribute('RefLineMeasStkChainage')) ?? num(n.getAttribute('RefLineMeasStkDistStart')) ?? 0;
    const off = num(n.getAttribute('RefLineMeasStkOffset')) ?? 0;
    const hoff = num(n.getAttribute('RefLineMeasStkHtOffset')) ?? 0;
    const mes = measuredPointId && cg[measuredPointId] ? cg[measuredPointId] : (strippedPointId && cg[strippedPointId] ? cg[strippedPointId] : null);

    // Théorique prioritaire : utiliser directement le point de base exporté par Leica quand il existe.
    // C'est la source la plus fiable pour la position théorique de la mesure sur ligne.
    let calcE = num(n.getAttribute('RefLineBasePointEast'));
    let calcN = num(n.getAttribute('RefLineBasePointNorth'));
    let calcH = num(n.getAttribute('RefLineBasePointHeight'));

    // Fallback robuste : reconstruction géométrique si le point de base n'est pas fourni.
    const v = (sE!=null && sN!=null && eE!=null && eN!=null) ? normLineVec(sE,sN,eE,eN) : null;
    if((calcE==null || calcN==null || calcH==null) && v){
      const px = -v.uy;
      const py =  v.ux;
      const reconE = sE + v.ux*chain + px*off;
      const reconN = sN + v.uy*chain + py*off;
      let reconH = null;
      if(sH!=null && eH!=null){
        reconH = sH + ((eH-sH)/v.L)*chain + hoff;
      }else if(sH!=null){
        reconH = sH + hoff;
      }
      if(calcE==null) calcE = reconE;
      if(calcN==null) calcN = reconN;
      if(calcH==null) calcH = reconH;
    }

    const dx = (mes?.E!=null && calcE!=null) ? (mes.E-calcE) : null;
    const dy = (mes?.N!=null && calcN!=null) ? (mes.N-calcN) : null;
    const dz = (mes?.H!=null && calcH!=null) ? (mes.H-calcH) : null;

    byLine[groupKey].rabPoints.push({
      id: measuredPointId,
      stationId: byLine[groupKey].stationId,
      stationName: byLine[groupKey].stationName,
      code: mes?.code || (measuredPointId && pointCodeById[measuredPointId]) || (strippedPointId && pointCodeById[strippedPointId]) || null,
      stakedId: measuredPointId,
      occurrenceId: measuredPointId || '',
      businessPointId: (mes && mes.oID) ? String(mes.oID) : stripAt(measuredPointId || ''),
      timeStamp: appTs || (mes && mes.t) || null,
      __nfPid: measuredPointId || `${strippedPointId || 'LR'}#${byLine[groupKey].rabPoints.length + 1}`,
      mes: { E: mes?.E ?? null, N: mes?.N ?? null, H: mes?.H ?? null },
      ec: { dL: chain, dT: off, dA: hoff },
      calc: { E: calcE, N: calcN, H: calcH },
      d: { dx, dy, dz }
    });
  });

  out.ligneRef = Object.values(byLine);
}catch(err){
  try{ console.warn('[LandXML][RefLine] parse failed', err); }catch(_){}
}


  
// ---- Points topo (levé) : InstrumentSetup + RawObservation (hors IMP / LigneRef) ----
try{
  const excluded = new Set();
  try{ (out.implantation?.points||[]).forEach(pt=>{ if(pt && pt.id) excluded.add(String(pt.id)); if(pt && pt.stakedId) excluded.add(String(pt.stakedId)); }); }catch(_){}
  try{ (out.ligneRef||[]).forEach(line=>{ 
    // points mesurés de la ligne
    (line?.rabPoints||[]).forEach(p=>{ if(p && p.id) excluded.add(String(p.id)); });
    // points de base (début/fin) : ne doivent pas polluer le levé topo
    if(line?.start?.id) excluded.add(String(line.start.id));
    if(line?.end?.id) excluded.add(String(line.end.id));
  }); }catch(_){}
  // Points utilisés pour la mise en station (station libre) : ne doivent pas apparaître en levé topo
  try{
    (out.stationLibreRuns||[]).forEach(r=>{
      (r?.observations||[]).forEach(o=>{ if(o && o.id) excluded.add(String(o.id)); });
      (r?.residuals||[]).forEach(o=>{ if(o && o.id) excluded.add(String(o.id)); });
      if(r?.results?.idStation) excluded.add(String(r.results.idStation));
    });
  }catch(_){}

  const setups = [];
  const setupById = {};
  const setupNodes = Array.from(doc.getElementsByTagName('*')).filter(n=>ln(n)==='InstrumentSetup');
  setupNodes.forEach((n, idx)=>{
    const sid = String(n.getAttribute('id')||'').trim();
    const name = String(n.getAttribute('stationName')||'').trim() || sid;
    let E=null,N=null,H=null;
    try{
      const ip = Array.from(n.childNodes||[]).find(x=>ln(x)==='InstrumentPoint');
      if(ip && ip.textContent){
        const parts = String(ip.textContent).trim().split(/\s+/);
        if(parts.length>=3){ N=num(parts[0]); E=num(parts[1]); H=num(parts[2]); }
      }
    }catch(e){ console.warn('[Nova-Fiches][LandXML] Points topo : InstrumentPoint illisible pour la station', sid || '(sans id)', e); }
    const o = { setupId:sid, stationName:name, __xmlOrder: idx, station:{E,N,H}, observations:[], results:[] };
    setups.push(o);
    if(sid) setupById[sid]=o;
  });

  const obsNodes = Array.from(doc.getElementsByTagName('*')).filter(n=>ln(n)==='RawObservation');
  obsNodes.forEach(n=>{
    const sid = String(n.getAttribute('setupID')||'').trim();
    const st = setupById[sid];
    if(!st) return;

    let pid = "";
    let tE=null,tN=null,tH=null;
    try{
      const tp = Array.from(n.childNodes||[]).find(x=>ln(x)==='TargetPoint');
      if(tp){
        pid = String(tp.getAttribute('name')||tp.getAttribute('pntRef')||'').trim();
        if(!pid && tp.textContent) pid = String(tp.textContent).trim().split(/\s+/)[0]||"";
        if(tp.textContent){
          // LandXML TargetPoint utilise l'ordre N E H (même convention que CgPoint/InstrumentPoint,
          // cf. ligne ~483) - ce site avait N et E inversés, faussant tous les points de "Levé topo".
          const parts = String(tp.textContent).trim().split(/\s+/);
          if(parts.length>=3){ tN=num(parts[0]); tE=num(parts[1]); tH=num(parts[2]); }
        }
      }
    }catch(e){ console.warn('[Nova-Fiches][LandXML] Points topo : TargetPoint illisible pour la station', sid || '(sans id)', e); }
    if(!pid) return;
    if(excluded.has(pid)) return;
    if(pid === st.stationName) return;

    const hz = num(n.getAttribute('horizAngle'));
    const vz = num(n.getAttribute('zenithAngle'));
    const dh = num(n.getAttribute('horizDistance'));
    const di = num(n.getAttribute('slopeDistance'));
    const th = num(n.getAttribute('targetHeight'));
    const ts = n.getAttribute('timeStamp') || '';

    // 1) Observations : ordre XML strict
    st.observations.push({
      __xmlOrder: globalObsOrder++,
      id: pid,
      hz,
      vz,
      // Station-style keys (Option A)
      dp: di,
      hr: th,
      constPrisme: (typeof prismByPoint !== 'undefined' && prismByPoint[String(pid)]!=null) ? prismByPoint[String(pid)] : null,
              
      prismConst: (typeof prismByPoint !== 'undefined' && prismByPoint[String(pid)]!=null) ? prismByPoint[String(pid)] : null,
// legacy keys kept (do not remove)
      dh,
      di,
      th,
      timeStamp: ts
    });

    // 2) Résultats rectangulaires : même ordre que l'observation (XML)
    st.results.push({
      __xmlOrder: globalResOrder++,
      id: pid,
      code: (pid && cg[pid]?.code) || (pid && pointCodeById[pid]) || null,
      E: tE,
      N: tN,
      H: tH
    });
  });

  out.topoStations = setups.filter(st => (st.observations?.length||0)>0 || (st.results?.length||0)>0);
}catch(e){ console.warn('[Nova-Fiches][LandXML] Section "Points topo" ignorée suite à une erreur de parsing (out.topoStations reste vide).', e); }

// Expose CgPoints for TXT exports (name => {E,N,H,t})
  try{ out.cgPoints = cg; }catch(e){ console.warn('[Nova-Fiches][LandXML] Exposition de cgPoints impossible.', e); }

  return out;
}

// Garde-fou (audit 06/07/2026) : les coordonnées LandXML ne sont jamais validées en plage
// plausible avant usage (parsing/export/rendu). On journalise ici les anomalies sans jamais
// modifier les données ni interrompre le traitement : un fichier corrompu ou une valeur
// aberrante (ex. 1e12, NaN) reste utilisable tel quel, mais devient diagnosticable.
const NF_COORD_ABS_LIMIT = 20000000; // couvre largement Lambert legacy, RGF93/CC et WGS84*1e6

function nfIsPlausibleCoordinateValue(value){
  return typeof value === "number" && Number.isFinite(value) && Math.abs(value) <= NF_COORD_ABS_LIMIT;
}

function nfScanCoordinateAnomalies(data, fileName){
  const KEYS = new Set(["E", "N", "H", "X", "Y", "Z"]);
  const seen = new Set();
  let anomalies = 0;

  function walk(node, path){
    if(!node || typeof node !== "object" || seen.has(node)) return;
    seen.add(node);
    for(const key of Object.keys(node)){
      const value = node[key];
      if(KEYS.has(key) && typeof value === "number"){
        if(!nfIsPlausibleCoordinateValue(value)){
          anomalies++;
          console.warn(`[Nova-Fiches] Coordonnée suspecte (${key}=${value}) dans ${fileName || "fichier importé"} (${path}.${key}).`);
        }
      }else if(value && typeof value === "object"){
        walk(value, path ? `${path}.${key}` : key);
      }
    }
  }

  try{ walk(data, ""); }catch(e){ console.warn("[Nova-Fiches] Analyse de plausibilité des coordonnées interrompue.", e); }
  return anomalies;
}

// IMPORTANT : on capture la référence de la fonction d'origine AVANT de réaffecter
// window.parseLandXmlLeica. `function parseLandXmlLeica(){}` déclarée au niveau supérieur
// d'un script classique crée à la fois la variable globale "parseLandXmlLeica" ET la
// propriété window.parseLandXmlLeica, sur le MÊME binding. Si le wrapper ci-dessous
// appelait l'identifiant nu "parseLandXmlLeica" directement, cet appel se résoudrait -
// au moment de l'exécution - vers la valeur COURANTE du binding global, c'est-à-dire le
// wrapper lui-même (puisqu'il vient d'être réaffecté à window.parseLandXmlLeica) :
// récursion infinie ("Maximum call stack size exceeded") au premier import LandXML.
const nfOriginalParseLandXmlLeica = parseLandXmlLeica;
window.parseLandXmlLeica = function(xmlText, fileName){
  const data = nfOriginalParseLandXmlLeica(xmlText, fileName);
  try{ nfScanCoordinateAnomalies(data, fileName); }catch(_){ }
  return data;
};

window.nfEnterLandXmlModule = function(targetId){
  try{
    // LandXML import stays neutral. Duplicate choices are handled in the
    // visualisation table by checking/unchecking occurrences.
    return;
  }catch(e){
    console.error('[LandXML] duplicate policy apply failed', e);
  }
};

/* =========================
   HTML rendering
========================= */
// Garde-fou (audit 06/07/2026) : aucune limite n'existait sur le nombre de lignes rendues,
// un LandXML anormalement volumineux pouvait créer des dizaines de milliers de <tr> et
// bloquer/planter la WebView2. Le rendu est plafonné ; les données réelles (analyse, export,
// PDF) ne sont pas affectées, seul l'affichage de ce tableau est tronqué.
const NF_TABLE_MAX_ROWS = 3000;

function tableHtml(headers, rows){
  const cell = (c) => {
    // Allow inline HTML only via explicit opt-in ({__html:true, value:...}) for cells we
    // generate ourselves (status badges). Never infer trust from string content (e.g. a
    // "starts with <span" heuristic) : a point code/ID imported from a LandXML/TXT file
    // could otherwise be crafted to bypass escaping and inject HTML/script.
    if(c && typeof c === "object" && c.__html === true) return String(c.value ?? "");
    return esc(String(c ?? ""));
  };

  const totalRows = Array.isArray(rows) ? rows.length : 0;
  const truncated = totalRows > NF_TABLE_MAX_ROWS;
  const displayedRows = truncated ? rows.slice(0, NF_TABLE_MAX_ROWS) : rows;
  const warningHtml = truncated
    ? `<div class="small" style="margin:0 0 8px;padding:6px 10px;border:1px solid #ffb020;background:#fff7d6;border-radius:8px;color:#5f3b00;">
         Affichage limité à ${NF_TABLE_MAX_ROWS} lignes sur ${totalRows} (le calcul et l'export portent bien sur l'ensemble des données).
       </div>`
    : "";

  return `
    ${warningHtml}
    <div>
      <table>
        <thead><tr>${headers.map(h=>`<th>${esc(h)}</th>`).join("")}</tr></thead>
        <tbody>${displayedRows.map(r=>{
          const cells = (r && typeof r === "object" && r.__row) ? (Array.isArray(r.cells) ? r.cells : []) : r;
          const style = (r && typeof r === "object" && r.__row && r.style) ? ` style="${String(r.style)}"` : "";
          return `<tr${style}>${(Array.isArray(cells)?cells:[]).map(c=>`<td class="mono">${cell(c)}</td>`).join("")}</tr>`;
        }).join("")}</tbody>
      </table>
    </div>`;
}

// -------------------------------
// PATCH 2 (UI) — Exclusion de points (tous onglets de visualisation)
// Objectif: permettre de cocher/décocher des points dans *toutes* les visualisations,
// sans impacter la génération PDF (ce sera le Patch 3).
// -------------------------------
// Key = "<idPoint>" (on normalise au maximum). Stockage en mémoire uniquement.
let nfExcludedPointIds = new Set();
let nfRefAltiPointIds = new Set();
let nfLastRawText = null;
// ids visibles par onglet (pour le compteur + actions Tout inclure/exclure)
let nfViewPointIds = { station: [], implant: [], lineref: [], refalti: [], topo: [] };
// Derniers arguments de renderStationMap (onglet "Plan station"), rejoués au
// tab-switch car le conteneur a une taille nulle tant qu'il est masqué.
let nfLastStationMapArgs = null;
// stations/points dérivés au dernier renderStationMap (pour l'export PDF, voir plus bas).
let nfLastStationMapDerived_ = { stations: [], points: [] };
// Coché via #stationMapSendToPdf : inclure ou non le plan station en dernière page du PDF Station.
let nfStationMapSendToPdf_ = false;

function nfPointCode_(p){
  try{
    return String((p?.code ?? p?.Code ?? p?.pointCode ?? p?.PointCode ?? "")).trim();
  }catch(_){ return ""; }
}

function nfAssignSource_(p, src){
  try{ return Object.assign({ __nfSource: src || '' }, p || {}); }catch(_){ return p; }
}

function nfEffectiveCode_(p, src){
  try{
    const pp = nfAssignSource_(p, src);
    if(typeof window.NF_resolvePointCode === 'function') return String(window.NF_resolvePointCode(pp) || '').trim();
    if(typeof window.NF_getGlobalZoneCode === 'function'){
      const g = String(window.NF_getGlobalZoneCode() || '').trim();
      if(g) return g;
    }
    return nfPointCode_(pp);
  }catch(_){ return nfPointCode_(p); }
}

function nfZoneLabel_(p, src){
  const c = nfEffectiveCode_(p, src);
  return c || "SANS CODE";
}

function nfSortByZoneThenId_(arr){
  const a = Array.isArray(arr) ? arr.slice() : [];
  a.sort((p1, p2) => {
    const z1 = nfZoneLabel_(p1, p1?.__nfSource);
    const z2 = nfZoneLabel_(p2, p2?.__nfSource);
    if(z1 !== z2) return z1.localeCompare(z2, 'fr', { numeric:true, sensitivity:'base' });
    const i1 = String(p1?.id ?? '');
    const i2 = String(p2?.id ?? '');
    return i1.localeCompare(i2, 'fr', { numeric:true, sensitivity:'base' });
  });
  return a;
}

function nfZoneSummaryHtml_(points){
  try{
    const counts = new Map();
    for(const p of (Array.isArray(points) ? points : [])){
      const z = nfZoneLabel_(p, p?.__nfSource);
      counts.set(z, (counts.get(z) || 0) + 1);
    }
    if(!counts.size) return `<div class="small">Aucun code détecté.</div>`;
    const chunks = Array.from(counts.entries())
      .sort((a,b)=> String(a[0]).localeCompare(String(b[0]), 'fr', { numeric:true, sensitivity:'base' }))
      .map(([z,n]) => `<span class="st st-na" style="margin-right:6px;">Code ${esc(z)} : ${esc(String(n))}</span>`);
    return `<div class="small" style="margin:0 0 8px;">${chunks.join('')}</div>`;
  }catch(_){
    return `<div class="small">Aucun code détecté.</div>`;
  }
}

const nfStripAt = (v) => String(v ?? '').replace(/@.*$/, '');
function nfPid(id){
  return String(id ?? '').trim();
}

function nfRowPid_(p){
  try{ return nfPid(p?.__nfPid || p?.stakedId || p?.occurrenceId || p?.id); }catch(_){ return ""; }
}

function nfBusinessPointKey_(p){
  try{
    const v = p?.businessPointId || p?.oID || p?.occurrenceId || p?.stakedId || p?.id || "";
    const s = String(v ?? "").trim();
    if(!s) return "";
    const i = s.indexOf("@");
    return (i > 0 ? s.slice(0, i) : s).trim();
  }catch(_){ return ""; }
}

function nfShortDateTime_(v){
  try{
    const s = String(v || "").trim();
    if(!s) return "";
    return s.replace("T", " ").replace(/\.\d+$/, "").replace(/Z$/, "");
  }catch(_){ return ""; }
}

function nfImplDuplicateMeta_(points){
  const groups = new Map();
  const rows = Array.isArray(points) ? points : [];
  for(const p of rows){
    const key = nfBusinessPointKey_(p);
    if(!key) continue;
    if(!groups.has(key)) groups.set(key, []);
    groups.get(key).push(p);
  }
  const dup = new Map();
  let colorIndex = 0;
  for(const [key, arr] of Array.from(groups.entries()).sort((a,b)=>String(a[0]).localeCompare(String(b[0]), 'fr', { numeric:true, sensitivity:'base' }))){
    if(arr.length <= 1) continue;
    const stations = Array.from(new Set(arr.map(p=>String(p?.stationName || p?.stationId || "").trim()).filter(Boolean)));
    dup.set(key, { key, count: arr.length, stationCount: stations.length, stations, colorIndex: colorIndex++ });
  }
  return dup;
}

function nfDupRowStyle_(dupInfo){
  if(!dupInfo) return "";
  const palette = [
    { bg:'#fff3b0', bar:'#ff2d55' },
    { bg:'#dff7ff', bar:'#00a3ff' },
    { bg:'#e9fbe7', bar:'#22c55e' },
    { bg:'#f4e8ff', bar:'#8b5cf6' }
  ];
  const c = palette[Math.abs(Number(dupInfo.colorIndex || 0)) % palette.length];
  return `background:${c.bg};box-shadow:inset 4px 0 0 ${c.bar};`;
}

function nfImplDuplicateSummaryHtml_(dupMap){
  try{
    if(!dupMap || !dupMap.size) return "";
    const occ = Array.from(dupMap.values()).reduce((a,g)=>a + (g.count || 0), 0);
    return `<div class="small" style="margin:0 0 10px;padding:8px 10px;border:1px solid #ffb020;background:#fff7d6;border-radius:8px;color:#5f3b00;"><b>Doublons LandXML detectes :</b> ${dupMap.size} point(s) / ${occ} occurrence(s). Chaque groupe a sa couleur; garder ou exclure avec la case Incl.</div>`;
  }catch(_){ return ""; }
}

function nfStationBadgeHtml_(p, dupInfo){
  const label = String(p?.stationName || p?.stationId || "").trim();
  if(!label) return "";
  const style = dupInfo && dupInfo.stationCount > 1
    ? "display:inline-block;padding:2px 7px;border-radius:999px;background:#e5edff;color:#123c86;font-weight:700;"
    : "display:inline-block;padding:2px 7px;border-radius:999px;background:#eef2f7;color:#334155;";
  return `<span style="${style}">${esc(label)}</span>`;
}

function nfIsIncluded(pid){
  const k = nfPid(pid);
  if(!k) return true;
  return !nfExcludedPointIds.has(k);
}

function nfIsRefAlti(pid){
  const k = nfPid(pid);
  if(!k) return false;
  return nfRefAltiPointIds.has(k);
}

function nfSetIncluded(pid, included){
  const k = nfPid(pid);
  if(!k) return;
  if(included) nfExcludedPointIds.delete(k);
  else nfExcludedPointIds.add(k);
}

function nfSetRefAlti(pid, selected){
  const k = nfPid(pid);
  if(!k) return;
  if(selected) nfRefAltiPointIds.add(k);
  else nfRefAltiPointIds.delete(k);
}

function nfUpdateCount(view){
  try{
    const ids = Array.isArray(nfViewPointIds[view]) ? nfViewPointIds[view] : [];
    const total = ids.length;
    let excluded = 0;
    let selected = 0;
    for(const pid of ids){
      if(view === 'refalti'){
        if(nfIsRefAlti(pid)) selected++;
      }else{
        if(!nfIsIncluded(pid) || nfIsRefAlti(pid)) excluded++;
      }
    }
    const included = (view === 'refalti') ? selected : (total - excluded);
    const el = document.getElementById(`${view}Count`);
    if(!el) return;
    const title = (view === 'topo') ? 'Levé topo' : (view === 'refalti' ? 'Réf alti' : (view === 'heighttransfer' ? 'Transfert alti' : (view === 'lineref' ? 'Ligne de référence' : (view === 'implant' ? 'Implantation' : 'Station libre'))));
    el.textContent = (view === 'refalti')
      ? `${title} : ${selected} sélectionné(s) / ${total}`
      : `${title} : ${included} inclus / ${excluded} exclus`;
  }catch(_){/* ignore */}
}

function nfUpdateAllCounts(){
  nfUpdateCount('station');
  nfUpdateCount('implant');
  nfUpdateCount('lineref');
  nfUpdateCount('refalti');
  nfUpdateCount('topo');
  nfUpdateCount('heighttransfer');
}

// Une couleur distincte par mise en station (triangle + traits de visée), pour
// distinguer les stations d'un coup d'œil quand il y en a plusieurs sur le même
// plan. Les points visés restent en vert/rouge (inclus/exclu), donc ces teintes
// évitent le vert et le rouge purs pour ne pas se confondre avec eux.
const NF_STATION_COLORS_ = ['#1267f3', '#f76707', '#9c36b5', '#0c8599', '#e64980', '#f59f00', '#495057', '#7048e8', '#1098ad', '#d6336c'];
function nfStationColor_(idx){
  return NF_STATION_COLORS_[Math.abs(Number(idx) || 0) % NF_STATION_COLORS_.length];
}

/* ===== Plan station (onglet "Plan station") =====
   Toutes les stations libres du fichier + tous les points visés, sur un fond
   de carte réel (Leaflet, cf. section "Plan station : fond de carte réel"
   plus bas). Ce plan SVG schématique (repère E/N local, pas de fond de
   carte) reste dessiné en repli - affiché tant que la reprojection GPS n'est
   pas revenue, et si Leaflet/le réseau sont indisponibles. Lecture seule
   (survol pour le détail) - l'inclusion se modifie uniquement depuis le
   tableau de l'onglet "Station libre". */
function renderStationMap(stationRuns, data){
  const container = document.getElementById('stationMapContainer');
  const legend = document.getElementById('stationMapLegend');
  const emptyEl = document.getElementById('stationMapEmpty');
  const tooltip = document.getElementById('stationMapTooltip');
  if(!container || !legend || !emptyEl || !tooltip) return;

  try{
    const cgPoints = data?.cgPoints || {};
    const resolvePoint = (id) => cgPoints[nfStripAt(id)] || cgPoints[nfPid(id)] || null;

    const stations = [];
    const pointsByKey = new Map();

    (stationRuns || []).forEach(run => {
      const SR = run?.results || {};
      if(SR.E == null || SR.N == null) return;
      const label = SR.stationName || SR.idStation || run?.setupId || '?';
      stations.push({
        E: Number(SR.E), N: Number(SR.N), H: SR.H, label,
        devE: SR.devE, devN: SR.devN, devH: SR.devH, devOri: SR.devOri
      });

      const resids = Array.isArray(run?.residuals) ? run.residuals : [];
      resids.forEach(r => {
        const pt = resolvePoint(r?.id);
        if(!pt || pt.E == null || pt.N == null) return;
        const displayId = nfStripAt(r.id) || String(r.id ?? '');
        const key = displayId || `${pt.E},${pt.N}`;
        if(!pointsByKey.has(key)){
          pointsByKey.set(key, { E: Number(pt.E), N: Number(pt.N), H: pt.H, id: displayId, occurrences: [] });
        }
        pointsByKey.get(key).occurrences.push({
          stationLabel: label,
          dHz: r.dHz, dAlti: r.dAlti, dDH: r.dDH,
          included: nfIsIncluded(r.id)
        });
      });
    });

    const points = Array.from(pointsByKey.values());
    // Conservés pour "Envoyer sur la fiche station" (window.nfGetStationPlanViewForPdf),
    // afin de ne pas recalculer stations/points au moment de générer le PDF.
    nfLastStationMapDerived_ = { stations, points };
    container.querySelectorAll('svg').forEach(el => el.remove());

    if(stations.length === 0){
      emptyEl.style.display = 'flex';
      legend.innerHTML = '';
      return;
    }
    emptyEl.style.display = 'none';
    nfRequestStationMapGeo(stations, points);

    const rect = container.getBoundingClientRect();
    const W = Math.max(rect.width, 200);
    const H = Math.max(rect.height, 200);
    if(rect.width < 10 || rect.height < 10){
      // Conteneur encore masqué (tab pas encore affiché) : rien d'exploitable à dessiner.
      return;
    }

    const allE = stations.map(s => s.E).concat(points.map(p => p.E));
    const allN = stations.map(s => s.N).concat(points.map(p => p.N));
    const minE = Math.min(...allE), maxE = Math.max(...allE);
    const minN = Math.min(...allN), maxN = Math.max(...allN);
    const spanE = Math.max(maxE - minE, 1e-6);
    const spanN = Math.max(maxN - minN, 1e-6);
    const padFrac = 0.15;
    const usableW = W * (1 - padFrac * 2);
    const usableH = H * (1 - padFrac * 2);
    const scale = Math.min(usableW / spanE, usableH / spanN);
    const offsetX = (W - spanE * scale) / 2;
    const offsetY = (H - spanN * scale) / 2;
    const toX = (e) => offsetX + (e - minE) * scale;
    const toY = (n) => H - (offsetY + (n - minN) * scale); // N croît vers le haut à l'écran

    const escHtml = (s) => String(s ?? '').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
    const fmtN = (v, d) => (v == null || !Number.isFinite(Number(v))) ? '—' : Number(v).toFixed(d ?? 3);

    const svgParts = [];
    svgParts.push(`<svg width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" style="position:absolute; inset:0;">`);

    // Traits de visée : station -> chacun de ses points, une couleur par station
    // (pas par inclusion) pour distinguer les mises en station d'un coup d'œil ;
    // le pointillé marque en plus les visées exclues du calcul.
    stations.forEach((s, sIdx) => {
      const color = nfStationColor_(sIdx);
      points.forEach(p => {
        const occ = p.occurrences.find(o => o.stationLabel === s.label);
        if(!occ) return;
        const dash = occ.included ? '' : ' stroke-dasharray="4,3"';
        svgParts.push(`<line x1="${toX(s.E)}" y1="${toY(s.N)}" x2="${toX(p.E)}" y2="${toY(p.N)}" stroke="${color}" stroke-opacity="0.45" stroke-width="1.5"${dash} />`);
      });
    });

    points.forEach((p, idx) => {
      const anyIncluded = p.occurrences.some(o => o.included);
      const color = anyIncluded ? '#2f9e44' : '#b91c1c';
      const x = toX(p.E), y = toY(p.N);
      svgParts.push(`<circle data-tt="pt-${idx}" cx="${x}" cy="${y}" r="6" fill="${color}" stroke="#fff" stroke-width="1.5" style="cursor:pointer;" />`);
      // Libellé au-dessus (pair) / en dessous (impair) pour limiter les recouvrements entre points proches.
      const ty = (idx % 2 === 0) ? y - 10 : y + 18;
      svgParts.push(`<text x="${x}" y="${ty}" font-size="10" font-weight="600" fill="#0b1020" text-anchor="middle" style="paint-order:stroke; stroke:#fff; stroke-width:3px;">${escHtml(p.id)}</text>`);
    });

    stations.forEach((s, idx) => {
      const x = toX(s.E), y = toY(s.N);
      const sz = 6;
      const color = nfStationColor_(idx);
      svgParts.push(`<polygon data-tt="st-${idx}" points="${x},${y - sz} ${x - sz},${y + sz * 0.75} ${x + sz},${y + sz * 0.75}" fill="${color}" stroke="#fff" stroke-width="1.5" style="cursor:pointer;" />`);
      svgParts.push(`<text x="${x}" y="${y - sz - 4}" font-size="11" font-weight="700" fill="#0b1020" text-anchor="middle">${escHtml(s.label)}</text>`);
    });

    svgParts.push('</svg>');
    container.insertAdjacentHTML('afterbegin', svgParts.join(''));

    const stationLegendChips = stations.map((s, idx) => `<span style="display:inline-flex; align-items:center; gap:5px;"><svg width="12" height="12"><polygon points="6,1 1,11 11,11" fill="${nfStationColor_(idx)}"/></svg>${escHtml(s.label)}</span>`).join('');
    legend.innerHTML = `
      <span style="display:inline-flex; align-items:center; gap:6px;"><svg width="14" height="14"><circle cx="7" cy="7" r="6" fill="#2f9e44"/></svg>Point visé — inclus</span>
      <span style="display:inline-flex; align-items:center; gap:6px;"><svg width="14" height="14"><circle cx="7" cy="7" r="6" fill="#b91c1c"/></svg>Point visé — exclu</span>
      ${stationLegendChips}
    `;

    const svg = container.querySelector('svg');
    const showTooltip = (evt, html) => {
      tooltip.innerHTML = html;
      tooltip.style.display = 'block';
      const cRect = container.getBoundingClientRect();
      let left = evt.clientX - cRect.left + 12;
      let top = evt.clientY - cRect.top + 12;
      if(left + 220 > cRect.width) left = evt.clientX - cRect.left - 232;
      tooltip.style.left = Math.max(4, left) + 'px';
      tooltip.style.top = Math.max(4, top) + 'px';
    };
    const hideTooltip = () => { tooltip.style.display = 'none'; };

    if(svg){
      svg.addEventListener('mousemove', (evt) => {
        const target = evt.target.closest('[data-tt]');
        if(!target){ hideTooltip(); return; }
        const tt = target.getAttribute('data-tt');
        if(tt.startsWith('pt-')){
          const p = points[Number(tt.slice(3))];
          if(!p) return;
          const rows = p.occurrences.map(o => `
            <div style="margin-top:4px; padding-top:4px; border-top:1px solid rgba(255,255,255,.15);">
              <b>${escHtml(o.stationLabel)}</b> — ${o.included ? 'inclus' : 'exclu'}<br>
              dHz ${fmtN(o.dHz, 4)} · dAlti ${fmtN(o.dAlti, 3)} · dDH ${fmtN(o.dDH, 3)}
            </div>`).join('');
          showTooltip(evt, `<b>${escHtml(p.id)}</b><br>E ${fmtN(p.E)} · N ${fmtN(p.N)} · H ${fmtN(p.H)}${rows}`);
        }else if(tt.startsWith('st-')){
          const s = stations[Number(tt.slice(3))];
          if(!s) return;
          showTooltip(evt, `<b>${escHtml(s.label)}</b> (station)<br>E ${fmtN(s.E)} · N ${fmtN(s.N)} · H ${fmtN(s.H)}<br>σE ${fmtN(s.devE, 4)} · σN ${fmtN(s.devN, 4)} · σH ${fmtN(s.devH, 4)} · σOri ${fmtN(s.devOri, 4)}`);
        }
      });
      svg.addEventListener('mouseleave', hideTooltip);
    }
  }catch(err){
    console.warn('[Nova-Fiches] renderStationMap a échoué.', err);
  }
}

window.nfRenderStationMap = function(){
  if(nfLastStationMapArgs) renderStationMap(nfLastStationMapArgs[0], nfLastStationMapArgs[1]);
};

// Appelé au moment de générer le PDF Station (m03c_pdf_reports_export.js) quand la
// case "Envoyer sur la fiche station" est cochée. Renvoie null si la case n'est pas
// cochée ou s'il n'y a rien à dessiner - le payload PDF n'aura alors simplement pas
// de clé stationPlanView, et StationPlanRenderer (C#) n'ajoutera pas de page.
// Le PDF redessine le plan en vectoriel à partir de ces coordonnées (mêmes formules
// que le schéma SVG) plutôt que de capturer une image de la carte : fiable hors-ligne,
// pas de dépendance aux tuiles du fond de carte, qualité d'impression garantie.
window.nfGetStationPlanViewForPdf = function(){
  try{
    if(!nfStationMapSendToPdf_) return null;
    const { stations, points } = nfLastStationMapDerived_ || {};
    if(!Array.isArray(stations) || stations.length === 0) return null;

    const sightings = [];
    stations.forEach((s, idx) => {
      const color = nfStationColor_(idx);
      (points || []).forEach(p => {
        const occ = p.occurrences.find(o => o.stationLabel === s.label);
        if(!occ) return;
        sightings.push({ stationLabel: s.label, pointId: p.id, color, included: !!occ.included });
      });
    });

    return {
      // Reprojetés côté C# (StationPlanRenderer) pour dessiner un vrai fond de carte
      // sur cette page - mêmes valeurs que celles utilisées pour le fond de carte à l'écran.
      sourceCrs: document.getElementById('stationMapCrs')?.value || '__AUTO__',
      basemap: nfStationMapGeo.basemap || 'plan',
      stations: stations.map((s, idx) => ({ label: s.label, e: s.E, n: s.N, color: nfStationColor_(idx) })),
      points: (points || []).map(p => ({ id: p.id, e: p.E, n: p.N, included: p.occurrences.some(o => o.included) })),
      sightings
    };
  }catch(err){
    console.warn('[Nova-Fiches] nfGetStationPlanViewForPdf a échoué.', err);
    return null;
  }
};

/* ===== Plan station : fond de carte réel (Leaflet + reprojection GPS) =====
   Les E/N du plan schématique sont dans le système de coordonnées du
   chantier (Lambert-93, CC, NTF...), pas en WGS84 - il faut les reprojeter
   avant de pouvoir les poser sur un fond Leaflet. La reprojection se fait
   côté C# (station_map_reproject / MainForm.ReprojectStationMapForUi), qui
   réutilise les primitives déjà utilisées par l'export KMZ
   (KmzExportService.ProjectForPreview / DetectCoordinateSystem). Chaque
   requête porte un jeton ("token") : si l'utilisateur coche/décoche vite
   plusieurs points ou change de CRS, plusieurs requêtes peuvent partir coup
   sur coup sans garantie d'ordre de retour - une réponse dont le jeton n'est
   plus le dernier envoyé est silencieusement ignorée. */
let nfStationMapLeafletLoading = false;
const nfStationMapGeo = {
  map: null,
  baseLayer: null,
  basemap: 'plan',
  markersLayer: null,
  requestToken: 0,
  pendingRequests: new Map(),
  locked: false
};

// Bloque/débloque le zoom et le déplacement de la carte ("Figer la vue"). Appelée à
// chaque (re)création de la carte pour que l'état coché survive à un changement de
// CRS/fond de carte (qui reconstruit nfStationMapGeo.map).
function nfApplyStationMapLock_(){
  const map = nfStationMapGeo.map;
  if(!map) return;
  const locked = nfStationMapGeo.locked;
  ['dragging', 'scrollWheelZoom', 'doubleClickZoom', 'boxZoom', 'touchZoom', 'keyboard'].forEach(h => {
    if(map[h]) locked ? map[h].disable() : map[h].enable();
  });
  if(map.zoomControl){
    if(locked) map.zoomControl.remove();
    else if(!map.zoomControl._map) map.zoomControl.addTo(map);
  }
}

function nfPostToHost_(payload){
  try{
    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function'){
      window.chrome.webview.postMessage(payload);
    }
  }catch(_){ }
}

function nfSetStationMapStatus_(text){
  const s = document.getElementById('stationMapStatus');
  if(s) s.textContent = text || '';
}

function nfLoadLeafletForStationMap(){
  if(window.L) return Promise.resolve(true);
  if(nfStationMapLeafletLoading) return new Promise(resolve => {
    const t = setInterval(() => {
      if(window.L){ clearInterval(t); resolve(true); }
    }, 100);
    setTimeout(() => { clearInterval(t); resolve(!!window.L); }, 7000);
  });
  nfStationMapLeafletLoading = true;
  return new Promise(resolve => {
    try{
      const css = document.createElement('link');
      css.rel = 'stylesheet';
      css.href = './vendor/leaflet/leaflet.css';
      document.head.appendChild(css);
      const script = document.createElement('script');
      script.src = './vendor/leaflet/leaflet.js';
      script.onload = () => resolve(!!window.L);
      script.onerror = () => resolve(false);
      document.head.appendChild(script);
    }catch(_){
      resolve(false);
    }
  });
}

function nfCreateStationMapTileLayer(kind){
  if(kind === 'satellite'){
    return L.tileLayer('https:' + '//' + 'server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
      maxZoom: 19,
      attribution: 'Tiles &copy; Esri'
    });
  }
  return L.tileLayer('https:' + '//' + '{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 22,
    attribution: '&copy; OpenStreetMap'
  });
}

function nfRequestStationMapGeo(stations, points){
  try{
    if(!Array.isArray(stations) || !stations.length) return;
    const crsSelect = document.getElementById('stationMapCrs');
    const sourceCrs = crsSelect ? crsSelect.value : '__AUTO__';
    const token = ++nfStationMapGeo.requestToken;
    const stationsById = new Map();
    const pointsById = new Map();
    const payloadPoints = [];
    stations.forEach((s, idx) => {
      const id = `st:${idx}`;
      stationsById.set(id, s);
      payloadPoints.push({ id, x: s.E, y: s.N });
    });
    (points || []).forEach((p, idx) => {
      const id = `pt:${idx}`;
      pointsById.set(id, p);
      payloadPoints.push({ id, x: p.E, y: p.N });
    });
    nfStationMapGeo.pendingRequests.set(token, { stationsById, pointsById });
    nfSetStationMapStatus_('Reprojection GPS en cours…');
    nfPostToHost_({ type: 'station_map_reproject', token, sourceCrs, points: payloadPoints });
  }catch(err){
    console.warn('[Nova-Fiches] nfRequestStationMapGeo a échoué.', err);
  }
}

function nfHandleStationMapReprojected_(msg){
  try{
    const pending = nfStationMapGeo.pendingRequests.get(msg.token);
    nfStationMapGeo.pendingRequests.delete(msg.token);
    // Jeton périmé (une requête plus récente est déjà partie) : on ignore, la
    // réponse la plus récente écrasera de toute façon la carte au bon état.
    if(!pending || msg.token !== nfStationMapGeo.requestToken) return;
    const lonLatById = new Map();
    (msg.points || []).forEach(p => {
      if(Number.isFinite(p.lon) && Number.isFinite(p.lat)) lonLatById.set(p.id, { lon: p.lon, lat: p.lat });
    });
    const crsLabel = msg.sourceCrs || '';
    const methodLabel = msg.detectionMethod ? ` (${msg.detectionMethod})` : '';
    nfSetStationMapStatus_(crsLabel ? `Fond de carte — CRS source : ${crsLabel}${methodLabel}` : '');
    nfBuildStationMapLeaflet(lonLatById, pending.stationsById, pending.pointsById);
  }catch(err){
    console.warn('[Nova-Fiches] traitement station_map_reprojected a échoué.', err);
  }
}

function nfHandleStationMapError_(msg){
  nfStationMapGeo.pendingRequests.delete(msg?.token);
  nfSetStationMapStatus_('Fond de carte indisponible — plan schématique affiché.');
}

async function nfBuildStationMapLeaflet(lonLatById, stationsById, pointsById){
  const leafletDiv = document.getElementById('stationMapLeaflet');
  const schematicContainer = document.getElementById('stationMapContainer');
  if(!leafletDiv || !schematicContainer || !lonLatById.size) return;

  const ok = await nfLoadLeafletForStationMap();
  if(!ok){
    nfSetStationMapStatus_('Fond de carte indisponible (hors-ligne) — plan schématique affiché.');
    return;
  }

  const escHtml = (s) => String(s ?? '').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
  const fmtN = (v, d) => (v == null || !Number.isFinite(Number(v))) ? '—' : Number(v).toFixed(d ?? 3);

  schematicContainer.querySelectorAll('svg').forEach(el => el.remove());
  leafletDiv.style.display = 'block';

  const isNewMap = !nfStationMapGeo.map;
  if(isNewMap){
    if(L.Popup) L.Popup.mergeOptions({ autoPan: false });
    nfStationMapGeo.map = L.map(leafletDiv, { attributionControl: true });
    nfStationMapGeo.baseLayer = nfCreateStationMapTileLayer(nfStationMapGeo.basemap);
    nfStationMapGeo.baseLayer.addTo(nfStationMapGeo.map);
    nfApplyStationMapLock_();
  }
  const map = nfStationMapGeo.map;

  if(nfStationMapGeo.markersLayer) map.removeLayer(nfStationMapGeo.markersLayer);
  nfStationMapGeo.markersLayer = L.featureGroup();

  // Traits de visée station -> point, comme sur le plan schématique - ajoutés
  // avant les marqueurs pour rester en dessous (ordre d'ajout = ordre d'empilement).
  // Une couleur par station (indice extrait de l'id "st:<idx>", stable quel que
  // soit l'ordre d'itération de la Map) ; le pointillé marque les visées exclues.
  const stationLatLngByLabel = new Map();
  const stationColorByLabel = new Map();
  stationsById.forEach((s, id) => {
    const ll = lonLatById.get(id);
    if(!ll) return;
    stationLatLngByLabel.set(s.label, [ll.lat, ll.lon]);
    stationColorByLabel.set(s.label, nfStationColor_(Number(String(id).split(':')[1])));
  });
  pointsById.forEach((p, id) => {
    const ll = lonLatById.get(id);
    if(!ll) return;
    p.occurrences.forEach(o => {
      const sLatLng = stationLatLngByLabel.get(o.stationLabel);
      if(!sLatLng) return;
      const color = stationColorByLabel.get(o.stationLabel) || '#1267f3';
      L.polyline([sLatLng, [ll.lat, ll.lon]], {
        color, weight: 2.5, opacity: 0.55, interactive: false,
        dashArray: o.included ? null : '6,5'
      }).addTo(nfStationMapGeo.markersLayer);
    });
  });

  pointsById.forEach((p, id) => {
    const ll = lonLatById.get(id);
    if(!ll) return;
    const anyIncluded = p.occurrences.some(o => o.included);
    const color = anyIncluded ? '#2f9e44' : '#b91c1c';
    const marker = L.circleMarker([ll.lat, ll.lon], {
      radius: 6, color: '#fff', weight: 1.5, fillColor: color, fillOpacity: 0.9
    });
    const rows = p.occurrences.map(o => `
      <div style="margin-top:4px; padding-top:4px; border-top:1px solid rgba(255,255,255,.15);">
        <b>${escHtml(o.stationLabel)}</b> — ${o.included ? 'inclus' : 'exclu'}<br>
        dHz ${fmtN(o.dHz, 4)} · dAlti ${fmtN(o.dAlti, 3)} · dDH ${fmtN(o.dDH, 3)}
      </div>`).join('');
    marker.bindTooltip(`<b>${escHtml(p.id)}</b><br>E ${fmtN(p.E)} · N ${fmtN(p.N)} · H ${fmtN(p.H)}${rows}`, { direction: 'top' });
    marker.addTo(nfStationMapGeo.markersLayer);

    // Étiquette ID à côté du point : un marqueur séparé, non-interactif, pour ne pas
    // gêner le survol/clic du cercle en dessous (comme le libellé des stations).
    const labelIcon = L.divIcon({
      className: '',
      html: `<span style="display:inline-block; transform:translate(6px,-8px); font-size:10px; font-weight:600; color:#0b1020; background:rgba(255,255,255,.8); padding:0 2px; border-radius:2px; white-space:nowrap; pointer-events:none;">${escHtml(p.id)}</span>`,
      iconSize: [0, 0],
      iconAnchor: [0, 0]
    });
    L.marker([ll.lat, ll.lon], { icon: labelIcon, interactive: false }).addTo(nfStationMapGeo.markersLayer);
  });

  stationsById.forEach((s, id) => {
    const ll = lonLatById.get(id);
    if(!ll) return;
    const color = nfStationColor_(Number(String(id).split(':')[1]));
    const icon = L.divIcon({
      className: '',
      html: `<div style="transform:translate(-50%,-100%); display:flex; flex-direction:column; align-items:center; white-space:nowrap;">
        <span style="font-size:11px; font-weight:700; color:#0b1020; background:rgba(255,255,255,.85); padding:0 3px; border-radius:3px; margin-bottom:2px;">${escHtml(s.label)}</span>
        <svg width="8" height="8" viewBox="0 0 8 8"><polygon points="4,0.5 0.5,7.5 7.5,7.5" fill="${color}" stroke="#fff" stroke-width="1"/></svg>
      </div>`,
      iconSize: [0, 0],
      iconAnchor: [0, 0]
    });
    const marker = L.marker([ll.lat, ll.lon], { icon });
    marker.bindTooltip(`<b>${escHtml(s.label)}</b> (station)<br>E ${fmtN(s.E)} · N ${fmtN(s.N)} · H ${fmtN(s.H)}<br>σE ${fmtN(s.devE, 4)} · σN ${fmtN(s.devN, 4)} · σH ${fmtN(s.devH, 4)} · σOri ${fmtN(s.devOri, 4)}`, { direction: 'top' });
    marker.addTo(nfStationMapGeo.markersLayer);
  });

  nfStationMapGeo.markersLayer.addTo(map);

  if(isNewMap){
    const bounds = nfStationMapGeo.markersLayer.getBounds();
    if(bounds.isValid()) map.fitBounds(bounds, { padding: [30, 30] });
  }
  setTimeout(() => { try{ map.invalidateSize(); }catch(_){} }, 50);
}

try{
  if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function'){
    window.chrome.webview.addEventListener('message', ev => {
      const msg = ev && ev.data ? ev.data : ev;
      if(!msg || !msg.type) return;
      if(msg.type === 'station_map_reprojected') nfHandleStationMapReprojected_(msg);
      if(msg.type === 'station_map_error') nfHandleStationMapError_(msg);
    });
  }
}catch(_){ }

function nfSyncCheckboxes(pid){
  try{
    const k = nfPid(pid);
    if(!k) return;
    const checked = nfIsIncluded(k);
    const sel = `input.nf-inc[data-pid="${CSS.escape(k)}"]`;
    for(const el of document.querySelectorAll(sel)){
      el.checked = checked;
    }
  }catch(_){/* ignore */}
}

function nfNotifyHost(){
  // Optionnel: le host peut écouter ces messages plus tard (Patch 3). Ici: safe no-op.
  try{
    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function'){
      window.chrome.webview.postMessage({ type: 'nf_point_exclusions_changed', pointIds: Array.from(nfExcludedPointIds) });
      window.chrome.webview.postMessage({ type: 'nf_refalti_changed', pointIds: Array.from(nfRefAltiPointIds) });
    }
  }catch(_){/* ignore */}
}

function nfRecomputePdfButtons_(){
  try{
    const d = (typeof window.lastData !== "undefined") ? window.lastData : null;
    if(d && typeof window.applyReportButtonStateFromData === "function"){
      window.applyReportButtonStateFromData(d);
    }
  }catch(_){ /* ignore */ }
}

function nfBindViewControls(view){
  try{
    const bIn = document.getElementById(`${view}BtnAllIn`);
    const bOut = document.getElementById(`${view}BtnAllOut`);
    const bReset = document.getElementById(`${view}BtnReset`);

    if(bIn) bIn.onclick = () => {
      for(const pid of (nfViewPointIds[view] || [])){
        if(view === 'refalti') nfSetRefAlti(pid, true);
        else nfSetIncluded(pid, true);
      }
      nfUpdateAllCounts();
      for(const pid of (nfViewPointIds[view] || [])) nfSyncCheckboxes(pid);
      nfNotifyHost();
      nfRecomputePdfButtons_();
    };
    if(bOut) bOut.onclick = () => {
      for(const pid of (nfViewPointIds[view] || [])){
        if(view === 'refalti') nfSetRefAlti(pid, false);
        else nfSetIncluded(pid, false);
      }
      nfUpdateAllCounts();
      for(const pid of (nfViewPointIds[view] || [])) nfSyncCheckboxes(pid);
      nfNotifyHost();
      nfRecomputePdfButtons_();
    };
    if(bReset) bReset.onclick = () => {
      nfExcludedPointIds = new Set();
      nfRefAltiPointIds = new Set();
      nfUpdateAllCounts();
      for(const el of document.querySelectorAll('input.nf-inc[data-pid]')){
        el.checked = true;
      }
      for(const el of document.querySelectorAll('input.nf-refalti[data-pid]')){
        el.checked = false;
      }
      nfNotifyHost();
      nfRecomputePdfButtons_();
    };
  }catch(_){/* ignore */}
}


function renderAll(data){
  // Reset exclusions on new import (raw text changed). Keeps selection when merely switching tabs.
  try{
    const rt = (data && typeof data.rawText === 'string') ? data.rawText : '';
    if(nfLastRawText !== rt){
      nfExcludedPointIds = new Set();
      nfRefAltiPointIds = new Set();
      nfLastRawText = rt;
    }
  }catch(_){/* ignore */}

  // Station libre (toutes les mises en station)
  const hasLegacyStation =
    !!(data?.stationLibre) &&
    (
      (Array.isArray(data.stationLibre.observations) && data.stationLibre.observations.length > 0) ||
      (Array.isArray(data.stationLibre.residuals) && data.stationLibre.residuals.length > 0) ||
      !!(data.stationLibre.results && (data.stationLibre.results.idStation || data.stationLibre.results.E != null || data.stationLibre.results.N != null || data.stationLibre.results.H != null))
    );
  const stationRuns = (data?.stationLibreRuns && data.stationLibreRuns.length)
    ? data.stationLibreRuns
    : (hasLegacyStation ? [data.stationLibre] : []);

  const stationIds = [];
  stationRuns.forEach(run => {
    const obs = Array.isArray(run?.observations) ? run.observations : [];
    const resids = Array.isArray(run?.residuals) ? run.residuals : [];
    for(const o of obs){ const pid = nfPid(o?.id); if(pid) stationIds.push(pid); }
    for(const r of resids){ const pid = nfPid(r?.id); if(pid) stationIds.push(pid); }
  });
  nfViewPointIds.station = Array.from(new Set(stationIds));

    const stationHtml = stationRuns.length ? stationRuns.map((run) => {
    const SR = run?.results || {};
    const obs = Array.isArray(run?.observations) ? run.observations : [];
    const resids = Array.isArray(run?.residuals) ? run.residuals : [];
    const stationLabel = SR.stationName || run?.stationName || SR.idStation || "";
    const setupLabel = SR.idStation || run?.setupId || "";
    const methodLabel = SR.method || run?.method || "Station libre";
    return `
      <div class="small" style="margin-bottom:10px;">
        <b>${esc(methodLabel)}</b> — ${esc(stationLabel)}${setupLabel ? ` / Setup ${esc(setupLabel)}` : ""} / E ${esc(fmt(SR.E))} / N ${esc(fmt(SR.N))} / H ${esc(fmt(SR.H))} / Hi ${esc(fmt(SR.Hi))}
      </div>
      ${tableHtml(["ID station","Nom station","E","N","H","Hi","Corr orient.","σE","σN","σH","σOri"], [[SR.idStation??"", stationLabel, fmt(SR.E),fmt(SR.N),fmt(SR.H),fmt(SR.Hi),fmt(SR.corrOrient,4),fmt(SR.devE),fmt(SR.devN),fmt(SR.devH),fmt(SR.devOri,4)]])}
      <div class="small" style="margin:12px 0 6px;"><b>Observations</b></div>
      ${tableHtml(["Incl.","ID","Hz","Vz","Dp","Hr","Const prisme"], obs.map(o=>[
        { __html:true, value:`<input type="checkbox" class="nf-inc" data-pid="${esc(nfPid(o.id))}" ${nfIsIncluded(o.id)?"checked":""} />` },
        nfStripAt(o.id), fmt(o.hz,4), fmt(o.vz,4), fmt(o.dp,3), fmt(o.hr,3), fmt(o.constPrisme,4)
      ]))}
      <div class="small" style="margin:12px 0 6px;"><b>Résidus</b></div>
      ${tableHtml(["Incl.","ID","dHz","dAlti","dDH","Utilisé"], resids.map(r=>[
        { __html:true, value:`<input type="checkbox" class="nf-inc" data-pid="${esc(nfPid(r.id))}" ${nfIsIncluded(r.id)?"checked":""} />` },
        nfStripAt(r.id), fmt(r.dHz,4), fmt(r.dAlti,3), fmt(r.dDH,3), r.used
      ]))}
      <div style="height:12px;"></div>
    `;
  }).join("") : '<div class="small">Aucune station libre détectée. Les transferts d’altitude sont affichés dans l’onglet Transfert alti.</div>';

  document.getElementById('stationContainer').innerHTML = stationHtml;

  // Plan station (onglet "Plan station") : le conteneur est masqué tant que
  // l'onglet n'est pas actif, donc getBoundingClientRect() y renverrait une
  // taille nulle si on dessinait maintenant. On mémorise juste les données et
  // le tab-switch (m03c_pdf_reports_export.js) appelle nfRenderStationMap()
  // au moment où le conteneur devient visible.
  nfLastStationMapArgs = [stationRuns, data];
  if(document.getElementById('view_stationmap')?.style.display !== 'none'){
    renderStationMap(stationRuns, data);
  }

  // Implantation
  const impPointsAll = Array.isArray(data.implantation?.points) ? data.implantation.points : [];
  const impPoints = impPointsAll.filter(p => !nfIsRefAlti(p?.id)).map(p => nfAssignSource_(p, 'IMP'));
  nfViewPointIds.implant = impPointsAll.map(p=>nfRowPid_(p)).filter(Boolean);
  const impPointsSorted = nfSortByZoneThenId_(impPoints);
  const impDupMeta = nfImplDuplicateMeta_(impPointsSorted);
  const imp = (impPointsSorted.length === 0)
    ? `<div class="small">Aucun point d'implantation détecté. Importez un fichier LandXML pour afficher les données.</div>`
    : nfImplDuplicateSummaryHtml_(impDupMeta) + nfZoneSummaryHtml_(impPointsSorted) + tableHtml(
    ["Incl.","Code","Affectation","ID","Point metier","Occurrence","Horodatage","Station","X calc","Y calc","Z calc","X mes","Y mes","Z mes","Dx","Dy","Dz","STATUT"],
    impPointsSorted.map(p=>{
      const pointKey = (typeof window.NF_buildPointAssignKey === 'function') ? String(window.NF_buildPointAssignKey(p) || '') : '';
      const currentInline = (typeof window.NF_getPointInlineCodeMap === 'function' && pointKey) ? String(window.NF_getPointInlineCodeMap()[pointKey] || '').trim() : '';
      const effectiveCode = nfZoneLabel_(p, 'IMP');
      const rowPid = nfRowPid_(p);
      const businessKey = nfBusinessPointKey_(p);
      const dupInfo = businessKey ? impDupMeta.get(businessKey) : null;
      const optionsHtml = (function(){
        try{
          if(typeof window.NF_buildCodeOptionsHtml !== 'function') return `<option value="">(aucune)</option>`;
          return window.NF_buildCodeOptionsHtml(currentInline);
        }catch(_){ return `<option value="">(aucune)</option>`; }
      })();
      const cells = [
        { __html:true, value:`<input type="checkbox" class="nf-inc" data-pid="${esc(rowPid)}" ${nfIsIncluded(rowPid)?"checked":""} />` },
        effectiveCode,
        { __html:true, value:`<select class="box nf-inline-code-select" data-point-key="${esc(pointKey)}" style="min-width:110px;">${optionsHtml}</select>` },
        p.id,
        businessKey || "",
        p.occurrenceId || p.stakedId || "",
        nfShortDateTime_(p.timeStamp),
        { __html:true, value:nfStationBadgeHtml_(p, dupInfo) },
        fmt(p.theo.E), fmt(p.theo.N), fmt(p.theo.H),
        fmt(p.mes.E), fmt(p.mes.N), fmt(p.mes.H),
        fmt(p.d.dx), fmt(p.d.dy), { __html:true, value:`<span class="nf-dz-big">${esc(fmt(p.d.dz))}</span>` },
        { __html:true, value:(function(){ const st=statusFromTol(p.d.dx, p.d.dy, p.d.dz); const cls= st==="VALIDE" ? "st st-ok" : (st==="REFUSÉ" ? "st st-ko" : "st st-na"); return `<span class="${cls}">${esc(st||"—")}</span>`; })() }
      ];
      return dupInfo
        ? { __row:true, style:nfDupRowStyle_(dupInfo), cells }
        : cells;
    })
  );
  document.getElementById('implantContainer').innerHTML = imp;

  // Ligne de ref (format rabattement)
  const lrPointsAll = Array.isArray(data.ligneRef) ? data.ligneRef.flatMap(lr => lr.rabPoints) : [];
  const lrPoints = lrPointsAll.filter(rp => !nfIsRefAlti(rp?.id)).map(rp => nfAssignSource_(rp, 'LR'));
  nfViewPointIds.lineref = lrPointsAll.map(rp=>nfRowPid_(rp)).filter(Boolean);
  const lrPointsSorted = nfSortByZoneThenId_(lrPoints);
  const lrDupMeta = nfImplDuplicateMeta_(lrPointsSorted);
  const lr = (lrPointsSorted.length === 0)
    ? `<div class="small">Aucun point de ligne de référence détecté. Importez un fichier LandXML pour afficher les données.</div>`
    : nfImplDuplicateSummaryHtml_(lrDupMeta) + nfZoneSummaryHtml_(lrPointsSorted) + tableHtml(
    ["Incl.","Code","Affectation","ID point","Point metier","Occurrence","Horodatage","Station","X calc","Y calc","Z calc","X mes","Y mes","Z mes","Dx","Dy","Dz","STATUT"],
    lrPointsSorted.map(rp=>{
      const pointKey = (typeof window.NF_buildPointAssignKey === 'function') ? String(window.NF_buildPointAssignKey(rp) || '') : '';
      const currentInline = (typeof window.NF_getPointInlineCodeMap === 'function' && pointKey) ? String(window.NF_getPointInlineCodeMap()[pointKey] || '').trim() : '';
      const effectiveCode = nfZoneLabel_(rp, 'LR');
      const rowPid = nfRowPid_(rp);
      const businessKey = nfBusinessPointKey_(rp);
      const dupInfo = businessKey ? lrDupMeta.get(businessKey) : null;
      const optionsHtml = (function(){
        try{
          if(typeof window.NF_buildCodeOptionsHtml !== 'function') return `<option value="">(aucune)</option>`;
          return window.NF_buildCodeOptionsHtml(currentInline);
        }catch(_){ return `<option value="">(aucune)</option>`; }
      })();
      const cells = [
        { __html:true, value:`<input type="checkbox" class="nf-inc" data-pid="${esc(rowPid)}" ${nfIsIncluded(rowPid)?"checked":""} />` },
        effectiveCode,
        { __html:true, value:`<select class="box nf-inline-code-select" data-point-key="${esc(pointKey)}" style="min-width:110px;">${optionsHtml}</select>` },
        rp.id,
        businessKey || "",
        rp.occurrenceId || rp.stakedId || "",
        nfShortDateTime_(rp.timeStamp),
        { __html:true, value:nfStationBadgeHtml_(rp, dupInfo) },
        fmt(rp.calc.E), fmt(rp.calc.N), fmt(rp.calc.H),
        fmt(rp.mes.E), fmt(rp.mes.N), fmt(rp.mes.H),
        fmt(rp.d.dx), fmt(rp.d.dy), fmt(rp.d.dz),
        { __html:true, value:(function(){ const st=statusFromTol(rp.d.dx, rp.d.dy, rp.d.dz); const cls= st==="VALIDE" ? "st st-ok" : (st==="REFUSÉ" ? "st st-ko" : "st st-na"); return `<span class="${cls}">${esc(st||"—")}</span>`; })() }
      ];
      return dupInfo
        ? { __row:true, style:nfDupRowStyle_(dupInfo), cells }
        : cells;
    })
  );
  document.getElementById('linerefContainer').innerHTML = lr;

  // Réf alti : candidats issus d'Implantation + Ligne de référence + Levé topo
  // Les points topo doivent aussi être accessibles ici.
  try{
    const refAltiRows = [];
    const refAltiSeen = new Set();
    const pushRefAlti = (srcName, pid, E, N, H) => {
      if(!pid || refAltiSeen.has(pid)) return;
      refAltiSeen.add(pid);
      refAltiRows.push([
        { __html:true, value:`<input type="checkbox" class="nf-refalti" data-pid="${esc(pid)}" ${nfIsRefAlti(pid)?"checked":""} />` },
        srcName,
        pid,
        fmt(E),
        fmt(N),
        fmt(H)
      ]);
    };

    for(const p of impPointsAll){
      const pid = nfPid(p?.id);
      if(!pid) continue;
      pushRefAlti('Implantation', pid, p?.mes?.E ?? p?.theo?.E, p?.mes?.N ?? p?.theo?.N, p?.mes?.H ?? p?.theo?.H);
    }

    for(const rp of lrPointsAll){
      const pid = nfPid(rp?.id);
      if(!pid) continue;
      pushRefAlti('Ligne de référence', pid, rp?.mes?.E ?? rp?.calc?.E, rp?.mes?.N ?? rp?.calc?.N, rp?.mes?.H ?? rp?.calc?.H);
    }

    const topoStationsForRef = Array.isArray(data.topoStations) ? data.topoStations : [];
    for(const st of topoStationsForRef){
      const res = Array.isArray(st?.results) ? st.results : [];
      for(const p of res){
        const pid = nfPid(p?.id);
        if(!pid) continue;
        pushRefAlti('Levé topo', pid, p?.E, p?.N, p?.H);
      }
    }

    nfViewPointIds.refalti = Array.from(refAltiSeen);
    const refAltiHtml = (refAltiRows.length === 0)
      ? `<div class="small">Aucun point disponible pour la référence altimétrique.</div>`
      : tableHtml(["Réf alti","Origine","ID point","X","Y","Z"], refAltiRows);
    const rc = document.getElementById('refaltiContainer');
    if(rc) rc.innerHTML = refAltiHtml;
  }catch(_){
    nfViewPointIds.refalti = [];
    const rc = document.getElementById('refaltiContainer');
    if(rc) rc.innerHTML = `<div class="small">Aucun point disponible pour la référence altimétrique.</div>`;
  }

  // Levé topo (points topo)
  try{
    // Transfert d'altitude
    const htRows = [];
    const transfers = Array.isArray(data.heightTransfers) ? data.heightTransfers : [];
    transfers.forEach(tr=>{
      htRows.push({ __row:true, style:"background:#eef6ff; font-weight:800;", cells:[
        "Station", tr.stationName || tr.setupId || "", tr.setupId || "", "", fmt(tr.stationHOriginal ?? tr.H), fmt(tr.calcHgt), fmt(tr.stdDevHgt,4), "", "", ""
      ]});
      (Array.isArray(tr.references) ? tr.references : []).forEach(r=>{
        htRows.push([
          "Référence", tr.stationName || "", r.id || "", r.useKind || "1D", fmt(r?.point?.H), fmt(tr.calcHgt), fmt(r.deltaHgt,4),
          fmt(r?.observation?.hz,4), fmt(r?.observation?.vz,4), fmt(r?.observation?.dp,3), fmt(r?.observation?.hr,3)
        ]);
      });
      (Array.isArray(tr.measuredPoints) ? tr.measuredPoints : []).forEach(p=>{
        htRows.push([
          "Mesuré", tr.stationName || "", p.id || "", "", fmt(p?.point?.H), fmt(tr.calcHgt), "",
          fmt(p?.observation?.hz,4), fmt(p?.observation?.vz,4), fmt(p?.observation?.dp,3), fmt(p?.observation?.hr,3)
        ]);
      });
    });
    nfViewPointIds.heighttransfer = [];
    const htn = document.getElementById('heighttransferCount');
    if(htn) htn.textContent = transfers.length
      ? `Transfert alti : ${transfers.length} station(s) / ${htRows.length} ligne(s)`
      : "Transfert alti : 0 station";
    const htc = document.getElementById('heighttransferContainer');
    if(htc) htc.innerHTML = htRows.length
      ? tableHtml(["Type","Station","Point","Utilisé","Z point / initial","Z station calculé","Contrôle","Hz","Vz","Dp","Hr"], htRows)
      : `<div class="small">Aucun transfert d'altitude détecté.</div>`;

    const topoStations = Array.isArray(data.topoStations) ? data.topoStations : [];
    const topoRows = [];
    const topoIds = [];

    for(const st of topoStations){
      const stName = st?.stationName || st?.setupId || '';
      const res = Array.isArray(st?.results) ? st.results : [];
      for(const p of res){
        const pid = nfPid(p?.id);
        if(!pid) continue;
        topoIds.push(pid);
        topoRows.push([
          { __html:true, value:`<input type="checkbox" class="nf-inc" data-pid="${esc(pid)}" ${nfIsIncluded(pid)?"checked":""} />` },
          stName,
          pid,
          fmt(p?.E),
          fmt(p?.N),
          fmt(p?.H)
        ]);

      }
    }

    nfViewPointIds.topo = topoIds;

    const topoHtml = (topoRows.length === 0)
      ? `<div class="small">Aucune donnée de levé topo.</div>`
      : tableHtml(["Incl.","Station","ID point","X","Y","Z"], topoRows);

    const tc = document.getElementById('topoContainer');
    if(tc) tc.innerHTML = topoHtml;
  }catch(_){
    nfViewPointIds.topo = [];
    const tc = document.getElementById('topoContainer');
    if(tc) tc.innerHTML = `<div class="small">Aucune donnée de levé topo.</div>`;
  }

  // Bind view controls (safe)
  nfBindViewControls('station');
  nfBindViewControls('implant');
  nfBindViewControls('lineref');
  nfBindViewControls('refalti');
  nfBindViewControls('topo');
  nfBindViewControls('heighttransfer');

  // Delegate checkbox changes (single handler)
  try{
    const handler = (ev) => {
      const t = ev?.target;
      if(!(t instanceof HTMLInputElement)) return;
      if(!t.classList.contains('nf-inc')) return;
      const pid = t.getAttribute('data-pid') || '';
      if(!pid) return;
      nfSetIncluded(pid, t.checked);
      nfSyncCheckboxes(pid);
      nfUpdateAllCounts();
      nfNotifyHost();
    };
    const c1 = document.getElementById('stationContainer'); if(c1) c1.onchange = handler;
    const c2 = document.getElementById('implantContainer');
    if(c2) c2.onchange = (ev) => {
      handler(ev);
      const t = ev?.target;
      if(!(t instanceof HTMLSelectElement)) return;
      if(!t.classList.contains('nf-inline-code-select')) return;
      try{
        const pointKey = String(t.getAttribute('data-point-key') || '').trim();
        if(!pointKey || typeof window.NF_setInlinePointCode !== 'function') return;
        window.NF_setInlinePointCode(pointKey, String(t.value || '').trim());
        renderAll(data);
      }catch(_){ }
    };
    const c3 = document.getElementById('linerefContainer');
    if(c3) c3.onchange = (ev) => {
      handler(ev);
      const t = ev?.target;
      if(!(t instanceof HTMLSelectElement)) return;
      if(!t.classList.contains('nf-inline-code-select')) return;
      try{
        const pointKey = String(t.getAttribute('data-point-key') || '').trim();
        if(!pointKey || typeof window.NF_setInlinePointCode !== 'function') return;
        window.NF_setInlinePointCode(pointKey, String(t.value || '').trim());
        renderAll(data);
      }catch(_){ }
    };
    const c4 = document.getElementById('topoContainer'); if(c4) c4.onchange = handler;

    const refHandler = (ev) => {
      const t = ev?.target;
      if(!(t instanceof HTMLInputElement)) return;
      if(!t.classList.contains('nf-refalti')) return;
      const pid = t.getAttribute('data-pid') || '';
      if(!pid) return;
      nfSetRefAlti(pid, !!t.checked);
      nfUpdateAllCounts();
      nfNotifyHost();
      // Re-render to exclude selected altimetric reference points from implantation / ligne de ref.
      try{ renderAll(data); }catch(_){ }
      nfRecomputePdfButtons_();
    };
    const c5 = document.getElementById('refaltiContainer'); if(c5) c5.onchange = refHandler;

    const crsSel = document.getElementById('stationMapCrs');
    if(crsSel) crsSel.onchange = () => { if(typeof window.nfRenderStationMap === 'function') window.nfRenderStationMap(); };
    const basemapSel = document.getElementById('stationMapBasemap');
    if(basemapSel) basemapSel.onchange = (ev) => {
      nfStationMapGeo.basemap = ev.target.value;
      if(nfStationMapGeo.map){
        if(nfStationMapGeo.baseLayer) nfStationMapGeo.map.removeLayer(nfStationMapGeo.baseLayer);
        nfStationMapGeo.baseLayer = nfCreateStationMapTileLayer(nfStationMapGeo.basemap);
        nfStationMapGeo.baseLayer.addTo(nfStationMapGeo.map);
      }
    };

    const lockSel = document.getElementById('stationMapLockView');
    if(lockSel){
      nfStationMapGeo.locked = !!lockSel.checked;
      lockSel.onchange = (ev) => {
        nfStationMapGeo.locked = !!ev.target.checked;
        nfApplyStationMapLock_();
      };
    }
    const sendPdfSel = document.getElementById('stationMapSendToPdf');
    if(sendPdfSel){
      nfStationMapSendToPdf_ = !!sendPdfSel.checked;
      sendPdfSel.onchange = (ev) => { nfStationMapSendToPdf_ = !!ev.target.checked; };
    }
  }catch(_){/* ignore */}

  // Update counts after render
  nfUpdateAllCounts();

  document.getElementById('rawArea').value = data.rawText;

  // PDF enablement (B1.1 behavior unchanged)
  document.getElementById('btnPdfIntervention').disabled = (impPoints.length === 0);
  document.getElementById('btnPdfLigneRef').disabled = (lrPoints.length === 0);
  // Station (copie du bloc Station libre du rapport complet)
  try{
    const runs = (data?.stationLibreRuns && data.stationLibreRuns.length)
      ? data.stationLibreRuns
      : (hasLegacyStation ? [data.stationLibre] : []);
    const hasStation = runs.some(r => {
      const SR = r?.results || {};
      const obs = r?.observations || [];
      return !!(SR.idStation || SR.E!=null || SR.N!=null || SR.H!=null || obs.length);
    });
    const b = document.getElementById('btnPdfStation');
    if(b) b.disabled = !hasStation;
  }catch(_){/* ignore */}
  const fullBtn = document.getElementById('btnPdfFull');
  if(fullBtn){
    fullBtn.disabled = (document.getElementById('btnPdfIntervention').disabled && document.getElementById('btnPdfLigneRef').disabled);
  }

  document.getElementById('btnRecalc').disabled = false;

  setStatus(`OK — Implantation: ${impPoints.length} pts, Rabattement: ${lrPoints.length} pts`);

  // ÉCHANGES (V2) : rafraîchit les messages / états "Station+" après chargement AppLog.
  try{ if(typeof updateExchangePills === 'function') updateExchangePills(); }catch(_){ }
}

/* =========================
   PDF layout helpers
========================= */

// --- Helper: draw rectangle with optional fill (kept minimal / compatible) ---
function pdfRect(doc, x, y, w, h, fill){
  try{
    if(fill){
      if(Array.isArray(fill)) doc.setFillColor(fill[0], fill[1], fill[2]);
      else if(typeof fill === "object" && fill && "r" in fill) doc.setFillColor(fill.r, fill.g, fill.b);
      doc.rect(x, y, w, h, "FD"); // Fill + Stroke
    }else{
      doc.rect(x, y, w, h, "S"); // Stroke only
    }
  }catch(e){
    // fallback: try basic rect
    try{ doc.rect(x, y, w, h); }catch(_){}
  }
}

function setStrokeBrand(doc){ doc.setDrawColor(BRAND.r, BRAND.g, BRAND.b); }

function pdfBar(doc,y,text){
  const x=10,w=190,h=8;
  pdfRect(doc,x,y,w,h,[BRAND.r, BRAND.g, BRAND.b]);
  doc.setTextColor(255,255,255);
  setTitleFont(doc);
  doc.setFontSize(12);
  doc.text(text, x+w/2, y+5.5, {align:"center"});
  doc.setTextColor(0,0,0);
  return y+h;
}

// ===== Filtre global des bandeaux indésirables =====
if(!window.__pdfBarFiltered){
  window.__pdfBarFiltered = true;
  const __pdfBarOrig = pdfBar;
  pdfBar = function(doc, y, text){
    try{
      const t = String(text||"").trim();
      if(t.startsWith("RAPPORT D'INTERVENTION DU")) return y; // supprimer partout
      if(t.toLowerCase().startsWith("type de prestation")) return y; // sécurité
    }catch(e){}
    return __pdfBarOrig(doc, y, text);
  };
}


function pdfLightBar(doc,y,text){
  y = nfGuardFooter(doc, y, 34);
  const x=10,w=190,h=7.5;
  pdfRect(doc,x,y,w,h,[230,230,230]);
  setTitleFont(doc);
  doc.setFontSize(10.5);
  doc.text(text, x+w/2, y+5.2, {align:"center"});
  return y+h;
}


// Draw thicker vertical separators for Implantation tables:
// - between Z théo and X mes
// - between Z mes and Dx
// + thicker outer frame
function addThickImplantationLines(doc){
  try{
    const t = doc.lastAutoTable;
    if(!t || !t.table || !t.table.columns) return;
    const startY = t.table.startY;
    const finalY = t.finalY || t.lastAutoTable?.finalY || (doc.lastAutoTable && doc.lastAutoTable.finalY);
    if(!finalY) return;

    const x0 = t.table.startX;
    const w  = t.table.width;

    const cols = t.table.columns;

    const xAfter3 = cols[3]?.x + cols[3]?.width;
    const xAfter6 = cols[6]?.x + cols[6]?.width;

    const prevLW = doc.getLineWidth ? doc.getLineWidth() : 0.2;
    doc.setDrawColor(0,0,0);
    doc.setLineWidth(0.6);

    // outer frame
    doc.rect(x0, startY, w, finalY - startY, "S");

    // separators
    if(xAfter3) doc.line(xAfter3, startY, xAfter3, finalY);
    if(xAfter6) doc.line(xAfter6, startY, xAfter6, finalY);

    doc.setLineWidth(prevLW || 0.2);
  }catch(e){}
}

// Draw thicker vertical separators + outer frame for tables (per page hook).
// IMPORTANT: We draw these AFTER the grid, otherwise the thin grid lines can overdraw the thick ones.
//
// Use:
//   thickTableLinesDidDrawPage(hookData, [0, 3, 6])
// Where each index means: draw a thick vertical line AFTER that column.
// For Nova tables:
//   0 = after "ID point"
//   3 = between Z théo/calc and X mes
//   6 = between Z mes and Dx/dL
//
// Backward compatible signature:
//   thickTableLinesDidDrawPage(hookData, 3, 6)
function thickTableLinesDidDrawPage(hookData, cfgOrIdx3, idxAfter6){
  // Draw uniform "thick" vertical separators for results tables.
  // Robust across jsPDF-AutoTable builds:
  // - Prefer cumulative column widths (always available)
  // - Fallback to cells.x/width if needed
  try{
    const doc = hookData.doc;
    const t = hookData.table;
    if(!doc || !t) return;

    // Legacy signatures supported:
    // - thickTableLinesDidDrawPage(hookData, 3, 6)
    // - thickTableLinesDidDrawPage(hookData, [0,3,6,9])
    // - thickTableLinesDidDrawPage(hookData, { vAfter:[0,3,6,9], frameW:0.45, sepVW:0.45 })
    let boundaries = [];
    let frameW = RESULTS_THICK_W;
    let sepVW  = RESULTS_THICK_W;

    if(Array.isArray(cfgOrIdx3)){
      boundaries = cfgOrIdx3;
    }else if(cfgOrIdx3 && typeof cfgOrIdx3 === "object"){
      boundaries = Array.isArray(cfgOrIdx3.vAfter) ? cfgOrIdx3.vAfter : [];
      if(typeof cfgOrIdx3.frameW === "number") frameW = cfgOrIdx3.frameW;
      if(typeof cfgOrIdx3.sepVW  === "number") sepVW  = cfgOrIdx3.sepVW;
    }else{
      boundaries = [cfgOrIdx3, idxAfter6].filter(v => typeof v === "number");
    }

    if(!boundaries.length) return;

    // NOTE (FIX31): On some jsPDF-AutoTable builds, `table.startPageNumber` is missing
    // for tables that start on page > 1 (common in our "rapport complet" second table).
    // If we default to 1, our thick vertical lines start at the top margin and cut through
    // the next section/banner. To make this deterministic, we always anchor to `table.startY`
    // (when available) and fallback to the top margin.
    const pageNo = hookData.pageNumber || 1;

    // For multi-page tables we must start thick lines at the top margin on pages > start page.
    // For some jsPDF-AutoTable builds, `table.startPageNumber` is missing (notably tables that
    // begin on a page > 1 in our "rapport complet"). In that case we treat the current page as
    // the start page to avoid drawing from the top margin through banners/headers.
    const marginTop = (hookData.settings?.margin?.top ?? hookData.settings?.marginTop ?? 10);
    const startPage = (typeof t.startPageNumber === 'number' && t.startPageNumber > 0)
      ? t.startPageNumber
      : pageNo;

    const startY0 = (pageNo > startPage)
      ? marginTop
      : ((typeof t.startY === 'number') ? t.startY : (hookData.settings?.startY ?? marginTop));

// Clamp to marginTop to guarantee we never start inside header area,
// and to avoid missing thick lines on tables that begin on a page > 1
// where some AutoTable builds provide an unexpected startY.
const startY = Math.max(startY0, marginTop);

    // End at the real end of the table on this page.
    const endY = (t.cursor && typeof t.cursor.y === "number")
      ? t.cursor.y
      : (hookData.cursor?.y ?? t.finalY);
    if(!(startY!=null && endY!=null) || endY <= startY) return;
    // Table X origin / width are not consistently exposed across autotable builds.
    // Fallbacks ensure the thick lines work on page 1 + all pages.
    const marginLeft = (hookData.settings?.margin?.left ?? hookData.settings?.marginLeft ?? 10);
    const x0 = (typeof t.startX === 'number') ? t.startX : marginLeft;

    // We'll try to resolve width from table.width, otherwise from columns cumulative widths.
    let w = (typeof t.width === 'number') ? t.width : null;

    // --- Compute X positions ---
    // Primary path: cumulative widths from columns
    let xs = [];
    const cols = Array.isArray(t.columns) ? t.columns : [];
    const widths = cols.map(c => {
      const ww = (typeof c.width === "number") ? c.width
        : (typeof c.wrappedWidth === "number") ? c.wrappedWidth
        : (typeof c.minWidth === "number") ? c.minWidth
        : null;
      return ww;
    });

    

    if(w == null && widths.length && widths.every(v => typeof v === 'number')){
      w = widths.reduce((a,b)=>a+b, 0);
    }

    if(!(typeof x0 === 'number' && typeof w === 'number')) return;
if(widths.length && widths.every(v => typeof v === "number")){
      // boundary after column idx = x0 + sum(widths[0..idx])
      const prefix = [];
      let acc = 0;
      for(let i=0;i<widths.length;i++){
        acc += widths[i];
        prefix[i] = acc;
      }
      xs = boundaries
        .map(idx => (idx>=0 && idx < prefix.length) ? (x0 + prefix[idx]) : null)
        .filter(x => typeof x === "number");
    }else{
      // Fallback: use cells of header/body (if exposed by this build)
      const headCellsObj = t.head?.[0]?.cells;
      const anyRowCellsObj = headCellsObj || t.body?.[0]?.cells;
      if(anyRowCellsObj){
        const ordered = Object.values(anyRowCellsObj)
          .filter(c => c && typeof c.x === "number" && typeof c.width === "number")
          .sort((a,b)=>a.x-b.x);
        xs = boundaries
          .map(idx => (idx>=0 && idx < ordered.length) ? (ordered[idx].x + ordered[idx].width) : null)
          .filter(x => typeof x === "number");
      }
    }

    // If we can't compute separator positions, still draw side borders uniformly.
    const prevLW = doc.getLineWidth ? doc.getLineWidth() : 0.2;
    doc.setDrawColor(0,0,0);

    // Left / Right borders (same thickness as separators)
    doc.setLineWidth(frameW);
    doc.line(x0, startY, x0, endY);
    doc.line(x0 + w, startY, x0 + w, endY);

    if(xs.length){
      doc.setLineWidth(sepVW);
      xs.forEach(x => doc.line(x, startY, x, endY));
    }

    doc.setLineWidth(prevLW);
  }catch(e){}
}

// Draw thick separators per CELL (row segments) to guarantee visibility on every page.
// Why: on some jsPDF-AutoTable builds, didDrawPage can be executed before the thin grid
// is fully rendered on continuation pages, making thick separators appear "not applied".
// Drawing AFTER each cell is rendered ensures thick separators stay on top.
//
// Use from autoTable wrapper:
//   didDrawCell: (cellData) => thickTableLinesDidDrawCell(cellData, { vAfter:[0,3,6,9], frameW:0.45, sepVW:0.45 })
function thickTableLinesDidDrawCell(cellData, cfg){
  try{
    if(!cellData || !cellData.doc || !cellData.cell || !cellData.column || !cellData.table) return;

    const doc = cellData.doc;
    const cell = cellData.cell;
    const colIdx = cellData.column.index;
    const t = cellData.table;

    const boundaries = Array.isArray(cfg?.vAfter) ? cfg.vAfter : [];
    if(!boundaries.length) return;

    const frameW = (typeof cfg?.frameW === 'number') ? cfg.frameW : RESULTS_THICK_W;
    const sepVW  = (typeof cfg?.sepVW  === 'number') ? cfg.sepVW  : RESULTS_THICK_W;

    // We only care about head/body sections (avoid footer)
    if(cellData.section !== 'head' && cellData.section !== 'body') return;

    // y-range for this cell (slightly extended to avoid micro-gaps)
    const eps = 0.15;
    const y1 = (typeof cell.y === 'number') ? (cell.y - eps) : null;
    const y2 = (typeof cell.y === 'number' && typeof cell.height === 'number') ? (cell.y + cell.height + eps) : null;
    if(!(typeof y1 === 'number' && typeof y2 === 'number') || y2 <= y1) return;

    const xL = (typeof cell.x === 'number') ? cell.x : null;
    const xR = (typeof cell.x === 'number' && typeof cell.width === 'number') ? (cell.x + cell.width) : null;
    if(!(typeof xL === 'number' && typeof xR === 'number')) return;

    const prevLW = doc.getLineWidth ? doc.getLineWidth() : 0.2;
    doc.setDrawColor(0,0,0);

    // Draw thick lines per ROW (only once on col 0) to survive colSpans / empty cells.
    // Some tables (ex: "Delta ligne") contain rows with merged/empty cells.
    // If we draw per boundary-column cell, some segments can be skipped on continuation pages.
    const cols = Array.isArray(t.columns) ? t.columns : null;
    const lastColIdx = (cols && cols.length) ? (cols.length - 1) : null;

    if(colIdx === 0 && cols){
      // We need the thick borders/separators to work even when AutoTable does NOT
      // provide per-column x/width on continuation pages (observed on page 5).
      // So we compute X from (startX + cumulative widths) as a primary path,
      // and cache the computed Xs on the table object for later pages.

      const cache = (t._nfThickCache && typeof t._nfThickCache === 'object') ? t._nfThickCache : (t._nfThickCache = {});

      // Determine table X origin
      const x0 = (typeof t.startX === 'number') ? t.startX : (typeof cols?.[0]?.x === 'number' ? cols[0].x : xL);

      // Determine column widths (always available even when x is missing)
      const wArr = cols.map(c => {
        if(typeof c.width === 'number') return c.width;
        if(typeof c.wrappedWidth === 'number') return c.wrappedWidth;
        if(typeof c.minWidth === 'number') return c.minWidth;
        return null;
      });

      let prefix = null;
      if(wArr.length && wArr.every(v => typeof v === 'number')){
        prefix = [];
        let acc = 0;
        for(let i=0;i<wArr.length;i++){
          acc += wArr[i];
          prefix[i] = acc;
        }
      }

      // Compute X positions for thick separators after selected columns
      let xs = [];
      if(prefix){
        xs = boundaries
          .map(b => (b>=0 && b < prefix.length) ? (x0 + prefix[b]) : null)
          .filter(x => typeof x === 'number');
      }else{
        // Fallback: direct column x/width if present
        xs = boundaries
          .map(b => {
            const c = cols[b];
            return (c && typeof c.x === 'number' && typeof c.width === 'number') ? (c.x + c.width) : null;
          })
          .filter(x => typeof x === 'number');
      }

      // Compute right border X
      let xRight = null;
      if(prefix){
        xRight = x0 + prefix[prefix.length - 1];
      }else if(lastColIdx != null){
        const cLast = cols[lastColIdx];
        if(cLast && typeof cLast.x === 'number' && typeof cLast.width === 'number') xRight = cLast.x + cLast.width;
      }
      if(!(typeof xRight === 'number')){
        // Fallback: table width or current cell right
        if(typeof t.width === 'number' && typeof x0 === 'number') xRight = x0 + t.width;
        else xRight = xR;
      }

      // Cache for later continuation pages where columns.x may be missing
      if(xs.length) cache.xs = xs;
      if(typeof xRight === 'number') cache.xRight = xRight;
      if(typeof x0 === 'number') cache.xLeft = x0;

      // Left frame
      doc.setLineWidth(frameW);
      doc.line(x0, y1, x0, y2);

      // Thick separators
      const xsUse = (Array.isArray(cache.xs) && cache.xs.length) ? cache.xs : xs;
      if(xsUse.length){
        doc.setLineWidth(sepVW);
        xsUse.forEach(x => doc.line(x, y1, x, y2));
      }

      // Right frame
      doc.setLineWidth(frameW);
      const xRightUse = (typeof cache.xRight === 'number') ? cache.xRight : xRight;
      if(typeof xRightUse === 'number') doc.line(xRightUse, y1, xRightUse, y2);
    }

    doc.setLineWidth(prevLW);
  }catch(e){}
}

function pdfLightBarDark(doc,y,text){
  y = nfGuardFooter(doc, y, 34);
  const x=10,w=190,h=7.5;
  // slightly darker grey than pdfLightBar
  pdfRect(doc,x,y,w,h,[230,230,230]);
  setTitleFont(doc);
  doc.setFontSize(10.5);
  doc.setTextColor(0,0,0);
  doc.text(text, x+w/2, y+5.2, {align:"center"});
  return y+h;
}

function pdfTypeStationBar(doc,y,text){
  y = nfGuardFooter(doc, y, 34);
  const x=10,w=190,h=7.5;

  // Orange opaque (sans transparence)
  try{ doc.setFillColor(255,90,23); }catch(e){}
  try{ doc.rect(x,y,w,h,'F'); }catch(e){ pdfRect(doc,x,y,w,h,[255,90,23]); }

  // Titre centré
  setTitleFont(doc);
  doc.setFontSize(10.5);
  doc.setTextColor(0,0,0);
  doc.text(String(text||""), x + w/2, y + 5.2, {align:"center"});
  doc.setTextColor(0,0,0);
  return y + h;
}



function pdfTolBar(doc,y,text){
  // POL1.5: Tolérances are already shown in the bottom "CONTRÔLES" block.
  // This top bar was redundant and has been intentionally removed.
  // Keep the function for backward compatibility with existing calls.
  return y; // no draw, no vertical space
}

// Helper: display IDs without Leica "@NN" suffix (keep internal raw IDs intact)
function nfStripAtId(s){
  try{
    if(s==null) return "";
    const str = String(s).trim();
    const i = str.indexOf("@");
    return (i>0)? str.substring(0,i) : str;
  }catch(_){ return String(s||""); }
}

// ========================= [D] PDF RENDERING ====================================
// (v1.76 base) drawHeaderV2: ancienne définition supprimée (consolidation)
// ===== NOUVEL EN-TÊTE PDF (gabarit) =====
// (v1.76 base) drawHeaderV2: ancienne définition supprimée (consolidation)
function pdfStationLibreFull(doc, y, data){
  // Robustness: stationLibre block may be absent depending on AppLog
  if(!data || !data.stationLibre || !data.stationLibre.results){
    try{ setBodyFont(doc); doc.setFontSize(10); doc.text("Aucune station trouvée dans l'AppLog.", 10, y+8); }catch(_){ }
    return y + 14;
  }

  const SR = data.stationLibre.results;

  // Prevent the station block from being drawn into the footer area.
  // This was causing overlaps when a new station starts near the bottom of a page.
  y = ensurePdfRoom(doc, y, 46);

  // "Type de station" doit être présenté comme les encarts (Observations / Résidus)
  y = drawStationBlock(doc, y, {station: SR, stationLibre: SR});
  y = nfGuardFooter(doc, y, 40);
  y = pdfLightBar(doc, y, "OBSERVATIONS");
  const obsList = Array.isArray(data.stationLibre.observations) ? data.stationLibre.observations : [];
  const obsBody = obsList.map(o => [
    nfStripAtId(o.id ?? ""),
    fmt(o.hz,4),
    fmt(o.vz,4),
    fmt(o.dp,3),
    fmt(o.hr,3),
    fmt(o.constPrisme,4)
  ]);

  nfAutoTable(doc, {
    startY: y + 1,
    head: [["ID","Hz","Vz","Dp","Hr","Const prisme"]],
    body: obsBody,
    margin: { left:10, right:10 },
    styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.9, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
    headStyles: { fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
    
    didParseCell: function (dataCell) {
      try{
        const row = dataCell.row?.raw;
        // row layout: [id, Xc,Yc,Zc, Xm,Ym,Zm, Dx,Dy,Dz, STATUT]
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
alternateRowStyles: null
  });
  y = doc.lastAutoTable.finalY + 3;

  // Résidus
  y = nfGuardFooter(doc, y, 40);
  y = pdfLightBarDark(doc, y, "RÉSIDUS");
  const resAll = Array.isArray(data.stationLibre.residuals) ? data.stationLibre.residuals : [];
  // Keep only "Utilisé" residuals (AppLog): exclude "Non"
  const resUsed = resAll.filter(r => {
    const u = (r?.used ?? "").toString().trim().toLowerCase();
    return u && u !== "non";
  });
  const resBody = resUsed.map(r => [ nfStripAtId(r.id ?? ""), fmt(r.dHz,4), fmt(r.dAlti,3), fmt(r.dDH,3), r.used ?? "" ]);
nfAutoTable(doc, {
    startY: y + 1,
    head: [["ID","dHz","dAlti","dDH","Utilisé"]],
    body: resBody,
    margin: { left:10, right:10 },
    styles: { fontSize: 7.2, textColor:[0,0,0], cellPadding: 0.8, minCellHeight: 4.9, halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
    headStyles: { fillColor:[230,230,230], textColor:[0,0,0], fontStyle:"bold", halign:"center", valign:"middle", lineWidth:0.2, lineColor:[0,0,0] },
    alternateRowStyles: null
  });
  y = doc.lastAutoTable.finalY + 3;

  return y;
}

function pdfStationLibreFullRun(doc, y, stationLibreRun){
  // Same rendering as pdfStationLibreFull, but for a given stationLibre snapshot (run)
  const data = { stationLibre: stationLibreRun };
  // Reuse the existing function by wrapping in a minimal object
  return pdfStationLibreFull(doc, y, data);
}


function pdfFooterAllPagesAddress(doc){
  const total = doc.getNumberOfPages();
  for(let p=1; p<=total; p++){
    doc.setPage(p);
    const w=doc.internal.pageSize.getWidth();
    const h=doc.internal.pageSize.getHeight();

    doc.setDrawColor(200);
    doc.line(10, h-14, w-10, h-14);

    setBodyFont(doc);
    // Address + build under it (ASCII-safe)
    doc.setFontSize(9);
    doc.text(NOVATLAS_ADDRESS, w/2, h-9, {align:"center"});
    doc.setFontSize(8);
    doc.text(`Build ${APP_BUILD}`, w/2, h-5.5, {align:"center"});

    doc.setFontSize(9);
    doc.text(`Page ${p} / ${total}`, w-10, h-9, {align:"right"});
  }
}

// Ensures there is enough free space for the last-page footer box.
// IMPORTANT: some flows end with manual text blocks (no autoTable), so lastAutoTable.finalY
// may not reflect the current cursor Y. Allow passing an explicit Y to avoid overlaps.
function ensureRoomForLastFooter(doc, minFree=110, curYOverride=null){
  const h = doc.internal.pageSize.getHeight();
  let curY = 40;
  if(typeof curYOverride === "number" && Number.isFinite(curYOverride)){
    curY = curYOverride;
  }else if(doc.lastAutoTable && doc.lastAutoTable.finalY){
    curY = doc.lastAutoTable.finalY;
  }
  if(curY > h - minFree){
    doc.addPage();
  }
}


function pdfFooterLastPageBox(doc, R, stats){
  const last = doc.getNumberOfPages();
  doc.setPage(last);

  const w = doc.internal.pageSize.getWidth();
  const h = doc.internal.pageSize.getHeight();

  const x = 10, pageW = 190;

  // Footer code start
  let y = h - 78; // lowered to sit closer to footer

  // CONTRÔLES (au-dessus de la ligne des cadres)
  {
    const S = stats || {};
    const boxH = 14;
    pdfRect(doc, x, y, pageW, boxH, null);
    pdfRect(doc, x, y, pageW, 6, [245,245,245]);
    setTitleFont(doc);
    doc.setFontSize(9.5);
    doc.text("CONTRÔLES", x + pageW/2, y + 4.4, { align:"center" });

    setBodyFont(doc);
    doc.setFontSize(8.8);

    const totalTxt = (S.total!=null && Number.isFinite(S.total)) ? String(S.total) : "";
    const okTxt    = (S.ok!=null && Number.isFinite(S.ok)) ? String(S.ok) : "";
    const koTxt    = (S.ko!=null && Number.isFinite(S.ko)) ? String(S.ko) : "";
    const neTxt    = (S.ne!=null && Number.isFinite(S.ne)) ? String(S.ne) : "";
    const sdXTxt   = (S.sdDx!=null && Number.isFinite(S.sdDx)) ? fmt(S.sdDx,3) : "";
    const sdYTxt   = (S.sdDy!=null && Number.isFinite(S.sdDy)) ? fmt(S.sdDy,3) : "";
    const sdZTxt   = (S.sdDz!=null && Number.isFinite(S.sdDz)) ? fmt(S.sdDz,3) : "";

    const l1 = `Points mesurés : ${totalTxt}    Valides : ${okTxt}    Refuses : ${koTxt}    Non eval. : ${neTxt}`;

    const xyOn = document.getElementById("tolXYOn")?.checked ?? true;
    const zOn  = document.getElementById("tolZOn")?.checked ?? true;
    const tXYv = Number(document.getElementById("tolXY")?.value);
    const tZv  = Number(document.getElementById("tolZ")?.value);
    const tolOn = document.getElementById("optTol")?.checked ?? false;

    const tolLine = tolOn
      ? `Tol : XY=${xyOn ? (Number.isFinite(tXYv) ? tXYv : "—") : "OFF"} ; Z=${zOn ? (Number.isFinite(tZv) ? tZv : "—") : "OFF"}`
      : "";

    doc.text(l1, x+3, y+9.0);
    if(tolLine) doc.text(tolLine, x+3, y+12.5);

    y += boxH + 2;
  }

  // Ligne de 3 cadres : OBSERVATIONS | RÉALISÉ PAR | VALIDÉ PAR
  const gap = 2;
  const boxW = (pageW - gap*2) / 3;
  const boxH = 46;
  const titles = ["OBSERVATIONS", "RÉALISÉ PAR", "VALIDÉ PAR"];

  for(let i=0;i<3;i++){
    const bx = x + i*(boxW+gap);
    const by = y;

    pdfRect(doc, bx, by, boxW, boxH, null);
    pdfRect(doc, bx, by, boxW, 10, [245,245,245]);

    setTitleFont(doc);
    doc.setFontSize(9.5);
    doc.text(titles[i], bx + boxW/2, by + 6.8, { align:"center" });
    if(i === 2){
      setBodyFont(doc);
      doc.setFontSize(7.2);
      doc.text("(client)", bx + boxW/2, by + 9.4, { align:"center" });
      setTitleFont(doc);
      doc.setFontSize(9.5);
    }

    const contentTop = by + 10;

    if(i === 0){
      // OBSERVATIONS : rectangle standard + texte wrap
      pdfRect(doc, bx, contentTop, boxW, boxH-10, null);
      setBodyFont(doc);
      doc.setFontSize(8.3);
      const text = String((R && (R.obs || R.observations)) || "").trim();
      const lines = doc.splitTextToSize(text, boxW-4);
      if(lines.length){
        doc.text(lines.slice(0,6), bx+2, contentTop+4, { baseline:"top" });
      }
      continue;
    }

    // Signature boxes (RÉALISÉ PAR / VALIDÉ PAR)
    const rowH = 8;
    pdfRect(doc, bx, contentTop, boxW, rowH, null);
    pdfRect(doc, bx, contentTop+rowH, boxW, rowH, null);
    const visaY = contentTop + 2*rowH;
    const visaH = (by + boxH) - visaY;
    pdfRect(doc, bx, visaY, boxW, visaH, null);

    setBodyFont(doc);
    doc.setFontSize(8.5);
    doc.text("Nom :",  bx+2, contentTop+5.2);
    doc.text("Date :", bx+2, contentTop+rowH+5.2);
    doc.text("Visa :", bx+2, visaY+6);

    // Auto-remplissage sur RÉALISÉ PAR (cadre du milieu)
    if(i === 1){
      if(R && R.surveyor) doc.text(R.surveyor, bx+16, contentTop+5.2);
      if(R && R.date) doc.text(R.date, bx+16, contentTop+rowH+5.2);
      if(typeof sigDataUrl !== "undefined" && sigDataUrl){
        try{
          const wImg = 25;
          const hImg = 8;
          doc.addImage(sigDataUrl, sigImageType, bx + (boxW - wImg)/2, visaY + (visaH - hImg)/2, wImg, hImg);
        }catch(e){}
      }
    }
  }
}



/* =========================
   PDF builders
========================= */

// ========================= STATION BLOCK (robuste) ============================
// Dessine un bloc "TYPE DE STATION" + contenu, avec hauteur auto.
// ASCII only (jsPDF) + hauteur auto (wrap).
function drawStationBlock(doc, y, R){
  y = pdfTypeStationBar(doc, y, "TYPE DE STATION");
  y += 1.5;

  const S = (R && (R.station || R.stationLibre || R.stationInfo || R.freeStation || R.station_data)) || {};
  const method = S.method || S.type || S.methode || "Station libre";
  const id = (S.id != null ? S.id : (S.idStation != null ? S.idStation : (S.stationId != null ? S.stationId : "")));

  const E = (S.E ?? S.X ?? (S.coord && (S.coord.E ?? S.coord.X)) ?? "");
  const N = (S.N ?? S.Y ?? (S.coord && (S.coord.N ?? S.coord.Y)) ?? "");
  const H = (S.H ?? S.Z ?? (S.coord && (S.coord.H ?? S.coord.Z)) ?? "");

  // Valeurs issues AppLog (Station libre)
  const corr = (S.corrOrient ?? S.CorrOri ?? S.corrOri ?? "");
  const az   = (S.azOrient ?? S.azOri ?? S.AzOri ?? S.azimuthOri ?? S.azimuthOrient ?? "");
  const scale = (S.scaleFactor ?? S.facteurEchelle ?? S.facteurEchellePpm ?? S.scale ?? "");
  const devE  = (S.devE ?? S.sE ?? S.AE ?? S.sigmaE ?? "");
  const devN  = (S.devN ?? S.sN ?? S.AN ?? S.sigmaN ?? "");
  const devH  = (S.devH ?? S.sH ?? S.AH ?? S.sigmaH ?? "");
  const devOri = (S.devOri ?? S.sOri ?? S.sigmaOri ?? S.SigmaOri ?? "");

  const lines = [];
  lines.push(`Methode : ${method}`);
  if(String(id).trim() !== "") lines.push(`ID station : ${id}`);
  if(String(E).trim() !== "" || String(N).trim() !== "" || String(H).trim() !== ""){
    lines.push(`Coordonnees : E=${E}  N=${N}  H=${H}`);
  }
  // Détails station (POL1.5 step4b): ...
  // On force l'affichage des champs attendus (même si vides) pour rester fidèle à l'AppLog.
  if(String(corr).trim() !== "" || String(az).trim() !== "" || String(devOri).trim() !== "" ||
     String(devE).trim() !== "" || String(devN).trim() !== "" || String(devH).trim() !== "" ||
     String(scale).trim() !== ""){
    const azDisp = (String(az).trim()==="" ? "—" : az);
    const corrDisp = (String(corr).trim()==="" ? "—" : corr);
    const scaleDisp = (String(scale).trim()==="" ? "—" : scale);
    const devEDisp = (String(devE).trim()==="" ? "—" : devE);
    const devNDisp = (String(devN).trim()==="" ? "—" : devN);
    const devHDisp = (String(devH).trim()==="" ? "—" : devH);
    const devODisp = (String(devOri).trim()==="" ? "—" : devOri);
    lines.push(`Corr. orientat° : ${corrDisp} | Fact. échelle : ${scaleDisp} | Dev.std E : ${devEDisp} | N : ${devNDisp} | Z : ${devHDisp} | Ori : ${devODisp}`);
    // Garder aussi une ligne "Orientation" ...
    lines.push(`Orientation : CorrOri=${String(corr).trim()==="" ? "—" : corr}  AzOri=${azDisp}`);
  }

  setBodyFont(doc);
  doc.setFontSize(8.8);
  const lineH = 3.9;
  const maxW = 190 - 6;
  const wrapped = [];
  for(const t of lines){
    const w = doc.splitTextToSize(String(t), maxW);
    for(const wline of w) wrapped.push(wline);
  }
  const boxH = Math.max(12, wrapped.length*lineH + 5);

  pdfRect(doc, 10, y, 190, boxH, null);
  let yy = y + 6;
  for(const t of wrapped){
    doc.text(String(t), 13, yy);
    yy += lineH;
  }
  return y + boxH + 2;
}


// ---- Public API (used by boot.js and other modules) ----
// Expose the rendering + parsing pipeline on window to avoid regressions where
// buttons remain disabled because the import handler can't reach these functions.
try {
  window.renderAll = renderAll;
  // Même précaution que pour parseLandXmlLeica ci-dessus : capturer la référence
  // d'origine AVANT de réaffecter window.parseTxtLeica1200, pour éviter une
  // récursion infinie au premier appel.
  const nfOriginalParseTxtLeica1200 = parseTxtLeica1200;
  window.parseTxtLeica1200 = function(text, fileName){
    const data = nfOriginalParseTxtLeica1200(text);
    try{ nfScanCoordinateAnomalies(data, fileName); }catch(_){ }
    return data;
  };
} catch (_) {}


// -------------------------------
// Patch 3 — Expose exclusions to other modules (PDF generation / button enablement)
// -------------------------------
try{
  window.nfGetExcludedPointIds = function(){ return Array.from(nfExcludedPointIds || []); };
  window.nfIsPointExcluded = function(id){ const k = nfPid(id); return !!k && (nfExcludedPointIds||new Set()).has(k); };
  window.nfGetRefAltiPointIds = function(){ return Array.from(nfRefAltiPointIds || []); };
  window.nfIsRefAltiPoint = function(id){ const k = nfPid(id); return !!k && (nfRefAltiPointIds||new Set()).has(k); };
}catch(_){ /* ignore */ }

