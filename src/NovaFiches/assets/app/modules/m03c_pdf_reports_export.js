function updatePdfSharpButtonState(){
  try{
    const okHost = !!lastData && isPdfSharpAvailable();
    const b1 = document.getElementById("btnPdfInterventionPdfSharp");
    if(b1){
      // Preserve content-based disabling (set elsewhere) and only enforce host capability.
      b1.disabled = b1.disabled || !okHost;
      b1.title = (!okHost)
        ? (!isPdfSharpAvailable() ? "PdfSharp indisponible (moteur non chargé côté C#)." : "Import LandXML requis.")
        : (b1.disabled ? "Aucune donnée Implantation détectée dans ce LandXML." : "Génère le PDF Implantation via PdfSharp (moteur C#)");
    }
    const b2 = document.getElementById("btnPdfCompletPdfSharp");
    if(b2){
      b2.disabled = b2.disabled || !okHost;
      b2.title = (!okHost)
        ? (!isPdfSharpAvailable() ? "PdfSharp indisponible (moteur non chargé côté C#)." : "Import LandXML requis.")
        : (b2.disabled ? "Rapport complet indisponible : données insuffisantes (Station + Implantation/Ligne de réf)." : "Génère le Rapport complet via PdfSharp (moteur C#)");
    }

    // Points topo is also generated via PdfSharp host
    const b3 = document.getElementById("btnPdfPointsTopo");
    if(b3){
      b3.disabled = b3.disabled || !okHost;
      b3.title = (!okHost)
        ? (!isPdfSharpAvailable() ? "PdfSharp indisponible (moteur non chargé côté C#)." : "Import LandXML requis.")
        : (b3.disabled ? "Aucun point topo (levé) détecté dans ce LandXML." : "Génère le PDF Points topo (levé) via PdfSharp (moteur C#)");
    }
  }catch(_){/* ignore */}
}
window.updatePdfSharpButtonState = updatePdfSharpButtonState;

