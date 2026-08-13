// File: SanityCheck/Checks/LinkSpeedCheck.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SystemOptimizer.SanityCheck.Checks
{
    /// <summary>
    /// A wired adapter should be running at the fastest speed it supports.
    ///
    /// THE TWO FACTS, and they come from two different places on purpose:
    ///   capability - the link modes the DRIVER declares it can do, read from its own
    ///                Ndi\params\*SpeedDuplex\enum list in the registry;
    ///   actual     - the speed the link ACTUALLY negotiated, read from the network stack.
    ///
    /// Neither is derived from the other, which is what makes this a relationship check
    /// rather than a state check. Windows shows "Connected" either way, nothing is in an
    /// error state, and everything works - at a fraction of the speed. That is the whole
    /// failure class this feature exists for.
    ///
    /// The obvious cheap version - parse "2.5 Gigabit" out of the adapter's NAME - was
    /// rejected. A marketing string is not an observation, plenty of adapters do not carry
    /// a number, and it would report a finding on hardware whose driver never offered the
    /// higher mode at all.
    /// </summary>
    public sealed class LinkSpeedCheck : IAnomalyCheck
    {
        public string Id => "NET.LINK_SPEED";
        public string Title => "Wired network speed";

        // Certain: both numbers are read directly, and the comparison is arithmetic. There
        // is no inference step that could be wrong - only a situation the user may have
        // chosen, which is what WhenToIgnore is for.
        public Confidence Confidence => Confidence.Certain;
        public DateTime? ReviewBy => null;

        private const string NetClassKey =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        public bool DefaultEnabled => true;  // every PC with a network cable, and the finding is a real slowdown

        public CheckDoc Doc => new CheckDoc
        {
            Summary = "Checks that a network cable connection is running at the fastest " +
                      "speed the adapter supports.",
            WhyItMatters =
                "A network link negotiates its speed with whatever is at the other end. If " +
                "that negotiation settles low, nothing reports a problem: Windows says " +
                "Connected, files still copy, pages still load. They are simply several " +
                "times slower than the hardware can manage, indefinitely, and there is no " +
                "symptom that points at the cable.",
            WhenToIgnore = new[]
            {
                "Your router or switch is slower than your PC's adapter. A 2.5 Gb adapter " +
                "on a 1 Gb network settles at 1 Gb, which is correct and the best it can do.",
                "You are on a long or older cable run where the faster mode will not " +
                "negotiate reliably, and you would rather have a stable slower link.",
                "The connection is only used for something undemanding - a printer, a " +
                "management port - and its speed does not matter to you.",
                "You share the wall socket with a device that forces a lower speed."
            },
            HowToConfirm = new[]
            {
                "Settings, Network and internet, then the connection's Properties. The " +
                "line is called Link speed (Receive/Transmit).",
                "Or run: Get-NetAdapter | Select-Object Name, LinkSpeed",
                "Check what your router or switch supports - its speed caps the link."
            },
            Remedy = new[]
            {
                "Find out what the other end supports first. If the router or switch is " +
                "slower than the adapter, nothing here is wrong and there is nothing to do.",
                "If both ends support the faster speed, try a different cable. A damaged " +
                "or low-category cable is the usual cause, and the damage is rarely visible.",
                "Try a different port on the router or switch.",
                "Leave the adapter set to Auto Negotiation. Pinning a speed by hand hides " +
                "this problem rather than fixing it."
            },
            HowToVerify =
                "Unplug and replug the cable, then look at the link speed again. It should " +
                "show the faster figure."
        };

        public AnomalyResult Evaluate(ProbeContext context)
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsWiredAndConnected)
                .ToList();

            if (interfaces.Count == 0)
                return AnomalyResult.NotApplicable(
                    "No wired network connection is plugged in, so there is no link speed to compare.");

            var observations = new List<Observation>();
            var unreadable = new List<string>();

            foreach (var nic in interfaces)
            {
                var declared = ReadDeclaredModes(nic.Id, out string pinnedMode, out string failure);
                if (declared == null)
                {
                    unreadable.Add($"{nic.Name}: {failure}");
                    continue;
                }

                observations.Add(new Observation
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    ActualBitsPerSecond = nic.Speed,
                    CapableBitsPerSecond = declared.Count == 0 ? 0 : declared.Max(),
                    PinnedMode = pinnedMode
                });
            }

            // Nothing readable on either side is exactly the case that must NOT be reported
            // as a pass. Say why instead, and let the quarantine machinery notice if it
            // keeps happening.
            if (observations.Count == 0)
                return AnomalyResult.Inconclusive(
                    "The adapter's supported speeds could not be read. " + string.Join("; ", unreadable));

            foreach (var o in observations) context.Note(o.ToString());

            return Decide(observations);
        }

        /// <summary>
        /// The decision, separated from the observing so it can be exercised with made-up
        /// adapters. On a machine whose hardware is fine, a check that only ever runs
        /// against real readings can never be shown to notice anything - and a check
        /// nobody has seen fail is a check nobody has tested.
        /// </summary>
        internal static AnomalyResult Decide(IReadOnlyList<Observation> observations)
        {
            // A link running BELOW what the driver says it can do, by a real margin.
            var slow = observations
                .Where(o => o.CapableBitsPerSecond > 0 && o.ActualBitsPerSecond > 0)
                .Where(o => o.PinnedMode == null)
                .Where(o => o.ActualBitsPerSecond < o.CapableBitsPerSecond)
                .OrderByDescending(o => o.CapableBitsPerSecond - o.ActualBitsPerSecond)
                .ToList();

            if (slow.Count == 0)
            {
                var pinned = observations.FirstOrDefault(o => o.PinnedMode != null);
                if (pinned != null && observations.All(o => o.PinnedMode != null))
                    return AnomalyResult.NotApplicable(
                        $"The link speed on {pinned.Name} is set by hand to " +
                        $"\"{pinned.PinnedMode}\" rather than negotiated, so there is nothing to compare.");

                var best = observations.First();
                return AnomalyResult.Pass(
                    $"{best.Name} supports up to {Describe(best.CapableBitsPerSecond)}",
                    $"the link negotiated {Describe(best.ActualBitsPerSecond)}");
            }

            var worst = slow[0];
            return AnomalyResult.Finding(
                $"{worst.Name} ({worst.Description}) supports up to {Describe(worst.CapableBitsPerSecond)}",
                $"the link negotiated {Describe(worst.ActualBitsPerSecond)}",
                "The two ends of a network connection agree a speed between them, and this " +
                "one settled below what the adapter can do. Everything still works, which " +
                "is why nothing reports it. The usual causes are equipment at the other " +
                "end that is slower than this adapter - in which case nothing is wrong - " +
                "or a damaged cable.");
        }

        internal sealed class Observation
        {
            public string Name = "";
            public string Description = "";
            public long ActualBitsPerSecond;
            public long CapableBitsPerSecond;

            /// <summary>Non-null when the user pinned the speed instead of auto-negotiating.</summary>
            public string PinnedMode;

            public override string ToString() =>
                $"{Name}: capable {Describe(CapableBitsPerSecond)}, actual {Describe(ActualBitsPerSecond)}" +
                (PinnedMode == null ? "" : $", pinned to \"{PinnedMode}\"");
        }

        private static bool IsWiredAndConnected(NetworkInterface nic) =>
            nic.OperationalStatus == OperationalStatus.Up &&
            nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
            // Tunnels and virtual switches describe themselves as Ethernet and report
            // invented speeds - a WireGuard tunnel here claims 100 Gb. They have no
            // physical link to negotiate, so they are not what this check is about.
            !LooksVirtual(nic.Description);

        private static bool LooksVirtual(string description)
        {
            if (string.IsNullOrEmpty(description)) return false;
            string d = description.ToLowerInvariant();
            return d.Contains("virtual") || d.Contains("tunnel") || d.Contains("tap-") ||
                   d.Contains("vpn") || d.Contains("loopback") || d.Contains("pseudo") ||
                   d.Contains("wireguard") || d.Contains("tailscale") || d.Contains("hyper-v");
        }

        /// <summary>
        /// The driver's own list of link modes, from the class key whose NetCfgInstanceId
        /// matches this adapter's GUID. Returns null - never an empty list - when it cannot
        /// be read, so the caller cannot mistake "could not ask" for "supports nothing".
        /// </summary>
        private static List<long> ReadDeclaredModes(string interfaceGuid, out string pinnedMode, out string failure)
        {
            pinnedMode = null;
            failure = null;
            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(NetClassKey);
                if (classKey == null)
                {
                    failure = "the network adapter driver list is not present in the registry.";
                    return null;
                }

                foreach (var name in classKey.GetSubKeyNames())
                {
                    using var instance = classKey.OpenSubKey(name);
                    if (instance == null) continue;
                    if (!string.Equals(instance.GetValue("NetCfgInstanceId") as string, interfaceGuid,
                                       StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var enumKey = instance.OpenSubKey(@"Ndi\params\*SpeedDuplex\enum");
                    if (enumKey == null)
                    {
                        failure = "its driver does not publish a list of supported speeds.";
                        return null;
                    }

                    var modes = new List<long>();
                    string chosen = instance.GetValue("*SpeedDuplex") as string;
                    foreach (var valueName in enumKey.GetValueNames())
                    {
                        string label = enumKey.GetValue(valueName) as string;
                        long bits = ParseMode(label);
                        if (bits > 0) modes.Add(bits);

                        // "0" is Auto Negotiation on every driver that publishes this list.
                        // Anything else means the user fixed the speed themselves, and a
                        // deliberately fixed speed is not an anomaly.
                        if (chosen != null && chosen == valueName && valueName != "0" && bits > 0)
                            pinnedMode = label;
                    }

                    if (modes.Count == 0)
                    {
                        failure = "its driver's list of supported speeds could not be understood.";
                        return null;
                    }
                    return modes;
                }

                failure = "no driver entry matched this adapter.";
                return null;
            }
            catch (Exception ex)
            {
                failure = "the adapter's driver settings could not be read (" + ex.Message + ").";
                return null;
            }
        }

        private static readonly Regex ModePattern =
            new(@"([\d.]+)\s*(G|M)bps", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>"2.5 Gbps Full Duplex" -> 2500000000. Half duplex counts: it is still a
        /// mode the hardware supports, and it is never the fastest one.</summary>
        internal static long ParseMode(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return 0;
            var m = ModePattern.Match(label);
            if (!m.Success) return 0;
            if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double value))
                return 0;

            double multiplier = m.Groups[2].Value.Equals("G", StringComparison.OrdinalIgnoreCase)
                ? 1_000_000_000d : 1_000_000d;
            return (long)(value * multiplier);
        }

        internal static string Describe(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "an unknown speed";
            if (bitsPerSecond >= 1_000_000_000)
            {
                double gb = bitsPerSecond / 1_000_000_000d;
                return (Math.Abs(gb - Math.Round(gb)) < 0.05 ? $"{gb:0}" : $"{gb:0.#}") + " Gb";
            }
            return $"{bitsPerSecond / 1_000_000d:0} Mb";
        }
    }
}
