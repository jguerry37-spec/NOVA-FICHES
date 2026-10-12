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
    nvpEnabled: false,
    nvpPoints: [],
    drawingZone: false,
    zoneTarget: 'ngf', // 'ngf' | 'nvp' - quelle couche la zone en cours de dessin doit alimenter
    zoneCorner1: null,
    zonePreviewLayer: null,
    zoneLayer: null,
    searchPoint: null, // {lat, lon, displayName} - dernière adresse recherchée, affichée en épingle
    searchMarkerLayer: null,
    searchNeedsFit: false, // true juste après une recherche : force le recentrage sur searchPoint
    measuring: false,
    measurePoints: [],
    measureLayer: null,
    measureDistanceMeters: 0,
    measureTouchedReperes: [],
    measureTouchedNvp: []
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
  function setNvpStatus(text){ const s = el('kmzNvpStatus'); if(s) s.textContent = text; }
  // Pas de variante "erreur" (rouge) ici : une adresse introuvable ou un rappel d'activer
  // NGF/NVP ne sont pas des échecs bloquants, juste une invite à reformuler - voir
  // .kmz-address-status (couleur neutre, pleine largeur, hauteur libre).
  function setAddressStatus(text){ const s = el('kmzAddressStatus'); if(s) s.textContent = text; }
  function setMeasureStatus(text, cls){ const s = el('kmzMeasureStatus'); if(s){ s.textContent = text; s.className = 'kmz-measure-pill' + (cls ? ' ' + cls : ''); } }
  // Le micro-decalage au clic (carte qui "saute" pile au moment du premier clic en
  // mode Mesurer/Dessiner une zone) etait corrige en desactivant completement le
  // glisser dans ces modes - mais ca empechait aussi de deplacer la carte pour
  // atteindre un point hors champ pendant une mesure. Le vrai correctif est plus
  // haut, a la creation de la carte (clickTolerance releve sur state.map.dragging) :
  // cette fonction ne fait plus que s'assurer que le glisser reste actif, au cas ou
  // il aurait ete desactive ailleurs.
  function updateDragLock(){
    if(!state.map || !state.map.dragging) return;
    if(!state.map.dragging.enabled()) state.map.dragging.enable();
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
    // Plan IGN v2 (Géoplateforme, data.geopf.fr) : service public en accès libre, sans clé API -
    // préféré à OpenStreetMap direct (bloqué à plusieurs reprises : leur politique d'usage
    // interdit en principe de distribuer une application qui s'en sert, voir historique des
    // versions) et à un fournisseur commercial (MapTiler, essayé un temps - aurait demandé à
    // chaque utilisateur de créer un compte et de saisir une clé).
    return L.tileLayer('https:' + '//' + 'data.geopf.fr/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0'
        + '&LAYER=GEOGRAPHICALGRIDSYSTEMS.PLANIGNV2&STYLE=normal&FORMAT=image/png&TILEMATRIXSET=PM'
        + '&TILEMATRIX={z}&TILEROW={y}&TILECOL={x}', {
      maxZoom: 22,
      // Le Plan IGN v2 ne sert des tuiles réelles que jusqu'au zoom 19 (comme OSM) - sans ça,
      // Leaflet demanderait des tuiles z20-22 inexistantes au serveur. maxNativeZoom fait
      // sur-échantillonner les tuiles z19 pour les zooms au-delà au lieu de continuer à
      // interroger le serveur.
      maxNativeZoom: 19,
      attribution: '&copy; IGN-F/Géoportail'
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
    const hasNvp = state.nvpEnabled && state.nvpPoints.length > 0;
    const hasSearch = !!state.searchPoint;
    if(!pts.length && !lines.length && !texts.length && !hasNgf && !hasNvp && !hasSearch){
      if(mapDiv) mapDiv.style.display = 'block';
      if(canvas) canvas.style.display = 'none';
      setMapStatus('Charge un TXT, ou recherche une adresse, pour afficher la carte.');
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
          // Micro-mouvement inevitable entre l'appui et le relachement d'un "clic"
          // souris : Leaflet applique un seuil de tolerance interne (3px par defaut,
          // tres bas) avant de considerer un geste comme un vrai glisser de carte.
          // Le relever evite qu'un simple clic (Mesurer, Dessiner une zone) ne
          // deplace deja legerement la vue avant meme d'etre classe comme un clic,
          // sans empecher un vrai glisser (mouvement nettement plus grand) de
          // fonctionner. Remplace l'ancienne desactivation totale du glisser pendant
          // Mesurer/Dessiner une zone (voir updateDragLock), qui bloquait tout
          // deplacement de la carte dans ces modes.
          if(state.map.dragging && state.map.dragging._draggable){
            state.map.dragging._draggable.options.clickTolerance = 8;
          }
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
          state.ngfPoints.forEach(p => {
            const lat = Number(p.lat), lon = Number(p.lon);
            if(!Number.isFinite(lat) || !Number.isFinite(lon)) return;
            // "BON ETAT" (ou etat absent/inconnu) -> triangle bleu ; tout le reste (MAUVAIS ETAT,
            // NON RETROUVE, IMPRENABLE, PRESUME DEPLACE...) -> rouge, valeurs reelles confirmees
            // via le flux WFS IGN (IGNF_GEODESIE:rn).
            const etatUpper = String(p.etat || '').toUpperCase();
            const isBad = etatUpper !== '' && etatUpper !== 'BON ETAT';
            const ngfIcon = L.divIcon({
              className: 'kmz-ngf-icon',
              html: `<div class="kmz-ngf-triangle-outer"><div class="kmz-ngf-triangle-inner${isBad ? ' kmz-ngf-triangle-inner--bad' : ''}"></div></div>`,
              iconSize: [20, 17],
              iconAnchor: [10, 9]
            });
            const marker = L.marker([lat, lon], { icon: ngfIcon });
            const label = String(p.nom || p.id || '');
            marker.bindTooltip(label, { permanent:false, direction:'top' });
            const altValue = (p.altitude !== null && p.altitude !== undefined && p.altitude !== '') ? Number(p.altitude) : NaN;
            const alt = Number.isFinite(altValue) ? altValue.toFixed(3) + ' m' : 'inconnue';
            const ficheLink = p.ficheUrl
              ? `<br><a href="${esc(p.ficheUrl)}" target="_blank" rel="noopener">Télécharger la fiche (PDF)</a>`
              : '';
            marker.bindPopup(`<b>${esc(label)}</b><br>Altitude NGF ${esc(alt)}<br>${esc(p.etat || '')}${ficheLink}`);
            // En mode Mesurer, un clic sur un repère demarre/poursuit la mesure exactement depuis
            // ses coordonnees (au lieu d'ouvrir sa fiche) - voir onMeasureRepereClick. closePopup()
            // annule l'ouverture automatique liee a bindPopup() (meme tick, avant tout rendu visible).
            marker.on('click', () => {
              if(state.measuring){
                marker.closePopup();
                onMeasureRepereClick(p);
              }
            });
            state.layer.addLayer(marker);
          });
        }
        if(hasNvp){
          const nvpIcon = L.divIcon({
            className: 'kmz-nvp-icon',
            html: '<div class="kmz-nvp-diamond"></div>',
            iconSize: [20, 20],
            iconAnchor: [10, 10]
          });
          state.nvpPoints.forEach(p => {
            const lat = Number(p.lat), lon = Number(p.lon);
            if(!Number.isFinite(lat) || !Number.isFinite(lon)) return;
            const marker = L.marker([lat, lon], { icon: nvpIcon });
            const label = String(p.numero || '');
            marker.bindTooltip(label, { permanent:false, direction:'top' });
            const nvpVal = Number(p.altitudeNvp);
            const ignVal = Number(p.altitudeIgn69);
            const altNvpTxt = Number.isFinite(nvpVal) ? nvpVal.toFixed(3) + ' m' : 'inconnue';
            const altIgnTxt = Number.isFinite(ignVal) ? ignVal.toFixed(3) + ' m' : 'inconnue';
            marker.bindPopup(
              `<b>${esc(label)}</b> — ${esc(p.denomination || '')}<br>` +
              `Altitude NVP ${esc(altNvpTxt)}<br>` +
              `Altitude NGF-IGN69 ${esc(altIgnTxt)}<br>` +
              `${esc(p.typ || '')}`
            );
            // Même principe que les repères NGF : en mode Mesurer, un clic sur le repère
            // démarre/poursuit la mesure depuis ses coordonnées exactes au lieu d'ouvrir sa
            // popup - voir onMeasureNvpClick. Pas de fiche PDF à joindre ici (la Ville de Paris
            // n'en publie pas pour ces repères, contrairement à l'IGN).
            marker.on('click', () => {
              if(state.measuring){
                marker.closePopup();
                onMeasureNvpClick(p);
              }
            });
            state.layer.addLayer(marker);
          });
        }
        state.layer.addTo(state.map);
        if(hasSearch){
          const sp = state.searchPoint;
          if(state.searchMarkerLayer){ state.map.removeLayer(state.searchMarkerLayer); state.searchMarkerLayer = null; }
          const searchIcon = L.divIcon({
            className: 'kmz-search-icon',
            html: '<div class="kmz-search-pin"></div>',
            iconSize: [22, 30],
            iconAnchor: [11, 30]
          });
          state.searchMarkerLayer = L.marker([sp.lat, sp.lon], { icon: searchIcon, zIndexOffset: 1000 });
          state.searchMarkerLayer.bindTooltip(sp.displayName || 'Adresse recherchée', { permanent:false, direction:'top' });
          state.searchMarkerLayer.addTo(state.map);
        }
        const bounds = state.layer.getBounds();
        const needsSearchFit = hasSearch && state.searchNeedsFit;
        const fitImportedData = () => {
          try{
            state.map.invalidateSize();
            // Une recherche d'adresse fraîche recentre toujours la vue sur le point trouvé,
            // même si d'autres données (points importés, repères déjà chargés) ont par
            // ailleurs une emprise valide - sinon l'utilisateur chercherait une adresse sans
            // jamais voir la carte s'y déplacer.
            if(needsSearchFit) state.map.setView([state.searchPoint.lat, state.searchPoint.lon], 18);
            else if(bounds && bounds.isValid()) state.map.fitBounds(bounds.pad(0.20), { maxZoom: 20 });
          }catch(_){}
        };
        if(isNewMap || forceFit || needsSearchFit){
          fitImportedData();
          setTimeout(fitImportedData, 120);
        }else{
          try{ state.map.invalidateSize(); }catch(_){}
        }
        state.searchNeedsFit = false;
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

  // state.zoneTarget distingue quelle couche la zone en cours de dessin doit alimenter (NGF ou
  // NVP) - le dessin de zone lui-même (drawingZone/zoneCorner1/zonePreviewLayer/zoneLayer) reste un
  // mécanisme unique et partagé, un seul outil de zone pouvant être actif à la fois.
  function zoneColor(){ return state.zoneTarget === 'nvp' ? '#9c36b5' : '#1c3fdc'; }
  function zoneStatusSetter(){ return state.zoneTarget === 'nvp' ? setNvpStatus : setNgfStatus; }

  function drawZoneLayer(bounds){
    if(!state.map) return;
    if(state.zoneLayer){ state.map.removeLayer(state.zoneLayer); state.zoneLayer = null; }
    state.zoneLayer = L.rectangle(bounds, { color:zoneColor(), weight:2, fillOpacity:0.03, dashArray:'4,4' }).addTo(state.map);
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

  function fetchNvpForBounds(bounds){
    setNvpStatus('Chargement des repères NVP…');
    post({
      type: 'kmz_fetch_nvp',
      minLon: bounds.getWest(),
      minLat: bounds.getSouth(),
      maxLon: bounds.getEast(),
      maxLat: bounds.getNorth()
    });
  }

  // Rayon de recherche fixe (pas de réglage utilisateur, pour rester simple) autour d'une adresse
  // géocodée - suffisant pour retrouver les repères NGF/NVP les plus proches sans avoir à dessiner
  // une zone à la main. Convertit un rayon en mètres en une boîte englobante lat/lon (approximation
  // sphérique standard, largement suffisante à cette échelle - pas de reprojection nécessaire
  // puisqu'on part déjà de coordonnées WGS84 lon/lat).
  const ADDRESS_SEARCH_RADIUS_M = 300;
  function bboxFromPoint(lat, lon, radiusMeters){
    const latDelta = radiusMeters / 111320;
    const lonDelta = radiusMeters / ((111320 * Math.cos(lat * Math.PI / 180)) || 1);
    return L.latLngBounds([lat - latDelta, lon - lonDelta], [lat + latDelta, lon + lonDelta]);
  }

  function geocodeAddress(){
    const input = el('kmzAddressInput');
    const address = input ? input.value.trim() : '';
    if(!address){
      setAddressStatus('Saisis une adresse.');
      return;
    }
    setAddressStatus('Recherche en cours…');
    post({ type: 'kmz_geocode_address', address });
  }

  async function handleGeocodeResult(msg){
    if(!msg || !msg.ok){
      setAddressStatus(String((msg && msg.error) || 'Adresse introuvable, essaie une formulation plus complète (ex. "75001 Paris" plutôt que "Paris 01").'));
      return;
    }
    const lat = Number(msg.lat), lon = Number(msg.lon);
    if(!Number.isFinite(lat) || !Number.isFinite(lon)){
      setAddressStatus('Adresse introuvable, essaie une formulation plus complète.');
      return;
    }
    state.searchPoint = { lat, lon, displayName: msg.displayName || '' };
    state.searchNeedsFit = true;
    const ok = await loadLeaflet();
    if(!ok){
      setAddressStatus("Adresse trouvée, mais la carte (Leaflet) n'a pas pu se charger.");
      return;
    }
    await renderMap();
    const wantNgf = state.ngfEnabled, wantNvp = state.nvpEnabled;
    if(!wantNgf && !wantNvp){
      setAddressStatus((msg.displayName ? `Trouvé : ${msg.displayName}. ` : 'Adresse trouvée. ') +
        'Coche "NGF" ou "NVP" pour charger les repères autour.');
      return;
    }
    const bounds = bboxFromPoint(lat, lon, ADDRESS_SEARCH_RADIUS_M);
    drawZoneLayer(bounds);
    if(wantNgf) fetchNgfForBounds(bounds);
    if(wantNvp) fetchNvpForBounds(bounds);
    setAddressStatus(msg.displayName ? `Trouvé : ${msg.displayName}` : 'Adresse trouvée.');
  }

  function onZoneMouseMove(ev){
    if(!state.drawingZone || !state.zoneCorner1 || !state.map) return;
    const bounds = L.latLngBounds(state.zoneCorner1, ev.latlng);
    if(state.zonePreviewLayer) state.map.removeLayer(state.zonePreviewLayer);
    state.zonePreviewLayer = L.rectangle(bounds, { color:zoneColor(), weight:2, dashArray:'4,4', fillOpacity:0.05 }).addTo(state.map);
  }

  function onZoneMapClick(ev){
    if(!state.zoneCorner1){
      state.zoneCorner1 = ev.latlng;
      zoneStatusSetter()('Clique le second coin de la zone à charger.');
      return;
    }
    const bounds = L.latLngBounds(state.zoneCorner1, ev.latlng);
    state.drawingZone = false;
    state.zoneCorner1 = null;
    if(state.map) state.map.getContainer().style.cursor = '';
    if(state.zonePreviewLayer){ state.map.removeLayer(state.zonePreviewLayer); state.zonePreviewLayer = null; }
    updateDragLock();
    drawZoneLayer(bounds);
    if(state.zoneTarget === 'nvp') fetchNvpForBounds(bounds);
    else fetchNgfForBounds(bounds);
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

  function handleNvpLoaded(msg){
    state.nvpPoints = Array.isArray(msg.points) ? msg.points.map(p => ({
      numero: p.numero ?? p.Numero ?? '',
      denomination: p.denomination ?? p.Denomination ?? '',
      typ: p.typ ?? p.Typ ?? null,
      altitudeNvp: p.altitudeNvp ?? p.AltitudeNvp ?? null,
      altitudeIgn69: p.altitudeIgn69 ?? p.AltitudeIgn69 ?? null,
      lon: p.lon ?? p.Lon,
      lat: p.lat ?? p.Lat
    })) : [];
    setNvpStatus(`${state.nvpPoints.length} repère(s) NVP chargé(s) sur la zone visible.`);
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

  // suffixMsg optionnel : ajoute une confirmation ("repere touche") au message de statut sans
  // ecraser l'affichage de la distance - utilise par onMeasureRepereClick ci-dessous.
  function addMeasurePoint(lat, lon, suffixMsg){
    state.measurePoints.push([lat, lon]);
    redrawMeasureLayer();
    let statusMsg;
    if(state.measurePoints.length < 2){
      statusMsg = 'Clique un second point pour mesurer.';
    }else{
      let total = 0;
      for(let i = 1; i < state.measurePoints.length; i++){
        total += state.map.distance(state.measurePoints[i-1], state.measurePoints[i]);
      }
      state.measureDistanceMeters = total;
      statusMsg = `Distance : ${fmtDistance(total)}`;
      el('btnKmzMeasureExportPdf')?.classList.remove('nf-space-hidden');
    }
    setMeasureStatus(suffixMsg ? `${statusMsg} — ${suffixMsg}` : statusMsg);
  }

  function onMeasureMapClick(ev){
    if(!state.measuring) return;
    addMeasurePoint(ev.latlng.lat, ev.latlng.lng);
  }

  // Clic sur un repere NGF pendant une mesure : demarre/poursuit le tracé depuis ses coordonnees
  // exactes (pas le pixel cliqué sur le triangle) et retient sa fiche PDF pour la joindre à
  // l'export - une même mesure peut toucher plusieurs repères (début, fin, ou point
  // intermédiaire), tous sont conservés (dédoublonnés par ficheUrl).
  function onMeasureRepereClick(p){
    if(!state.measuring) return;
    const lat = Number(p.lat), lon = Number(p.lon);
    if(!Number.isFinite(lat) || !Number.isFinite(lon)) return;
    const label = String(p.nom || p.id || 'repère');
    let suffix = `repère NGF "${label}" sur le tracé`;
    if(p.ficheUrl){
      if(!state.measureTouchedReperes.some(r => r.ficheUrl === p.ficheUrl)){
        state.measureTouchedReperes.push({ nom: label, ficheUrl: p.ficheUrl });
      }
      suffix += ' (fiche jointe à l\'export)';
    }
    addMeasurePoint(lat, lon, suffix);
  }

  // Même principe que onMeasureRepereClick (NGF), sans la partie fiche PDF : la Ville de Paris ne
  // publie pas de fiche téléchargeable pour ces repères. À la place, l'info de la popup (numéro,
  // dénomination, les deux altitudes, état) est retenue pour être imprimée en texte dans l'export
  // PDF de la mesure (voir measureTouchedNvp / buildExportPayload ci-dessous).
  function onMeasureNvpClick(p){
    if(!state.measuring) return;
    const lat = Number(p.lat), lon = Number(p.lon);
    if(!Number.isFinite(lat) || !Number.isFinite(lon)) return;
    const label = String(p.numero || 'repère NVP');
    if(!state.measureTouchedNvp.some(r => r.numero === p.numero)){
      state.measureTouchedNvp.push({
        numero: p.numero || '',
        denomination: p.denomination || '',
        altitudeNvp: p.altitudeNvp ?? null,
        altitudeIgn69: p.altitudeIgn69 ?? null,
        typ: p.typ || ''
      });
    }
    addMeasurePoint(lat, lon, `repère NVP "${label}" sur le tracé (info jointe à l'export)`);
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
      ngfPoints: state.ngfEnabled ? state.ngfPoints : [],
      nvpPoints: state.nvpEnabled ? state.nvpPoints : []
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
      state.zoneTarget = 'ngf';
      state.drawingZone = true;
      state.zoneCorner1 = null;
      state.map.getContainer().style.cursor = 'crosshair';
      updateDragLock();
      setNgfStatus('Clique un premier coin de la zone à charger.');
    });

    el('kmzNvpToggle')?.addEventListener('change', e => {
      state.nvpEnabled = !!e.target.checked;
      const refreshBtn = el('btnKmzNvpRefresh');
      if(refreshBtn) refreshBtn.disabled = !state.nvpEnabled;
      if(!state.nvpEnabled){
        state.nvpPoints = [];
        setNvpStatus('');
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
    el('btnKmzNvpRefresh')?.addEventListener('click', () => {
      if(!state.map){
        setNvpStatus("Charge d'abord un TXT ou un DXF pour afficher la carte.");
        return;
      }
      if(state.measuring){
        state.measuring = false;
        const measureBtn = el('btnKmzMeasureToggle');
        if(measureBtn) measureBtn.textContent = 'Mesurer une distance';
      }
      state.zoneTarget = 'nvp';
      state.drawingZone = true;
      state.zoneCorner1 = null;
      state.map.getContainer().style.cursor = 'crosshair';
      updateDragLock();
      setNvpStatus('Clique un premier coin de la zone à charger.');
    });

    el('btnKmzAddressSearch')?.addEventListener('click', geocodeAddress);
    el('kmzAddressInput')?.addEventListener('keydown', e => {
      if(e.key === 'Enter'){ e.preventDefault(); geocodeAddress(); }
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
        state.measureDistanceMeters = 0;
        state.measureTouchedReperes = [];
        state.measureTouchedNvp = [];
        redrawMeasureLayer();
        setMeasureStatus('Clique sur la carte pour placer le premier point.');
        el('btnKmzMeasureClear')?.classList.remove('nf-space-hidden');
        el('btnKmzMeasureExportPdf')?.classList.add('nf-space-hidden');
      }
      updateDragLock();
    });
    el('btnKmzMeasureClear')?.addEventListener('click', () => {
      state.measurePoints = [];
      state.measureDistanceMeters = 0;
      state.measureTouchedReperes = [];
      state.measureTouchedNvp = [];
      redrawMeasureLayer();
      setMeasureStatus('');
      el('btnKmzMeasureClear')?.classList.add('nf-space-hidden');
      el('btnKmzMeasureExportPdf')?.classList.add('nf-space-hidden');
    });
    el('btnKmzMeasureExportPdf')?.addEventListener('click', () => {
      if(!state.measureDistanceMeters || state.measurePoints.length < 2){
        setMeasureStatus('Mesure au moins 2 points avant d’exporter.');
        return;
      }
      // Repères NGF + points topo transmis pour affichage en contexte sur la carte exportée, et
      // fiches PDF des repères touchés pendant le tracé pour fusion dans le PDF final (voir
      // ExportKmzMeasurePdfForUiAsync / NgfFicheMergeService.MergeFicheUrlsAsync côté C#).
      post({
        type: 'kmz_export_measure_pdf',
        distanceMeters: state.measureDistanceMeters,
        points: state.measurePoints,
        basemap: el('kmzBasemap')?.value || 'plan',
        ngfPoints: state.ngfEnabled ? state.ngfPoints : [],
        nvpPoints: state.nvpEnabled ? state.nvpPoints : [],
        topoPoints: (state.points || [])
          .map(p => ({ id: p.id ?? p.Id ?? '', lat: Number(p.lat ?? p.Lat), lon: Number(p.lon ?? p.Lon) }))
          .filter(p => Number.isFinite(p.lat) && Number.isFinite(p.lon)),
        ngfFicheUrls: state.measureTouchedReperes.map(r => r.ficheUrl),
        nvpInfo: state.measureTouchedNvp
      });
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
          if(msg.type === 'kmz_nvp_loaded') handleNvpLoaded(msg);
          if(msg.type === 'kmz_geocode_result') handleGeocodeResult(msg);
          if(msg.type === 'kmz_error') setStatus('KMZ : erreur', 'err');
          if(msg.type === 'kmz_export_result'){
            if(msg.ok) setStatus(`KMZ exporte : ${msg.fileName || 'OK'}`, 'ok');
            else setStatus('KMZ : erreur export', 'err');
          }
          if(msg.type === 'kmz_measure_pdf_result'){
            setMeasureStatus(msg.ok ? `PDF enregistré : ${msg.fileName || 'OK'}` : String(msg.error || "Échec de l'export PDF."), msg.ok ? 'ok' : '');
          }
        });
      }
    }catch(_){ }
  }

  if(document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
