// File: SanityCheck/Checks/DisplayGpuCheck.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemOptimizer.SanityCheck.Checks
{
    /// <summary>
    /// If there is a graphics card, the monitor should be plugged into it.
    ///
    /// THE TWO FACTS: which display adapters exist, and which of them is actually driving
    /// a screen. Both from Win32_VideoController, but they are genuinely independent -
    /// having a card says nothing about what the cable is plugged into.
    ///
    /// The failure is ordinary and completely silent: the monitor cable goes into the
    /// motherboard's socket instead of the card's. Windows works, the desktop appears, the
    /// card shows up in Device Manager with no error - and games run on the processor's
    /// built-in graphics while an expensive card sits idle a few centimetres away.
    ///
    /// WHY Probable, not Certain. Telling integrated graphics from a card is not always
    /// deterministic: Intel now sells discrete cards under the same vendor ID as its
    /// integrated ones, and AMD uses one vendor ID for both. The rules below are good but
    /// they are rules, not readings, so this must not interrupt. It also refuses outright
    /// on laptops, where the screen is wired to the processor's graphics by design and no
    /// amount of replugging will change it.
    /// </summary>
    public sealed class DisplayGpuCheck : IAnomalyCheck
    {
        public string Id => "GPU.DISPLAY_PATH";
        public string Title => "Which graphics chip drives the screen";

        public Confidence Confidence => Confidence.Probable;
        public DateTime? ReviewBy => null;

        public bool DefaultEnabled => true;  // refuses on laptops itself, so it is quiet where it does not apply

        public CheckDoc Doc => new CheckDoc
        {
            Summary = "Checks that your monitor is plugged into your graphics card rather " +
                      "than into the motherboard.",
            WhyItMatters =
                "A desktop with a graphics card usually also has graphics built into the " +
                "processor, and both have monitor sockets. Plugging into the motherboard by " +
                "mistake works perfectly - the desktop appears, Windows is happy, the card " +
                "shows no fault - but games and anything else demanding run on the much " +
                "slower built-in graphics. It is one of the most common ways a new PC " +
                "quietly underperforms from the day it is built.",
            WhenToIgnore = new[]
            {
                "You use the built-in graphics on purpose - to keep the machine quiet or " +
                "cool, or to leave the card free for something else.",
                "You run several monitors and deliberately drive some from the motherboard " +
                "because the card has run out of sockets.",
                "The card is passed through to a virtual machine, or reserved for compute " +
                "work rather than display.",
                "You are using a monitor arrangement your card cannot support - an unusual " +
                "connector, or a KVM switch that only works from the motherboard socket."
            },
            HowToConfirm = new[]
            {
                "Open Task Manager, Performance tab. Each graphics chip is listed " +
                "separately - watch which one moves while you drag a window about.",
                "Look at the back of the PC: the card's sockets are in a horizontal group " +
                "lower down, separate from the motherboard's cluster of USB and network " +
                "sockets.",
                "Or run: Get-CimInstance Win32_VideoController | " +
                "Select-Object Name, CurrentHorizontalResolution"
            },
            Remedy = new[]
            {
                "Shut the PC down fully.",
                "Move the monitor cable from the motherboard's socket to one on the " +
                "graphics card itself - the group of sockets lower down, on its own.",
                "Start the PC. If the screen stays black, put the cable back and check the " +
                "card is seated and has its power cables connected.",
                "If you use more than one monitor, move them all to the card if it has " +
                "enough sockets."
            },
            HowToVerify =
                "In Task Manager, Performance, the graphics card should now show activity " +
                "when you move a window around."
        };

        public AnomalyResult Evaluate(ProbeContext context)
        {
            if (!context.TryQuery(
                    "SELECT Name, PNPDeviceID, CurrentHorizontalResolution, AdapterCompatibility " +
                    "FROM Win32_VideoController",
                    out var rows, out string reason))
                return AnomalyResult.Inconclusive(reason);

            // A laptop's screen is wired to the processor's graphics and cannot be moved.
            // Checked BEFORE anything else: on a laptop this finding is not merely
            // ignorable, it is unactionable, and an unactionable alert is pure noise.
            if (IsPortable(context, out string portableReason))
                return AnomalyResult.NotApplicable(portableReason);

            var adapters = new List<Adapter>();
            foreach (var row in rows)
            {
                ProbeContext.TryValue<string>(row, "Name", out string name);
                ProbeContext.TryValue<string>(row, "PNPDeviceID", out string pnp);
                bool active = ProbeContext.TryValue<uint>(row, "CurrentHorizontalResolution", out uint width)
                              && width > 0;

                if (string.IsNullOrWhiteSpace(name)) continue;
                adapters.Add(new Adapter
                {
                    Name = name.Trim(),
                    Kind = Classify(pnp, name),
                    DrivingADisplay = active
                });
            }

            if (adapters.Count == 0)
                return AnomalyResult.Inconclusive(
                    "Windows reported no display adapters, so there is nothing to compare.");

            foreach (var a in adapters)
                context.Note($"{a.Name}: {a.Kind}, {(a.DrivingADisplay ? "driving a display" : "idle")}");

            return Decide(adapters);
        }

        internal static AnomalyResult Decide(IReadOnlyList<Adapter> adapters)
        {
            var discrete = adapters.Where(a => a.Kind == AdapterKind.Discrete).ToList();
            var integrated = adapters.Where(a => a.Kind == AdapterKind.Integrated).ToList();

            if (discrete.Count == 0)
                return AnomalyResult.NotApplicable(
                    "This PC has no separate graphics card, so there is nothing for the " +
                    "monitor to be plugged into instead.");

            if (discrete.Any(a => a.DrivingADisplay))
                return AnomalyResult.Pass(
                    $"this PC has {Join(discrete.Select(a => a.Name))}",
                    $"the display is being driven by {Join(discrete.Where(a => a.DrivingADisplay).Select(a => a.Name))}");

            var activeIntegrated = integrated.Where(a => a.DrivingADisplay).ToList();

            // A card driving nothing while nothing else is driving anything either means
            // the screen is on something this check could not classify - a capture card, a
            // remote session, a USB display adapter. Not a finding: one of the two facts is
            // missing, and inventing the missing one is how a detector starts crying wolf.
            if (activeIntegrated.Count == 0)
                return AnomalyResult.Inconclusive(
                    "No display adapter reports that it is driving a screen, so where the " +
                    "picture comes from could not be established. This is normal in a " +
                    "remote desktop session.");

            return AnomalyResult.Finding(
                $"this PC has {Join(discrete.Select(a => a.Name))}",
                $"the monitor is being driven by {Join(activeIntegrated.Select(a => a.Name))} instead",
                "The graphics card is not driving the screen, which normally means the " +
                "monitor cable is plugged into the motherboard rather than the card. " +
                "Everything works and nothing reports a fault - the card simply sits idle " +
                "while the processor's built-in graphics does the work.");
        }

        internal enum AdapterKind { Integrated, Discrete, Unknown }

        internal sealed class Adapter
        {
            public string Name = "";
            public AdapterKind Kind;
            public bool DrivingADisplay;
        }

        /// <summary>
        /// Vendor first, name second. Vendor IDs are exact where they are decisive:
        /// NVIDIA has never shipped integrated graphics for a PC, so VEN_10DE is a card.
        /// Intel and AMD ship both under one ID, so those fall through to the name, and
        /// anything still unclear stays Unknown rather than being guessed - an adapter
        /// counted as the wrong kind produces a confidently wrong finding.
        /// </summary>
        internal static AdapterKind Classify(string pnpDeviceId, string name)
        {
            string id = (pnpDeviceId ?? "").ToUpperInvariant();
            string n = (name ?? "").ToUpperInvariant();

            // Virtual adapters in remote sessions and VMs are neither.
            if (n.Contains("REMOTE") || n.Contains("VIRTUAL") || n.Contains("BASIC DISPLAY") ||
                n.Contains("MESHED") || n.Contains("IDD") || n.Contains("PARSEC"))
                return AdapterKind.Unknown;

            if (id.Contains("VEN_10DE")) return AdapterKind.Discrete;   // NVIDIA

            if (id.Contains("VEN_8086"))                                 // Intel
                return n.Contains("ARC") ? AdapterKind.Discrete : AdapterKind.Integrated;

            if (id.Contains("VEN_1002") || id.Contains("VEN_1022"))      // AMD
            {
                // AMD's integrated parts are the ones that do not carry a model number.
                if (n.Contains("RADEON(TM) GRAPHICS") || n.Contains("VEGA") ||
                    n.Contains("INTEGRATED") || n.Contains("APU"))
                    return AdapterKind.Integrated;
                if (n.Contains("RX ") || n.Contains("RADEON PRO")) return AdapterKind.Discrete;
                return AdapterKind.Unknown;
            }

            return AdapterKind.Unknown;
        }

        private static bool IsPortable(ProbeContext context, out string reason)
        {
            reason = null;
            if (!context.TryQuery("SELECT ChassisTypes FROM Win32_SystemEnclosure", out var rows, out _))
                return false;   // Unreadable: fall through and let the display facts decide.

            foreach (var row in rows)
            {
                if (row["ChassisTypes"] is not ushort[] types) continue;
                // 8 Portable, 9 Laptop, 10 Notebook, 11 Hand Held, 12 Docking Station,
                // 14 Sub Notebook, 30 Tablet, 31 Convertible, 32 Detachable.
                if (types.Any(t => t is 8 or 9 or 10 or 11 or 12 or 14 or 30 or 31 or 32))
                {
                    reason = "This is a laptop, where the built-in screen is wired to the " +
                             "processor's graphics and cannot be moved to the card.";
                    return true;
                }
            }
            return false;
        }

        private static string Join(IEnumerable<string> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return "nothing";
            if (list.Count == 1) return list[0];
            return string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
        }
    }
}