document.getElementById("btnPdfInterventionPdfSharp")?.addEventListener("click", async () => {
  try{
    const pdfOverride = (window.__NF_IMPLANTATION_PDF_OVERRIDE && typeof window.__NF_IMPLANTATION_PDF_OVERRIDE === "object")
      ? window.__NF_IMPLANTATION_PDF_OVERRIDE
      : null;
    try{ delete window.__NF_IMPLANTATION_PDF_OVERRIDE; }catch(_){ window.__NF_IMPLANTATION_PDF_OVERRIDE = null; }
    if(!lastData){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }
    if(!isPdfSharpAvailable()){ setStatus("PdfSharp indisponible côté C#.", true); return; }
    setStatus("Génération du PDF en cours…");

    const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";
    const R = (typeof rf === "function") ? rf() : {};
    const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };
    const ex = nfExcludedForImplantLr_();
    const allImpPtsRaw = collectAllImplantPoints(lastData);
    const allImpPtsFixed = nfFixMissingImplantStationIds_(allImpPtsRaw, lastData);
    const allImpPts = nfFilterPointsById_(allImpPtsFixed, ex);
    const bodyImp = allImpPts.map(rowFromPoint);

    const implantationByStation = nfBuildImplantationByStationPayload_(allImpPts, lastData);
    const implantationStationIds = nfCollectUsedStationIdsFromPayload_(implantationByStation);
    const implantationRuns = nfFilterStationLibreRunsByStationIds_(lastData?.stationLibreRuns, implantationStationIds);

    const payload = {
      type: "pdfsharp_implantation",
      title: pdfOverride?.title || "IMPLANTATION",
      planView: pdfOverride?.planView || null,
      subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
      header: ["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"],
      rows: bodyImp,
      fileName: (typeof buildExportFileName === "function")
        ? buildExportFileName(pdfOverride?.fileNameType || "IMPLANTATION", "PDF")
        : "NOVA_Implantation_PdfSharp.pdf"
,
// Extra fields for PdfSharp full report (best-effort)
elements: (R && R.elements) ? R.elements : "",
entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : "",
ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
intervenant: getAutoIntervenant(R),
systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      // Instrument model + serial (LandXML: InstrumentDetails model="..." serialNumber="...")
      model: String((lastData && lastData.meta && lastData.meta.instrument) ? lastData.meta.instrument : "").trim(),
      serialNumber: String((lastData && lastData.meta && lastData.meta.serial) ? lastData.meta.serial : "").trim(),
      // Backward compatible field used by older PdfSharp renderers
      appareil: (()=>{
        const r = (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "";
        if(r) return r;
        const inst = String((lastData && lastData.meta && lastData.meta.instrument) ? lastData.meta.instrument : "").trim();
        return inst;
      })(),
      ...nfPdfInstrumentMeta_(lastData, R),
      ville: (R && R.ville) ? R.ville : "",
adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : "",
cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : "",

      // Validation / tolérances / observations (pour bloc bas de page PDF)
      validation: {
        statutOn: !!O.tolOn,
        tolXYOn: !!O.xyOn,
        tolZOn: !!O.zOn,
        tolXY: isFinite(O.tXY) ? O.tXY : null,
        tolZ: isFinite(O.tZ) ? O.tZ : null,
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
      },

      // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
      // NOTE: Les renderers PdfSharp lisent ces valeurs au niveau racine (compat).
      obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Intervenant: getAutoIntervenant(R),

      // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
      signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
      signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

      // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
      signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
      signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

stationLibre: (lastData && lastData.stationLibre) ? lastData.stationLibre : null,
      stationLibreRuns: nfSanitizeStationRunsForPdf_(implantationRuns),
      implantationByStation,
      groupByZone: nfPdfGroupByZoneEnabled_(),
      zoneLabels: nfGetZoneRenameMap(),
      refAltiPoints: nfCollectRefAltiPoints_(lastData)
    };

    if(pdfOverride && Object.prototype.hasOwnProperty.call(pdfOverride, "subTitle")){
      payload.subTitle = String(pdfOverride.subTitle || "");
    }

    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
      window.chrome.webview.postMessage(payload);
    else
      setStatus("WebView2 host indisponible (postMessage).", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF PdfSharp (implantation).", true);
    try{ if(window.chrome?.webview?.postMessage) window.chrome.webview.postMessage({type:"ui_error", message: String(err)}); }catch(_){ }
  }
});

// ===== PdfSharp TEST: Rapport complet (cover + tables) =====
document.getElementById("btnPdfCompletPdfSharp")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }
    setStatus("Génération du rapport complet en cours…");
    const R = (typeof rf === "function") ? rf() : {};
    const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };
    const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";

    // ---------- Build Implantation payload (same structure as btnPdfInterventionPdfSharp) ----------
    const ex = nfExcludedForImplantLr_();
    const allImpPtsRaw = collectAllImplantPoints(lastData);
    const allImpPtsFixed = nfFixMissingImplantStationIds_(allImpPtsRaw, lastData);
    const allImpPts = nfFilterPointsById_(allImpPtsFixed, ex);
    const bodyImp = allImpPts.map(p => rowFromPoint(p, lastData));

    const implantationByStation = nfBuildImplantationByStationPayload_(allImpPts, lastData);
    const implantationStationIds = nfCollectUsedStationIdsFromPayload_(implantationByStation);
    const implantationRuns = nfFilterStationLibreRunsByStationIds_(lastData?.stationLibreRuns, implantationStationIds);

    const impPayload = {
      type: "pdfsharp_implantation",
      title: "IMPLANTATION",
      subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
      header: ["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"],
      rows: bodyImp,
      implantationByStation,
      groupByZone: nfPdfGroupByZoneEnabled_(),
      zoneLabels: nfGetZoneRenameMap(),
      stationLibreRuns: nfSanitizeStationRunsForPdf_(implantationRuns),
      refAltiPoints: nfCollectRefAltiPoints_(lastData),

      // Cartouche / meta (same keys as other PdfSharp reports)
      // En PDF levé, on conserve le cartouche commun mais le libellé central doit être
      // celui du rapport, pas l'intitulé Implantation du projet.
      elements: "POINTS TOPO (LEVÉ)",
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : "",
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
      intervenant: getAutoIntervenant(R),
      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      // Instrument model + serial (LandXML: InstrumentDetails model="..." serialNumber="...")
      model: String(lastData?.meta?.instrument || "").trim(),
      serialNumber: String(lastData?.meta?.serial || "").trim(),
      appareil: (()=>{
        const r = (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "";
        if(r) return r;
        const inst = String(lastData?.meta?.instrument || "").trim();
        return inst;
      })(),
      ...nfPdfInstrumentMeta_(lastData, R),
      ville: (R && R.ville) ? R.ville : "",
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : "",
      cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : "",

      validation: {
        statutOn: !!O.tolOn,
        tolXYOn: !!O.xyOn,
        tolZOn: !!O.zOn,
        tolXY: isFinite(O.tXY) ? O.tXY : null,
        tolZ: isFinite(O.tZ) ? O.tZ : null,
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
      },
    };

    // ---------- Build Ligne de référence payload (same as btnPdfLigneRef) ----------
    const filteredLrForFull = nfFilterLigneRef_(Array.isArray(lastData?.ligneRef) ? lastData.ligneRef : [], nfExcludedSet_());
    const ligneRefRowsByStation = nfBuildLigneRefRowsByStationPayload_(filteredLrForFull, lastData);
    const ligneRefStationIds = nfCollectUsedStationIdsFromPayload_(ligneRefRowsByStation);
    const ligneRefRuns = nfFilterStationLibreRunsByStationIds_(lastData?.stationLibreRuns, ligneRefStationIds);

    const lrPayload = {
      type: "pdfsharp_ligne_reference",
      title: "LIGNE DE RÉFÉRENCE",
      subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
      elements: (R && R.elements) ? R.elements : "",
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem) : "",
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
      intervenant: getAutoIntervenant(R),

      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      appareil: (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "",
      ...nfPdfInstrumentMeta_(lastData, R),
      ville: (R && R.ville) ? R.ville : "",
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.address)) ? (R.adresseChantier || R.adresse || R.address) : "",
      cha: (R && (R.cha || R.numCha || R.noCha)) ? (R.cha || R.numCha || R.noCha) : "",
      refAltiPoints: nfCollectRefAltiPoints_(lastData),

      validation: {
        statutOn: !!O?.tolOn ? !!document.getElementById("optStatut")?.checked : !!document.getElementById("optStatut")?.checked,
        tolXYOn: !!document.getElementById("tolXYOn")?.checked,
        tolZOn: !!document.getElementById("tolZOn")?.checked,
        tolXY: Number(document.getElementById("tolXY")?.value || 0),
        tolZ: Number(document.getElementById("tolZ")?.value || 0),
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
      },

      // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
      obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Intervenant: getAutoIntervenant(R),

      // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
      signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
      signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

      stationLibreRuns: nfSanitizeStationRunsForPdf_(ligneRefRuns),
      ligneRef: filteredLrForFull,
      ligneRefRowsByStation,
      groupByZone: nfPdfGroupByZoneEnabled_(),
      zoneLabels: nfGetZoneRenameMap(),
    };

    // Cover info block (key/value)
    const info = {
      "Nom intervention": R.nomIntervention || "",
      "Ville": R.ville || "",
      "Adresse": R.adresse || "",
      "CHA": R.cha || "",
      "Phase": R.phase || "",
      "Type": (R.typeDoc || R.type || ""),
      "Indice": R.indice || "",
      "Entreprise": (R.client || R.entreprise || ""),
      "Client": R.client || "",
      "Intervenant": R.intervenant || "",
      "Date": R.dateIntervention || ""
    };

    const fileName = (typeof buildExportFileName === "function")
      ? buildExportFileName("", "pdf")
      : `${pdfFileBase(R)}_RapportComplet_PdfSharp.pdf`;

    if(window.chrome?.webview){
      window.chrome.webview.postMessage({
        type: "pdfsharp_rapport_complet_v2",
        fileName,
        info,
        implantationPayload: impPayload,
        ligneRefPayload: lrPayload
      });
      setStatus("Génération PDF (PdfSharp) …");
    } else {
      setStatus("Mode PdfSharp indisponible (WebView2 host)", true);
      showErrorDialog("Mode PdfSharp indisponible (WebView2 host)");
    }
  }catch(e){
    console.error(e);
    setStatus("Erreur PdfSharp : " + (e?.message || String(e)), true);
    showErrorDialog("Erreur PdfSharp : " + (e?.message || String(e)));
  }
});
// =====================================================
// exportPdf — point d'entrée unique (appelé par les boutons)
// =====================================================
// NOTE: lastData + setStatus() sont définis dans m01_core.js.

