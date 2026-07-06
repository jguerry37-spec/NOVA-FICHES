  // If you run offline (no internet), copy the libs into ./vendor/ :
  // - jspdf.umd.min.js
  // - jspdf.plugin.autotable.min.js
  // - xlsx.min.js (xlsx-js-style build)
  window.__OFFLINE_VENDOR_HINT__ = function(){
    try{
      if(!window.jspdf || !window.jspdf.jsPDF){
        console.warn("[Topo][Offline] jsPDF missing. Add ./vendor/jspdf.umd.min.js or keep internet.");
      }
      if(!(window.jspdf && window.jspdf.jsPDF && window.jspdf.jsPDF.API && window.jspdf.jsPDF.API.autoTable)){
        console.warn("[Topo][Offline] autoTable missing. Add ./vendor/jspdf.plugin.autotable.min.js or keep internet.");
      }
      if(!window.XLSX){
        console.warn("[Topo][Offline] XLSX missing. Add ./vendor/xlsx.min.js or keep internet.");
      }
    }catch(e){}
  };
  