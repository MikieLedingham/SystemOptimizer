// File: CleanupHelper.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using SystemOptimizer.Dialogs;
using SystemOptimizer.Core.Ram;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Core.Cleanup
{
    public static class CleanupHelper
    {
        // ---- UI INVOKE HELPERS -------------------------------------------------
        private static T UI<T>(Func<T> func)
        {
            var app = Application.Current;
            if (app?.Dispatcher == null) return func();
            if (app.Dispatcher.CheckAccess()) return func();
            return app.Dispatcher.Invoke(func);
        }
        private static void UI(Action action)
        {
            var app = Application.Current;
            if (app?.Dispatcher == null)
            {
                action();
                return;
            }
            if (app.Dispatcher.CheckAccess()) action();
            else app.Dispatcher.Invoke(action);
        }

        /// <summary>Ensures a ProgressDialog exists and is visible.</summary>
        private static ProgressDialog EnsureProgressDialog()
        {
            return UI(() =>
            {
                var inst = ProgressDialog.Instance ?? new ProgressDialog
                {
                    Owner = Application.Current?.MainWindow
                };
                if (!inst.IsVisible) inst.Show();
                return inst;
            });
        }

        // ---- P/INVOKE RECYCLE BIN ---------------------------------------------
        [DllImport("Shell32.dll")]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, RecycleFlags dwFlags);

        [Flags]
        public enum RecycleFlags : int
        {
            SHERB_NOCONFIRMATION = 0x00000001,
            SHERB_NOPROGRESSUI = 0x00000002,
            SHERB_NOSOUND = 0x00000004
        }

        // ---- SAFETY ------------------------------------------------------------
        /// <summary>
        /// Anything created or written inside this window is assumed to belong to a
        /// process that is still running. Temp directories are working storage for
        /// live applications, not garbage - deleting a scratch file out from under an
        /// app turns our "cleanup" into their crash.
        /// </summary>
        private static readonly TimeSpan InUseGrace = TimeSpan.FromHours(24);

        /// <summary>
        /// Downloads is the user's own data, not scratch space, so it gets a far longer
        /// leash than a temp folder: only things untouched for a month are candidates.
        /// </summary>
        private static readonly TimeSpan DownloadsAge = TimeSpan.FromDays(30);

        // ---- METRICS -----------------------------------------------------------
        public static int LastUsedRamMB { get; set; }
        public static int TotalFilesDeleted { get; private set; }
        public static int TotalFoldersDeleted { get; private set; }
        public static long TotalBytesFreed { get; private set; }
        /// <summary>Items left alone because they were too new, or were a link.</summary>
        public static int TotalItemsSkipped { get; private set; }

        /// <summary>Files and bytes actually recycled, per cleanup stage.</summary>
        public static Dictionary<string, (int Files, long Bytes)> ByArea { get; } = new();

        // ---- PUBLIC QUICK RAM ONLY (Hotkey B / Tray "Boost RAM Now") -----------
        /// <summary>
        /// Performs a *quick* RAM-only boost (user-mode trim + optional admin pass),
        /// caps displayed recovery, records stats, always notifies user.
        /// </summary>
        public static int RunRamOnlyQuickTrim(bool isAdmin)
        {
            try
            {
                var infoBefore = new Microsoft.VisualBasic.Devices.ComputerInfo();
                ulong beforeAvail = infoBefore.AvailablePhysicalMemory;

                UserModeRamBooster.ClearAllProcessWorkingSets();
                if (isAdmin)
                    RamCleanupHelper.PerformRamCleanup();

                ulong afterAvail = new Microsoft.VisualBasic.Devices.ComputerInfo().AvailablePhysicalMemory;
                long deltaBytes = (long)afterAvail - (long)beforeAvail;
                if (deltaBytes < 0) deltaBytes = 0;
                int freedMB = (int)Math.Round(deltaBytes / (1024.0 * 1024.0));

                LastUsedRamMB = freedMB;

                PreferencesManager.RecordRamBoost(freedMB, automatic: false);

                OverlayWindow.RefreshAllAfterRamBoost(); // uses in-memory property
                App.ShowTrayNotification(
                    freedMB > 0
                        ? $"Quick RAM Boost: {freedMB} MB recovered."
                        : "Quick RAM Boost: No reclaimable RAM right now.");

                return freedMB;
            }
            catch (Exception ex)
            {
                LogHelper.Log("RunRamOnlyQuickTrim EXCEPTION: " + ex);
                App.ShowTrayNotification("Quick RAM boost failed.");
                return 0;
            }
        }
        // ---- FULL EXECUTION ----------------------------------------------------
        public static void ExecuteCleanup(BoostOptions opts, bool isAdmin)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));

            // Compute step count (only those selected; Recycle Bin only if admin)
            int stepCount = 0;
            void Count(bool cond) { if (cond) stepCount++; }

            // The admin-only steps are gated HERE as well as by the caller. The caller
            // clearing them is what keeps the confirmation dialog honest; this is what
            // makes the engine incapable of the operation regardless of who asks. Only
            // CleanRecycleBin used to be gated, so an unelevated run really did walk
            // C:\Windows\Temp, the WER queues and Windows.old.
            Count(opts.CleanUserTemp);
            Count(opts.CleanWindowsTemp && isAdmin);
            Count(opts.CleanBrowserCache);
            Count(opts.CleanDownloadsFolder);
            Count(opts.CleanRecent);
            Count(opts.CleanDNSCache);
            Count(opts.CleanCrashDumps && isAdmin);
            Count(opts.CleanOldWindows && isAdmin);
            Count(opts.CleanRecycleBin && isAdmin);
            Count(opts.BoostRam);

            if (stepCount == 0)
            {
                App.ShowTrayNotification("No cleanup options selected.");
                return;
            }

            // Reset metrics
            TotalFilesDeleted = 0;
            TotalFoldersDeleted = 0;
            TotalBytesFreed = 0;
            TotalItemsSkipped = 0;
            ByArea.Clear();

            // Nothing is removed while we walk. We build a plan first, then hand the whole
            // batch to the Recycle Bin in one operation and write down exactly what went.
            var plan = new CleanPlan();
            var session = CleanHistory.Begin();

            // Logging target
            string logDir = AppPaths.LogsDir;
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, $"SystemOptimizer_BoostLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            List<string> logLines = new() { $"Boost Cleanup Log - {DateTime.Now}", "" };

            ProgressDialog progress = null;
            try
            {
                progress = EnsureProgressDialog();
                UI(() => progress.InitializeProgress(stepCount));
                _progress = progress;
                _examined = 0;
                _stage = "Starting";
            }
            catch (Exception ex)
            {
                LogHelper.Log("ExecuteCleanup: progress init failed: " + ex);
            }

            void LogStep(string label)
            {
                logLines.Add("✔ " + label);
                session.Steps.Add(label);
                UI(() => progress?.AddStep(label));
            }
            void LogError(string label, Exception ex)
            {
                logLines.Add($"✖ {label} – {ex.Message}");
                session.Errors.Add($"{label}: {ex.Message}");
                // The label is what the user reads in the progress list, so it has to be the
                // step that failed - not a file path, which used to produce one unreadable
                // row per locked file.
                UI(() => progress?.AddError($"{label} – {ex.Message}"));
                LogHelper.Log($"Cleanup step '{label}' failed: {ex}");
            }

            // Neither a success tick nor a failure cross: something the user should know
            // that did not go wrong. Files being in use is the obvious case - it is normal,
            // it is not a fault, and dressing it up as a failed step trains people to
            // ignore the one row that would matter if it ever meant something.
            void LogNote(string note)
            {
                logLines.Add($"• {note}");
                UI(() => progress?.AddNote(note));
                LogHelper.Log("Cleanup note: " + note);
            }

            try
            {
                // --- BASIC CLEANUPS ------------------------------------------------
                // User Temp and App Temp resolve to the same folder on a normal profile
                // (%LOCALAPPDATA%\Temp), so a set keeps us from walking it twice and
                // reporting one folder as two steps.
                var tempRoots = new List<string>();
                void AddTempRoot(string p)
                {
                    if (string.IsNullOrWhiteSpace(p)) return;
                    string full = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);
                    if (!tempRoots.Any(t => t.Equals(full, StringComparison.OrdinalIgnoreCase)))
                        tempRoots.Add(full);
                }

                if (opts.CleanUserTemp)
                {
                    Stage("Scanning temporary files");
                    AddTempRoot(Path.GetTempPath());
                    AddTempRoot(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
                    foreach (var root in tempRoots)
                        Collect(root, plan, InUseGrace, pruneDirectories: false);
                    LogStep("Temp Files");
                }
                if (opts.CleanWindowsTemp && isAdmin)
                {
                    Stage("Scanning Windows temp");
                    Collect(@"C:\Windows\Temp", plan, InUseGrace, pruneDirectories: false);
                    LogStep("Windows Temp");
                }
                if (opts.CleanBrowserCache)
                {
                    // Was a comment. Literally: "// Placeholder for real browser cache
                    // cleaning." followed by LogStep - a ticked checkbox that did nothing
                    // and wrote a tick to the log, which is the CleanRestorePoints fault
                    // with a different name.
                    Stage("Scanning browser caches");
                    int roots = 0;
                    foreach (var cacheDir in BrowserCacheRoots())
                    {
                        Collect(cacheDir, plan, InUseGrace, pruneDirectories: false);
                        roots++;
                    }
                    if (roots == 0) LogNote("Browser Cache: no browser caches found");
                    else LogStep("Browser Cache");
                }
                if (opts.CleanDownloadsFolder)
                {
                    Stage("Scanning downloads");
                    string downloads = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    // Downloads is user data, not scratch space. Only things left untouched
                    // for a month are candidates, and like everything else they go to the
                    // Recycle Bin rather than being destroyed.
                    Collect(downloads, plan, DownloadsAge, pruneDirectories: false);
                    LogStep("Downloads");
                }
                if (opts.CleanRecent)
                {
                    Stage("Scanning recent items");
                    string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                    Collect(recent, plan, InUseGrace, pruneDirectories: false);
                    LogStep("Recent Files");
                }
                if (opts.CleanDNSCache)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        })?.WaitForExit(5000);
                        LogStep("DNS Cache");
                    }
                    catch (Exception ex) { LogError("DNS Cache", ex); }
                }

                // --- ADMIN / SYSTEM CLEANUPS --------------------------------------
                if (opts.CleanCrashDumps && isAdmin)
                {
                    Collect(@"C:\ProgramData\Microsoft\Windows\WER\ReportQueue", plan, InUseGrace);
                    Collect(@"C:\ProgramData\Microsoft\Windows\WER\ReportArchive", plan, InUseGrace);
                    LogStep("Crash Dumps");
                }
                if (opts.CleanOldWindows && isAdmin)
                {
                    // Was: start "cleanmgr /sagerun:1", never wait, log the step as done.
                    // /sagerun:1 does nothing at all unless /sageset:1 was configured
                    // first, which nothing here ever did - so this reported a completed
                    // step for an operation that had almost certainly performed none.
                    //
                    // Now two things. Windows.old is deleted by this application, where
                    // the result is knowable; then Disk Cleanup is launched for the rest,
                    // deliberately WITHOUT waiting, and said so rather than claimed as
                    // finished: it runs outside SO, so SO should not pretend
                    // to own it.
                    try
                    {
                        DeleteWindowsOld(LogStep, LogError, LogNote);
                    }
                    catch (Exception ex) { LogError("Old Windows", ex); }

                    try
                    {
                        // /sageset:1 opens a UI, so it cannot be used unattended. Passing
                        // the cleanup categories directly is the unattended equivalent.
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cleanmgr.exe",
                            Arguments = "/verylowdisk",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        LogNote("Windows' own Disk Cleanup was started and may still be running after this finishes");
                    }
                    catch (Exception ex) { LogError("Disk Cleanup", ex); }
                }
                if (opts.CleanRecycleBin && isAdmin)
                {
                    try
                    {
                        long before = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)).AvailableFreeSpace;
                        int res = SHEmptyRecycleBin(IntPtr.Zero, null,
                            RecycleFlags.SHERB_NOCONFIRMATION |
                            RecycleFlags.SHERB_NOPROGRESSUI |
                            RecycleFlags.SHERB_NOSOUND);
                        long after = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)).AvailableFreeSpace;
                        if (res == 0)
                        {
                            long freed = after - before;
                            if (freed > 0) TotalBytesFreed += freed;
                            LogStep("Recycle Bin");
                        }
                        else
                            LogError("Recycle Bin", new Exception("Code " + res));
                    }
                    catch (Exception ex) { LogError("Recycle Bin", ex); }
                }

                // --- RAM BOOST ----------------------------------------------------
                if (opts.BoostRam)
                {
                    try
                    {
                        var infoBefore = new Microsoft.VisualBasic.Devices.ComputerInfo();
                        ulong beforeAvail = infoBefore.AvailablePhysicalMemory;

                        RamCleanupHelper.PerformRamCleanup(); // internal GC + trimming

                        ulong afterAvail = new Microsoft.VisualBasic.Devices.ComputerInfo().AvailablePhysicalMemory;
                        long deltaBytes = (long)afterAvail - (long)beforeAvail;
                        if (deltaBytes < 0) deltaBytes = 0;
                        int freedMB = (int)Math.Round(deltaBytes / (1024.0 * 1024.0));

                        LastUsedRamMB = freedMB;
                        PreferencesManager.RecordRamBoost(freedMB, automatic: false);
                        RamCleanupHelper.RecordManualBoost(freedMB);
                        OverlayWindow.RefreshAllAfterRamBoost();

                        LogStep("RAM Boost");
                        App.ShowTrayNotification(
                            freedMB > 0
                                ? $"RAM Boost completed: {freedMB} MB recovered."
                                : "RAM Boost: No reclaimable RAM right now."
                        );
                    }
                    catch (Exception ex) { LogError("RAM Boost", ex); }
                }

                // Nothing has been removed up to this point - do it now, in one batch.
                Apply(plan, session, LogError, LogNote);

                session.Skipped.AddRange(plan.Skipped);
                CleanHistory.Save(session);

                logLines.Add("");
                // "freeing N MB" was the old, destructive framing: nothing is freed by
                // this run. The bytes are what the user WILL reclaim if and when they
                // empty the bin, and that decision is deliberately theirs.
                logLines.Add($"Moved {TotalFilesDeleted} files and {TotalFoldersDeleted} folders " +
                             $"to the Recycle Bin. Emptying it will reclaim " +
                             $"{TotalBytesFreed / (1024 * 1024)} MB.");
                logLines.Add($"Left {TotalItemsSkipped} items alone:");
                foreach (var reason in plan.Skipped) logLines.Add("   - " + reason);
                logLines.Add("");
                logLines.Add("Nothing here was permanently deleted. Undo this run with " +
                             "Restore a previous clean.");

                // Write log
                bool logWritten = false;
                try { File.WriteAllLines(logFile, logLines); logWritten = true; }
                catch (Exception ex) { LogHelper.Log("Write log failed: " + ex); }

                // Handed over only once the file exists, so the summary cannot offer a
                // link to something that was never written.
                UI(() =>
                {
                    if (progress != null && logWritten) progress.RunLogPath = logFile;
                    progress?.MarkComplete();
                });

                // No "Cleanup finished." toast here, deliberately.
                //
                // By the time it appeared, the progress window had already written
                // "Cleanup complete. All selected areas have been optimised." and the
                // results window was opening on top of it. Three statements of the same
                // fact, one of which covered the other two while they were being read.
                //
                // The toasts that remain all report something no window is showing: the
                // RAM figure above (the results window gives the cleanup totals, not
                // that), and the quick RAM boost from the tray and window menus, which
                // has no window at all - a toast is its ONLY feedback. Removing that one
                // would make the menu item look like it did nothing.
            }
            catch (Exception ex)
            {
                LogHelper.Log("ExecuteCleanup UNHANDLED: " + ex);
                UI(() =>
                {
                    MessageBox.Show("Unexpected error: " + ex.Message,
                        "Boost Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }
        // ---- HELPERS -----------------------------------------------------------
        /// <summary>What a cleanup intends to do, built before anything is touched.</summary>
        private sealed class CleanPlan
        {
            public List<CleanedItem> Items { get; } = new List<CleanedItem>();
            public List<string> Skipped { get; } = new List<string>();

            public void Add(string path, long bytes, bool isFolder)
                => Items.Add(new CleanedItem { Path = path, Bytes = bytes, IsFolder = isFolder, Area = _stage });

            public void Skip(string reason)
            {
                TotalItemsSkipped++;
                // One line per *reason*, not per file - a thousand "in use" entries tell the
                // user nothing a single counted line doesn't.
                if (!Skipped.Contains(reason)) Skipped.Add(reason);
            }
        }

        private static bool IsReparsePoint(FileSystemInfo info)
            => (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

        private static bool IsRecent(FileSystemInfo info, TimeSpan grace)
        {
            if (grace <= TimeSpan.Zero) return false;
            DateTime cutoff = DateTime.UtcNow - grace;
            return info.LastWriteTimeUtc > cutoff || info.CreationTimeUtc > cutoff;
        }

        /// <summary>Judges a single file and adds it to the plan if it qualifies.</summary>
        private static void Consider(string file, CleanPlan plan, TimeSpan grace)
        {
            try
            {
                var fi = new FileInfo(file);
                if (IsReparsePoint(fi)) { plan.Skip("links were not followed"); return; }
                if (IsRecent(fi, grace)) { plan.Skip("in use or recently changed"); return; }
                if (fi.Length >= RecycleBin.MaxRecyclableBytes)
                {
                    plan.Skip($"larger than {RecycleBin.MaxRecyclableBytes / (1024 * 1024)} MB, " +
                              "too big to guarantee a Recycle Bin restore");
                    return;
                }
                plan.Add(file, fi.Length, isFolder: false);
            }
            catch (Exception ex)
            {
                plan.Skip("unreadable (" + ex.GetType().Name + ")");
            }
        }

        /// <summary>
        /// Walks <paramref name="path"/> and records what should go. Deletes nothing.
        /// </summary>
        /// <param name="grace">
        /// Skip anything created or written within this window. Never pass TimeSpan.Zero
        /// for a location a running process could be using.
        /// </param>
        /// <param name="pruneDirectories">
        /// Remove sub-directories once they are empty. Must be false for temp roots:
        /// applications create their scratch folder once at startup and then assume it
        /// exists for the life of the process.
        /// </param>
        // Live progress state for the current run. Static because the engine itself is,
        // and set to null the moment the run ends so a stale dialog is never poked.
        private static ProgressDialog _progress;
        private static string _stage = "";
        private static int _examined;

        /// <summary>Names the phase, and paints it immediately rather than waiting for the throttle.</summary>
        private static void Stage(string name)
        {
            _stage = name;
            _progress?.ReportActivity(_stage, "", _examined, 0, force: true);
        }

        /// <summary>Enough of a path to be recognisable, without overflowing the line.</summary>
        private static string ShortPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var parts = path.Split(Path.DirectorySeparatorChar);
            return parts.Length <= 3 ? path : string.Join("\\", parts.Skip(parts.Length - 3));
        }

        private static void Collect(string path, CleanPlan plan,
                                    TimeSpan grace, bool pruneDirectories = true)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

            try
            {
                // Never follow a junction or symlink - the target lives somewhere we
                // were never asked to touch.
                if (IsReparsePoint(new DirectoryInfo(path))) return;

                foreach (var file in Directory.GetFiles(path))
                {
                    Consider(file, plan, grace);
                    _examined++;
                    // Reported from the walk rather than per stage: the walk IS the slow
                    // part, and it is the part that used to show nothing at all.
                    _progress?.ReportActivity(_stage, ShortPath(path), _examined, plan.Items.Count);
                }

                foreach (var dir in Directory.GetDirectories(path))
                {
                    var di = new DirectoryInfo(dir);
                    if (IsReparsePoint(di)) { plan.Skip("links were not followed"); continue; }

                    // Snapshot before recursing: emptying a directory bumps its own
                    // LastWriteTime, which would otherwise make every folder look fresh.
                    bool wasRecent = IsRecent(di, grace);

                    Collect(dir, plan, grace, pruneDirectories);

                    if (!pruneDirectories || wasRecent) continue;

                    // Only offer up a directory we are about to leave empty, and only the
                    // directory itself - never a recursive delete, which would take the
                    // in-use files we deliberately spared.
                    bool everythingInsideIsGoing = Directory
                        .EnumerateFileSystemEntries(dir)
                        .All(entry => plan.Items.Any(i =>
                            i.Path.Equals(entry, StringComparison.OrdinalIgnoreCase)));

                    if (everythingInsideIsGoing) plan.Add(dir, 0, isFolder: true);
                }
            }
            catch (Exception ex)
            {
                plan.Skip($"{path} could not be read ({ex.GetType().Name})");
            }
        }

        /// <summary>
        /// Carries out the plan: one batched trip to the Recycle Bin, then the manifest
        /// that makes it undoable.
        /// </summary>
        private static void Apply(CleanPlan plan, CleanSession session,
                                  Action<string, Exception> logError,
                                  Action<string> logNote)
        {
            if (plan.Items.Count == 0) return;

            // Deepest paths first so a directory is never sent before its contents.
            var ordered = plan.Items
                .OrderByDescending(i => i.Path.Count(c => c == Path.DirectorySeparatorChar))
                .ToList();

            // Re-check existence IMMEDIATELY before the shell call.
            //
            // The plan was built by walking the disk, which takes a while - eighteen
            // thousand items in a real run - and the machine does not hold still while
            // that happens. A running browser rotates its cache constantly, so by the time
            // the batch is submitted some of those paths are already gone.
            //
            // Two things went wrong because of that. The shell returned DE_INVALIDFILES
            // (0x7C, "one of the paths was not valid") and the run ended on a red cross,
            // for an ordinary race rather than a fault. And worse, the verification loop
            // below asks only "is it gone?" - so a file the BROWSER deleted counted as one
            // SO had recycled: it went into the undo manifest, into the file count and
            // into the bytes-freed figure. Restore would then look for it in the Recycle
            // Bin, where it had never been.
            _progress?.ReportActivity("Checking what is still there", "", _examined, ordered.Count, force: true);

            int vanished = 0;
            var present = new List<CleanedItem>();
            foreach (var item in ordered)
            {
                bool exists = item.IsFolder ? Directory.Exists(item.Path) : File.Exists(item.Path);
                if (exists) present.Add(item);
                else vanished++;
            }
            ordered = present;

            if (ordered.Count == 0)
            {
                if (vanished > 0)
                    logNote($"{vanished} item{(vanished == 1 ? " was" : "s were")} already gone before the cleanup ran");
                return;
            }

            _progress?.ReportRecycling(0, ordered.Count);

            var outcome = SendIsolatingBadPaths(ordered);
            bool shellOk = outcome.Ok;
            string error = outcome.Error;
            bool filesInUse = outcome.FilesInUse;
            var rejected = outcome.Refused;

            _progress?.ReportRecycling(ordered.Count - rejected.Count, ordered.Count);

            // A file that stopped existing is a race, not a refusal, and the log has to say
            // so. Reporting it as "refused by Windows" is what made an ordinary browser
            // cache rotation read as a fault.
            if (outcome.Vanished.Count > 0)
            {
                logNote($"{outcome.Vanished.Count} item{(outcome.Vanished.Count == 1 ? "" : "s")} " +
                        "disappeared while the cleanup was running and were skipped " +
                        "(usually a browser rewriting its own cache)");
            }

            if (outcome.InUse.Count > 0)
            {
                logNote($"{outcome.InUse.Count} item{(outcome.InUse.Count == 1 ? " was" : "s were")} " +
                        "in use by another program and left alone");
                foreach (var p in outcome.InUse.Take(5))
                    logNote("   in use: " + p);
            }

            if (outcome.NotTested.Count > 0)
            {
                logNote($"{outcome.NotTested.Count} paths were left alone without being tested " +
                        "individually: too many were being refused to isolate them one by one");
            }

            // The count is logged because its absence is what made the slow run
            // undiagnosable. A healthy batch is 1; the run that looked frozen was of the
            // order of fifteen thousand.
            if (outcome.ShellCalls > 1)
                LogHelper.Log($"Recycle Bin: {outcome.ShellCalls} shell operations for {ordered.Count} items");

            // Drop the raced paths from the plan BEFORE the verification pass below.
            //
            // This is load-bearing, and it is the same trap that was closed once already
            // for the pre-flight check. That pass asks one question - "is it gone?" - so a
            // file the BROWSER deleted looks exactly like a file this program recycled. It
            // would be counted in the totals, reported as space reclaimed, and written into
            // the undo manifest, and Restore would then hunt for it in a Recycle Bin it had
            // never been in.
            //
            // The pre-flight check cannot prevent this on its own: these are precisely the
            // paths that survived it and then vanished in the milliseconds before the shell
            // call. They have to be removed here too, where they are finally known.
            if (outcome.Vanished.Count > 0)
            {
                var raced = new HashSet<string>(outcome.Vanished, StringComparer.OrdinalIgnoreCase);
                ordered = ordered.Where(i => !raced.Contains(i.Path)).ToList();
            }

            // One bad path used to cost the entire run. SHFileOperation is atomic about
            // its complaint: hand it a batch containing a single path it dislikes and it
            // can refuse ALL of it, which is why a real run reported "one of the paths was
            // not valid" and moved zero files while sixteen thousand candidates sat there.
            //
            // Naming them also ends the guessing. The log now says which path the shell
            // would not take, so the next occurrence is diagnosable instead of mysterious.
            if (rejected.Count > 0)
            {
                logNote($"{rejected.Count} path{(rejected.Count == 1 ? " was" : "s were")} refused by Windows and left alone");
                foreach (var p in rejected.Take(10))
                    logNote("   refused: " + p);
                if (rejected.Count > 10)
                    logNote($"   ...and {rejected.Count - 10} more");
            }

            // DO NOT return early when the shell reports a problem.
            //
            // SHFileOperation reports a fault with the BATCH, not the absence of one: it
            // routinely recycles almost everything and returns non-zero because a single
            // file was locked. Bailing out here meant every file that HAD gone to the bin
            // was left out of session.Recycled - so "Restore a previous clean" could not
            // bring back files that genuinely had been recycled, and the summary reported
            // zero files freed when it had in fact freed thousands. The verification pass
            // below is the source of truth either way; run it regardless.
            int leftBehind = 0;
            foreach (var item in ordered)
            {
                bool gone = item.IsFolder ? !Directory.Exists(item.Path) : !File.Exists(item.Path);
                if (!gone)
                {
                    plan.Skip("in use by another program");
                    leftBehind++;
                    continue;
                }

                session.Recycled.Add(item);
                if (item.IsFolder) TotalFoldersDeleted++;
                else
                {
                    TotalFilesDeleted++;
                    TotalBytesFreed += item.Bytes;

                    // Per-area tally, so the results window can say where the space came
                    // from instead of only how much there was. The cache
                    // figure specifically, and on his machine it is most of the run:
                    // 10,824 of 11,064 files.
                    string area = item.Area ?? "Other";
                    ByArea.TryGetValue(area, out var t);
                    ByArea[area] = (t.Files + 1, t.Bytes + item.Bytes);
                }
            }

            if (vanished > 0)
                logNote($"{vanished} item{(vanished == 1 ? " was" : "s were")} already gone before the cleanup ran");

            if (shellOk || leftBehind == 0)
                return;   // the shell grumbled but nothing was actually left behind

            // A red cross is for something going wrong, and files being open is not that -
            // even when it is ALL of them. A cleanup run shortly after another one finds
            // little left but the handful something still has open, so "nothing moved"
            // is the ordinary outcome there, not a fault. Reporting it as a failure is the
            // same over-alarming that made this line worth fixing in the first place.
            //
            // Access denied, an invalid path or a cancelled operation ARE faults, and they
            // still get the cross.
            if (!filesInUse)
            {
                logError("Recycle Bin", new IOException(error));
                return;
            }

            logNote(leftBehind == ordered.Count
                ? $"Nothing was removed: all {leftBehind} item{(leftBehind == 1 ? " was" : "s were")} in use"
                : $"{leftBehind} item{(leftBehind == 1 ? " was" : "s were")} in use and left alone");
        }

        /// <summary>
        /// What one batched trip to the Recycle Bin actually did.
        ///
        /// A class rather than a fistful of out parameters because the three ways a path
        /// can fail to move are genuinely different and the log must not conflate them:
        /// the shell refusing a path is a fault worth naming, a file vanishing underneath
        /// us is an ordinary race, and giving up on narrowing is the program's own limit.
        /// Calling all three "refused by Windows" is how a race got reported as an error.
        /// </summary>
        private sealed class SendOutcome
        {
            public bool Ok;
            public string Error = "";
            public bool FilesInUse;

            /// <summary>Individually proven: the shell was handed this path alone and said no.</summary>
            public List<string> Refused = new List<string>();

            /// <summary>
            /// Refused, and still on disk, and something else has it open.
            ///
            /// Separate from Refused because it is not a fault. The shell does not always
            /// say so: thumbcache_*.db and Chrome's GPUCache come back as DE_INVALIDFILES
            /// ("one of the paths was not valid") rather than a sharing violation, so the
            /// in-use detection never saw them and an ordinary locked file was reported as
            /// an error. Explorer holds the thumbnail caches open permanently, which made
            /// a red cross the GUARANTEED outcome of ticking Thumbnail cache.
            /// </summary>
            public List<string> InUse = new List<string>();
            /// <summary>Gone from disk between the pre-flight check and the shell call.</summary>
            public List<string> Vanished = new List<string>();
            /// <summary>Never tested individually - the narrowing budget ran out first.</summary>
            public List<string> NotTested = new List<string>();

            /// <summary>How many times SHFileOperation was called. The diagnostic that was missing.</summary>
            public int ShellCalls;
        }

        /// <summary>
        /// Sends the batch, and if the shell refuses it, works out which paths it will not
        /// take rather than losing the whole run to them.
        ///
        /// SHFileOperation reports one verdict for the whole operation. A single path it
        /// dislikes - DE_INVALIDFILES, a name it considers malformed, something that
        /// changed underneath it - can make it reject everything, so a run with sixteen
        /// thousand candidates moved nothing and showed a red cross.
        ///
        /// A sharing violation is NOT narrowed: files being open is the ordinary case in a
        /// temp cleanup, it is already reported honestly as "left alone", and splitting a
        /// large batch into single calls to rediscover that would be slow for no gain.
        ///
        /// ORDER MATTERS, and this is the whole lesson of the run that appeared to hang:
        ///
        ///   1. try the batch;
        ///   2. if it fails, ASK THE DISK, not the shell - re-check existence in process
        ///      and retry the survivors once;
        ///   3. only then narrow by halves, and only within a budget.
        ///
        /// Step 2 exists because the dominant cause of DE_INVALIDFILES here is a file that
        /// stopped existing, not a file the shell dislikes. The plan is built by walking
        /// the disk, and a running browser rewrites its cache continuously - a real run
        /// raced on 2,757 of about 7,400 paths, all of them Chrome and Edge cache.
        ///
        /// Narrowing alone cannot cope with that. Its cost is driven by how DENSELY the
        /// bad paths are spread, not how many there are: when most subdivisions contain
        /// one, the recursion reaches single paths across nearly the whole batch, which
        /// on that run meant of the order of fifteen thousand shell calls. The progress
        /// readout sat at zero for minutes and the run was indistinguishable from a hang.
        /// One existence check in memory replaces all of it.
        /// </summary>
        /// <summary>
        /// Is something else holding this file open?
        ///
        /// Opened for READ with FileShare.None: the read asks for nothing we are not about
        /// to do anyway, and denying all sharing is what makes the call fail when another
        /// process already has a handle. Nothing is written, and the handle is released
        /// immediately.
        ///
        /// Directories cannot be probed this way and are reported as refused, which is the
        /// conservative answer - it names the path rather than quietly excusing it.
        /// </summary>
        private static bool IsLocked(string path)
        {
            if (Directory.Exists(path)) return false;
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
        }

        private static SendOutcome SendIsolatingBadPaths(List<CleanedItem> items)
        {
            var outcome = new SendOutcome();
            if (items.Count == 0) { outcome.Ok = true; return outcome; }

            outcome.ShellCalls++;
            outcome.Ok = RecycleBin.Send(items.Select(i => i.Path), out string error,
                                         out bool filesInUse);
            outcome.Error = error;
            outcome.FilesInUse = filesInUse;
            if (outcome.Ok || filesInUse) return outcome;

            // Re-filter and retry, up to a few times.
            //
            // One pass is not enough. A browser does not rewrite its cache once and stop -
            // it keeps going for as long as the cleanup runs, so every pass finds a fresh
            // crop of paths that existed a moment ago and do not now. Each pass drops
            // those and tries the survivors again.
            //
            // The loop ends the moment a pass removes NOTHING, because that is the signal
            // that the remaining refusal is not a race at all: something in the batch is
            // genuinely unacceptable to the shell, and only then is it worth hunting for.
            var stillThere = items;
            for (int pass = 0; pass < 4; pass++)
            {
                var survivors = new List<CleanedItem>(stillThere.Count);
                foreach (var item in stillThere)
                {
                    bool exists = item.IsFolder ? Directory.Exists(item.Path) : File.Exists(item.Path);
                    if (exists) survivors.Add(item);
                    else outcome.Vanished.Add(item.Path);
                }

                bool removedSome = survivors.Count != stillThere.Count;
                stillThere = survivors;

                if (stillThere.Count == 0) return outcome;
                if (!removedSome) break;

                outcome.ShellCalls++;
                outcome.Ok = RecycleBin.Send(stillThere.Select(i => i.Path), out error, out filesInUse);
                outcome.Error = error;
                outcome.FilesInUse = filesInUse;
                if (outcome.Ok || filesInUse) return outcome;
            }

            // Something in here really is unacceptable to the shell.
            //
            // Send in CHUNKS rather than binary-narrowing the whole batch. Narrowing from
            // the top costs a call for every subdivision that contains a bad path, which
            // with bad paths spread widely is most of them - a real run spent its entire
            // budget and then abandoned 1,835 items untried, which is a worse failure than
            // being slow: it silently did less work than it reported.
            //
            // A chunk that succeeds costs ONE call for its whole contents. Only a chunk
            // that fails is narrowed, and the budget is spent inside failing chunks rather
            // than on the batch as a whole. Every item is attempted either way, which is
            // the property that matters.
            const int ChunkSize = 256;
            int budget = 2048;
            var remaining = stillThere.Select(i => i.Path).ToList();

            for (int start = 0; start < remaining.Count; start += ChunkSize)
            {
                var chunk = remaining.GetRange(start, Math.Min(ChunkSize, remaining.Count - start));

                outcome.ShellCalls++;
                if (RecycleBin.Send(chunk, out _, out bool chunkInUse) || chunkInUse) continue;

                Narrow(chunk, outcome, ref budget);
            }

            // Neither vanished nor in-use paths are failures. Both are the ordinary result
            // of cleaning folders that live applications are still using, which is every
            // cache folder on a running machine.
            outcome.Ok = outcome.Refused.Count == 0 && outcome.NotTested.Count == 0;
            return outcome;

            static void Narrow(List<string> paths, SendOutcome result, ref int budget)
            {
                if (paths.Count == 0) return;

                if (budget <= 0) { result.NotTested.AddRange(paths); return; }

                budget--;
                result.ShellCalls++;
                if (RecycleBin.Send(paths, out _, out bool inUse) || inUse) return;

                if (paths.Count == 1)
                {
                    // Ask WHY this single path was refused, rather than assuming a fault.
                    //
                    // Two ordinary explanations, and the shell distinguishes neither: the
                    // file stopped existing (a browser rewriting its cache), or something
                    // has it open. Both arrive as DE_INVALIDFILES, and calling either one
                    // "refused by Windows" is what put a red cross on a healthy cleanup.
                    if (!File.Exists(paths[0]) && !Directory.Exists(paths[0]))
                        result.Vanished.Add(paths[0]);
                    else if (IsLocked(paths[0]))
                        result.InUse.Add(paths[0]);
                    else
                        result.Refused.Add(paths[0]);
                    return;
                }

                int half = paths.Count / 2;
                Narrow(paths.GetRange(0, half), result, ref budget);
                Narrow(paths.GetRange(half, paths.Count - half), result, ref budget);
            }
        }

        /// <summary>
        /// Deletes Windows.old, permanently, after a second explicit confirmation.
        ///
        /// This is the ONLY thing in the application that destroys data outright. It
        /// cannot use the Recycle Bin: Windows.old is routinely 10-30 GB, far past the
        /// bin's quota, and Windows silently PERMANENTLY deletes anything over that
        /// quota - so "recycling" it would destroy the data anyway while claiming to be
        /// undoable. Saying so and meaning it is better than an undo that is not one.
        ///
        /// Guards, in order:
        ///   - the path is built from the system drive and compared EXACTLY. It never
        ///     comes from options, preferences or user input, so there is nothing to
        ///     point somewhere else.
        ///   - a reparse point is refused outright. A junction named Windows.old would
        ///     otherwise take a recursive delete straight into its target, which is the
        ///     exact way this engine destroyed data outside the cleaned path before.
        ///   - the size is measured and shown, and the user must choose "Yes, delete it"
        ///     against a Cancel that is the default answer.
        /// </summary>
        private static void DeleteWindowsOld(Action<string> logStep, Action<string, Exception> logError,
                                             Action<string> logNote)
        {
            string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string target = Path.Combine(systemDrive, "Windows.old");

            if (!Directory.Exists(target))
            {
                logNote("Old Windows installations: nothing to remove");
                return;
            }

            // Never traverse a link. See RecycleBin/Collect - this is the same refusal.
            var attrs = File.GetAttributes(target);
            if ((attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                logNote("Old Windows installations: skipped, Windows.old is a link rather than a real folder");
                return;
            }

            long bytes = 0;
            int files = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; files++; } catch { }
                }
            }
            catch { /* partial measurement is still worth showing */ }

            double gb = bytes / (1024.0 * 1024.0 * 1024.0);

            bool go = UI(() => Shell.CustomMessageBox.Confirm(
                $"Permanently delete {target}?\n\n" +
                $"{files:N0} files, {gb:F1} GB.\n\n" +
                "This does NOT go to the Recycle Bin and cannot be undone. Windows.old is " +
                "what \"Go back to your previous version of Windows\" restores from, so " +
                "removing it ends that option.",
                "Delete Windows.old permanently",
                "Yes, delete it",
                Shell.CustomMessageBox.Kind.Warning));

            if (!go)
            {
                logNote("Old Windows installations: cancelled, nothing was removed");
                return;
            }

            // Windows.old is owned by TrustedInstaller, so an administrator still gets
            // access-denied on much of it until ownership and rights are taken. This is
            // what every manual removal does; it is applied to this one fixed path only.
            RunHidden("takeown.exe", $"/f \"{target}\" /r /d y");
            RunHidden("icacls.exe", $"\"{target}\" /grant *S-1-5-32-544:F /t /c /q");

            try
            {
                Directory.Delete(target, recursive: true);
            }
            catch (Exception ex)
            {
                // Partial success is the normal outcome when something is still in use.
                if (Directory.Exists(target))
                {
                    logError("Old Windows", ex);
                    return;
                }
            }

            TotalBytesFreed += bytes;
            TotalFilesDeleted += files;
            logStep($"Old Windows installations ({gb:F1} GB)");
        }

        /// <summary>Runs a console tool to completion with no visible window.</summary>
        private static void RunHidden(string exe, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                p?.WaitForExit(120000);
            }
            catch { /* the delete below reports the real outcome */ }
        }

        /// <summary>
        /// Every browser cache directory that exists on this machine, and nothing else.
        ///
        /// The rule this list obeys: ONLY folders whose entire purpose is re-downloadable
        /// cache. Never Cookies, Login Data, History, Bookmarks, Web Data, Local Storage
        /// or IndexedDB - those sit in the same profile folder, and clearing them is how a
        /// "cleaner" logs people out of everything and gets uninstalled. A cache miss costs
        /// a re-download; a lost login costs trust.
        ///
        /// Chromium keeps one folder per profile ("Default", "Profile 1", ...), so the
        /// profile level is enumerated rather than assumed - cleaning only "Default" would
        /// quietly miss everything for anyone with a second profile.
        ///
        /// Nothing is deleted here. Each directory is handed to the same Collect used by
        /// every other target, so the 24-hour grace, the reparse-point refusal, the
        /// Recycle Bin routing and the undo manifest all apply unchanged. A browser that
        /// is open simply holds its files, and they get counted as in-use and left alone.
        /// </summary>
        private static IEnumerable<string> BrowserCacheRoots()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Chromium family: <root>\User Data\<profile>\{Cache,Code Cache,GPUCache}
            var chromiumRoots = new[]
            {
                Path.Combine(local,   @"Google\Chrome\User Data"),
                Path.Combine(local,   @"Microsoft\Edge\User Data"),
                Path.Combine(local,   @"BraveSoftware\Brave-Browser\User Data"),
                Path.Combine(local,   @"Vivaldi\User Data"),
                Path.Combine(local,   @"Chromium\User Data"),
                Path.Combine(roaming, @"Opera Software\Opera Stable"),
            };

            foreach (var userData in chromiumRoots)
            {
                if (!Directory.Exists(userData)) continue;

                // Opera Stable IS the profile folder; the others contain profile folders.
                var profiles = new List<string>();
                if (userData.EndsWith("Opera Stable", StringComparison.OrdinalIgnoreCase))
                {
                    profiles.Add(userData);
                }
                else
                {
                    string[] dirs;
                    try { dirs = Directory.GetDirectories(userData); } catch { continue; }
                    foreach (var d in dirs)
                    {
                        string name = Path.GetFileName(d);
                        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                            profiles.Add(d);
                    }
                }

                foreach (var profile in profiles)
                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                    {
                        string path = Path.Combine(profile, sub);
                        if (Directory.Exists(path)) yield return path;
                    }
            }

            // Firefox keeps its cache under a separate roaming-free root, one randomly
            // named folder per profile, with the content in "cache2".
            string ffRoot = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ffRoot))
            {
                string[] ffProfiles;
                try { ffProfiles = Directory.GetDirectories(ffRoot); } catch { ffProfiles = Array.Empty<string>(); }
                foreach (var p in ffProfiles)
                {
                    string cache2 = Path.Combine(p, "cache2");
                    if (Directory.Exists(cache2)) yield return cache2;
                }
            }
        }

        // UpdateOverlaysAfterRamBoost moved to OverlayWindow.RefreshAllAfterRamBoost.
        // It was private here and called only by the two manual boost paths, which is
        // why an automatic boost never reached the overlay's "Last Boost" line.
    }
}
