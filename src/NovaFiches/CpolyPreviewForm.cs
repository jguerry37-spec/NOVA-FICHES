using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TopoRapportWin;

// Fenêtre secondaire non modale pour l'aperçu PDF en direct du rapport "Contrôle de polygonale"
// (thème 5B du plan) : WebView2 sait nativement afficher un PDF local via une URL file://, jamais
// utilisé jusqu'ici dans ce projet (tous les autres PDF s'ouvrent via Process.Start/UseShellExecute
// dans le lecteur externe par défaut - voir MainForm.cs). Une seule instance créée et réutilisée
// (pas une nouvelle fenêtre par clic sur "Aperçu PDF") : Show()/BringToFront() + re-Navigate() vers
// un nouveau fichier temporaire à chaque appel. Codée à la main (pas de designer/resx), cohérent
// avec Licensing/LicenseForm.cs.
public sealed class CpolyPreviewForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private string? _lastTempFile;

    // Environnement WebView2 dédié, distinct de celui de la fenêtre principale (MainForm.
    // WebViewUserDataDir) : deux CoreWebView2Environment ne peuvent pas pointer vers le même
    // dossier utilisateur en même temps. Pas de logique de fingerprint/cache-bust ici (contrairement
    // à celui de MainForm) : ce WebView2 n'affiche jamais d'assets HTML/JS versionnés, seulement des
    // PDF locaux dont le nom de fichier change déjà à chaque aperçu.
    private static string UserDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NOVATLAS", "Nova-Fiches", "WebView2_Preview");

    public CpolyPreviewForm()
    {
        Text = "Aperçu — Contrôle de polygonale";
        StartPosition = FormStartPosition.Manual;
        Size = new Size(900, 1000);
        Controls.Add(_webView);
    }

    public async Task ShowPreviewAsync(string pdfPath)
    {
        if (_webView.CoreWebView2 == null)
        {
            Directory.CreateDirectory(UserDataDir);
            var env = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: UserDataDir);
            await _webView.EnsureCoreWebView2Async(env);
        }

        _webView.CoreWebView2!.Navigate(new Uri(pdfPath).AbsoluteUri);

        if (!Visible)
        {
            PositionBesideOwner();
            Show();
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();

        // Nettoie l'aperçu précédent (évite d'accumuler des PDF temporaires orphelins au fil des
        // clics "Aperçu") - échoue silencieusement si le fichier est encore ouvert/verrouillé, sans
        // conséquence (juste un fichier temporaire de plus, nettoyé par Windows tôt ou tard).
        if (_lastTempFile != null && !string.Equals(_lastTempFile, pdfPath, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(_lastTempFile); } catch { /* verrouillé ou déjà supprimé : sans conséquence */ }
        }
        _lastTempFile = pdfPath;
    }

    private void PositionBesideOwner()
    {
        if (Owner == null) return;
        var screen = Screen.FromControl(Owner);
        int x = Math.Min(Owner.Right + 8, screen.WorkingArea.Right - Width);
        Location = new Point(Math.Max(x, screen.WorkingArea.Left), Owner.Top);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Fenêtre réutilisée, pas détruite par une simple fermeture utilisateur (sinon le prochain
        // clic sur "Aperçu PDF" échouerait, la Form étant disposée) - seule la fermeture de la
        // fenêtre principale (Owner, CloseReason.FormOwnerClosing) doit vraiment la détruire.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
