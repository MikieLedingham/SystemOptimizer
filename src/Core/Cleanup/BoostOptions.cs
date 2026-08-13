// File: BoostOptions.cs
using SystemOptimizer.Core.Settings;
namespace SystemOptimizer.Core.Cleanup
{
    public class BoostOptions
    {
        // Removed in 2.0 as unsafe or dishonest, deliberately and permanently:
        //   CleanEventLogs     - destroyed the machine's own troubleshooting history, and
        //                        mass log clearing reads as attacker behaviour to EDR.
        //   CleanTempProfiles  - recursively deleted any C:\Users\TEMP*, which matches real
        //                        accounts, ignored whether the profile was loaded, and left
        //                        orphaned ProfileList entries behind.
        //   CleanWindowsUpdate - emptied SoftwareDistribution\Download underneath a running
        //                        wuauserv, which can corrupt an update in flight.
        //   CleanPrefetch      - no measurable benefit; makes the next launch of everything
        //                        slower.
        //   CleanRestorePoints - the checkbox said "Delete Restore Points" and no code ever
        //                        implemented it. It promised a destructive act and did
        //                        nothing, which is the worst of both.
        // None of these touch files a user would miss, and all of them touch things the
        // operating system relies on. They are not coming back.

        // BASIC
        public bool CleanUserTemp { get; set; }
        public bool CleanBrowserCache { get; set; }
        public bool CleanDownloadsFolder { get; set; }
        public bool CleanRecent { get; set; }
        // Flushing the DNS cache moved to BASIC in 2.0. It has never needed elevation -
        // ipconfig /flushdns works as a standard user - but it sat on the Admin page, so
        // an unelevated run had to drop it to keep the page and the run agreeing. It
        // belongs where it can actually be used.
        public bool CleanDNSCache { get; set; }

        // CleanThumbnailCache was REMOVED. It cannot work: Explorer holds
        // thumbcache_*.db open for as long as it is running, which is always, so the
        // files could never be recycled - and the step ticked "cleaned" every time
        // regardless. A control that reports success for work it did not do is the same
        // fault as the "Delete restore points" checkbox no code ever implemented.
        //
        // Clearing it for real would mean stopping Explorer or scheduling deletion for
        // the next reboot. Both are far more disruptive than the few megabytes involved
        // justify, and Windows rebuilds the cache on demand anyway.
        // ADMIN - genuinely requires elevation, every one of these
        public bool CleanWindowsTemp { get; set; }
        public bool CleanCrashDumps { get; set; }
        public bool CleanOldWindows { get; set; }
        public bool CleanRecycleBin { get; set; }
        // RAM (options available in preferences)
        public bool AutoMonitorEnabled { get; set; }
        public int AutoThreshold { get; set; }
        public bool BoostRam { get; set; }
        public bool RememberChoices { get; set; }
        /// <summary>
        /// Drops everything the Admin page owns, for a run that is not elevated.
        ///
        /// The rule, and it is the right one: unelevated means nothing from that page
        /// can run, ever. Without this the stored ticks still reached the engine, which
        /// gated only the Recycle Bin - so an unelevated run walked C:\Windows\Temp, the
        /// WER report queues and Windows.old and attempted deletions in all of them. It
        /// also listed those steps in the confirmation dialog, promising work it could not
        /// carry out.
        ///
        /// This clears the OPTIONS for one run. It does not touch preferences.json, so the
        /// choices are still there when the app next runs as administrator.
        ///
        /// The DNS cache is NOT cleared: it moved to Basic, because it never needed
        /// elevation. An unelevated run can still do it.
        /// </summary>
        public void ClearAdminActions()
        {
            CleanWindowsTemp = false;
            CleanCrashDumps = false;
            CleanOldWindows = false;
            CleanRecycleBin = false;
        }

        // FromPreferences() was deleted here. It was a SECOND preferences-to-options
        // mapper, duplicating PreferencesManager.ToBoostOptions, and it had no callers
        // anywhere. Two mappers for one job is a drift hazard rather than a spare: moving
        // the DNS cache from Admin to Basic would have had to be made in both,
        // and the copy nothing calls is the copy nobody would have updated.
    }
}
