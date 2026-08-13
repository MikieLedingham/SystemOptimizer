// File: SanityCheck/SanityReport.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>One check's outcome on one run.</summary>
    public sealed class CheckOutcome
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public Confidence Confidence { get; init; }
        public AnomalyResult Result { get; init; }

        /// <summary>
        /// True for findings inside the run's budget. The rest are still in the report -
        /// they are just not put in front of the user unasked.
        /// </summary>
        public bool Surfaced { get; set; }
    }

    /// <summary>A check that did not run, and why. Shown as a list, never as an alert.</summary>
    public sealed class DisabledCheck
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Reason { get; init; } = "";

        /// <summary>Whether the user can turn this one back on (quarantine and mute) or
        /// not (a check past its own review date, which is retired for everyone).</summary>
        public bool Reinstatable { get; init; }
    }

    public sealed class SanityReport
    {
        public DateTime RanAtUtc { get; init; } = DateTime.UtcNow;
        public List<CheckOutcome> Outcomes { get; init; } = new();
        public List<DisabledCheck> Disabled { get; init; } = new();

        public IEnumerable<CheckOutcome> Findings =>
            Outcomes.Where(o => o.Result.Verdict == Verdict.Finding);

        public IEnumerable<CheckOutcome> SurfacedFindings => Findings.Where(o => o.Surfaced);

        public int RanCount => Outcomes.Count;
        public int FindingCount => Findings.Count();
        public int InconclusiveCount => Outcomes.Count(o => o.Result.Verdict == Verdict.Inconclusive);

        /// <summary>
        /// True only if a Certain finding is being surfaced. Nothing less may interrupt:
        /// Probable and Heuristic wait for the user to open the panel.
        /// </summary>
        public bool MayNotifyUnprompted =>
            SurfacedFindings.Any(o => o.Confidence == Confidence.Certain);

        /// <summary>
        /// The one line the user reads first.
        ///
        /// Zero findings MUST say so out loud. If silence and breakage look identical, the
        /// feature has already failed - that is the exact lesson this whole thing was built
        /// from, where every indicator was green while four things were badly wrong.
        /// </summary>
        public string Headline
        {
            get
            {
                if (RanCount == 0)
                    return "No checks could run.";

                string ran = RanCount == 1 ? "1 check ran" : $"{RanCount} checks ran";

                if (FindingCount == 0)
                    return InconclusiveCount == 0
                        ? $"{ran}, nothing odd found."
                        : $"{ran}, nothing odd found. {InconclusiveCount} could not be answered.";

                return FindingCount == 1
                    ? $"{ran}. 1 thing looks inconsistent."
                    : $"{ran}. {FindingCount} things look inconsistent.";
            }
        }
    }
}
