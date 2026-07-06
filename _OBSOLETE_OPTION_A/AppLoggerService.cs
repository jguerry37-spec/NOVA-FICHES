using System;
using System.IO;
using System.Text;

namespace TopoRapportWin
{
    /// <summary>
    /// Service de logging centralisé pour Nova-Fiches.
    /// Écrit les logs dans %APPDATA%\Nova-Fiches\logs\app.log
    /// </summary>
    public static class AppLoggerService
    {
        private static readonly string LogDirectory;
        private static readonly string LogFilePath;
        private static readonly object LockObject = new object();

        static AppLoggerService()
        {
            // Créer le répertoire logs dans APPDATA
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            LogDirectory = Path.Combine(appDataPath, "Nova-Fiches", "logs");
            LogFilePath = Path.Combine(LogDirectory, "app.log");

            // Créer le répertoire s'il n'existe pas
            try
            {
                Directory.CreateDirectory(LogDirectory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur création répertoire logs: {ex.Message}");
            }
        }

        /// <summary>
        /// Énumération des niveaux de log
        /// </summary>
        public enum LogLevel
        {
            Debug = 0,
            Info = 1,
            Warning = 2,
            Error = 3,
            Critical = 4
        }

        /// <summary>
        /// Écrit un message de log avec timestamp
        /// </summary>
        public static void Log(LogLevel level, string message, Exception? exception = null)
        {
            lock (LockObject)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logMessage = $"[{timestamp}] [{level}] {message}";

                    if (exception != null)
                    {
                        logMessage += Environment.NewLine + $"Exception: {exception.Message}" + Environment.NewLine;
                        logMessage += $"StackTrace: {exception.StackTrace}";
                    }

                    // Écrire dans le fichier
                    File.AppendAllText(LogFilePath, logMessage + Environment.NewLine, Encoding.UTF8);

                    // Aussi en console pour DEBUG
                    Console.WriteLine(logMessage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur écriture log: {ex.Message}");
                }
            }
        }

        public static void Debug(string message) => Log(LogLevel.Debug, message);
        public static void Info(string message) => Log(LogLevel.Info, message);
        public static void Warning(string message) => Log(LogLevel.Warning, message);
        public static void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
        public static void Critical(string message, Exception? ex = null) => Log(LogLevel.Critical, message, ex);

        /// <summary>
        /// Retourne le chemin du fichier log pour inspection manuelle
        /// </summary>
        public static string GetLogFilePath() => LogFilePath;

        /// <summary>
        /// Efface les logs (utile pour nettoyer avant une session de test)
        /// </summary>
        public static void ClearLogs()
        {
            lock (LockObject)
            {
                try
                {
                    if (File.Exists(LogFilePath))
                        File.Delete(LogFilePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur suppression logs: {ex.Message}");
                }
            }
        }
    }
}
