// File: SanityCheck/AnomalyResult.cs
using System;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// What one check concluded on one run.
    ///
    /// Built only through the factory methods below, because the rules that keep this
    /// feature honest are rules about which fields must be present with which verdict,
    /// and a public constructor cannot enforce them:
    ///
    ///   Finding       requires BOTH observed facts. "Your network is slow" is worthless;
    ///                 "this adapter supports 1 Gb, the link negotiated 100 Mb" is the
    ///                 entire product. A finding that cannot show both sides is not a
    ///                 relationship check, it is a state check wearing one's clothes.
    ///   Inconclusive  requires a reason. Without one it decays into a silent Pass, which
    ///                 is the failure this whole design exists to prevent.
    ///
    /// There is deliberately no Remedy field. The ordered steps live in CheckDoc, which
    /// generates the guide, and the finding deep-links to them. A per-result copy would be
    /// a second source for the same fact.
    /// </summary>
    public sealed class AnomalyResult
    {
        public Verdict Verdict { get; }

        /// <summary>Side A, as observed. Empty unless the verdict is Pass or Finding.</summary>
        public string Expected { get; }

        /// <summary>Side B, as observed. Empty unless the verdict is Pass or Finding.</summary>
        public string Actual { get; }

        /// <summary>Plain language, no jargon. Why these two disagreeing matters.</summary>
        public string Why { get; }

        /// <summary>Required when and only when the verdict is Inconclusive.</summary>
        public string InconclusiveReason { get; }

        private AnomalyResult(Verdict verdict, string expected, string actual,
                              string why, string inconclusiveReason)
        {
            Verdict = verdict;
            Expected = expected ?? "";
            Actual = actual ?? "";
            Why = why ?? "";
            InconclusiveReason = inconclusiveReason ?? "";
        }

        /// <summary>Both sides were read and they agree. Both are still recorded: the
        /// report shows what was compared, so a Pass is evidence rather than an assertion.</summary>
        public static AnomalyResult Pass(string expected, string actual)
        {
            Require(expected, nameof(expected), "Pass");
            Require(actual, nameof(actual), "Pass");
            return new AnomalyResult(Verdict.Pass, expected, actual, "", "");
        }

        /// <summary>Both sides were read and they disagree.</summary>
        public static AnomalyResult Finding(string expected, string actual, string why)
        {
            Require(expected, nameof(expected), "Finding");
            Require(actual, nameof(actual), "Finding");
            Require(why, nameof(why), "Finding");
            return new AnomalyResult(Verdict.Finding, expected, actual, why, "");
        }

        /// <summary>
        /// One side could not be read. The reason is mandatory and is shown to the user in
        /// the "checks that could not run" list - never as an alert, and never as a pass.
        /// </summary>
        public static AnomalyResult Inconclusive(string reason)
        {
            Require(reason, nameof(reason), "Inconclusive");
            return new AnomalyResult(Verdict.Inconclusive, "", "", "", reason);
        }

        /// <summary>
        /// The assertion does not apply to this machine. The reason is required for the
        /// same purpose as an Inconclusive one - "not applicable" with no explanation is
        /// indistinguishable from a check that has quietly stopped working.
        /// </summary>
        public static AnomalyResult NotApplicable(string reason)
        {
            Require(reason, nameof(reason), "NotApplicable");
            return new AnomalyResult(Verdict.NotApplicable, "", "", "", reason);
        }

        private static void Require(string value, string field, string verdict)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    $"A {verdict} result must supply {field}. This is enforced because " +
                    "a result missing it cannot be shown honestly to the user.", field);
        }
    }
}
