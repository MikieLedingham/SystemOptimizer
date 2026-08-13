// File: Helpers/PreferencesManager.cs
using Newtonsoft.Json;
using System;
using System.IO;
using System.Windows;
using SystemOptimizer.Core.Cleanup;
namespace SystemOptimizer.Core.Settings
{
    public static class PreferencesManager
    {
        // Path constants
        public static readonly string AppDataDir = AppPaths.Root;
        public static readonly string PrefsPath = AppPaths.PreferencesFile;
        public static readonly string LogsDir = AppPaths.LogsDir;
        /// <summary>
        /// Which rows the overlay shows.
        ///
        /// THE DEFAULTS ARE A DELIBERATE SET, and they are mostly OFF.
        /// Nine rows on rather than twenty-two: a first launch used to be 652 pixels of
        /// overlay, which is most of a laptop screen, and an options page whose every box
        /// is already ticked reads as a list of things to switch off rather than a menu of
        /// things to add.
        ///
        /// The set is MIKIE'S OWN, taken from what he had actually settled on after living
        /// with the overlay - not a set reasoned out from first principles. An earlier
        /// draft of these defaults was the latter, and it chose differently: it kept CPU,
        /// network and disk on the grounds that live telemetry is what a HUD is for, and
        /// dropped the RAM boost readouts as application trivia. He wanted the opposite,
        /// and he is the one who has watched it every day.
        ///
        /// What is on: memory, and what this application has done - the last clean, the
        /// Recycle Bin, when it last ran, whether automatic boosting is being held off.
        /// What is off: everything a second window would tell you just as well, and every
        /// fact about the machine that never changes.
        ///
        /// User name and PC name are off for a second reason: they put the account name
        /// and the hostname into any screenshot. Diagnostics redacts both already.
        ///
        /// CHANGING A DEFAULT ONLY AFFECTS A MACHINE THAT HAS NEVER SAVED THAT SETTING.
        /// Anyone with an existing preferences.json keeps exactly what they had, which is
        /// right - and is why the options page has a "Reset to defaults" button, so this
        /// set is reachable rather than theoretical.
        /// </summary>
        public class OverlayFieldsSettings
        {
            // --- On: memory ----------------------------------------------------------
            public bool Ram { get; set; } = true;
            /// <summary>Cumulative RAM recovered.</summary>
            public bool ShowRamBadge { get; set; } = true;
            /// <summary>The process using the most memory right now.</summary>
            public bool ShowTopProcess { get; set; } = true;
            /// <summary>What the last RAM boost recovered.</summary>
            public bool ShowLastBoost { get; set; } = true;
            /// <summary>The automatic-boost trigger percentage.</summary>
            public bool ShowThreshold { get; set; } = true;

            // --- On: what this application has actually done -------------------------
            /// <summary>The standing "last clean" line - the one readout that says what
            /// the application has actually done for you.</summary>
            public bool ShowLastClean { get; set; } = true;
            /// <summary>Everything waiting in the Recycle Bin, not just what we put there:
            /// the number that decides whether to empty it.</summary>
            public bool ShowRecycleBin { get; set; } = true;
            /// <summary>"Last clean: 3 days ago". A nudge that never nags.</summary>
            public bool ShowLastCleanAge { get; set; } = true;
            /// <summary>Why automatic RAM boosting has gone quiet - otherwise it just
            /// looks broken.</summary>
            public bool ShowBoostHeldOff { get; set; } = true;

            // --- Off: live readings a second window reports just as well ---------------
            public bool Cpu { get; set; } = false;
            /// <summary>C:\ FREE SPACE, despite the name - see Disk below.</summary>
            public bool CDrive { get; set; } = false;
            public bool Network { get; set; } = false;
            public bool Wifi { get; set; } = false;
            public bool Battery { get; set; } = false;
            public bool Uptime { get; set; } = false;

