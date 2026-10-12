# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Nova-Fiches: a Windows-only .NET 8 WinForms desktop app (`AssemblyName` `NovaFiches`, `RootNamespace` `TopoRapportWin`) for topographic report generation. The UI is a WebView2 control hosting a fully offline local HTML/JS single-page app; PDF generation is a separate class library built on PDFsharp-GDI. There is no top-level `.sln`/`.slnx` — each project is built/run independently by path.

Stack is intentionally fixed: Windows-only, .NET 8 WinForms, WebView2, offline local HTML/JS UI (no external CDN/network calls from the UI layer — enforced by `check_vendor.ps1`, see below).

## Projects

- `src/NovaFiches/TopoRapportWin.csproj` — the WinForms app (`OutputType=WinExe`). Hosts WebView2, contains all `*Service.cs` business logic (import/export/analysis) and PDF payload assembly in `MainForm.cs`.
- `src/NovaFiches.PdfSharpEngine/NovaFiches.PdfSharpEngine.csproj` — PDF rendering engine (PDFsharp-GDI / `XGraphics`), one renderer class per report type.
- `src/NovaFiches.Tests/NovaFiches.Tests.csproj` — xUnit tests. Both other projects declare `<InternalsVisibleTo Include="NovaFiches.Tests" />`, so tests call `internal` classes/methods directly (e.g. `ControlePrecisionRenderer` is `internal static class`).
- `tools/license-gen/LicenseGen.csproj` — internal console tool that generates the offline license keypair/files. Not shipped with the app.

## Common commands

Build a single project (no solution file, always target the `.csproj` directly):
```
dotnet build src/NovaFiches/TopoRapportWin.csproj
```

Run the full test suite:
```
dotnet test src/NovaFiches.Tests/NovaFiches.Tests.csproj
```

Run a single test (xUnit `--filter`, by fully qualified name or a substring):
```
dotnet test src/NovaFiches.Tests/NovaFiches.Tests.csproj --filter "FullyQualifiedName~ControlePrecisionRendererTests"
```

Vendor/offline check (must pass after any change under `src/NovaFiches/assets/**/*.js` or `*.html`) — fails if any `http://`/`https://` string appears outside `assets/vendor/`, and verifies `assets/vendor/VERSIONS.json` SHA256 hashes against the actual vendor files:
```
pwsh tools/vendor/check_vendor.ps1
```

Smoke check (requires Node.js in PATH — `node --check` on every file under `assets/app/**/*.js`, then a symbol/HTML consistency check):
```
pwsh tools/smoke/smoke_check.ps1
```

Release build + publish (framework-dependent, win-x64 only) — from repo root:
```
pwsh BUILD_RELEASE.ps1
```
This calls `src/NovaFiches/build.ps1 -Configuration Release`, which runs `dotnet publish TopoRapportWin.csproj -c Release -r win-x64 --self-contained true -o Installer\staging\publish`. Self-contained means the .NET 8 runtime is embedded in the publish output — neither installer (Inno or MSI) needs to detect or deploy it separately, only the WebView2 runtime is still checked (see MSI launch condition in `Installer/msi/NovaFiches.wxs`). `build.ps1` also accepts `-Version` to inject `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` via MSBuild properties without touching the csproj.

Installer packaging (Inno Setup — despite older docs mentioning Advanced Installer/MSI, this is the actual working pipeline for the consumer-facing installer):
```
& "C:\Program Files\Inno Setup 7\ISCC.exe" "/DMyAppVersion=<version>" "NovaFiches_2.3.0.100.iss"
```
Produces `Installer\out\NovaFiches_Setup_<version>.exe`. Requires the publish step above to have populated `Installer\staging\publish` first, and `NovaFiches.exe` must not be running (it locks the build output — check with `tasklist /FI "IMAGENAME eq NovaFiches.exe"`).

