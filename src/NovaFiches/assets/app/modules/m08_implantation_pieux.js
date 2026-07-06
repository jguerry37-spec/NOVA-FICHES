/*
===============================================================================
Nova-Fiches - Implantation de pieux
- Le rapport et les écarts viennent exclusivement de l'implantation LandXML.
- Le TXT théorique est utilisé exclusivement pour la page de plan.
- Tous les points TXT sont dessinés ; seuls les points inclus au rapport sont
  entourés et étiquetés.
===============================================================================
*/

(function(){
  'use strict';

  const state = {
    refs: []
  };

  function setPill(id, text, kind){
    const el = document.getElementById(id);
    if(!el) return;
    el.textContent = text;
    el.classList.remove('ok', 'err', 'warn', 'nf-hidden');
    if(kind) el.classList.add(kind);
  }

  function parseNumber(value){
    const n = Number.parseFloat(String(value ?? '').trim().replace(',', '.'));
    return Number.isFinite(n) ? n : null;
  }

  function pointKey(raw){
    let value = String(raw ?? '').trim();
    const at = value.indexOf('@');
    if(at >= 0) value = value.slice(0, at);
    value = value.replace(',', '.').replace(/\s+/g, '').toUpperCase();
    if(!value) return '';

    // Leica ajoute parfois un I au matricule implanté (IPI204 -> PI204).
    if(/^IPI(?=\d)/.test(value)) value = value.slice(1);
    if(/^IP(?=\d)/.test(value)) value = value.slice(1);

    let match = value.match(/^(?:PIEU|PI|P)[._-]?0*(\d+)$/);
    if(match) return String(Number.parseInt(match[1], 10));

    match = value.match(/^0*(\d+)$/);
    if(match) return String(Number.parseInt(match[1], 10));

    match = value.match(/^([EZ]\d+(?:\.\d+)?-P)0*(\d+)(\.BIS)?$/);
    if(match) return `${match[1]}${Number.parseInt(match[2], 10)}${match[3] || ''}`;

    match = value.match(/^(G\d+-P)0*(\d+)(\.BIS)?$/);
    if(match) return `${match[1]}${Number.parseInt(match[2], 10)}${match[3] || ''}`;

    match = value.match(/^(P[EG])0*(\d+)$/);
    if(match) return `${match[1]}${Number.parseInt(match[2], 10)}`;

    return value;
  }

  function parseTxt(text){
    const refs = [];
    for(const rawLine of String(text || '').split(/\r?\n/)){
      const line = rawLine.trim();
      if(!line) continue;

      let parts = line.split('\t').map(v=>v.trim());
      if(parts.length < 3) parts = line.split(/[; ]+/).map(v=>v.trim()).filter(Boolean);
      if(parts.length < 3) continue;

      const x = parseNumber(parts[1]);
      const y = parseNumber(parts[2]);
      if(x == null || y == null) continue;

      const id = parts[0] || '';
      const key = pointKey(id);
      if(!key) continue;
      refs.push({
        id,
        key,
        x,
        y,
        z: parts.length > 3 ? parseNumber(parts[3]) : null
      });
    }
    return refs;
  }

  function currentImplantationPoints(){
    try{
      const data = window.__NF_LASTDATA || window.lastData || null;
      if(!data) return [];
      const raw = (typeof collectAllImplantPoints === 'function')
        ? collectAllImplantPoints(data)
        : (Array.isArray(data?.implantation?.points) ? data.implantation.points : []);
      const fixed = (typeof nfFixMissingImplantStationIds_ === 'function')
        ? nfFixMissingImplantStationIds_(raw, data)
        : raw;
      const excluded = (typeof nfExcludedForImplantLr_ === 'function')
        ? nfExcludedForImplantLr_()
        : new Set();
      return (typeof nfFilterPointsById_ === 'function')
        ? nfFilterPointsById_(fixed, excluded)
        : fixed;
    }catch(_){
      return [];
    }
  }

  function pointId(point){
    return String(point?.id ?? point?.Id ?? point?.ID ?? point?.name ?? point?.pntRef ?? '').trim();
  }

  function buildPlan(){
    const included = currentImplantationPoints();
    const includedKeys = new Set(included.map(p=>pointKey(pointId(p))).filter(Boolean));
    const pointsAll = state.refs.map(p=>({
      id: p.id,
      key: p.key,
      x: p.x,
      y: p.y
    }));
    const pointsImplanted = pointsAll.filter(p=>includedKeys.has(p.key));

    return {
      planView: {
        title: 'VUE EN PLAN - IMPLANTATION PIEUX',
        pointsAll,
        pointsImplanted,
        showNorthArrow: true,
        rotateLongAxisVertical: true
      },
      reportCount: included.length,
      matchedCount: pointsImplanted.length
    };
  }

  function refresh(){
    const reportCount = currentImplantationPoints().length;
    const hasLandXml = reportCount > 0;
    const hasTxt = state.refs.length > 0;
    const hasPdfEngine = (typeof isPdfSharpAvailable === 'function') ? isPdfSharpAvailable() : true;
    const button = document.getElementById('btnPieuxImplPdfPlan');
    if(button) button.disabled = !(hasLandXml && hasTxt && hasPdfEngine);

    const info = document.getElementById('pieuxImplMatchStatus');
    if(info){
      if(!hasLandXml) info.textContent = 'Aucun point d’implantation inclus dans le LandXML.';
      else if(!hasTxt) info.textContent = `${reportCount} point(s) d’implantation inclus. Chargez le TXT pour créer le plan.`;
      else {
        const plan = buildPlan();
        info.textContent = `${reportCount} point(s) dans le rapport, ${plan.matchedCount} retrouvé(s) dans le TXT graphique.`;
      }
    }
  }

  function setMode(mode){
    const implantation = mode !== 'recolement';
    window.__NF_PIEUX_MODE = implantation ? 'implantation' : 'recolement';

    const implPanel = document.getElementById('pieuxImplantationPanel');
    const recoPanel = document.getElementById('pieuxRecolementPanel');
    const implButton = document.getElementById('btnPieuxModeImplantation');
    const recoButton = document.getElementById('btnPieuxModeRecolement');

    implPanel?.classList.toggle('nf-hidden', !implantation);
    recoPanel?.classList.toggle('nf-hidden', implantation);
    implButton?.classList.toggle('active', implantation);
    recoButton?.classList.toggle('active', !implantation);
    implButton?.setAttribute('aria-selected', implantation ? 'true' : 'false');
    recoButton?.setAttribute('aria-selected', implantation ? 'false' : 'true');

    if(typeof window.NOVA_placeVisualisation === 'function'){
      window.NOVA_placeVisualisation('module-recolement');
    }
    if(!implantation && typeof window.NOVA_refreshRecolementPieux === 'function'){
      window.NOVA_refreshRecolementPieux();
    }
    refresh();
  }

  async function loadTxt(file){
    try{
      const text = await file.text();
      state.refs = parseTxt(text);
      if(!state.refs.length) throw new Error('Aucun point XY valide');
      setPill('pieuxImplTxtStatus', `TXT plan : chargé (${state.refs.length} points)`, 'ok');
    }catch(error){
      console.error('[Pieux implantation] TXT', error);
      state.refs = [];
      setPill('pieuxImplTxtStatus', 'TXT plan : erreur', 'err');
    }
    refresh();
  }

  function generatePdf(){
    const genericButton = document.getElementById('btnPdfInterventionPdfSharp');
    const built = buildPlan();
    const hasPdfEngine = (typeof isPdfSharpAvailable === 'function') ? isPdfSharpAvailable() : true;
    if(!genericButton || !hasPdfEngine || !built.planView.pointsAll.length){
      setPill('pieuxImplTxtStatus', 'PDF : données incomplètes', 'warn');
      return;
    }

    window.__NF_IMPLANTATION_PDF_OVERRIDE = {
      title: 'IMPLANTATION PIEUX',
      subTitle: '',
      fileNameType: 'IMPLANTATION_PIEUX_PLAN',
      planView: built.planView
    };
    genericButton.dispatchEvent(new MouseEvent('click', { bubbles:true, cancelable:true }));
  }

  function bind(){
    window.__NF_PIEUX_MODE = 'implantation';
    document.getElementById('btnPieuxModeImplantation')?.addEventListener('click', ()=>setMode('implantation'));
    document.getElementById('btnPieuxModeRecolement')?.addEventListener('click', ()=>setMode('recolement'));

    const input = document.getElementById('pieuxImplTxtInput');
    document.getElementById('btnPieuxImplTxtPick')?.addEventListener('click', ()=>input?.click());
    input?.addEventListener('change', async event=>{
      const file = event.target.files?.[0];
      if(file) await loadTxt(file);
      try{ input.value = ''; }catch(_){ }
    });
    document.getElementById('btnPieuxImplPdfPlan')?.addEventListener('click', generatePdf);
    document.getElementById('landXmlInput')?.addEventListener('change', ()=>setTimeout(refresh, 150));
    document.addEventListener('change', event=>{
      if(event.target?.closest?.('#view_implant')) setTimeout(refresh, 0);
    });

    setMode('implantation');
  }

  document.addEventListener('DOMContentLoaded', bind);
})();
