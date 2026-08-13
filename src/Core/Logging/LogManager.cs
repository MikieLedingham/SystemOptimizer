// File: Helpers/LogManager.cs
using System;
using System.Diagnostics;
using System.IO;
using SystemOptimizer.Core.Settings;
namespace SystemOptimizer.Core.Logging
{
    public static class LogManager
    {
        // Updated log root: Documents\Mikies Tools\logs
        private static readonly string LogRoot = AppPaths.LogsDir;
        private static string LogFileName => $"SystemOptimizer_{DateTime.UtcNow:yyyy-MM-dd}.log";
        /// <summary>
        /// Returns the folder where logs are stored (Documents\Mikies Tools\logs).
        /// </summary>
        public static string GetLogFolder()
        {
            EnsureLogDirectory();
            return LogRoot;
        }
        /// <summary>
        /// Returns the current log file path (today's log).
        /// </summary>
        public static string GetLogFilePath()
        {
            EnsureLogDirectory();
            return Path.Combine(LogRoot, LogFileName);
        }
        /// <summary>
        /// Appends a log entry to the current log file, with timestamp.
        /// </summary>
        public static void WriteLog(string message)
        {
            try
            {
                EnsureLogDirectory();
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(GetLogFilePath(), line + Environment.NewLine);
            }
            catch
            {
                // Optionally handle/log failures elsewhere, but do not throw from logger.
            }
        }
        /// <summary>
        /// Opens the logs folder in Explorer.
        /// </summary>
        public static void OpenLogFolder()
        {
            try
            {
                EnsureLogDirectory();
                Process.Start("explorer.exe", LogRoot);
            }
            catch { }
        }
        /// <summary>
        /// Ensures the log directory exists.
        /// </summary>
        private static void EnsureLogDirectory()
        {
            try
            {
                if (!Directory.Exists(LogRoot))
                    Directory.CreateDirectory(LogRoot);
            }
            catch { }
        }
        /// <summary>
        /// Deletes log files older than the given retention period.
        /// </summary>
        public static void PruneOldLogs(int daysToKeep = 30)
        {
            try
            {
                EnsureLogDirectory();
                var files = Directory.GetFiles(LogRoot, "SystemOptimizer_*.log");
                foreach (var file in files)
                {
                    var fi = new FileInfo(file);
                    if (fi.LastWriteTime < DateTime.Now.AddDays(-daysToKeep))
                        fi.Delete();
                }
            }
            catch { }
        }
    }
}
