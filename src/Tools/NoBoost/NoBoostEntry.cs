// File: Tools/NoBoost/NoBoostEntry.cs
namespace SystemOptimizer.Tools.NoBoost
{
    /// <summary>
    /// One application in the no-boost list.
    ///
    /// Was Helpers/GameInfo, and the rename is not cosmetic: the old model had a single
    /// Name doing two incompatible jobs. It had to be shown to the user, so the scanner
    /// filled it with friendly things like "Crystal Disk Info" harvested from Start Menu
    /// shortcuts - and it had to be matched against running processes, which are called
    /// things like "DiskInfo64K". One field cannot be both, and the matching silently lost
    /// every time the two differed.
    ///
    /// Name is now the MATCH KEY - the executable's filename without extension, exactly as
    /// Windows reports a running process. DisplayName is what the user reads. Where they
    /// are the same, DisplayName is left null.
    /// </summary>
    public class NoBoostEntry
    {
        /// <summary>Process name: the executable's filename without its extension.</summary>
        public string Name { get; set; }

        /// <summary>What the user recognises, when that differs from the process name.</summary>
        public string DisplayName { get; set; }

        /// <summary>Full path to the executable, where it is known. Unambiguous.</summary>
        public string ExePath { get; set; }

        public bool Selected { get; set; }

        /// <summary>What to show in the list.</summary>
        public string Label =>
            string.IsNullOrWhiteSpace(DisplayName) || DisplayName == Name
                ? Name
                : $"{DisplayName}  ({Name})";
    }
}
