using System;
using System.IO;

namespace SystemOptimizer.Core.Settings
{
    /// <summary>
    /// Single source of truth for every path the application reads or writes.
    ///
    /// Before 2.0 these were scattered across 17 call sites in 13 files, split over two
    /// root folders both named "Mikies Tools" - one under %APPDATA% and one under
    /// Documents - with four separate log destinations ("logs", "Logs", a loose
    /// systemoptimizer.log, and "Boost Logs"). Two files called preferences.json existed
    /// in different roots and disagreed about their contents.
    ///
    /// Everything now lives under %APPDATA%\System Optimizer. Migration from the old
    /// locations runs once, automatically, from the static constructor - so it happens
    /// before any consumer can read a path, regardless of initialisation order.
    /// </summary>
    public static class AppPaths
    {
        private const string AppFolderName = "System Optimizer";
        private const string LegacyFolderName = "Mikies Tools";

        private static readonly string AppData =
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string Documents =
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>%APPDATA%\System Optimizer - the one root.</summary>
        public static string Root { get; } = Path.Combine(AppData, AppFolderName);

        public static string LogsDir { get; } = Path.Combine(Root, "logs");
        public static string BoostLogsDir { get; } = Path.Combine(LogsDir, "boost");
        /// <summary>One manifest per cleanup run - what "Restore a previous clean" reads.</summary>
        public static string HistoryDir { get; } = Path.Combine(Root, "history");

        /// <summary>
        /// Where the Sanity Check guide is written when someone asks to read it.
        ///
        /// Here rather than beside the executable for two reasons. The publish is a single
        /// file, and a folder that has to ship next to it retires that. And under Program
        /// Files the application's own directory is not user-writable, which is already a
        /// live bug for bootstrap.log.
        /// </summary>
        public static string GuideDir { get; } = Path.Combine(Root, "guide");

        public static string PreferencesFile { get; } = Path.Combine(Root, "preferences.json");
        /// <summary>
        /// Sanity Check bookkeeping - which checks have quarantined themselves and which
        /// findings the user has dismissed. Kept out of preferences.json on purpose: it is
        /// state the program maintains about itself, not a choice the user made, and one
        /// entry per check would bury the settings a person might actually want to read.
        /// </summary>
        public static string SanityStateFile { get; } = Path.Combine(Root, "sanity-state.json");
        public static string AppsListFile { get; } = Path.Combine(Root, "AppsList.json");
        public static string ThemeFile { get; } = Path.Combine(Root, "theme.pref");
        public static string StartupLogFile { get; } = Path.Combine(LogsDir, "startup.log");
        public static string GeneralLogFile { get; } = Path.Combine(LogsDir, "systemoptimizer.log");

        // Legacy roots, kept only so the migration can find them.
        private static readonly string LegacyAppData = Path.Combine(AppData, LegacyFolderName);
        private static readonly string LegacyDocuments = Path.Combine(Documents, LegacyFolderName);

        static AppPaths()
        {
            try
            {
                bool fresh = !Directory.Exists(Root);
                Directory.CreateDirectory(Root);
                Directory.CreateDirectory(LogsDir);
                Directory.CreateDirectory(BoostLogsDir);
                Directory.CreateDirectory(HistoryDir);

                // Only migrate into a folder we just created. Never overwrite live data.
                if (fresh)
                {
                    // Documents first, then %APPDATA%, so that on a filename collision the
                    // %APPDATA% copy wins. That matters for preferences.json: PreferencesManager
                    // used the %APPDATA% copy, so that one is the real user state. The Documents
                    // copy was written only by two dialogs that nothing ever opened.
                    MigrateFrom(LegacyDocuments);
                    MigrateFrom(LegacyAppData);
                }
            }
            catch
            {
                // Never let path setup take the app down - callers create directories defensively.
            }
        }

        private static void MigrateFrom(string legacyRoot)
        {
            if (!Directory.Exists(legacyRoot)) return;

            foreach (var file in Directory.GetFiles(legacyRoot))
                CopyIfAbsent(file, Path.Combine(Root, Path.GetFileName(file)));

            foreach (var dir in Directory.GetDirectories(legacyRoot))
            {
                var name = Path.GetFileName(dir);
                // "logs" and "Logs" are the same folder on Windows; "Boost Logs" becomes logs\boost.
                string target =
                    name.Equals("Boost Logs", StringComparison.OrdinalIgnoreCase) ? BoostLogsDir :
                    name.Equals("logs", StringComparison.OrdinalIgnoreCase) ? LogsDir :
                    Path.Combine(Root, name);

                Directory.CreateDirectory(target);
                foreach (var file in Directory.GetFiles(dir))
                    CopyIfAbsent(file, Path.Combine(target, Path.GetFileName(file)));
            }
        }

        private static void CopyIfAbsent(string source, string destination)
        {
            try
            {
                // Copy, not move: the old folders are left intact so nothing is destroyed
                // if the user rolls back to an earlier build.
                if (!File.Exists(destination)) File.Copy(source, destination);
            }
            catch { /* skip individual files that are locked or unreadable */ }
        }
    }
}
