// File: SanityCheck/Verdict.cs

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// What a check concluded.
    ///
    /// <see cref="Inconclusive"/> is the whole anti-rot mechanism and the reason this is
    /// four values rather than two. Assertions decay because an input that can no longer
    /// be read gets quietly treated as passing - the check goes silent and useless - or as
    /// failing, so it cries wolf and gets ignored. A third state, with a REQUIRED reason,
    /// makes that decay visible instead of silent.
    ///
    /// Which matters more here than in most products: a detector whose own checks have
    /// rotted while still reporting green would be an exact instance of the failure class
    /// it exists to find. The detector must not become the thing it detects.
    /// </summary>
    public enum Verdict
    {
        /// <summary>Both sides were observed and they agree.</summary>
        Pass,

        /// <summary>Both sides were observed and they disagree.</summary>
        Finding,

        /// <summary>
        /// At least one side could not be observed, so no honest conclusion is available.
        /// Never a substitute for Pass and never for Finding.
        /// </summary>
        Inconclusive,

        /// <summary>
        /// The assertion does not apply to this machine at all - no discrete GPU to be
        /// idle, no SSD to have TRIM. Distinct from Pass: nothing was asserted.
        /// </summary>
        NotApplicable
    }
}
