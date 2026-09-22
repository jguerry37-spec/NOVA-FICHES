using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using TopoRapportWin.Branding;
using TopoRapportWin.Licensing;
using TopoRapportWin.DuplicateControl;
using TopoRapportWin.ControlePrecision;

namespace TopoRapportWin;

public class MainForm : Form
{
    private readonly WebView2 _webView = new();

    private AppSettings _settings = new();

    private const long MaxImportTextBytes = 100L * 1024L * 1024L;

    // ===== ÉCHANGES (V2) =====
    // Données chargées via le menu "Échanges" (TXT points / GSI Leica)
    // Le lien avec l'AppLog est géré côté HTML/JS (boutons activés/désactivés).
    private List<ExchangePoint>? _txtPoints;
    private List<ExchangePoint>? _gsiPoints;
    private List<ExchangeObservation>? _gsiObservations;
    private string? _gsiMode; // "coords_81", "coords_21", "obs"
    private string? _txtFilePath;
    private string? _gsiFilePath;
    private string? _kmzTxtFilePath;
    private string? _kmzTxtRawText;
    private List<ExchangePoint>? _kmzTxtPoints;
    private string? _kmzDxfFilePath;
    private DxfKmzService.DxfDocument? _kmzDxfDocument;
    private string? _projectFilePath;
    private string? _landXmlProjectPath;
    private string? _pieuxTxtProjectPath;

    private string? _nextDownloadFileName;
    // Cache WebView2 versionné (évite d’exécuter un front-end ancien après mise à jour)
    private readonly string _webViewCacheTag;

    private static string GetWebViewCacheTag()
    {
        // On prend Major.Minor.Build et on remplace '.' par '_' pour un dossier stable.
        // Exemple: 1.7.46.0 -> v1_7_46
        var pv = Application.ProductVersion ?? "0.0.0.0";
        var parts = pv.Split('.');
        var tag = (parts.Length >= 3) ? $"v{parts[0]}_{parts[1]}_{parts[2]}" : $"v{pv.Replace('.', '_')}";
        return tag;
    }
private static string GetDisplayVersion4()
{
    // Want the full 4-part version when available (e.g. 2.2.0.11).
    // This is important because many builds differ only by the 4th part.
    var pv = Application.ProductVersion ?? "";

    if (Version.TryParse(pv, out var v))
        return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

    // Fallback: keep only first 4 dot-separated parts
    var parts = pv.Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length >= 4) return $"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}";
    if (parts.Length >= 3) return $"{parts[0]}.{parts[1]}.{parts[2]}";
    return pv;
}

private static string GetPdfFooterVersion() => $"Version {GetDisplayVersion4()}";

static string ComputeAssetsSha256(string assetsRoot)
{
    try
    {
        if (!Directory.Exists(assetsRoot)) return "no_assets_dir";
        var files = Directory.GetFiles(assetsRoot, "*.*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        using var sha = SHA256.Create();
        foreach (var f in files)
        {
            // Hash file path + bytes to avoid collisions when names change
            var rel = Path.GetRelativePath(assetsRoot, f).Replace('\\', '/');
            var relBytes = System.Text.Encoding.UTF8.GetBytes(rel + "\n");
            sha.TransformBlock(relBytes, 0, relBytes.Length, null, 0);

            var bytes = File.ReadAllBytes(f);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
    catch
    {
        return "sha_error";
    }
}





    private static bool IsDebugPayloadDumpEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NOVA_FICHES_DEBUG_PAYLOADS"),
            "1",
            StringComparison.Ordinal);
    }

    private void TryWritePdfSharpDebugPayload(string kind, string json)
    {
        if (!IsDebugPayloadDumpEnabled()) return;

        try
        {
            var dbgDir = Path.Combine(ExportsDir, "_debug");
            Directory.CreateDirectory(dbgDir);
            // last payload (overwrite)
            File.WriteAllText(Path.Combine(dbgDir, "last_pdfsharp_payload.json"), json);
            // kind-specific (overwrite)
            var safeKind = string.Join("_", kind.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            File.WriteAllText(Path.Combine(dbgDir, $"last_{safeKind}_payload.json"), json);
            // timestamped snapshot
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.WriteAllText(Path.Combine(dbgDir, $"{stamp}_{safeKind}.json"), json);
        }
        catch
        {
            // Never block PDF generation for debug
        }
    }

    private string ExportsDir { get; } =
        GetDownloadsDir();

    
    private static string GetDownloadsDir()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(userProfile, "Downloads");
        try
        {
            Directory.CreateDirectory(downloads);
        }
        catch
        {
            // If creation fails (locked-down environment), fallback to user profile.
            downloads = userProfile;
        }
        return downloads;
    }

    private static string ReadAllTextWithLimit(string path, long maxBytes = MaxImportTextBytes)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists && fileInfo.Length > maxBytes)
        {
            var sizeMb = Math.Ceiling(fileInfo.Length / 1024d / 1024d);
            var limitMb = Math.Ceiling(maxBytes / 1024d / 1024d);
            throw new IOException($"Fichier trop volumineux ({sizeMb:0} Mo). Limite: {limitMb:0} Mo.");
        }

        return File.ReadAllText(path);
    }

    private string AssetsHtmlPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "assets", "topo_app.html");

    // IMPORTANT: WebView2 doit écrire ici -> LocalAppData (pas Program Files)
    private string AppDataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NOVATLAS", "Nova-Fiches");

    // IMPORTANT: WebView2 doit écrire ici -> LocalAppData (pas Program Files), et dossier versionné pour éviter le cache HTML/JS
    private string WebViewUserDataDir =>
        Path.Combine(AppDataDir, "WebView2", _webViewCacheTag);

    private string SettingsPath =>
        Path.Combine(AppDataDir, "settings.json");
