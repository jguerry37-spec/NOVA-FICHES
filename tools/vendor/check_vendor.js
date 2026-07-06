const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

function sha256(p){
  const h = crypto.createHash("sha256");
  h.update(fs.readFileSync(p));
  return h.digest("hex");
}

function walk(dir, out){
  for(const name of fs.readdirSync(dir)){
    const p = path.join(dir,name);
    const st = fs.statSync(p);
    if(st.isDirectory()) walk(p,out);
    else if(/\.(js|html)$/i.test(name)) out.push(p);
  }
}

const assets = path.join(process.cwd(), "assets");
const versionsPath = path.join(process.cwd(), "assets", "vendor", "VERSIONS.json");

if(!fs.existsSync(assets)){
  console.error("❌ Dossier assets introuvable");
  process.exit(1);
}

const files=[];
walk(assets, files);
for(const f of files){
  const c = fs.readFileSync(f,"utf8");
  if(/https?:\/\//i.test(c)){
    console.error("❌ URL externe détectée (CDN interdit):", f);
    process.exit(1);
  }
}

if(!fs.existsSync(versionsPath)){
  console.error("❌ VERSIONS.json manquant:", versionsPath);
  process.exit(1);
}

const v = JSON.parse(fs.readFileSync(versionsPath,"utf8"));
if(!v.files){
  console.error("❌ VERSIONS.json invalide (files manquant)");
  process.exit(1);
}

for(const [name, expected] of Object.entries(v.files)){
  const p = path.join(process.cwd(), "assets", "vendor", name);
  if(!fs.existsSync(p)){
    console.error("❌ Vendor manquant:", name);
    process.exit(1);
  }
  if(expected){
    const actual = sha256(p);
    if(actual.toLowerCase() !== String(expected).toLowerCase()){
      console.error("❌ Hash vendor différent:", name);
      console.error("   attendu:", expected);
      console.error("   actuel :", actual);
      process.exit(1);
    }
  }
}

console.log("✅ Vendor OK — Offline garanti");
