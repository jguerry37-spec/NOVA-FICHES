using System;
using System.IO;
using System.Text;

namespace TopoRapportWin;

internal static class AppLog
{
    private static readonly object _lock = new();

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NOVATLAS", "Nova-Fiches");

    public static string LogsDir => Path.Combine(AppDataDir, "Logs");

    public static string CurrentLogPath =>
        Path.Combine(LogsDir, $"Nova-Fiches_{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        var sb = new StringBuilder();
        sb.Append(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.AppendLine(ex.ToString());
        }
        Write("ERROR", sb.ToString());
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogsDir);

            lock (_lock)
            {
                File.AppendAllText(
                    CurrentLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                    Encoding.UTF8
                );
            }
        }
        catch
        {
            // Ne jamais faire planter l'app à cause du logging.
        }
    }
}
