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

