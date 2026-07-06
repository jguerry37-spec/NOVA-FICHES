/*
===============================================================================
Nova-Fiches — Récolement de pieux (PATCH 2)
Objectif:
- Import TXT (référence) + analyse pieux depuis LandXML (levé)
- Exclure tout pieu dont le N° est présent dans ApplStakeout
- Calcul centre XY (cercle moyen) + résidus
- Garde-fous: min 3 points, max 10 points
- Si max résidu > 4 cm : contrôle interactif (exclusion points + recalcul live)

IMPORTANT:
- Ne modifie pas m02_parser_calc.
- N'altère pas les flux PDF existants (PDF pieux viendra en patch suivant).
- Lecture seule sur lastData.rawText (LandXML déjà importé par le pipeline existant).
===============================================================================
*/

(function(){
  'use strict';

  const CFG = {
    minPts: 3,
    maxPts: 10,
    reviewMaxResid_m: 0.04, // 4 cm
  };

  const state = {
    landXmlText: null,
    txtText: null,
    refs: new Map(),          // key -> {id,key,base,x,y,z?}
    lastControlledBase: null,
    lastControlledKey: null,
    wantPlan: false,
    groups: new Map(),        // key -> group object
    lastRender: null,

    // Review modal
    review: {
      open: false,
      base: null,
      tmpIncluded: null, // boolean[]
    }
  };

  // --------- Utils
  function setPill(id, text, kind){
    const el = document.getElementById(id);
    if(!el) return;
    el.textContent = text;
    el.classList.remove('ok','err','warn','nf-hidden');
    if(kind) el.classList.add(kind);
  }

  function fmtNum(v, digits=3){
    if(v === null || v === undefined || !Number.isFinite(v)) return '—';
    return Number(v).toFixed(digits);
  }

  function fmtResidMm(m){
    if(m === null || m === undefined || !Number.isFinite(m)) return '—';
    return `${Math.round(m*1000)} mm`;
  }

  function readFileText(file){
    return new Promise((resolve, reject)=>{
      try{
        const fr = new FileReader();
        fr.onerror = ()=> reject(new Error('Lecture fichier impossible'));
        fr.onload = ()=> resolve(String(fr.result || ''));
        fr.readAsText(file);
      }catch(e){ reject(e); }
    });
  }

    function numericBaseFromKey(key){
    const m = String(key ?? '').match(/(\d+)(?:\.BIS)?$/i);
    if(!m) return null;
    const n = parseInt(m[1], 10);
    return Number.isFinite(n) ? n : null;
  }

  function normalizePileKeyRaw(raw){
    if(raw === null || raw === undefined) return null;
    let s = String(raw).trim();
    if(!s) return null;
    s = s.replace(',', '.').replace(/\s+/g, '').toUpperCase();
    s = s.replace(/^E(?=\d+(?:\.\d+)?-P\d+)/, 'Z');

    let m = s.match(/^(?:PIEU|PI|P)[._-]?0*(\d+)$/i);
    if(m) return String(parseInt(m[1], 10));

    m = s.match(/^0*(\d+)$/);
    if(m) return String(parseInt(m[1], 10));

    m = s.match(/^(Z\d+(?:\.\d+)?-P)0*(\d+)(\.BIS)?$/i);
    if(m) return `${m[1].toUpperCase()}${parseInt(m[2], 10)}${(m[3] || '').toUpperCase()}`;

    m = s.match(/^(G\d+-P)0*(\d+)(\.BIS)?$/i);
    if(m) return `${m[1].toUpperCase()}${parseInt(m[2], 10)}${(m[3] || '').toUpperCase()}`;

    m = s.match(/^(P[EG])0*(\d+)$/i);
    if(m) return `${m[1].toUpperCase()}${parseInt(m[2], 10)}`;

    return s;
  }

  function tryParseMeasuredPointName(raw){
    if(!raw) return null;
    const s = String(raw).trim().replace(',', '.').replace(/\s+/g, '').toUpperCase();
    let m = s.match(/^([EZ]\d+(?:\.\d+)?-P\d+(?:\.BIS)?)\.(\d+)$/i);
    if(m){
      const key = normalizePileKeyRaw(m[1]);
      return { key, label: key, base: numericBaseFromKey(key), idx: parseInt(m[2], 10) };
    }

    m = s.match(/^(?:PIEU|PI|P)[._-]?0*(\d+)\.(\d+)$/i);
    if(m){
      const base = parseInt(m[1], 10);
      return { key: String(base), label: String(base), base, idx: parseInt(m[2], 10) };
    }

    m = s.match(/^0*(\d+)\.(\d+)$/);
    if(m){
      const base = parseInt(m[1], 10);
      return { key: String(base), label: String(base), base, idx: parseInt(m[2], 10) };
    }

    // Generic pile naming: keep the complete name before the last ".n" as the pile key.
    // Examples: T62.1 -> T62, ABC-12.3 -> ABC-12, Z3-P12.2 -> Z3-P12.
    m = s.match(/^(.+)\.(\d+)$/);
    if(m){
      const key = normalizePileKeyRaw(m[1]);
      if(key) return { key, label: key, base: numericBaseFromKey(key), idx: parseInt(m[2], 10) };
    }

    return null;
  }

  function tryParseBaseIdx(raw){
    const p = tryParseMeasuredPointName(raw);
    return p ? { base: p.base, idx: p.idx, key: p.key, label: p.label } : null;
  }

  function tryParseBaseFromId(raw){
    return numericBaseFromKey(normalizePileKeyRaw(raw));
  }

  function naturalPileCompare(a, b){
    const aa = String(a?.label || a?.key || a?.base || '');
    const bb = String(b?.label || b?.key || b?.base || '');
    return aa.localeCompare(bb, 'fr', { numeric: true, sensitivity: 'base' });
  }

  function groupBandStyle(index){
    const palette = [
      { bg:'#fff7d6', bar:'#ffb020' },
      { bg:'#e8f7ff', bar:'#00a3ff' }
    ];
    const c = palette[Math.abs(Number(index || 0)) % palette.length];
    return `background:${c.bg};box-shadow:inset 4px 0 0 ${c.bar};`;
  }

  function tryParseFloatAny(v){
    if(v === null || v === undefined) return null;
    const s = String(v).trim().replace(',', '.');
    const n = Number.parseFloat(s);
    return Number.isFinite(n) ? n : null;
  }

  function parseStakeoutBases(xmlText){
    const set = new Set();
    try{
      // Lightweight extraction (regex) to avoid namespace headaches
      // DesignPointID="PI204" / StakedPointID="IPI204"
      const rx = /(DesignPointID|StakedPointID)\s*=\s*"([^"]+)"/ig;
      let m;
      while((m = rx.exec(xmlText)) !== null){
        const id = m[2];
        const key = normalizePileKeyRaw(id);
        if(key) set.add(key);
      }
    }catch(e){ /* ignore */ }
    return set;
  }

  function buildHexagonGridMap(dom){
    // Map uniqueID -> { e, n } from HexagonLandXML Point/Coordinates/.../Grid e="" n="".
    // Avoid namespace headaches by walking nodes and matching localName.
    const map = new Map();
    try{
      const nodes = dom.querySelectorAll('*[uniqueID]');
      for(const node of nodes){
        const uid = node.getAttribute('uniqueID');
        if(!uid) continue;

        // Find first descendant with localName == 'Grid'
        let grid = null;
        const stack = [node];
        while(stack.length){
          const cur = stack.pop();
          if(cur && cur.localName === 'Grid') { grid = cur; break; }
          if(!cur || !cur.children) continue;
          for(let i=0;i<cur.children.length;i++) stack.push(cur.children[i]);
        }
        if(!grid) continue;
        const e = tryParseFloatAny(grid.getAttribute('e'));
        const n = tryParseFloatAny(grid.getAttribute('n'));
        if(e === null || n === null) continue;
        map.set(uid, { e, n });
      }
    }catch(e){ /* ignore */ }
    return map;
  }

  function parseMeasuredGroups(xmlText){
    const groups = new Map();
    try{
      const dom = new DOMParser().parseFromString(xmlText, 'text/xml');
      const gridMap = buildHexagonGridMap(dom);
      // role="measured" points
      const pts = dom.querySelectorAll('CgPoint[role="measured"], CgPoints CgPoint[role="measured"]');
      pts.forEach(p=>{
        const name = p.getAttribute('name') || '';
        const bi = tryParseMeasuredPointName(name);
        if(!bi) return;
        const tsAttr = p.getAttribute('timeStamp') || '';
        const tsMs = Number.isFinite(Date.parse(tsAttr)) ? Date.parse(tsAttr) : null;
        const txt = (p.textContent || '').trim();
        if(!txt) return;
        const parts = txt.split(/\s+/).filter(Boolean);
        if(parts.length < 2) return;
        let x = null;
        let y = null;

        // HexagonLandXML provides explicit Grid e/n for many points. Prefer that when available.
        const gm = gridMap.get(name);
        if(gm){
          x = gm.e;
          y = gm.n;
        } else {
          // LandXML CgPoint text is Northing/Easting/Height. Nova-Fiches works as X/Y/Z,
          // so the second value is X and the first value is Y.
          const a = tryParseFloatAny(parts[0]);
          const b = tryParseFloatAny(parts[1]);
          if(a === null || b === null) return;
          x = b;
          y = a;
        }
        const z = parts.length >= 3 ? tryParseFloatAny(parts[2]) : 0;
        if(x === null || y === null) return;

        const obs = { rawName: name, key: bi.key, label: bi.label, base: bi.base, idx: bi.idx, x, y, z: z ?? 0, tsMs };
        if(!groups.has(obs.key)) groups.set(obs.key, { key: obs.key, label: obs.label, base: obs.base, points: [] });
        groups.get(obs.key).points.push(obs);
      });
    }catch(e){
      console.error('[Pieux] parseMeasuredGroups error', e);
    }
    return groups;
  }

  function parseRefsTxt(txtText){
    const refs = new Map();
    const lines = String(txtText||'').split(/\r?\n/);
    for(const line0 of lines){
      const line = String(line0||'').trim();
      if(!line) continue;
      const parts = line.split('\t').map(s=>String(s||'').trim());
      if(parts.length < 3) continue;
      const key = normalizePileKeyRaw(parts[0]);
      if(!key) continue;
      const base = numericBaseFromKey(key);
      const x = tryParseFloatAny(parts[1]);
      const y = tryParseFloatAny(parts[2]);
      const z = parts.length >= 4 ? tryParseFloatAny(parts[3]) : null;
      if(x === null || y === null) continue;
      refs.set(key, { id: parts[0] || key, key, label: parts[0] || key, base, x, y, z }); // keep last if exact duplicate
    }
    return refs;
  }

  // --------- Circle fit (XY only)
  function solve3x3(A, b){
    // Gaussian elimination (in place copy)
    const M = [
      [A[0][0], A[0][1], A[0][2], b[0]],
      [A[1][0], A[1][1], A[1][2], b[1]],
      [A[2][0], A[2][1], A[2][2], b[2]],
    ];

    for(let col=0; col<3; col++){
      // pivot
      let pivot = col;
      for(let r=col+1; r<3; r++){
        if(Math.abs(M[r][col]) > Math.abs(M[pivot][col])) pivot = r;
      }
      if(Math.abs(M[pivot][col]) < 1e-12) return null;
      if(pivot !== col){
        const tmp = M[col]; M[col] = M[pivot]; M[pivot] = tmp;
      }
      // normalize
      const div = M[col][col];
      for(let c=col; c<4; c++) M[col][c] /= div;
      // eliminate
      for(let r=0; r<3; r++){
        if(r === col) continue;
        const f = M[r][col];
        for(let c=col; c<4; c++){
          M[r][c] -= f * M[col][c];
        }
      }
    }
    return [M[0][3], M[1][3], M[2][3]];
  }

  function fitCircle3(p1, p2, p3){
    // Circumcenter of triangle (deterministic)
    const x1=p1.x, y1=p1.y;
    const x2=p2.x, y2=p2.y;
    const x3=p3.x, y3=p3.y;

    const a = x1 - x2, b = y1 - y2;
    const c = x1 - x3, d = y1 - y3;

    const e = ((x1*x1 - x2*x2) + (y1*y1 - y2*y2)) / 2.0;
    const f = ((x1*x1 - x3*x3) + (y1*y1 - y3*y3)) / 2.0;

    const det = a*d - b*c;
    if(Math.abs(det) < 1e-12) return null;

    const cx = (d*e - b*f) / det;
    const cy = (-c*e + a*f) / det;
    const r = Math.hypot(x1 - cx, y1 - cy);
    if(!Number.isFinite(cx) || !Number.isFinite(cy) || !Number.isFinite(r)) return null;
    return { cx, cy, r };
  }

  function fitCircleLeastSquares(points){
    const n = points.length;
    if(n < 3) return null;
    if(n === 3){
      return fitCircle3(points[0], points[1], points[2]);
    }

    // Numerical stability: center coordinates (large Easting/Northing values).
    let mx = 0, my = 0;
    for(const p of points){ mx += p.x; my += p.y; }
    mx /= n; my /= n;

    // Algebraic fit on centered coords: x^2+y^2 + A x + B y + C = 0
    let Sx=0, Sy=0, Sxx=0, Syy=0, Sxy=0;
    let Sxxx=0, Sxxy=0, Sxyy=0, Syyy=0;

    for(const p of points){
      const x = p.x - mx;
      const y = p.y - my;
      const xx = x*x, yy = y*y;
      Sx += x; Sy += y;
      Sxx += xx; Syy += yy; Sxy += x*y;
      Sxxx += xx*x;
      Sxxy += xx*y;
      Sxyy += x*yy;
      Syyy += yy*y;
    }

    const A = [
      [Sxx, Sxy, Sx],
      [Sxy, Syy, Sy],
      [Sx , Sy , n ],
    ];
    const b = [
      -(Sxxx + Sxyy),
      -(Sxxy + Syyy),
      -(Sxx  + Syy ),
    ];

    const sol = solve3x3(A, b);
    if(!sol) return null;

    const a = sol[0], bb = sol[1], c = sol[2];
    const cx0 = -a/2.0;
    const cy0 = -bb/2.0;

    // Radius squared in centered system
    let r2 = (a*a + bb*bb)/4.0 - c;
    // Clamp tiny negatives caused by floating-point noise
    if(r2 < 0 && r2 > -1e-10) r2 = 0;
    if(r2 <= 0) return null;

    const r = Math.sqrt(r2);
    const cx = cx0 + mx;
    const cy = cy0 + my;

    if(!Number.isFinite(cx) || !Number.isFinite(cy) || !Number.isFinite(r)) return null;
    return { cx, cy, r };
  }

