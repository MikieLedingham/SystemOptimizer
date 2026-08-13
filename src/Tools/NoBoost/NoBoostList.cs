// File: Tools/NoBoost/NoBoostList.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Tools.NoBoost
{
    /// <summary>
    /// The applications the user has chosen to suppress automatic RAM boosting for.
    ///
    /// One place reads and writes AppsList.json, because there were three and they
    /// disagreed about its shape. The file holds a list of objects - Name, ExePath,
    /// Selected - but GamesListManager and GamingModeManager both read and wrote a plain
    /// list of strings. Neither was ever called, which is the only reason the file was
    /// still intact: GamesListManager.SaveGamesList would have overwritten every entry's
    /// Selected flag and path with a bare name the moment anything used it.
    /// </summary>
    public static class NoBoostList
    {
        public static List<NoBoostEntry> Load()
        {
            try
            {
                string path = AppPaths.AppsListFile;
                if (!File.Exists(path)) return new List<NoBoostEntry>();
                return JsonConvert.DeserializeObject<List<NoBoostEntry>>(File.ReadAllText(path))
                       ?? new List<NoBoostEntry>();
            }
            catch (Exception ex)
            {
                LogHelper.Log("No-boost list could not be read: " + ex.Message);
                return new List<NoBoostEntry>();
            }
        }

        public static void Save(IEnumerable<NoBoostEntry> entries)
        {
            try
            {
                string path = AppPaths.AppsListFile;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogHelper.Log("No-boost list could not be saved: " + ex.Message);
            }
        }

        public static List<NoBoostEntry> Selected() =>
            Load().Where(e => e.Selected && !string.IsNullOrWhiteSpace(e.Name)).ToList();

        /// <summary>
        /// The process names an entry could appear as.
        ///
        /// Name is the executable's filename without its extension, which is exactly what
        /// Windows reports as a process name - ProgramScanner guarantees that, because
        /// nothing enters the list without a real executable behind it. ExePath is
        /// preferred where known because it is unambiguous; Name is the fallback for
        /// entries whose path could not be read (protected or cross-bitness processes).
        ///
        /// Entries written before the scanner was reworked may still hold a shortcut or
        /// folder name here - "Crystal Disk Info", "Adobe" - which cannot match anything.
        /// Rescanning replaces them.
        /// </summary>
        public static IEnumerable<string> ProcessKeys(NoBoostEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.ExePath))
            {
                string fromPath = null;
                try { fromPath = Path.GetFileNameWithoutExtension(entry.ExePath); } catch { }
                if (!string.IsNullOrWhiteSpace(fromPath)) yield return fromPath;
            }

            if (!string.IsNullOrWhiteSpace(entry.Name))
            {
                // Tolerate a name recorded with its extension, e.g. from a hand-typed entry.
                yield return entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? entry.Name.Substring(0, entry.Name.Length - 4)
                    : entry.Name;
            }
        }
    }
}
