/*
===============================================================================
Nova-Fiches — PDF Post-Processing — Thick Vertical Lines (LOCKED ENGINE)

Goal
- Draw ONLY the required thick vertical separators for:
    1) IMPLANTATION results table
    2) MESURE SUR LIGNE results table

Contract (stable column indices)
  0  = ID point
  3  = Z théo / calc
  6  = Z mes
  9  = Dz
  10 = Statut

Separators to draw (vertical)
  - Left border of table (left of ID point)
  - Right of ID point
  - Between Z théo/calc and X mes  (end of col 3)
  - Between Z mes and Dx          (end of col 6)
  - Between Dz and Statut         (end of col 9)
  - Right border of Statut        (end of col 10)

Hard rules
- ❌ No drawing inside autoTable hooks.
- ✅ Measure ONLY via cell geometry (cell.x/y/width/height).
- ✅ Post-processing draw at the very end.
- ✅ Deterministic, isolated per table instance (no bleed between blocks).
===============================================================================
*/

(function(){
  const THICK_W = 0.6; // mm — visible but not "marker"-thick

  const REQUIRED_END_COLS = [0,3,6,9,10];

  function isNum(v){ return typeof v === 'number' && isFinite(v); }
  function round3(v){ return Math.round(v*1000)/1000; }

  function ensureState(doc){
    if(!doc) return null;
    if(!doc.__nfTL) doc.__nfTL = { seq: 0, segments: {} };
    return doc.__nfTL;
  }

  // Transactional gating: only the code that explicitly calls begin/end can be measured.
  function begin(doc, spec){
    if(!doc) return;
    const st = ensureState(doc);
    st.seq++;
    doc.__nfTL_active = {
      id: st.seq,
      spec: String(spec||'').toLowerCase()
    };
  }

  function end(doc){
    if(!doc) return;
    doc.__nfTL_active = null;
  }

  function _pageNo(doc, cellData){
    return (cellData && cellData.pageNumber)
      || (doc && doc.internal && doc.internal.getCurrentPageInfo && doc.internal.getCurrentPageInfo().pageNumber)
      || 1;
  }

  function _segKey(activeId, pageNo){
    return String(activeId) + ':' + String(pageNo);
  }

  function measureCell(cellData){
    try{
      if(!cellData || !cellData.doc || !cellData.table || !cellData.cell) return;
      const doc = cellData.doc;
      const active = doc.__nfTL_active;
      if(!active) return;

      // Only two specs are permitted.
      if(active.spec !== 'implantation' && active.spec !== 'ligne') return;

      const table = cellData.table;
      const cols = Array.isArray(table.columns) ? table.columns : [];
      if(cols.length < 11) return; // hard filter: only results tables

      const colIdx = cols.indexOf(cellData.column);
      if(colIdx < 0) return;

      const st = ensureState(doc);
      const pageNo = _pageNo(doc, cellData);
      const key = _segKey(active.id, pageNo);
      if(!st.segments[key]){
        st.segments[key] = {
          spec: active.spec,
          pageNumber: pageNo,
          yMin: null,
          yMax: null,
          xLeft: null,
          xEnds: {} // endX by column index
        };
      }
      const seg = st.segments[key];

      // Y bounds (real rendered cell bounds)
      const cy = cellData.cell.y;
      const ch = cellData.cell.height;
      if(isNum(cy) && isNum(ch)){
        const y1 = cy;
        const y2 = cy + ch;
        seg.yMin = isNum(seg.yMin) ? Math.min(seg.yMin, y1) : y1;
        seg.yMax = isNum(seg.yMax) ? Math.max(seg.yMax, y2) : y2;
      }

      // X bounds
      const cx = cellData.cell.x;
      const cw = cellData.cell.width;
      if(isNum(cx) && isNum(cw)){
        if(colIdx === 0){
          seg.xLeft = isNum(seg.xLeft) ? Math.min(seg.xLeft, cx) : cx;
        }
        if(REQUIRED_END_COLS.indexOf(colIdx) >= 0){
          const endX = cx + cw;
          const prev = seg.xEnds[colIdx];
          seg.xEnds[colIdx] = isNum(prev) ? Math.max(prev, endX) : endX;
        }
      }
    }catch(e){
      // Never block export
    }
  }

  function _hasAllXs(seg){
    if(!seg || !isNum(seg.xLeft)) return false;
    for(const c of REQUIRED_END_COLS){
      if(!isNum(seg.xEnds[c])) return false;
    }
    return true;
  }

  function draw(doc, thickW){
    try{
      if(!doc || !doc.__nfTL || !doc.__nfTL.segments) return;
      const st = doc.__nfTL;
      const w = isNum(thickW) ? thickW : THICK_W;

      // Save nothing, but always reset to known good values.
      doc.setDrawColor(0,0,0);
      doc.setLineWidth(w);

      const keys = Object.keys(st.segments);
      for(const k of keys){
        const seg = st.segments[k];
        if(!seg) continue;
        if(seg.spec !== 'implantation' && seg.spec !== 'ligne') continue;
        if(!isNum(seg.yMin) || !isNum(seg.yMax) || seg.yMax <= seg.yMin) continue;
        if(!_hasAllXs(seg)) continue; // all-or-nothing to avoid parasitic lines

        if(typeof doc.setPage === 'function') doc.setPage(seg.pageNumber);

        const y1 = round3(seg.yMin);
        const y2 = round3(seg.yMax);

        const xs = [
          seg.xLeft,
          seg.xEnds[0],
          seg.xEnds[3],
          seg.xEnds[6],
          seg.xEnds[9],
          seg.xEnds[10]
        ].map(round3);

        // Draw exactly 6 vertical lines, nothing else.
        for(const x of xs){
          if(!isNum(x)) continue;
          doc.line(x, y1, x, y2);
        }
      }
    }catch(e){
      // Never block export
    }
  }

  // Public API
  window.NF_THICKLINES = {
    begin,
    end,
    measureCell,
    draw
  };

  // Compatibility helpers used by PDF generators
  window.nfPostBeginPdf = function(doc){
    try{
      // reset state for each new PDF
      if(doc) doc.__nfTL = { seq: 0, segments: {} };
    }catch(_){ }
  };

  window.nfPostDrawThickLines = function(doc){
    try{ draw(doc); }catch(_){ }
  };
})();