async function buildPdfPointsTopo(data){
  try{
    if(!data) throw new Error("Aucune donnée (import LandXML requis)");
    if(!isPdfSharpAvailable()) throw new Error("PdfSharp indisponible côté C#.");

    const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";
    const R = (typeof rf === "function") ? rf() : {};
    const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };

    // Règle métier NOVATLAS:
    // - pas de points issus d'un programme Stakeout (implantation)
    // - conserver les observations polaires + résultats rectangulaires
    //   (data.topoStations) pour le LEVÉ.
    const topoStationsLeve = nfFilterTopoStationsForLeve_(data);

    const payload = {
      type: "pdfsharp_points_topo",
      title: "LEVÉ",
      // The PdfSharp renderer expects this key and will default to IMPLANTATION if not provided.
      sectionImplantationTitle: "LEVÉ",
      // Observations polaires + résultats rectangulaires (topoStations) filtrés (sans Stakeout)
      topoStations: topoStationsLeve,
      subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
      header: ["ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT"],
      // IMPORTANT: ne pas injecter de points d'implantation dans le LEVÉ.
      rows: [],
      fileName: (typeof buildExportFileName === "function")
        ? buildExportFileName("LEVE", "PDF")
        : "NOVA_Leve_PdfSharp.pdf",

      // Cartouche / meta (same keys as Implantation PdfSharp)
      elements: (R && R.elements) ? R.elements : "",
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : "",
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
      intervenant: getAutoIntervenant(R),
      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      model: String((data && data.meta && data.meta.instrument) ? data.meta.instrument : "").trim(),
      serialNumber: String((data && data.meta && data.meta.serial) ? data.meta.serial : "").trim(),
      appareil: (()=>{
        const r = (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "";
        if(r) return r;
        const inst = String((data && data.meta && data.meta.instrument) ? data.meta.instrument : "").trim();
        return inst;
      })(),
      ...nfPdfInstrumentMeta_(data, R),
      ville: (R && R.ville) ? R.ville : "",
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : "",
      cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : "",

      validation: {
        statutOn: !!O.tolOn,
        tolXYOn: !!O.xyOn,
        tolZOn: !!O.zOn,
        tolXY: isFinite(O.tXY) ? O.tXY : null,
        tolZ: isFinite(O.tZ) ? O.tZ : null,
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
      },

      // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
      // NOTE: Les renderers PdfSharp lisent ces valeurs au niveau racine (compat).
      obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Intervenant: getAutoIntervenant(R),

      stationLibre: (data && data.stationLibre) ? data.stationLibre : null,
      stationLibreRuns: nfSanitizeStationRunsForPdf_((data && Array.isArray(data.stationLibreRuns)) ? data.stationLibreRuns : []),
      implantationByStation: [],
      refAltiPoints: nfCollectRefAltiPoints_(data)
    };

    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
      window.chrome.webview.postMessage(payload);
    else
      setStatus("WebView2 host indisponible (postMessage).", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF Points topo : " + (err?.message || err), true);
  }
}

async function buildPdfHeightTransfer(data){
  try{
    if(!data) throw new Error("Aucune donnée (import LandXML requis)");
    if(!isPdfSharpAvailable()) throw new Error("PdfSharp indisponible côté C#.");
    const transfers = Array.isArray(data?.heightTransfers) ? data.heightTransfers : [];
    if(!transfers.length) throw new Error("Aucun transfert d'altitude détecté.");
    const R = (typeof rf === "function") ? rf() : {};
    const payload = {
      type: "pdfsharp_height_transfer",
      title: "TRANSFERT D'ALTITUDE",
      fileName: (typeof buildExportFileName === "function") ? buildExportFileName("TRANSFERT_ALTITUDE", "PDF") : "NOVA_Transfert_Altitude.pdf",
      heightTransfers: transfers,
      elements: (R && R.elements) ? R.elements : "",
      entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
      contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
      systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.coordSystem) : "",
      ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
      intervenant: getAutoIntervenant(R),
      systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
      planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.reference || R.plan) : "",
      date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
      model: String(data?.meta?.instrument || "").trim(),
      serialNumber: String(data?.meta?.serial || "").trim(),
      appareil: (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : String(data?.meta?.instrument || "").trim(),
      ...nfPdfInstrumentMeta_(data, R),
      ville: (R && R.ville) ? R.ville : "",
      adresseChantier: (R && (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress)) ? (R.adresseChantier || R.adresse || R.siteAddress || R.site || R.siteAdress) : "",
      cha: (R && (R.cha || R.CHA)) ? (R.cha || R.CHA) : "",
      observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
      surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
      Intervenant: getAutoIntervenant(R)
    };
    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
      window.chrome.webview.postMessage(payload);
    else
      setStatus("WebView2 host indisponible (postMessage).", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF Transfert altitude : " + (err?.message || err), true);
  }
}

async function exportPdf(kind){
  try{
    // Robustesse: selon l'ordre de chargement / refactor, les donnees peuvent etre stockees
    // dans `lastData` (var globale) OU dans `window.lastData` OU `window.__NF_LASTDATA`.
    const data = (window.__NF_LASTDATA || window.lastData || (typeof lastData !== 'undefined' ? lastData : null));

    if(!data){
      setStatus("Aucune donnée à exporter (import TXT requis).", true);
      showErrorDialog("Aucune donnée à exporter (import TXT requis).");
      return;
    }

    // Retour visuel immédiat (audit V2 : le clic était muet jusqu'au résultat).
    // Pour les rapports PdfSharp, le statut sera mis à jour à nouveau par le
    // handler "pdf_result" (voir m01_core.js) une fois la génération C# terminée.
    setStatus("Génération du PDF en cours…");

    const k = String(kind || "").toLowerCase();
    if(k === "implantation" || k === "intervention") return await buildPdfIntervention(data);
    if(k === "ligne")        return await buildPdfLigneRef(data);
    if(k === "station"){
      // PdfSharp is now the official engine for Station as well. If available, use it.
      if(isPdfSharpAvailable()){
        const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";
        const R = (typeof rf === "function") ? rf() : {};
        const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };

        const payload = {
          type: "pdfsharp_station",
          title: "STATION",
          subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
          fileName: (typeof buildExportFileName === "function")
            ? buildExportFileName("STATION", "PDF")
            : "NOVA_Station_PdfSharp.pdf",

          // Cartouche / meta
          nomIntervention: "STATION",
          entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
          contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
          systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem) : "",
          ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
          intervenant: getAutoIntervenant(R),
          systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
          planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
          date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
          appareil: (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "",
          serialNumber: (R && (R.serialNumber || R.numeroSerie || R.serial)) ? (R.serialNumber || R.numeroSerie || R.serial) : "",
          ...nfPdfInstrumentMeta_(data, R),
          ville: (R && R.ville) ? R.ville : "",
          adresse: (R && (R.adresseChantier || R.adresse || R.address)) ? (R.adresseChantier || R.adresse || R.address) : "",
          cha: (R && (R.cha || R.numCha || R.noCha)) ? (R.cha || R.numCha || R.noCha) : "",

          validation: {
            statutOn: !!document.getElementById("optStatut")?.checked,
            tolXYOn: !!document.getElementById("tolXYOn")?.checked,
            tolZOn: !!document.getElementById("tolZOn")?.checked,
            tolXY: Number(document.getElementById("tolXY")?.value || 0),
            tolZ: Number(document.getElementById("tolZ")?.value || 0),
            observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
          },

          // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
          obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
          observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
          Observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
          surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Intervenant: getAutoIntervenant(R),

          // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
          signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
          signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

          // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
          signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
          signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

          // Core data
          stationLibreRuns: nfSanitizeStationRunsForPdf_(Array.isArray(data?.stationLibreRuns) ? data.stationLibreRuns : (Array.isArray(lastData?.stationLibreRuns) ? lastData.stationLibreRuns : [])),
          refAltiPoints: nfCollectRefAltiPoints_(data),
        };
        payload.stationLibre = Array.isArray(payload.stationLibreRuns) && payload.stationLibreRuns.length ? payload.stationLibreRuns[0] : null;

        if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
          window.chrome.webview.postMessage(payload);

        return;
      }
      


return await buildPdfStationOnly(data);
    }
    if(k === "station_txt")  return await buildPdfStationPlusPoints(data, (window.nfExchange&&window.nfExchange.txt?window.nfExchange.txt.points:[]), "TXT");
    if(k === "station_gsi"){
      const gsi = (window.nfExchange && window.nfExchange.gsi) ? window.nfExchange.gsi : null;
      const gPts = gsi && Array.isArray(gsi.points) ? gsi.points : [];
      const gObs = gsi && Array.isArray(gsi.observations) ? gsi.observations : [];
      const mode = gsi ? (gsi.gsiMode || null) : null;

      // Étape 4.2 (sans calcul) : si le GSI est en mode observations, on sort un PDF avec la table d'observations.
      if(mode === 'obs' && gObs.length){
        return await buildPdfStationPlusObservations(data, gObs);
      }
      return await buildPdfStationPlusPoints(data, gPts, "GSI");
    }
    if(k === "pointstopo")      return await buildPdfPointsTopo(data);
    if(k === "heighttransfer")  return await buildPdfHeightTransfer(data);
    if(k === "complet")      return await buildPdfComplet(data);

    setStatus("Type de PDF inconnu : " + kind, true);
    showErrorDialog("Type de PDF inconnu : " + kind);
  }catch(e){
    try{ setStatus("Erreur PDF : " + (e?.message || e)); }catch(_){ }
    console.error(e);
    setStatus("Erreur PDF : " + (e?.message || e), true);
    showErrorDialog("Erreur PDF : " + (e?.message || e));
  }
}

