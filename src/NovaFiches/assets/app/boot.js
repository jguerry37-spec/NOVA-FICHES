// Nova-Fiches - Safety bootstrap (offline)
// Makes JS errors visible (avoid "buttons greyed" mystery) and logs to the WinForms host.

(function(){
  // Build/version injected by host (MainForm) as window.__NF_BUILD
  try{ window.APP_BUILD = (window.__NF_BUILD || window.APP_BUILD || "DEV").toString(); }catch(e){}

  function postToHost(payload){
    try{
      if(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function"){
        window.chrome.webview.postMessage(payload);
      }
    }catch(_){ /* ignore */ }
  }

  function disableAllActionButtons(){
    try{
      [
        "btnPdfIntervention",
        "btnPdfInterventionPdfSharp",
        "btnPdfLigneRef",
        "btnPdfStation",
        "btnPdfFull",
        "btnRecalc"
      ].forEach(id=>{
        const el = document.getElementById(id);
        if(el) el.disabled = true;
      });
    }catch(_){ /* ignore */ }
  }

  // IMPORTANT (Entreprise): ne jamais injecter de bandeau rouge en prod.
  // On loggue dans l'hôte WinForms + console, et on désactive les actions.
  function showFatal(msg){
    try{ console.error("Nova-Fiches JS fatal:", msg); }catch(_){ /* ignore */ }
  }

  function handleFatal(err){
    const msg = (err && (err.stack || err.message)) ? (err.stack || err.message) : String(err);
    // NOTE: en production on ne desactive pas les boutons sur une erreur JS globale (sinon impression de regression).
    showFatal(msg);
    postToHost({ type: "jsFatal", message: msg });
  }

  window.addEventListener("error", function(e){
    // Script errors in WebView2 can be opaque; surface them.
    handleFatal(e.error || e.message || "Erreur inconnue");
  });

  window.addEventListener("unhandledrejection", function(e){
    handleFatal(e.reason || "Promise rejection");
  });

  // Basic readiness self-check (non-fatal): helps catch mixed assets.
  document.addEventListener("DOMContentLoaded", function(){
    try{
      // We only warn to host; do not block.
      const required = []; // build comes from host via window.__NF_BUILD
      const missing = required.filter(k=>typeof window[k] === "undefined");
      if(missing.length){
        postToHost({ type: "jsWarn", message: "Missing globals: " + missing.join(", ") });
      }
    }catch(_){ /* ignore */ }
  });
})();
