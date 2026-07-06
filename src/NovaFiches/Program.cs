using System;
using System.Windows.Forms;

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

        Application.Run(new MainForm());
    }
}
