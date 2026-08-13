// File: Helpers/AutoRamMonitorHelper.cs
using System;
using System.Linq;
using System.Windows;
using System.Timers;
using Microsoft.VisualBasic.Devices;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Logging;
using SystemOptimizer.Shell;
namespace SystemOptimizer.Core.Ram
{
    public static class AutoRamMonitorHelper
    {
        /// <summary>
        /// Reads the PERSISTED count. It was a plain static int that reset to zero on
        /// every launch, while the main window and Diagnostics read a preferences field
        /// nothing wrote - so one surface forgot on restart and the other two always said
        /// zero. RecordRamBoost owns the number now.
        /// </summary>
        public static int AutoRamTriggerCount => PreferencesManager.GetAutoTriggerCount();
        private static Timer _autoRamMonitorTimer;
        private static bool _autoRamMonitorBusy = false;
        private static bool _autoRamWarningShown = false;
        private const int RamMonitorIntervalSeconds = 60;
        /// <summary>
        /// Start the auto RAM monitor timer.
        /// </summary>
        public static void Start()
        {
            if (_autoRamMonitorTimer != null)
                return; // already running
            _autoRamMonitorTimer = new Timer(RamMonitorIntervalSeconds * 1000);
            _autoRamMonitorTimer.Elapsed += (s, e) => CheckAutoRamBoost();
            _autoRamMonitorTimer.AutoReset = true;
            _autoRamMonitorTimer.Start();
        }
        public static bool AutoRamWarningShown => _autoRamWarningShown;
        /// <summary>
        /// Stop and clean up the auto RAM monitor timer.
        /// </summary>
        public static void Stop()
        {
            _autoRamMonitorTimer?.Stop();
            _autoRamMonitorTimer?.Dispose();
            _autoRamMonitorTimer = null;
        }
        private static void CheckAutoRamBoost()
        {
            if (_autoRamMonitorBusy) return;
            _autoRamMonitorBusy = true;
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    bool autoEnabled = PreferencesManager.GetAutoRamEnabled();
                    int threshold = PreferencesManager.GetAutoThreshold();
                    int warningThreshold = PreferencesManager.GetAutoWarningThreshold(); // <-- DYNAMIC
                    if (!autoEnabled)
                        return;
                    double ramPercent = GetCurrentRamUsagePercent();
                    // --- Show warning if threshold is too low ---
                    if (threshold < warningThreshold && !_autoRamWarningShown)
                    {
                        _autoRamWarningShown = true;
                        App.ShowTrayNotification("Your RAM auto-clean threshold is low. Raise the value to reduce boost frequency and notifications.");
                        TrayIconManager.RefreshTrayMenu();
                    }
                    // --- Clear RAM warning if threshold is raised to safe value ---
                    if (_autoRamWarningShown && threshold >= warningThreshold)
                    {
                        _autoRamWarningShown = false;
                        App.ShowTrayNotification("RAM warning cleared. Your auto-clean threshold is now set to a safe value.");
                        TrayIconManager.RefreshTrayMenu();
                    }
                    // --- Perform RAM boost if usage exceeds threshold ---
                    if (ramPercent >= threshold)
                    {
                        // Ask the Tools whether anything wants this held off. Core does not
                        // know what a no-boost list is, or that Gaming Mode exists - it asks
                        // a question and takes the answer, which is what lets a Tool be
                        // deleted without leaving a hole here.
                        //
                        // AUTOMATIC work only. RunRamOnlyQuickTrim, the tray's "Quick RAM
                        // boost" and the cleanup engine are all untouched: a Tool may decide
                        // the app should keep quiet, never that it should ignore something
                        // the user directly asked for.
                        string heldOff = Tools.ToolRegistry.AutomaticMaintenanceHeldOff();
                        if (heldOff != null)
                        {
                            LogHelper.Log($"Automatic RAM boost held off: {heldOff}.");
                            return;
                        }

                        // Cast result to double for Math.Round, then to long for MB result
                        double freedMbRaw = RamCleanupHelper.PerformRamCleanup();
                        long freedMb = (long)Math.Round(freedMbRaw);
                        // This path previously recorded nothing, so the overlay's "Last Boost"
                        // and the last-result view only ever showed manual boosts.
                        PreferencesManager.RecordRamBoost((int)freedMb, automatic: true);
                        App.ShowTrayNotification($"Auto RAM Boost: {freedMb} MB Recovered (Usage: {ramPercent:F0}%)");

                        // The increment used to be here, on a static that reset every
                        // launch. RecordRamBoost above owns the count now and persists it.

                        // Both halves, and in this order.
                        //
                        // This path used to push ONLY the trigger count, so the overlay's
                        // "Last Boost" line kept showing the previous figure while the
                        // counter beside it went up - the overlay reported that a boost had
                        // happened and simultaneously reported the wrong boost. The manual
                        // paths had always refreshed it; the automatic one, which is the
                        // whole point of having an overlay to watch, never did.
                        SystemOptimizer.OverlayWindow.RefreshAllAfterRamBoost();

                        var overlay = Application.Current.Windows
                            .OfType<SystemOptimizer.OverlayWindow>()
                            .FirstOrDefault();
                        if (overlay != null)
                        {
                            overlay.SetAutoTriggerCount(AutoRamTriggerCount);
                        }
                    }
                });
            }
            finally
            {
                _autoRamMonitorBusy = false;
            }
        }
        private static double GetCurrentRamUsagePercent()
        {
            var info = new ComputerInfo();
            double total = info.TotalPhysicalMemory;
            double available = info.AvailablePhysicalMemory;
            double used = total - available;
            return (used / total) * 100.0;
        }
    }
}
