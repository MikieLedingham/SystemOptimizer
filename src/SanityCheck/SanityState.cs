// File: SanityCheck/SanityState.cs
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SystemOptimizer.Core.Logging;
using SystemOptimizer.Core.Settings;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// What this machine has learned about its own checks: which ones have stopped being
    /// able to answer, and which findings the user has already said they do not care about.
    ///
    /// This is the half of the anti-rot design that needs to survive a restart. A check
    /// that quarantines itself only for the current session has not quarantined itself.
    /// </summary>
    public sealed class CheckState
    {
        /// <summary>Reset by any conclusive verdict. Three in a row and the check stops.</summary>
        public int ConsecutiveInconclusive { get; set; }

        /// <summary>The reason for the most recent Inconclusive, so the disabled list can say why.</summary>
        public string LastInconclusiveReason { get; set; } = "";

        /// <summary>Set once, when the third consecutive Inconclusive arrives.</summary>
        public bool Quarantined { get; set; }

        /// <summary>Times the user has dismissed this finding. Two and it switches itself off.</summary>
        public int Dismissals { get; set; }

        /// <summary>
        /// Whether the user wants this check to run. NULL means they have never said, so
        /// the check's own DefaultEnabled applies - which is why this is nullable rather
        /// than a bool defaulting to false. A plain bool would read as "off" for every
        /// check on every machine that has never opened the list.
        ///
        /// THIS IS ALSO THE MUTE. Dismissing a finding twice sets it false, rather than
        /// there being a separate muted flag - see SanityStateStore.Dismiss for why that
        /// matters more than it looks.
        /// </summary>
        public bool? Enabled { get; set; }

        public string LastRunUtc { get; set; } = "";

        /// <summary>
        /// When the user switched this check off, by either route.
        ///
        /// Recorded so the list can say so, with the date. A machine is not the same
        /// machine two years later - somebody who silenced "your 2.5 Gb adapter is running
        /// at 1 Gb" because their network was 1 Gb needs to be told they did that, on the
        /// day they finally upgrade the switch. Without the note, the check is simply
        /// absent and there is nothing to prompt them.
        /// </summary>
        public string SwitchedOffUtc { get; set; } = "";
    }

    /// <summary>
    /// The whole file. A wrapper rather than a bare dictionary so the last run's outcome
    /// has somewhere to live: the overlay and the main window both need to say when Sanity
    /// Check last ran and what it found, without running it again to find out. Running a
    /// scan to draw a label is how a feature ends up running on every launch, which the
    /// design forbids for good reason.
    /// </summary>
    public sealed class SanityState
    {
        public Dictionary<string, CheckState> Checks { get; set; } =
            new(StringComparer.Ordinal);

        public string LastRunUtc { get; set; } = "";
        public int LastRanCount { get; set; }
        public int LastFindingCount { get; set; }

        public DateTime? LastRunLocal =>
            DateTime.TryParse(LastRunUtc, null,
                              System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToLocalTime()
                : null;
    }

    /// <summary>
    /// Load, mutate, save. Small enough to rewrite whole every time, which avoids the
    /// class of bug where a partial write leaves a check permanently muted.
    /// </summary>
    public static class SanityStateStore
    {
        private const int QuarantineAfterInconclusive = 3;
        private const int MuteAfterDismissals = 2;

        public static SanityState Load()
        {
            try
            {
                if (!File.Exists(AppPaths.SanityStateFile)) return new SanityState();

                var text = File.ReadAllText(AppPaths.SanityStateFile);
                var loaded = JsonConvert.DeserializeObject<SanityState>(text);
                if (loaded == null) return new SanityState();
                loaded.Checks ??= new Dictionary<string, CheckState>(StringComparer.Ordinal);
                return loaded;
            }
            catch (Exception ex)
            {
                // Losing this file costs a quarantine streak and some dismissals, not data.
                // Starting clean is strictly better than refusing to run.
                LogHelper.Log("Sanity Check state could not be read, starting fresh: " + ex.Message);
                return new SanityState();
            }
        }

        public static void Save(SanityState state)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(AppPaths.SanityStateFile,
                                  JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogHelper.Log("Sanity Check state could not be saved: " + ex.Message);
            }
        }

        public static CheckState For(SanityState state, string id)
        {
            if (!state.Checks.TryGetValue(id, out var s)) state.Checks[id] = s = new CheckState();
            return s;
        }

        /// <summary>
        /// Folds one result into the check's history and reports whether that result just
        /// caused the check to quarantine itself.
        /// </summary>
        public static bool Record(SanityState state, string id, AnomalyResult result)
        {
            var s = For(state, id);
            s.LastRunUtc = DateTime.UtcNow.ToString("o");

            if (result.Verdict == Verdict.Inconclusive)
            {
                s.ConsecutiveInconclusive++;
                s.LastInconclusiveReason = result.InconclusiveReason;

                if (!s.Quarantined && s.ConsecutiveInconclusive >= QuarantineAfterInconclusive)
                {
                    s.Quarantined = true;
                    // Logged ONCE, here, rather than every run afterwards. A check that
                    // complains forever about being unable to run is the noise this whole
                    // design is built to avoid.
                    LogHelper.Log($"Sanity Check: {id} has quarantined itself after " +
                                  $"{QuarantineAfterInconclusive} runs it could not answer. " +
                                  $"Last reason: {result.InconclusiveReason}");
                    return true;
                }
                return false;
            }

            // Any conclusive answer clears the streak - including NotApplicable, which is a
            // real answer about the machine.
            s.ConsecutiveInconclusive = 0;
            s.LastInconclusiveReason = "";
            return false;
        }

        /// <summary>
        /// The user dismissed this finding. Twice and the check switches off.
        ///
        /// IT SETS THE SAME FLAG THE CHECKBOX SETS, and that is the whole point. A
        /// separate "muted" flag alongside a user-facing tick would allow a check to be
        /// ticked in the list and silently never run - a control that displays one thing
        /// and does another. That is the most repeated bug on this project: the Remember
        /// gates where the visible control controlled nothing, and the settings stored
        /// twice that drifted apart on a real machine. One state, two doors to it.
        /// </summary>
        public static void Dismiss(SanityState state, string id)
        {
            var s = For(state, id);
            s.Dismissals++;

            if (s.Dismissals >= MuteAfterDismissals && s.Enabled != false)
            {
                s.Enabled = false;
                s.SwitchedOffUtc = DateTime.UtcNow.ToString("o");
                LogHelper.Log($"Sanity Check: {id} switched off on this machine after " +
                              $"{MuteAfterDismissals} dismissals.");
            }
        }

        /// <summary>Whether the check runs. Falls back to the check's own default.</summary>
        public static bool IsEnabled(SanityState state, IAnomalyCheck check)
            => state.Checks.TryGetValue(check.Id, out var s) && s.Enabled.HasValue
                ? s.Enabled.Value
                : check.DefaultEnabled;

        /// <summary>
        /// Was it switched off by the user's dismissals rather than by the list? Only
        /// changes the wording, but the wording is the difference between "you turned this
        /// off" and "you have no idea why this stopped".
        /// </summary>
        public static bool SwitchedOffByDismissals(SanityState state, string id)
            => state.Checks.TryGetValue(id, out var s) &&
               s.Enabled == false && s.Dismissals >= MuteAfterDismissals;

        /// <summary>
        /// Turns a check on or off.
        ///
        /// Switching one back ON also clears the dismissal count. Without that, a check
        /// disabled by two dismissals would come back one dismissal from disappearing
        /// again - the tick would appear to work and then not, which is worse than it
        /// having refused.
        /// </summary>
        public static void SetEnabled(SanityState state, string id, bool enabled)
        {
            var s = For(state, id);
            s.Enabled = enabled;

            if (!enabled)
            {
                if (string.IsNullOrEmpty(s.SwitchedOffUtc))
                    s.SwitchedOffUtc = DateTime.UtcNow.ToString("o");
                return;
            }

            // Switching a check back on starts it FRESH - as though it had never run here.
            // The point is that the machine has probably changed: somebody turning the
            // link-speed check back on has most likely just replaced the switch it was
            // complaining about. Carrying the old dismissals, the old inconclusive streak
            // or the old run date forward would mean judging new hardware on the strength
            // of the old hardware's history.
            s.Dismissals = 0;
            s.SwitchedOffUtc = "";
            s.LastRunUtc = "";
            s.ConsecutiveInconclusive = 0;
            s.LastInconclusiveReason = "";
            s.Quarantined = false;
        }

        /// <summary>
        /// Forgets that the user ever chose, so the check's own default applies again.
        ///
        /// This is what "reset to defaults" needs, and writing an explicit false would NOT
        /// do: a check that ships switched off would then carry a note saying the user
        /// turned it off, on a day they did the opposite.
        /// </summary>
        public static void ClearChoice(SanityState state, string id)
        {
            var s = For(state, id);
            s.Enabled = null;
            s.Dismissals = 0;
            s.SwitchedOffUtc = "";
            s.LastRunUtc = "";
            s.ConsecutiveInconclusive = 0;
            s.LastInconclusiveReason = "";
            s.Quarantined = false;
        }

        /// <summary>
        /// The line shown beside a switched-off check, or null when there is nothing to
        /// say. Never guesses a date it does not have.
        /// </summary>
        public static string OffNote(SanityState state, string id)
        {
            if (!state.Checks.TryGetValue(id, out var s) || s.Enabled != false) return null;

            string when = DateTime.TryParse(s.SwitchedOffUtc, null,
                              System.Globalization.DateTimeStyles.RoundtripKind, out var off)
                ? $" on {off.ToLocalTime():d}"
                : "";

            string how = s.Dismissals >= MuteAfterDismissals
                ? $"You switched this off{when}, by dismissing it twice."
                : $"You switched this off{when}.";

            return how + " Turn it back on if this PC has changed since - it starts fresh, " +
                   "as though it had never been checked here.";
        }

        /// <summary>
        /// Undoes a self-quarantine. Deliberately sticky, so there has to be a way back
        /// that is not "delete a file we never told you about" - a feature that switches
        /// itself off with no visible way back is indistinguishable from a broken one,
        /// which is the accusation this product exists to answer.
        ///
        /// Does NOT touch Enabled: quarantine is the program's decision, not the user's,
        /// and clearing it must not tick a box they deliberately unticked.
        /// </summary>
        public static void Reinstate(SanityState state, string id)
        {
            var s = For(state, id);
            s.Quarantined = false;
            s.ConsecutiveInconclusive = 0;
        }
    }
}