            // --- Off: facts about the machine that do not change ----------------------
            public bool WindowsVersion { get; set; } = false;
            public bool Arch { get; set; } = false;
            /// <summary>Off also because a shared screenshot should not carry the account
            /// name. Diagnostics redacts the username everywhere for the same reason.</summary>
            public bool User { get; set; } = false;
            /// <summary>Off also because a shared screenshot should not carry the hostname.</summary>
            public bool Machine { get; set; } = false;
            public bool Gpu { get; set; } = false;
            /// <summary>Uptime already says when the machine started, in words.</summary>
            public bool Boot { get; set; } = false;

            // --- Off: detail, novelty, or narrow interest -----------------------------
            public bool Pagefile { get; set; } = false;
            public bool AppCount { get; set; } = false;
            /// <summary>
            /// C:\ READ AND WRITE THROUGHPUT, despite the name.
            ///
            /// Disk drives DiskRow, which the markup labels "Disk I/O"; CDrive drives
            /// CDriveRow, "C: Drive Free Space". The fields and the rows have always
            /// agreed. What did not agree were the two checkboxes in Overlay options,
            /// which carried each other's labels - so ticking "C:\ free" toggled the
            /// throughput row and ticking "C:\ activity" toggled free space. Fixed on the
            /// dialog, where the fault was; the names here are kept because they match the
            /// rows and are what every saved preferences.json already contains.
            /// </summary>
            public bool Disk { get; set; } = false;
            public bool ShowMouseDistance { get; set; } = false;
            /// <summary>One row per printer - three of them on the machine this was
            /// decided on.</summary>
            public bool ShowPrinterStatus { get; set; } = false;
            public bool ShowLaptopBattery { get; set; } = false;

            // ShowLastCleanAge and ShowBoostHeldOff are declared with the other things
            // this application reports about its own work, above. They are ON now.

