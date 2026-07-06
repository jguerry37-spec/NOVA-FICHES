(()=>{ 
  try{
    const fb = document.getElementById("footerBuild");
    if(!fb) return;

    const raw = (window.APP_BUILD || window.__NF_BUILD || "DEV").toString();
    // Normalize: turn '1.7.35.0' into '1.7.35'
    const v = raw.replace(/\.0$/,'');

    // User-facing footer: keep it simple.
    fb.textContent = "Version " + v;
    const ab = document.getElementById("aboutBuild");
    if(ab) ab.textContent = v;
  }catch(e){}
})();

// About modal (footer link)
(()=>{
  try{
    const link = document.getElementById('aboutLink');
    const modal = document.getElementById('aboutModal');
    const closeBtn = document.getElementById('aboutClose');
    const vEl = document.getElementById('aboutVersion');
    const bEl = document.getElementById('aboutBuild');

    if(vEl){
      const v = (window.APP_VERSION || window.__NF_BUILD || window.APP_BUILD || '').toString().replace(/\.0$/,'');
      vEl.textContent = v;
    }
    if(bEl){
      const b = (window.APP_BUILD || window.__NF_BUILD || '').toString().replace(/\.0$/,'');
      bEl.textContent = b;
    }

    function open(){
      if(!modal) return;
      modal.style.display = 'flex';
    }
    function close(){
      if(!modal) return;
      modal.style.display = 'none';
    }

    if(link){
      link.addEventListener('click', (e)=>{ e.preventDefault(); open(); });
    }
    if(closeBtn){
      closeBtn.addEventListener('click', (e)=>{ e.preventDefault(); close(); });
    }
    if(modal){
      // click outside closes
      modal.addEventListener('click', (e)=>{ if(e.target === modal) close(); });
      // escape closes
      document.addEventListener('keydown', (e)=>{ if(e.key === 'Escape') close(); });
    }
  }catch(e){}
})();
