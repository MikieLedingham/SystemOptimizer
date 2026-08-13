// File: CleanHistory.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Core.Cleanup
{
    /// <summary>One thing the cleaner removed, and where it came from.</summary>
    public sealed class CleanedItem
    {
        public string Path { get; set; }
        public long Bytes { get; set; }
        public bool IsFolder { get; set; }

        /// <summary>
        /// Which cleanup stage put this here - "Scanning browser caches" and so on.
        ///
        /// Recorded rather than worked out from the path afterwards. Guessing the area by
        /// pattern-matching the path is the kind of thing that stays right until somebody
        /// adds a browser, and this is also written into the undo manifest, where it
        /// answers "what did that run actually take?" long after the run.
        /// </summary>
        public string Area { get; set; }
    }

    /// <summary>
    /// A single cleanup run: what was chosen, what went to the Recycle Bin, what was
    /// deliberately left alone. This is the record "Restore a previous clean" works from,
    /// and the answer to "what did it actually do to my machine?".
    /// </summary>
    public sealed class CleanSession
    {
        public string Id { get; set; }
        public DateTime StartedLocal { get; set; }
        public DateTime FinishedLocal { get; set; }
        public List<string> Steps { get; set; } = new List<string>();
        public List<CleanedItem> Recycled { get; set; } = new List<CleanedItem>();
        public List<string> Skipped { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();

        [JsonIgnore] public long BytesRecycled => Recycled.Sum(i => i.Bytes);
        [JsonIgnore] public int FileCount => Recycled.Count(i => !i.IsFolder);
        [JsonIgnore] public int FolderCount => Recycled.Count(i => i.IsFolder);

        /// <summary>What the restore list shows, e.g. "8 Aug 2026, 18:07 - 1,204 items, 342 MB".</summary>
        [JsonIgnore]
        public string Summary =>
            $"{StartedLocal:d MMM yyyy, HH:mm}  -  {Recycled.Count:N0} items, " +
            $"{BytesRecycled / (1024.0 * 1024.0):N0} MB";
    }

    /// <summary>Reads and writes the per-run manifests under %APPDATA%\System Optimizer\history.</summary>
    public static class CleanHistory
    {
        /// <summary>How many past runs stay restorable. Older manifests are pruned.</summary>
        public const int KeepSessions = 10;

        public static CleanSession Begin()
        {
            return new CleanSession
            {
                Id = DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                StartedLocal = DateTime.Now
            };
        }

        public static void Save(CleanSession session)
        {
            if (session == null) return;
            try
            {
                Directory.CreateDirectory(AppPaths.HistoryDir);
                session.FinishedLocal = DateTime.Now;
                string file = Path.Combine(AppPaths.HistoryDir, $"clean_{session.Id}.json");
                File.WriteAllText(file, JsonConvert.SerializeObject(session, Formatting.Indented));
                Prune();
            }
            catch (Exception ex)
            {
                LogHelper.Log("CleanHistory.Save failed: " + ex);
            }
        }

        private static string _summaryFile;
        private static DateTime _summaryStamp;
        private static (int Files, long Bytes, DateTime When)? _summary;

        /// <summary>
        /// What the most recent cleanup moved, for the overlay's standing line.
        ///
        /// Read from the manifest rather than kept in preferences, so there is one source
        /// and it survives a restart - CleanupHelper's totals are per-run statics and read
        /// zero on next launch.
        ///
        /// CACHED on the newest file's path and timestamp, because the overlay asks about
        /// once a second and a manifest for a large run is a couple of megabytes of JSON.
        /// Listing a ten-file directory is cheap; parsing that every tick would not be.
        /// </summary>
        public static (int Files, long Bytes, DateTime When)? LastCleanSummary()
        {
            try
            {
                if (!Directory.Exists(AppPaths.HistoryDir)) return null;

                var newest = Directory.GetFiles(AppPaths.HistoryDir, "clean_*.json")
                                      .OrderByDescending(f => f)
                                      .FirstOrDefault();
                if (newest == null) return null;

                var stamp = File.GetLastWriteTimeUtc(newest);
                if (_summary != null && newest == _summaryFile && stamp == _summaryStamp)
                    return _summary;

                var s = JsonConvert.DeserializeObject<CleanSession>(File.ReadAllText(newest));
                if (s == null) return null;

                long bytes = 0;
                int files = 0;
                foreach (var item in s.Recycled)
                {
                    if (item.IsFolder) continue;
                    files++;
                    bytes += item.Bytes;
                }

                _summaryFile = newest;
                _summaryStamp = stamp;
                _summary = (files, bytes, s.FinishedLocal);
                return _summary;
            }
            catch (Exception ex)
            {
                LogHelper.Log("CleanHistory.LastCleanSummary failed: " + ex);
                return null;
            }
        }

        private static long _survivorsForBinCount = -1;
        private static string _survivorsForFile;
        private static (int Files, long Bytes)? _survivors;
        private static int _survivorsWorking;

        /// <summary>
        /// How much of the LAST run is still sitting in the Recycle Bin.
        ///
        /// The manifest records what was moved and nothing after that, so on its own it
        /// keeps reporting eleven thousand recoverable files however many of them the user
        /// has since deleted by hand. Answering properly means intersecting the recorded
        /// paths with the bin's own index.
        ///
        /// Two things make that affordable:
        ///
        ///   - It is recomputed ONLY when the bin's item count changes. That count comes
        ///     from SHQueryRecycleBin, which is cheap and already polled, and it is the
        ///     only thing that can change the answer.
        ///   - The recompute runs on a BACKGROUND thread. Reading the $I index is one file
        ///     read per item - twelve thousand of them on this machine - and the caller is
        ///     a once-a-second UI timer. Doing it inline would freeze the overlay for
        ///     seconds every time anything anywhere deleted a file.
        ///
        /// So this returns the last known answer immediately and lets the next tick pick
        /// up the new one. A figure a second out of date is worth far more than a frozen
        /// window, and this line was previously wrong indefinitely rather than briefly.
        /// </summary>
        public static (int Files, long Bytes)? LastCleanStillInBin()
        {
            var summary = LastCleanSummary();
            if (summary == null) return null;

            var (_, binItems) = RecycleBin.CurrentContents();
            if (binItems < 0) return _survivors;          // bin unreadable: keep what we had

            bool stale = _survivors == null
                         || _survivorsForFile != _summaryFile
                         || _survivorsForBinCount != binItems;

            if (stale && System.Threading.Interlocked.CompareExchange(ref _survivorsWorking, 1, 0) == 0)
            {
                string file = _summaryFile;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var session = JsonConvert.DeserializeObject<CleanSession>(File.ReadAllText(file));
                        var present = RecycleBin.PresentOriginalPaths();
                        if (session == null || present == null) return;

                        int files = 0;
                        long bytes = 0;
                        foreach (var item in session.Recycled)
                        {
                            if (item.IsFolder || !present.Contains(item.Path)) continue;
                            files++;
                            bytes += item.Bytes;
                        }

                        _survivors = (files, bytes);
                        _survivorsForFile = file;
                        _survivorsForBinCount = binItems;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Log("CleanHistory.LastCleanStillInBin failed: " + ex.Message);
                    }
                    finally
                    {
                        System.Threading.Volatile.Write(ref _survivorsWorking, 0);
                    }
                });
            }

            return _survivors;
        }

        /// <summary>Most recent run first.</summary>
        public static List<CleanSession> Recent(int count = KeepSessions)
        {
            var sessions = new List<CleanSession>();
            try
            {
                if (!Directory.Exists(AppPaths.HistoryDir)) return sessions;

                foreach (var file in Directory.GetFiles(AppPaths.HistoryDir, "clean_*.json")
                                              .OrderByDescending(f => f)
                                              .Take(count))
                {
                    try
                    {
                        var s = JsonConvert.DeserializeObject<CleanSession>(File.ReadAllText(file));
                        if (s != null) sessions.Add(s);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Log($"CleanHistory: unreadable manifest {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log("CleanHistory.Recent failed: " + ex);
            }
            return sessions;
        }

        private static void Prune()
        {
            try
            {
                var stale = Directory.GetFiles(AppPaths.HistoryDir, "clean_*.json")
                                     .OrderByDescending(f => f)
                                     .Skip(KeepSessions);
                foreach (var file in stale)
                {
                    try { File.Delete(file); } catch { /* a stale manifest is not worth failing over */ }
                }
            }
            catch { }
        }
    }
}