// exposer pour les appels depuis d'autres scripts / debug console
window.exportPdf = exportPdf;

// call once before wiring listeners
ensureStationButton();
document.getElementById("btnPdfIntervention")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import TXT)", true); return; }
    await exportPdf("intervention");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});

document.getElementById("btnPdfLigneRef")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }

    // PdfSharp is now the official engine. If available, use it.
    if(isPdfSharpAvailable()){
      const tolTxt = (typeof buildTolText === "function") ? buildTolText() : "";
      const R = (typeof rf === "function") ? rf() : {};
      const O = (typeof getOptions === "function") ? getOptions() : { tolOn:false, xyOn:true, zOn:true, tXY: NaN, tZ: NaN };

      const filteredLr = nfNormalizeLigneRefStationIds_(nfFilterLigneRef_(Array.isArray(lastData?.ligneRef) ? lastData.ligneRef : [], nfExcludedForImplantLr_()), lastData);
      const ligneRefRowsByStation = nfBuildLigneRefRowsByStationPayload_(filteredLr, lastData);
      const ligneRefStationIds = nfCollectUsedStationIdsFromPayload_(ligneRefRowsByStation);
      const ligneRefRuns = nfFilterStationLibreRunsByStationIds_(lastData?.stationLibreRuns, ligneRefStationIds);

      // Keep only station runs réellement liés à la fiche ligne de réf (source of truth is JSON -> C#)
      const payload = {
        type: "pdfsharp_ligne_reference",
        title: "LIGNE DE RÉFÉRENCE",
        subTitle: tolTxt ? ("Tolérances : " + tolTxt) : "",
        fileName: (typeof buildExportFileName === "function")
          ? buildExportFileName("LIGNEREF", "PDF")
          : "NOVA_LigneDeReference_PdfSharp.pdf",

        // Cartouche / meta
        elements: (R && R.elements) ? R.elements : "",
        entreprise: (R && (R.client || R.entreprise || R.ent)) ? (R.client || R.entreprise || R.ent) : "",
        contactClient: (R && (R.siteContact || R.contactClient || R.contact)) ? (R.siteContact || R.contactClient || R.contact) : "",
        systemeCoord: (R && (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem)) ? (R.sysCoord || R.systemeCoord || R.systemeCo || R.coordSystem) : "",
        ppm: (R && (R.ppm || R.PPM)) ? (R.ppm || R.PPM) : "",
        intervenant: getAutoIntervenant(R),

        systemeAlti: (R && (R.sysAlti || R.systemeAlti || R.altimetricSystem)) ? (R.sysAlti || R.systemeAlti || R.altimetricSystem) : "",
        planRef: (R && (R.planRef || R.plan || R.reference)) ? (R.planRef || R.plan || R.reference) : "",
        date: (R && (R.date || R.dateIntervention)) ? (R.date || R.dateIntervention) : "",
        appareil: (R && (R.appareil || R.instrument || R.app)) ? (R.appareil || R.instrument || R.app) : "",
        ...nfPdfInstrumentMeta_(lastData, R),
        ville: (R && R.ville) ? R.ville : "",
        adresseChantier: (R && (R.adresseChantier || R.adresse || R.address)) ? (R.adresseChantier || R.adresse || R.address) : "",
        cha: (R && (R.cha || R.numCha || R.noCha)) ? (R.cha || R.numCha || R.noCha) : "",

        // Validation bloc (same keys as IMP)
        validation: {
          statutOn: !!O?.tolOn ? !!document.getElementById("optStatut")?.checked : !!document.getElementById("optStatut")?.checked,
          tolXYOn: !!document.getElementById("tolXYOn")?.checked,
          tolZOn: !!document.getElementById("tolZOn")?.checked,
          tolXY: Number(document.getElementById("tolXY")?.value || 0),
          tolZ: Number(document.getElementById("tolZ")?.value || 0),
          observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : ""
        },

        // Champs UI (bloc final : OBSERVATIONS / RÉALISÉ PAR / VALIDÉ PAR)
        obs: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
        observations: (R && (R.obs || R.observations)) ? (R.obs || R.observations) : "",
        surveyor: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
        geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Geometre: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Utilisateur: (R && (R.surveyor || R.geometre)) ? (R.surveyor || R.geometre) : "",
          Intervenant: getAutoIntervenant(R),

          // Signature (Visa) — utilisée dans le bloc "RÉALISÉ PAR" (PdfSharp)
          signatureDataUrl: (typeof sigDataUrl !== "undefined" && sigDataUrl) ? String(sigDataUrl) : "",
          signatureImageType: (typeof sigImageType !== "undefined" && sigImageType) ? String(sigImageType) : "JPEG",

        // Core data
        stationLibreRuns: nfSanitizeStationRunsForPdf_(ligneRefRuns),
        refAltiPoints: nfCollectRefAltiPoints_(lastData),
        ligneRef: filteredLr,
        ligneRefRowsByStation,
        groupByZone: nfPdfGroupByZoneEnabled_(),
        zoneLabels: nfGetZoneRenameMap(),
      };

      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function")
        window.chrome.webview.postMessage(payload);

      return;
    }

    // Fallback (should not be used anymore, but keep a message)
    setStatus("PdfSharp indisponible côté C#. Impossible de générer le PDF.", true);
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF", true);
    setStatus(String(err?.message || err), true);
    showErrorDialog(String(err?.message || err));
  }
});

