using System;
using System.Windows.Forms;
using TopoRapportWin.Licensing;

namespace TopoRapportWin;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Capture exceptions UI (WinForms)
        Application.ThreadException += (_, e) =>
        {
            AppLog.Error("Unhandled UI exception", e.Exception);

            MessageBox.Show(
                "Une erreur inattendue est survenue.\n\n" +
                "Un journal a été enregistré dans les logs.",
                "Nova-Fiches",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        // Capture exceptions hors UI
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                AppLog.Error("Unhandled non-UI exception", ex);
            else
                AppLog.Error("Unhandled non-UI exception (non-Exception object)");
        };

        // Log de démarrage
        AppLog.Info("=== Application start ===");
        AppLog.Info($"BaseDirectory: {AppContext.BaseDirectory}");

        // Activation par licence hors-ligne (V2). Bloquant : sans licence valide,
        // l'application ne va pas jusqu'à MainForm. Pas de vérification réseau,
        // pas de mode dégradé (cohérent avec un usage interne Novatlas).
        var licenseResult = LicenseService.LoadAndValidate();
        if (!licenseResult.IsValid)
        {
            AppLog.Info($"License: statut au démarrage = {licenseResult.Status} ({licenseResult.Message})");
            using var licenseForm = new LicenseForm(licenseResult);
            if (licenseForm.ShowDialog() != DialogResult.OK)
            {
                AppLog.Info("License: activation annulée par l'utilisateur — fermeture de l'application.");
                return;
            }
        }

        Application.Run(new MainForm());
    }
}