            // HasSeenOverlayZoomTip was here, defaulting to false, and NOTHING read it -
            // all four readers use PreferencesModel.HasSeenOverlayZoomTip. A second copy
            // of one fact, in a class that otherwise holds only display toggles, where it
            // was not one. Same fault as LastRamBoostMessage and AdminShowAdminWarning,
            // both of which were deleted for the same reason.
        }
        // In-memory model
        private static PreferencesModel _currentPrefs;
        private static readonly object _lock = new();
        // JSON Versioning
        // 3: DNS cache and thumbnail cache moved from the Admin section to Basic.
        // 4: Top process, Last RAM boost and Boost threshold gained toggles, and the
        //    overlay defaults were reconsidered. Bumped so the file is rewritten once -
        //    which also drops HasSeenOverlayZoomTip, deleted with the first-run tips.
        private const int CurrentPrefsVersion = 4;
        // Load or initialize preferences at startup
        static PreferencesManager()
        {
            Directory.CreateDirectory(AppDataDir);
            Directory.CreateDirectory(LogsDir);
            LoadPreferences();
        }
        /// <summary>
        /// Singleton current preferences object. (Type-safe: PreferencesModel!)
        /// </summary>
        public static PreferencesModel Current
        {
            get
            {
                if (_currentPrefs == null)
                    LoadPreferences();
                return _currentPrefs;
            }
            set
            {
                _currentPrefs = value;
            }
        }
        /// <summary>
        /// Load preferences from disk (JSON) or create default.
        /// </summary>
        public static void LoadPreferences()
        {
            lock (_lock)
            {
                bool needsResave = false;
                if (!File.Exists(PrefsPath))
                {
                    _currentPrefs = PreferencesModel.CreateDefault();
                    needsResave = true;
                }
                else
                {
                    try
                    {
                        string json = File.ReadAllText(PrefsPath);
                        _currentPrefs = JsonConvert.DeserializeObject<PreferencesModel>(json)
                            ?? PreferencesModel.CreateDefault();
                        if (_currentPrefs.Version != CurrentPrefsVersion)
                        {
                            MigratePreferences(_currentPrefs.Version, CurrentPrefsVersion);
                            needsResave = true;
                        }
                    }
                    catch
                    {
                        _currentPrefs = PreferencesModel.CreateDefault();
                        needsResave = true;
                    }
                }
                // Defensive: initialize new fields if missing (for old upgrades)
                if (string.IsNullOrEmpty(_currentPrefs.InstallTime))
                {
                    _currentPrefs.InstallTime = DateTime.UtcNow.ToString("o");
                    needsResave = true;
                }
                if (needsResave)
                    SavePreferences(_currentPrefs);
            }
        }
        /// <summary>
        /// Save preferences (to disk) from supplied object or singleton.
        /// </summary>
        public static void SavePreferences(PreferencesModel model = null)
        {
            lock (_lock)
            {
                // Make extra sure the directory exists before we write
                if (!Directory.Exists(AppDataDir))
                    Directory.CreateDirectory(AppDataDir);
                _currentPrefs = model ?? _currentPrefs ?? PreferencesModel.CreateDefault();
                _currentPrefs.Version = CurrentPrefsVersion;
                string json = JsonConvert.SerializeObject(_currentPrefs, Formatting.Indented);
                File.WriteAllText(PrefsPath, json);
            }
        }
        public static void ReloadPreferences() => LoadPreferences();
        // Overlay Section Access
        public static double GetOverlayOpacity() => Current.Overlay?.Opacity ?? 0.5;
        public static void SetOverlayOpacity(double value)
        {
            Current.Overlay.Opacity = value;
            SavePreferences();
        }
        public static bool GetOverlayAlwaysOnTop() => Current.Overlay?.AlwaysOnTop ?? false;
        public static void SetOverlayAlwaysOnTop(bool value)
        {
            Current.Overlay.AlwaysOnTop = value;
            SavePreferences();
        }
        public static bool GetOverlayClickThrough() => Current.Overlay?.ClickThrough ?? false;
        public static void SetOverlayClickThrough(bool value)
        {
            Current.Overlay.ClickThrough = value;
            SavePreferences();
        }
        public static Rect GetOverlayPosition()
        {
            var o = Current.Overlay;
            return new Rect(o.Left, o.Top, o.Width, o.Height);
        }
        public static void SaveOverlayPosition(Rect rect)
        {
            var o = Current.Overlay;
            o.Left = rect.Left;
            o.Top = rect.Top;
            o.Width = rect.Width;
            o.Height = rect.Height;
            SavePreferences();
        }
        // --- General/Section Accessors ---
        public static bool GetBool(string key, string section = null)
        {
            var prefs = Current;
            if (string.IsNullOrEmpty(section)) section = "Root";
            return section switch
            {
                "Basic" => prefs.Basic.GetBool(key),
                "Admin" => prefs.Admin.GetBool(key),
                "Ram" => prefs.Ram.GetBool(key),
                "Overlay" => prefs.Overlay.GetBool(key),
                _ => false
            };
        }
        public static int GetInt(string key, string section = null)
        {
            var prefs = Current;
            if (string.IsNullOrEmpty(section)) section = "Root";
            return section switch
            {
                "Basic" => prefs.Basic.GetInt(key),
                "Admin" => prefs.Admin.GetInt(key),
                "Ram" => prefs.Ram.GetInt(key),
                "Overlay" => prefs.Overlay.GetInt(key),
                _ => 0
            };
        }
        public static T Get<T>(string section, string key, T defaultValue = default)
        {
            var prefs = Current;
            return prefs.Get<T>(section, key, defaultValue);
        }
        public static void Set<T>(string section, string key, T value)
        {
            var prefs = Current;
            prefs.Set(section, key, value);
            SavePreferences(prefs);
        }
        // --- Specialized API for key behaviors ---
        // RAM/Auto Cleanup Accessors
        public static int GetAutoThreshold() => Current.Ram?.AutoThreshold ?? 85;
        public static bool GetAutoRamEnabled() => Current.Ram?.AutoRam ?? false;
        public static string GetLastRamBoostMessage() => Current.Ram?.LastBoostMessage ?? "";