function computeResiduals(points, fit){
    // Returns resid per point (meters): |distance - r|
    const out = [];
    for(const p of points){
      const d = Math.hypot(p.x - fit.cx, p.y - fit.cy);
      const resid = Math.abs(d - fit.r);
      out.push(resid);
    }
    return out;
  }

  function computeFitStats(points, included, fit){
    // stats only on included points
    const resAll = computeResiduals(points, fit);
    let max=0, sum2=0, cnt=0;
    for(let i=0;i<points.length;i++){
      if(!included[i]) continue;
      const r = resAll[i];
      if(r>max) max=r;
      sum2 += r*r;
      cnt++;
    }
    const rms = cnt ? Math.sqrt(sum2/cnt) : NaN;
    return { resAll, maxResid: max, rmsResid: rms };
  }


  function popcountBits(v){
    let c = 0;
    while(v){ v &= (v - 1); c++; }
    return c;
  }

  function isBetterSubsetCandidate(a, b){
    if(!b) return true;
    const eps = 1e-9;
    if(a.score < b.score - eps) return true;
    if(a.score > b.score + eps) return false;

    if(a.refDist < b.refDist - eps) return true;
    if(a.refDist > b.refDist + eps) return false;

    if(a.maxResid < b.maxResid - eps) return true;
    if(a.maxResid > b.maxResid + eps) return false;

    if(a.rmsResid < b.rmsResid - eps) return true;
    if(a.rmsResid > b.rmsResid + eps) return false;

    if(a.count > b.count) return true;
    if(a.count < b.count) return false;

    return false;
  }

  function chooseBestIncluded(points, refPoint){
    const n = points.length;
    if(!refPoint || !Number.isFinite(refPoint.x) || !Number.isFinite(refPoint.y) || n < CFG.minPts){
      return null;
    }

    const maxMask = (1 << n);
    let bestAcceptable = null;
    let bestAny = null;

    for(let mask = 0; mask < maxMask; mask++){
      const count = popcountBits(mask);
      if(count < CFG.minPts) continue;

      const included = new Array(n).fill(false);
      const used = [];
      for(let i=0;i<n;i++){
        if(mask & (1 << i)){
          included[i] = true;
          used.push(points[i]);
        }
      }

      const fit = fitCircleLeastSquares(used);
      if(!fit) continue;

      const st = computeFitStats(points, included, fit);
      const refDist = Math.hypot(fit.cx - refPoint.x, fit.cy - refPoint.y);
      const omitted = n - count;
      const score = refDist + (omitted * 0.005) + (st.maxResid * 0.25) + (st.rmsResid * 0.10);

      const cand = { included, fit, refDist, maxResid: st.maxResid, rmsResid: st.rmsResid, count, score };

      if(st.maxResid <= CFG.reviewMaxResid_m){
        if(isBetterSubsetCandidate(cand, bestAcceptable)) bestAcceptable = cand;
      }
      if(isBetterSubsetCandidate(cand, bestAny)) bestAny = cand;
    }

    return bestAcceptable || bestAny;
  }

  function autoChooseBestSubset(g){
    if(!g || g.excludedStakeout || g.tooFew || g.tooMany || !g.hasRef) return false;
    const ref = state.refs.get(g.key);
    if(!ref) return false;

    const best = chooseBestIncluded(g.points || [], ref);
    if(!best || !best.included) return false;

    const prev = JSON.stringify(g.included || []);
    const next = JSON.stringify(best.included);
    g.included = best.included.slice();
    recomputeGroup(g);

    if(prev !== next) g.reviewed = true;
    return true;
  }

  function recomputeGroup(g){
    const pts = g.points || [];
    const incl = g.included || pts.map(()=>true);
    const used = pts.filter((_,i)=>incl[i]);
    if(used.length < CFG.minPts){
      g.fit = null;
      g.residuals = pts.map(()=>null);
      g.maxResid = null;
      g.rmsResid = null;
      g.needsReview = false;
      return;
    }
    const fit = fitCircleLeastSquares(used);
    if(!fit){
      g.fit = null;
      g.residuals = pts.map(()=>null);
      g.maxResid = null;
      g.rmsResid = null;
      g.needsReview = false;
      return;
    }
    // stats in context of all points, but gate on included
    const st = computeFitStats(pts, incl, fit);
    g.fit = fit;
    g.residuals = st.resAll;
    g.maxResid = st.maxResid;
    g.rmsResid = st.rmsResid;
    g.needsReview = (Number.isFinite(g.maxResid) && g.maxResid > CFG.reviewMaxResid_m);
  }

  // --------- Analysis build
  function buildAnalysis(){
    const xmlText = (typeof window.nfResolveLandXmlPointDuplicates === 'function')
      ? window.nfResolveLandXmlPointDuplicates(state.landXmlText, 'Recolement pieux')
      : state.landXmlText;
    state.landXmlText = xmlText;
    if(!xmlText) throw new Error('LandXML non chargé');

    const stakeoutBases = parseStakeoutBases(xmlText);
    const measured = parseMeasuredGroups(xmlText);
    const out = new Map();

    measured.forEach((gg, key)=>{
      const pts = gg.points || [];
      // sort by idx (stable)
      pts.sort((a,b)=> (a.idx||0) - (b.idx||0));
      const g = {
        key,
        label: gg.label || key,
        base: gg.base,
        points: pts,
        included: pts.map(()=>true),
        excludedStakeout: stakeoutBases.has(key),
        tooFew: pts.length < CFG.minPts,
        tooMany: pts.length > CFG.maxPts,
        hasRef: state.refs.has(key),
        fit: null,
        residuals: [],
        maxResid: null,
        rmsResid: null,
        needsReview: false,
        reviewed: false,
      };
      // Only compute fit if candidate is valid (not excluded and within min/max)
      if(!g.excludedStakeout && !g.tooFew && !g.tooMany){
        recomputeGroup(g);
        autoChooseBestSubset(g);
      }
      out.set(key, g);
    });

    state.groups = out;
    state.lastRender = { stakeoutCount: stakeoutBases.size };
  }

  // --------- Render main table
  function render(){
    const tbody = document.querySelector('#pieuxTable tbody');
    if(!tbody) return;
    tbody.innerHTML = '';

    const groups = Array.from(state.groups.values()).sort(naturalPileCompare);

    for(let gi=0; gi<groups.length; gi++){
      const g = groups[gi];
      const tr = document.createElement('tr');
      tr.dataset.key = String(g.key);
      tr.setAttribute('style', groupBandStyle(gi));

      const tdBase = document.createElement('td');
      tdBase.textContent = String(g.label || g.key || g.base || "");
      tr.appendChild(tdBase);

      const tdN = document.createElement('td');
      tdN.textContent = String((g.points||[]).length);
      tr.appendChild(tdN);

      const tdSt = document.createElement('td');
      tdSt.innerHTML = g.excludedStakeout ? '<span class="st st-ko">EXCLU</span>' : '<span class="st st-ok">OK</span>';
      tr.appendChild(tdSt);

      const tdMm = document.createElement('td');
      if(g.tooFew) tdMm.innerHTML = `<span class="st st-ko">&lt; ${CFG.minPts}</span>`;
      else if(g.tooMany) tdMm.innerHTML = `<span class="st st-ko">&gt; ${CFG.maxPts}</span>`;
      else tdMm.innerHTML = '<span class="st st-ok">OK</span>';
      tr.appendChild(tdMm);

      const tdRef = document.createElement('td');
      tdRef.innerHTML = g.hasRef ? '<span class="st st-ok">OK</span>' : '<span class="st st-na">—</span>';
      tr.appendChild(tdRef);

      const tdCx = document.createElement('td');
      tdCx.textContent = g.fit ? fmtNum(g.fit.cx, 3) : '—';
      tr.appendChild(tdCx);

      const tdCy = document.createElement('td');
      tdCy.textContent = g.fit ? fmtNum(g.fit.cy, 3) : '—';
      tr.appendChild(tdCy);

      const tdMr = document.createElement('td');
      tdMr.textContent = (g.maxResid !== null && Number.isFinite(g.maxResid)) ? fmtResidMm(g.maxResid) : '—';
      tr.appendChild(tdMr);

      const tdStat = document.createElement('td');
      if(g.excludedStakeout || g.tooFew || g.tooMany || !g.fit){
        tdStat.innerHTML = '<span class="st st-na">—</span>';
      }else if(g.needsReview){
        tdStat.innerHTML = '<span class="st st-ko">À contrôler</span>';
        tr.classList.add('nf-clickable');
        tr.title = 'Max résidu > 4 cm : cliquer pour contrôler';
      }else{
        tdStat.innerHTML = '<span class="st st-ok">OK</span>';
        // Allow manual review even when under threshold (useful for validation / inspection)
        tr.classList.add('nf-clickable');
        tr.title = 'Cliquer pour contrôler (manuel)';
      }
      tr.appendChild(tdStat);

      // Grey out excluded ones (purely visual)
      if(g.excludedStakeout){
        tr.style.opacity = '0.55';
      }

      // Click handler for review
      tr.addEventListener('click', ()=>{
        if(!g.excludedStakeout && !g.tooFew && !g.tooMany && g.fit){
          openReview(g.key);
        }
      });

      tbody.appendChild(tr);
    }

    const analysed = groups.length;
    const excluded = groups.filter(g=>g.excludedStakeout).length;
    const valid = groups.filter(g=>!g.excludedStakeout && !g.tooFew && !g.tooMany).length;
    const withRef = groups.filter(g=>g.hasRef && !g.excludedStakeout && !g.tooFew && !g.tooMany).length;
    const toReview = groups.filter(g=>g.needsReview && !g.excludedStakeout && !g.tooFew && !g.tooMany).length;

    const msg = `Pieux : ${analysed} (valides: ${valid}, exclus stakeout: ${excluded}, avec réf: ${withRef}, à contrôler: ${toReview})`;
    setPill('pieuxAnalyseStatus', msg, analysed ? (toReview ? 'warn' : (excluded ? 'warn' : 'ok')) : null);
  }

  // --------- Review modal
  function qs(id){ return document.getElementById(id); }

  function openReview(key){
    const g = state.groups.get(key);
    if(!g) return;

    state.review.open = true;
    state.review.base = key;
    state.review.tmpIncluded = g.included.slice();

    const overlay = qs('pieuxReviewOverlay');
    if(overlay) overlay.classList.remove('nf-hidden');

    const title = qs('pieuxReviewTitle');
    if(title){
      title.textContent = `Pieu ${g.label || g.key || key} - ${g.points.length} point(s)`;
    }

    renderReviewTable();
    recalcReviewLive();
  }

  function closeReview(){
    state.review.open = false;
    state.review.base = null;
    state.review.tmpIncluded = null;
    const overlay = qs('pieuxReviewOverlay');
    if(overlay) overlay.classList.add('nf-hidden');
  }

  function renderReviewTable(){
    const base = state.review.base;
    const g = state.groups.get(base);
    if(!g) return;

    const tbody = qs('pieuxReviewTable')?.querySelector('tbody');
    if(!tbody) return;
    tbody.innerHTML = '';

    for(let i=0;i<g.points.length;i++){
      const p = g.points[i];
      const tr = document.createElement('tr');

      const tdInc = document.createElement('td');
      const cb = document.createElement('input');
      cb.type = 'checkbox';
      cb.checked = !!state.review.tmpIncluded[i];
      cb.addEventListener('change', ()=>{
        state.review.tmpIncluded[i] = cb.checked;
        recalcReviewLive();
      });
      tdInc.appendChild(cb);
      tr.appendChild(tdInc);

      const tdName = document.createElement('td');
      tdName.textContent = p.rawName || `${p.base}.${p.idx}`;
      tr.appendChild(tdName);

      const tdX = document.createElement('td');
      tdX.className = 'mono';
      tdX.textContent = fmtNum(p.x, 3);
      tr.appendChild(tdX);

      const tdY = document.createElement('td');
      tdY.className = 'mono';
      tdY.textContent = fmtNum(p.y, 3);
      tr.appendChild(tdY);

      const tdR = document.createElement('td');
      tdR.className = 'mono';
      tdR.dataset.residIdx = String(i);
      tdR.textContent = '—';
      tr.appendChild(tdR);

      tbody.appendChild(tr);
    }
  }

  function recalcReviewLive(){
    const base = state.review.base;
    const g = state.groups.get(base);
    if(!g) return;

    const warn = qs('pieuxReviewWarn');
    if(warn) warn.textContent = '';

    const incl = state.review.tmpIncluded;
    const used = g.points.filter((_,i)=>incl[i]);
    if(used.length < CFG.minPts){
      if(warn) warn.textContent = `Minimum ${CFG.minPts} points inclus.`;
      if(qs('pieuxReviewCenter')) qs('pieuxReviewCenter').textContent = '—';
      if(qs('pieuxReviewMaxResid')) qs('pieuxReviewMaxResid').textContent = '—';
      // Clear residues display
      const cells = qs('pieuxReviewTable')?.querySelectorAll('[data-resid-idx]');
      cells?.forEach(c=>{ c.textContent = '—'; });
      return;
    }

    const fit = fitCircleLeastSquares(used);
    if(!fit){
      if(warn) warn.textContent = 'Calcul cercle impossible (points alignés ?).';
      return;
    }

    const st = computeFitStats(g.points, incl, fit);

    // Update displays
    if(qs('pieuxReviewCenter')) qs('pieuxReviewCenter').textContent = `X=${fmtNum(fit.cx,3)}  Y=${fmtNum(fit.cy,3)}`;
    if(qs('pieuxReviewMaxResid')) qs('pieuxReviewMaxResid').textContent = fmtResidMm(st.maxResid);

    // Resid per point
    const cells = qs('pieuxReviewTable')?.querySelectorAll('[data-resid-idx]');
    cells?.forEach(c=>{
      const idx = parseInt(c.getAttribute('data-resid-idx')||'0',10);
      const r = st.resAll[idx];
      c.textContent = Number.isFinite(r) ? fmtResidMm(r) : '—';
    });
  }

  function applyReview(){
    const base = state.review.base;
    const g = state.groups.get(base);
    if(!g) return;

    // Apply included flags
    g.included = state.review.tmpIncluded.slice();
    recomputeGroup(g);
    g.reviewed = true;

    state.lastControlledKey = g.key;
    state.lastControlledBase = Number.isFinite(g.base) ? g.base : null;

    // Confirmation visuelle (audit V2) : la validation n'affichait auparavant aucun
    // retour, la fermeture était immédiate et silencieuse. On affiche brièvement un
    // état de succès avant de fermer, pour que l'utilisateur voie que c'est pris en compte.
    const confirmEl = qs('pieuxReviewConfirm');
    if(confirmEl){
      confirmEl.classList.remove('nf-hidden');
      setTimeout(() => {
        confirmEl.classList.add('nf-hidden');
        closeReview();
        render();
      }, 600);
    }else{
      closeReview();
      render();
    }
  }

  // --------- Enable buttons
  function refreshAnalyseButton(){
    const b = document.getElementById('btnPieuxAnalyse');
    if(!b) return;
    // Ensure we always consider the latest parsed LandXML (even if the input change event didn't fire)
    try{
      if(!state.landXmlText || state.landXmlText.length < 20){
        const ld = window.__NF_LASTDATA || window.lastData || null;
        const raw = ld && ld.rawText ? String(ld.rawText) : null;
        if(raw && raw.length > 20) state.landXmlText = raw;
      }
    }catch(_){ /* ignore */ }
    const landOk = !!(state.landXmlText && state.landXmlText.length > 20);
    b.disabled = !landOk;


    const txtOk = !!(state.txtText && state.txtText.length > 0);
    if(txtOk) setPill('pieuxTxtStatus', `TXT : chargé (${state.refs.size} lignes)`, 'ok');

    refreshPdfButton();
  }


  async function loadPieuxTxtContent(textContent, fileName, fullPath){
    try{
      setPill('pieuxTxtStatus', 'TXT : lecture…', 'warn');
      state.txtText = String(textContent || '');
      state.refs = parseRefsTxt(state.txtText);
      try{
        window.__NF_PROJECT_FILES = window.__NF_PROJECT_FILES || { landxmlPath:'', pieuxTxtPath:'' };
        if(fullPath) window.__NF_PROJECT_FILES.pieuxTxtPath = String(fullPath);
      }catch(_){ }
      setPill('pieuxTxtStatus', `TXT : chargé (${state.refs.size} lignes)`, 'ok');

      if(state.groups && state.groups.size){
        state.groups.forEach(g=>{
          g.hasRef = state.refs.has(g.key);
          if(g.hasRef && !g.excludedStakeout && !g.tooFew && !g.tooMany){
            autoChooseBestSubset(g);
          }
        });
        render();
      }
      refreshAnalyseButton();
      refreshPdfButton();
      maybeAutoAnalyse('txt');
      return true;
    }catch(e){
      console.error(e);
      setPill('pieuxTxtStatus', 'TXT : erreur', 'err');
      return false;
    }
  }
  window.NOVA_loadPieuxTxtContent = loadPieuxTxtContent;

  function refreshPdfButton(){
    const b = document.getElementById("btnPieuxPdf");
    const bPlan = document.getElementById("btnPieuxPdfPlan");
    const bExp = document.getElementById("btnPieuxExportTxt");
    if(!b && !bPlan && !bExp) return;

    const landOk = !!(state.landXmlText && state.landXmlText.length > 20);
    const txtOk = !!(state.refs && state.refs.size > 0);
    const pdfOk = (typeof isPdfSharpAvailable === "function") ? !!isPdfSharpAvailable() : true;

    const hasRows = (()=>{
      try{
        if(!state.groups || !state.groups.size) return false;
        for(const g of state.groups.values()){
          if(g && !g.excludedStakeout && !g.tooFew && !g.tooMany && g.fit && g.hasRef) return true;
        }
        return false;
      }catch(_){ return false; }
    })();

    const ok = (landOk && txtOk && pdfOk && hasRows);
    if(b) b.disabled = !ok;
    if(bPlan) bPlan.disabled = !ok;
    // Export does not require PdfSharp, only data.
    if(bExp) bExp.disabled = !(landOk && txtOk && hasRows);
  }

  function dlTextFile_(fileName, content){
    try{
      let name = String(fileName || 'pieux.txt').replace(/[\\/:*?\"<>|]+/g, '_').trim();
      if(!name.toLowerCase().endsWith('.txt')) name = name.replace(/\.[^.]+$/,'') + '.txt';
      const blob = new Blob([String(content||'')], { type: 'text/plain;charset=utf-8' });

      if(typeof window.saveBlobAs === 'function'){
        return window.saveBlobAs(blob, name, 'text/plain');
      }

      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = name;
      document.body.appendChild(a);
      a.click();
      setTimeout(()=>{ try{ URL.revokeObjectURL(a.href); }catch(_){ } try{ a.remove(); }catch(_){ } }, 0);
    }catch(err){ console.error(err); }
  }


  
  function today_(){
    try{
      const d = new Date();
      const y = d.getFullYear();
      const m = String(d.getMonth()+1).padStart(2,'0');
      const da = String(d.getDate()).padStart(2,'0');
      return `${y}.${m}.${da}`;
    }catch(_){ return ""; }
  }

  function buildPieuxExportName_(){
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
      const date = today_() || "";
      return `NOVA_${vill}_${phase}_PIEUX_OK_${indice}_${date}.txt`;
    }catch(_){
      return `NOVA_PIEUX_OK_${today_()}.txt`;
    }
  }
function fmt3_(v){
    const n = Number(v);
    return Number.isFinite(n) ? n.toFixed(3) : '';
  }

  function exportControlledTxt(){
    try{
      if(!state.groups || !state.groups.size){ return; }
      const rows = [];
      const groups = Array.from(state.groups.values())
        .filter(g => g && g.hasRef && g.fit && !g.excludedStakeout && !g.tooFew && !g.tooMany)
        .sort(naturalPileCompare);

      for(const g of groups){
        const ref = state.refs.get(g.key);
        rows.push([String(ref?.id || g.label || g.key || g.base || ''), fmt3_(g.fit.cx), fmt3_(g.fit.cy), '' ].join('\t'));
      }

      const fn = buildPieuxExportName_();
      dlTextFile_(fn, rows.join('\n'));
    }catch(err){
      console.error(err);
    }
  }

  function updateAnalyseSummaryPill(){
    try{
      const elId = 'pieuxAnalyseStatus';
      if(!state.groups || !state.groups.size){
        setPill(elId, 'Pieux : aucun', null);
        return;
      }
      let total=0, valid=0, excl=0, noRef=0;
      for(const g of state.groups.values()){
        total++;
        if(g.excludedStakeout){ excl++; continue; }
        if(!g.hasRef){ noRef++; continue; }
        if(!g.tooFew && !g.tooMany && g.fit) valid++;
      }
      const txt = `Pieux : ${valid}/${total} OK`;
      const kind = valid>0 ? 'ok' : (total>0 ? 'warn' : null);
      setPill(elId, txt + (excl?` · ${excl} excl`: '') + (noRef?` · ${noRef} sans réf`: ''), kind);
    }catch(_){ }
  }

  function maybeAutoAnalyse(reason){
    try{
      const landOk = !!(state.landXmlText && state.landXmlText.length > 20);
      const txtOk  = !!(state.refs && state.refs.size > 0);
      if(!landOk || !txtOk) return;

      // Auto-run analysis when both inputs are present and nothing has been analysed yet.
      if(!state.groups || !state.groups.size){
        setPill('pieuxAnalyseStatus', 'Pieux : analyse…', 'warn');
        buildAnalysis();
        render();
      }

      updateAnalyseSummaryPill();
      refreshPdfButton();
    }catch(e){
      console.error('[Pieux] autoAnalyse failed', reason, e);
      setPill('pieuxAnalyseStatus', 'Pieux : erreur', 'err');
    }
  }

  function refreshRecolementView(){
    try{
      const ld = window.__NF_LASTDATA || window.lastData || null;
      const raw = ld && ld.rawText ? String(ld.rawText) : state.landXmlText;
      if(raw && raw.length > 20){
        state.landXmlText = (typeof window.nfResolveLandXmlPointDuplicates === 'function')
          ? window.nfResolveLandXmlPointDuplicates(raw, 'Recolement pieux')
          : raw;
      }

      refreshAnalyseButton();
      maybeAutoAnalyse('tab');
      if(state.groups && state.groups.size){
        render();
        updateAnalyseSummaryPill();
      }
      refreshPdfButton();
    }catch(e){
      console.error('[Pieux] refresh recolement failed', e);
      setPill('pieuxAnalyseStatus', 'Pieux : erreur', 'err');
    }
  }
  window.NOVA_refreshRecolementPieux = refreshRecolementView;


  // --------- Bind UI
  function bind(){
    const txtIn = document.getElementById('pieuxTxtInput');
    const txtPickBtn = document.getElementById('btnPieuxTxtPick');
    const analyseBtn = document.getElementById('btnPieuxAnalyse');
    const landIn = document.getElementById('landXmlInput');
    const pdfBtn = document.getElementById('btnPieuxPdf');
    const pdfPlanBtn = document.getElementById('btnPieuxPdfPlan');
    const exportBtn = document.getElementById('btnPieuxExportTxt');

    // Review modal buttons
    const btnClose = document.getElementById('btnPieuxReviewClose');
    const btnApply = document.getElementById('btnPieuxReviewApply');
    const overlay = document.getElementById('pieuxReviewOverlay');

    if(btnClose) btnClose.addEventListener('click', closeReview);
    if(btnApply) btnApply.addEventListener('click', applyReview);
    if(overlay){
      overlay.addEventListener('click', (ev)=>{
        if(ev.target === overlay) closeReview(); // click outside modal
      });
    }

    
    if(txtPickBtn && txtIn){
      txtPickBtn.addEventListener('click', ()=>{ try{ txtIn.click(); }catch(_){} });
    }
if(txtIn){
      txtIn.addEventListener('change', async (ev)=>{
        try{
          const f = ev.target.files?.[0];
          if(!f) return;
          const txt = await readFileText(f);
          try{ txtIn.value = ''; }catch(_){ }
          await loadPieuxTxtContent(txt, f.name || '', '');
        }catch(e){
          console.error(e);
          setPill('pieuxTxtStatus', 'TXT : erreur', 'err');
        }
      });
    }

    // LandXML import is handled elsewhere; we just latch onto it.
    if(landIn){
      landIn.addEventListener('change', ()=>{
        // Let existing parser run first
        setTimeout(()=>{
          try{
            const ld = window.__NF_LASTDATA || window.lastData || null;
            const raw = ld && ld.rawText ? String(ld.rawText) : null;
            state.landXmlText = (typeof window.nfResolveLandXmlPointDuplicates === 'function')
            ? window.nfResolveLandXmlPointDuplicates(raw, 'Recolement pieux')
            : raw;
            refreshAnalyseButton();
            refreshPdfButton();
            maybeAutoAnalyse('landxml');
          }catch(_){ /* ignore */ }
        }, 50);
      });
    }

    if(analyseBtn){
      analyseBtn.addEventListener('click', ()=>{
        try{
          // Ensure we have the latest LandXML raw text
          const ld = window.__NF_LASTDATA || window.lastData || null;
          const raw = ld && ld.rawText ? String(ld.rawText) : state.landXmlText;
          state.landXmlText = (typeof window.nfResolveLandXmlPointDuplicates === 'function')
            ? window.nfResolveLandXmlPointDuplicates(raw, 'Recolement pieux')
            : raw;

          setPill('pieuxAnalyseStatus', 'Pieux : analyse…', 'warn');
          buildAnalysis();
          render();
          updateAnalyseSummaryPill();
          refreshPdfButton();
        }catch(e){
          console.error(e);
          setPill('pieuxAnalyseStatus', 'Pieux : erreur', 'err');
        }
      });
    }

    if(exportBtn){
      exportBtn.addEventListener('click', ()=>{
        exportControlledTxt();
      });
    }


    if(pdfPlanBtn && pdfBtn){
      pdfPlanBtn.addEventListener('click', ()=>{
        // Same generation path as base PDF; just request the extra plan page.
        state.wantPlan = true;
        pdfBtn.click();
      });
    }

    if(pdfBtn){
      pdfBtn.addEventListener('click', ()=>{
        try{
          if(!state.groups || !state.groups.size){ setPill('pieuxAnalyseStatus', 'Pieux : analyse requise', 'warn'); return; }
          if(typeof rowFromPoint !== 'function') throw new Error('rowFromPoint indisponible');

          const rows = [];
          const groups = Array.from(state.groups.values()).sort(naturalPileCompare);

          // Use real station runs (mises en station + résidus) from parsed LandXML
          const ld = window.__NF_LASTDATA || window.lastData || null;
          const runsAll = (ld && Array.isArray(ld.stationLibreRuns)) ? ld.stationLibreRuns : [];

          // Build stationId -> timestamp (ms) index from LandXML CgPoints
          const stationTimes = new Map();
          const stationIds = [];
          for(const r of runsAll){
            const sid = r?.results?.idStation || null;
            if(sid && !stationIds.includes(sid)) stationIds.push(sid);
          }
          try{
            const dom = new DOMParser().parseFromString(state.landXmlText || '', 'text/xml');
            for(const sid of stationIds){
              // exact match on name attribute
              const node = dom.querySelector(`CgPoint[name="${CSS.escape(String(sid))}"]`);
              const tsAttr = node ? (node.getAttribute('timeStamp') || '') : '';
              const ts = Number.isFinite(Date.parse(tsAttr)) ? Date.parse(tsAttr) : null;
              stationTimes.set(sid, ts);
            }
          }catch(_){ /* ignore */ }

          // Stable order: runs are already ordered (m02) by station timestamp when available.
          const stationsOrdered = stationIds.map(id => ({ id, t: stationTimes.get(id) ?? null }));

          function pickStation(tsMs){
            if(!stationsOrdered.length) return 'PIEUX';
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

          const rowsByStation = new Map();

          for(const g of groups){
            if(!g || g.excludedStakeout || g.tooFew || g.tooMany || !g.fit || !g.hasRef) continue;
            const ref = state.refs.get(g.key);
            if(!ref) continue;

            // Attach pieu to station by time (earliest measured point in group)
            let tsMin = null;
            try{
              for(const pt of (g.points||[])){
                const t = pt && pt.tsMs != null ? pt.tsMs : null;
                if(t == null) continue;
                tsMin = (tsMin == null) ? t : Math.min(tsMin, t);
              }
            }catch(_){ }
            const stationId = pickStation(tsMin);

            const dx = g.fit.cx - ref.x;
            const dy = g.fit.cy - ref.y;
            // dA : distance planimétrique entre le théorique (TXT) et le calculé (centre)
            // (affichée dans la colonne "Dz / dA" car le récolement pieux est XY-only).
            const dA = Math.sqrt((dx*dx) + (dy*dy));

            const p = {
              id: ref.id || g.label || g.key || `PI${g.base}`,
              stationId,
              calc: { X: ref.x, Y: ref.y, Z: null },
              mes: { X: g.fit.cx, Y: g.fit.cy, Z: null },
              d: { dx, dy, dz: dA }
            };

            const row = rowFromPoint(p);
            if(!rowsByStation.has(stationId)) rowsByStation.set(stationId, []);
            rowsByStation.get(stationId).push(row);
          }

          if(rowsByStation.size === 0){ setPill('pieuxAnalyseStatus', 'Pieux : aucune ligne exportable', 'warn'); return; }

          // Build implantationByStation with stable station order
          const stationOrder = [];
          for(const s of stationsOrdered){
            if(rowsByStation.has(s.id)) stationOrder.push(s.id);
          }
          // Any remaining stations (should be rare)
          Array.from(rowsByStation.keys()).filter(id=>!stationOrder.includes(id)).sort().forEach(id=>stationOrder.push(id));

          const implantationByStation = [];
          for(const sid of stationOrder){
            const rws = rowsByStation.get(sid) || [];
            if(!rws.length) continue;
            implantationByStation.push({ stationId: sid, rows: rws });
            rows.push(...rws);
          }

          // stationLibreRuns: keep only stations used by pieux (in order), but preserve details
          let stationLibreRuns = [];
          if(runsAll.length){
            const idx = new Map(stationOrder.map((id,i)=>[id,i]));
            stationLibreRuns = runsAll
              .filter(r => idx.has(r?.results?.idStation))
              .sort((a,b)=> (idx.get(a?.results?.idStation)??999999) - (idx.get(b?.results?.idStation)??999999));
          }
          // Ensure every station has an entry (fallback minimal)
          for(const sid of stationOrder){
            if(!stationLibreRuns.some(r=>r?.results?.idStation===sid)){
              stationLibreRuns.push({ results: { idStation: sid, E: null, N: null, H: null }, observations: [], residuals: [] });
            }
          }
	// Tolérances : ce module peut générer un PDF même si l'UI tolérances n'existe pas
	// (ex: Récolement de pieux). On protège donc l'accès.
	// Cartouche : même principe, on récupère au mieux les infos du bloc "Infos dossier".
	const R = (typeof rf === 'function') ? (rf() || {}) : {};
	const O = (typeof getOptions === 'function') ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };
	const tolTxt = (typeof buildTolText === 'function') ? buildTolText() : '';

	// Optional extra page: plan view (A4) with all theoretical TXT points + highlight controlled pieu
	const withPlan = !!state.wantPlan;
	state.wantPlan = false;
	let controlledKey = state.lastControlledKey || null;
	let controlledBase = Number.isFinite(state.lastControlledBase) ? state.lastControlledBase : null;
	if(!controlledKey){
	  try{
	    for(const g of (state.groups?.values?.() || [])){
	      if(g && g.reviewed && g.key){ controlledKey = g.key; controlledBase = Number.isFinite(g.base) ? g.base : null; break; }
	    }
	    if(!controlledKey){
	      for(const g of (state.groups?.values?.() || [])){
	        if(g && !g.excludedStakeout && !g.tooFew && !g.tooMany && g.fit && g.hasRef && g.key){
	          controlledKey = g.key;
	          controlledBase = Number.isFinite(g.base) ? g.base : null;
	          break;
	        }
	      }
	    }
	  }catch(_){ controlledKey = null; controlledBase = null; }
	}
	const planView = withPlan ? {
	  title: 'VUE EN PLAN',
	  controlledKey,
	  controlledBase,
	  avoidRingOverlap: true,
	  emphasizeControlled: false,
	  pointsAll: Array.from(state.refs?.values?.() || []).map(p => ({ id: p.id || p.key || String(p.base), key: p.key, base: p.base, x: p.x, y: p.y })),
	  pointsImplanted: (() => {
	    try{
	      const groups = Array.from(state.groups?.values?.() || []);
	      const implantedKeys = new Set(groups
	        .filter(g => g && g.hasRef && g.fit && !g.excludedStakeout && !g.tooFew && !g.tooMany)
	        .map(g => g.key));
	      return Array.from(state.refs?.values?.() || [])
	        .filter(p => implantedKeys.has(p.key))
	        .map(p => ({ id: p.id || p.key || String(p.base), key: p.key, base: p.base, x: p.x, y: p.y }));
	    }catch(_){ return []; }
	  })()
	} : null;

		// Source unique, identique aux autres PDF : InstrumentDetails du LandXML,
		// puis champs du projet uniquement en secours.
		const instrumentMeta = (typeof window.nfPdfInstrumentMeta === 'function')
		  ? window.nfPdfInstrumentMeta(ld, R)
		  : {
		      model: String(ld?.meta?.instrument || '').trim(),
		      appareil: String(ld?.meta?.instrument || '').trim(),
		      appareilModel: String(ld?.meta?.instrument || '').trim(),
		      serialNumber: String(ld?.meta?.serial || '').trim(),
		      appareilSerial: String(ld?.meta?.serial || '').trim()
		    };

		const okCount = (() => {
		  try{
		    const groups = Array.from(state.groups?.values?.() || []);
		    return groups.filter(g => g && g.hasRef && g.fit && !g.excludedStakeout && !g.tooFew && !g.tooMany).length;
		  }catch(_){ return 0; }
		})();

		// Inject station meta so the PdfSharp renderer can draw the same "Appareil / Type de station"
		// blocks as Implantation, BUT we explicitly suppress all observation tables.
		// Note: we DO NOT pass topoStations in this report (it would trigger the LandXML "LEVÉ" mode).
		const ldMeta = (window.__NF_LASTDATA || window.lastData || (typeof lastData !== 'undefined' ? lastData : null) || null);
		const stationLibreRunsMeta = (ldMeta && Array.isArray(ldMeta.stationLibreRuns)) ? ldMeta.stationLibreRuns : [];
		const stationLibreMeta = (ldMeta && ldMeta.stationLibre && typeof ldMeta.stationLibre === 'object') ? ldMeta.stationLibre : null;

		const payload = {
            type: 'pdfsharp_implantation',
            // IMPORTANT : ce PDF réutilise le renderer "Implantation" validé.
            // On renomme explicitement le bloc "Implantation" -> "Récolement".
	            // UX : le mot "Pieux" doit être dans le titre. Le sous-titre ne sert qu'à indiquer le nombre contrôlé.
	            title: 'RÉCOLEMENT PIEUX',
	            // No subtitle under the blue bar: table must start immediately.
	            subTitle: '',
            planView: planView,
		    // Station blocks (best effort) — observations are suppressed by flag below.
		    stationLibreRuns: stationLibreRunsMeta,
		    stationLibre: stationLibreMeta,
		    // Make sure the renderer stays in "Implantation table" mode (no topoStations here)
		    topoStations: [],
		    suppressObsTables: true,
            header: ['ID point','X théo','Y théo','Z théo','X mes','Y mes','Z mes','Dx / dL','Dy / dT','Dz / dA','STATUT'],
            rows,
            fileName: (typeof buildExportFileName === 'function')
              ? buildExportFileName(withPlan ? 'RECOLEMENT_PIEUX_PLAN' : 'RECOLEMENT_PIEUX', 'PDF')
              : 'NOVA_Recolement_Pieux.pdf',

	            // Cartouche / meta (best effort, same keys as implantation PdfSharp)
            elements: (R && R.elements) ? R.elements : '',
            entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : '',
            contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : '',
            systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : '',
            ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : '',
            intervenant: (typeof getAutoIntervenant === 'function') ? getAutoIntervenant(R) : '',
            systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : '',
            planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : '',
            date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : '',
	            ...instrumentMeta,
            ville: (R && R.ville) ? R.ville : '',
            adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : '',
            cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : '',

            validation: {
              statutOn: !!O.tolOn,
              tolXYOn: !!O.xyOn,
              tolZOn: false,
              tolXY: isFinite(O.tXY) ? O.tXY : null,
              tolZ: null,
              observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ''
            },

            obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : '',
            observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : '',
            Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : '',
            surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : '',
            geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : '',
            Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : '',
            utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : '',
            Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : '',
            Intervenant: (typeof getAutoIntervenant === 'function') ? getAutoIntervenant(R) : '',

            signatureDataUrl: (typeof sigDataUrl !== 'undefined' && sigDataUrl) ? String(sigDataUrl) : '',
            signatureImageType: (typeof sigImageType !== 'undefined' && sigImageType) ? String(sigImageType) : 'JPEG',
            // Per-station rows for the implantation-style renderer
            implantationByStation
          };

          if(window.chrome?.webview?.postMessage) window.chrome.webview.postMessage(payload);
          else throw new Error('WebView2 host indisponible (postMessage).');

        }catch(e){
          console.error(e);
          setPill('pieuxAnalyseStatus', 'PDF : erreur', 'err');
          try{ if(window.chrome?.webview?.postMessage) window.chrome.webview.postMessage({type:'ui_error', message:String(e)}); }catch(_){ }
        }
      });
    }

    // Initial latch (if LandXML already loaded)
    try{
      const ld = window.__NF_LASTDATA || window.lastData || null;
      if(ld && ld.rawText){
        state.landXmlText = String(ld.rawText);
      }
    }catch(_){ }
    refreshAnalyseButton();
    refreshPdfButton();
    maybeAutoAnalyse('init');
  }

  document.addEventListener('DOMContentLoaded', bind);
})();

