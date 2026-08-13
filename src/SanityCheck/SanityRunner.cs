// File: SanityCheck/SanityRunner.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// Runs the registered checks and applies the noise budget.
    ///
    /// Everything restrictive in here serves one constraint: a detector that gets ignored
    /// is worse than no detector, because it teaches the user to dismiss the one alert
    /// that mattered. Where a trade-off is available, this bends toward silence.
    ///
    /// Never runs on every launch. That is how a feature becomes wallpaper.
    /// </summary>
    public static class SanityRunner
    {
        /// <summary>
        /// At most this many findings are put in front of the user. The rest are in the
        /// report, which is a different thing from being shown unprompted.
        /// </summary>
        public const int MaxSurfacedFindings = 3;

        public static SanityReport Run() => Run(CheckRegistry.All);

        public static SanityReport Run(IEnumerable<IAnomalyCheck> checks)
        {
            var report = new SanityReport();
            var context = new ProbeContext();
            var state = SanityStateStore.Load();
            var now = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            foreach (var check in checks)
            {
                // Self-retirement. A check written against a transient upstream bug is
                // guaranteed to become wrong; the only question is whether it stops on its
                // own or waits for somebody to notice. Silent, and not reinstatable - the
                // author retired it, not this machine.
                if (check.ReviewBy.HasValue && check.ReviewBy.Value <= now)
                {
                    report.Disabled.Add(new DisabledCheck
                    {
                        Id = check.Id,
                        Title = check.Title,
                        Reason = $"Retired by its author on {check.ReviewBy.Value:d}.",
                        Reinstatable = false
                    });
                    continue;
                }

                var saved = SanityStateStore.For(state, check.Id);

                if (saved.Quarantined)
                {
                    report.Disabled.Add(new DisabledCheck
                    {
                        Id = check.Id,
                        Title = check.Title,
                        Reason = string.IsNullOrWhiteSpace(saved.LastInconclusiveReason)
                            ? "Stopped after three runs it could not answer."
                            : "Stopped after three runs it could not answer. Last reason: " +
                              saved.LastInconclusiveReason,
                        Reinstatable = true
                    });
                    continue;
                }

                // Switched off - either from the list, or by having been dismissed twice,
                // which sets the same flag. Listed either way rather than silently absent:
                // a check that is not running must never be indistinguishable from one
                // that ran and found nothing. That confusion is the founding complaint of
                // this entire feature.
                if (!SanityStateStore.IsEnabled(state, check))
                {
                    report.Disabled.Add(new DisabledCheck
                    {
                        Id = check.Id,
                        Title = check.Title,
                        Reason = SanityStateStore.SwitchedOffByDismissals(state, check.Id)
                            ? "You dismissed this twice, so it was switched off."
                            : "You switched this off.",
                        Reinstatable = true
                    });
                    continue;
                }

                AnomalyResult result;
                try
                {
                    result = check.Evaluate(context) ??
                             AnomalyResult.Inconclusive("The check returned no answer.");
                }
                catch (Exception ex)
                {
                    // A throwing check is Inconclusive, never a finding and never a pass.
                    // Three of these and it quarantines itself like any other check that
                    // has stopped being able to answer.
                    LogHelper.Log($"Sanity Check {check.Id} threw: {ex}");
                    result = AnomalyResult.Inconclusive("The check could not complete: " + ex.Message);
                }

                SanityStateStore.Record(state, check.Id, result);

                report.Outcomes.Add(new CheckOutcome
                {
                    Id = check.Id,
                    Title = check.Title,
                    Confidence = check.Confidence,
                    Result = result
                });
            }

            // Rank and apply the budget. Certain first; within a confidence level the
            // registry's own order stands, which is where importance is recorded.
            var ranked = report.Outcomes
                .Where(o => o.Result.Verdict == Verdict.Finding)
                .OrderBy(o => (int)o.Confidence)
                .ToList();

            foreach (var outcome in ranked.Take(MaxSurfacedFindings))
                outcome.Surfaced = true;

            // Recorded so the main window and the overlay can say when this last ran and
            // what it found WITHOUT running it again. A label that has to start a scan to
            // draw itself is how a feature that must never run on every launch ends up
            // running on every launch.
            state.LastRunUtc = report.RanAtUtc.ToString("o");
            state.LastRanCount = report.RanCount;
            state.LastFindingCount = report.FindingCount;

            SanityStateStore.Save(state);

            stopwatch.Stop();
            LogHelper.Log($"Sanity Check: {report.Headline} " +
                          $"({report.Disabled.Count} not run, {stopwatch.ElapsedMilliseconds} ms)");
            return report;
        }

        /// <summary>
        /// What the last run found, without running anything. Null when it has never run -
        /// which callers must show as "not run yet" rather than as "nothing found". Those
        /// two look identical on screen and mean opposite things, and confusing them is
        /// this whole feature's founding complaint.
        /// </summary>
        public static SanityState LastRun()
        {
            var state = SanityStateStore.Load();
            return string.IsNullOrEmpty(state.LastRunUtc) ? null : state;
        }

        /// <summary>Records a dismissal. Two on the same check mutes it on this machine.</summary>
        public static void Dismiss(string checkId)
        {
            var state = SanityStateStore.Load();
            SanityStateStore.Dismiss(state, checkId);
            SanityStateStore.Save(state);
        }

        /// <summary>
        /// Puts a check back: clears a self-quarantine AND switches it on. This is the
        /// single "turn this back on" action offered next to a not-running check, so it
        /// has to cover both reasons a check can be in that list - the user cannot be
        /// expected to know which one applied.
        /// </summary>
        public static void Reinstate(string checkId)
        {
            var state = SanityStateStore.Load();
            SanityStateStore.Reinstate(state, checkId);
            SanityStateStore.SetEnabled(state, checkId, true);
            SanityStateStore.Save(state);
        }

        /// <summary>Which checks are on, for the list, each with the note explaining a
        /// switched-off one. Registry order, which is where importance is recorded.</summary>
        public static List<(IAnomalyCheck Check, bool Enabled, string OffNote)> Selection()
        {
            var state = SanityStateStore.Load();
            return CheckRegistry.All
                .Select(c => (Check: c,
                              Enabled: SanityStateStore.IsEnabled(state, c),
                              OffNote: SanityStateStore.OffNote(state, c.Id)))
                .ToList();
        }

        /// <summary>Forgets the user's choice for every check, so the defaults apply again.</summary>
        public static void ResetSelection()
        {
            var state = SanityStateStore.Load();
            foreach (var check in CheckRegistry.All)
                SanityStateStore.ClearChoice(state, check.Id);
            SanityStateStore.Save(state);
        }

        /// <summary>Ticking IS the save - no OK gate anywhere in this application.</summary>
        public static void SetEnabled(string checkId, bool enabled)
        {
            var state = SanityStateStore.Load();
            SanityStateStore.SetEnabled(state, checkId, enabled);
            SanityStateStore.Save(state);
        }
    }
}