        /// <summary>
        /// Record a completed RAM boost: amount, when, and whether it ran automatically.
        /// Use this rather than SetLastRamBoostMessage - the automatic path in
        /// AutoRamMonitorHelper never recorded anything at all, so "last boost" only ever
        /// reflected manual boosts.
        /// </summary>
        public static void RecordRamBoost(int freedMb, bool automatic)
        {
            var ram = Current.Ram;
            ram.LastBoostMessage = $"{freedMb} MB Recovered";
            ram.LastBoostTimeUtc = DateTime.UtcNow.ToString("o");
            ram.LastBoostAutomatic = automatic;

            // The automatic path keeps its own record, because "when did this last do
            // something on its own?" cannot be answered from the shared fields: a manual
            // boost afterwards overwrites LastBoostTimeUtc and clears LastBoostAutomatic,
            // and the automatic run it replaced becomes unfindable.
            //
            // AutoTriggerCount was also never written by anything. AutoRamMonitorHelper
            // incremented a STATIC int that reset to zero on every launch, while the main
            // window and the Diagnostics report read this field - which nothing had ever
            // set. Both surfaces reported zero automatic boosts forever.
            if (automatic)
            {
                ram.AutoTriggerCount++;
                ram.LastAutoBoostTimeUtc = DateTime.UtcNow.ToString("o");
            }
            // The "in-memory mirror" of this value on the model was removed. Two fields
            // held the last boost - Ram.LastBoostMessage and PreferencesModel
            // .LastRamBoostMessage - and five places read one while five read the other.
            // They had already drifted apart on a real machine (765 MB against 848 MB),
            // so the overlay and the tray were reporting different last boosts. There is
            // one field now, reached through GetLastRamBoostMessage().
            SavePreferences();
        }

        /// <summary>Local time of the last recorded boost, or null if none.</summary>
        public static DateTime? GetLastRamBoostTime()
        {
            var s = Current.Ram?.LastBoostTimeUtc;
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t.ToLocalTime() : (DateTime?)null;
        }

        public static bool GetLastRamBoostWasAutomatic() => Current.Ram?.LastBoostAutomatic ?? false;

        /// <summary>Local time automatic boosting last ran, or null if it never has.</summary>
        public static DateTime? GetLastAutoBoostTime()
        {
            var s = Current.Ram?.LastAutoBoostTimeUtc;
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t.ToLocalTime() : (DateTime?)null;
        }

