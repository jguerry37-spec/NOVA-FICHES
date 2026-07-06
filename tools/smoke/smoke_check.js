/**
 * Smoke checks for Nova-Fiches assets (RF4)
 * - Verifies JS files parse (run via node --check separately)
 * - Verifies critical symbols exist across modules
 * - Verifies HTML includes expected script tags (optional)
 *
 * Usage:
 *   node tools/smoke/smoke_check.js
 */
const fs = require("fs");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..", "..");
const assetsDir = path.join(repoRoot, "src", "NovaFiches", "assets");
const appDir = path.join(assetsDir, "app");
const modulesDir = path.join(appDir, "modules");

function readText(p) {
  return fs.readFileSync(p, "utf8");
}

function listFiles(dir, exts) {
  const out = [];
  const stack = [dir];
  while (stack.length) {
    const d = stack.pop();
    for (const ent of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, ent.name);
      if (ent.isDirectory()) stack.push(p);
      else if (exts.some(e => p.toLowerCase().endsWith(e))) out.push(p);
    }
  }
  return out;
}

function assert(cond, msg) {
  if (!cond) {
    console.error("❌", msg);
    process.exitCode = 1;
  } else {
    console.log("✅", msg);
  }
}

console.log("Nova-Fiches RF4 smoke check");
console.log("Repo:", repoRoot);

const jsFiles = listFiles(appDir, [".js"]);
assert(jsFiles.length > 0, "Found JS files in assets/app");

const requiredSymbols = [
  "autoTableResults",
  "thickTableLinesDidDrawPage",
  "applyStatusFitAutoTable",
  "ensureStationButton",
];

let joined = "";
for (const p of jsFiles) {
  // only app + modules; avoid vendor
  if (p.includes(path.join("assets","app"))) {
    joined += "\n\n// FILE: " + path.relative(repoRoot, p) + "\n" + readText(p);
  }
}

for (const sym of requiredSymbols) {
  const re = new RegExp("\\b" + sym + "\\b");
  assert(re.test(joined), `Symbol present: ${sym}`);
}

// HTML script order check (best-effort)
const htmlPath = path.join(assetsDir, "topo_app.html");
if (fs.existsSync(htmlPath)) {
  const html = readText(htmlPath);
  // Accept both historical and current relative paths.
  const hasBoot = html.includes("assets/app/boot.js") || html.includes("./app/boot.js") || html.includes("app/boot.js");
  const hasInline03 = html.includes("assets/app/inline_03.js") || html.includes("./app/inline_03.js") || html.includes("app/inline_03.js");
  assert(hasBoot, "HTML includes boot.js");
  assert(hasInline03, "HTML includes inline_03.js");
  // Must include at least one module file
  const moduleRef = html.match(/(?:assets\/)?\.?(?:\/)?app\/modules\/m01_core\.js/);
  assert(!!moduleRef, "HTML includes modules (m01_core.js)");
} else {
  console.warn("⚠️ topo_app.html not found at", htmlPath);
}

if (process.exitCode) {
  console.error("\nSmoke check FAILED.");
  process.exit(1);
} else {
  console.log("\nSmoke check OK.");
}