document.getElementById("btnPdfStation")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import TXT)", true); return; }
    await exportPdf("station");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});



document.getElementById("btnPdfPointsTopo")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }
    await exportPdf("pointstopo");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});
document.getElementById("btnPdfHeightTransfer")?.addEventListener("click", async () => {
  try{
    const data = (window.__NF_LASTDATA || window.lastData || (typeof lastData !== 'undefined' ? lastData : null));
    if(!data){ setStatus("Aucune donnée chargée (import LandXML)", true); return; }
    await exportPdf("heighttransfer");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF Transfert altitude: " + (err?.message || String(err)), true);
  }
});

document.getElementById("btnExportImplantTxt")?.addEventListener("click", () => {
  exportPointsTxt('implant_lr');
});

document.getElementById("btnExportLeveTxt")?.addEventListener("click", () => {
  exportPointsTxt('leve');
});

document.getElementById("btnPdfStationPlusTxt")?.addEventListener("click", async () => {
  try{
    const tPts = Array.isArray(window.nfExchange?.txt?.points) ? window.nfExchange.txt.points : [];
    if(!lastData){ setStatus("Veuillez importer un fichier AppLog avant de générer ce PDF.", true); return; }
    if(tPts.length===0){ setStatus("Aucun fichier TXT n’est chargé. Importez un fichier TXT (points XYZC).", true); return; }
    await exportPdf("station_txt");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});

document.getElementById("btnPdfStationPlusGsi")?.addEventListener("click", async () => {
  try{
    const gPts = Array.isArray(window.nfExchange?.gsi?.points) ? window.nfExchange.gsi.points : [];
    const gObs = Array.isArray(window.nfExchange?.gsi?.observations) ? window.nfExchange.gsi.observations : [];
    if(!lastData){ setStatus("Veuillez importer un fichier AppLog avant de générer ce PDF.", true); return; }
    if(gPts.length===0 && gObs.length===0){ setStatus("Aucun fichier GSI n’est chargé. Importez un fichier GSI Leica.", true); return; }
    await exportPdf("station_gsi");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});



document.getElementById("btnPdfFull")?.addEventListener("click", async () => {
  try{
    if(!lastData){ setStatus("Aucune donnée chargée", true); return; }
    if(typeof ensureFontsRegistered==="function"){ ensureFontsRegistered(); }
    // await exportPdf("complet") gère la création du document + le save()
    await exportPdf("complet");
  }catch(err){
    console.error(err);
    setStatus("Erreur PDF: " + (err?.message || String(err)), true);
  }
});
refreshTolWarnings();
document.getElementById("btnRecalc")?.addEventListener("click", async () => {
  if(!lastData){ setStatus("Aucune donnée chargée (import TXT)", true); return; }
  refreshAll();
});

// Tabs
document.querySelectorAll(".tab[data-view]").forEach(el => {
  el.addEventListener("click", async () => {
    document.querySelectorAll(".tab[data-view]").forEach(t => t.classList.remove("active"));
    el.classList.add("active");
    const view = el.getAttribute("data-view");
    ["station","implant","lineref","refalti","topo","heighttransfer","raw"].forEach(v => {
      const node = document.getElementById("view_"+v);
      if(node) node.style.display = (v === view) ? "block" : "none";
    });
  });
});

window.NF_afterRefresh = function(){
  try{ nfRenderZoneLabelsEditor_(); }catch(_){ }
};

document.getElementById('pdfGroupByZone')?.addEventListener('change', ()=>{
  try{ nfRenderZoneLabelsEditor_(); }catch(_){ }
});

try{ nfRenderZoneLabelsEditor_(); }catch(_){ }

["tolXYOn","tolZOn","tolXY","tolZ","optTol","calcDzOn"].forEach(id=>{ const el=document.getElementById(id); if(el){ el.addEventListener("change", ()=>{ refreshTolWarnings(); syncDzUI(); if(lastData){ refreshAll(); } }); el.addEventListener("input", ()=>{ refreshTolWarnings(); }); }});
refreshTolWarnings();
syncDzUI();

setStatus("Prêt");


/* ===== PDF COMBINÉ (implantation + rabattement) ===== */
