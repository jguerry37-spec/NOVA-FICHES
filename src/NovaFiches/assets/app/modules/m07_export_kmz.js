(function(){
  const state = {
    mode: 'combined',
    points: [],
    lines: [],
    texts: [],
    txtPoints: [],
    dxfPreviewPoints: [],
    dxfPoints: [],
    selectedTxtKeys: new Set(),
    selectedLayers: new Set(),
    selectedPointKeys: new Set(),
    map: null,
    layer: null,
    baseLayer: null,
    basemap: 'plan',
    leafletReady: false,
    leafletLoading: false,
    ngfEnabled: false,
    ngfPoints: [],
    drawingZone: false,
    zoneCorner1: null,
    zonePreviewLayer: null,
    zoneLayer: null,
    measuring: false,
    measurePoints: [],
    measureLayer: null
  };

  function el(id){ return document.getElementById(id); }
  function post(payload){
    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function'){
        window.chrome.webview.postMessage(payload);
      }
    }catch(_){ }
  }
  function esc(s){ return String(s ?? '').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }
  function fmt(n, d){ const v = Number(n); return Number.isFinite(v) ? v.toFixed(d) : ''; }
  function setStatus(text, cls){
    const p = el('kmzStatus');
    if(!p) return;
    p.textContent = text;
    p.className = 'pill' + (cls ? ' ' + cls : '');
  }
  function setMapStatus(text){ const s = el('kmzMapStatus'); if(s) s.textContent = text; }
  function setNgfStatus(text){ const s = el('kmzNgfStatus'); if(s) s.textContent = text; }
  function setMeasureStatus(text){ const s = el('kmzMeasureStatus'); if(s) s.textContent = text; }
  // Avec la souris, un "clic" comporte quasi toujours quelques pixels de mouvement
  // entre l'appui et le relachement. Leaflet interprete ca comme un mini-glisser et
  // deplace deja la vue en consequence avant de calculer les coordonnees du clic,
  // meme quand il classe encore le geste comme un simple clic (pas un vrai
  // deplacement de carte). Resultat : la carte semble "sauter" pile au moment du
  // premier clic, et les coordonnees renvoyees correspondent a la vue deja
  // deplacee. Desactiver le glisser (pas le zoom, deja retire) pendant Mesurer et
  // Dessiner une zone elimine ce micro-decalage sans toucher au zoom.
  function updateDragLock(){
    if(!state.map || !state.map.dragging) return;
    if(state.measuring || state.drawingZone) state.map.dragging.disable();
    else state.map.dragging.enable();
  }
  function refreshCombined(){
    const txt = state.txtPoints.filter(p => state.selectedTxtKeys.has(String(p.key ?? p.Key)));
    state.points = txt.concat(state.dxfPreviewPoints);
    renderTable();
    renderMap();
    el('btnKmzExport') && (el('btnKmzExport').disabled =
      state.points.length === 0 && state.lines.length === 0 && state.texts.length === 0);
  }
  function webUrl(host, path){
    return 'https:' + '//' + host + path;
  }

  function loadLeaflet(){
    if(window.L) return Promise.resolve(true);
    if(state.leafletLoading) return new Promise(resolve => {
      const t = setInterval(() => {
        if(window.L){ clearInterval(t); resolve(true); }
      }, 100);
      setTimeout(() => { clearInterval(t); resolve(!!window.L); }, 7000);
    });

    state.leafletLoading = true;
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

  function createTileLayer(kind){
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

  async function renderMap(opts){
    const forceFit = !!(opts && opts.forceFit);
    const pts = state.points || [];
    const lines = state.lines || [];
    const texts = state.texts || [];
    const mapDiv = el('kmzMap');
    const canvas = el('kmzCanvas');
    const hasNgf = state.ngfEnabled && state.ngfPoints.length > 0;
    if(!pts.length && !lines.length && !texts.length && !hasNgf){
      if(mapDiv) mapDiv.style.display = 'block';
      if(canvas) canvas.style.display = 'none';
      setMapStatus('Charge un TXT pour afficher les points sur fond de plan en ligne.');
      return;
    }

    const ok = await loadLeaflet();
    if(ok && mapDiv){
      try{
        mapDiv.style.display = 'block';
        if(canvas) canvas.style.display = 'none';
        const isNewMap = !state.map;
        if(isNewMap){
          // Les popups Leaflet recentrent la carte par defaut (autoPan) des qu'ils
          // s'ouvrent trop pres du bord visible. Avec des points denses (reperes NGF,
          // TXT/DXF), un simple clic pres d'un marqueur pour poser un point de mesure
          // ou un coin de zone ouvrait son popup et deplacait la carte sous l'utilisateur.
          if(L.Popup) L.Popup.mergeOptions({ autoPan: false });
          state.map = L.map(mapDiv, { attributionControl:true });
          state.baseLayer = createTileLayer(state.basemap);
          state.baseLayer.addTo(state.map);
          state.map.on('click', onMapClick);
          state.map.on('mousemove', onZoneMouseMove);
          // Au premier clic sur la carte, Leaflet donne le focus clavier a son
          // conteneur (accessibilite) puis tente de restaurer la position de
          // defilement de la PAGE pour compenser le "scroll to focus" natif du
          // navigateur. Mais cette page defile via un conteneur interne
          // (main.nf-content, overflow:auto), pas via le corps de la page - le
          // correctif de Leaflet ne regarde que document.body/documentElement et
          // ne voit donc jamais ce defilement, qui reste intact. Resultat : la
          // zone visible saute au premier clic (carte + barre d'outils), donnant
          // l'impression que la carte "se deplace". Verifie par reproduction
          // directe. Empecher tout defilement associe au focus du conteneur
          // regle le probleme sans toucher au comportement clavier lui-meme.
          const mapContainer = state.map.getContainer();
          const nativeFocus = mapContainer.focus.bind(mapContainer);
          mapContainer.focus = function(opts){
            return nativeFocus(Object.assign({}, opts, { preventScroll: true }));
          };
        }
        if(state.layer) state.map.removeLayer(state.layer);
        state.layer = L.featureGroup();
        pts.forEach(p => {
          const lat = Number(p.lat ?? p.Lat);
          const lon = Number(p.lon ?? p.Lon);
          if(!Number.isFinite(lat) || !Number.isFinite(lon)) return;
          const fromTxt = String(p.source ?? '').toUpperCase() === 'TXT';
          const marker = L.circleMarker([lat, lon], {
            radius: 5,
            color: fromTxt ? '#087f5b' : '#1267f3',
            weight: 2,
            fillColor: fromTxt ? '#63e6be' : '#ffb020',
            fillOpacity: 0.85
          });
          marker.bindTooltip(String(p.id ?? p.Id ?? ''), { permanent:false, direction:'top' });
          const hasZ = (p.hasZ ?? p.HasZ) !== false;
          marker.bindPopup(`<b>${esc(p.id ?? p.Id ?? '')}</b><br>X ${esc(fmt(p.x ?? p.X,3))}<br>Y ${esc(fmt(p.y ?? p.Y,3))}<br>Z ${hasZ ? esc(fmt(p.z ?? p.Z,3)) : 'non fourni'}`);
          state.layer.addLayer(marker);
        });
        lines.forEach(line => {
          const lat1 = Number(line.lat1 ?? line.Lat1);
          const lon1 = Number(line.lon1 ?? line.Lon1);
          const lat2 = Number(line.lat2 ?? line.Lat2);
          const lon2 = Number(line.lon2 ?? line.Lon2);
          if(![lat1,lon1,lat2,lon2].every(Number.isFinite)) return;
          const shape = L.polyline([[lat1,lon1],[lat2,lon2]], {
            color:'#1267f3',
            weight:3,
            opacity:0.9
          });
          shape.bindTooltip(String(line.layer ?? line.Layer ?? 'DXF'));
          state.layer.addLayer(shape);
        });
        texts.forEach(text => {
          const lat = Number(text.lat ?? text.Lat);
          const lon = Number(text.lon ?? text.Lon);
          if(!Number.isFinite(lat) || !Number.isFinite(lon)) return;
          const marker = L.circleMarker([lat, lon], {
            radius:1,
            color:'transparent',
            fillColor:'transparent',
            fillOpacity:0
          });
          marker.bindTooltip(String(text.text ?? text.Text ?? ''), {
            permanent:true,
            direction:'center',
            className:'kmz-dxf-text'
          });
          state.layer.addLayer(marker);
        });
        if(hasNgf){
          const ngfIcon = L.divIcon({
            className: 'kmz-ngf-icon',
            html: '<div class="kmz-ngf-triangle-outer"><div class="kmz-ngf-triangle-inner"></div></div>',
            iconSize: [20, 17],
            iconAnchor: [10, 9]
          });
          state.ngfPoints.forEach(p => {
            const lat = Number(p.lat), lon = Number(p.lon);
            if(!Number.isFinite(lat) || !Number.isFinite(lon)) return;
            const marker = L.marker([lat, lon], { icon: ngfIcon });
            const label = String(p.nom || p.id || '');
            marker.bindTooltip(label, { permanent:false, direction:'top' });
            const altValue = (p.altitude !== null && p.altitude !== undefined && p.altitude !== '') ? Number(p.altitude) : NaN;
            const alt = Number.isFinite(altValue) ? altValue.toFixed(3) + ' m' : 'inconnue';
            const ficheLink = p.ficheUrl
              ? `<br><a href="${esc(p.ficheUrl)}" target="_blank" rel="noopener">Télécharger la fiche (PDF)</a>`
              : '';
            marker.bindPopup(`<b>${esc(label)}</b><br>Altitude NGF ${esc(alt)}<br>${esc(p.etat || '')}${ficheLink}`);
            state.layer.addLayer(marker);
          });
        }
        state.layer.addTo(state.map);
        const bounds = state.layer.getBounds();
        const fitImportedData = () => {
          try{
            state.map.invalidateSize();
            if(bounds && bounds.isValid()) state.map.fitBounds(bounds.pad(0.20), { maxZoom: 20 });
          }catch(_){}
        };
        if(isNewMap || forceFit){
          fitImportedData();
          setTimeout(fitImportedData, 120);
        }else{
          try{ state.map.invalidateSize(); }catch(_){}
        }
        setMapStatus('Fond de carte chargé via Internet. Contrôle visuel des points actif.');
        return;
      }catch(e){
        console.warn('[KMZ] Leaflet render failed', e);
      }
    }

    renderCanvasFallback();
  }

  function renderCanvasFallback(){
    const pts = state.points || [];
    const lines = state.lines || [];
    const texts = state.texts || [];
    const mapDiv = el('kmzMap');
    const canvas = el('kmzCanvas');
    if(mapDiv) mapDiv.style.display = 'none';
    if(!canvas) return;
    canvas.style.display = 'block';
    const ctx = canvas.getContext('2d');
    const w = canvas.width, h = canvas.height;
    ctx.clearRect(0,0,w,h);
    ctx.fillStyle = '#f7fafc';
    ctx.fillRect(0,0,w,h);
    ctx.strokeStyle = '#d5dde8';
    for(let x=0;x<w;x+=50){ ctx.beginPath(); ctx.moveTo(x,0); ctx.lineTo(x,h); ctx.stroke(); }
    for(let y=0;y<h;y+=50){ ctx.beginPath(); ctx.moveTo(0,y); ctx.lineTo(w,y); ctx.stroke(); }

    const xs = pts.map(p=>Number(p.lon ?? p.Lon))
      .concat(lines.flatMap(l=>[Number(l.lon1 ?? l.Lon1),Number(l.lon2 ?? l.Lon2)]))
      .concat(texts.map(t=>Number(t.lon ?? t.Lon)))
      .filter(Number.isFinite);
    const ys = pts.map(p=>Number(p.lat ?? p.Lat))
      .concat(lines.flatMap(l=>[Number(l.lat1 ?? l.Lat1),Number(l.lat2 ?? l.Lat2)]))
      .concat(texts.map(t=>Number(t.lat ?? t.Lat)))
      .filter(Number.isFinite);
    if(!xs.length || !ys.length) return;
    const minX = Math.min(...xs), maxX = Math.max(...xs);
    const minY = Math.min(...ys), maxY = Math.max(...ys);
    const pad = 35;
    const sx = (w-2*pad) / Math.max(1e-12, maxX-minX);
    const sy = (h-2*pad) / Math.max(1e-12, maxY-minY);
    const s = Math.min(sx, sy);
    const cx = (minX+maxX)/2, cy = (minY+maxY)/2;
    function px(lon){ return w/2 + (lon-cx)*s; }
    function py(lat){ return h/2 - (lat-cy)*s; }

    ctx.strokeStyle = '#1267f3';
    ctx.lineWidth = 2;
    lines.forEach(line=>{
      const lon1=Number(line.lon1 ?? line.Lon1), lat1=Number(line.lat1 ?? line.Lat1);
      const lon2=Number(line.lon2 ?? line.Lon2), lat2=Number(line.lat2 ?? line.Lat2);
      if(![lon1,lat1,lon2,lat2].every(Number.isFinite)) return;
      ctx.beginPath(); ctx.moveTo(px(lon1),py(lat1)); ctx.lineTo(px(lon2),py(lat2)); ctx.stroke();
    });

    ctx.fillStyle = '#1267f3';
    ctx.strokeStyle = '#ffffff';
    ctx.font = '12px Segoe UI, Arial';
    pts.forEach(p=>{
      const lon = Number(p.lon ?? p.Lon), lat = Number(p.lat ?? p.Lat);
      if(!Number.isFinite(lon) || !Number.isFinite(lat)) return;
      const x = px(lon), y = py(lat);
      ctx.beginPath(); ctx.arc(x,y,5,0,Math.PI*2); ctx.fill(); ctx.stroke();
      ctx.fillStyle = '#0b1020';
      ctx.fillText(String(p.id ?? p.Id ?? ''), x+7, y-7);
      ctx.fillStyle = '#1267f3';
    });
    ctx.fillStyle = '#111111';
    texts.forEach(text=>{
      const lon = Number(text.lon ?? text.Lon), lat = Number(text.lat ?? text.Lat);
      if(!Number.isFinite(lon) || !Number.isFinite(lat)) return;
      ctx.fillText(String(text.text ?? text.Text ?? ''), px(lon), py(lat));
    });
    ctx.fillStyle = '#0b1020';
    ctx.font = 'bold 13px Segoe UI, Arial';
    ctx.fillText('N', w-28, 24);
    ctx.beginPath(); ctx.moveTo(w-24, 38); ctx.lineTo(w-24, 10); ctx.lineTo(w-30, 20); ctx.moveTo(w-24, 10); ctx.lineTo(w-18, 20); ctx.strokeStyle = '#0b1020'; ctx.stroke();
    setMapStatus('Carte Internet indisponible : affichage local de secours.');
  }

  function renderTable(){
    const tbody = el('kmzTable')?.querySelector('tbody');
    if(!tbody) return;
    el('kmzIncludeHead')?.classList.remove('nf-hidden');
    el('kmzLayerHead')?.classList.remove('nf-hidden');
    const projected = new Map(state.dxfPreviewPoints.map(p => [String(p.id ?? p.Id), p]));
    const txtRows = state.txtPoints.map(p => {
      const key = String(p.key ?? p.Key);
      const checked = state.selectedTxtKeys.has(key) ? ' checked' : '';
      return `<tr>
        <td><input class="kmz-txt-point-check" type="checkbox" data-key="${esc(key)}"${checked}></td>
        <td>${esc(p.id ?? p.Id)}</td><td>TXT</td>
        <td>${esc(fmt(p.x ?? p.X,3))}</td><td>${esc(fmt(p.y ?? p.Y,3))}</td>
        <td>${(p.hasZ ?? p.HasZ) === false ? '' : esc(fmt(p.z ?? p.Z,3))}</td>
        <td>${esc(fmt(p.lon ?? p.Lon,8))}</td><td>${esc(fmt(p.lat ?? p.Lat,8))}</td>
        <td>${esc(p.code ?? p.Code ?? '')}</td>
      </tr>`;
    });
    const dxfRows = state.dxfPoints.map(p => {
      const id = String(p.id ?? p.Id ?? '');
      const key = String(p.key ?? p.Key ?? id);
      const preview = projected.get(id) || {};
      const checked = state.selectedPointKeys.has(key) ? ' checked' : '';
      return `<tr>
        <td><input class="kmz-dxf-point-check" type="checkbox" data-key="${esc(key)}"${checked}></td>
        <td>${esc(id)}</td><td>DXF · ${esc(p.layer ?? p.Layer ?? '')}</td>
        <td>${esc(fmt(p.x ?? p.X,3))}</td><td>${esc(fmt(p.y ?? p.Y,3))}</td>
        <td>${(p.hasZ ?? p.HasZ) === false ? '' : esc(fmt(p.z ?? p.Z,3))}</td>
        <td>${esc(fmt(preview.lon ?? preview.Lon,8))}</td><td>${esc(fmt(preview.lat ?? preview.Lat,8))}</td>
        <td>${esc(p.code ?? p.Code ?? '')}</td>
      </tr>`;
    });
    tbody.innerHTML = txtRows.concat(dxfRows).join('');
    tbody.querySelectorAll('.kmz-txt-point-check').forEach(box => box.addEventListener('change', () => {
      if(box.checked) state.selectedTxtKeys.add(box.dataset.key);
      else state.selectedTxtKeys.delete(box.dataset.key);
      refreshCombined();
    }));
    tbody.querySelectorAll('.kmz-dxf-point-check').forEach(box => box.addEventListener('change', () => {
      if(box.checked) state.selectedPointKeys.add(box.dataset.key);
      else state.selectedPointKeys.delete(box.dataset.key);
      requestDxfPreview();
    }));
  }

  function applyCoordinateSystems(systems, selected){
    const sel = el('kmzCoordSys');
    if(!sel || !Array.isArray(systems) || !systems.length) return;
    const current = selected || sel.value;
    sel.innerHTML = '';
    const auto = document.createElement('option');
    auto.value = '__AUTO__';
    auto.textContent = 'Détection automatique';
    sel.appendChild(auto);
    systems.forEach(name => {
      const opt = document.createElement('option');
      opt.value = name;
      opt.textContent = name;
      sel.appendChild(opt);
    });
    if(current && !Array.from(sel.options).some(o => o.value === current)){
      const opt = document.createElement('option');
      opt.value = current;
      opt.textContent = current;
      sel.appendChild(opt);
    }
    if(current) sel.value = current;
  }


  function onMapClick(ev){
    if(state.drawingZone){ onZoneMapClick(ev); return; }
    if(state.measuring){ onMeasureMapClick(ev); return; }
  }

  function drawNgfZoneLayer(bounds){
    if(!state.map) return;
    if(state.zoneLayer){ state.map.removeLayer(state.zoneLayer); state.zoneLayer = null; }
    state.zoneLayer = L.rectangle(bounds, { color:'#1c3fdc', weight:2, fillOpacity:0.03, dashArray:'4,4' }).addTo(state.map);
  }

  function fetchNgfForBounds(bounds){
    setNgfStatus('Chargement des repères NGF…');
    post({
      type: 'kmz_fetch_ngf',
      minLon: bounds.getWest(),
      minLat: bounds.getSouth(),
      maxLon: bounds.getEast(),
      maxLat: bounds.getNorth()
    });
  }

  function onZoneMouseMove(ev){
    if(!state.drawingZone || !state.zoneCorner1 || !state.map) return;
    const bounds = L.latLngBounds(state.zoneCorner1, ev.latlng);
    if(state.zonePreviewLayer) state.map.removeLayer(state.zonePreviewLayer);
    state.zonePreviewLayer = L.rectangle(bounds, { color:'#1c3fdc', weight:2, dashArray:'4,4', fillOpacity:0.05 }).addTo(state.map);
  }

  function onZoneMapClick(ev){
    if(!state.zoneCorner1){
      state.zoneCorner1 = ev.latlng;
      setNgfStatus('Clique le second coin de la zone à charger.');
      return;
    }
    const bounds = L.latLngBounds(state.zoneCorner1, ev.latlng);
    state.drawingZone = false;
    state.zoneCorner1 = null;
    if(state.map) state.map.getContainer().style.cursor = '';
    if(state.zonePreviewLayer){ state.map.removeLayer(state.zonePreviewLayer); state.zonePreviewLayer = null; }
    updateDragLock();
    drawNgfZoneLayer(bounds);
    fetchNgfForBounds(bounds);
  }

  function handleNgfLoaded(msg){
    state.ngfPoints = Array.isArray(msg.points) ? msg.points.map(p => ({
      id: p.id ?? p.Id ?? '',
      nom: p.nom ?? p.Nom ?? '',
      etat: p.etat ?? p.Etat ?? null,
      altitude: p.altitude ?? p.Altitude ?? null,
      lon: p.lon ?? p.Lon,
      lat: p.lat ?? p.Lat,
      ficheUrl: p.ficheUrl ?? p.FicheUrl ?? null
    })) : [];
    setNgfStatus(`${state.ngfPoints.length} repère(s) NGF chargé(s) sur la zone visible.`);
    renderMap();
    renderTable();
  }

  function fmtDistance(meters){
    return meters >= 1000 ? (meters/1000).toFixed(2) + ' km' : meters.toFixed(1) + ' m';
  }

  function redrawMeasureLayer(){
    if(!state.map) return;
    if(state.measureLayer){ state.map.removeLayer(state.measureLayer); state.measureLayer = null; }
    if(!state.measurePoints.length) return;
    state.measureLayer = L.layerGroup();
    state.measurePoints.forEach(pt => {
      L.circleMarker(pt, { radius:4, color:'#e03131', weight:2, fillColor:'#ff8787', fillOpacity:0.9 }).addTo(state.measureLayer);
    });
    if(state.measurePoints.length >= 2){
      L.polyline(state.measurePoints, { color:'#e03131', weight:3, dashArray:'6,6' }).addTo(state.measureLayer);
    }
    state.measureLayer.addTo(state.map);
  }

  function onMeasureMapClick(ev){
    if(!state.measuring) return;
    state.measurePoints.push([ev.latlng.lat, ev.latlng.lng]);
    redrawMeasureLayer();
    if(state.measurePoints.length < 2){
      setMeasureStatus('Clique un second point pour mesurer.');
      return;
    }
    let total = 0;
    for(let i = 1; i < state.measurePoints.length; i++){
      total += state.map.distance(state.measurePoints[i-1], state.measurePoints[i]);
    }
    setMeasureStatus(`Distance : ${fmtDistance(total)}`);
  }

  function handleLoaded(msg){
    state.mode = 'combined';
    const hadTxt = state.txtPoints.length > 0;
    const resetSelection = msg.resetSelection === true;
    const previousTxtSelection = new Set(state.selectedTxtKeys);
    state.txtPoints = Array.isArray(msg.points) ? msg.points.map(p => ({
      key: p.Key ?? p.key,
      id: p.Id ?? p.id,
      x: p.X ?? p.x,
      y: p.Y ?? p.y,
      z: p.Z ?? p.z,
      hasZ: p.HasZ ?? p.hasZ,
      code: p.Code ?? p.code,
      lon: p.Lon ?? p.lon,
      lat: p.Lat ?? p.lat,
      source: 'TXT'
    })) : [];
    state.selectedTxtKeys = hadTxt && !resetSelection
      ? new Set(state.txtPoints.map(p => String(p.key)).filter(key => previousTxtSelection.has(key)))
      : new Set(state.txtPoints.map(p => String(p.key)));
    el('kmzPointActions')?.classList.remove('nf-hidden');
    if(el('kmzPointsTitle')) el('kmzPointsTitle').textContent = 'Points importés';
    const sel = el('kmzCoordSys');
    applyCoordinateSystems(msg.coordinateSystems, msg.sourceCrs);
    if(sel && msg.sourceCrs){
      if(!Array.from(sel.options).some(o => o.value === msg.sourceCrs)){
        const opt = document.createElement('option');
        opt.value = msg.sourceCrs;
        opt.textContent = msg.sourceCrs;
        sel.appendChild(opt);
      }
      sel.value = msg.sourceCrs;
    }
    setStatus(`TXT : ${msg.fileName || 'chargé'} (${state.txtPoints.length} pts)${state.dxfPoints.length ? ' + DXF chargé' : ''}`, 'ok');
    const out = msg.outputPath ? ` Export : ${msg.outputPath}` : '';
    const detected = msg.detectionMethod ? ` Système : ${msg.sourceCrs} (${msg.detectionMethod}).` : '';
    setMapStatus(`${state.txtPoints.length} point(s) TXT transformé(s) en WGS84.${detected}${out}`);
    refreshCombined();
    if(state.dxfPoints.length) requestDxfPreview();
  }

  function renderDxfLayers(layers){
    const list = el('kmzLayerList');
    if(!list) return;
    list.innerHTML = (layers || []).map(layer => {
      const name = String(layer.Name ?? layer.name ?? '');
      const pc = Number(layer.PointCount ?? layer.pointCount ?? 0);
      const lc = Number(layer.LineCount ?? layer.lineCount ?? 0);
      const tc = Number(layer.TextCount ?? layer.textCount ?? 0);
      const checked = state.selectedLayers.has(name) ? ' checked' : '';
      return `<label style="display:flex;align-items:center;gap:8px;">
        <input class="kmz-layer-check" type="checkbox" data-layer="${esc(name)}"${checked}>
        <span><b>${esc(name)}</b> <span class="small">${pc} point(s), ${lc} trait(s), ${tc} texte(s)</span></span>
      </label>`;
    }).join('');
    list.querySelectorAll('.kmz-layer-check').forEach(box => box.addEventListener('change', () => {
      if(box.checked) state.selectedLayers.add(box.dataset.layer);
      else state.selectedLayers.delete(box.dataset.layer);
      requestDxfPreview();
    }));
  }

  function currentDxfPayload(type){
    return {
      type,
      sourceCrs: el('kmzCoordSys')?.value || '__AUTO__',
      layers: Array.from(state.selectedLayers),
      pointKeys: Array.from(state.selectedPointKeys)
    };
  }

  function combinedExportPayload(){
    return {
      type:'kmz_export_combined',
      sourceCrs:el('kmzCoordSys')?.value || '__AUTO__',
      txtPointKeys:Array.from(state.selectedTxtKeys),
      dxfPointKeys:Array.from(state.selectedPointKeys),
      layers:Array.from(state.selectedLayers),
      ngfPoints: state.ngfEnabled ? state.ngfPoints : []
    };
  }

  function requestDxfPreview(){
    if(state.dxfPoints.length) post(currentDxfPayload('kmz_preview_dxf'));
  }

  function handleDxfLoaded(msg){
    state.mode = 'combined';
    const firstDxfLoad = state.dxfPoints.length === 0;
    state.dxfPoints = Array.isArray(msg.points) ? msg.points : [];
    state.dxfPreviewPoints = Array.isArray(msg.previewPoints) ? msg.previewPoints.map(p => ({
      id:p.Id ?? p.id, x:p.X ?? p.x, y:p.Y ?? p.y, z:p.Z ?? p.z,
      code:p.Code ?? p.code, lon:p.Lon ?? p.lon, lat:p.Lat ?? p.lat,
      hasZ:p.HasZ ?? p.hasZ, source:'DXF'
    })) : [];
    state.lines = Array.isArray(msg.previewLines) ? msg.previewLines.map(l => ({
      id:l.Id ?? l.id, layer:l.Layer ?? l.layer,
      lon1:l.Lon1 ?? l.lon1, lat1:l.Lat1 ?? l.lat1,
      lon2:l.Lon2 ?? l.lon2, lat2:l.Lat2 ?? l.lat2
    })) : [];
    state.texts = Array.isArray(msg.previewTexts) ? msg.previewTexts.map(t => ({
      id:t.Id ?? t.id, layer:t.Layer ?? t.layer, text:t.Text ?? t.text,
      lon:t.Lon ?? t.lon, lat:t.Lat ?? t.lat
    })) : [];
    state.selectedLayers = new Set(msg.selectedLayers || []);
    state.selectedPointKeys = new Set(msg.selectedPointKeys || []);
    applyCoordinateSystems(msg.coordinateSystems, msg.sourceCrs);
    el('kmzDxfOptions')?.classList.remove('nf-hidden');
    el('kmzPointActions')?.classList.remove('nf-hidden');
    if(el('kmzPointsTitle')) el('kmzPointsTitle').textContent = 'Points importés';
    renderDxfLayers(msg.layers || []);
    refreshCombined();
    setStatus(`DXF : ${msg.fileName || 'chargé'} (${state.dxfPoints.length} pts, ${state.lines.length} traits, ${state.texts.length} textes)${state.txtPoints.length ? ' + TXT chargé' : ''}`, 'ok');
    setMapStatus(`Système : ${msg.sourceCrs} (${msg.detectionMethod || 'choix manuel'}).`);
    if(firstDxfLoad && state.txtPoints.length) post({ type:'kmz_reproject', sourceCrs:msg.sourceCrs });
  }

  function init(){
    const bImport = el('btnKmzImportTxt');
    const bImportDxf = el('btnKmzImportDxf');
    const bExport = el('btnKmzExport');
    const sel = el('kmzCoordSys');
    if(bImport) bImport.addEventListener('click', () => post({ type:'kmz_import_txt', sourceCrs: sel ? sel.value : '__AUTO__' }));
    if(bImportDxf) bImportDxf.addEventListener('click', () => post({ type:'kmz_import_dxf' }));
    if(bExport) bExport.addEventListener('click', () => {
      setStatus('Export KMZ en cours…');
      post(combinedExportPayload());
    });
    if(sel) sel.addEventListener('change', () => {
      if(state.txtPoints.length) post({ type:'kmz_reproject', sourceCrs: sel.value });
      if(state.dxfPoints.length) requestDxfPreview();
    });
    el('btnKmzLayersAll')?.addEventListener('click', () => {
      document.querySelectorAll('.kmz-layer-check').forEach(box => { box.checked = true; state.selectedLayers.add(box.dataset.layer); });
      requestDxfPreview();
    });
    el('btnKmzLayersNone')?.addEventListener('click', () => {
      document.querySelectorAll('.kmz-layer-check').forEach(box => { box.checked = false; });
      state.selectedLayers.clear(); requestDxfPreview();
    });
    el('btnKmzPointsAll')?.addEventListener('click', () => {
      state.txtPoints.forEach(p => state.selectedTxtKeys.add(String(p.key ?? p.Key)));
      state.dxfPoints.forEach(p => state.selectedPointKeys.add(String(p.Key ?? p.key ?? p.Id ?? p.id)));
      if(state.dxfPoints.length) requestDxfPreview();
      else refreshCombined();
    });
    el('btnKmzPointsNone')?.addEventListener('click', () => {
      state.selectedTxtKeys.clear();
      state.selectedPointKeys.clear(); requestDxfPreview();
      if(!state.dxfPoints.length) refreshCombined();
    });

    el('kmzNgfToggle')?.addEventListener('change', e => {
      state.ngfEnabled = !!e.target.checked;
      const refreshBtn = el('btnKmzNgfRefresh');
      if(refreshBtn) refreshBtn.disabled = !state.ngfEnabled;
      if(!state.ngfEnabled){
        state.ngfPoints = [];
        setNgfStatus('');
        state.drawingZone = false;
        state.zoneCorner1 = null;
        if(state.map){
          state.map.getContainer().style.cursor = '';
          if(state.zoneLayer){ state.map.removeLayer(state.zoneLayer); state.zoneLayer = null; }
          if(state.zonePreviewLayer){ state.map.removeLayer(state.zonePreviewLayer); state.zonePreviewLayer = null; }
        }
        updateDragLock();
      }
      renderMap();
      renderTable();
    });
    el('btnKmzNgfRefresh')?.addEventListener('click', () => {
      if(!state.map){
        setNgfStatus("Charge d'abord un TXT ou un DXF pour afficher la carte.");
        return;
      }
      // Mesurer et dessiner une zone se partagent le clic sur la carte : un outil
      // laisse forcement l'autre au repos, sinon un clic destine a l'un se retrouve
      // intercepte par l'etat (demi-termine) de l'autre.
      if(state.measuring){
        state.measuring = false;
        const measureBtn = el('btnKmzMeasureToggle');
        if(measureBtn) measureBtn.textContent = 'Mesurer une distance';
      }
      state.drawingZone = true;
      state.zoneCorner1 = null;
      state.map.getContainer().style.cursor = 'crosshair';
      updateDragLock();
      setNgfStatus('Clique un premier coin de la zone à charger.');
    });

    el('btnKmzMeasureToggle')?.addEventListener('click', () => {
      state.measuring = !state.measuring;
      const toggleBtn = el('btnKmzMeasureToggle');
      if(toggleBtn) toggleBtn.textContent = state.measuring ? 'Arrêter la mesure' : 'Mesurer une distance';
      if(state.map) state.map.getContainer().style.cursor = state.measuring ? 'crosshair' : '';
      if(state.measuring){
        // Meme raison que ci-dessus : un dessin de zone laisse en plan (1er coin
        // clique, jamais termine) interceptait le clic suivant destine a la mesure.
        state.drawingZone = false;
        state.zoneCorner1 = null;
        if(state.zonePreviewLayer && state.map){ state.map.removeLayer(state.zonePreviewLayer); state.zonePreviewLayer = null; }
        state.measurePoints = [];
        redrawMeasureLayer();
        setMeasureStatus('Clique sur la carte pour placer le premier point.');
        el('btnKmzMeasureClear')?.classList.remove('nf-space-hidden');
      }
      updateDragLock();
    });
    el('btnKmzMeasureClear')?.addEventListener('click', () => {
      state.measurePoints = [];
      redrawMeasureLayer();
      setMeasureStatus('');
      el('btnKmzMeasureClear')?.classList.add('nf-space-hidden');
    });

    el('kmzBasemap')?.addEventListener('change', e => {
      state.basemap = e.target.value;
      if(state.map){
        if(state.baseLayer) state.map.removeLayer(state.baseLayer);
        state.baseLayer = createTileLayer(state.basemap);
        state.baseLayer.addTo(state.map);
      }
    });

    el('btnKmzRecenter')?.addEventListener('click', () => {
      renderMap({ forceFit: true });
    });

    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function'){
        window.chrome.webview.addEventListener('message', ev => {
          const msg = ev && ev.data ? ev.data : ev;
          if(!msg || !msg.type) return;
          if(msg.type === 'kmz_txt_loaded') handleLoaded(msg);
          if(msg.type === 'kmz_dxf_loaded') handleDxfLoaded(msg);
          if(msg.type === 'kmz_ngf_loaded') handleNgfLoaded(msg);
          if(msg.type === 'kmz_error') setStatus('KMZ : erreur', 'err');
          if(msg.type === 'kmz_export_result'){
            if(msg.ok) setStatus(`KMZ exporte : ${msg.fileName || 'OK'}`, 'ok');
            else setStatus('KMZ : erreur export', 'err');
          }
        });
      }
    }catch(_){ }
  }

  if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
