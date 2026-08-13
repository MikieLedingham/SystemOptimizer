// File: Tools/NoBoost/NoBoostTool.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Tools.NoBoost
{
    /// <summary>
    /// Holds off automatic RAM boosting while one of the user's chosen applications is
    /// running. The first Tool, and the reason the Tools idea exists.
    ///
    /// UNTIL NOW THIS DID NOTHING AT ALL. Toggling "No-boost mode" set a bool, raised an
    /// event that rebuilt the tray menu, and showed a notification reading "RAM cleanup
    /// pauses whenever your chosen apps are running" - while nothing anywhere consulted
    /// it. AutoRamMonitorHelper, RamCleanupHelper and CleanupHelper contained no reference
    /// to gaming mode between them; GamingModeManager.StartGameDetection and
    /// LoadGamesList were never called by anything; and LoadGamesList would have failed
    /// anyway, reading a list of strings out of a file holding a list of objects. The
    /// no-boost list was a fifteen-kilobyte file that nothing read for its stated purpose.
    ///
    /// It works now, and the wiring is one question asked by Core - see ToolRegistry.
    /// </summary>
    public sealed class NoBoostTool : ITool
    {
        public string Name => "No-boost";

        public bool IsActive => NoBoostMode.Enabled;

        public string HoldOffReason() => IsActive ? BlockedSummary() : null;

        /// <summary>
        /// What to tell the user, in one short phrase, or null if nothing is holding off.
        ///
        ///     one running    "Claude is running"
        ///     several        "2 programs are running"
        ///
        /// The right call. The name is the useful answer when there is a
        /// single culprit - that is the whole question being asked. Past one it stops
        /// being useful and starts being a list: this phrase appears on the main window,
        /// in the overlay, in RAM options and in a notification, and somebody with a dozen
        /// applications ticked would find every one of those places overwhelmed by names
        /// they already know they chose.
        /// </summary>
        public static string BlockedSummary()
        {
            var names = RunningBlockers();
            if (names.Count == 0) return null;

            // NAMES THE LIST, every time. "Held off - Claude is running" assumes the
            // reader remembers choosing Claude. Mikie's case: somebody who set this up six
            // months ago, has not opened the application since, and is now being told that
            // something is paused for a reason that means nothing to them. Saying which
            // list is doing it turns a mystery into a place to go and look.
            return names.Count == 1
                ? $"Paused - 'No-boost' list has {names[0]} running"
                : $"Paused - 'No-boost' list has {names.Count} running programs";
        }

        /// <summary>
        /// The name of ONE ticked application that is running, or null. Kept for callers
        /// that genuinely want a single name rather than a summary.
        /// </summary>
        public static string RunningBlocker() => RunningBlockers().FirstOrDefault();

        /// <summary>
        /// EVERY ticked application running right now.
        ///
        /// This used to stop at the first match and return it, which is why adding
        /// CrystalDiskInfo alongside Claude changed nothing anywhere: both were running,
        /// both were ticked, and every surface still named Claude. It looked like a
        /// refresh problem and was not - the count was simply never taken.
        ///
        /// Public and independent of whether no-boost mode is switched on, because the UI
        /// needs to answer "if I turn this on, will it block anything?" at the moment the
        /// user turns it on - before IsActive would say yes.
        /// </summary>
        public static List<string> RunningBlockers()
        {
            var found = new List<string>();

            var watched = NoBoostList.Selected();
            if (watched.Count == 0) return found;   // nothing chosen is not a reason to hold off

            HashSet<string> running = RunningProcessNames();
            if (running.Count == 0) return found;

            foreach (var entry in watched)
            {
                foreach (var key in NoBoostList.ProcessKeys(entry))
                {
                    if (!running.Contains(key)) continue;
                    // One entry, one mention, however many process names it matches.
                    if (!found.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                        found.Add(entry.Name);
                    break;
                }
            }
            return found;
        }

        /// <summary>
        /// Exact, case-insensitive process names.
        ///
        /// Deliberately not a substring match. The list is seeded by a scanner that
        /// harvests names like "Adobe" and "ea", and "contains" would let those hold off
        /// every boost forever the moment anything with those letters in its name was
        /// running - a feature that silently stops working is bad, but one that silently
        /// stops the app working is worse.
        /// </summary>
        private static HashSet<string> RunningProcessNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(p.ProcessName)) names.Add(p.ProcessName);
                    }
                    catch { /* the process ended while we were looking at it */ }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log("No-boost could not list running processes: " + ex.Message);
            }
            return names;
        }
    }
}
