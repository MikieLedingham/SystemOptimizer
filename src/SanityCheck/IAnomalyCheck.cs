// File: SanityCheck/IAnomalyCheck.cs
using System;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// One relationship assertion.
    ///
    /// The distinction that makes this feature worth having: a check observes TWO
    /// independent facts and asserts they agree. It does not ask one question of one
    /// thing. "Is the link up?" cannot notice a gigabit adapter running at 100 Mb,
    /// because everything is working - just stupidly, and every status indicator is
    /// green. That is the class of problem nothing else reports.
    ///
    /// A check that finds itself unable to read one of its two facts must return
    /// Inconclusive with a reason. Returning Pass in that situation is how a detector
    /// rots into decoration.
    /// </summary>
    public interface IAnomalyCheck
    {
        /// <summary>
        /// Stable, dotted, never reused: "NET.LINK_SPEED". It is also the guide anchor and
        /// the key this check's quarantine and dismissal state is stored under, so changing
        /// one silently resets a user's history and orphans their bookmark.
        /// </summary>
        string Id { get; }

        /// <summary>Short, human, shown as the row heading.</summary>
        string Title { get; }

        Confidence Confidence { get; }

        /// <summary>
        /// The date this check retires itself, or null for one with no expiry.
        ///
        /// Mandatory in spirit for any check written against a transient upstream bug: such
        /// a check is guaranteed to become wrong, and the only question is whether it stops
        /// on its own or waits to be noticed. Past this date the runner drops it silently.
        /// </summary>
        DateTime? ReviewBy { get; }

        /// <summary>
        /// Whether this check runs on a machine that has never been told otherwise.
        ///
        /// The user can switch any check on or off, and THE DEFAULTS ARE THE DESIGN of
        /// that feature. All on, and nothing has changed - the picker is a chore nobody
        /// visits. All off, and the product does nothing out of the box, which is the
        /// placeholder-button fault this project keeps finding. So: checks that apply to
        /// almost any PC start on, specialised ones start off.
        ///
        /// It is a judgement, written down once per check and arguable - the same way the
        /// registry's order is where importance is recorded.
        /// </summary>
        bool DefaultEnabled { get; }

        /// <summary>Documentation. Generates the guide; the build fails if it is incomplete.</summary>
        CheckDoc Doc { get; }

        AnomalyResult Evaluate(ProbeContext context);
    }
}