Enterprise MSI packaging (`Installer/msi/`, own README with full GPO deployment guide) — separate from the Inno Setup `.exe` above because GPO Software Installation only accepts `.msi`: built with [WiX Toolset](https://wixtoolset.org/) v6 (`dotnet tool install --global wix`), source at `Installer/msi/NovaFiches.wxs`, wrapped by:
```
pwsh Installer\build_msi.ps1 -Version <version>
```
Also requires `Installer\staging\publish` populated first (same publish step). Produces `Installer\out\NovaFiches_Setup_<version>.msi`. Silent install/uninstall via `msiexec /i|/x ... /quiet`; version upgrades are automatic (`MajorUpgrade`, stable `UpgradeCode` derived from the `Id="NOVATLAS.NovaFiches"` package identifier in the `.wxs` — never change that identifier). No embedded WebView2 bootstrap (unlike the Inno installer) — GPO-deployed installs run under SYSTEM with no guaranteed network access, so the MSI instead hard-blocks installation with a clear message if the WebView2 Runtime isn't already present (see `Installer/msi/README.md`).

License tooling (`tools/license-gen`, own README with full details): `genkey` generates an ECDSA P-256 keypair; `issue --key ... --to ... --out license.json` (optional `--expires`/`--machine`) issues a license. The private key must never be committed (`tools/license-gen/keys/` and `*.lickey` are gitignored) and must be backed up externally.

## Versioning

**Not centralized.** Version numbers (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`) are set redundantly in both `src/NovaFiches/TopoRapportWin.csproj` and `src/NovaFiches.PdfSharpEngine/NovaFiches.PdfSharpEngine.csproj` — both must be bumped together for a release; there is no `Directory.Build.props`.

The in-app, UI-wired changelog is `src/NovaFiches/assets/aide/HISTORIQUE_MISES_A_JOUR.md`, opened from the app's "Aide > Historique" menu (reads the file from `AppContext.BaseDirectory` at runtime, so it must be updated for any release the user will see). `SUIVI_MODIFICATIONS.md` in the same folder is legacy/stale and not referenced anywhere in code — do not update it.

## Architecture

### UI ↔ backend bridge (WebView2)

The entire UI is `src/NovaFiches/assets/topo_app.html` plus JS modules under `assets/app/modules/` (`m01_core.js` … `m15_*.js`, one module roughly per feature/report type — numbering has a gap, there is no `m12_*`), loaded offline into a `Microsoft.Web.WebView2` control.

- **JS → C#**: `CoreWebView2.WebMessageReceived` in `MainForm.cs` (~line 2515) is a single large sequential dispatcher: `if (string.Equals(type, "<msg-type>", StringComparison.OrdinalIgnoreCase)) { ...; return; }`, one block per message type (e.g. `pdfsharp_ligne_reference`, `cp_export`, `kmz_export`, `kmz_fetch_ngf`, `fiches_geocode`, `params_load`/`params_save`, `cpoly_export`). This is the map of "what the app can do" — grep this method for the full feature list rather than reading `MainForm.cs` end to end.
- **C# → JS**: `SendToUi(object payload)` (wraps `PostToWebIfReady`) or direct `_webView.CoreWebView2.ExecuteScriptAsync(...)`.
- Business/import/export logic lives in per-feature `*Service.cs` files (root of `src/NovaFiches` and subfolders `ControlePrecision/`, `ControlePolygonale/`, `DuplicateControl/`, `Licensing/`, `Branding/`), called from the message handlers in `MainForm.cs`, which assembles the JSON payload sent on to the PDF engine. A `*MapService.cs` per report type (e.g. `ControlePrecisionMapService`, `ControlePolygonaleMapService`, `KmzMeasureMapService`) builds a situation-map PNG by reprojecting points to WGS84, then calling the shared `MapTileFetcher` (OSM/Esri tile fetch+stitch) — copy-adapted per report rather than shared, same convention as the PDF renderers below.
- Whole-project save/load (`.nova` files, "Fichier" menu → `SaveProjectAsync`/`OpenProjectAsync` in `MainForm.cs`) round-trips through `window.NOVA_getState()`/`NOVA_setState(data)` in `m01_core.js`. A module with state beyond simple form fields (arrays of dynamic rows, images as data URLs) hooks in *without touching `m01_core.js`*: it saves references to `window.NOVA_getState`/`NOVA_setState`, replaces them with wrapped versions that call the original then merge in a module-specific key, guarded by a `.__nfXxxWrapped` flag against double-wrapping. See `wrapState()` in `m09_photos.js` (simplest example) or `m15_controle_polygonale.js` (most elaborate: manual restore of dynamic row lists, then explicit re-trigger of visibility/preview updates since the generic setter doesn't know about them).

Most modules are a single long form panel. `m15_controle_polygonale.js` ("Rapport de polygonale") is the exception and the template to follow for a similarly large module: a step-wizard (`.cpoly-step[data-cpoly-step-panel]` panels, one per report section, sidebar nav + Précédent/Suivant) with a live PDF preview shown in a second, non-modal WebView2 window (`CpolyPreviewForm.cs`) kept alive and re-`Navigate()`-d rather than recreated per preview — the previewed PDF is written to a fresh GUID-named temp file each time specifically to dodge WebView2's cache on a repeated `file://` URL.

### PDF rendering engine

`src/NovaFiches.PdfSharpEngine` — one renderer class per report type (e.g. `ControlePrecisionRenderer.cs`, `LigneReferencePlanRenderer.cs`, `StationPlanRenderer.cs`, `FicheSignaletiqueRenderer.cs`), built directly on PDFsharp-GDI (`XGraphics`/`XPen`/`XSolidBrush`).

Two generations of entry-point pattern coexist:
- **Older**: typed C# payload classes (e.g. `ImplantationTablePayload`) consumed by static `Generate*` methods in `PdfSharpReports.cs`.
- **Newer**: `MainForm.cs` serializes an anonymous object straight to a JSON string; the renderer parses it itself via `Render(PdfDocument doc, string payloadJson, string buildFooter)` + `JsonDocument.Parse` (used by `ControlePrecision`, `LigneReferencePlan`, `FicheSignaletique`, `StationPlan`). Prefer this pattern for new renderers.

Shared PDF helpers worth knowing about before adding a new renderer: `MapTileFetcher.cs` (OSM/Esri tile fetch+stitch, used by `StationPlanRenderer`, `ControlePrecisionMapService`, fiches signalétiques), `NovatlasTheme.cs` (branding resolvers — logo/address/colors, sourced from `BrandingService.cs` and injected into every `pdfsharp_*` payload from `MainForm.cs`), and the anti-collision label-placement + `NiceScaleLength`/`DrawNorthArrow` logic in `RecolementPlanViewRenderer.cs`, which has been copy-adapted (not shared) into newer plan renderers to avoid regressing the pieux module.

Merging pages from an external PDF into a generated report (as opposed to drawing new pages) is a distinct, less-common capability: `NgfFicheMergeService.cs` opens the already-saved output with `PdfReader.Open(path, PdfDocumentOpenMode.Modify)` and appends pages copied from a downloaded PDF opened with `PdfDocumentOpenMode.Import`. It's a post-processing step run from `MainForm.cs` *after* the renderer has written the file, not something the renderer itself does. Used for joining official IGN benchmark ("repère NGF") fiche PDFs, fetched by geographic bounding box via `IgnGeodesyService.cs` (IGN's public WFS, `data.geopf.fr`, no API key).

### Licensing

Offline ECDSA P-256/SHA-256-signed license files (`license.json` = `{"payload": base64, "signature": base64}`). Validated in `Program.cs` (`LicenseService.LoadAndValidate()`) **before** `MainForm` is ever constructed — an invalid/missing license shows a blocking `LicenseForm` and the app exits without reaching any UI code. Individual paid features are gated by license feature flags (see `tools/license-gen/README.md` and the `nf-feature-any`/similar `data-nf-feature-*` attributes in `topo_app.html`).

### CI

`.github/workflows/ci.yml` runs on `windows-latest` only (WinForms/WebView2 doesn't build on Linux/macOS). It builds `TopoRapportWin.csproj` and `tools/license-gen/LicenseGen.csproj` (Release), runs `dotnet test`, runs the smoke and vendor checks above, then does a validation-only `dotnet publish` (asserting `assets/topo_app.html` exists in the output) — it does not package the installer.

## Working conventions established in this codebase

- For any change to a PDF renderer, verify visually rather than by reading code: write a throwaway xUnit test that feeds real/representative data through the actual service+renderer classes to produce a real PDF, render pages to PNG (`pypdfium2` via the `py` launcher — `python`/`python3`/`node` are not reliably on PATH in every dev environment, only `py`), inspect, then delete the throwaway test before finishing.
- Ship sequence for a release: `dotnet build --no-incremental` + full `dotnet test` → `check_vendor.ps1` (only if HTML/JS changed) → confirm `NovaFiches.exe` isn't running → `BUILD_RELEASE.ps1` → Inno Setup compile (only if an installer was requested) → git commit/push only if explicitly requested.