public MainForm()
    {
        _webViewCacheTag = GetWebViewCacheTag();
        Text = "Nova-Fiches";
        // ===== DPI / sizing (ETAPE 5) =====
        // Keep UI crisp under Windows scaling (125%/150%/...) and allow safe resizing.
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new System.Drawing.Size(980, 650);

        // Default size (will be overridden by saved placement or small-screen maximize)
        Width = 1200;
        Height = 800;

        // ===== Settings (avant construction menu) =====
        LoadSettings();
        ApplyWindowPlacement();

        // ===== MENU =====
        var menu = new MenuStrip();

        var mFile = new ToolStripMenuItem("Fichier");
        mFile.DropDownItems.Add(new ToolStripMenuItem("Ouvrir projet…", null, async (_, __) => await OpenProjectAsync()));
        mFile.DropDownItems.Add(new ToolStripMenuItem("Enregistrer le projet", null, async (_, __) => await SaveProjectAsync(false)));
        mFile.DropDownItems.Add(new ToolStripMenuItem("Enregistrer le projet sous…", null, async (_, __) => await SaveProjectAsync(true)));
        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add(new ToolStripMenuItem("Recharger", null, async (_, __) => await ReloadAsync()));
var miAutoOpen = new ToolStripMenuItem("Ouvrir Exports après export")
        {
            CheckOnClick = true,
            Checked = _settings.OpenExportsAfterSave
        };
        miAutoOpen.CheckedChanged += (_, __) =>
        {
            _settings.OpenExportsAfterSave = miAutoOpen.Checked;
            SaveSettings();
        };

        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add(miAutoOpen);
        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add(new ToolStripMenuItem("Quitter", null, (_, __) => Close()));


        var mAutoCad = new ToolStripMenuItem("Export AutoCAD", null, async (_, __) => await ExportAutoCadAsync());

        var mFusion = new ToolStripMenuItem("Fusion PDF", null, async (_, __) => await MergePdfClientAsync());

        var mHelp = new ToolStripMenuItem("Aide");
        mHelp.DropDownItems.Add(new ToolStripMenuItem("Notice utilisateur (PDF)", null, (_, __) => OpenHelpNoticePdf()));
        mHelp.DropDownItems.Add(new ToolStripMenuItem("Historique des mises à jour", null, (_, __) => OpenUpdateHistory()));
        mHelp.DropDownItems.Add(new ToolStripSeparator());
        mHelp.DropDownItems.Add(new ToolStripMenuItem("Gérer la licence…", null, async (_, __) => await ManageLicenseAsync()));
        mHelp.DropDownItems.Add(new ToolStripMenuItem("À propos", null, (_, __) => ShowAbout()));
        mHelp.DropDownItems.Add(new ToolStripMenuItem("Copier diagnostic", null, (_, __) => CopyDiagnostic()));

        menu.Items.Add(mFile);
        menu.Items.Add(mAutoCad);
        menu.Items.Add(mFusion);
        menu.Items.Add(mHelp);

        MainMenuStrip = menu;
        Controls.Add(menu);
        menu.Dock = DockStyle.Top;
        menu.BringToFront();

        // ===== WEBVIEW =====
// Fix 1.6.7 : WebView2 ne doit pas recouvrir la zone du MenuStrip (dock/z-order)
// On encapsule WebView2 dans un panel Dock=Fill, ce qui rend le layout robuste.
var contentPanel = new Panel { Dock = DockStyle.Fill };
_webView.Dock = DockStyle.Fill;
contentPanel.Controls.Add(_webView);
Controls.Add(contentPanel);
Load += async (_, __) => await InitializeWebViewAsync();

        // Persist window placement (ETAPE 5)
        ResizeEnd += (_, __) => { CaptureWindowPlacement(); SaveSettings(); };
        FormClosing += (_, __) => { CaptureWindowPlacement(); SaveSettings(); };
    }


    private async Task<string?> GetProjectStateJsonAsync()
    {
        if (_webView?.CoreWebView2 == null) return null;

        // Push known external file references (paths only, never file contents) into JS snapshot state.
        var fileStateScript = $"window.__NF_PROJECT_FILES = {{ landxmlPath: {JsonSerializer.Serialize(_landXmlProjectPath ?? string.Empty)}, pieuxTxtPath: {JsonSerializer.Serialize(_pieuxTxtProjectPath ?? string.Empty)} }};";
        await _webView.CoreWebView2.ExecuteScriptAsync(fileStateScript);

        var raw = await _webView.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.NOVA_getState ? NOVA_getState() : null)");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return JsonSerializer.Deserialize<string>(raw);
    }

    private async Task SaveProjectAsync(bool saveAs)
    {
        try
        {
            var json = await GetProjectStateJsonAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                MessageBox.Show(this, "Impossible de récupérer l'état du projet depuis l'interface.", "Nova-Fiches",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var targetPath = _projectFilePath;
            if (saveAs || string.IsNullOrWhiteSpace(targetPath))
            {
                using var sfd = new SaveFileDialog
                {
                    Title = "Enregistrer le projet Nova-Fiches",
                    Filter = "Projet Nova-Fiches (*.nova)|*.nova",
                    DefaultExt = "nova",
                    AddExtension = true,
                    OverwritePrompt = true,
                    InitialDirectory = ExportsDir,
                    FileName = string.IsNullOrWhiteSpace(_projectFilePath) ? "projet.nova" : Path.GetFileName(_projectFilePath)
                };
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                targetPath = sfd.FileName;
            }

            using var jd = JsonDocument.Parse(json);
            var pretty = JsonSerializer.Serialize(jd.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(targetPath!, pretty, new UTF8Encoding(false));
            _projectFilePath = targetPath;

            AppLog.Info($"Projet .nova sauvegardé: {targetPath}");
            MessageBox.Show(this, $"Projet enregistré :{Environment.NewLine}{targetPath}", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("SaveProjectAsync failed", ex);
            MessageBox.Show(this, "Erreur enregistrement projet .nova." + Environment.NewLine + "Voir logs.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OpenProjectAsync()
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Ouvrir un projet Nova-Fiches",
                Filter = "Projet Nova-Fiches (*.nova)|*.nova",
                RestoreDirectory = true
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            await LoadProjectAsync(ofd.FileName);
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenProjectAsync failed", ex);
            MessageBox.Show(this, "Erreur ouverture projet .nova." + Environment.NewLine + "Voir logs.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadProjectAsync(string path)
    {
        var json = ReadAllTextWithLimit(path);
        using var jd = JsonDocument.Parse(json);
        var root = jd.RootElement;

        var script = $"window.NOVA_setState && NOVA_setState({json});";
        await _webView.CoreWebView2.ExecuteScriptAsync(script);

        _projectFilePath = path;

        var missing = new List<string>();
        if (root.TryGetProperty("fichiers", out var files))
        {
            var landPath = files.TryGetProperty("landxmlPath", out var l) ? (l.GetString() ?? string.Empty) : string.Empty;
            var txtPath = files.TryGetProperty("pieuxTxtPath", out var t) ? (t.GetString() ?? string.Empty) : string.Empty;

            _landXmlProjectPath = landPath;
            _pieuxTxtProjectPath = txtPath;

            if (!string.IsNullOrWhiteSpace(landPath))
            {
                if (File.Exists(landPath))
                    ImportLandXmlFromPath(landPath);
                else
                    missing.Add($"LandXML introuvable : {landPath}");
            }

            if (!string.IsNullOrWhiteSpace(txtPath))
            {
                if (File.Exists(txtPath))
                    await LoadPieuxTxtFromPathAsync(txtPath);
                else
                    missing.Add($"TXT pieux introuvable : {txtPath}");
            }
        }

        if (missing.Count > 0)
        {
            MessageBox.Show(this,
                "Projet chargé, mais certains fichiers référencés sont introuvables :" + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, missing),
                "Nova-Fiches",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ImportLandXmlFromPath(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            var xmlText = ReadAllTextWithLimit(filePath);
            _landXmlProjectPath = filePath;

            try
            {
                var script = $"window.__NF_PROJECT_FILES = Object.assign(window.__NF_PROJECT_FILES || {{}}, {{ landxmlPath: {JsonSerializer.Serialize(filePath)} }});";
                _webView.CoreWebView2?.ExecuteScriptAsync(script);
            }
            catch { }

            PostToWebIfReady(new
            {
                type = "importLandXml",
                fileName = Path.GetFileName(filePath),
                xmlText = xmlText
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("ImportLandXmlFromPath failed", ex);
        }
    }

    private async Task LoadPieuxTxtFromPathAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || _webView?.CoreWebView2 == null) return;
            var txt = ReadAllTextWithLimit(filePath);
            _pieuxTxtProjectPath = filePath;
            var js = $"window.NOVA_loadPieuxTxtContent && NOVA_loadPieuxTxtContent({JsonSerializer.Serialize(txt)}, {JsonSerializer.Serialize(Path.GetFileName(filePath))}, {JsonSerializer.Serialize(filePath)});";
            await _webView.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch (Exception ex)
        {
            AppLog.Error("LoadPieuxTxtFromPathAsync failed", ex);
        }
    }

    private void ApplyWindowPlacement()
    {
        // 1) If we have a saved placement, restore it (only if it fits current screens).
        try
        {
            if (_settings.RememberWindowPlacement && _settings.Window is not null && _settings.Window.IsValid())
            {
                var wa = Screen.GetWorkingArea(new System.Drawing.Rectangle(0, 0, 1, 1));
                // Ensure we restore within an existing screen working area.
                var rect = _settings.Window.ToRectangle();
                var screen = Screen.FromRectangle(rect);
                var work = screen.WorkingArea;

                // Clamp to working area to avoid "lost window" after monitor changes.
                var clamped = ClampToWorkingArea(rect, work);

                StartPosition = FormStartPosition.Manual;
                Bounds = clamped;
                WindowState = _settings.Window.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                return;
            }
        }
        catch
        {
            // fall back to defaults below
        }

        // 2) No saved placement: adapt to small screens.
        try
        {
            var work = Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

            // If working area is smaller than our default, start maximized.
            if (work.Width < 1200 || work.Height < 800)
            {
                StartPosition = FormStartPosition.CenterScreen;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                WindowState = FormWindowState.Normal;
            }
        }
        catch
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    private void CaptureWindowPlacement()
    {
        try
        {
            if (!_settings.RememberWindowPlacement)
                return;

            var isMax = WindowState == FormWindowState.Maximized;
            var rect = isMax ? RestoreBounds : Bounds;

            _settings.Window ??= new AppWindowPlacement();
            _settings.Window.X = rect.X;
            _settings.Window.Y = rect.Y;
            _settings.Window.Width = rect.Width;
            _settings.Window.Height = rect.Height;
            _settings.Window.Maximized = isMax;
        }
        catch
        {
            // non bloquant
        }
    }

    private static System.Drawing.Rectangle ClampToWorkingArea(System.Drawing.Rectangle r, System.Drawing.Rectangle work)
    {
        // Ensure minimal size
        var w = Math.Max(r.Width, 980);
        var h = Math.Max(r.Height, 650);

        // If bigger than working area, shrink (but keep usable)
        w = Math.Min(w, work.Width);
        h = Math.Min(h, work.Height);

        var x = r.X;
        var y = r.Y;

        // Move into view
        if (x < work.Left) x = work.Left;
        if (y < work.Top) y = work.Top;
        if (x + w > work.Right) x = Math.Max(work.Left, work.Right - w);
        if (y + h > work.Bottom) y = Math.Max(work.Top, work.Bottom - h);

        return new System.Drawing.Rectangle(x, y, w, h);
    }

    private async System.Threading.Tasks.Task InitializeWebViewAsync()
    {
        try
        {
            Directory.CreateDirectory(ExportsDir);
            Directory.CreateDirectory(WebViewUserDataDir);

// Auto-clear WebView2 cache when embedded assets change (prevents stale JS/logo issues)
// We compute a lightweight fingerprint from the last write times of key asset files.
try
{
    var fp = BuildAssetsFingerprint();
    var fpFile = Path.Combine(WebViewUserDataDir, "assets_fingerprint.txt");
    var oldFp = File.Exists(fpFile) ? File.ReadAllText(fpFile) : "";
    if (!string.Equals(oldFp, fp, StringComparison.Ordinal))
    {
        try { Directory.Delete(WebViewUserDataDir, recursive: true); } catch { /* ignore */ }
        Directory.CreateDirectory(WebViewUserDataDir);
        File.WriteAllText(fpFile, fp);
        AppLog.Info($"WebView2 cache reset (assets changed). Fingerprint={fp}");
    }
}
catch (Exception ex)
{
    AppLog.Error("WebView2 cache reset check failed", ex);
}

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: WebViewUserDataDir);

            await _webView.EnsureCoreWebView2Async(env);

            

// Inject build/version into the WebView (single source of truth)
try
{
	var build = Application.ProductVersion; // e.g. "2.1.0.90"
	var assetsRoot = Path.Combine(AppContext.BaseDirectory, "assets");
	var assetsSha = ComputeAssetsSha256(assetsRoot);

	// Detect PdfSharp engine availability (runtime safe-guard)
	var pdfSharpOk = false;
	try
	{
		// Reference the type so the JIT / loader will validate the assembly is present.
		_ = typeof(NovaFiches.PdfSharpEngine.PdfSharpReports);
		pdfSharpOk = true;
	}
	catch
	{
		pdfSharpOk = false;
	}

	// IMPORTANT: avoid C# interpolated strings containing raw '{' / '}' (JS objects)
	// Use JSON serialization for safe quoting.
	var buildJs = System.Text.Json.JsonSerializer.Serialize(build);
	var shaJs = System.Text.Json.JsonSerializer.Serialize(assetsSha);
	var pdfSharpJs = System.Text.Json.JsonSerializer.Serialize(pdfSharpOk);
	var initScript =
	    "(() => {" +
	    "  try {" +
	    "    window.__NF_BUILD = " + buildJs + ";" +
	    "    window.__NF_ASSETS_SHA = " + shaJs + ";" +
	    "    window.__NF_PDFSHARP_AVAILABLE = " + pdfSharpJs + ";" +
	    "    window.APP_VERSION = window.__NF_BUILD;" +
	    "  } catch (e) { /* ignore */ }" +
	    "})();";

	await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(initScript);
}
catch (Exception ex)
{
    AppLog.Error("Failed to inject __NF_BUILD", ex);
}

// Update footer label after navigation (DOM-ready)
_webView.CoreWebView2.NavigationCompleted += async (_, __) =>
{
    try
    {
        var build = Application.ProductVersion;
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"(function(){{var el=document.getElementById('footerBuild'); if(el) el.textContent='Build '+{System.Text.Json.JsonSerializer.Serialize(build)};}})();");
    }
    catch { }

    try
    {
        await _webView.CoreWebView2.ExecuteScriptAsync(BuildFooterLicenseScript());
    }
    catch (Exception ex)
    {
        AppLog.Error("Footer: injection statut licence impossible", ex);
    }
};

// V2 Phase 6 — vérification de mise à jour, non bloquante, échec silencieux
// (voir UpdateCheck.cs). Fire-and-forget : ne retarde jamais l'affichage de l'appli.
_webView.CoreWebView2.NavigationCompleted += (_, __) => _ = CheckForUpdateAsync();

HookDiagnostics(_webView);
            HookDownloadSaveAs(_webView);      // PDF typiquement
            HookWebMessageSaveAs(_webView);    // PDF via base64 + mimeType

            if (!File.Exists(AssetsHtmlPath))
            {
                AppLog.Error($"HTML introuvable: {AssetsHtmlPath}");
                MessageBox.Show(this,
                    $"Fichier HTML introuvable :\n{AssetsHtmlPath}\n\nVérifie que 'assets/topo_app.html' est bien copié à l'installation.",
                    "Nova-Fiches",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var fileUri = new Uri(AssetsHtmlPath).AbsoluteUri; // file:///C:/...
            AppLog.Info($"Navigate: {fileUri}");
            _webView.Source = new Uri(fileUri);
        }
        catch (Exception ex)
        {
            AppLog.Error("WebView2 init failed", ex);
            MessageBox.Show(this, "Erreur au démarrage WebView2.\nVoir logs.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async System.Threading.Tasks.Task CheckForUpdateAsync()
    {
        try
        {
            var update = await UpdateCheck.CheckAsync();
            if (update == null) return;

            if (_webView?.CoreWebView2 == null) return;
            var versionJs = System.Text.Json.JsonSerializer.Serialize(update.Value.LatestVersion);
            var urlJs = System.Text.Json.JsonSerializer.Serialize(update.Value.DownloadUrl);
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.NOVA_showUpdateBanner && NOVA_showUpdateBanner({versionJs}, {urlJs});");
            AppLog.Info($"UpdateCheck: nouvelle version disponible ({update.Value.LatestVersion}).");
        }
        catch (Exception ex)
        {
            AppLog.Info($"UpdateCheck: échec silencieux — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void HookDiagnostics(WebView2 view)
    {
        if (view.CoreWebView2 == null) return;

        view.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
                AppLog.Info("NavigationCompleted: success");
            else
                AppLog.Error($"NavigationCompleted: failed ({e.WebErrorStatus})");
        };

        view.CoreWebView2.ProcessFailed += (_, e) =>
        {
            AppLog.Error($"WebView2 ProcessFailed: {e.ProcessFailedKind}");
        };

        view.CoreWebView2.WebResourceResponseReceived += (_, e) =>
        {
            try
            {
                var uri = e.Request?.Uri ?? "";
                var resp = e.Response;
                if (resp == null) return;

                int status = resp.StatusCode;
                if (status >= 400)
                    AppLog.Error($"Resource error {status}: {uri}");
            }
            catch
            {
                // ignore
            }
        };

#if DEBUG
        view.CoreWebView2.Settings.AreDevToolsEnabled = true;
#endif
    }

    private async System.Threading.Tasks.Task ReloadAsync()
    {
        try
        {
            if (_webView.CoreWebView2 == null)
                await InitializeWebViewAsync();
            else
                _webView.Reload();
        }
        catch (Exception ex)
        {
            AppLog.Error("Reload failed", ex);
            MessageBox.Show(this, "Impossible de recharger la page.\nVoir logs.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenExportsFolder()
    {
        try
        {
            Directory.CreateDirectory(ExportsDir);
            using var process = Process.Start(new ProcessStartInfo { FileName = ExportsDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenExportsFolder failed", ex);
            MessageBox.Show(this, "Impossible d'ouvrir le dossier Exports.\nVoir logs.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ============================================================
    // ÉCHANGES (V2) — Imports (TXT points / GSI Leica) + Export TXT
    // Étape 3.2 : on charge, on parse, on notifie la UI (HTML/JS).
    // Aucun impact sur le pipeline PDF existant.
    // ============================================================

    private void ImportTxtPoints()
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Importer un fichier TXT (points XYZC)",
                Filter = "Fichiers TXT (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            var text = ReadAllTextWithLimit(ofd.FileName);
            var (points, rejects) = ParseTxtPoints(text);

            _txtFilePath = ofd.FileName;
            _txtPoints = points;

            AppLog.Info($"Échanges: TXT import '{Path.GetFileName(ofd.FileName)}' => {points.Count} points, {rejects.Count} rejets");

            PostToWebIfReady(new
            {
                type = "exchangeImportedTxt",
                fileName = Path.GetFileName(ofd.FileName),
                pointCount = points.Count,
                rejectCount = rejects.Count,
                // IMPORTANT (V2): fournir aussi les points à la UI pour les futurs PDF combinés.
                // (format léger: Id/X/Y/Z/Code)
                points = points
            });

            if (rejects.Count > 0)
            {
                // On reste non bloquant : un simple avertissement.
                MessageBox.Show(this,
                    $"Import TXT terminé avec {rejects.Count} ligne(s) ignorée(s).\n\n" +
                    "Les points valides sont disponibles pour les futurs exports/PDF.",
                    "Nova-Fiches — Import TXT",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Échanges: Import TXT failed", ex);
            MessageBox.Show(this, "Erreur import TXT. Voir logs.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportGsi()
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Importer un fichier GSI (Leica)",
                Filter = "Fichiers GSI (*.gsi;*.GSI)|*.gsi;*.GSI|Tous les fichiers (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            var text = ReadAllTextWithLimit(ofd.FileName);
            var (points, obs, rejects, mode, coords81_82_83) = ParseGsi(text);

            _gsiFilePath = ofd.FileName;
            _gsiPoints = points;
            _gsiObservations = obs;
            _gsiMode = mode;

            AppLog.Info($"Échanges: GSI import '{Path.GetFileName(ofd.FileName)}' => {points.Count} point(s), {obs.Count} obs, {rejects} rejets, mode={mode}");

            PostToWebIfReady(new
            {
                type = "exchangeImportedGsi",
                fileName = Path.GetFileName(ofd.FileName),
                pointCount = points.Count,
                rejectCount = rejects,
                coords81_82_83 = coords81_82_83,
                gsiMode = mode,
                obsCount = obs.Count,
                // IMPORTANT (V2): fournir aussi les points à la UI pour les futurs PDF combinés.
                points = points,
                // V2 Étape 4.2 : fournir aussi les observations polaires (si présentes) pour export PDF sans calcul.
                observations = obs
            });
            if (mode == "obs")
            {
                MessageBox.Show(this,
                    @"Le fichier GSI importé contient des observations (Hz / V / Distance) mais pas de coordonnées exportées (WI81/82/83).

Nova-Fiches V2 va traiter ce cas en Étape 4 (calcul de points depuis la station + visées).
Pour l'instant, les observations sont détectées et seront utilisées à l'étape suivante.",
                    "Nova-Fiches — Import GSI",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else if (mode == "coords_21")
            {
                MessageBox.Show(this,
                    @"Le fichier GSI importé fournit des coordonnées, mais via un masque utilisateur (WI21/22/31) au lieu de WI81/82/83.

Nova-Fiches les a reconnues et importées comme des points XYZ.",
                    "Nova-Fiches — Import GSI",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Échanges: Import GSI failed", ex);
            MessageBox.Show(this, "Erreur import GSI. Voir logs.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ------------------------------------------------------------
    // ÉCHANGES (V2) — Import LandXML (Leica)
    // Objectif : lire un export LandXML issu Leica Captivate et l'envoyer
    // au front HTML/JS qui le convertit en "dataset" AppLog-compatible.
    // IMPORTANT : aucun refactor du pipeline PDF existant.
    // ------------------------------------------------------------
    private void ImportLandXml()
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Importer un fichier LandXML (Leica)",
                Filter = "Fichiers LandXML (*.xml)|*.xml|Tous les fichiers (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            var xmlText = ReadAllTextWithLimit(ofd.FileName);

            AppLog.Info($"Échanges: LandXML import '{Path.GetFileName(ofd.FileName)}' ({xmlText.Length} char)");

            PostToWebIfReady(new
            {
                type = "importLandXml",
                fileName = Path.GetFileName(ofd.FileName),
                xmlText = xmlText
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Échanges: Import LandXML failed", ex);
            MessageBox.Show(this, "Erreur import LandXML. Voir logs.", "Nova-Fiches",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ExportTxt()
    {
        try
        {
            // Étape 3.2: export minimal (points TXT ou GSI => TXT XYZC)
            var points = _txtPoints ?? _gsiPoints;
            if (points == null || points.Count == 0)
            {
                MessageBox.Show(this, "Aucun point à exporter.\n\nImporte un TXT ou un GSI d'abord.", "Nova-Fiches",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Title = "Exporter en TXT (XYZC)",
                Filter = "Fichiers TXT (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
                FilterIndex = 1,
                DefaultExt = "txt",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = ExportsDir,
                FileName = "points_export.txt"
            };

            if (sfd.ShowDialog(this) != DialogResult.OK)
                return;

            var lines = new List<string> { "N°\tX\tY\tZ\tCode" };
            foreach (var p in points)
            {
                var code = string.IsNullOrWhiteSpace(p.Code) ? "" : p.Code;
                lines.Add($"{p.Id}\t{p.X.ToString("0.000", CultureInfo.InvariantCulture)}\t{p.Y.ToString("0.000", CultureInfo.InvariantCulture)}\t{p.Z.ToString("0.000", CultureInfo.InvariantCulture)}\t{code}");
            }
            File.WriteAllLines(sfd.FileName, lines);

            AppLog.Info($"Échanges: Export TXT OK => {sfd.FileName} ({points.Count} points)");
            AfterSuccessfulExport(sfd.FileName);
        }
        catch (Exception ex)
        {
            AppLog.Error("Échanges: Export TXT failed", ex);
            MessageBox.Show(this, "Erreur export TXT. Voir logs.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportKmzTxtForUi(string requestedCrs)
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Importer un TXT pour export KMZ",
                Filter = "Fichiers TXT (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            var text = ReadAllTextWithLimit(ofd.FileName);
            var (points, rejects) = ParseTxtPoints(text);
            if (points.Count == 0)
            {
                MessageBox.Show(this, "Aucun point valide dans le TXT.", "Export KMZ",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _kmzTxtFilePath = ofd.FileName;
            _kmzTxtRawText = text;
            _kmzTxtPoints = points;

            var detectionPoints = points.Select(p => new KmzExportService.KmzPoint(p.Id, p.X, p.Y, p.Z, p.Code, p.HasZ));
            var detection = KmzExportService.DetectCoordinateSystem(ofd.FileName, text, detectionPoints);
            var auto = string.IsNullOrWhiteSpace(requestedCrs) ||
                       string.Equals(requestedCrs, "__AUTO__", StringComparison.OrdinalIgnoreCase);
            var crs = auto ? detection.CoordinateSystem : requestedCrs;

            PostKmzPreview(crs, rejects.Count, auto ? detection.Method : "choix manuel", resetSelection: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("KMZ: import TXT failed", ex);
            MessageBox.Show(this, "Erreur import TXT KMZ.\n\n" + ex.Message, "Export KMZ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SendToUi(new { type = "kmz_error", message = ex.Message });
        }
    }

    private void ReprojectKmzForUi(string sourceCrs)
    {
        try
        {
            if (_kmzTxtPoints == null || _kmzTxtPoints.Count == 0)
                return;
            if (string.Equals(sourceCrs, "__AUTO__", StringComparison.OrdinalIgnoreCase))
            {
                var detectionPoints = _kmzTxtPoints.Select(p => new KmzExportService.KmzPoint(p.Id, p.X, p.Y, p.Z, p.Code, p.HasZ));
                var detection = KmzExportService.DetectCoordinateSystem(_kmzTxtFilePath ?? "", _kmzTxtRawText, detectionPoints);
                PostKmzPreview(detection.CoordinateSystem, 0, detection.Method);
            }
            else
            {
                PostKmzPreview(sourceCrs, 0, "choix manuel");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("KMZ: reproject failed", ex);
            SendToUi(new { type = "kmz_error", message = ex.Message });
        }
    }

    private async Task FetchNgfForUiAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        try
        {
            var benchmarks = await IgnGeodesyService.FetchBenchmarksAsync(minLon, minLat, maxLon, maxLat);
            SendToUi(new
            {
                type = "kmz_ngf_loaded",
                points = benchmarks.Select(b => new
                {
                    id = b.Id,
                    nom = b.Nom,
                    etat = b.Etat,
                    altitude = b.Altitude,
                    lon = b.Lon,
                    lat = b.Lat,
                    ficheUrl = b.FicheUrl
                })
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("KMZ: fetch repères NGF failed", ex);
            SendToUi(new { type = "kmz_error", message = "Repères NGF (IGN) : " + ex.Message });
        }
    }

    // Reprojection generique E/N -> WGS84 pour l'onglet "Plan station" (Station /
    // Leve topo). Contrairement a ReprojectKmzForUi, ne depend d'aucun etat KMZ
    // (_kmzTxtPoints) : la station libre est calculee a partir d'un LandXML parse
    // cote client (JS), le C# n'a jamais vu ces points avant ce message. On
    // reutilise directement les primitives publiques de KmzExportService (memes
    // formules de projection que l'export KMZ) sans passer par le nom/contenu du
    // fichier source (non transmis ici) - la detection automatique retombe donc
    // sur l'heuristique par plage de coordonnees, suffisante pour les CRS francais
    // courants (Lambert-93, CC42-50).
    private void ReprojectStationMapForUi(JsonElement root)
    {
        // "token" est renvoye tel quel dans la reponse : plusieurs requetes peuvent
        // partir coup sur coup (l'utilisateur coche/decoche vite plusieurs points),
        // et rien ne garantit que les reponses reviennent dans le meme ordre cote
        // JS - le token permet d'ignorer une reponse perimee plutot que d'afficher
        // des infobulles associees au mauvais jeu de points.
        int? token = root.TryGetProperty("token", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.Number
            ? tokenEl.GetInt32()
            : null;
        try
        {
            string sourceCrs = root.TryGetProperty("sourceCrs", out var crsEl) ? (crsEl.GetString() ?? "__AUTO__") : "__AUTO__";
            if (!root.TryGetProperty("points", out var pointsEl) || pointsEl.ValueKind != JsonValueKind.Array)
            {
                SendToUi(new { type = "station_map_error", token, message = "Aucun point à reprojeter." });
                return;
            }

            var kmzPoints = new List<KmzExportService.KmzPoint>();
            foreach (var el in pointsEl.EnumerateArray())
            {
                string id = el.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
                double x = el.TryGetProperty("x", out var xEl) ? xEl.GetDouble() : 0;
                double y = el.TryGetProperty("y", out var yEl) ? yEl.GetDouble() : 0;
                if (string.IsNullOrWhiteSpace(id)) continue;
                kmzPoints.Add(new KmzExportService.KmzPoint(id, x, y, 0, null, false));
            }

            if (kmzPoints.Count == 0)
            {
                SendToUi(new { type = "station_map_error", token, message = "Aucun point à reprojeter." });
                return;
            }

            string detectionMethod = "choix manuel";
            var crs = sourceCrs;
            if (string.IsNullOrWhiteSpace(sourceCrs) || string.Equals(sourceCrs, "__AUTO__", StringComparison.OrdinalIgnoreCase))
            {
                var detection = KmzExportService.DetectCoordinateSystem("", null, kmzPoints);
                crs = detection.CoordinateSystem;
                detectionMethod = detection.Method;
            }

            var preview = KmzExportService.ProjectForPreview(kmzPoints, crs);
            SendToUi(new
            {
                type = "station_map_reprojected",
                token,
                sourceCrs = crs,
                detectionMethod,
                coordinateSystems = KmzExportService.CoordinateSystems,
                points = preview.Select(p => new { id = p.Id, lon = p.Lon, lat = p.Lat })
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Plan station : reprojection failed", ex);
            SendToUi(new { type = "station_map_error", token, message = ex.Message });
        }
    }

    // Avant de générer le PDF Station, si "Envoyer sur la fiche station" a été coché
    // (payload.stationPlanView présent), reprojette stations/points en WGS84 et injecte
    // lon/lat dans le payload : StationPlanRenderer (projet NovaFiches.PdfSharpEngine)
    // n'a pas accès à KmzExportService, car les projets ne référencent que dans un sens
    // (NovaFiches -> PdfSharpEngine) - la reprojection doit donc se faire ici, exactement
    // comme pour ReprojectStationMapForUi (fond de carte à l'écran). Toute erreur retombe
    // silencieusement sur le payload d'origine : StationPlanRenderer redessine alors le
    // plan en repère local, sans fond de carte (comportement déjà existant).
    private static string EnrichStationPlanViewWithLonLat(string payloadJson)
    {
        try
        {
            if (JsonNode.Parse(payloadJson) is not JsonObject node) return payloadJson;
            if (node["stationPlanView"] is not JsonObject planView) return payloadJson;

            var stationsArr = planView["stations"] as JsonArray;
            var pointsArr = planView["points"] as JsonArray;

            var kmzPoints = new List<KmzExportService.KmzPoint>();
            void CollectPoints(JsonArray? arr, string prefix)
            {
                if (arr == null) return;
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is not JsonObject o) continue;
                    if (o["e"] is not JsonValue eVal || !eVal.TryGetValue(out double e)) continue;
                    if (o["n"] is not JsonValue nVal || !nVal.TryGetValue(out double n)) continue;
                    kmzPoints.Add(new KmzExportService.KmzPoint($"{prefix}{i}", e, n, 0, null, false));
                }
            }
            CollectPoints(stationsArr, "st:");
            CollectPoints(pointsArr, "pt:");
            if (kmzPoints.Count == 0) return payloadJson;

            string sourceCrs = planView["sourceCrs"] is JsonValue crsVal && crsVal.TryGetValue(out string? crsStr) ? (crsStr ?? "__AUTO__") : "__AUTO__";
            if (string.IsNullOrWhiteSpace(sourceCrs) || string.Equals(sourceCrs, "__AUTO__", StringComparison.OrdinalIgnoreCase))
                sourceCrs = KmzExportService.DetectCoordinateSystem("", null, kmzPoints).CoordinateSystem;

            var preview = KmzExportService.ProjectForPreview(kmzPoints, sourceCrs);
            var byId = preview.ToDictionary(p => p.Id, p => p, StringComparer.Ordinal);

            void ApplyLonLat(JsonArray? arr, string prefix)
            {
                if (arr == null) return;
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is not JsonObject o) continue;
                    if (!byId.TryGetValue($"{prefix}{i}", out var pv)) continue;
                    if (!double.IsFinite(pv.Lon) || !double.IsFinite(pv.Lat)) continue;
                    o["lon"] = pv.Lon;
                    o["lat"] = pv.Lat;
                }
            }
            ApplyLonLat(stationsArr, "st:");
            ApplyLonLat(pointsArr, "pt:");

            return node.ToJsonString();
        }
        catch (Exception ex)
        {
            AppLog.Error("Plan station : enrichissement lon/lat pour le PDF a échoué (page restera en repère local)", ex);
            return payloadJson;
        }
    }

    // Priorité latitude/longitude explicites (déjà WGS84) sur x/y/epsg_source (reprojection) -
    // même ordre que l'outil autonome d'origine (computeLatLon). Retourne null si aucune des
    // deux voies n'aboutit (pas de carte pour cette fiche, jamais d'exception qui bloquerait
    // les autres lignes du CSV).
    private static (double Lon, double Lat)? ResolveFicheLonLat(double? latitude, double? longitude, double? x, double? y, string? epsgSource)
    {
        if (latitude.HasValue && longitude.HasValue)
            return (longitude.Value, latitude.Value);
        if (x.HasValue && y.HasValue && !string.IsNullOrWhiteSpace(epsgSource))
            return FicheSignaletiqueReprojection.ToWgs84FromEpsg(x.Value, y.Value, epsgSource);
        return null;
    }

    // Même contrainte que EnrichStationPlanViewWithLonLat : FicheSignaletiqueReprojection vit
    // dans ce projet (NovaFiches), inaccessible depuis PdfSharpEngine. Le PDF (import ET export)
    // ne fait jamais confiance à un lon/lat éventuellement renvoyé par le JS - toujours
    // recalculé ici à partir des champs bruts (x/y/epsgSource ou latitude/longitude), source
    // unique de vérité pour un document qui peut avoir une valeur légale.
    private static string EnrichFichesSignaletiquesWithLonLat(string payloadJson)
    {
        try
        {
            if (JsonNode.Parse(payloadJson) is not JsonObject node) return payloadJson;
            if (node["fichesSignaletiques"] is not JsonObject fiches) return payloadJson;
            if (fiches["rows"] is not JsonArray rows) return payloadJson;

            foreach (var item in rows)
            {
                if (item is not JsonObject row) continue;

                double? Num(string key) => row[key] is JsonValue v && v.TryGetValue(out double d) ? d : null;
                string? Str(string key) => row[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

                var resolved = ResolveFicheLonLat(Num("latitude"), Num("longitude"), Num("x"), Num("y"), Str("epsgSource"));
                if (resolved.HasValue)
                {
                    row["lon"] = resolved.Value.Lon;
                    row["lat"] = resolved.Value.Lat;
                }
                else
                {
                    row.Remove("lon");
                    row.Remove("lat");
                }
            }

            return node.ToJsonString();
        }
        catch (Exception ex)
        {
            AppLog.Error("Fiches signalétiques : enrichissement lon/lat pour le PDF a échoué", ex);
            return payloadJson;
        }
    }

    private void ImportFichesSignaletiquesCsvForUi()
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Importer un CSV/TXT de fiches signalétiques",
                Filter = "CSV/TXT (*.csv;*.txt)|*.csv;*.txt|Tous les fichiers (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            var text = ReadAllTextWithLimit(ofd.FileName);
            var (rows, rejects) = FicheSignaletiqueService.ParseCsv(text);

            var outRows = rows.Select(row =>
            {
                var lonLat = ResolveFicheLonLat(row.Latitude, row.Longitude, row.X, row.Y, row.EpsgSource);
                var revisions = FicheSignaletiqueService.CollectRevisions(row)
                    .Select(r => new { indice = r.Indice, date = r.Date, description = r.Description, auteur = r.Auteur })
                    .ToList();
                return new
                {
                    idFiche = row.IdFiche,
                    typeFiche = row.TypeFiche,
                    dossier = row.Dossier,
                    chantier = row.Chantier,
                    reference = row.Reference,
                    indice = row.Indice,
                    dateEdition = row.DateEdition,
                    dateDetermination = row.DateDetermination,
                    client = row.Client,
                    prestataire = row.Prestataire,
                    commune = row.Commune,
                    departement = row.Departement,
                    adresse = row.Adresse,
                    site = row.Site,
                    point = row.Point,
                    nature = row.Nature,
                    systemePlani = row.SystemePlani,
                    systemeAlti = row.SystemeAlti,
                    epsgSource = row.EpsgSource,
                    ppm = row.Ppm,
                    x = row.X,
                    y = row.Y,
                    z = row.Z,
                    latitude = row.Latitude,
                    longitude = row.Longitude,
                    zoom = row.Zoom,
                    observations = row.Observations,
                    photo = row.Photo,
                    lon = lonLat?.Lon,
                    lat = lonLat?.Lat,
                    revisions
                };
            }).ToList();

            SendToUi(new { type = "fiches_csv_loaded", fileName = Path.GetFileName(ofd.FileName), rows = outRows, rejectCount = rejects.Count });
        }
        catch (Exception ex)
        {
            AppLog.Error("Fiches signalétiques : import CSV échoué", ex);
            SendToUi(new { type = "fiches_error", message = ex.Message });
        }
    }

    // Modèle CSV ported tel quel depuis l'outil autonome d'origine (sampleCsv), colonnes dans
    // le même ordre - garde la compatibilité avec les fichiers déjà produits par cet outil.
    private const string FichesSignaletiquesTemplateCsv =
        "id_fiche;type_fiche;dossier;chantier;reference;indice;date_edition;date_determination;client;prestataire;commune;departement;adresse;site;point;nature;systeme_plani;systeme_alti;epsg_source;ppm;x;y;z;latitude;longitude;zoom;observations;photo;revision_1;revision_2\r\n"
      + "FS-001;point_de_canevas;P22960;T7 Séquence Tramway Juvisy-Athis-Mons;FS;A;2026-03-30;2026-03-29;NOVATLAS;NOVATLAS;Juvisy-sur-Orge;Essonne;45 Boulevard Romain Rolland;T7 Séquence Tramway;GEO-ST101;Clou implanté dans le béton;RGF93-CC49;IGN69;3949;100.012;1653717.673;8168127.579;86.770;;;19;Implanté dans le béton sur trottoir. Contrôle visuel OK.;ST101_photo_point.jpg;A | 30/03/2026 | Création fiche | NVT;\r\n"
      + "FS-002;station;018-0084;Site CVE Argenteuil;FS;B;2026-03-30;2026-03-28;NOVATLAS;NOVATLAS;Argenteuil;Val-d'Oise;Rue du Chemin Vert;Site CVE;S.50;Clou d'arpentage rouge – coin bâti;RGF93-CC49;NGF-IGN69;3949;99.995;1641847.080;8195685.860;55.790;;;19;Point de repère pour canevas polygonal.;S50_photo_point.jpg;A | 16/05/2025 | Fiche signalétique | N.E.;B | 20/02/2026 | Mise à jour fiche signalétique | N.E.\r\n";

    private void DownloadFichesSignaletiquesTemplateForUi()
    {
        try
        {
            using var sfd = new SaveFileDialog
            {
                Title = "Enregistrer le modèle CSV - Fiches signalétiques",
                Filter = "CSV (*.csv)|*.csv",
                FileName = "modele_import_fiches_signaletiques.csv",
                InitialDirectory = ExportsDir
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            File.WriteAllText(sfd.FileName, FichesSignaletiquesTemplateCsv, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            AppLog.Error("Fiches signalétiques : téléchargement du modèle CSV échoué", ex);
            MessageBox.Show(this, "Erreur lors de l'enregistrement du modèle CSV.\n\n" + ex.Message, "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Géocodage inverse (Nominatim) des fiches sans adresse : le JS ne filtre que les lignes
    // avec lon/lat résolus et sans adresse (voir m11_fiches_signaletiques.js), donc "targets"
    // ici est déjà le bon sous-ensemble. Appels strictement séquentiels avec un délai de 1.1s
    // entre chacun (politique d'usage Nominatim, ~1 req/s max) - jamais en parallèle. Tourne en
    // fire-and-forget depuis le handler WebMessageReceived (synchrone) : ce await ne bloque donc
    // jamais le thread UI, qui continue de pomper les messages Windows normalement pendant les
    // délais. SendToUi reprend sur le thread UI (SynchronizationContext WinForms capturé par le
    // await), comme il se doit pour toucher le WebView2.
    private async Task GeocodeFichesSignaletiquesAsync(List<(int Index, double Lat, double Lon)> targets)
    {
        if (targets.Count == 0)
        {
            SendToUi(new { type = "fiches_geocode_result", updates = Array.Empty<object>() });
            return;
        }

        var updates = new List<object>();
        for (int i = 0; i < targets.Count; i++)
        {
            var (index, lat, lon) = targets[i];
            try
            {
                var result = await NominatimGeocodingService.ReverseGeocodeAsync(lat, lon);
                if (result != null)
                {
                    updates.Add(new { index, adresse = result.Adresse, commune = result.Commune, departement = result.Departement });
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Fiches signalétiques : géocodage d'une fiche a échoué", ex);
            }

            SendToUi(new { type = "fiches_geocode_progress", current = i + 1, total = targets.Count });

            if (i < targets.Count - 1)
                await Task.Delay(1100);
        }

        SendToUi(new { type = "fiches_geocode_result", updates });
    }

    // Carte de situation "Contrôle classe de précision" - centrée sur les points topo eux-mêmes
    // (ControlePrecisionMapService : auto-détection du système de coordonnées + reprojection
    // WGS84 déjà utilisées pour l'export KMZ, tuiles OSM via MapTileFetcher), plutôt que sur un
    // géocodage d'adresse qui échouait dès que l'adresse saisie n'était pas un format que
    // Nominatim sait résoudre (ex. "Rues X et Y").
    private async Task GenerateCpMapFromPointsAsync(IReadOnlyList<CpPoint> points)
    {
        try
        {
            var result = await ControlePrecisionMapService.BuildMapFromPointsAsync(points);
            if (result == null)
            {
                SendToUi(new { type = "cp_map_result", ok = false, error = "Impossible de géoréférencer les points (système de coordonnées non reconnu)." });
                return;
            }
            SendToUi(new { type = "cp_map_result", ok = true, image = result.ImageDataUrl, coordinateSystem = result.CoordinateSystem });
        }
        catch (Exception ex)
        {
            AppLog.Error("Contrôle classe de précision : génération de la carte échouée", ex);
            SendToUi(new { type = "cp_map_result", ok = false, error = ex.Message });
        }
    }

    // "Une fiche par fichier" : le payload (déjà enrichi lon/lat + brandé) est tranché en un
    // sous-payload par ligne, chacun envoyé au même renderer que le mode "lot complet"
    // (FicheSignaletiqueRenderer ne connaît qu'un tableau de lignes, peu importe sa taille).
    private void ExportFichesSignaletiquesPerFiche(string payloadJson, string buildFooter)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(payloadJson); }
        catch { node = null; }

        if (node is not JsonObject root ||
            root["fichesSignaletiques"] is not JsonObject fiches ||
            fiches["rows"] is not JsonArray rows ||
            rows.Count == 0)
        {
            MessageBox.Show(this, "Aucune fiche à exporter.", "Fiches signalétiques", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SendToUi(new { type = "fiches_export_result", ok = false, error = "Aucune fiche à exporter." });
            return;
        }

        using var fbd = new FolderBrowserDialog
        {
            Description = "Choisir le dossier de destination des fiches signalétiques"
        };
        if (fbd.ShowDialog(this) != DialogResult.OK) return;

        static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        static string FirstNonEmpty(params string?[] vals)
        {
            foreach (var v in vals)
                if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
            return "fiche";
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JsonObject row) continue;

            var fichesSingle = new JsonObject();
            foreach (var kv in fiches)
                fichesSingle[kv.Key] = kv.Key == "rows" ? null : kv.Value?.DeepClone();
            fichesSingle["rows"] = new JsonArray(row.DeepClone());

            var single = new JsonObject();
            foreach (var kv in root)
                single[kv.Key] = kv.Key == "fichesSignaletiques" ? null : kv.Value?.DeepClone();
            single["fichesSignaletiques"] = fichesSingle;

            string baseName = SanitizeFileName(FirstNonEmpty(Str(row["point"]), Str(row["idFiche"]), $"fiche_{i + 1}"));
            string name = baseName;
            int suffix = 2;
            while (!usedNames.Add(name))
                name = $"{baseName}_{suffix++}";

            var outPath = Path.Combine(fbd.SelectedPath, name + ".pdf");
            NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateFichesSignaletiquesFromJson(outPath, single.ToJsonString(), buildFooter);
            count++;
        }

        try { using var process = Process.Start(new ProcessStartInfo { FileName = fbd.SelectedPath, UseShellExecute = true }); } catch { }
        SendToUi(new { type = "fiches_export_result", ok = true, folder = fbd.SelectedPath, count });
    }

    private void ExportKmzForUi(string sourceCrs)
    {
        try
        {
            if (_kmzTxtPoints == null || _kmzTxtPoints.Count == 0 || string.IsNullOrWhiteSpace(_kmzTxtFilePath))
            {
                MessageBox.Show(this, "Importe d'abord un TXT dans le module Export KMZ.", "Export KMZ",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var kmzPoints = _kmzTxtPoints.Select(p => new KmzExportService.KmzPoint(p.Id, p.X, p.Y, p.Z, p.Code, p.HasZ)).ToList();
            var auto = string.IsNullOrWhiteSpace(sourceCrs) ||
                       string.Equals(sourceCrs, "__AUTO__", StringComparison.OrdinalIgnoreCase);
            var crs = auto
                ? KmzExportService.DetectCoordinateSystem(_kmzTxtFilePath, _kmzTxtRawText, kmzPoints).CoordinateSystem
                : sourceCrs;
            var dir = Path.GetDirectoryName(_kmzTxtFilePath) ?? ExportsDir;
            var name = Path.GetFileNameWithoutExtension(_kmzTxtFilePath) + ".kmz";
            var output = Path.Combine(dir, name);

            KmzExportService.ExportPointsToKmz(kmzPoints, crs, output, Path.GetFileNameWithoutExtension(_kmzTxtFilePath));
            AppLog.Info($"KMZ export OK => {output} ({kmzPoints.Count} points, {crs})");
            SendToUi(new { type = "kmz_export_result", ok = true, filePath = output, fileName = Path.GetFileName(output) });
            AfterSuccessfulExport(output);
        }
        catch (Exception ex)
        {
            AppLog.Error("KMZ: export failed", ex);
            MessageBox.Show(this, "Erreur export KMZ.\n\n" + ex.Message, "Export KMZ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SendToUi(new { type = "kmz_export_result", ok = false, error = ex.Message });
        }
    }

    private void PostKmzPreview(string sourceCrs, int rejects, string detectionMethod = "", bool resetSelection = false)
    {
        if (_kmzTxtPoints == null || _kmzTxtPoints.Count == 0 || string.IsNullOrWhiteSpace(_kmzTxtFilePath))
            return;

        var crs = string.IsNullOrWhiteSpace(sourceCrs) ? KmzExportService.GuessCoordinateSystemFromFileName(_kmzTxtFilePath) : sourceCrs;
        var kmzPoints = _kmzTxtPoints.Select(p => new KmzExportService.KmzPoint(p.Id, p.X, p.Y, p.Z, p.Code, p.HasZ)).ToList();
        var preview = KmzExportService.ProjectForPreview(kmzPoints, crs)
            .Select((point, index) => new
            {
                Key = $"TXT{index}",
                point.Id,
                point.X,
                point.Y,
                point.Z,
                point.Code,
                point.Lon,
                point.Lat,
                point.HasZ
            })
            .ToList();
        var outPath = Path.Combine(Path.GetDirectoryName(_kmzTxtFilePath) ?? ExportsDir, Path.GetFileNameWithoutExtension(_kmzTxtFilePath) + ".kmz");
        SendToUi(new
        {
            type = "kmz_txt_loaded",
            fileName = Path.GetFileName(_kmzTxtFilePath),
            filePath = _kmzTxtFilePath,
            outputPath = outPath,
            sourceCrs = crs,
            detectionMethod,
            coordinateSystems = KmzExportService.CoordinateSystems,
            pointCount = preview.Count,
            rejects,
            resetSelection,
            points = preview
        });
    }

    private void ExportCombinedKmzForUi(JsonElement root)
    {
        try
        {
            string sourceCrs = root.TryGetProperty("sourceCrs", out var crsEl)
                ? (crsEl.GetString() ?? "__AUTO__")
                : "__AUTO__";
            var txtKeys = ReadJsonStringArray(root, "txtPointKeys");
            var layers = ReadJsonStringArray(root, "layers");
            var dxfKeys = ReadJsonStringArray(root, "dxfPointKeys");
            var ngfPoints = ReadJsonNgfPoints(root, "ngfPoints");

            var points = new List<KmzExportService.KmzPoint>();
            if (_kmzTxtPoints is { Count: > 0 })
            {
                var selected = txtKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                points.AddRange(_kmzTxtPoints
                    .Select((point, index) => (point, key: $"TXT{index}"))
                    .Where(item => selected.Contains(item.key))
                    .Select(item => new KmzExportService.KmzPoint(
                        item.point.Id,
                        item.point.X,
                        item.point.Y,
                        item.point.Z,
                        item.point.Code,
                        item.point.HasZ,
                        "TXT")));
            }

            var lines = new List<KmzExportService.KmzLine>();
            var texts = new List<KmzExportService.KmzText>();
            string crs = sourceCrs;
            if (_kmzDxfDocument != null)
            {
                var geometry = BuildSelectedDxfGeometry(sourceCrs, layers, dxfKeys);
                points.AddRange(geometry.Points);
                lines.AddRange(geometry.Lines);
                texts.AddRange(geometry.Texts);
                crs = geometry.CoordinateSystem;
            }
            else if (string.IsNullOrWhiteSpace(crs) || string.Equals(crs, "__AUTO__", StringComparison.OrdinalIgnoreCase))
            {
                var detectionPoints = points;
                crs = KmzExportService.DetectCoordinateSystem(
                    _kmzTxtFilePath ?? "",
                    _kmzTxtRawText,
                    detectionPoints).CoordinateSystem;
            }

            if (points.Count == 0 && lines.Count == 0 && texts.Count == 0 && ngfPoints.Count == 0)
                throw new InvalidOperationException("Aucun élément sélectionné pour l'export KMZ.");

            string basePath = !string.IsNullOrWhiteSpace(_kmzTxtFilePath) ? _kmzTxtFilePath! : _kmzDxfFilePath!;
            string output = Path.Combine(
                Path.GetDirectoryName(basePath) ?? ExportsDir,
                Path.GetFileNameWithoutExtension(basePath) + ".kmz");
            KmzExportService.ExportGeometryToKmz(
                points,
                lines,
                texts,
                crs,
                output,
                Path.GetFileNameWithoutExtension(basePath),
                ngfPoints);
            SendToUi(new { type = "kmz_export_result", ok = true, filePath = output, fileName = Path.GetFileName(output) });
            AfterSuccessfulExport(output);
        }
        catch (Exception ex)
        {
            AppLog.Error("KMZ: combined export failed", ex);
            MessageBox.Show(this, "Erreur export KMZ combiné.\n\n" + ex.Message, "Export KMZ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SendToUi(new { type = "kmz_export_result", ok = false, error = ex.Message });
        }
    }

    private void ImportKmzDxfForUi()
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Importer un DXF pour export KMZ",
                Filter = "Fichiers DXF (*.dxf)|*.dxf|Tous les fichiers (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };
            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            _kmzDxfDocument = DxfKmzService.Load(ofd.FileName);
            _kmzDxfFilePath = ofd.FileName;
            SendDxfKmzState(
                _kmzDxfDocument.Detection.CoordinateSystem,
                _kmzDxfDocument.Layers.Select(layer => layer.Name),
                _kmzDxfDocument.Points.Select(point => point.Key),
                false);
        }
        catch (Exception ex)
        {
            AppLog.Error("KMZ: import DXF failed", ex);
            MessageBox.Show(this, "Erreur import DXF KMZ.\n\n" + ex.Message, "Export KMZ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SendToUi(new { type = "kmz_error", message = ex.Message });
        }
    }

    private void PreviewKmzDxfForUi(JsonElement root)
    {
        try
        {
            if (_kmzDxfDocument == null)
                return;
            string sourceCrs = root.TryGetProperty("sourceCrs", out var crsEl) ? (crsEl.GetString() ?? "__AUTO__") : "__AUTO__";
            var layers = ReadJsonStringArray(root, "layers");
            var pointKeys = ReadJsonStringArray(root, "pointKeys");
            SendDxfKmzState(sourceCrs, layers, pointKeys, false);
        }
        catch (Exception ex)
        {
            AppLog.Error("KMZ: preview DXF failed", ex);
            SendToUi(new { type = "kmz_error", message = ex.Message });
        }
    }

    private void ExportKmzDxfForUi(JsonElement root)
    {
        try
        {
            if (_kmzDxfDocument == null || string.IsNullOrWhiteSpace(_kmzDxfFilePath))
                return;
            string sourceCrs = root.TryGetProperty("sourceCrs", out var crsEl) ? (crsEl.GetString() ?? "__AUTO__") : "__AUTO__";
            var layers = ReadJsonStringArray(root, "layers");
            var pointKeys = ReadJsonStringArray(root, "pointKeys");
            var geometry = BuildSelectedDxfGeometry(sourceCrs, layers, pointKeys);

            string output = Path.Combine(
                Path.GetDirectoryName(_kmzDxfFilePath) ?? ExportsDir,
                Path.GetFileNameWithoutExtension(_kmzDxfFilePath) + ".kmz");
            KmzExportService.ExportGeometryToKmz(
                geometry.Points,
                geometry.Lines,
                geometry.Texts,
                geometry.CoordinateSystem,
                output,
                Path.GetFileNameWithoutExtension(_kmzDxfFilePath));
            SendToUi(new { type = "kmz_export_result", ok = true, filePath = output, fileName = Path.GetFileName(output) });
            AfterSuccessfulExport(output);
        }
        catch (Exception ex)
        {
            AppLog.Error("KMZ: export DXF failed", ex);
            MessageBox.Show(this, "Erreur export DXF vers KMZ.\n\n" + ex.Message, "Export KMZ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SendToUi(new { type = "kmz_export_result", ok = false, error = ex.Message });
        }
    }

    private void SendDxfKmzState(
        string sourceCrs,
        IEnumerable<string> selectedLayers,
        IEnumerable<string> selectedPointKeys,
        bool exported)
    {
        if (_kmzDxfDocument == null || string.IsNullOrWhiteSpace(_kmzDxfFilePath))
            return;

        var geometry = BuildSelectedDxfGeometry(sourceCrs, selectedLayers, selectedPointKeys);
        bool automatic = string.IsNullOrWhiteSpace(sourceCrs) ||
                         string.Equals(sourceCrs, "__AUTO__", StringComparison.OrdinalIgnoreCase);
        var previewPoints = KmzExportService.ProjectForPreview(geometry.Points, geometry.CoordinateSystem);
        var previewLines = KmzExportService.ProjectLinesForPreview(geometry.Lines, geometry.CoordinateSystem);
        var previewTexts = KmzExportService.ProjectTextsForPreview(geometry.Texts, geometry.CoordinateSystem);

        SendToUi(new
        {
            type = "kmz_dxf_loaded",
            fileName = _kmzDxfDocument.FileName,
            filePath = _kmzDxfFilePath,
            outputPath = Path.Combine(Path.GetDirectoryName(_kmzDxfFilePath) ?? ExportsDir, Path.GetFileNameWithoutExtension(_kmzDxfFilePath) + ".kmz"),
            sourceCrs = geometry.CoordinateSystem,
            detectionMethod = automatic ? _kmzDxfDocument.Detection.Method : "choix manuel",
            coordinateSystems = KmzExportService.CoordinateSystems,
            layers = _kmzDxfDocument.Layers,
            points = _kmzDxfDocument.Points,
            selectedLayers = geometry.SelectedLayers,
            selectedPointKeys = geometry.SelectedPointKeys,
            previewPoints,
            previewLines,
            previewTexts,
            exported
        });
    }

    private (string CoordinateSystem,
        List<KmzExportService.KmzPoint> Points,
        List<KmzExportService.KmzLine> Lines,
        List<KmzExportService.KmzText> Texts,
        List<string> SelectedLayers,
        List<string> SelectedPointKeys) BuildSelectedDxfGeometry(
            string sourceCrs,
            IEnumerable<string> selectedLayers,
            IEnumerable<string> selectedPointKeys)
    {
        if (_kmzDxfDocument == null)
            throw new InvalidOperationException("Aucun DXF chargé.");

        string crs = string.IsNullOrWhiteSpace(sourceCrs) ||
                     string.Equals(sourceCrs, "__AUTO__", StringComparison.OrdinalIgnoreCase)
            ? _kmzDxfDocument.Detection.CoordinateSystem
            : sourceCrs;
        var layers = selectedLayers?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        var keys = selectedPointKeys?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();

        var layerSet = layers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keySet = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var points = _kmzDxfDocument.Points
            .Where(point => layerSet.Contains(point.Layer) && keySet.Contains(point.Key))
            .Select(point => new KmzExportService.KmzPoint(
                point.Id,
                point.X,
                point.Y,
                point.Z,
                point.Code,
                point.HasZ,
                "DXF"))
            .ToList();
        var lines = _kmzDxfDocument.Lines
            .Where(line => layerSet.Contains(line.Layer))
            .Select(line => new KmzExportService.KmzLine(
                line.Key,
                line.Layer,
                line.X1,
                line.Y1,
                line.Z1,
                line.X2,
                line.Y2,
                line.Z2,
                HasUsableDxfLineAltitude(line)))
            .ToList();
        var texts = _kmzDxfDocument.Texts
            .Where(text => layerSet.Contains(text.Layer))
            .Select(text => new KmzExportService.KmzText(
                text.Key,
                text.Layer,
                text.Text,
                text.X,
                text.Y,
                text.Z,
                text.HasZ))
            .ToList();
        return (crs, points, lines, texts, layers, keys);
    }

    private static bool HasUsableDxfLineAltitude(DxfKmzService.DxfLine line)
    {
        const double altitudeTolerance = 0.001;
        return line.HasZ &&
               (Math.Abs(line.Z1) > altitudeTolerance || Math.Abs(line.Z2) > altitudeTolerance);
    }

    private static List<string> ReadJsonStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(value => value.Length > 0)
            .ToList();
    }

    private static string BuildFooterLicenseScript()
    {
        return BuildFooterLicenseScript(LicenseService.LoadAndValidate());
    }

    /// <summary>
    /// Ouvre l'écran d'activation depuis une session déjà lancée (menu Aide), pour charger
    /// une nouvelle licence sans avoir à copier le fichier manuellement dans AppData ni à
    /// redémarrer l'application. Réinjecte le script de statut licence/features juste après,
    /// que l'utilisateur ait changé de licence ou simplement fermé la fenêtre.
    /// </summary>
    private async Task ManageLicenseAsync()
    {
        var current = LicenseService.LoadAndValidate();
        using var form = new LicenseForm(current, isStartupFlow: false);
        form.ShowDialog(this);

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(BuildFooterLicenseScript());
        }
        catch (Exception ex)
        {
            AppLog.Error("Footer: rafraîchissement statut licence impossible", ex);
        }
    }

    /// <summary>
    /// Fusionne le branding personnalisé courant (logo/adresse/couleurs, module manager
    /// "Paramètres") dans un payload JSON avant de l'envoyer à un renderer PdfSharpEngine.
    /// PdfSharpEngine ne référence jamais BrandingService (règle NovaFiches -> PdfSharpEngine
    /// à sens unique) : c'est donc ici, côté appelant, que l'injection doit avoir lieu.
    /// Ne bloque jamais la génération PDF : en cas d'erreur, retourne le payload inchangé
    /// (identité NOVATLAS par défaut côté renderer).
    /// </summary>
    internal static string InjectBranding(string payloadJson)
    {
        try
        {
            var branding = BrandingService.LoadOrDefault();
            if (string.IsNullOrWhiteSpace(branding.LogoPngBase64) &&
                string.IsNullOrWhiteSpace(branding.FooterAddress) &&
                string.IsNullOrWhiteSpace(branding.ColorBlueHex) &&
                string.IsNullOrWhiteSpace(branding.ColorOrangeHex))
            {
                return payloadJson; // rien de personnalisé : pas la peine d'alourdir le payload
            }

            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return payloadJson;

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, "branding", StringComparison.OrdinalIgnoreCase))
                        prop.WriteTo(writer);
                }
                writer.WritePropertyName("branding");
                writer.WriteStartObject();
                if (!string.IsNullOrWhiteSpace(branding.LogoPngBase64)) writer.WriteString("logoPngBase64", branding.LogoPngBase64);
                if (!string.IsNullOrWhiteSpace(branding.FooterAddress)) writer.WriteString("footerAddress", branding.FooterAddress);
                if (!string.IsNullOrWhiteSpace(branding.ColorBlueHex)) writer.WriteString("colorBlue", branding.ColorBlueHex);
                if (!string.IsNullOrWhiteSpace(branding.ColorOrangeHex)) writer.WriteString("colorOrange", branding.ColorOrangeHex);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex)
        {
            AppLog.Error("Branding: injection dans le payload PDF impossible, génération avec l'identité par défaut", ex);
            return payloadJson;
        }
    }

    /// <summary>
    /// Construit le script d'injection du statut de licence ET du masquage des modules
    /// complémentaires (attribut HTML <c>data-nf-feature="cle"</c> sur n'importe quel
    /// élément - bouton de nav, section...). Séparé de LoadAndValidate() (qui touche
    /// %LOCALAPPDATA%) pour rester testable sans toucher au disque.
    /// </summary>
    internal static string BuildFooterLicenseScript(LicenseValidationResult result)
    {
        string text;
        string cssClass;

        if (result.IsValid && result.Payload != null)
        {
            var payload = result.Payload;
            if (payload.ExpiresAtUtc.HasValue)
            {
                var daysLeft = (int)Math.Ceiling((payload.ExpiresAtUtc.Value.Date - DateTime.UtcNow.Date).TotalDays);
                var dateStr = payload.ExpiresAtUtc.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                if (daysLeft <= 7)
                {
                    text = $"{payload.LicensedTo} — expire dans {Math.Max(0, daysLeft)} jour(s) ({dateStr})";
                    cssClass = "pill err";
                }
                else if (daysLeft <= 30)
                {
                    text = $"{payload.LicensedTo} — expire dans {daysLeft} jours ({dateStr})";
                    cssClass = "pill warn";
                }
                else
                {
                    text = $"Licence active — {payload.LicensedTo} (jusqu'au {dateStr})";
                    cssClass = "pill";
                }
            }
            else
            {
                text = $"Licence active — {payload.LicensedTo}";
                cssClass = "pill";
            }
        }
        else
        {
            // Ne devrait normalement pas s'afficher : Program.cs bloque avant MainForm si la
            // licence est invalide au demarrage. Filet de securite si elle expire en cours de
            // session (poste laisse ouvert plusieurs jours).
            text = "Licence : " + result.Message;
            cssClass = "pill err";
        }

        var textJs = System.Text.Json.JsonSerializer.Serialize(text);
        var classJs = System.Text.Json.JsonSerializer.Serialize(cssClass);
        // Licence invalide/absente = aucun module complémentaire (échec fermé), pas de
        // tentative de lecture partielle d'une licence corrompue.
        var features = result.IsValid ? (result.Payload?.Features ?? Array.Empty<string>()) : Array.Empty<string>();
        var featuresJs = System.Text.Json.JsonSerializer.Serialize(features);

        // Ecrit directement le footer ET le panneau lateral dans la meme injection :
        // la synchronisation cote HTML (setActive, au clic sur un module) ne se
        // declenchait qu'a la navigation suivante, laissant la pastille absente du
        // panneau lateral tant que l'utilisateur n'avait pas change de module apres
        // le chargement.
        // Meme injection : masque/affiche tout element marque data-nf-feature="cle" selon
        // les modules complementaires actives par la licence (modules manager - fondation
        // pour de futurs modules geres par flag de licence plutot que par masquage en dur).
        // data-nf-feature-any="cle1,cle2" : variante pour les elements (ex. separateur de nav)
        // qui doivent rester visibles des qu'AU MOINS une des cles listees est active, sans
        // etre lies a un seul module (utile des que 2+ modules manager independants existent).
        return "(function(){"
             + "['footerLicense','sbLicense'].forEach(function(id){var el=document.getElementById(id); if(el){ el.textContent=" + textJs + "; el.className=" + classJs + "; el.classList.remove('nf-hidden'); }});"
             + "var feats=" + featuresJs + ";"
             + "document.querySelectorAll('[data-nf-feature]').forEach(function(el){ if(feats.indexOf(el.getAttribute('data-nf-feature'))===-1){ el.classList.add('nf-hidden'); } else { el.classList.remove('nf-hidden'); } });"
             + "document.querySelectorAll('[data-nf-feature-any]').forEach(function(el){ var keys=el.getAttribute('data-nf-feature-any').split(',').map(function(k){return k.trim();}); var any=keys.some(function(k){return feats.indexOf(k)!==-1;}); if(any){ el.classList.remove('nf-hidden'); } else { el.classList.add('nf-hidden'); } });"
             + "})();";
    }

    private static double ReadJsonDouble(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
            return 0d;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
            return number;
        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        return 0d;
    }

    private static List<KmzExportService.KmzNgfPoint> ReadJsonNgfPoints(JsonElement root, string property)
    {
        var result = new List<KmzExportService.KmzNgfPoint>();
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("lon", out var lonEl) || !item.TryGetProperty("lat", out var latEl)) continue;
            if (lonEl.ValueKind != JsonValueKind.Number || latEl.ValueKind != JsonValueKind.Number) continue;

            string id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() ?? "" : "";
            string nom = item.TryGetProperty("nom", out var nomEl) && nomEl.ValueKind == JsonValueKind.String ? nomEl.GetString() ?? id : id;
            string? etat = item.TryGetProperty("etat", out var etatEl) && etatEl.ValueKind == JsonValueKind.String ? etatEl.GetString() : null;
            double? altitude = item.TryGetProperty("altitude", out var altEl) && altEl.ValueKind == JsonValueKind.Number && altEl.TryGetDouble(out var alt) ? alt : null;

            result.Add(new KmzExportService.KmzNgfPoint(id, nom, etat, altitude, lonEl.GetDouble(), latEl.GetDouble()));
        }

        return result;
    }

    private void TriggerExportTxt()
    {
        try
        {
            // Demande au front-end de générer le TXT XYZC (onglet Échanges)
            PostToWebIfReady(new { type = "exportTxtXYZC" });
        }
        catch (Exception ex)
        {
            AppLog.Error("Échanges: TriggerExportTxt failed", ex);
            MessageBox.Show(this, ex.Message, "Export TXT", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PostToWebIfReady(object payload)
    {
        // CoreWebView2 n'est accessible que depuis le thread UI (COM) - les handlers async qui
        // enchaînent des await avec .ConfigureAwait(false) (convention de ce fichier pour tout
        // appel réseau, voir MapTileFetcher) reprennent sur un thread de pool après le premier
        // await, donc un appel direct échoue silencieusement (exception attrapée ci-dessous,
        // jamais montrée à l'écran) - repéré concrètement sur un handler envoyant plusieurs
        // messages de progression successifs, dont tous ceux après le premier échouaient ainsi.
        // On se re-poste sur le thread UI plutôt que d'imposer ConfigureAwait(true) à chaque
        // appelant.
        if (InvokeRequired)
        {
            try { BeginInvoke((Action)(() => PostToWebIfReady(payload))); }
            catch (Exception ex) { AppLog.Error("Échanges: BeginInvoke vers le thread UI a échoué", ex); }
            return;
        }

        try
        {
            if (_webView?.CoreWebView2 == null)
                return;
            var json = JsonSerializer.Serialize(payload);
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            AppLog.Error("Échanges: PostWebMessageAsJson failed", ex);
        }
    }

    // Alias used by PdfSharp callbacks (UI comfort messages)
    private void SendToUi(object payload) => PostToWebIfReady(payload);

    private sealed record ExchangePoint(string Id, double X, double Y, double Z, string? Code, bool HasZ = true);
    private sealed record ExchangeObservation(string Id, double? Hz, double? V, double? Sd, double? PrismH, double? InstH, string? Code);


    private static (List<ExchangePoint> points, List<(int lineNo, string reason)> rejects) ParseTxtPoints(string text)
    {
        var points = new List<ExchangePoint>();
        var rejects = new List<(int, string)>();

        if (string.IsNullOrWhiteSpace(text))
            return (points, rejects);

        // Split lines robustly (Windows + Unix). Important: do NOT use Environment.NewLine because
        // input files may contain mixed line endings.
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        bool headerSkipped = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0) continue;
            if (raw.StartsWith("#") || raw.StartsWith("//")) continue;

            // Normalize separators: tabs/;,/spaces
            var norm = raw.Replace(';', ' ').Replace(',', '.');
            var parts = SplitByWhitespace(norm);

            // Header detection: column EXACTLY "X"/"Y"/"Z"/"N" (not just a substring anywhere in
            // the line) - bug found in an audit: the old check (raw.Contains("X") etc. against the
            // whole raw line) tripped on an alphanumeric point ID like "PTX12" or "BORNE-N2" and
            // silently dropped the file's very first point, no reject/warning. Comparing columns
            // one by one (rather than scanning the line for the letters) also keeps a single
            // malformed data line (no real header) correctly rejected with a reason instead of
            // silently swallowed.
            if (!headerSkipped)
            {
                headerSkipped = true;
                if (raw.IndexOf('°') >= 0 || parts.Any(p =>
                        p.Equals("X", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("N", StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            if (parts.Count < 3)
            {
                rejects.Add((i + 1, "Colonnes insuffisantes"));
                continue;
            }

            string id = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                rejects.Add((i + 1, "Id point vide"));
                continue;
            }

            if (!TryParseDouble(parts[1], out double x) || !TryParseDouble(parts[2], out double y))
            {
                rejects.Add((i + 1, "X/Y/Z non numériques"));
                continue;
            }

            double z = 0d;
            bool hasZ = parts.Count >= 4 && TryParseDouble(parts[3], out z);

            string? code = null;
            int codeIndex = hasZ ? 4 : 3;
            if (parts.Count > codeIndex)
                code = parts[codeIndex].Trim();

            points.Add(new ExchangePoint(id, x, y, z, string.IsNullOrWhiteSpace(code) ? null : code, hasZ));
        }

        return (points, rejects);
    }

    private static (List<ExchangePoint> points, List<ExchangeObservation> obs, int rejects, string mode, bool coords81_82_83) ParseGsi(string text)
    {
        var points = new List<ExchangePoint>();
        var obs = new List<ExchangeObservation>();
        int rejects = 0;

        bool sawCoords81 = false;
        bool sawCoords21 = false;
        bool sawObs = false;

        if (string.IsNullOrWhiteSpace(text))
            return (points, obs, 0, "none", false);

        // Split lines robustly (Windows + Unix). Input files may contain mixed line endings.
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0) continue;
            if (!raw.StartsWith("*")) continue;

            var parts = SplitByWhitespace(raw);
            if (parts.Count < 2) { rejects++; continue; }

            // First token like *110001+0000000000000C.4
            var first = parts[0];
            var plusIdx = first.IndexOf('+');
            if (plusIdx < 0 || plusIdx >= first.Length - 1) { rejects++; continue; }
            var idRaw = first[(plusIdx + 1)..].Trim();
            var id = TrimLeadingZeros(idRaw);
            if (string.IsNullOrWhiteSpace(id)) id = idRaw;

            long? v81 = null, v82 = null, v83 = null;
            long? v21 = null, v22 = null, v31 = null;
            long? v87 = null, v88 = null;
            string? code71 = null;

            for (int p = 1; p < parts.Count; p++)
            {
                var token = parts[p];
                int signPos = FindSignPos(token);
                if (signPos <= 0) continue;
                var tag = token[..signPos];
                var valStr = token[signPos..];

                if (tag.Length < 2) continue;
                if (!int.TryParse(tag[..2], out int wi)) continue;

                // WI71 can be alphanum; keep raw
                if (wi == 71)
                {
                    code71 = valStr.TrimStart('+');
                    continue;
                }

                if (!TryParseLong(valStr, out long val)) continue;

                if (wi == 81) v81 = val;
                else if (wi == 82) v82 = val;
                else if (wi == 83) v83 = val;
                else if (wi == 21) v21 = val;
                else if (wi == 22) v22 = val;
                else if (wi == 31) v31 = val;
                else if (wi == 87) v87 = val;
                else if (wi == 88) v88 = val;
            }

            // 1) Standard coordinates via WI81/82/83
            if (v81.HasValue && v82.HasValue && v83.HasValue)
            {
                sawCoords81 = true;
                double x = v81.Value / 1000.0;
                double y = v82.Value / 1000.0;
                double z = v83.Value / 1000.0;
                points.Add(new ExchangePoint(id, x, y, z, string.IsNullOrWhiteSpace(code71) ? null : code71));
                continue;
            }

            // 2) Some user-defined output masks export coords but labelled as WI21/22/31.
            // Heuristic: E/N are typically large (mm), while angles are bounded.
            if (v21.HasValue && v22.HasValue && v31.HasValue)
            {
                long a = Math.Abs(v21.Value);
                long b = Math.Abs(v22.Value);
                long c = Math.Abs(v31.Value);

                bool looksLikeCoords = (a > 1000000 && b > 1000000 && c < 100000000);
                if (looksLikeCoords)
                {
                    sawCoords21 = true;
                    double x = v21.Value / 1000.0;
                    double y = v22.Value / 1000.0;
                    double z = v31.Value / 1000.0;
                    points.Add(new ExchangePoint(id, x, y, z, string.IsNullOrWhiteSpace(code71) ? null : code71));
                    continue;
                }

                // 3) Otherwise treat as observations (Hz/V/SD)
                sawObs = true;
                double? hz = v21.HasValue ? v21.Value / 10000.0 : null;   // typical: 0.0001 gon/deg depending on instrument settings
                double? vv = v22.HasValue ? v22.Value / 10000.0 : null;
                double? sd = v31.HasValue ? v31.Value / 1000.0 : null;    // typical: mm -> m
                double? ph = v87.HasValue ? v87.Value / 1000.0 : null;    // mm -> m
                double? ih = v88.HasValue ? v88.Value / 1000.0 : null;

                obs.Add(new ExchangeObservation(id, hz, vv, sd, ph, ih, string.IsNullOrWhiteSpace(code71) ? null : code71));
                continue;
            }

            // No usable fields
            rejects++;
        }

        string mode;
        if (sawCoords81) mode = "coords_81";
        else if (sawCoords21) mode = "coords_21";
        else if (sawObs) mode = "obs";
        else mode = "none";

        return (points, obs, rejects, mode, sawCoords81);
    }


    private static List<string> SplitByWhitespace(string s)
    {
        var list = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            list.Add(s[start..i]);
        }
        return list;
    }

    private static bool TryParseDouble(string s, out double d)
    {
        // Accept dot or comma decimal (we pre-normalize commas to dots)
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
    }

    private static bool TryParseLong(string signed, out long value)
    {
        // Accept +0000123 / -0000123
        signed = signed.Trim();
        if (signed.StartsWith("+")) signed = signed[1..];
        return long.TryParse(signed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static int FindSignPos(string token)
    {
        // Find first '+' or '-' after the first char (to avoid leading '*')
        for (int i = 1; i < token.Length; i++)
        {
            if (token[i] == '+' || token[i] == '-') return i;
        }
        return -1;
    }

    private static string TrimLeadingZeros(string idRaw)
    {
        if (string.IsNullOrWhiteSpace(idRaw)) return "";
        int i = 0;
        while (i < idRaw.Length && idRaw[i] == '0') i++;
        var trimmed = idRaw[i..];
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    // Export Save As - via DownloadStarting (PDF typiquement)
    private void HookDownloadSaveAs(WebView2 view)
    {
        if (view.CoreWebView2 == null) return;

        view.CoreWebView2.DownloadStarting += (_, e) =>
        {
            try
            {
                string suggestedName = GetSuggestedFileName(e) ?? "export";

// jsPDF downloads are often blob: and suggest a generic name (e.g. "export").
// If JS provided the intended name, prefer it.
if ((string.Equals(suggestedName, "export", StringComparison.OrdinalIgnoreCase)
     || string.Equals(Path.GetFileNameWithoutExtension(suggestedName), "export", StringComparison.OrdinalIgnoreCase))
    && !string.IsNullOrWhiteSpace(_nextDownloadFileName))
{
    suggestedName = _nextDownloadFileName!;
    _nextDownloadFileName = null; // one-shot
}

                if (string.IsNullOrWhiteSpace(suggestedName)) suggestedName = "export";

                bool hadExt = !string.IsNullOrWhiteSpace(Path.GetExtension(suggestedName));

                using var sfd = new SaveFileDialog
                {
                    Title = "Enregistrer sous...",
                    FileName = hadExt ? suggestedName : Path.GetFileNameWithoutExtension(suggestedName),
                    OverwritePrompt = true,
                    AddExtension = true,
                    InitialDirectory = ExportsDir,
                };

                if (!hadExt)
                {
                    sfd.Filter = "PDF (*.pdf)|*.pdf|Tous les fichiers (*.*)|*.* ";
                    sfd.FilterIndex = 1;   // PDF par défaut
                    sfd.DefaultExt = "pdf";
                }
                else
                {
                    sfd.Filter = BuildFilterFromFileName(suggestedName);
                    sfd.DefaultExt = Path.GetExtension(suggestedName);
                }

                if (sfd.ShowDialog(this) != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }

                string chosenPath = sfd.FileName;

                if (string.IsNullOrWhiteSpace(Path.GetExtension(chosenPath)) && !hadExt)
                {
                    chosenPath += sfd.FilterIndex switch
                    {
                        1 => ".pdf",
                        _ => ".bin"
                    };
                }

                if (TrySetResultFilePathIfPossible(e, chosenPath))
                {
                    TrySetHandledIfPossible(e, true);
                    AppLog.Info($"Download saveAs OK (ResultFilePath): {chosenPath}");

                    AfterSuccessfulExport(chosenPath);
                    return;
                }

                // Fallback temp + move
                string tempPath = Path.Combine(Path.GetTempPath(),
                    $"NovaFiches_{Guid.NewGuid():N}{Path.GetExtension(chosenPath)}");

                if (TrySetResultFilePathIfPossible(e, tempPath))
                {
                    TrySetHandledIfPossible(e, true);

                    e.DownloadOperation.StateChanged += (_, __) =>
                    {
                        try
                        {
                            if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                            {
                                if (File.Exists(tempPath))
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(chosenPath)!);

                                    if (File.Exists(chosenPath))
                                        File.Delete(chosenPath);

                                    File.Move(tempPath, chosenPath);
                                    BeginInvoke(new Action(() =>
                                    {
                                        AfterSuccessfulExport(chosenPath);
                                    }));
                                    AppLog.Info($"Download saveAs OK (temp move): {chosenPath}");
                                }
                            }
                            else if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
                            {
                                AppLog.Error($"Download interrupted: {e.DownloadOperation.InterruptReason}");
                                TryDeleteQuiet(tempPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLog.Error("Download temp move failed", ex);
                            TryDeleteQuiet(tempPath);
                        }
                    };

                    return;
                }

                e.Cancel = true;
                AppLog.Error("SaveAs impossible : API WebView2 incompatible (ResultFilePath non accessible).");
                MessageBox.Show(this,
                    "Export impossible sur cette version de WebView2 (API download non compatible).\nVoir logs.",
                    "Nova-Fiches",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                AppLog.Error("DownloadStarting handler failed", ex);
                try { e.Cancel = true; } catch { }

                MessageBox.Show(this, "Erreur export. Voir logs.", "Nova-Fiches",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }

    // Export Save As - via WebMessage (PDF en base64)
    private void HookWebMessageSaveAs(WebView2 view)
    {
        if (view.CoreWebView2 == null) return;

        view.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeEl)) return;
                
var type = typeEl.GetString()?.Trim();

if (string.Equals(type, "nextDownloadName", StringComparison.OrdinalIgnoreCase))
{
    string fn = root.TryGetProperty("fileName", out var fn2) ? (fn2.GetString() ?? "") : "";
    if (!string.IsNullOrWhiteSpace(fn))
    {
        _nextDownloadFileName = SanitizeFileName(fn);
        AppLog.Info($"NextDownloadName set: {_nextDownloadFileName}");
    }
    return;
}

                // Affichage de messages d'erreur côté UI (fenêtre WinForms)
                if (string.Equals(type, "ui_error", StringComparison.OrdinalIgnoreCase))
                {
                    string msg = root.TryGetProperty("message", out var mEl) ? (mEl.GetString() ?? "") : "";
                    msg = msg.Trim();
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show(this,
                                msg,
                                "Nova-Fiches",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }));
                    }
                    return;
                }

                if (string.Equals(type, "kmz_import_txt", StringComparison.OrdinalIgnoreCase))
                {
                    string crs = root.TryGetProperty("sourceCrs", out var crsImportEl) ? (crsImportEl.GetString() ?? "") : "";
                    ImportKmzTxtForUi(crs);
                    return;
                }

                if (string.Equals(type, "kmz_reproject", StringComparison.OrdinalIgnoreCase))
                {
                    string crs = root.TryGetProperty("sourceCrs", out var crsReprojEl) ? (crsReprojEl.GetString() ?? "") : "";
                    ReprojectKmzForUi(crs);
                    return;
                }

                if (string.Equals(type, "kmz_export", StringComparison.OrdinalIgnoreCase))
                {
                    string crs = root.TryGetProperty("sourceCrs", out var crsEl) ? (crsEl.GetString() ?? "") : "";
                    ExportKmzForUi(crs);
                    return;
                }

                if (string.Equals(type, "kmz_import_dxf", StringComparison.OrdinalIgnoreCase))
                {
                    ImportKmzDxfForUi();
                    return;
                }

                if (string.Equals(type, "kmz_preview_dxf", StringComparison.OrdinalIgnoreCase))
                {
                    PreviewKmzDxfForUi(root);
                    return;
                }

                if (string.Equals(type, "kmz_export_dxf", StringComparison.OrdinalIgnoreCase))
                {
                    ExportKmzDxfForUi(root);
                    return;
                }

                if (string.Equals(type, "kmz_export_combined", StringComparison.OrdinalIgnoreCase))
                {
                    ExportCombinedKmzForUi(root);
                    return;
                }

                if (string.Equals(type, "kmz_fetch_ngf", StringComparison.OrdinalIgnoreCase))
                {
                    double minLon = ReadJsonDouble(root, "minLon");
                    double minLat = ReadJsonDouble(root, "minLat");
                    double maxLon = ReadJsonDouble(root, "maxLon");
                    double maxLat = ReadJsonDouble(root, "maxLat");
                    _ = FetchNgfForUiAsync(minLon, minLat, maxLon, maxLat);
                    return;
                }

                if (string.Equals(type, "station_map_reproject", StringComparison.OrdinalIgnoreCase))
                {
                    ReprojectStationMapForUi(root);
                    return;
                }


                // ===== PdfSharp TEST: implantation =====
                if (string.Equals(type, "pdfsharp_implantation", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var payloadJson = root.GetRawText();
                        TryWritePdfSharpDebugPayload("pdfsharp_implantation", payloadJson);
                        payloadJson = InjectBranding(payloadJson);
                        // Extract minimal fields
                        string title = root.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "IMPLANTATION") : "IMPLANTATION";
                        string subTitle = root.TryGetProperty("subTitle", out var stEl) ? (stEl.GetString() ?? "") : "";
                        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl) ? (JsonElToString(fEl) ?? "NOVA_Implantation_PdfSharp.pdf") : "NOVA_Implantation_PdfSharp.pdf";

                        string[] header = Array.Empty<string>();
                        if (root.TryGetProperty("header", out var hEl) && hEl.ValueKind == JsonValueKind.Array)
                        {
                            header = hEl.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                        }

                        var rows = new List<string[]>();
                        if (root.TryGetProperty("rows", out var rEl) && rEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var rowEl in rEl.EnumerateArray())
                            {
                                if (rowEl.ValueKind != JsonValueKind.Array) continue;
                                rows.Add(rowEl.EnumerateArray().Select(x => x.GetString() ?? "").ToArray());
                            }
                        }

                        using var sfd = new SaveFileDialog();
                        sfd.Title = "Enregistrer le PDF (PdfSharp TEST)";
                        sfd.Filter = "PDF (*.pdf)|*.pdf";
                        sfd.FileName = SanitizeFileName(fileNameSuggested);
                        sfd.InitialDirectory = ExportsDir;

                        if (sfd.ShowDialog(this) != DialogResult.OK) return;

                        var dto = new NovaFiches.PdfSharpEngine.ImplantationTablePayload
                        {
                            Title = title,
                            SubTitle = subTitle,
                            Header = header.Length > 0 ? header : new[] { "ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT" },
                            Rows = rows,
                            FooterLeft = "NOVATLAS — Nova-Fiches",
                        };
                        // PDF footer version (simple)
                        var proof = GetPdfFooterVersion();
                        NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateImplantationFullFromJson(sfd.FileName, payloadJson, proof);

                        try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

                        // Comfort: signal end of generation to the WebView UI.
                        SendToUi(new { type = "pdf_result", report = "implantation", ok = true, filePath = sfd.FileName });
                    }
                    catch (Exception ex2)
                    {
                        AppLog.Error("PdfSharp implantation generation failed", ex2);
                        MessageBox.Show(this, "Erreur génération PDF (PdfSharp).\n\n" + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        SendToUi(new { type = "pdf_result", report = "implantation", ok = false, error = ex2.Message });
                    }
                    return;
                }

                // ===== PdfSharp: rapport complet (cover + tables) =====
                
                // ===== PdfSharp: rapport complet V2 (cover + IMP full + LIGNE REF full) =====
                if (string.Equals(type, "pdfsharp_rapport_complet_v2", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl) ? (fEl.GetString() ?? "NOVA_RapportComplet_PdfSharp.pdf") : "NOVA_RapportComplet_PdfSharp.pdf";

                        // Info block (key/value) for the cover page
                        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (root.TryGetProperty("info", out var infoEl) && infoEl.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in infoEl.EnumerateObject())
                                info[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? (prop.Value.GetString() ?? "") : prop.Value.ToString();
                        }

                        // Expect nested payload objects (raw JSON) coming from the UI
                        string impJson = "";
                        if (root.TryGetProperty("implantationPayload", out var impEl))
                            impJson = impEl.GetRawText();

                        string lrJson = "";
                        if (root.TryGetProperty("ligneRefPayload", out var lrEl))
                            lrJson = lrEl.GetRawText();
                        TryWritePdfSharpDebugPayload("pdfsharp_rapport_complet_v2", root.GetRawText());
                        impJson = InjectBranding(impJson);
                        lrJson = InjectBranding(lrJson);

                        using var sfd = new SaveFileDialog
                        {
                            Title = "Enregistrer le PDF (Rapport complet PdfSharp)",
                            Filter = "PDF (*.pdf)|*.pdf",
                            FileName = SanitizeFileName(fileNameSuggested),
                            InitialDirectory = ExportsDir
                        };
                        if (sfd.ShowDialog(this) != DialogResult.OK) return;
                        // PDF footer version (simple)
                        var proof = GetPdfFooterVersion();

                        NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateRapportCompletV2(
                            sfd.FileName,
                            info,
                            impJson,
                            lrJson,
                            proof);

                        try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { /* ignore */ }

                        SendToUi(new { type = "pdf_result", report = "rapport_complet_v2", ok = true, filePath = sfd.FileName });
                    }
                    catch (Exception ex2)
                    {
                        MessageBox.Show(this, "Erreur génération PDF (PdfSharp).\n\n" + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);

                        SendToUi(new { type = "pdf_result", report = "rapport_complet_v2", ok = false, error = ex2.Message });
                    }
                    return;
                }

if (string.Equals(type, "pdfsharp_rapport_complet", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl) ? (JsonElToString(fEl) ?? "NOVA_RapportComplet_PdfSharp.pdf") : "NOVA_RapportComplet_PdfSharp.pdf";

                        static string JsonElToString(JsonElement el)
                        {
                            // Some payloads (Mesure sur ligne in rapport complet) can contain
                            // rich cells like: {"content":"1","__kind":"LINE",...}
                            if (el.ValueKind == JsonValueKind.Object)
                            {
                                try
                                {
                                    if (el.TryGetProperty("content", out var contentEl))
                                        return JsonElToString(contentEl);
                                }
                                catch { /* ignore */ }
                            }

                            return el.ValueKind switch
                            {
                                JsonValueKind.String => el.GetString() ?? "",
                                JsonValueKind.Number => el.ToString(),
                                JsonValueKind.True => "true",
                                JsonValueKind.False => "false",
                                JsonValueKind.Null => "",
                                JsonValueKind.Undefined => "",
                                _ => el.GetRawText()
                            };
                        }



                        // Info block (key/value)
                        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (root.TryGetProperty("info", out var infoEl) && infoEl.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in infoEl.EnumerateObject())
                                info[prop.Name] = JsonElToString(prop.Value);
                        }

                        // Table helper
                        static NovaFiches.PdfSharpEngine.ImplantationTablePayload ReadTable(JsonElement root, string propName)
                        {
                            var t = new NovaFiches.PdfSharpEngine.ImplantationTablePayload();
                            if (!root.TryGetProperty(propName, out var tEl) || tEl.ValueKind != JsonValueKind.Object) return t;

                            t.Title = tEl.TryGetProperty("title", out var tt) ? JsonElToString(tt) : "";
                            t.SubTitle = tEl.TryGetProperty("subTitle", out var st) ? JsonElToString(st) : "";

                            if (tEl.TryGetProperty("header", out var hEl) && hEl.ValueKind == JsonValueKind.Array)
                                t.Header = hEl.EnumerateArray().Select(JsonElToString).ToArray();

                            if (tEl.TryGetProperty("rows", out var rEl) && rEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var rowEl in rEl.EnumerateArray())
                                {
                                    if (rowEl.ValueKind != JsonValueKind.Array) continue;
                                    t.Rows.Add(rowEl.EnumerateArray().Select(JsonElToString).ToArray());
                                }
                            }
                            return t;
                        }

                        var imp = ReadTable(root, "implantation");
                        var ligne = ReadTable(root, "mesureSurLigne");
                        TryWritePdfSharpDebugPayload("pdfsharp_rapport_complet", root.GetRawText());


                        using var sfd = new SaveFileDialog
                        {
                            Title = "Enregistrer le PDF (Rapport complet PdfSharp)",
                            Filter = "PDF (*.pdf)|*.pdf",
                            FileName = SanitizeFileName(fileNameSuggested),
                            InitialDirectory = ExportsDir
                        };
                        if (sfd.ShowDialog(this) != DialogResult.OK) return;
                        // PDF footer version (simple)
                        var proof = GetPdfFooterVersion();

                        var dto = new NovaFiches.PdfSharpEngine.RapportCompletPayload
                        {
                            Info = info,
                            Implantation = imp,
                            MesureSurLigne = ligne
                        };
                        // Default headers if missing
                        var defaultHeader = new[] { "ID point", "X théo/calc", "Y théo/calc", "Z théo/calc", "X mes", "Y mes", "Z mes", "Dx / dL", "Dy / dT", "Dz / dA", "STATUT" };
                        dto.Implantation.Header ??= defaultHeader;
                        dto.MesureSurLigne.Header ??= defaultHeader;
                        dto.Implantation.FooterLeft ??= "NOVATLAS — Nova-Fiches";
                        dto.MesureSurLigne.FooterLeft ??= "NOVATLAS — Nova-Fiches";

                        NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateRapportComplet(sfd.FileName, dto, proof);
                        try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

                        SendToUi(new { type = "pdf_result", report = "rapport_complet", ok = true, filePath = sfd.FileName });
                    }
                    catch (Exception ex2)
                    {
                        AppLog.Error("PdfSharp rapport complet generation failed", ex2);
                        MessageBox.Show(this, "Erreur génération PDF (Rapport complet PdfSharp).\n\n" + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        try { SendToUi(new { type = "pdf_result", report = "rapport_complet", ok = false, error = ex2.Message }); } catch { }
                    }
                    return;
                }

                // ===== PdfSharp: ligne de référence (rabattement) =====
                if (string.Equals(type, "pdfsharp_ligne_reference", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl)
                            ? (JsonElToString(fEl) ?? "NOVA_LigneDeReference_PdfSharp.pdf")
                            : "NOVA_LigneDeReference_PdfSharp.pdf";

                        var payloadJson = e.WebMessageAsJson;
                        TryWritePdfSharpDebugPayload("pdfsharp_ligne_reference", payloadJson);
                        payloadJson = InjectBranding(payloadJson);

                        using var sfd = new SaveFileDialog
                        {
                            Title = "Enregistrer le PDF - Ligne de référence (PdfSharp)",
                            Filter = "PDF (*.pdf)|*.pdf",
                            FileName = fileNameSuggested,
                            InitialDirectory = ExportsDir
                        };

                        if (sfd.ShowDialog(this) == DialogResult.OK)
                        {
                            var b = Application.ProductVersion ?? "0";
                            var shaShort = "";
                            try
                            {
	                                var assetsRoot = Path.Combine(AppContext.BaseDirectory, "assets");
	                                var sha = ComputeAssetsSha256(assetsRoot);
                                shaShort = sha.Substring(0, Math.Min(8, sha.Length));
                            }
                            catch { /* ignore */ }

						// Footer PDF: keep it simple and user-facing.
						var proof = GetPdfFooterVersion();
                            NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateLigneReferenceFromJson(sfd.FileName, payloadJson, proof);

                            try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

                            SendToUi(new { type = "pdf_result", report = "ligne_reference", ok = true, filePath = sfd.FileName });
                        }
                    }
                    catch (Exception ex2)
                    {
                        AppLog.Error("PdfSharp ligne reference generation failed", ex2);
                        MessageBox.Show(this, "Erreur génération PDF (PdfSharp).\n\n" + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);

                        try { SendToUi(new { type = "pdf_result", report = "ligne_reference", ok = false, error = ex2.Message }); } catch { /* ignore */ }
                    }

                    return;
                }

                
// ===== PdfSharp: points topo (levé) =====
if (string.Equals(type, "pdfsharp_points_topo", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl)
            ? (JsonElToString(fEl) ?? "NOVA_PointsTopo_PdfSharp.pdf")
            : "NOVA_PointsTopo_PdfSharp.pdf";

        var payloadJson = e.WebMessageAsJson;
        TryWritePdfSharpDebugPayload("pdfsharp_points_topo", payloadJson);
        payloadJson = InjectBranding(payloadJson);

        using var sfd = new SaveFileDialog
        {
            Title = "Exporter PDF (Points topo)",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = SanitizeFileName(fileNameSuggested),
            InitialDirectory = ExportsDir
        };

        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            string b = Application.ProductVersion ?? "";
            string shaShort = "";
            try
            {
                var assetsRoot = Path.Combine(AppContext.BaseDirectory, "assets");
                var sha = ComputeAssetsSha256(assetsRoot);
                shaShort = string.IsNullOrWhiteSpace(sha) ? "" : sha.Substring(0, Math.Min(8, sha.Length));
            }
            catch { /* ignore */ }
			// Footer PDF: keep it simple and user-facing.
			var proof = GetPdfFooterVersion();

            NovaFiches.PdfSharpEngine.PdfSharpReports.GeneratePointsTopoFromJson(sfd.FileName, payloadJson, proof);
            try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

            SendToUi(new { type = "pdf_result", report = "points_topo", ok = true, filePath = sfd.FileName });
        }
    }
    catch (Exception ex2)
    {
        AppLog.Error("PdfSharp points topo generation failed", ex2);
        MessageBox.Show(this, "Erreur génération PDF (Points topo PdfSharp).\n\n" + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);

        SendToUi(new { type = "pdf_result", report = "points_topo", ok = false, error = ex2.Message });
    }
    return;
}

// ===== PdfSharp: transfert altitude =====
if (string.Equals(type, "pdfsharp_height_transfer", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl)
            ? (JsonElToString(fEl) ?? "NOVA_Transfert_Altitude.pdf")
            : "NOVA_Transfert_Altitude.pdf";

        var payloadJson = e.WebMessageAsJson;
        TryWritePdfSharpDebugPayload("pdfsharp_height_transfer", payloadJson);
        payloadJson = InjectBranding(payloadJson);

        using var sfd = new SaveFileDialog
        {
            Title = "Exporter PDF (Transfert altitude)",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = SanitizeFileName(fileNameSuggested),
            InitialDirectory = ExportsDir
        };

        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            var proof = GetPdfFooterVersion();
            NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateHeightTransferFromJson(sfd.FileName, payloadJson, proof);
            try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }
            SendToUi(new { type = "pdf_result", report = "height_transfer", ok = true, filePath = sfd.FileName });
        }
    }
    catch (Exception ex2)
    {
        AppLog.Error("PdfSharp height transfer generation failed", ex2);
        MessageBox.Show(this, "Erreur generation PDF (Transfert altitude PdfSharp).\n\n" + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);
        SendToUi(new { type = "pdf_result", report = "height_transfer", ok = false, error = ex2.Message });
    }
    return;
}

// ===== PdfSharp: station (station uniquement) =====
                if (string.Equals(type, "pdfsharp_station", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl)
                            ? (JsonElToString(fEl) ?? "NOVA_Station_PdfSharp.pdf")
                            : "NOVA_Station_PdfSharp.pdf";

                        var payloadJson = EnrichStationPlanViewWithLonLat(e.WebMessageAsJson);
                        TryWritePdfSharpDebugPayload("pdfsharp_station", payloadJson);
                        payloadJson = InjectBranding(payloadJson);

                        using var sfd = new SaveFileDialog
                        {
                            Title = "Enregistrer le PDF - Station (PdfSharp)",
                            Filter = "PDF (*.pdf)|*.pdf",
                            FileName = SanitizeFileName(fileNameSuggested),
                            InitialDirectory = ExportsDir
                        };
                        if (sfd.ShowDialog(this) != DialogResult.OK) return;
                        // PDF footer version (simple)
                        var proof = GetPdfFooterVersion();

                        NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateStationFromJson(sfd.FileName, payloadJson, proof);
                        try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

                        SendToUi(new { type = "pdf_result", report = "station", ok = true, filePath = sfd.FileName });
                    }
                    catch (Exception ex2)
                    {
                        AppLog.Error("PdfSharp station generation failed", ex2);
                        MessageBox.Show(this, "Erreur génération PDF (Station PdfSharp).\n\n" + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);

                        SendToUi(new { type = "pdf_result", report = "station", ok = false, error = ex2.Message });
                    }
                    return;
                }

// ===== PdfSharp: page de garde seule (en-tête + cartouche, corps vide) =====
if (string.Equals(type, "pdfsharp_cover", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl)
            ? (JsonElToString(fEl) ?? "NOVA_Page_de_garde.pdf")
            : "NOVA_Page_de_garde.pdf";

        var payloadJson = e.WebMessageAsJson;
        TryWritePdfSharpDebugPayload("pdfsharp_cover", payloadJson);
        payloadJson = InjectBranding(payloadJson);

        using var sfd = new SaveFileDialog
        {
            Title = "Enregistrer le PDF - Page de garde (PdfSharp)",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = SanitizeFileName(fileNameSuggested),
            InitialDirectory = ExportsDir
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        var proof = GetPdfFooterVersion();

        NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateCoverOnlyFromJson(sfd.FileName, payloadJson, proof);
        try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

        SendToUi(new { type = "pdf_result", report = "cover", ok = true, filePath = sfd.FileName });
    }
    catch (Exception ex2)
    {
        AppLog.Error("PdfSharp cover-only generation failed", ex2);
        MessageBox.Show(this, "Erreur génération PDF (Page de garde).\n\n" + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);
        SendToUi(new { type = "pdf_result", report = "cover", ok = false, error = ex2.Message });
    }
    return;
}

// ===== PdfSharp: reportage photo autonome =====
if (string.Equals(type, "pdfsharp_photo_report", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl)
            ? (JsonElToString(fEl) ?? "NOVA_Reportage_Photo.pdf")
            : "NOVA_Reportage_Photo.pdf";

        var payloadJson = e.WebMessageAsJson;
        TryWritePdfSharpDebugPayload("pdfsharp_photo_report", payloadJson);
        payloadJson = InjectBranding(payloadJson);

        using var sfd = new SaveFileDialog
        {
            Title = "Enregistrer le PDF - Reportage photo",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = SanitizeFileName(fileNameSuggested),
            InitialDirectory = ExportsDir
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var proof = GetPdfFooterVersion();
        NovaFiches.PdfSharpEngine.PdfSharpReports.GeneratePhotoReportFromJson(sfd.FileName, payloadJson, proof);
        try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

        SendToUi(new { type = "pdf_result", report = "reportage_photo", ok = true, filePath = sfd.FileName });
    }
    catch (Exception ex2)
    {
        AppLog.Error("PdfSharp photo report generation failed", ex2);
        MessageBox.Show(this, "Erreur generation PDF (Reportage photo PdfSharp)." + Environment.NewLine + Environment.NewLine + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);
        SendToUi(new { type = "pdf_result", report = "reportage_photo", ok = false, error = ex2.Message });
    }
    return;
}

// ===== Paramètres (module manager "branding") : charger / enregistrer =====
if (string.Equals(type, "params_load", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var branding = BrandingService.LoadOrDefault();
        SendToUi(new
        {
            type = "params_loaded",
            logoPngBase64 = branding.LogoPngBase64,
            footerAddress = branding.FooterAddress,
            colorBlue = branding.ColorBlueHex,
            colorOrange = branding.ColorOrangeHex
        });
    }
    catch (Exception ex2)
    {
        AppLog.Error("Paramètres: chargement du branding impossible", ex2);
        SendToUi(new { type = "params_loaded", logoPngBase64 = (string?)null, footerAddress = (string?)null, colorBlue = (string?)null, colorOrange = (string?)null });
    }
    return;
}

if (string.Equals(type, "params_save", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        string? logoPngBase64 = root.TryGetProperty("logoPngBase64", out var logoEl) && logoEl.ValueKind == JsonValueKind.String ? logoEl.GetString() : null;
        string? footerAddress = root.TryGetProperty("footerAddress", out var addrEl) && addrEl.ValueKind == JsonValueKind.String ? addrEl.GetString() : null;
        string? colorBlue = root.TryGetProperty("colorBlue", out var blueEl) && blueEl.ValueKind == JsonValueKind.String ? blueEl.GetString() : null;
        string? colorOrange = root.TryGetProperty("colorOrange", out var orangeEl) && orangeEl.ValueKind == JsonValueKind.String ? orangeEl.GetString() : null;

        var options = new BrandingOptions(logoPngBase64, footerAddress, colorBlue, colorOrange);
        var (success, error) = BrandingService.TrySave(options);
        SendToUi(new { type = "params_save_result", ok = success, error });
    }
    catch (Exception ex2)
    {
        AppLog.Error("Paramètres: enregistrement du branding impossible", ex2);
        SendToUi(new { type = "params_save_result", ok = false, error = "Erreur interne lors de l'enregistrement." });
    }
    return;
}

// ===== Fiches signalétiques (module manager) : import CSV + export PDF =====
if (string.Equals(type, "fiches_import_csv", StringComparison.OrdinalIgnoreCase))
{
    ImportFichesSignaletiquesCsvForUi();
    return;
}

if (string.Equals(type, "fiches_download_template", StringComparison.OrdinalIgnoreCase))
{
    DownloadFichesSignaletiquesTemplateForUi();
    return;
}

if (string.Equals(type, "fiches_geocode", StringComparison.OrdinalIgnoreCase))
{
    var targets = new List<(int Index, double Lat, double Lon)>();
    if (root.TryGetProperty("targets", out var targetsEl) && targetsEl.ValueKind == JsonValueKind.Array)
    {
        foreach (var t in targetsEl.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object) continue;
            if (!t.TryGetProperty("index", out var idxEl) || !idxEl.TryGetInt32(out var idx)) continue;
            if (!t.TryGetProperty("lat", out var latEl) || !latEl.TryGetDouble(out var lat)) continue;
            if (!t.TryGetProperty("lon", out var lonEl) || !lonEl.TryGetDouble(out var lon)) continue;
            targets.Add((idx, lat, lon));
        }
    }
    // Fire-and-forget : appels Nominatim espacés de 1.1s (politique d'usage), potentiellement
    // longs pour un lot de fiches - ne doit jamais bloquer le thread UI WinForms (voir
    // GeocodeFichesSignaletiquesAsync). La méthode capture son propre contexte d'erreurs.
    _ = GeocodeFichesSignaletiquesAsync(targets);
    return;
}

if (string.Equals(type, "pdfsharp_fiches_signaletiques", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        string mode = root.TryGetProperty("mode", out var modeEl) ? (JsonElToString(modeEl) ?? "batch") : "batch";
        string fileNameSuggested = root.TryGetProperty("fileName", out var fEl)
            ? (JsonElToString(fEl) ?? "NOVA_Fiches_Signaletiques.pdf")
            : "NOVA_Fiches_Signaletiques.pdf";

        var payloadJson = EnrichFichesSignaletiquesWithLonLat(e.WebMessageAsJson);
        TryWritePdfSharpDebugPayload("pdfsharp_fiches_signaletiques", payloadJson);
        payloadJson = InjectBranding(payloadJson);
        var proof = GetPdfFooterVersion();

        if (string.Equals(mode, "perFiche", StringComparison.OrdinalIgnoreCase))
        {
            ExportFichesSignaletiquesPerFiche(payloadJson, proof);
        }
        else
        {
            using var sfd = new SaveFileDialog
            {
                Title = "Enregistrer le PDF - Fiches signalétiques",
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = SanitizeFileName(fileNameSuggested),
                InitialDirectory = ExportsDir
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateFichesSignaletiquesFromJson(sfd.FileName, payloadJson, proof);
            try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

            SendToUi(new { type = "fiches_export_result", ok = true, fileName = sfd.FileName });
        }
    }
    catch (Exception ex2)
    {
        AppLog.Error("PdfSharp fiches signalétiques generation failed", ex2);
        MessageBox.Show(this, "Erreur génération PDF (Fiches signalétiques)." + Environment.NewLine + Environment.NewLine + ex2.Message, "Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Error);
        SendToUi(new { type = "fiches_export_result", ok = false, error = ex2.Message });
    }
    return;
}

// ===== Contrôle de doublons (module manager) : import, résolution, export =====
if (string.Equals(type, "dupctrl_import_inputs", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Importer les fichiers d'entrée (TXT)",
            Filter = "Fichiers TXT (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
            Multiselect = true,
            RestoreDirectory = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var allPoints = new List<DcPoint>();
        var fileSummaries = new List<object>();
        foreach (var path in ofd.FileNames)
        {
            var text = ReadAllTextWithLimit(path);
            var name = Path.GetFileName(path);
            var (points, rejects) = DuplicateControlService.ParseTxtPoints(text, name, "input");
            allPoints.AddRange(points);
            fileSummaries.Add(new { name, count = points.Count, rejects = rejects.Count });
        }

        SendToUi(new
        {
            type = "dupctrl_inputs_result",
            ok = true,
            files = fileSummaries,
            points = allPoints.Select(DcPointToJs)
        });
    }
    catch (Exception ex2)
    {
        AppLog.Error("Contrôle de doublons : import entrées échoué", ex2);
        SendToUi(new { type = "dupctrl_inputs_result", ok = false, error = ex2.Message });
    }
    return;
}

if (string.Equals(type, "dupctrl_import_control", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Importer le fichier de contrôle (TXT)",
            Filter = "Fichiers TXT (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
            RestoreDirectory = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var text = ReadAllTextWithLimit(ofd.FileName);
        var name = Path.GetFileName(ofd.FileName);
        var (points, rejects) = DuplicateControlService.ParseTxtPoints(text, name, "control");

        SendToUi(new
        {
            type = "dupctrl_control_result",
            ok = true,
            fileName = name,
            count = points.Count,
            rejects = rejects.Count,
            points = points.Select(DcPointToJs)
        });
    }
    catch (Exception ex2)
    {
        AppLog.Error("Contrôle de doublons : import contrôle échoué", ex2);
        SendToUi(new { type = "dupctrl_control_result", ok = false, error = ex2.Message });
    }
    return;
}

if (string.Equals(type, "dupctrl_export", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var inputPoints = ParseDcPointsFromJson(root, "inputPoints", "input");
        var controlPoints = ParseDcPointsFromJson(root, "controlPoints", "control");

        // Renommages : extrait une occurrence de son groupe de doublons sous un nouvel ID (cas
        // où l'écart entre occurrences est trop grand pour être la même mesure - moyenne ou
        // suppression perdraient sa donnée). Appliqué avant Analyze, comme côté JS, pour que le
        // point renommé n'entre plus en collision et ressorte tel quel (orphelin, ou nouveau
        // groupe si le nouvel ID entre à son tour en collision).
        var renames = new List<(string SourceFile, string OriginalId, string NewId)>();
        if (root.TryGetProperty("renames", out var renArr) && renArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in renArr.EnumerateArray())
            {
                var sf = r.TryGetProperty("sourceFile", out var sfEl) ? sfEl.GetString() : null;
                var oid = r.TryGetProperty("originalId", out var oidEl) ? oidEl.GetString() : null;
                var nid = r.TryGetProperty("newId", out var nidEl) ? nidEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(sf) && !string.IsNullOrWhiteSpace(oid) && !string.IsNullOrWhiteSpace(nid))
                    renames.Add((sf!, oid!, nid!));
            }
        }

        List<DcPoint> ApplyRenames(List<DcPoint> points) => points
            .Select(p =>
            {
                var match = renames.FirstOrDefault(r => r.SourceFile == p.SourceFile && r.OriginalId == p.Id);
                return match.NewId != null ? p with { Id = match.NewId } : p;
            })
            .ToList();

        inputPoints = ApplyRenames(inputPoints);
        controlPoints = ApplyRenames(controlPoints);

        var analysis = DuplicateControlService.Analyze(inputPoints, controlPoints);

        // Le C# reste la source de vérité pour l'export : on refait l'analyse ici plutôt que de
        // faire confiance à la copie côté JS, et on applique les résolutions envoyées par
        // référence de fichier source (pas d'index de tableau, qui pourrait ne pas correspondre
        // exactement à l'ordre recalculé ici).
        var resolutionsByNormId = new Dictionary<string, (string Action, HashSet<string> SelectedSourceFiles)>();
        if (root.TryGetProperty("resolutions", out var resArr) && resArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in resArr.EnumerateArray())
            {
                var id = r.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
                var action = r.TryGetProperty("action", out var actEl) ? (actEl.GetString() ?? "") : "";
                var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (r.TryGetProperty("selectedSourceFiles", out var selArr) && selArr.ValueKind == JsonValueKind.Array)
                    foreach (var s in selArr.EnumerateArray())
                        if (s.GetString() is string sf) selected.Add(sf);
                if (!string.IsNullOrWhiteSpace(id))
                    resolutionsByNormId[id.Trim().ToUpperInvariant()] = (action, selected);
            }
        }

        var resolutions = new List<(string Id, string Action, string Detail)>();
        var finalPoints = new List<DcPoint>();
        finalPoints.AddRange(analysis.OrphanInputs);
        finalPoints.AddRange(analysis.OrphanControls);

        foreach (var group in analysis.Duplicates)
        {
            var key = group.Id.Trim().ToUpperInvariant();
            if (!resolutionsByNormId.TryGetValue(key, out var res))
            {
                SendToUi(new { type = "dupctrl_export_result", ok = false, error = $"Groupe de doublons non résolu : {group.Id}." });
                return;
            }

            if (string.Equals(res.Action, "delete", StringComparison.OrdinalIgnoreCase))
            {
                resolutions.Add((group.Id, "Supprimé", $"{group.Occurrences.Count} occurrence(s) exclue(s) du fichier final"));
                continue;
            }

            var selectedOccurrences = group.Occurrences.Where(o => res.SelectedSourceFiles.Contains(o.SourceFile)).ToList();
            if (selectedOccurrences.Count == 0)
            {
                SendToUi(new { type = "dupctrl_export_result", ok = false, error = $"Aucune occurrence sélectionnée pour {group.Id}." });
                return;
            }

            var (avgX, avgY, avgZ) = DuplicateControlService.Average(selectedOccurrences);
            finalPoints.Add(new DcPoint(group.Id, avgX, avgY, avgZ ?? 0, avgZ.HasValue, selectedOccurrences[0].Code, "fusionné", "output"));
            resolutions.Add((group.Id, selectedOccurrences.Count == 1 ? "Conservé" : "Moyenne",
                selectedOccurrences.Count == 1
                    ? $"occurrence de {selectedOccurrences[0].SourceFile} conservée telle quelle"
                    : $"moyenne de {selectedOccurrences.Count} occurrences ({string.Join(", ", selectedOccurrences.Select(o => o.SourceFile))})"));
        }

        using var sfd = new SaveFileDialog
        {
            Title = "Enregistrer le fichier de points final",
            Filter = "Fichiers TXT (*.txt)|*.txt",
            FileName = "points_final.txt",
            InitialDirectory = ExportsDir
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var outputText = DuplicateControlService.FormatOutputTxt(
            finalPoints.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList());
        File.WriteAllText(sfd.FileName, outputText, new UTF8Encoding(false));

        var inputFileCount = inputPoints.Select(p => p.SourceFile).Distinct().Count();
        var report = DuplicateControlService.FormatReport(analysis, resolutions, inputPoints.Count, controlPoints.Count, inputFileCount);
        if (renames.Count > 0)
        {
            var renameLines = renames.Select(r => $"  {r.OriginalId} -> {r.NewId} (source : {r.SourceFile})");
            report += Environment.NewLine + "--- Occurrences renommées (séparées plutôt que fondues/supprimées) ---" +
                      Environment.NewLine + string.Join(Environment.NewLine, renameLines) + Environment.NewLine;
        }
        var reportPath = Path.Combine(
            Path.GetDirectoryName(sfd.FileName) ?? ExportsDir,
            Path.GetFileNameWithoutExtension(sfd.FileName) + "_rapport.txt");
        File.WriteAllText(reportPath, report, new UTF8Encoding(false));

        try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

        SendToUi(new { type = "dupctrl_export_result", ok = true, filePath = sfd.FileName, reportPath });
    }
    catch (Exception ex2)
    {
        AppLog.Error("Contrôle de doublons : export échoué", ex2);
        SendToUi(new { type = "dupctrl_export_result", ok = false, error = ex2.Message });
    }
    return;
}

if (string.Equals(type, "cp_generate_map", StringComparison.OrdinalIgnoreCase))
{
    // Points Levé des groupes inclus dans l'analyse (envoyés par la JS après "Lancer l'analyse") -
    // pas de géocodage, juste la reprojection de points déjà connus.
    var cpMapPoints = new List<CpPoint>();
    if (root.TryGetProperty("points", out var cpMapPtsArr) && cpMapPtsArr.ValueKind == JsonValueKind.Array)
    {
        foreach (var el in cpMapPtsArr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var id = el.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
            double x = el.TryGetProperty("x", out var xEl) && xEl.TryGetDouble(out var xv) ? xv : 0;
            double y = el.TryGetProperty("y", out var yEl) && yEl.TryGetDouble(out var yv) ? yv : 0;
            double z = el.TryGetProperty("z", out var zEl) && zEl.TryGetDouble(out var zv) ? zv : 0;
            cpMapPoints.Add(new CpPoint(id, x, y, z));
        }
    }
    // Fire-and-forget : téléchargement des tuiles OSM, potentiellement long - ne doit jamais
    // bloquer le thread UI WinForms.
    _ = GenerateCpMapFromPointsAsync(cpMapPoints);
    return;
}

if (string.Equals(type, "cp_import_leve", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Importer le fichier Levé (TXT)",
            Filter = "Fichiers TXT (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
            RestoreDirectory = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var text = ReadAllTextWithLimit(ofd.FileName);
        var name = Path.GetFileName(ofd.FileName);
        var (points, warnings) = ControlePrecisionService.ParseTxtPoints(text);

        SendToUi(new
        {
            type = "cp_leve_result",
            ok = true,
            fileName = name,
            count = points.Count,
            warnings = warnings.Count,
            points = points.Values.Select(CpPointToJs)
        });
    }
    catch (Exception ex2)
    {
        AppLog.Error("Contrôle classe de précision : import Levé échoué", ex2);
        SendToUi(new { type = "cp_leve_result", ok = false, error = ex2.Message });
    }
    return;
}

if (string.Equals(type, "cp_import_controle", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Importer le fichier Contrôle (TXT)",
            Filter = "Fichiers TXT (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
            RestoreDirectory = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var text = ReadAllTextWithLimit(ofd.FileName);
        var name = Path.GetFileName(ofd.FileName);
        var (points, warnings) = ControlePrecisionService.ParseTxtPoints(text);

        SendToUi(new
        {
            type = "cp_controle_result",
            ok = true,
            fileName = name,
            count = points.Count,
            warnings = warnings.Count,
            points = points.Values.Select(CpPointToJs)
        });
    }
    catch (Exception ex2)
    {
        AppLog.Error("Contrôle classe de précision : import Contrôle échoué", ex2);
        SendToUi(new { type = "cp_controle_result", ok = false, error = ex2.Message });
    }
    return;
}

if (string.Equals(type, "cp_export", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        var levePoints = ParseCpPointsFromJson(root, "levePoints");
        var controlePoints = ParseCpPointsFromJson(root, "controlePoints");

        var includedIds = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("includedIds", out var idsArr) && idsArr.ValueKind == JsonValueKind.Array)
            foreach (var idEl in idsArr.EnumerateArray())
                if (idEl.GetString() is string idStr) includedIds.Add(idStr);

        var allMatches = ControlePrecisionService.MatchPoints(levePoints, controlePoints);
        var included = allMatches.Where(m => includedIds.Contains(m.Id)).ToList();

        if (included.Count == 0)
        {
            SendToUi(new { type = "cp_export_result", ok = false, error = "Aucun point inclus dans l'analyse." });
            return;
        }

        var metaEl = root.TryGetProperty("meta", out var mEl) && mEl.ValueKind == JsonValueKind.Object ? mEl : default;
        var paramsEl = root.TryGetProperty("params", out var pEl) && pEl.ValueKind == JsonValueKind.Object ? pEl : default;

        double tolXYcm = GetJsonNum(paramsEl, "tolXYcm") ?? 4;
        double tolZcm = GetJsonNum(paramsEl, "tolZcm") ?? 4;
        double securityCoefficient = GetJsonNum(paramsEl, "securityCoefficient") ?? 2;
        double confidenceZ = GetJsonNum(paramsEl, "confidenceZ") ?? 1.96;
        string controlType = GetJsonStr(paramsEl, "controlType") ?? "2D+1D";

        var analysis = ControlePrecisionService.Analyze(included, tolXYcm / 100.0, tolZcm / 100.0, securityCoefficient, confidenceZ, controlType);

        // Histogrammes (comptage par plage mm, sur les points inclus).
        var fAltiHist = included.Select(m => m.AltiDev).ToList();
        var fPlaniHist = included.Select(m => m.PlaniDev).ToList();
        var f3DHist = included.Select(m => m.ThreeDDev).ToList();
        var counts1D = ControlePrecisionService.CalculateHistogram(fAltiHist);
        var counts2D = ControlePrecisionService.CalculateHistogram(fPlaniHist);
        var counts3D = ControlePrecisionService.CalculateHistogram(f3DHist);
        int nIncluded = included.Count;
        double[] Pct(int[] counts) => counts.Select(c => nIncluded > 0 ? (c / (double)nIncluded) * 100.0 : 0.0).ToArray();
        int outside1D = fAltiHist.Count(v => v * 1000 > 300);
        int outside2D = fPlaniHist.Count(v => v * 1000 > 300);
        int outside3D = f3DHist.Count(v => v * 1000 > 300);
        double tol3Dcm = Math.Sqrt(tolXYcm * tolXYcm + tolZcm * tolZcm);
        double tauxControlePct = levePoints.Count > 0 ? included.Count / (double)levePoints.Count * 100.0 : 0.0;

        // Paramètres de la courbe de Gauss théorique (en cm) par dimension - mêmes valeurs que
        // les cartes de conditions, juste reprojetées pour le tracé (moyenne/écart-type/IC/seuils).
        object GaussianJs(CpStats stats, CpConditionSet? cond, bool applicable) => new
        {
            applicable = applicable && stats.N > 0,
            n = stats.N,
            meanCm = stats.Mean * 100,
            stdDevCm = stats.StdDev * 100,
            ciLowerCm = stats.CiLower * 100,
            ciUpperCm = stats.CiUpper * 100,
            s1Cm = (cond?.Cond1.Seuil ?? 0) * 100,
            t1Cm = (cond?.Cond2.SeuilT1 ?? 0) * 100,
            t2Cm = (cond?.Cond3.SeuilT2 ?? 0) * 100
        };

        // Tableau "Analyse Mathématique des Écarts" (section dédiée du rapport, en mètres,
        // toutes dimensions - reprend les mêmes CpStats déjà calculées par Analyze()).
        object StatsJs(CpStats s) => new
        {
            n = s.N, min = s.Min, max = s.Max, mean = s.Mean, median = s.Median,
            stdDev = s.StdDev, meanAbs = s.MeanAbs, ciLower = s.CiLower, ciUpper = s.CiUpper,
            p33 = s.P33, p67 = s.P67
        };

        var payload = new
        {
            meta = new
            {
                commanditaire = GetJsonStr(metaEl, "commanditaire") ?? "",
                redacteur = GetJsonStr(metaEl, "redacteur") ?? "",
                dateRedaction = GetJsonStr(metaEl, "dateRedaction") ?? "",
                prestataireLeve = GetJsonStr(metaEl, "prestataireLeve") ?? "",
                prestataireControle = GetJsonStr(metaEl, "prestataireControle") ?? "",
                commune = GetJsonStr(metaEl, "commune") ?? "",
                adresse = GetJsonStr(metaEl, "adresse") ?? "",
                typePlan = GetJsonStr(metaEl, "typePlan") ?? "",
                reference = GetJsonStr(metaEl, "reference") ?? "",
            },
            @params = new { tolXYcm, tolZcm, controlType, securityCoefficient, confidenceZ },
            counts = new
            {
                levePoints = levePoints.Count,
                controlePoints = controlePoints.Count,
                matchedPoints = allMatches.Count,
                includedPoints = included.Count,
                tolerance3Dcm = tol3Dcm,
                tauxControlePct
            },
            points = included.Select(m => new
            {
                id = m.Id,
                leveX = m.Leve.X, leveY = m.Leve.Y, leveZ = m.Leve.Z,
                controleX = m.Controle.X, controleY = m.Controle.Y, controleZ = m.Controle.Z,
                dX = m.DX, dY = m.DY, dZ = m.DZ,
                planiDev = m.PlaniDev, altiDev = m.AltiDev, threeDDev = m.ThreeDDev
            }),
            conditions = new
            {
                alti = CpConditionToJs(analysis.AltiConditions, controlType.Contains("1D")),
                plani = CpConditionToJs(analysis.PlaniConditions, controlType.Contains("2D")),
                // Le contrôle 3D isotrope est un contrôle croisé toujours informatif, montré par
                // l'outil de référence quel que soit le "Type de contrôle" principal choisi (ex.
                // "2D+1D (Combiné)" y affiche quand même le bloc 3D) - contrairement à alti/plani,
                // ne pas le conditionner à la présence du texte "3D" dans controlType (qui ne le
                // contient jamais pour "2D+1D", faisant disparaître le bloc à tort).
                threeD = CpConditionToJs(analysis.ThreeDConditions, true),
                overallConformity = analysis.OverallConformity
            },
            barycentres = new
            {
                leve = analysis.BarycentreLeve.HasValue
                    ? new { x = analysis.BarycentreLeve.Value.X, y = analysis.BarycentreLeve.Value.Y, z = analysis.BarycentreLeve.Value.Z }
                    : null,
                controle = analysis.BarycentreControle.HasValue
                    ? new { x = analysis.BarycentreControle.Value.X, y = analysis.BarycentreControle.Value.Y, z = analysis.BarycentreControle.Value.Z }
                    : null
            },
            histogram = new
            {
                ranges = ControlePrecisionService.HistogramRanges.Select(r => r.Label),
                counts1D, counts2D, counts3D,
                percent1D = Pct(counts1D), percent2D = Pct(counts2D), percent3D = Pct(counts3D),
                outside1D, outside2D, outside3D,
                outsidePercent1D = nIncluded > 0 ? outside1D / (double)nIncluded * 100.0 : 0.0,
                outsidePercent2D = nIncluded > 0 ? outside2D / (double)nIncluded * 100.0 : 0.0,
                outsidePercent3D = nIncluded > 0 ? outside3D / (double)nIncluded * 100.0 : 0.0
            },
            // Les 3 courbes de Gauss sont toujours affichées (comme la référence, qui les
            // dessine sans condition sur le type de contrôle sélectionné) - contrairement aux
            // cartes "Vérification des Conditions" qui elles restent filtrées par controlType.
            gaussian = new
            {
                alti = GaussianJs(analysis.AltiStats, analysis.AltiConditions, true),
                plani = GaussianJs(analysis.PlaniStats, analysis.PlaniConditions, true),
                threeD = GaussianJs(analysis.ThreeDStats, analysis.ThreeDConditions, true)
            },
            mathStats = new
            {
                dX = StatsJs(analysis.DxStats), dY = StatsJs(analysis.DyStats), dZ = StatsJs(analysis.DzStats),
                plani = StatsJs(analysis.PlaniStats), alti = StatsJs(analysis.AltiStats), threeD = StatsJs(analysis.ThreeDStats)
            },
            // Nuage de points global : tous les points Levé (pas seulement ceux inclus dans
            // l'analyse), avec leur statut apparié/non apparié - comme le scatterplot de référence.
            scatter = levePoints.Values.Select(p => new { x = p.X, y = p.Y, matched = controlePoints.ContainsKey(p.Id) }),
            // Carte de situation (data URL PNG déjà générée via cp_generate_map, ou absente si
            // l'utilisateur n'a pas cliqué sur "Générer la carte" avant l'export - le renderer PDF
            // omet simplement la carte dans ce cas).
            mapImageDataUrl = GetJsonStr(root, "mapImageDataUrl"),
            // Quartiles/min/max Plani et Alti pour le boxplot expert (le reste des 6 graphiques
            // experts se recalcule côté renderer directement depuis "points", qui porte déjà
            // dX/dY/dZ/planiDev/altiDev/threeDDev par point inclus).
            statsBox = new
            {
                plani = new { q1 = analysis.PlaniStats.Q1, median = analysis.PlaniStats.Median, q3 = analysis.PlaniStats.Q3, min = analysis.PlaniStats.Min, max = analysis.PlaniStats.Max },
                alti = new { q1 = analysis.AltiStats.Q1, median = analysis.AltiStats.Median, q3 = analysis.AltiStats.Q3, min = analysis.AltiStats.Min, max = analysis.AltiStats.Max }
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        TryWritePdfSharpDebugPayload("cp_export", payloadJson);
        // Bug réel constaté (audit du 2026-09-03) : contrairement à tous les autres exports PDF de
        // ce fichier, ce handler passait payloadJson directement au renderer sans jamais appeler
        // InjectBranding - le rapport Contrôle classe de précision retombait donc silencieusement
        // sur l'identité NOVATLAS par défaut même quand l'utilisateur avait configuré un logo/des
        // couleurs personnalisés dans Paramètres.
        payloadJson = InjectBranding(payloadJson);

        // Nom de fichier par défaut identique à la référence : NOVATLAS_RAPPORT_ARRETE_<référence>.pdf
        // (ex: NOVATLAS_RAPPORT_ARRETE_86150) - repli sur l'ancien nom horodaté si le champ
        // "Référence / Dossier" est vide ou ne contient aucun caractère valide pour un nom de fichier.
        string referenceRaw = GetJsonStr(metaEl, "reference") ?? "";
        string referenceSafe = new string(referenceRaw.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        string defaultFileName = string.IsNullOrWhiteSpace(referenceSafe)
            ? $"controle_precision_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
            : $"NOVATLAS_RAPPORT_ARRETE_{referenceSafe}.pdf";

        using var sfd = new SaveFileDialog
        {
            Title = "Enregistrer le rapport de contrôle (PDF)",
            Filter = "Fichiers PDF (*.pdf)|*.pdf",
            FileName = defaultFileName,
            InitialDirectory = ExportsDir
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var proof = GetPdfFooterVersion();
        NovaFiches.PdfSharpEngine.PdfSharpReports.GenerateControlePrecisionFromJson(sfd.FileName, payloadJson, proof);
        try { using var process = Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true }); } catch { }

        SendToUi(new { type = "cp_export_result", ok = true, filePath = sfd.FileName });
    }
    catch (Exception ex2)
    {
        AppLog.Error("Contrôle classe de précision : export échoué", ex2);
        SendToUi(new { type = "cp_export_result", ok = false, error = ex2.Message });
    }
    return;
}

if (!string.Equals(type, "saveAs", StringComparison.OrdinalIgnoreCase))
                    return;

                string fileNameRaw = root.TryGetProperty("fileName", out var fnEl) ? (fnEl.GetString() ?? "") : "";
                string base64 = root.TryGetProperty("base64", out var b64El) ? (b64El.GetString() ?? "") : "";
                string mimeType = root.TryGetProperty("mimeType", out var mtEl) ? (mtEl.GetString() ?? "") : "";

                if (string.IsNullOrWhiteSpace(base64))
                {
                    MessageBox.Show(this,
                        "Export impossible : données vides (base64 manquant).",
                        "Export",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                byte[] bytes;
                try { bytes = Convert.FromBase64String(base64); }
                catch
                {
                    MessageBox.Show(this,
                        "Export impossible : données invalides (base64 illisible).",
                        "Export",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                string fileName = NormalizeFileName(fileNameRaw, mimeType);

                BeginInvoke(new Action(() =>
                {
                    using var sfd = new SaveFileDialog
                    {
                        Title = "Enregistrer sous...",
                        FileName = fileName,
                        Filter = BuildFilterFromFileName(fileName),
                        DefaultExt = Path.GetExtension(fileName),
                        OverwritePrompt = true,
                        AddExtension = true,
                        InitialDirectory = ExportsDir
                    };

                    if (sfd.ShowDialog(this) != DialogResult.OK)
                        return;

                    File.WriteAllBytes(sfd.FileName, bytes);
                    AppLog.Info($"Export saveAs OK: {sfd.FileName} ({mimeType})");

                    AfterSuccessfulExport(sfd.FileName);
                }));
            }
            catch (Exception ex)
            {
                AppLog.Error("WebMessageReceived error", ex);
                BeginInvoke(new Action(() =>
                    MessageBox.Show(this, "Erreur export (WebMessage). Voir logs.", "Export",
                        MessageBoxButtons.OK, MessageBoxIcon.Error)
                ));
            }
        };
    }

    // UX post-export
private void AfterSuccessfulExport(string filePath)
{
    try
    {
        // Open the folder where the file was actually saved (prevents "empty folder" confusion
        // when the user chose another folder in Save As).
        var savedDir = Path.GetDirectoryName(filePath) ?? ExportsDir;

        if (_settings.OpenExportsAfterSave)
        {
            OpenFolder(savedDir);
            return;
        }

        var r = MessageBox.Show(this,
            $@"Export réussi{Environment.NewLine}{Environment.NewLine}{Path.GetFileName(filePath)}{Environment.NewLine}{filePath}{Environment.NewLine}{Environment.NewLine}Ouvrir le dossier de destination ?",
            "Nova-Fiches",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (r == DialogResult.Yes)
            OpenFolder(savedDir);
    }
    catch { }
}

private void OpenFolder(string dir)
{

        try
        {
            Directory.CreateDirectory(dir);
            using var process = Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenFolder failed", ex);
        }
    }


    // About + Diagnostic

    private void OpenHelpNoticePdf()
    {
        try
        {
            var helpPdf = Path.Combine(AppContext.BaseDirectory, "assets", "aide", "Nova-Fiches_Notice_Pro_v2.2.pdf");
            if (!File.Exists(helpPdf))
            {
                MessageBox.Show(
                    "Le fichier de notice est introuvable.\n\nVérifiez que le dossier 'assets/aide' contient :\nNova-Fiches_Notice_Pro_v2.2.pdf",
                    "Aide indisponible",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = helpPdf,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenHelpNoticePdf failed", ex);
            MessageBox.Show(
                $"Erreur lors de l'ouverture de l'aide : {ex.Message}",
                "Erreur",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void OpenUpdateHistory()
    {
        try
        {
            var historyPath = Path.Combine(AppContext.BaseDirectory, "assets", "aide", "HISTORIQUE_MISES_A_JOUR.md");
            if (!File.Exists(historyPath))
            {
                MessageBox.Show(
                    "Le fichier d'historique est introuvable.\n\nVérifiez que le dossier 'assets/aide' contient :\nHISTORIQUE_MISES_A_JOUR.md",
                    "Historique indisponible",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = historyPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("OpenUpdateHistory failed", ex);
            MessageBox.Show(
                $"Erreur lors de l'ouverture de l'historique : {ex.Message}",
                "Erreur",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void ShowAbout()
    {
        try
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "(inconnue)";
            var msg =
                $"Nova-Fiches\n\nVersion : {ver}\n\nExports :\n{ExportsDir}\n";

            MessageBox.Show(this, msg, "À propos — Nova-Fiches", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("ShowAbout failed", ex);
        }
    }

    private void CopyDiagnostic()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version?.ToString() ?? "(inconnue)";

            string webviewVersion = "(non initialisé)";
            try
            {
                if (_webView?.CoreWebView2 != null)
                    webviewVersion = _webView.CoreWebView2.Environment.BrowserVersionString ?? "(?)";
            }
            catch { }

            var txt =
                "=== Nova-Fiches Diagnostic ===\r\n" +
                $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"AppVersion: {ver}\r\n" +
                $"BaseDirectory: {AppContext.BaseDirectory}\r\n" +
                $"ExportsDir: {ExportsDir}\r\n" +
                $"WebViewUserDataDir: {WebViewUserDataDir}\r\n" +
                $"SettingsPath: {SettingsPath}\r\n" +
                $"OpenExportsAfterSave: {_settings.OpenExportsAfterSave}\r\n" +
                $"WebView2BrowserVersion: {webviewVersion}\r\n" +
                $"OS: {Environment.OSVersion}\r\n" +
                $"Machine: {Environment.MachineName}\r\n" +
                $"User: {Environment.UserName}\r\n";

            Clipboard.SetText(txt);

            MessageBox.Show(this,
                "Diagnostic copié dans le presse-papiers ✅",
                "Nova-Fiches",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLog.Error("CopyDiagnostic failed", ex);
            MessageBox.Show(this, "Impossible de copier le diagnostic. Voir logs.", "Nova-Fiches",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // Settings
    private void LoadSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);

            if (!File.Exists(SettingsPath))
            {
                _settings = new AppSettings();
                return;
            }

            var json = File.ReadAllText(SettingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    private void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // non bloquant
        }
    }

    private class AppSettings
    {
        public bool OpenExportsAfterSave { get; set; } = false;

        // ETAPE 5: window sizing adaptability
        public bool RememberWindowPlacement { get; set; } = true;
        public AppWindowPlacement? Window { get; set; }
    }

    private class AppWindowPlacement
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }

        public bool IsValid()
        {
            // Basic sanity checks
            return Width >= 400 && Height >= 300;
        }

        public System.Drawing.Rectangle ToRectangle()
            => new System.Drawing.Rectangle(X, Y, Width, Height);
    }

    // Helpers
    private static string BuildFilterFromFileName(string fileName)
    {
        string ext = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        return ext switch
        {
            ".pdf" => "PDF (*.pdf)|*.pdf|Tous les fichiers (*.*)|*.*",
            ".csv" => "CSV (*.csv)|*.csv|Tous les fichiers (*.*)|*.*",
            ".txt" => "Texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
            _ => "Tous les fichiers (*.*)|*.*",
        };
    }

    private static string NormalizeFileName(string fileName, string mimeType)
    {
        fileName = (fileName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "export";

        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrWhiteSpace(ext))
            return fileName;

        string forcedExt = (mimeType ?? "").ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "text/csv" => ".csv",
            "text/plain" => ".txt",
            _ => ".bin"
        };

        return fileName + forcedExt;
    }

    
private static string SanitizeFileName(string fileName)
{
    foreach (var c in Path.GetInvalidFileNameChars())
        fileName = fileName.Replace(c, '_');
    fileName = fileName.Trim();
    return string.IsNullOrWhiteSpace(fileName) ? "export" : fileName;
}

	private static object DcPointToJs(DcPoint p) => new
	{
	    id = p.Id, x = p.X, y = p.Y, z = p.Z, hasZ = p.HasZ, code = p.Code, sourceFile = p.SourceFile, role = p.Role
	};

	private static List<DcPoint> ParseDcPointsFromJson(JsonElement root, string propertyName, string role)
	{
	    var list = new List<DcPoint>();
	    if (!root.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
	    foreach (var el in arr.EnumerateArray())
	    {
	        if (el.ValueKind != JsonValueKind.Object) continue;
	        var id = el.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
	        if (string.IsNullOrWhiteSpace(id)) continue;
	        double x = el.TryGetProperty("x", out var xEl) && xEl.TryGetDouble(out var xv) ? xv : 0;
	        double y = el.TryGetProperty("y", out var yEl) && yEl.TryGetDouble(out var yv) ? yv : 0;
	        double z = el.TryGetProperty("z", out var zEl) && zEl.TryGetDouble(out var zv) ? zv : 0;
	        bool hasZ = el.TryGetProperty("hasZ", out var hzEl) && hzEl.ValueKind == JsonValueKind.True;
	        string? code = el.TryGetProperty("code", out var cEl) ? cEl.GetString() : null;
	        string sourceFile = el.TryGetProperty("sourceFile", out var sfEl) ? (sfEl.GetString() ?? "") : "";
	        list.Add(new DcPoint(id, x, y, z, hasZ, code, sourceFile, role));
	    }
	    return list;
	}

	/// <summary>
	/// Helper de compatibilité : conversion sûre d'un JsonElement vers string.
	/// (Certains messages WebView2 envoient des valeurs non-string, on uniformise ici.)
	/// </summary>
	private static object CpPointToJs(CpPoint p) => new { id = p.Id, x = p.X, y = p.Y, z = p.Z };

	// Reconstruit le dictionnaire de points (id -> CpPoint) depuis le tableau brut renvoyé par la
	// JS (echo de ce que cp_import_leve/cp_import_controle avaient eux-mêmes envoyé) - un même ID
	// répété écrase silencieusement l'entrée précédente, comme ControlePrecisionService.ParseTxtPoints.
	private static Dictionary<string, CpPoint> ParseCpPointsFromJson(JsonElement root, string propertyName)
	{
	    var points = new Dictionary<string, CpPoint>();
	    if (!root.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array) return points;
	    foreach (var el in arr.EnumerateArray())
	    {
		if (el.ValueKind != JsonValueKind.Object) continue;
		var id = el.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
		if (string.IsNullOrWhiteSpace(id)) continue;
		double x = el.TryGetProperty("x", out var xEl) && xEl.TryGetDouble(out var xv) ? xv : 0;
		double y = el.TryGetProperty("y", out var yEl) && yEl.TryGetDouble(out var yv) ? yv : 0;
		double z = el.TryGetProperty("z", out var zEl) && zEl.TryGetDouble(out var zv) ? zv : 0;
		points[id] = new CpPoint(id, x, y, z);
	    }
	    return points;
	}

	// Projection camelCase d'un CpConditionSet (ou son absence) pour le renderer PDF ; "applicable"
	// indique si cette dimension (1D/2D/3D) fait partie du type de contrôle sélectionné - le
	// renderer n'affiche que les conditions marquées applicable=true.
	private static object CpConditionToJs(CpConditionSet? set, bool applicable) => new
	{
	    applicable = applicable && set != null,
	    meanDev = set?.Cond1.MeanDev ?? 0,
	    seuilS1 = set?.Cond1.Seuil ?? 0,
	    cond1Ok = set?.Cond1.Ok ?? false,
	    countOverT1 = set?.Cond2.CountOverT1 ?? 0,
	    nPrimeMax = set?.Cond2.NPrimeMax ?? 0,
	    seuilT1 = set?.Cond2.SeuilT1 ?? 0,
	    cond2Ok = set?.Cond2.Ok ?? false,
	    countOverT2 = set?.Cond3.CountOverT2 ?? 0,
	    seuilT2 = set?.Cond3.SeuilT2 ?? 0,
	    cond3Ok = set?.Cond3.Ok ?? false,
	    overallOk = set?.OverallOk ?? false
	};

	private static string? GetJsonStr(JsonElement el, string key) =>
	    el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

	private static double? GetJsonNum(JsonElement el, string key) =>
	    el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : (double?)null;

	private static string JsonElToString(JsonElement el)
	{
	    // Some payloads (notably Rapport complet -> Mesure sur ligne) can contain
	    // rich cells like: {"content":"1","__kind":"LINE","styles":{...}}
	    // We want the displayed text to be the content, not the raw JSON.
	    if (el.ValueKind == JsonValueKind.Object)
	    {
	        try
	        {
	            if (el.TryGetProperty("content", out var contentEl))
	                return JsonElToString(contentEl);
	        }
	        catch { /* ignore */ }
	    }

	    return el.ValueKind switch
	    {
	        JsonValueKind.String => el.GetString() ?? "",
	        JsonValueKind.Number => el.ToString(),
	        JsonValueKind.True => "true",
	        JsonValueKind.False => "false",
	        JsonValueKind.Null => "",
	        JsonValueKind.Undefined => "",
	        _ => el.GetRawText()
	    };
	}

private static string? GetSuggestedFileName(CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            string? uri = null;
            try { uri = e.DownloadOperation?.Uri; } catch { }

            if (!string.IsNullOrWhiteSpace(uri))
            {
                try
                {
                    var u = new Uri(uri);
                    var name = Path.GetFileName(u.LocalPath);
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
                catch { }
            }

            return "export";
        }
        catch
        {
            return "export";
        }
    }

    private static void TryDeleteQuiet(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool TrySetResultFilePathIfPossible(CoreWebView2DownloadStartingEventArgs e, string path)
    {
        var prop = e.GetType().GetProperty("ResultFilePath", BindingFlags.Instance | BindingFlags.Public);
        if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
        {
            prop.SetValue(e, path);
            return true;
        }

        try
        {
            var op = e.DownloadOperation;
            if (op != null)
            {
                var opProp = op.GetType().GetProperty("ResultFilePath", BindingFlags.Instance | BindingFlags.Public);
                if (opProp != null && opProp.CanWrite && opProp.PropertyType == typeof(string))
                {
                    opProp.SetValue(op, path);
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static void TrySetHandledIfPossible(CoreWebView2DownloadStartingEventArgs e, bool handled)
    {
        var prop = e.GetType().GetProperty("Handled", BindingFlags.Instance | BindingFlags.Public);
        if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
        {
            prop.SetValue(e, handled);
        }
    }
private string BuildAssetsFingerprint()
{
    // Avoid heavy hashing: last-write timestamps are enough to bust cache reliably.
    // Includes HTML + main JS + logo file.
    var parts = new List<string>();
    void Add(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                parts.Add($"{Path.GetFileName(path)}:{fi.Length}:{fi.LastWriteTimeUtc.Ticks}");
            }
            else
            {
                parts.Add($"{Path.GetFileName(path)}:missing");
            }
        }
        catch
        {
            parts.Add($"{Path.GetFileName(path)}:err");
        }
    }

    var assetsRoot = Path.Combine(AppContext.BaseDirectory, "assets");
    if (Directory.Exists(assetsRoot))
    {
        foreach (var file in Directory.EnumerateFiles(assetsRoot, "*.*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            Add(file);
        }
    }
    else
    {
        Add(AssetsHtmlPath);
    }

    // Also include app version if available
    var ver = Application.ProductVersion ?? "0";
    parts.Add($"ver:{ver}");

    using var sha = System.Security.Cryptography.SHA256.Create();
    var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("|", parts));
    return Convert.ToHexString(sha.ComputeHash(bytes));
}


    private sealed class AutoCadPointExportRow
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    private sealed class AutoCadExportPayload
    {
        public List<AutoCadPointExportRow> Implantation { get; set; } = new();
        public List<AutoCadPointExportRow> Ligne { get; set; } = new();
        public List<AutoCadPointExportRow> Leve { get; set; } = new();
    }

    private async Task<string?> GetAutoCadExportPayloadJsonAsync()
    {
        if (_webView?.CoreWebView2 == null) return null;

        var script = @"JSON.stringify((() => {
            const d = window.lastData || window.__NF_LASTDATA || {};
            const pickId = (p) => String(
                p?.id ?? p?.ID ?? p?.Id ?? p?.name ?? p?.Name ?? p?.pointName ?? p?.pntRef ?? ''
            ).trim();

            const num = (v) => {
                const n = Number(v);
                return Number.isFinite(n) ? n : 0;
            };

            const row = (p) => ({
                Id: pickId(p),
                X: num(p?.mes?.E ?? p?.calc?.E ?? p?.E ?? p?.e ?? p?.x),
                Y: num(p?.mes?.N ?? p?.calc?.N ?? p?.N ?? p?.n ?? p?.y),
                Z: num(p?.mes?.H ?? p?.calc?.H ?? p?.H ?? p?.h ?? p?.z)
            });

            const imp = Array.isArray(d?.implantation?.points)
                ? d.implantation.points.map(row).filter(p => p.Id)
                : [];

            const ligne = [];
            const lines = Array.isArray(d?.refLine) ? d.refLine : (Array.isArray(d?.ligneRef) ? d.ligneRef : []);
            for (const ln of lines) {
                const rab = Array.isArray(ln?.rabPoints) ? ln.rabPoints : [];
                for (const p of rab) {
                    const r = row(p);
                    if (r.Id) ligne.push(r);
                }
            }

            const leve = [];
            const topoStations = Array.isArray(d?.topoStations) ? d.topoStations : [];
            for (const st of topoStations) {
                const rows = Array.isArray(st?.results) ? st.results : [];
                for (const p of rows) {
                    const r = row(p);
                    if (r.Id) leve.push(r);
                }
            }

            return { Implantation: imp, Ligne: ligne, Leve: leve };
        })())";
        var raw = await _webView.CoreWebView2.ExecuteScriptAsync(script);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return JsonSerializer.Deserialize<string>(raw);
    }

    private async Task ExportAutoCadAsync()
    {
        try
        {
            var stateJson = await GetProjectStateJsonAsync();
            if (string.IsNullOrWhiteSpace(stateJson))
            {
                MessageBox.Show(this, "Impossible de récupérer l'état du projet.", "Nova-Fiches",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var payloadJson = await GetAutoCadExportPayloadJsonAsync() ?? "{}";

            using var dialog = new FolderBrowserDialog
            {
                Description = "Choisir le dossier d'export AutoCAD"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            TopoRapportWin.AutoCadExportService.ExportFromNovaState(stateJson, payloadJson, dialog.SelectedPath);
            MessageBox.Show(this, "Export AutoCAD terminé.", "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export AutoCAD impossible : " + ex.Message, "Nova-Fiches",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


private async Task MergePdfClientAsync()
{
    try
    {
        using var fichesDialog = new OpenFileDialog
        {
            Title = "Choisir le PDF des fiches",
            Filter = "Fichiers PDF (*.pdf)|*.pdf"
        };
        if (fichesDialog.ShowDialog(this) != DialogResult.OK) return;

        using var planDialog = new OpenFileDialog
        {
            Title = "Choisir le PDF du plan",
            Filter = "Fichiers PDF (*.pdf)|*.pdf"
        };
        if (planDialog.ShowDialog(this) != DialogResult.OK) return;

        using var saveDialog = new SaveFileDialog
        {
            Title = "Enregistrer le PDF fusionné",
            Filter = "Fichiers PDF (*.pdf)|*.pdf",
            FileName = "NOVA_DOSSIER_CLIENT.pdf"
        };
        if (saveDialog.ShowDialog(this) != DialogResult.OK) return;

        TopoRapportWin.AutoCadExportService.MergePlanAndFiches(
            planDialog.FileName,
            fichesDialog.FileName,
            saveDialog.FileName);

        MessageBox.Show(this, "Fusion PDF terminée.", "Nova-Fiches",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show(this, "Fusion PDF impossible : " + ex.Message, "Nova-Fiches",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

}
