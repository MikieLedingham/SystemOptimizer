// File: SanityCheck/Confidence.cs

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// How much a check's finding can be trusted, which decides whether it may interrupt.
    ///
    /// This is not decoration. Only <see cref="Certain"/> may notify unprompted; anything
    /// less waits for the user to open the panel. The noise budget is the single
    /// constraint the whole feature is built around - a detector that gets ignored is
    /// worse than no detector, because it trains the user to dismiss the one alert that
    /// mattered.
    /// </summary>
    public enum Confidence
    {
        /// <summary>
        /// Deterministic. Two facts were read directly from the system and they contradict
        /// each other. There is no inference step to be wrong about.
        /// </summary>
        Certain,

        /// <summary>Very likely wrong, but the reading involves a judgement call.</summary>
        Probable,

        /// <summary>A rule of thumb. May never notify; report only.</summary>
        Heuristic
    }
}
