(function(){
  const MODULES = [
    { id:'module-implantation', label:'Implantation / ligne de ref' },
    { id:'module-suivi', label:'Station / leve topo' },
    { id:'module-recolement', label:'Pieux' },
    { id:'module-recolement-mnt', label:'Recolement MNT' },
    { id:'module-reportage-photo', label:'Reportage photo' }
  ];

  const STORE_KEY = '__NF_PHOTOS';
  const MAX_SIDE = 1600;
  const JPEG_QUALITY = 0.74;
  let currentModule = 'module-implantation';
  let selectedId = null;
  let tool = 'arrow';
  let drawColor = '#f97316';
  let drawWidth = 4;
  let textSize = 44;
  let drawing = false;
  let start = null;
  let previewShape = null;

  window[STORE_KEY] = window[STORE_KEY] || {};

  function qs(id){ return document.getElementById(id); }
  function esc(s){
    return String(s || '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }
  function activeModuleId(){
    const active = document.querySelector('.nf-module.active');
    return active?.id || 'module-implantation';
  }
  function arr(moduleId){
    const key = moduleId || currentModule;
    window[STORE_KEY][key] = Array.isArray(window[STORE_KEY][key]) ? window[STORE_KEY][key] : [];
    return window[STORE_KEY][key];
  }
  function allPhotosFor(moduleId){
    return arr(moduleId).filter(p => p && p.dataUrl);
  }
  function moduleLabel(moduleId){
    return (MODULES.find(m => m.id === moduleId)?.label || moduleId || 'Photos');
  }
  function getReportPhotosPerPage(){
    const v = Number(qs('photoReportPerPage')?.value || 4);
    return [1,2,3,4].includes(v) ? v : 4;
  }
  function setReportPhotosPerPage(v){
    const n = Number(v || 4);
    const el = qs('photoReportPerPage');
    if(el) el.value = String([1,2,3,4].includes(n) ? n : 4);
  }
  function uuid(){
    try{ return crypto.randomUUID(); }catch(_){ return 'ph_' + Date.now() + '_' + Math.random().toString(16).slice(2); }
  }

  function ensureUi(){
    if(qs('nfPhotoModal')) return;
    const css = document.createElement('style');
    css.textContent = `
      .nf-photo-btn{ margin-left:8px; }
      .nf-photo-count{ display:inline-flex; align-items:center; justify-content:center; min-width:20px; height:20px; border-radius:999px; background:#e8f1ff; color:#0b1020; font-size:11px; font-weight:850; margin-left:6px; }
      .nf-photo-modal{ position:fixed; inset:0; background:rgba(0,0,0,.38); z-index:10020; display:none; align-items:center; justify-content:center; padding:18px; }
      .nf-photo-dialog{ width:min(1180px, 100%); height:min(760px, 96vh); background:#fff; color:#0b1020; border:1px solid rgba(0,0,0,.18); border-radius:14px; box-shadow:0 22px 70px rgba(0,0,0,.28); display:grid; grid-template-columns:280px 1fr; overflow:hidden; }
      .nf-photo-side{ border-right:1px solid rgba(0,0,0,.10); padding:14px; overflow:auto; background:#f7f9fc; }
      .nf-photo-main{ padding:14px; overflow:auto; display:flex; flex-direction:column; gap:10px; }
      .nf-photo-title{ font-weight:900; font-size:14px; text-transform:uppercase; letter-spacing:.2px; line-height:1.15; }
      .nf-photo-actions{ display:grid; grid-template-columns:1fr 1fr; gap:8px; margin-top:10px; }
      .nf-photo-actions .btn{ width:100%; min-width:0; padding-left:8px; padding-right:8px; }
      .nf-photo-list{ display:flex; flex-direction:column; gap:8px; margin-top:12px; }
      .nf-photo-item{ display:grid; grid-template-columns:58px 1fr; gap:8px; align-items:center; padding:7px; border:1px solid rgba(0,0,0,.10); background:#fff; border-radius:9px; cursor:pointer; }
      .nf-photo-item.active{ border-color:#1267f3; background:#edf4ff; }
      .nf-photo-item img{ width:58px; height:44px; object-fit:cover; border-radius:6px; background:#e5e7eb; }
      .nf-photo-canvas-wrap{ width:100%; min-height:360px; display:flex; align-items:center; justify-content:center; background:#eef3f8; border:1px solid rgba(0,0,0,.12); border-radius:10px; padding:10px; box-sizing:border-box; }
      #nfPhotoCanvas{ max-width:100%; max-height:480px; background:#fff; box-shadow:0 1px 12px rgba(0,0,0,.12); cursor:crosshair; }
      .nf-photo-tools{ display:flex; gap:8px; flex-wrap:wrap; align-items:center; }
      .nf-photo-tools button.active{ background:#0b1020; }
      .nf-photo-swatches{ display:flex; gap:5px; align-items:center; }
      .nf-photo-swatch{ width:26px; height:26px; border-radius:999px; border:2px solid #fff; box-shadow:0 0 0 1px rgba(0,0,0,.25); cursor:pointer; padding:0; min-width:0; }
      .nf-photo-swatch.active{ box-shadow:0 0 0 3px #1267f3; }
      .nf-photo-weight{ width:92px; }
      .nf-photo-text-input{ width:210px; min-width:180px; }
      .nf-photo-text-size{ width:92px; }
      .nf-photo-caption{ width:100%; min-height:56px; resize:vertical; box-sizing:border-box; }
    `;
    document.head.appendChild(css);

    const modal = document.createElement('div');
    modal.id = 'nfPhotoModal';
    modal.className = 'nf-photo-modal';
    modal.innerHTML = `
      <div class="nf-photo-dialog">
        <div class="nf-photo-side">
          <div class="nf-photo-title">
            <span id="nfPhotoTitle">Photos</span>
          </div>
          <div class="nf-photo-actions">
            <button id="nfPhotoValidate" class="btn" type="button">Valider</button>
            <button id="nfPhotoClose" class="btn" type="button">Fermer</button>
          </div>
          <div style="margin-top:12px;">
            <label class="btn" for="nfPhotoInput">Ajouter photos</label>
            <input id="nfPhotoInput" type="file" accept="image/*,.jpg,.jpeg,.png,.webp,.bmp" multiple />
          </div>
          <div class="small" style="margin-top:10px;">Les images sont redimensionnees et compressees automatiquement pour garder les PDF legers.</div>
          <div id="nfPhotoList" class="nf-photo-list"></div>
        </div>
        <div class="nf-photo-main">
          <div class="nf-photo-tools">
            <button id="nfToolArrow" class="btn active" type="button">Fleche</button>
            <button id="nfToolLine" class="btn" type="button">Trait</button>
            <button id="nfToolCircle" class="btn" type="button">Rond</button>
            <button id="nfToolRect" class="btn" type="button">Carre</button>
            <button id="nfToolPoint" class="btn" type="button">Point</button>
            <button id="nfToolText" class="btn" type="button">Texte</button>
            <div class="nf-photo-swatches" title="Couleur du dessin">
              <button class="nf-photo-swatch active" type="button" data-color="#f97316" style="background:#f97316"></button>
              <button class="nf-photo-swatch" type="button" data-color="#ef4444" style="background:#ef4444"></button>
              <button class="nf-photo-swatch" type="button" data-color="#facc15" style="background:#facc15"></button>
              <button class="nf-photo-swatch" type="button" data-color="#22c55e" style="background:#22c55e"></button>
              <button class="nf-photo-swatch" type="button" data-color="#2563eb" style="background:#2563eb"></button>
              <button class="nf-photo-swatch" type="button" data-color="#111827" style="background:#111827"></button>
              <button class="nf-photo-swatch" type="button" data-color="#ffffff" style="background:#ffffff"></button>
            </div>
            <select id="nfDrawWidth" class="box nf-photo-weight" title="Epaisseur du trait">
              <option value="3">Fin</option>
              <option value="5" selected>Moyen</option>
              <option value="8">Epais</option>
              <option value="12">Tres epais</option>
            </select>
            <input id="nfDrawText" class="box nf-photo-text-input" type="text" placeholder="Texte a poser" title="Texte a poser sur la photo" />
            <select id="nfTextSize" class="box nf-photo-text-size" title="Taille du texte">
              <option value="28">Petit</option>
              <option value="44" selected>Moyen</option>
              <option value="64">Grand</option>
              <option value="86">Tres grand</option>
            </select>
            <button id="nfUndoShape" class="btn" type="button">Annuler dessin</button>
            <button id="nfClearShapes" class="btn" type="button">Effacer</button>
            <button id="nfDeletePhoto" class="btn" type="button">Supprimer photo</button>
          </div>
          <div class="nf-photo-canvas-wrap"><canvas id="nfPhotoCanvas" width="960" height="720"></canvas></div>
          <div id="nfPhotoPointLinkRow" class="nf-hidden" style="display:flex; align-items:center; gap:8px;">
            <label for="nfPhotoPointLink" class="small" style="white-space:nowrap;">Point lie</label>
            <select id="nfPhotoPointLink" class="box" style="max-width:280px;"></select>
          </div>
          <textarea id="nfPhotoCaption" class="box nf-photo-caption" placeholder="Legende de la photo"></textarea>
        </div>
      </div>`;
    document.body.appendChild(modal);

    qs('nfPhotoClose')?.addEventListener('click', close);
    qs('nfPhotoValidate')?.addEventListener('click', validateAndClose);
    modal.addEventListener('click', e => { if(e.target === modal) close(); });
    qs('nfPhotoInput')?.addEventListener('change', onFiles);
    qs('nfPhotoCaption')?.addEventListener('input', e => {
      const p = getSelected();
      if(p){ p.caption = e.target.value || ''; renderList(); updateButtons(); }
    });
    qs('nfPhotoPointLink')?.addEventListener('change', e => {
      const p = getSelected();
      if(p){ p.linkedPointId = e.target.value || null; }
    });
    [['nfToolArrow','arrow'],['nfToolLine','line'],['nfToolCircle','circle'],['nfToolRect','rect'],['nfToolPoint','point'],['nfToolText','text']].forEach(([id,t])=>{
      qs(id)?.addEventListener('click', ()=>setTool(t));
    });
    document.querySelectorAll('.nf-photo-swatch').forEach(btn=>{
      btn.addEventListener('click', ()=>{
        drawColor = btn.getAttribute('data-color') || '#f97316';
        document.querySelectorAll('.nf-photo-swatch').forEach(b=>b.classList.toggle('active', b === btn));
      });
    });
    qs('nfDrawWidth')?.addEventListener('change', e=>{
      const v = Number(e.target.value);
      drawWidth = Number.isFinite(v) ? v : 5;
    });
    qs('nfTextSize')?.addEventListener('change', e=>{
      const v = Number(e.target.value);
      textSize = Number.isFinite(v) ? v : 44;
    });
    qs('nfUndoShape')?.addEventListener('click', ()=>{
      const p = getSelected();
      if(p && Array.isArray(p.shapes) && p.shapes.length){ p.shapes.pop(); drawSelected(); }
    });
    qs('nfClearShapes')?.addEventListener('click', ()=>{
      const p = getSelected();
      if(p){
        p.shapes = [];
        p.renderedDataUrl = p.dataUrl;
        drawSelected();
      }
    });
    qs('nfDeletePhoto')?.addEventListener('click', ()=>{
      const list = arr(currentModule);
      const idx = list.findIndex(p => p.id === selectedId);
      if(idx >= 0) list.splice(idx, 1);
      selectedId = list[0]?.id || null;
      renderAll();
    });

    const canvas = qs('nfPhotoCanvas');
    canvas?.addEventListener('mousedown', beginDraw);
    canvas?.addEventListener('mousemove', moveDraw);
    canvas?.addEventListener('mouseup', endDraw);
    canvas?.addEventListener('mouseleave', endDraw);

    qs('btnPhotoReportOpen')?.addEventListener('click', ()=>open('module-reportage-photo'));
    qs('btnPhotoReportPdf')?.addEventListener('click', generatePhotoReportPdf);
    qs('photoReportPerPage')?.addEventListener('change', ()=>updateButtons());
  }

  function injectButtons(){
    ensureUi();
    MODULES.forEach(m => {
      const mod = qs(m.id);
      if(!mod || mod.querySelector('.nf-photo-btn')) return;
      const h2 = mod.querySelector('.card h2, h2');
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'btn nf-photo-btn';
      btn.innerHTML = 'Photos <span class="nf-photo-count" data-photo-count="'+esc(m.id)+'">0</span>';
      btn.addEventListener('click', e => { e.preventDefault(); open(m.id); });
      const actionRow = mod.querySelector('.card .row, .row');
      if(actionRow){
        actionRow.appendChild(btn);
      }else if(h2 && h2.parentElement){
        h2.parentElement.insertBefore(btn, h2.nextSibling);
      }else{
        mod.insertBefore(btn, mod.firstChild);
      }
    });
    updateButtons();
  }

  function open(moduleId){
    currentModule = moduleId || activeModuleId();
    ensureUi();
    qs('nfPhotoTitle').textContent = 'Photos - ' + moduleLabel(currentModule);
    selectedId = arr(currentModule)[0]?.id || null;
    renderPointLinkSelect();
    renderAll();
    qs('nfPhotoModal').style.display = 'flex';
  }

  // Points du rapport disponibles pour lier une photo, selon le module photo courant, ainsi
  // que leurs coordonnees rectangulaires (X/Y/Z) quand elles existent. Implantation/ligne de
  // ref et Station-leve/transfert alti sont les seuls contextes concernes (portee confirmee
  // avec l'utilisateur) ; les autres modules renvoient des groupes vides.
  // Un point peut n'avoir que X/Y, que Z, les trois, ou aucune coordonnee exploitable : chaque
  // composante est prise independamment (jamais d'exigence "tout ou rien").
  function moduleReportPoints(moduleId){
    const data = (typeof lastData !== 'undefined' && lastData) ? lastData : (window.lastData || null);
    const groups = [];
    const coordsById = new Map();
    if(!data) return { groups, coordsById };
    const uniq = arr => [...new Set(arr.filter(Boolean))];
    const setCoord = (id, x, y, z) => {
      if(!id || coordsById.has(id)) return;
      const c = {
        x: Number.isFinite(x) ? x : null,
        y: Number.isFinite(y) ? y : null,
        z: Number.isFinite(z) ? z : null
      };
      if(c.x != null || c.y != null || c.z != null) coordsById.set(id, c);
    };
    if(moduleId === 'module-implantation'){
      try{
        const imp = (typeof collectAllImplantPoints === 'function') ? collectAllImplantPoints(data) : [];
        const ids = uniq(imp.map(p => String(p?.id || '').trim()));
        if(ids.length) groups.push({ label:'Implantation', ids });
        imp.forEach(p => setCoord(String(p?.id || '').trim(), p?.mes?.E, p?.mes?.N, p?.mes?.H));
      }catch(_){ }
      try{
        const ex = (typeof nfExcludedSet_ === 'function') ? nfExcludedSet_() : new Set();
        const lr = (typeof nfFilterLigneRef_ === 'function') ? nfFilterLigneRef_(data.ligneRef, ex) : [];
        const ids = [];
        lr.forEach(line => (line?.rabPoints || []).forEach(p => {
          const id = String(p?.id || '').trim();
          ids.push(id);
          setCoord(id, p?.mes?.E, p?.mes?.N, p?.mes?.H);
        }));
        const u = uniq(ids);
        if(u.length) groups.push({ label:'Ligne de reference', ids:u });
      }catch(_){ }
      return { groups, coordsById };
    }
    if(moduleId === 'module-suivi'){
      try{
        const stations = (typeof nfFilterTopoStationsForLeve_ === 'function') ? nfFilterTopoStationsForLeve_(data) : [];
        const ids = [];
        stations.forEach(st => {
          (st?.observations || []).forEach(o => ids.push(String(o?.id || '').trim()));
          (st?.results || []).forEach(r => {
            const id = String(r?.id || '').trim();
            ids.push(id);
            setCoord(id, r?.E, r?.N, r?.H);
          });
        });
        const u = uniq(ids);
        if(u.length) groups.push({ label:'Leve', ids:u });
      }catch(_){ }
      try{
        const ids = [];
        (data.heightTransfers || []).forEach(ht => {
          (ht?.references || []).forEach(r => {
            const id = String(r?.id || '').trim();
            ids.push(id);
            setCoord(id, r?.point?.E, r?.point?.N, r?.point?.H);
          });
          (ht?.measuredPoints || []).forEach(m => {
            const id = String(m?.id || '').trim();
            ids.push(id);
            setCoord(id, m?.point?.E, m?.point?.N, m?.point?.H);
          });
        });
        const u = uniq(ids);
        if(u.length) groups.push({ label:'Transfert alti', ids:u });
      }catch(_){ }
      return { groups, coordsById };
    }
    return { groups, coordsById };
  }

  function renderPointLinkSelect(){
    const row = qs('nfPhotoPointLinkRow');
    const sel = qs('nfPhotoPointLink');
    if(!row || !sel) return;
    const groups = moduleReportPoints(currentModule).groups;
    if(!groups.length){
      row.classList.add('nf-hidden');
      sel.innerHTML = '';
      return;
    }
    row.classList.remove('nf-hidden');
    sel.innerHTML = '<option value="">Aucun point lie</option>' + groups.map(g =>
      `<optgroup label="${esc(g.label)}">` + g.ids.map(id => `<option value="${esc(id)}">${esc(id)}</option>`).join('') + '</optgroup>'
    ).join('');
  }
  function close(){ const m = qs('nfPhotoModal'); if(m) m.style.display = 'none'; }
  function validateAndClose(){
    drawSelected();
    close();
  }
  function getSelected(){ return arr(currentModule).find(p => p.id === selectedId) || null; }
  function setTool(t){
    tool = t;
    [['nfToolArrow','arrow'],['nfToolLine','line'],['nfToolCircle','circle'],['nfToolRect','rect'],['nfToolPoint','point'],['nfToolText','text']].forEach(([id,tt])=>{
      qs(id)?.classList.toggle('active', tt === tool);
    });
  }
  function updateButtons(){
    MODULES.forEach(m=>{
      const c = allPhotosFor(m.id).length;
      document.querySelectorAll('[data-photo-count="'+m.id+'"]').forEach(el=>el.textContent = String(c));
    });
    const c = allPhotosFor('module-reportage-photo').length;
    const st = qs('photoReportStatus');
    if(st) st.textContent = 'Photos : ' + c;
    const pdf = qs('btnPhotoReportPdf');
    if(pdf) pdf.disabled = c <= 0;
  }
  function renderAll(){ renderList(); drawSelected(); updateButtons(); }
  function renderList(){
    const host = qs('nfPhotoList');
    if(!host) return;
    const list = arr(currentModule);
    host.innerHTML = list.length ? list.map(p => `
      <div class="nf-photo-item ${p.id===selectedId?'active':''}" data-id="${esc(p.id)}">
        <img src="${esc(p.dataUrl)}" alt="">
        <div>
          <div style="font-weight:850; font-size:12px;">${esc(p.name || 'Photo')}</div>
          <div class="small">${esc(p.caption || 'Sans legende')}</div>
        </div>
      </div>`).join('') : '<div class="small">Aucune photo pour ce module.</div>';
    host.querySelectorAll('.nf-photo-item').forEach(el=>{
      el.addEventListener('click', ()=>{
        selectedId = el.getAttribute('data-id');
        renderAll();
      });
    });
  }

  function drawSelected(){
    const canvas = qs('nfPhotoCanvas');
    const cap = qs('nfPhotoCaption');
    if(!canvas) return;
    const ctx = canvas.getContext('2d');
    const p = getSelected();
    if(cap) cap.value = p?.caption || '';
    const linkSel = qs('nfPhotoPointLink');
    if(linkSel) linkSel.value = p?.linkedPointId || '';
    ctx.clearRect(0,0,canvas.width,canvas.height);
    ctx.fillStyle = '#fff';
    ctx.fillRect(0,0,canvas.width,canvas.height);
    if(!p || !p.dataUrl){
      ctx.fillStyle = '#4b5563';
      ctx.font = '20px Segoe UI, Arial';
      ctx.textAlign = 'center';
      ctx.fillText('Aucune photo selectionnee', canvas.width/2, canvas.height/2);
      return;
    }
    const img = new Image();
    img.onload = ()=>{
      canvas.width = p.w || img.naturalWidth || 1200;
      canvas.height = p.h || img.naturalHeight || 900;
      const c = canvas.getContext('2d');
      c.clearRect(0,0,canvas.width,canvas.height);
      c.drawImage(img, 0, 0, canvas.width, canvas.height);
      drawShapes(c, p.shapes || []);
      if(previewShape) drawShapes(c, [previewShape], true);
      if(!previewShape){
        try{ p.renderedDataUrl = canvas.toDataURL('image/jpeg', JPEG_QUALITY); }catch(_){}
      }
    };
    img.src = p.dataUrl;
  }

  function canvasPoint(ev){
    const canvas = qs('nfPhotoCanvas');
    const r = canvas.getBoundingClientRect();
    return {
      x: (ev.clientX - r.left) * canvas.width / r.width,
      y: (ev.clientY - r.top) * canvas.height / r.height
    };
  }
  function beginDraw(ev){
    if(!getSelected()) return;
    drawing = true;
    start = canvasPoint(ev);
    previewShape = null;
  }
  function moveDraw(ev){
    if(!drawing || !start) return;
    const p = canvasPoint(ev);
    previewShape = makeShape(start, p);
    drawSelected();
  }
  function endDraw(ev){
    if(!drawing || !start) return;
    drawing = false;
    const p2 = canvasPoint(ev);
    const p = getSelected();
    if(p && (tool === 'point' || tool === 'text' || Math.hypot(p2.x - start.x, p2.y - start.y) > 8)){
      p.shapes = Array.isArray(p.shapes) ? p.shapes : [];
      const shape = makeShape(start, p2);
      if(shape.type !== 'text' || String(shape.text || '').trim()) p.shapes.push(shape);
    }
    start = null;
    previewShape = null;
    drawSelected();
  }

  function makeShape(a, b){
    const shape = { type:tool, x1:a.x, y1:a.y, x2:b.x, y2:b.y, color:drawColor, width:drawWidth };
    if(tool === 'text'){
      shape.text = String(qs('nfDrawText')?.value || '').trim();
      shape.size = textSize;
    }
    return shape;
  }

  function drawShapes(ctx, shapes, preview){
    ctx.save();
    const defaultW = Math.max(4, Math.round(ctx.canvas.width / 320));
    shapes.forEach(s=>{
      const x1=+s.x1||0, y1=+s.y1||0, x2=+s.x2||0, y2=+s.y2||0;
      ctx.strokeStyle = preview ? '#0b1020' : (s.color || '#f97316');
      ctx.fillStyle = ctx.strokeStyle;
      ctx.lineWidth = Math.max(1, Number(s.width || defaultW)) * Math.max(1, ctx.canvas.width / 1200);
      if(s.type === 'circle'){
        const cx = (x1+x2)/2, cy = (y1+y2)/2, rx = Math.abs(x2-x1)/2, ry = Math.abs(y2-y1)/2;
        ctx.beginPath(); ctx.ellipse(cx, cy, rx, ry, 0, 0, Math.PI*2); ctx.stroke();
      }else if(s.type === 'rect'){
        ctx.strokeRect(Math.min(x1,x2), Math.min(y1,y2), Math.abs(x2-x1), Math.abs(y2-y1));
      }else if(s.type === 'point'){
        const r = Math.max(12, Number(s.width || 5) * 3.2) * Math.max(1, ctx.canvas.width / 1200);
        ctx.beginPath(); ctx.arc(x1, y1, r, 0, Math.PI*2); ctx.fill();
        ctx.lineWidth = Math.max(2, r / 5);
        ctx.strokeStyle = '#ffffff';
        ctx.beginPath(); ctx.arc(x1, y1, r * 0.55, 0, Math.PI*2); ctx.stroke();
      }else if(s.type === 'text'){
        const text = String(s.text || '').trim();
        if(!text) return;
        const scale = Math.max(1, ctx.canvas.width / 1200);
        const size = Math.max(14, Number(s.size || 44)) * scale;
        const ang = Math.atan2(y2-y1, x2-x1);
        ctx.save();
        ctx.translate(x1, y1);
        if(Math.hypot(x2-x1, y2-y1) > 8) ctx.rotate(ang);
        ctx.font = `600 ${size}px Segoe UI, Arial, sans-serif`;
        ctx.textBaseline = 'middle';
        ctx.lineJoin = 'round';
        ctx.strokeStyle = 'rgba(0,0,0,.35)';
        ctx.lineWidth = Math.max(3, size / 14);
        ctx.strokeText(text, 0, 0);
        ctx.fillStyle = preview ? '#0b1020' : (s.color || '#f97316');
        ctx.fillText(text, 0, 0);
        ctx.restore();
      }else if(s.type === 'line'){
        ctx.beginPath(); ctx.moveTo(x1,y1); ctx.lineTo(x2,y2); ctx.stroke();
      }else{
        ctx.beginPath(); ctx.moveTo(x1,y1); ctx.lineTo(x2,y2); ctx.stroke();
        const ang = Math.atan2(y2-y1, x2-x1), len = Math.max(18, ctx.canvas.width/35);
        ctx.beginPath();
        ctx.moveTo(x2,y2);
        ctx.lineTo(x2 - len*Math.cos(ang-Math.PI/7), y2 - len*Math.sin(ang-Math.PI/7));
        ctx.lineTo(x2 - len*Math.cos(ang+Math.PI/7), y2 - len*Math.sin(ang+Math.PI/7));
        ctx.closePath(); ctx.fill();
      }
    });
    ctx.restore();
  }

  async function onFiles(ev){
    const files = Array.from(ev.target.files || []);
    ev.target.value = '';
    for(const f of files){
      try{
        const p = await normalizeFile(f);
        arr(currentModule).push(p);
        selectedId = p.id;
      }catch(e){
        try{ alert('Photo non lisible : ' + (f?.name || '') + '\n' + (e?.message || e)); }catch(_){}
      }
    }
    renderAll();
  }
  function loadImageFromFile(file){
    return new Promise((resolve, reject)=>{
      const url = URL.createObjectURL(file);
      const img = new Image();
      img.onload = ()=>{ URL.revokeObjectURL(url); resolve(img); };
      img.onerror = ()=>{ URL.revokeObjectURL(url); reject(new Error('format image non pris en charge par Windows/WebView2')); };
      img.src = url;
    });
  }
  async function normalizeFile(file){
    const img = await loadImageFromFile(file);
    let w = img.naturalWidth || img.width;
    let h = img.naturalHeight || img.height;
    if(!w || !h) throw new Error('dimensions invalides');
    const scale = Math.min(1, MAX_SIDE / Math.max(w, h));
    w = Math.max(1, Math.round(w * scale));
    h = Math.max(1, Math.round(h * scale));
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d', { alpha:false });
    ctx.fillStyle = '#fff';
    ctx.fillRect(0,0,w,h);
    ctx.drawImage(img, 0, 0, w, h);
    const dataUrl = canvas.toDataURL('image/jpeg', JPEG_QUALITY);
    return { id:uuid(), name:file.name || 'photo.jpg', caption:'', dataUrl, w, h, shapes:[] };
  }
  function renderedDataUrl(photo){
    if(!photo || !photo.dataUrl) return '';
    const canvas = document.createElement('canvas');
    canvas.width = photo.w || 1200;
    canvas.height = photo.h || 900;
    const ctx = canvas.getContext('2d', { alpha:false });
    return new Promise(resolve=>{
      const img = new Image();
      img.onload = ()=>{
        ctx.fillStyle = '#fff'; ctx.fillRect(0,0,canvas.width,canvas.height);
        ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
        drawShapes(ctx, photo.shapes || []);
        resolve(canvas.toDataURL('image/jpeg', JPEG_QUALITY));
      };
      img.onerror = ()=>resolve(photo.dataUrl);
      img.src = photo.dataUrl;
    });
  }
  async function exportPhotos(moduleId){
    const mid = moduleId || activeModuleId();
    const list = allPhotosFor(mid);
    // Ordre des points tel qu'utilise pour peupler le selecteur de liaison (m03b: implantation
    // puis ligne de reference, ou leve puis transfert alti) : sert de cle de tri des photos
    // liees dans l'annexe PDF, pour qu'elles suivent l'ordre d'apparition dans le rapport.
    const { groups, coordsById } = moduleReportPoints(mid);
    const order = new Map();
    let orderIdx = 0;
    groups.forEach(g => g.ids.forEach(id => { if(!order.has(id)) order.set(id, orderIdx++); }));
    const out = [];
    for(const p of list){
      const linkedPointId = p.linkedPointId || null;
      const coord = linkedPointId ? coordsById.get(linkedPointId) : null;
      out.push({
        module: mid,
        moduleLabel: moduleLabel(mid),
        name: p.name || '',
        caption: p.caption || '',
        imageData: p.renderedDataUrl || await renderedDataUrl(p),
        w: p.w || 0,
        h: p.h || 0,
        linkedPointId: linkedPointId,
        orderKey: (linkedPointId && order.has(linkedPointId)) ? order.get(linkedPointId) : null,
        linkedPointX: coord?.x ?? null,
        linkedPointY: coord?.y ?? null,
        linkedPointZ: coord?.z ?? null
      });
    }
    return out;
  }

  async function generatePhotoReportPdf(){
    try{
      const moduleId = 'module-reportage-photo';
      const photos = await exportPhotos(moduleId);
      if(!photos.length){
        alert('Ajoute au moins une photo avant de generer le reportage.');
        return;
      }
      const R = (typeof rf === 'function') ? (rf() || {}) : {};
      const fileName = (typeof buildExportFileName === 'function')
        ? buildExportFileName('REPORTAGE_PHOTO', 'PDF')
        : 'NOVA_Reportage_Photo.pdf';
      const payload = {
        type:'pdfsharp_photo_report',
        fileName,
        info:R,
        photoAppendix:{ photos, photosPerPage:getReportPhotosPerPage() }
      };
      if(window.chrome?.webview?.postMessage){
        window.chrome.webview.postMessage(payload);
      }else{
        alert('Export PDF disponible dans Nova-Fiches.');
      }
    }catch(e){
      alert('Erreur reportage photo : ' + (e?.message || e));
    }
  }
  async function appendPayloadPhotos(payload, moduleId){
    const photos = await exportPhotos(moduleId);
    if(photos.length){
      const previous = (payload && payload.photoAppendix && typeof payload.photoAppendix === 'object') ? payload.photoAppendix : {};
      payload.photoAppendix = Object.assign({}, previous, { photos });
    }
    return payload;
  }
  function appendPhotosToJsPdf(doc, moduleId){
    const photos = allPhotosFor(moduleId || activeModuleId());
    if(!photos.length || !doc) return;
    const R = (typeof rf === 'function') ? rf() : {};
    const pageW = 210, pageH = 297;
    const margin = 10;
    const top = 30;
    const footer = 18;
    const gapX = 8, gapY = 8;
    const cellW = (pageW - margin*2 - gapX) / 2;
    const cellH = (pageH - top - footer - gapY) / 2;
    const imgH = cellH - 12;

    photos.forEach((p, idx)=>{
      if(idx % 4 === 0){
        doc.addPage();
        try{ drawHeaderV2(doc, Object.assign({}, R, { elements:'ANNEXE PHOTOS' })); }catch(_){}
        try{
          doc.setFontSize(11); doc.setTextColor(0,0,0); doc.setFont('helvetica','bold');
          doc.text('ANNEXE PHOTOS - ' + moduleLabel(moduleId || activeModuleId()).toUpperCase(), margin, 25);
        }catch(_){}
      }
      const local = idx % 4;
      const col = local % 2;
      const row = Math.floor(local / 2);
      const x = margin + col * (cellW + gapX);
      const y = top + row * (cellH + gapY);
      try{
        doc.setDrawColor(190,190,190);
        doc.rect(x, y, cellW, imgH);
        const ar = (p.w && p.h) ? p.w/p.h : 1.33;
        let dw = cellW, dh = dw / ar;
        if(dh > imgH){ dh = imgH; dw = dh * ar; }
        const dx = x + (cellW-dw)/2, dy = y + (imgH-dh)/2;
        doc.addImage(renderedCanvasSync(p), 'JPEG', dx, dy, dw, dh, undefined, 'FAST');
        doc.setFontSize(8);
        doc.setFont('helvetica','normal');
        doc.text(String(p.caption || p.name || ''), x, y + imgH + 5, { maxWidth:cellW });
      }catch(e){}
    });
  }
  function renderedCanvasSync(photo){
    // Fast path for PDF save: annotations already exist as vector data, draw synchronously on cached image if possible.
    // jsPDF accepts the normalized image; if the cache is not ready, annotations remain visible in the editor and PDFSharp path.
    return photo.renderedDataUrl || photo.dataUrl;
  }

  function wrapState(){
    const oldGet = window.NOVA_getState;
    if(typeof oldGet === 'function' && !oldGet.__nfPhotosWrapped){
      const wrapped = function(){
        const st = oldGet();
        st.photos = window[STORE_KEY] || {};
        st.photoReportOptions = Object.assign({}, st.photoReportOptions || {}, { photosPerPage:getReportPhotosPerPage() });
        return st;
      };
      wrapped.__nfPhotosWrapped = true;
      window.NOVA_getState = wrapped;
    }
    const oldSet = window.NOVA_setState;
    if(typeof oldSet === 'function' && !oldSet.__nfPhotosWrapped){
      const wrapped = function(st){
        oldSet(st);
        window[STORE_KEY] = (st && st.photos && typeof st.photos === 'object') ? st.photos : {};
        setReportPhotosPerPage(st?.photoReportOptions?.photosPerPage || 4);
        updateButtons();
      };
      wrapped.__nfPhotosWrapped = true;
      window.NOVA_setState = wrapped;
    }
  }
  function wrapPdfSave(){
    const old = window.savePdfDoc;
    if(typeof old === 'function' && !old.__nfPhotosWrapped){
      const wrapped = function(doc, fileName){
        try{ appendPhotosToJsPdf(doc, activeModuleId()); }catch(e){}
        return old.call(this, doc, fileName);
      };
      wrapped.__nfPhotosWrapped = true;
      window.savePdfDoc = wrapped;
    }
  }
  function wrapPostMessage(){
    const wv = window.chrome?.webview;
    if(!wv || typeof wv.postMessage !== 'function' || wv.postMessage.__nfPhotosWrapped) return;
    const old = wv.postMessage.bind(wv);
    const wrapped = function(payload){
      try{
        const t = String(payload?.type || '').toLowerCase();
        if(t.startsWith('pdfsharp_')){
          appendPayloadPhotos(payload, activeModuleId()).then(p => old(p));
          return;
        }
      }catch(_){}
      return old(payload);
    };
    wrapped.__nfPhotosWrapped = true;
    wv.postMessage = wrapped;
  }

  window.NF_Photos = {
    open,
    generatePhotoReportPdf,
    getStore: () => window[STORE_KEY],
    setStore: (s) => { window[STORE_KEY] = s || {}; updateButtons(); },
    exportPhotos,
    appendPayloadPhotos,
    appendPhotosToJsPdf
  };

  document.addEventListener('DOMContentLoaded', ()=>{
    injectButtons();
    wrapState();
    wrapPdfSave();
    wrapPostMessage();
    setInterval(()=>{ wrapState(); wrapPdfSave(); wrapPostMessage(); }, 1200);
  });
})();
