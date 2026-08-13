// File: SanityCheck/Checks/TrimCheck.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace SystemOptimizer.SanityCheck.Checks
{
    /// <summary>
    /// A PC with an SSD should have TRIM switched on.
    ///
    /// THE TWO FACTS: whether any drive is solid state, and whether Windows is allowed to
    /// tell drives which blocks are no longer in use. Neither implies the other, and the
    /// setting is global while the hardware is per-drive - which is exactly how a machine
    /// ends up with the setting wrong for the drive it actually has.
    ///
    /// Silent in the worst way: an SSD without TRIM does not fail, it gets slower over
    /// months as the drive loses track of which blocks are free. There is no event, no
    /// warning, and no moment where anything looks wrong - just a machine that is not as
    /// quick as it was and nobody able to say when that started.
    ///
    /// Read from the registry rather than by running fsutil. Same value, no process
    /// launch, and no output text to parse - fsutil's wording is localised, and this
    /// project has already been bitten once by depending on a localised string (restoring
    /// from the Recycle Bin by verb name, which silently did nothing on non-English
    /// Windows).
    /// </summary>
    public sealed class TrimCheck : IAnomalyCheck
    {
        public string Id => "STORAGE.TRIM";
        public string Title => "TRIM on solid state drives";

        public Confidence Confidence => Confidence.Certain;
        public DateTime? ReviewBy => null;

        private const string FileSystemKey = @"SYSTEM\CurrentControlSet\Control\FileSystem";

        public bool DefaultEnabled => true;  // NotApplicable without an SSD, so it costs nothing to leave on

        public CheckDoc Doc => new CheckDoc
        {
            Summary = "Checks that Windows is allowed to tell solid state drives which " +
                      "blocks are no longer in use.",
            WhyItMatters =
                "A solid state drive has to be told when a file is deleted, or it keeps " +
                "treating those blocks as full and has to shuffle data around before it can " +
                "write anything new. With this switched off the drive gets gradually slower " +
                "over months and wears faster. Nothing fails, nothing is reported, and there " +
                "is no point at which it looks broken - which is why it is usually found " +
                "years later, if at all.",
            WhenToIgnore = new[]
            {
                "You turned it off deliberately - some file recovery and forensic work needs " +
                "deleted data to stay readable, and TRIM is what destroys it.",
                "Your drives sit behind a RAID controller that handles this itself, or that " +
                "does not pass the command through.",
                "Every solid state drive in the machine is used only for temporary data you " +
                "would never try to recover, and you would rather keep the setting uniform " +
                "across your PCs.",
                "This is a virtual machine whose disks are managed by the host."
            },
            HowToConfirm = new[]
            {
                "Run: fsutil behavior query DisableDeleteNotify",
                "A result of 0 means TRIM is allowed. 1 means it is switched off.",
                "To see which of your drives are solid state, open Task Manager, " +
                "Performance - each disk is labelled SSD or HDD."
            },
            Remedy = new[]
            {
                "Open a command prompt as administrator.",
                "Run: fsutil behavior set DisableDeleteNotify NTFS 0",
                "Restart the PC.",
                "Windows will catch up on its own over the following days as the drive is " +
                "used. There is nothing to run manually and no need to force anything."
            },
            HowToVerify =
                "Run fsutil behavior query DisableDeleteNotify again. The NTFS line should " +
                "read 0."
        };

        public AnomalyResult Evaluate(ProbeContext context)
        {
            // Side one: is there an SSD at all? The storage namespace is the accurate
            // source; Win32_DiskDrive cannot tell solid state from spinning.
            if (!context.TryQuery(@"\\.\root\microsoft\windows\storage",
                                  "SELECT FriendlyName, MediaType FROM MSFT_PhysicalDisk",
                                  out var disks, out string reason))
                return AnomalyResult.Inconclusive(
                    "Which drives are solid state could not be established. " + reason);

            var ssds = new List<string>();
            bool anyKnown = false;
            foreach (var disk in disks)
            {
                if (!ProbeContext.TryValue<ushort>(disk, "MediaType", out ushort media)) continue;
                anyKnown = true;
                // 3 spinning, 4 solid state, 5 SCM. 0 is "the drive did not say", which is
                // not the same as "not an SSD" and must not be counted either way.
                if (media == 4)
                {
                    ProbeContext.TryValue<string>(disk, "FriendlyName", out string name);
                    ssds.Add(string.IsNullOrWhiteSpace(name) ? "a solid state drive" : name.Trim());
                }
            }

            if (!anyKnown)
                return AnomalyResult.Inconclusive(
                    "No drive reported whether it is solid state or spinning.");

            if (ssds.Count == 0)
                return AnomalyResult.NotApplicable(
                    "This PC has no solid state drives, so TRIM does not apply to it.");

            // Side two, read independently of side one.
            int? disableDeleteNotify = ReadDisableDeleteNotify();
            if (disableDeleteNotify == null)
                return AnomalyResult.Inconclusive(
                    "Whether TRIM is switched on could not be read from Windows.");

            context.Note($"{ssds.Count} SSD(s); NtfsDisableDeleteNotify={disableDeleteNotify}");
            return Decide(ssds, disableDeleteNotify.Value);
        }

        internal static AnomalyResult Decide(IReadOnlyList<string> ssds, int disableDeleteNotify)
        {
            string drives = ssds.Count == 1 ? ssds[0] : $"{ssds.Count} solid state drives";

            // 0 is on. Anything else is off - Windows has used more than one non-zero value
            // over the years, so this tests for "not enabled" rather than for a specific
            // disabled value.
            if (disableDeleteNotify == 0)
                return AnomalyResult.Pass($"this PC has {drives}", "TRIM is switched on");

            return AnomalyResult.Finding(
                $"this PC has {drives}",
                "TRIM is switched off",
                "Windows is not telling the drive which blocks are free. A solid state " +
                "drive that is never told this gradually slows down and wears faster as it " +
                "fills up. Nothing will fail and nothing else will report it.");
        }

        private static int? ReadDisableDeleteNotify()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(FileSystemKey);
                // Absent means the default, which is enabled. Reported as such rather than
                // as unreadable: a missing value here is a real answer.
                object value = key?.GetValue("NtfsDisableDeleteNotify");
                if (key == null) return null;
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch { return null; }
        }
    }
}
