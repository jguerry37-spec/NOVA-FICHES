// Module "Paramètres" (manager, gated par data-nf-feature="branding" sur le bouton de nav) :
// personnalisation du logo / adresse de pied de page / couleurs utilisés dans tous les PDF
// générés sur ce poste. Persisté côté C# (BrandingService, %LOCALAPPDATA%), lu par
// InjectBranding() (MainForm) qui fusionne ces valeurs dans chaque payload PDF envoyé aux
// renderers PdfSharpEngine (NovatlasTheme.Resolve*).

(function initParametresModule(){
  const MAX_LOGO_BYTES = 2 * 1024 * 1024; // 2 Mo - doit rester cohérent avec BrandingService.cs
  const DEFAULT_BLUE = "#1267F3";
  const DEFAULT_ORANGE = "#FF5A17";

  const state = {
    logoPngBase64: null, // sans le préfixe data:...;base64,
    footerAddress: "",
    colorBlue: DEFAULT_BLUE,
    colorOrange: DEFAULT_ORANGE
  };

  function els(){
    return {
      file: document.getElementById("paramLogoFile"),
      preview: document.getElementById("paramLogoPreview"),
      status: document.getElementById("paramLogoStatus"),
      clearBtn: document.getElementById("btnParamLogoClear"),
      address: document.getElementById("paramFooterAddress"),
      colorBlue: document.getElementById("paramColorBlue"),
      colorOrange: document.getElementById("paramColorOrange"),
      saveBtn: document.getElementById("btnParamSave"),
      resetBtn: document.getElementById("btnParamReset"),
      paramStatus: document.getElementById("paramStatus")
    };
  }

  function setParamStatus(text, isError){
    const e = els();
    if(!e.paramStatus) return;
    e.paramStatus.textContent = text || "";
    e.paramStatus.style.color = isError ? "var(--err, #c0392b)" : "";
  }

  function applyLogoPreview(){
    const e = els();
    if(!e.preview || !e.status || !e.clearBtn) return;
    if(state.logoPngBase64){
      e.preview.src = "data:image/png;base64," + state.logoPngBase64;
      e.preview.classList.remove("nf-hidden");
      e.status.textContent = "Logo personnalisé chargé";
      e.clearBtn.classList.remove("nf-hidden");
    }else{
      e.preview.removeAttribute("src");
      e.preview.classList.add("nf-hidden");
      e.status.textContent = "Aucun logo personnalisé - logo NOVATLAS utilisé";
      e.clearBtn.classList.add("nf-hidden");
    }
  }

  function populateFromState(){
    const e = els();
    if(e.address) e.address.value = state.footerAddress || "";
    if(e.colorBlue) e.colorBlue.value = state.colorBlue || DEFAULT_BLUE;
    if(e.colorOrange) e.colorOrange.value = state.colorOrange || DEFAULT_ORANGE;
    applyLogoPreview();
  }

  function readStateFromFields(){
    const e = els();
    state.footerAddress = (e.address && e.address.value ? e.address.value : "").trim();
    state.colorBlue = (e.colorBlue && e.colorBlue.value) ? e.colorBlue.value : DEFAULT_BLUE;
    state.colorOrange = (e.colorOrange && e.colorOrange.value) ? e.colorOrange.value : DEFAULT_ORANGE;
  }

  function onLogoFileChange(ev){
    const file = ev.target && ev.target.files && ev.target.files[0];
    if(!file) return;

    if(file.type !== "image/png"){
      setParamStatus("Le logo doit être un fichier PNG.", true);
      ev.target.value = "";
      return;
    }
    if(file.size > MAX_LOGO_BYTES){
      setParamStatus("Logo trop volumineux (max 2 Mo).", true);
      ev.target.value = "";
      return;
    }

    const reader = new FileReader();
    reader.onload = function(){
      try{
        const dataUrl = String(reader.result || "");
        const idx = dataUrl.indexOf("base64,");
        if(idx < 0){ setParamStatus("Fichier logo illisible.", true); return; }
        state.logoPngBase64 = dataUrl.substring(idx + "base64,".length);
        applyLogoPreview();
        setParamStatus("");
      }catch(_){
        setParamStatus("Fichier logo illisible.", true);
      }
    };
    reader.onerror = function(){ setParamStatus("Fichier logo illisible.", true); };
    reader.readAsDataURL(file);
  }

  function onLogoClear(){
    state.logoPngBase64 = null;
    const e = els();
    if(e.file) e.file.value = "";
    applyLogoPreview();
  }

  function requestLoad(){
    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function"){
        window.chrome.webview.postMessage({ type: "params_load" });
      }
    }catch(_){ }
  }

  function onSave(){
    readStateFromFields();
    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function"){
        window.chrome.webview.postMessage({
          type: "params_save",
          logoPngBase64: state.logoPngBase64 || null,
          footerAddress: state.footerAddress || null,
          colorBlue: state.colorBlue || null,
          colorOrange: state.colorOrange || null
        });
        setParamStatus("Enregistrement…");
      }
    }catch(_){
      setParamStatus("Impossible d'enregistrer (erreur de communication).", true);
    }
  }

  function onReset(){
    state.logoPngBase64 = null;
    state.footerAddress = "";
    state.colorBlue = DEFAULT_BLUE;
    state.colorOrange = DEFAULT_ORANGE;
    populateFromState();
    onSave();
  }

  function wire(){
    const e = els();
    if(!e.saveBtn) return; // module absent de cette build/licence : rien à faire
    e.file && e.file.addEventListener("change", onLogoFileChange);
    e.clearBtn && e.clearBtn.addEventListener("click", onLogoClear);
    e.saveBtn.addEventListener("click", onSave);
    e.resetBtn && e.resetBtn.addEventListener("click", onReset);

    // Charge les paramètres actuels dès que l'onglet Paramètres est ouvert la première fois
    // (pas au chargement de la page : le module peut être masqué/non pertinent).
    const navBtn = document.querySelector('.nf-nav-item[data-target="module-parametres"]');
    if(navBtn){
      let loaded = false;
      navBtn.addEventListener("click", function(){
        if(loaded) return;
        loaded = true;
        requestLoad();
      });
    }
  }

  try{
    if(window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === "function"){
      window.chrome.webview.addEventListener("message", function(ev){
        const msg = (ev && ev.data) || {};
        if(!msg || typeof msg !== "object") return;
        const type = String(msg.type || "").toLowerCase();

        if(type === "params_loaded"){
          state.logoPngBase64 = msg.logoPngBase64 || null;
          state.footerAddress = msg.footerAddress || "";
          state.colorBlue = msg.colorBlue || DEFAULT_BLUE;
          state.colorOrange = msg.colorOrange || DEFAULT_ORANGE;
          populateFromState();
          return;
        }

        if(type === "params_save_result"){
          if(msg.ok){
            setParamStatus("Paramètres enregistrés.", false);
          }else{
            setParamStatus(String(msg.error || "Échec de l'enregistrement."), true);
          }
          return;
        }
      });
    }
  }catch(_){ }

  if(document.readyState === "loading"){
    document.addEventListener("DOMContentLoaded", wire);
  }else{
    wire();
  }
})();
