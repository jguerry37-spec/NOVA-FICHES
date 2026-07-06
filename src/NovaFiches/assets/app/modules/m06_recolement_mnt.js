/*
===============================================================================
Nova-Fiches - Récolement MNT / DTM
Lecture stricte des resultats Leica ApplicationStakeout avec StakeoutDTMHeight.
Ce module ne lit pas les implantations simples : seules les lignes MNT/DTM sont
exploitees.
===============================================================================
*/

(function(){
  'use strict';

  const state = {
    landXmlText: null,
    dxfText: null,
    dxfFaces: [],
    dxfFileName: null,
    rows: [],
    mode: 'leica',
    lastRender: null,
  };

  function qs(id){ return document.getElementById(id); }

  function setPill(id, text, kind){
    const el = qs(id);
    if(!el) return;
    el.textContent = text;
    el.classList.remove('ok','err','warn','nf-hidden');
    if(kind) el.classList.add(kind);
  }

  function tryParseFloatAny(v){
    if(v === null || v === undefined) return null;
    const n = Number.parseFloat(String(v).trim().replace(',', '.'));
    return Number.isFinite(n) ? n : null;
  }

  function fmt3(v){
    const n = Number(v);
    return Number.isFinite(n) ? n.toFixed(3) : '';
  }

  function fmtDz(v){
    const n = Number(v);
    return Number.isFinite(n) ? n.toFixed(3) : '';
  }

  function businessKey(id, fallback){
    const s = String(fallback || id || '').trim();
    if(!s) return '';
    const i = s.indexOf('@');
    return (i > 0 ? s.slice(0, i) : s).trim();
  }

  function duplicateMeta(rows){
    const groups = new Map();
    for(const r of (Array.isArray(rows) ? rows : [])){
      const k = businessKey(r.id, r.businessPointId);
      if(!k) continue;
      if(!groups.has(k)) groups.set(k, []);
      groups.get(k).push(r);
    }
    const out = new Map();
    let colorIndex = 0;
    for(const [k, arr] of Array.from(groups.entries()).sort((a,b)=>String(a[0]).localeCompare(String(b[0]), 'fr', { numeric:true, sensitivity:'base' }))){
      if(arr.length <= 1) continue;
      out.set(k, { key:k, count:arr.length, colorIndex:colorIndex++ });
    }
    return out;
  }

  function dupRowStyle(info){
    if(!info) return '';
    const palette = [
      { bg:'#fff3b0', bar:'#ff2d55' },
      { bg:'#dff7ff', bar:'#00a3ff' },
      { bg:'#e9fbe7', bar:'#22c55e' },
      { bg:'#f4e8ff', bar:'#8b5cf6' }
    ];
    const c = palette[Math.abs(Number(info.colorIndex || 0)) % palette.length];
    return `background:${c.bg};box-shadow:inset 4px 0 0 ${c.bar};`;
  }

  function escapeAttr(s){
    try{
      if(window.CSS && typeof window.CSS.escape === 'function') return window.CSS.escape(String(s));
    }catch(_){ }
    return String(s).replace(/\\/g,'\\\\').replace(/"/g,'\\"');
  }

  function buildHexagonGridMap(dom){
    const map = new Map();
    try{
      const nodes = dom.querySelectorAll('*[uniqueID]');
      for(const node of nodes){
        const uid = node.getAttribute('uniqueID');
        if(!uid) continue;
        let grid = null;
        const stack = [node];
        while(stack.length){
          const cur = stack.pop();
          if(cur && cur.localName === 'Grid'){ grid = cur; break; }
          if(!cur || !cur.children) continue;
          for(let i=0;i<cur.children.length;i++) stack.push(cur.children[i]);
        }
        if(!grid) continue;
        const e = tryParseFloatAny(grid.getAttribute('e'));
        const n = tryParseFloatAny(grid.getAttribute('n'));
        if(e === null || n === null) continue;
        map.set(uid, { x:e, y:n });
      }
    }catch(_){ }
    return map;
  }

  function readCgPointMap(dom){
    const gridMap = buildHexagonGridMap(dom);
    const map = new Map();
    const pts = dom.querySelectorAll('CgPoint, CgPoints CgPoint');
    pts.forEach(p=>{
      const name = p.getAttribute('name') || p.getAttribute('oID') || '';
      if(!name) return;
      const txt = String(p.textContent || '').trim();
      const parts = txt.split(/\s+/).filter(Boolean);
      if(parts.length < 2) return;

      let x = null, y = null;
      const gm = gridMap.get(name);
      if(gm){
        x = gm.x;
        y = gm.y;
      }else{
        const a = tryParseFloatAny(parts[0]);
        const b = tryParseFloatAny(parts[1]);
        if(a === null || b === null) return;
        // Leica LandXML may store CgPoint text as N E Z. For French grids, Northing is often > 1,000,000.
        const looksNE = a > 1000000 && b < 1000000;
        x = looksNE ? b : a;
        y = looksNE ? a : b;
      }
      const z = parts.length >= 3 ? tryParseFloatAny(parts[2]) : null;
      const tsAttr = p.getAttribute('timeStamp') || '';
      const tsMs = Number.isFinite(Date.parse(tsAttr)) ? Date.parse(tsAttr) : null;
      const oid = p.getAttribute('oID') || name;
      map.set(name, { id:name, businessPointId: oid, x, y, z, tsMs });
    });
    return map;
  }

  function readFileText(file){
    return new Promise((resolve, reject)=>{
      try{
        const fr = new FileReader();
        fr.onerror = ()=>reject(new Error('Lecture fichier impossible'));
        fr.onload = ()=>resolve(String(fr.result || ''));
        fr.readAsText(file);
      }catch(e){ reject(e); }
    });
  }

  function parseDxf3dFaces(text){
    const lines = String(text || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
    const pairs = [];
    for(let i=0;i<lines.length-1;i+=2){
      pairs.push({ code:String(lines[i] || '').trim(), value:String(lines[i+1] || '').trim() });
    }
    const faces = [];
    function num(v){ const n = Number.parseFloat(String(v || '').replace(',', '.')); return Number.isFinite(n) ? n : null; }
    function samePt(a,b){ return a && b && Math.abs(a.x-b.x)<1e-9 && Math.abs(a.y-b.y)<1e-9 && Math.abs(a.z-b.z)<1e-9; }
    function addTri(a,b,c){
      if(!a || !b || !c) return;
      const den = ((b.y-c.y)*(a.x-c.x) + (c.x-b.x)*(a.y-c.y));
      if(Math.abs(den) < 1e-12) return;
      faces.push({
        a,b,c, den,
        minX: Math.min(a.x,b.x,c.x), maxX: Math.max(a.x,b.x,c.x),
        minY: Math.min(a.y,b.y,c.y), maxY: Math.max(a.y,b.y,c.y)
      });
    }
    for(let i=0;i<pairs.length;i++){
      if(pairs[i].code !== '0' || String(pairs[i].value).toUpperCase() !== '3DFACE') continue;
      const pts = [ {}, {}, {}, {} ];
      for(let j=i+1;j<pairs.length && pairs[j].code !== '0';j++){
        const code = pairs[j].code;
        const val = num(pairs[j].value);
        if(val === null) continue;
        const pidx = ({'10':0,'20':0,'30':0,'11':1,'21':1,'31':1,'12':2,'22':2,'32':2,'13':3,'23':3,'33':3})[code];
        if(pidx === undefined) continue;
        if(code === '10' || code === '11' || code === '12' || code === '13') pts[pidx].x = val;
        if(code === '20' || code === '21' || code === '22' || code === '23') pts[pidx].y = val;
        if(code === '30' || code === '31' || code === '32' || code === '33') pts[pidx].z = val;
      }
      const p = pts.map(q => (Number.isFinite(q.x) && Number.isFinite(q.y) && Number.isFinite(q.z)) ? q : null);
      addTri(p[0], p[1], p[2]);
      if(p[3] && !samePt(p[2], p[3])) addTri(p[0], p[2], p[3]);
    }
    return faces;
  }

  function zOnDxfSurface(x, y, faces){
    const eps = 1e-8;
    for(const f of faces || []){
      if(x < f.minX - eps || x > f.maxX + eps || y < f.minY - eps || y > f.maxY + eps) continue;
      const l1 = ((f.b.y-f.c.y)*(x-f.c.x) + (f.c.x-f.b.x)*(y-f.c.y)) / f.den;
      const l2 = ((f.c.y-f.a.y)*(x-f.c.x) + (f.a.x-f.c.x)*(y-f.c.y)) / f.den;
      const l3 = 1 - l1 - l2;
      if(l1 >= -eps && l2 >= -eps && l3 >= -eps){
        return l1*f.a.z + l2*f.b.z + l3*f.c.z;
      }
    }
    return null;
  }

  function topoRowsFromLastDataDxf(faces){
    const ld = window.__NF_LASTDATA || window.lastData || null;
    const rows = [];
    const stations = Array.isArray(ld?.topoStations) ? ld.topoStations : [];
    for(const st of stations){
      const sid = st?.stationName || st?.setupId || 'TOPO';
      const res = Array.isArray(st?.results) ? st.results : [];
      for(const p of res){
        const id = String(p?.id || '').trim();
        const a = Number(p?.E);
        const b = Number(p?.N);
        const z = Number(p?.H);
        if(!id || !Number.isFinite(a) || !Number.isFinite(b) || !Number.isFinite(z)) continue;
        const looksNE = a > 1000000 && b < 1000000;
        const x = looksNE ? b : a;
        const y = looksNE ? a : b;
        const zTheo = zOnDxfSurface(x, y, faces);
        if(zTheo === null || !Number.isFinite(zTheo)) continue;
        rows.push({
          id, x, y, z,
          zTheo,
          dz: z - zTheo,
          tsMs: Number.isFinite(Date.parse(p?.timeStamp || '')) ? Date.parse(p.timeStamp) : null,
          appNumber: '',
          start: p?.timeStamp || '',
          stationId: sid,
          businessPointId: businessKey(id),
          source: 'DXF'
        });
      }
    }
    rows.sort((a,b)=>String(a.id).localeCompare(String(b.id), 'fr', { numeric:true, sensitivity:'base' }));
    return rows;
  }
  function parseMntRows(xmlText){
    const dom = new DOMParser().parseFromString(String(xmlText || ''), 'text/xml');
    const pointMap = readCgPointMap(dom);
    const rows = [];

    const nodes = dom.querySelectorAll('ApplicationStakeout');
    nodes.forEach(n=>{
      const isMnt = String(n.getAttribute('StakeoutDTMHeight') || '').toLowerCase() === 'true';
      if(!isMnt) return;

      const id = n.getAttribute('StakedPointID') || '';
      if(!id) return;
      const p = pointMap.get(id);
      if(!p) return;

      const zTheo = tryParseFloatAny(n.getAttribute('DesignPointOrthoHeight'));
      const dz = tryParseFloatAny(n.getAttribute('StakeoutHeightDiff'));
      if(zTheo === null || dz === null) return;

      const appNumber = n.getAttribute('ApplicationNumber') || '';
      const start = n.getAttribute('ApplicationStartDateTime') || '';
      rows.push({
        id,
        businessPointId: p.businessPointId || businessKey(id),
        x: p.x,
        y: p.y,
        z: p.z,
        zTheo,
        dz,
        tsMs: p.tsMs,
        appNumber,
        start,
        stationId: null,
      });
    });

    rows.sort((a,b)=>{
      const ta = a.tsMs ?? Date.parse(a.start || '') ?? 0;
      const tb = b.tsMs ?? Date.parse(b.start || '') ?? 0;
      if(ta !== tb) return ta - tb;
      return String(a.id).localeCompare(String(b.id), 'fr', { numeric:true, sensitivity:'base' });
    });
    try{
      attachStations(rows, dom);
    }catch(e){
      console.warn('MNT station association failed', e);
      rows.forEach(r => { r.stationId = r.stationId || 'MNT'; });
    }
    return rows;
  }

  function stationRunsFromLastData(){
    try{
      const ld = window.__NF_LASTDATA || window.lastData || null;
      return (ld && Array.isArray(ld.stationLibreRuns)) ? ld.stationLibreRuns : [];
    }catch(_){ return []; }
  }

  function xmlNodes(dom, tagName){
    const a = Array.from(dom.getElementsByTagName(tagName) || []);
    let b = [];
    try{ b = Array.from(dom.getElementsByTagNameNS('*', tagName) || []); }catch(_){ b = []; }
    return Array.from(new Set(a.concat(b)));
  }

  function parseStationSetupApps(dom){
    const setupToStation = new Map();
    xmlNodes(dom, 'InstrumentSetup').forEach(n=>{
      const setupId = n.getAttribute('id') || '';
      const station = n.getAttribute('stationName') || '';
      if(setupId && station) setupToStation.set(setupId, station);
    });

    const apps = [];
    xmlNodes(dom, 'ApplicationTPSSetupResults').forEach(n=>{
      const app = Number.parseInt(n.getAttribute('ApplicationNumber') || '', 10);
      const setupId = n.getAttribute('TPSSetupUniqueID') || '';
      const station = setupToStation.get(setupId) || '';
      if(Number.isFinite(app) && station) apps.push({ app, station });
    });
    apps.sort((a,b)=>a.app-b.app);
    return apps;
  }

  function stationFromAppNumber(appNumber, setupApps){
    const app = Number.parseInt(appNumber || '', 10);
    if(!Number.isFinite(app) || !setupApps.length) return null;
    let best = null;
    for(const setup of setupApps){
      if(setup.app < app) best = setup;
      else break;
    }
    return best ? best.station : null;
  }

  function attachStations(rows, dom){
    const runsAll = stationRunsFromLastData();
    const setupApps = parseStationSetupApps(dom);
    const stationIds = [];
    for(const r of runsAll){
      const keys = [r?.results?.idStation, r?.results?.stationName, r?.stationName, r?.idStation].filter(Boolean).map(String);
      for(const sid of keys){
        if(sid && !stationIds.includes(sid)) stationIds.push(sid);
      }
    }
    for(const setup of setupApps){
      if(setup.station && !stationIds.includes(setup.station)) stationIds.push(setup.station);
    }

    const stationTimes = new Map();
    for(const sid of stationIds){
      try{
        let node = null;
        const pts = xmlNodes(dom, 'CgPoint');
        for(const p of pts){
          if((p.getAttribute('name') || p.getAttribute('oID') || '') === sid){ node = p; break; }
        }
        const tsAttr = node ? (node.getAttribute('timeStamp') || '') : '';
        const ts = Number.isFinite(Date.parse(tsAttr)) ? Date.parse(tsAttr) : null;
        stationTimes.set(sid, ts);
      }catch(_){ stationTimes.set(sid, null); }
    }
    const stationsOrdered = stationIds.map(id => ({ id, t: stationTimes.get(id) ?? null }));

    function pickStation(tsMs){
      if(!stationsOrdered.length) return 'MNT';
      if(tsMs == null) return stationsOrdered[0].id;
      let best = stationsOrdered[0].id;
      let bestT = -Infinity;
      for(const s of stationsOrdered){
        if(s.t == null) continue;
        if(s.t <= tsMs && s.t > bestT){
          bestT = s.t;
          best = s.id;
        }
      }
      return best;
    }

    rows.forEach(r => {
      r.stationId = stationFromAppNumber(r.appNumber, setupApps) || pickStation(r.tsMs);
    });
  }

  function getTolZ(){
    try{
      const O = (typeof getOptions === 'function') ? getOptions() : null;
      if(!O || !O.tolOn || !O.zOn || !Number.isFinite(O.tZ)) return null;
      return Math.abs(Number(O.tZ));
    }catch(_){ return null; }
  }

  function statusForDz(dz){
    const tol = getTolZ();
    if(tol === null) return '';
    return Math.abs(Number(dz)) <= tol ? 'VALIDE' : 'REFUSE';
  }

  function statusHtml(dz){
    const st = statusForDz(dz);
    if(!st) return '<span class="st st-na">-</span>';
    return st === 'VALIDE' ? '<span class="st st-ok">VALIDE</span>' : '<span class="st st-ko">REFUSE</span>';
  }

  function analyse(){
    const ld = window.__NF_LASTDATA || window.lastData || null;
    const raw = (ld && ld.rawText) ? String(ld.rawText) : state.landXmlText;
    if(!raw || raw.length < 20){
      state.rows = [];
      setPill('mntStatus', 'MNT : LandXML requis', 'warn');
      render();
      return;
    }
    const xml = (typeof window.nfResolveLandXmlPointDuplicates === 'function')
      ? window.nfResolveLandXmlPointDuplicates(raw, 'Récolement MNT')
      : raw;
    state.landXmlText = xml;
    state.rows = parseMntRows(xml);
    state.mode = 'leica';
    if(!state.rows.length && state.dxfFaces.length){
      state.rows = topoRowsFromLastDataDxf(state.dxfFaces);
      state.mode = 'dxf';
    }
    const n = state.rows.length;
    const label = state.mode === 'dxf' ? 'DXF 3D' : 'MNT Leica';
    setPill('mntStatus', n ? `${label} : ${n} point(s)` : (state.dxfFaces.length ? 'DXF 3D : aucun point topo dans la surface' : 'MNT : aucune donnée'), n ? 'ok' : 'warn');
    render();
  }

  function render(){
    const tbody = qs('mntTable')?.querySelector('tbody');
    if(!tbody) return;
    tbody.innerHTML = '';

    const dups = duplicateMeta(state.rows);
    for(const r of state.rows){
      const tr = document.createElement('tr');
      const dupInfo = dups.get(businessKey(r.id, r.businessPointId));
      if(dupInfo){
        tr.setAttribute('style', dupRowStyle(dupInfo));
        tr.title = `Doublon LandXML : ${dupInfo.key} (${dupInfo.count} occurrences)`;
      }
      const cells = [
        r.id,
        fmt3(r.x),
        fmt3(r.y),
        fmt3(r.z),
        fmt3(r.zTheo),
        fmtDz(r.dz),
        r.stationId || '',
      ];
      for(const c of cells){
        const td = document.createElement('td');
        td.textContent = c;
        if(/^-?\d/.test(String(c))) td.className = 'mono';
        tr.appendChild(td);
      }
      const tdSt = document.createElement('td');
      tdSt.innerHTML = statusHtml(r.dz);
      tr.appendChild(tdSt);
      tbody.appendChild(tr);
    }

    const btnPdf = qs('btnMntPdf');
    const btnTxt = qs('btnMntExportTxt');
    const ok = state.rows.length > 0;
    if(btnPdf) btnPdf.disabled = !ok;
    if(btnTxt) btnTxt.disabled = !ok;
    drawVisual();
  }

  function drawVisual(){
    const canvas = qs('mntCanvas');
    const msg = qs('mntVisualStatus');
    if(!canvas) return;
    const ctx = canvas.getContext('2d');
    const w = canvas.width, h = canvas.height;
    ctx.clearRect(0,0,w,h);
    ctx.fillStyle = '#fff';
    ctx.fillRect(0,0,w,h);
    if(!state.rows.length){
      if(msg) msg.textContent = 'Aucun point MNT à afficher.';
      return;
    }

    const xs = state.rows.map(r=>r.x).filter(Number.isFinite);
    const ys = state.rows.map(r=>r.y).filter(Number.isFinite);
    if(!xs.length || !ys.length){
      if(msg) msg.textContent = 'Coordonnées XY indisponibles.';
      return;
    }
    let minX = Math.min(...xs), maxX = Math.max(...xs);
    let minY = Math.min(...ys), maxY = Math.max(...ys);
    const dx = Math.max(0.001, maxX - minX);
    const dy = Math.max(0.001, maxY - minY);
    minX -= dx * 0.05; maxX += dx * 0.05;
    minY -= dy * 0.05; maxY += dy * 0.05;
    const pad = 28;
    const sx = (w - 2*pad) / Math.max(0.001, maxX - minX);
    const sy = (h - 2*pad) / Math.max(0.001, maxY - minY);
    const scale = Math.min(sx, sy);
    const tol = getTolZ();

    function map(r){
      return {
        x: pad + (r.x - minX) * scale,
        y: h - pad - (r.y - minY) * scale,
      };
    }
    function color(r){
      if(tol === null) return '#1267f3';
      return Math.abs(r.dz) <= tol ? '#219653' : '#d64545';
    }

    ctx.strokeStyle = '#d1d5db';
    ctx.lineWidth = 1;
    ctx.strokeRect(0.5,0.5,w-1,h-1);

    for(const r of state.rows){
      const p = map(r);
      ctx.beginPath();
      ctx.arc(p.x, p.y, 3.2, 0, Math.PI*2);
      ctx.fillStyle = color(r);
      ctx.fill();
    }
    if(msg){
      const maxAbs = Math.max(...state.rows.map(r=>Math.abs(r.dz)).filter(Number.isFinite));
      msg.textContent = `${state.rows.length} point(s) - Dz max ${fmtDz(maxAbs)} m`;
    }
  }

  function rowsForPdf(){
    // PdfSharp "implantation" renderer expects the validated 11-column layout.
    // For MNT, theoretical XY is intentionally empty: only Z théo MNT is meaningful.
    return state.rows.map(r => [
      r.id,
      '',
      '',
      fmt3(r.zTheo),
      fmt3(r.x),
      fmt3(r.y),
      fmt3(r.z),
      '',
      '',
      fmtDz(r.dz),
      statusForDz(r.dz),
    ]);
  }

  function buildMntByStation(rows){
    const by = new Map();
    for(const r of rows){
      const sid = r.stationId || '';
      if(!by.has(sid)) by.set(sid, []);
      by.get(sid).push(r);
    }
    return Array.from(by.entries()).map(([stationId, items]) => ({
      stationId,
      rows: items.map(r => [
        r.id,
        '',
        '',
        fmt3(r.zTheo),
        fmt3(r.x),
        fmt3(r.y),
        fmt3(r.z),
        '',
        '',
        fmtDz(r.dz),
        statusForDz(r.dz),
      ])
    }));
  }


  function buildMntPlanView(){
    return {
      title: 'VUE EN PLAN - RÉCOLEMENT MNT',
      style: 'mnt',
      markerShape: 'cross',
      hideLabels: true,
      hideImplantedRings: true,
      rotateLongAxisVertical: true,
      showNorthArrow: true,
      pointsAll: state.rows.map(r => ({ id: '', key: r.id, x: r.x, y: r.y })),
      pointsImplanted: []
    };
  }

  function runStationKeys(run){
    return [run?.results?.idStation, run?.results?.stationName, run?.stationName, run?.idStation]
      .filter(Boolean)
      .map(String);
  }

  function buildStationPayload(){
    const runsAll = stationRunsFromLastData();
    const used = new Set(state.rows.map(r=>r.stationId).filter(Boolean).map(String));
    const matched = new Set();
    let runs = runsAll.filter(r => {
      const keys = runStationKeys(r);
      const hit = keys.some(k => used.has(k));
      if(hit) keys.forEach(k => { if(used.has(k)) matched.add(k); });
      return hit;
    });

    for(const sid of used){
      if(matched.has(sid)) continue;
      runs.push({
        results: { idStation: sid, stationName: sid, E: null, N: null, H: null },
        observations: [],
        residuals: []
      });
    }

    if(typeof nfSanitizeStationRunsForPdf_ === 'function') runs = nfSanitizeStationRunsForPdf_(runs);
    return runs;
  }

  function commonPayloadBase(){
    const R = (typeof rf === 'function') ? (rf() || {}) : {};
    const O = (typeof getOptions === 'function') ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };
    const ld = window.__NF_LASTDATA || window.lastData || null;
    return {
      elements: (R && R.elements) ? R.elements : '',
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : '',
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : '',
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : '',
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : '',
      intervenant: (typeof getAutoIntervenant === 'function') ? getAutoIntervenant(R) : '',
      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : '',
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : '',
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : '',
      ...((typeof window.nfPdfInstrumentMeta === 'function')
        ? window.nfPdfInstrumentMeta(ld, R)
        : {
            model: String(ld?.meta?.instrument || '').trim(),
            appareil: String(ld?.meta?.instrument || '').trim(),
            appareilModel: String(ld?.meta?.instrument || '').trim(),
            serialNumber: String(ld?.meta?.serial || '').trim(),
            appareilSerial: String(ld?.meta?.serial || '').trim()
          }),
      ville: (R && R.ville) ? R.ville : '',
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : '',
      cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : '',
      validation: {
        statutOn: !!O.tolOn,
        tolXYOn: false,
        tolZOn: !!O.zOn,
        tolXY: null,
        tolZ: Number.isFinite(O.tZ) ? O.tZ : null,
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ''
      },
      obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : '',
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : '',
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : '',
      geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : '',
      Intervenant: (typeof getAutoIntervenant === 'function') ? getAutoIntervenant(R) : '',
      signatureDataUrl: (typeof sigDataUrl !== 'undefined' && sigDataUrl) ? String(sigDataUrl) : '',
      signatureImageType: (typeof sigImageType !== 'undefined' && sigImageType) ? String(sigImageType) : 'JPEG',
    };
  }

  function generatePdf(){
    try{
      if(!state.rows.length){ setPill('mntStatus', 'MNT : analyse requise', 'warn'); return; }
      const tolTxt = (typeof buildTolText === 'function') ? buildTolText() : '';
      const payload = {
        type: 'pdfsharp_implantation',
        title: 'RÉCOLEMENT MNT',
        subTitle: (state.mode === 'dxf' ? 'Comparaison points topo LandXML / surface DXF 3D' : (tolTxt ? ('Tolérance Z : ' + tolTxt) : '')),
        header: ['ID point','X théo','Y théo','Z théo MNT','X relevé','Y relevé','Z relevé','Dx','Dy','Dz','STATUT'],
        rows: rowsForPdf(),
        implantationByStation: buildMntByStation(state.rows),
        suppressObsTables: true,
        topoStations: [],
        stationLibreRuns: buildStationPayload(),
        planView: buildMntPlanView(),
        fileName: (typeof buildExportFileName === 'function')
          ? buildExportFileName('RECOLEMENT_MNT', 'PDF')
          : 'NOVA_Recolement_MNT.pdf',
        ...commonPayloadBase(),
      };
      if(window.chrome?.webview?.postMessage) window.chrome.webview.postMessage(payload);
      else setPill('mntStatus', 'MNT : WebView2 indisponible', 'err');
    }catch(e){
      console.error(e);
      setPill('mntStatus', 'MNT : erreur PDF', 'err');
    }
  }

  function saveTextFile(fileName, content){
    let name = String(fileName || 'recolement_mnt.txt').replace(/[\\/:*?"<>|]+/g, '_').trim();
    if(!name.toLowerCase().endsWith('.txt')) name += '.txt';
    const blob = new Blob([String(content || '')], { type:'text/plain;charset=utf-8' });
    if(typeof window.saveBlobAs === 'function') return window.saveBlobAs(blob, name, 'text/plain');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    document.body.appendChild(a);
    a.click();
    setTimeout(()=>{ try{ URL.revokeObjectURL(a.href); }catch(_){} try{ a.remove(); }catch(_){} }, 0);
  }

  function exportTxt(){
    if(!state.rows.length) return;
    const lines = [];
    lines.push(['ID','X_releve','Y_releve','Z_releve','Z_theorique','Dz','Station','Statut'].join('\t'));
    for(const r of state.rows){
      lines.push([r.id, fmt3(r.x), fmt3(r.y), fmt3(r.z), fmt3(r.zTheo), fmtDz(r.dz), r.stationId || '', statusForDz(r.dz)].join('\t'));
    }
    const fn = (typeof buildExportFileName === 'function')
      ? buildExportFileName('RECOLEMENT_MNT', 'TXT')
      : 'NOVA_Recolement_MNT.txt';
    saveTextFile(fn, lines.join('\n'));
  }

  function refresh(){
    const ld = window.__NF_LASTDATA || window.lastData || null;
    const raw = (ld && ld.rawText) ? String(ld.rawText) : null;
    if(raw && raw.length > 20){
      state.landXmlText = raw;
      analyse();
    }else{
      setPill('mntStatus', 'MNT : LandXML requis', 'warn');
      render();
    }
  }

  function bind(){
    qs('btnMntAnalyse')?.addEventListener('click', analyse);
    qs('mntDxfInput')?.addEventListener('change', async (ev)=>{
      try{
        const file = ev?.target?.files?.[0];
        if(!file) return;
        setPill('mntDxfStatus', 'DXF : lecture...', 'warn');
        state.dxfText = await readFileText(file);
        state.dxfFileName = file.name || 'surface.dxf';
        state.dxfFaces = parseDxf3dFaces(state.dxfText);
        setPill('mntDxfStatus', state.dxfFaces.length ? `DXF : ${state.dxfFaces.length} triangle(s)` : 'DXF : aucune 3DFACE', state.dxfFaces.length ? 'ok' : 'err');
        analyse();
      }catch(e){
        console.error(e);
        setPill('mntDxfStatus', 'DXF : erreur', 'err');
      }
    });
    qs('btnMntPdf')?.addEventListener('click', generatePdf);
    qs('btnMntExportTxt')?.addEventListener('click', exportTxt);
    qs('landXmlInput')?.addEventListener('change', ()=>setTimeout(refresh, 80));
    ['optTol','tolZOn','tolZ'].forEach(id => qs(id)?.addEventListener('change', render));
    qs('tolZ')?.addEventListener('input', render);
    setTimeout(refresh, 250);
  }

  window.NOVA_refreshMntRecolement = refresh;
  document.addEventListener('DOMContentLoaded', bind);
})();




