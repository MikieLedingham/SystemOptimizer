// File: SanityCheck/Checks/MemorySpeedCheck.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemOptimizer.SanityCheck.Checks
{
    /// <summary>
    /// Memory should be running at the speed it is rated for.
    ///
    /// THE TWO FACTS, and this is the cleanest pairing of the four because the firmware
    /// itself reports both, separately:
    ///   rated   - SMBIOS "Speed", the fastest the module declares it can run;
    ///   running - SMBIOS "Configured Memory Speed", what the memory controller actually
    ///             set it to.
    ///
    /// When they differ the usual cause is that the memory profile (XMP on Intel, EXPO on
    /// AMD) was never switched on in the firmware, so the modules fall back to the slow
    /// baseline the standard guarantees. Nothing anywhere reports this. The PC is healthy,
    /// stable, and running expensive memory at a fraction of what it was bought for -
    /// often for the machine's entire life.
    /// </summary>
    public sealed class MemorySpeedCheck : IAnomalyCheck
    {
        public string Id => "MEM.RATED_SPEED";
        public string Title => "Memory speed";

        public Confidence Confidence => Confidence.Certain;
        public DateTime? ReviewBy => null;

        public bool DefaultEnabled => true;  // every PC has memory, and it is invisible everywhere else

        public CheckDoc Doc => new CheckDoc
        {
            Summary = "Checks that your memory is running at the speed it is rated for.",
            WhyItMatters =
                "Memory modules are sold at a headline speed they can only reach once the " +
                "matching profile is enabled in the firmware. Until then they run at the " +
                "slower speed the standard guarantees. Everything works perfectly, nothing " +
                "reports a fault, and the difference is real - it shows up most in games " +
                "and anything the processor's integrated graphics does.",
            WhenToIgnore = new[]
            {
                "You chose the slower speed on purpose, for stability. A machine that stays " +
                "up is worth more than a faster memory figure.",
                "Your processor or motherboard does not support the module's rated speed, " +
                "so the slower figure is the fastest this combination can do.",
                "You have filled every memory slot. Many boards drop the supported speed " +
                "when all slots are populated, and that is by design.",
                "This is a work machine you are not permitted to change firmware settings on."
            },
            HowToConfirm = new[]
            {
                "Task Manager, Performance tab, Memory. The Speed figure there is what the " +
                "memory is actually running at.",
                "Or run: Get-CimInstance Win32_PhysicalMemory | " +
                "Select-Object Speed, ConfiguredClockSpeed",
                "The rated speed is usually printed on the module's own label."
            },
            Remedy = new[]
            {
                "Check the module's rated speed against what your motherboard and processor " +
                "support. If they cannot reach it, there is nothing to change.",
                "Otherwise, restart into the firmware settings - usually Delete or F2 during " +
                "startup - and look for XMP, EXPO, or DOCP.",
                "Enable the profile, save, and let the machine restart.",
                "If it will not start afterwards, clear the setting and leave it off. Not " +
                "every module reaches its rated speed in every board, and that is normal."
            },
            HowToVerify =
                "Open Task Manager, Performance, Memory, and read the Speed figure. It " +
                "should now match the rated speed."
        };

        public AnomalyResult Evaluate(ProbeContext context)
        {
            if (!context.TryQuery(
                    "SELECT BankLabel, DeviceLocator, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory",
                    out var rows, out string reason))
                return AnomalyResult.Inconclusive(reason);

            var modules = new List<Module>();
            foreach (var row in rows)
            {
                // Both sides must be present. A driver or firmware that leaves
                // ConfiguredClockSpeed empty gives no second fact, and treating a missing
                // number as "same as rated" would turn every such machine into a silent
                // pass - the precise way a check rots into decoration.
                if (!ProbeContext.TryValue<uint>(row, "Speed", out uint rated)) continue;
                if (!ProbeContext.TryValue<uint>(row, "ConfiguredClockSpeed", out uint running)) continue;
                if (rated == 0 || running == 0) continue;

                ProbeContext.TryValue<string>(row, "DeviceLocator", out string slot);
                ProbeContext.TryValue<string>(row, "BankLabel", out string bank);

                modules.Add(new Module
                {
                    Slot = !string.IsNullOrWhiteSpace(slot) ? slot
                         : !string.IsNullOrWhiteSpace(bank) ? bank : "a memory module",
                    RatedMhz = rated,
                    RunningMhz = running
                });
            }

            if (modules.Count == 0)
                return AnomalyResult.Inconclusive(
                    rows.Count == 0
                        ? "Windows reported no memory modules, which cannot be right - the " +
                          "information is unavailable on this PC."
                        : "The firmware does not report both the rated and the running speed " +
                          "for any module, so there is nothing to compare.");

            foreach (var m in modules)
                context.Note($"{m.Slot}: rated {m.RatedMhz}, running {m.RunningMhz}");

            return Decide(modules);
        }

        internal static AnomalyResult Decide(IReadOnlyList<Module> modules)
        {
            var slow = modules
                .Where(m => m.RunningMhz < m.RatedMhz)
                .OrderByDescending(m => m.RatedMhz - m.RunningMhz)
                .ToList();

            if (slow.Count == 0)
            {
                var first = modules[0];
                return AnomalyResult.Pass(
                    $"the memory is rated for {first.RatedMhz} MT/s",
                    $"it is running at {first.RunningMhz} MT/s");
            }

            var worst = slow[0];
            string which = slow.Count == modules.Count
                ? (modules.Count == 1 ? "The memory module" : "Every memory module")
                : $"{slow.Count} of {modules.Count} memory modules";

            return AnomalyResult.Finding(
                $"the memory in {worst.Slot} is rated for {worst.RatedMhz} MT/s",
                $"it is running at {worst.RunningMhz} MT/s",
                $"{which} is running slower than it is rated for. This normally means the " +
                "memory profile - XMP, EXPO or DOCP depending on the board - has never been " +
                "switched on in the firmware, so the modules fall back to the slower speed " +
                "the standard guarantees. Nothing is faulty and nothing will report it.");
        }

        internal sealed class Module
        {
            public string Slot = "";
            public uint RatedMhz;
            public uint RunningMhz;
        }
    }
}
