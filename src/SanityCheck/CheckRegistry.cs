// File: SanityCheck/CheckRegistry.cs
using System.Collections.Generic;
using SystemOptimizer.SanityCheck.Checks;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// Every check that ships, in the order they should be considered.
    ///
    /// ORDER IS MEANINGFUL and it is how importance is expressed. The design called for
    /// ranking findings by "confidence x impact", which would need an impact score - and a
    /// number nobody can calibrate, invented per check by its own author, would give the
    /// ranking a precision it does not have. Confidence is a real property of the reading;
    /// impact is a judgement, so it is written down once, here, as a deliberate order that
    /// can be argued with.
    ///
    /// This array is the ONLY line that names a concrete check, exactly like ToolRegistry -
    /// adding one is a file plus a line here.
    ///
    /// SCOPE DISCIPLINE, which is the standing instruction on this feature: few
    /// high-confidence checks beat many mediocre ones. Fifteen good ones would beat sixty,
    /// and the sixty would kill the feature by making it noise. The temptation is always to
    /// add another.
    /// </summary>
    public static class CheckRegistry
    {
        public static IReadOnlyList<IAnomalyCheck> All { get; } = new IAnomalyCheck[]
        {
            // The four marquee checks first: each one is individually worth having, and no
            // mainstream cleaner reports any of them.
            new LinkSpeedCheck(),
            new MemorySpeedCheck(),
            new DnsResolverCheck(),
            new DisplayGpuCheck(),

            // Then four quieter ones. TASK.NEVER_RAN is the generalisation of the case this
            // whole feature came from - a backup that had never once worked while reporting
            // itself healthy - so it earns its place despite being the least tidy to read.
            new TrimCheck(),
            new ScheduledTaskCheck(),
            new StartupEntryCheck(),
            new PathEntryCheck(),
        };
    }
}