        public static void SetLastRamBoostMessage(string msg)
        {
            Current.Ram.LastBoostMessage = msg;
            SavePreferences();
        }
        public static int GetAutoTriggerCount() => Current.Ram?.AutoTriggerCount ?? 0;
        /// <summary>
        /// Returns the low-RAM warning threshold for auto-clean (default 60).
        /// In the future, make this user-configurable if desired.
        /// </summary>
        public static int GetAutoWarningThreshold()
        {
            // Hardcoded for now; can extend PreferencesModel for future user configurability
            return 60;
        }
        // FlagTampered / GetIsTampered / the IsTampered field are gone. Tamper detection
        // existed to protect the licence, and nothing ever called any of it even then.
        // With licensing removed there is nothing to tamper with, and a flag that can be
        // set but never read is a liability rather than a safeguard.
        // ApplyGracePeriod removed in 2.0 - licensing is gone, so there is no trial to grant.
        // BoostOptions hydration
        public static BoostOptions ToBoostOptions()
        {
            // Each option now has exactly ONE source section.
            //
            // Previously three of them were read from Basic and then read again from
            // Admin, so the Admin copy silently overwrote the Basic one - a tick in Basic
            // could be cancelled by an untouched default in Admin, with nothing on screen
            // to explain it. DNS and thumbnail cache now come from Basic, where their
            // checkboxes are; the Recycle Bin comes from Admin, where its checkbox is.
            var opts = new BoostOptions();
            var b = Current.Basic;
            opts.CleanUserTemp = b.TempFiles;
            opts.CleanRecent = b.Recent;
            opts.CleanDownloadsFolder = b.Downloads;
            opts.CleanBrowserCache = b.BrowserCache;
            opts.CleanDNSCache = b.DNSCache;
            var a = Current.Admin;
            opts.CleanWindowsTemp = a.WindowsTemp;
            opts.CleanCrashDumps = a.CrashDumps;
            opts.CleanOldWindows = a.OldWindows;
            opts.CleanRecycleBin = a.RecycleBin;
            var r = Current.Ram;
            opts.BoostRam = r.BoostRam;
            opts.AutoMonitorEnabled = r.AutoRam;
            opts.AutoThreshold = r.AutoThreshold;
            opts.RememberChoices = r.Remember;
            return opts;
        }
        // SaveBoostOptions was here - the reverse of ToBoostOptions, with no callers
        // anywhere. Worth deleting rather than leaving: it wrote BOTH copies of the values
        // that have just been reduced to one source, setting a.DNSCache, a.ThumbnailCache
        // and both b.RecycleBin and a.RecycleBin. Anything that started calling it would
        // have quietly re-created the duplicate-source bug the same day.
        // Free-usage quota accessors removed in 2.0 - there is no quota.
        public static PreferencesModel GetAllPreferences() => Current;
        // --- Versioning / Migration Logic ---
        private static void MigratePreferences(int oldVersion, int newVersion)
        {
            // if we’re upgrading from v1 to v2, make sure we default Wifi=true
            if (oldVersion < 2)
            {
                _currentPrefs.OverlayFields ??= new OverlayFieldsSettings();
                _currentPrefs.OverlayFields.Wifi = true;
            }

            // v3: flushing the DNS cache and clearing the thumbnail cache moved from the
            // Admin page to Basic, because neither ever required elevation.
            //
            // Carry the existing ticks across rather than letting them disappear. Someone
            // who had ticked these would otherwise open Basic, find them off, and have no
            // way of knowing the setting had been relocated rather than reset - which is
            // the same silent-loss shape as the OK button that called ClearPreferences.
            // OR-ed, not assigned, so a tick already present in Basic is never turned off.
            if (oldVersion < 3)
            {
                _currentPrefs.Basic ??= new BasicSection();
                _currentPrefs.Admin ??= new AdminSection();
                _currentPrefs.Basic.DNSCache |= _currentPrefs.Admin.DNSCache;
                _currentPrefs.Basic.ThumbnailCache |= _currentPrefs.Admin.ThumbnailCache;
                _currentPrefs.Admin.DNSCache = false;
                _currentPrefs.Admin.ThumbnailCache = false;
            }

            // Top process, Last RAM boost and Boost threshold had no toggle before v4:
            // they were on the overlay unconditionally, for everyone. Their new fields
            // default to FALSE, which is right for a new installation and wrong for a
            // machine already running - an upgrade would have silently removed three rows
            // the user was used to seeing, with no setting they could point at to explain
            // where they went.
            //
            // So anyone arriving from an older file keeps them. This is the same reasoning
            // as the v3 step above: a migration preserves what the user HAD, and a default
            // decides what a stranger GETS. Confusing the two is how a setting appears to
            // reset itself.
            if (oldVersion < 4)
            {
                _currentPrefs.OverlayFields ??= new OverlayFieldsSettings();
                _currentPrefs.OverlayFields.ShowTopProcess = true;
                _currentPrefs.OverlayFields.ShowLastBoost = true;
                _currentPrefs.OverlayFields.ShowThreshold = true;
            }

            _currentPrefs.Version = newVersion;
            SavePreferences(_currentPrefs);
        }
        // GetFreeUsesLeft / IncrementFreeUsage removed in 2.0 - the rolling 30-day quota is gone.
        // ===== OverlayFieldsSettings and section classes unchanged (see your original code) =====
        // The static ShowAdminWarning property was here, reading and writing
        // PreferencesModel.AdminShowAdminWarning. AdminCleanupDialog has always used
        // Admin.ShowAdminWarning instead, so this was a SECOND copy of the same setting
        // that nothing called - and had anything called it, the dialog and the property
        // would have disagreed. Both it and the field it wrapped are gone.
        // ========= PREFERENCES MODEL =========
        public class PreferencesModel
        {
            // LastRamBoostMessage and AdminShowAdminWarning were here. Both were second
            // copies of values that already live in a section (Ram.LastBoostMessage and
            // Admin.ShowAdminWarning), and one of them had measurably drifted from its
            // twin in a real preferences file. Any value stored twice will disagree
            // eventually; the only question is whether anyone notices.
            public int Version { get; set; } = CurrentPrefsVersion;
            public OverlaySection Overlay { get; set; } = new();
            public BasicSection Basic { get; set; } = new();
            public AdminSection Admin { get; set; } = new();
            public RamSection Ram { get; set; } = new();
            // HasSeenOverlayZoomTip was here. It remembered that a first-run "hold Ctrl to
            // zoom" tooltip had been shown, and both copies of that tooltip are gone -
            // On the whole first-run-tip pattern the old build was full of: "they're
            // overload and just me trying too hard". A setting whose only purpose is to
            // remember that a deleted thing happened is not a setting.
            /// <summary>No-boost mode. TRUE by default so that ticking an application in
            /// the list is enough to make it take effect - see Tools/NoBoost/NoBoostMode.</summary>
            public bool NoBoostEnabled { get; set; } = true;
            public OverlayFieldsSettings OverlayFields { get; set; } = new OverlayFieldsSettings();
            public string InstallTime { get; set; } = "";
            public int TotalRamFreedMB { get; set; } = 0;
            public T Get<T>(string section, string key, T defaultValue = default)
            {
                object value = section switch
                {
                    "Overlay" => Overlay.Get(key),
                    "Basic" => Basic.Get(key),
                    "Admin" => Admin.Get(key),
                    "Ram" => Ram.Get(key),
                    _ => defaultValue
                };
                return value is T tVal ? tVal : defaultValue;
            }
            public void Set<T>(string section, string key, T value)
            {
                switch (section)
                {
                    case "Overlay": Overlay.Set(key, value); break;
                    case "Basic": Basic.Set(key, value); break;
                    case "Admin": Admin.Set(key, value); break;
                    case "Ram": Ram.Set(key, value); break;
                }
            }
            public static PreferencesModel CreateDefault()
            {
                return new PreferencesModel
                {
                    InstallTime = DateTime.UtcNow.ToString("o"),
                    // all other defaults are handled automatically
                };
            }
        }
        public class OverlaySection
        {
            public double Left { get; set; } = 100.0;
            public double Top { get; set; } = 100.0;
            /// <summary>
            /// The size a machine that has never moved the overlay opens it at, and the
            /// ONLY place that size is decided - OverlayWindow.xaml used to carry a
            /// Width and Height too, which LoadWindowPosition overwrote before first
            /// paint, so they looked authoritative and were dead.
            ///
            /// 540 x 380 is measured, not chosen: with the default rows the content asks
            /// for 533 x 363. The old 240 x 300 predates most of those rows and clipped
            /// them - the overlay's rows do not wrap, so anything too narrow is cut off
            /// rather than reflowed. Slightly generous, because the longest line is the
            /// processor name and that varies by machine.
            /// </summary>
            public double Width { get; set; } = 540.0;
            public double Height { get; set; } = 380.0;
            public double Opacity { get; set; } = 0.8;
            public bool AlwaysOnTop { get; set; } = false;
            public bool ClickThrough { get; set; } = false;
            public object Get(string key) => key switch
            {
                nameof(Left) => Left,
                nameof(Top) => Top,
                nameof(Width) => Width,
                nameof(Height) => Height,
                nameof(Opacity) => Opacity,
                nameof(AlwaysOnTop) => AlwaysOnTop,
                nameof(ClickThrough) => ClickThrough,
                _ => null
            };
            public bool GetBool(string key) => (bool)(Get(key) ?? false);
            public int GetInt(string key) => (int)Convert.ChangeType(Get(key) ?? 0, typeof(int));
            public void Set<T>(string key, T value)
            {
                switch (key)
                {
                    case nameof(Left): Left = Convert.ToDouble(value); break;
                    case nameof(Top): Top = Convert.ToDouble(value); break;
                    case nameof(Width): Width = Convert.ToDouble(value); break;
                    case nameof(Height): Height = Convert.ToDouble(value); break;
                    case nameof(Opacity): Opacity = Convert.ToDouble(value); break;
                    case nameof(AlwaysOnTop): AlwaysOnTop = Convert.ToBoolean(value); break;
                    case nameof(ClickThrough): ClickThrough = Convert.ToBoolean(value); break;
                }
            }
        }
        public class BasicSection
        {
            public bool TempFiles { get; set; } = false;
            public bool BrowserCache { get; set; } = false;
            public bool Downloads { get; set; } = false;
            public bool TempProfiles { get; set; } = false;
            public bool AppTemp { get; set; } = false;
            public bool Recent { get; set; } = false;
            public bool ThumbnailCache { get; set; } = false;
            public bool DNSCache { get; set; } = false;
            public bool RecycleBin { get; set; } = false;
            public bool Remember { get; set; } = false;
            public object Get(string key) => key switch
            {
                nameof(TempFiles) => TempFiles,
                nameof(BrowserCache) => BrowserCache,
                nameof(Downloads) => Downloads,
                nameof(TempProfiles) => TempProfiles,
                nameof(AppTemp) => AppTemp,
                nameof(Recent) => Recent,
                nameof(ThumbnailCache) => ThumbnailCache,
                nameof(DNSCache) => DNSCache,
                nameof(RecycleBin) => RecycleBin,
                _ => null
            };
            public bool GetBool(string key) => (bool)(Get(key) ?? false);
            public int GetInt(string key) => (int)Convert.ChangeType(Get(key) ?? 0, typeof(int));
            public void Set<T>(string key, T value)
            {
                switch (key)
                {
                    case nameof(TempFiles): TempFiles = Convert.ToBoolean(value); break;
                    case nameof(BrowserCache): BrowserCache = Convert.ToBoolean(value); break;
                    case nameof(Downloads): Downloads = Convert.ToBoolean(value); break;
                    case nameof(TempProfiles): TempProfiles = Convert.ToBoolean(value); break;
                    case nameof(AppTemp): AppTemp = Convert.ToBoolean(value); break;
                    case nameof(Recent): Recent = Convert.ToBoolean(value); break;
                    case nameof(ThumbnailCache): ThumbnailCache = Convert.ToBoolean(value); break;
                    case nameof(DNSCache): DNSCache = Convert.ToBoolean(value); break;
                    case nameof(RecycleBin): RecycleBin = Convert.ToBoolean(value); break;
                }
            }
        }
        public class AdminSection
        {
            public bool WindowsTemp { get; set; } = false;
            public bool Prefetch { get; set; } = false;
            public bool CrashDumps { get; set; } = false;
            public bool DNSCache { get; set; } = false;
            public bool OldWindows { get; set; } = false;
            public bool RecycleBin { get; set; } = false;
            public bool RestorePoints { get; set; } = false;
            public bool EventLogs { get; set; } = false;
            public bool ThumbnailCache { get; set; } = false;
            public bool WindowsUpdate { get; set; } = false;
            public bool Remember { get; set; } = false;
            public bool ShowAdminWarning { get; set; } = true;
            public object Get(string key) => key switch
            {
                nameof(WindowsTemp) => WindowsTemp,
                nameof(Prefetch) => Prefetch,
                nameof(CrashDumps) => CrashDumps,
                nameof(DNSCache) => DNSCache,
                nameof(OldWindows) => OldWindows,
                nameof(RecycleBin) => RecycleBin,
                nameof(RestorePoints) => RestorePoints,
                nameof(EventLogs) => EventLogs,
                nameof(ThumbnailCache) => ThumbnailCache,
                nameof(WindowsUpdate) => WindowsUpdate,
                _ => null
            };
            public bool GetBool(string key) => (bool)(Get(key) ?? false);
            public int GetInt(string key) => (int)Convert.ChangeType(Get(key) ?? 0, typeof(int));
            public void Set<T>(string key, T value)
            {
                switch (key)
                {
                    case nameof(WindowsTemp): WindowsTemp = Convert.ToBoolean(value); break;
                    case nameof(Prefetch): Prefetch = Convert.ToBoolean(value); break;
                    case nameof(CrashDumps): CrashDumps = Convert.ToBoolean(value); break;
                    case nameof(DNSCache): DNSCache = Convert.ToBoolean(value); break;
                    case nameof(OldWindows): OldWindows = Convert.ToBoolean(value); break;
                    case nameof(RecycleBin): RecycleBin = Convert.ToBoolean(value); break;
                    case nameof(RestorePoints): RestorePoints = Convert.ToBoolean(value); break;
                    case nameof(EventLogs): EventLogs = Convert.ToBoolean(value); break;
                    case nameof(ThumbnailCache): ThumbnailCache = Convert.ToBoolean(value); break;
                    case nameof(WindowsUpdate): WindowsUpdate = Convert.ToBoolean(value); break;
                }
            }
        }
        public class RamSection
        {
            public bool BoostRam { get; set; } = false;
            public int AutoThreshold { get; set; } = 85;
            public bool AutoRam { get; set; } = false;
            public string LastBoostMessage { get; set; } = "";
            // Added 2.0 so the "last RAM result" view can say when the boost happened and
            // whether it was automatic. Empty string means no boost recorded yet.
            public string LastBoostTimeUtc { get; set; } = "";
            public bool LastBoostAutomatic { get; set; } = false;
            /// <summary>When automatic boosting last actually ran. Separate from
            /// LastBoostTimeUtc, which a later manual boost overwrites.</summary>
            public string LastAutoBoostTimeUtc { get; set; } = "";
            public int AutoTriggerCount { get; set; } = 0;
            public bool Remember { get; set; } = false;  // <-- Leave this!
            public object Get(string key) => key switch
            {
                nameof(BoostRam) => BoostRam,
                nameof(AutoThreshold) => AutoThreshold,
                nameof(AutoRam) => AutoRam,
                nameof(LastBoostMessage) => LastBoostMessage,
                nameof(AutoTriggerCount) => AutoTriggerCount,
                nameof(Remember) => Remember,
                _ => null
            };
            public bool GetBool(string key) => (bool)(Get(key) ?? false);
            public int GetInt(string key) => (int)Convert.ChangeType(Get(key) ?? 0, typeof(int));
            public void Set<T>(string key, T value)
            {
                switch (key)
                {
                    case nameof(BoostRam): BoostRam = Convert.ToBoolean(value); break;
                    case nameof(AutoThreshold): AutoThreshold = Convert.ToInt32(value); break;
                    case nameof(AutoRam): AutoRam = Convert.ToBoolean(value); break;
                    case nameof(LastBoostMessage): LastBoostMessage = value?.ToString() ?? ""; break;
                    case nameof(AutoTriggerCount): AutoTriggerCount = Convert.ToInt32(value); break;
                    case nameof(Remember): Remember = Convert.ToBoolean(value); break;
                }
            }
        }
    }
}
