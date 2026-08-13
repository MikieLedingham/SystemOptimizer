// File: Models/PreferencesData.cs

namespace SystemOptimizer.Core.Settings
{
    public class PreferencesData
    {
        public BasicSettings? Basic { get; set; }
        public AdminSettings? Admin { get; set; }
        public RamSettings? Ram { get; set; }

        // NEW: Tray/Overlay UI flags
        public bool ClickThroughEnabled { get; set; }
    }

    public class BasicSettings
    {
        public bool TempFiles { get; set; }
        public bool AppTemp { get; set; }
        public bool BrowserCache { get; set; }
        public bool Downloads { get; set; }
        public bool Recent { get; set; }
        public bool TempProfiles { get; set; }
        public bool Remember { get; set; }
    }

    public class AdminSettings
    {
        public bool WindowsTemp { get; set; }
        public bool Prefetch { get; set; }
        public bool CrashDumps { get; set; }
        public bool DNSCache { get; set; }
        public bool OldWindows { get; set; }
        public bool RecycleBin { get; set; }
        public bool RestorePoints { get; set; }
        public bool EventLogs { get; set; }
        public bool ThumbnailCache { get; set; }
        public bool WindowsUpdate { get; set; }
        public bool Remember { get; set; }
    }

    public class RamSettings
    {
        public bool BoostRam { get; set; }
        public bool AutoMonitorEnabled { get; set; }
        public int AutoThreshold { get; set; }
        public bool RememberChoices { get; set; }
    }
}
