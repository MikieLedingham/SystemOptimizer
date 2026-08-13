// File: SanityCheck/CheckDoc.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// A check's own documentation, carried by the check rather than written beside it.
    ///
    /// The user guide is GENERATED from these at build time (see GuideWriter), so the
    /// documentation cannot drift from the code the way separately-maintained prose
    /// always does. A finding links straight to its entry by Id.
    ///
    /// WhenToIgnore is required and must be non-empty, and the build fails if it is not -
    /// see <see cref="Validate"/>. That is deliberate, and it is the most useful rule in
    /// this file. Every check in the Tier 1 list has users for whom the "problem" is a
    /// deliberate choice: someone on a 100 Mb switch on purpose, someone whose pagefile is
    /// off the NVMe to spare it, someone running integrated graphics to keep a laptop
    /// quiet. An author who cannot name those people has not finished thinking about the
    /// check, and shipping it would produce either a pointless change or - worse - a
    /// dismissal that teaches the user to dismiss the next one too.
    /// </summary>
    public sealed class CheckDoc
    {
        /// <summary>One line, plain language, no jargon. What this check looks at.</summary>
        public string Summary { get; init; } = "";

        /// <summary>The cost of leaving it alone. Why a reader should care at all.</summary>
        public string WhyItMatters { get; init; } = "";

        /// <summary>
        /// REQUIRED, non-empty. The cases where this finding is correct behaviour and the
        /// right response is to do nothing.
        /// </summary>
        public string[] WhenToIgnore { get; init; } = Array.Empty<string>();

        /// <summary>
        /// How to confirm the finding independently, without taking our word for it -
        /// a Settings page, a command, a label on the hardware.
        /// </summary>
        public string[] HowToConfirm { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Ordered steps. This is the ONLY place remedy text lives: AnomalyResult
        /// deliberately does not carry a copy. Two descriptions of the same fix would
        /// disagree eventually, which is a fault this project has already found twice in
        /// its own settings (LastRamBoostMessage and ShowAdminWarning both existed twice,
        /// and one pair had measurably drifted).
        /// </summary>
        public string[] Remedy { get; init; } = Array.Empty<string>();

        /// <summary>How to know the fix worked. A remedy with no test is a suggestion.</summary>
        public string HowToVerify { get; init; } = "";

        /// <summary>
        /// Every reason this documentation is not fit to ship, or an empty list.
        /// Returned rather than thrown so the build can report ALL of them at once
        /// instead of one per rebuild.
        /// </summary>
        public IReadOnlyList<string> Validate(string checkId)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(Summary))
                problems.Add($"{checkId}: Summary is empty.");
            if (string.IsNullOrWhiteSpace(WhyItMatters))
                problems.Add($"{checkId}: WhyItMatters is empty - a finding without a cost is an accusation.");

            // The rule this whole class exists for.
            if (WhenToIgnore == null || WhenToIgnore.Length == 0 ||
                WhenToIgnore.All(string.IsNullOrWhiteSpace))
                problems.Add($"{checkId}: WhenToIgnore is empty. Name at least one user for whom " +
                             "this finding is a deliberate choice, or do not ship the check.");

            if (HowToConfirm == null || HowToConfirm.Length == 0)
                problems.Add($"{checkId}: HowToConfirm is empty - the reader must be able to check us.");
            if (Remedy == null || Remedy.Length == 0)
                problems.Add($"{checkId}: Remedy is empty.");
            if (string.IsNullOrWhiteSpace(HowToVerify))
                problems.Add($"{checkId}: HowToVerify is empty - a remedy with no test is a suggestion.");

            return problems;
        }
    }
}
