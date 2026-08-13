// File: MainWindow.xaml.cs
#define ENABLE_STARTUP_LOG   // comment this out to disable logging

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SystemOptimizer.Dialogs;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;
using Keyboard = System.Windows.Input.Keyboard;
using SystemOptimizer.Core.Cleanup;
using SystemOptimizer.Core.Ram;
using SystemOptimizer.Core.Monitoring;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Platform;
using SystemOptimizer.Core.Logging;
using SystemOptimizer.Shell;

namespace SystemOptimizer
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        private bool _wasElevatedAtStartup;
        private bool _isElevated;
        private bool _notifiedUserMode;
        // Licensing removed in 2.0 - the product is free, so every feature is always available.
        private bool CanRunQuickRamBoost() => true;

        private void RunQuickRamBoost()
        {
            try
            {
                bool isAdmin = _isElevated; // or UacHelper.IsRunningAsAdmin();
                int freed = CleanupHelper.RunRamOnlyQuickTrim(isAdmin);

                if (freed <= 0)
                {
                    // Optional: differentiate “0” from a failure (the helper already toasts)
                    // App.ShowTrayNotification("Quick RAM boost: no additional memory freed.");
                }
            }
            catch (Exception ex)
            {
                // The exception was caught and then dropped on the floor, with the logging
                // left commented out - so a failed boost told the user it had failed and
                // told nobody why. Logged now, like the hotkey path already does.
                LogHelper.Log("RunQuickRamBoost exception: " + ex);
                App.ShowTrayNotification("Quick RAM boost failed.");
            }
        }


        private OverlayWindow _overlayInstance;
        private ResourceMonitorManager _resourceMonitor;

#if ENABLE_STARTUP_LOG
        private static readonly object _logSync = new object();
        private static void DebugLog(string msg)
        {
            try
            {
                var logPath = AppPaths.StartupLogFile;
                lock (_logSync)
                {
                    File.AppendAllText(logPath,
                        $"{DateTime.Now:O} [MainWindow] {msg}{Environment.NewLine}");
                }
            }
            catch { /* ignore logging errors */ }
        }
#else
        [Conditional("NEVER")]
        private static void DebugLog(string _) { }
#endif

        public MainWindow()
        {
            Instance = this;

            InitializeComponent();

            Loaded += MainWindow_Loaded;

            // Version, from the one place that decides what it is.
            if (VersionTextBlock != null)
                VersionTextBlock.Text = $"Version {Core.AppInfo.Version}";

            // Hotkeys
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            // Elevation snapshot. (Licensing removed in 2.0.)
            _isElevated = UacHelper.IsRunningAsAdmin();
            _wasElevatedAtStartup = _isElevated;

            // Resource monitor (started after Loaded)
            try
            {
                _resourceMonitor = new ResourceMonitorManager(1.0);
                _resourceMonitor.ResourceUpdated += OnResourceUpdated;
            }
            catch { /* swallow — non‑fatal */ }

            // Feature gating
            ApplyFeatureGates();

            // Inform user if running without elevation
            if (!_isElevated && !_notifiedUserMode)
            {
                _notifiedUserMode = true;
                App.ShowTrayNotification("Running in User Mode: Admin features disabled. Some optimizations limited.");
            }

            ApplyElevationGates(_isElevated);
        }
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _resourceMonitor?.Start();
            AutoRamMonitorHelper.Start();

            LoadBoostSettings();
            // Reads the LAST result. It does not run the checks - deliberately, because
            // "never on every launch" is what keeps this from becoming wallpaper.
            RefreshSanityCheckSummary();
            ApplyFeatureGates();
            ApplyElevationGates(_isElevated);
            // Title bar theming is handled centrally by ThemeManager.HookNewWindows(),
            // which covers every window rather than just this one.

            if (!_isElevated)
                App.ShowTrayNotification("Running in User Mode: Admin-only optimizations are disabled.");
        }
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            const ModifierKeys required = ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt;
            if ((Keyboard.Modifiers & required) != required)
                return;

            switch (e.Key)
            {
                case Key.G: // Manage Games
                    new ManageGamesDialog { Owner = this }.ShowDialog();
                    e.Handled = true;
                    break;

                case Key.O: // Toggle Overlay
                    ToggleOverlayFromTray();
                    e.Handled = true;
                    break;

                case Key.B: // Quick RAM Boost (RAM ONLY)
                    try
                    {
                        bool isAdmin = _isElevated; // or UacHelper.IsRunningAsAdmin();
                        int freed = CleanupHelper.RunRamOnlyQuickTrim(isAdmin);
                        // freed already logged / toast shown inside the method.
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Log("Hotkey B RAM boost exception: " + ex);
                        App.ShowTrayNotification("Quick RAM boost failed.");
                    }
                    e.Handled = true;
                    break;

                case Key.S: // Show last RAM result
                    App.ShowLastRamResult();
                    e.Handled = true;
                    break;

                case Key.L: // Open Logs folder. Was K, which mapped to nothing memorable.
                    LogManager.OpenLogFolder();
                    e.Handled = true;
                    break;

                // Both windows existed on disk but were
                // never in the csproj, so they had never been compiled or reachable.
                case Key.D: // Diagnostics
                    new DiagnosticsWindow { Owner = this }.ShowDialog();
                    e.Handled = true;
                    break;

                case Key.T: // Advanced admin Tools
                    new AdminToolsDialog { Owner = this }.ShowDialog();
                    e.Handled = true;
                    break;

                // No hotkey for About - a version number does not warrant one, and it is
                // on the right-click menu. BoostConfirmDialog was deleted entirely.
            }
        }
        private void OnResourceUpdated(ResourceMonitorManager.ResourceSnapshot stats)
        {
            Dispatcher.Invoke(() =>
            {
                _resourceMonitor.LatestSnapshot = stats;
                // The labels ("CPU", "Free RAM") live in XAML now, so these carry the value only.
                CpuTextBlock.Text = $"{stats.CpuUsage}%";
                RamTextBlock.Text = $"{Math.Round(stats.RamFreeGB, 1)} GB";
                App.UpdateTrayIcon(stats.RamPercentUsed, stats.CpuUsage);

                // The hold-off note used to be refreshed only when this window loaded or a
                // dialog closed, so starting or closing a listed program while it was open
                // left it saying something that had stopped being true - Mikie watched it
                // report two programs while the overlay, which does refresh, reported four.
                //
                // The CACHED ask, deliberately: this runs every second and the uncached one
                // enumerates every process on the machine. Ten seconds is well inside the
                // sixty-second automatic boost interval.
                RefreshBlockedNote(cached: true);
            });
        }

        // Licensing removed in 2.0. Every feature is available to everyone; the only
        // remaining distinction is whether the process is elevated, which genuinely
        // changes what the cleanup engine is able to do.
        private void ApplyFeatureGates()
        {
            StatsOverlayButton.IsEnabled = true;
            GoButton.IsEnabled = true;
            GoButton.ToolTip = "Run the selected cleanup and boost actions.";
        }

        private void ApplyElevationGates(bool allowAdminFeatures)
        {
            StatsOverlayButton.IsEnabled = true;
            GoButton.IsEnabled = true;
        }

        private void UpdateGoButtonState()
        {
            Dispatcher.Invoke(() =>
            {
                GoButton.IsEnabled = true;
                GoButton.ToolTip = "Run the selected cleanup and boost actions.";
            });
        }

        private void StatsOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayInstance != null)
            {
                _overlayInstance.Close();
                _overlayInstance = null;
                return;
            }

            PreferencesManager.LoadPreferences();

            _overlayInstance = new OverlayWindow();
            _overlayInstance.Closed += (_, __) =>
            {
                _overlayInstance = null;
            };

            var snapshot = _resourceMonitor?.LatestSnapshot;
            float ramUsage = snapshot?.RamPercentUsed ?? 0f;
            int threshold = PreferencesManager.GetAutoThreshold();
            bool autoEn = PreferencesManager.GetAutoRamEnabled();
            string lastMsg = PreferencesManager.GetLastRamBoostMessage();
            int triggers = PreferencesManager.GetAutoTriggerCount();

            var topProcInfo = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.ProcessName)
                         && p.ProcessName != "Idle"
                         && p.ProcessName != "System"
                         && p.ProcessName != "Memory Compression")
                .OrderByDescending(p =>
                {
                    try { return p.WorkingSet64; } catch { return 0; }
                })
                .FirstOrDefault();

            string topProcess = topProcInfo != null
                ? $"{topProcInfo.ProcessName} ({topProcInfo.WorkingSet64 / (1024 * 1024)} MB)"
                : "None";

            string status = (ramUsage >= threshold && autoEn) ? "Waiting" : "Idle";

            _overlayInstance.UpdateOverlay(
                ramUsage, threshold, autoEn,
                lastMsg, triggers,
                topProcess, status);

            _overlayInstance.Show();
            _overlayInstance.Activate();
        }

        /// <summary>
        /// Opens one of the cleanup pages.
        ///
        /// These three used to sit behind a "Cleanup options..." button, whose entire
        /// content was three more buttons plus an OK and a Close that did the same thing
        /// as each other. That dialog is gone; this window owns the choice now.
        ///
        /// Every page saves as it goes, so there is no result to collect - but the boost
        /// card on this window shows the same RamSection that RAM cleanup edits, so it is
        /// reloaded afterwards rather than left displaying a stale copy.
        /// </summary>
        private void ShowCleanupPage(Window page)
        {
            page.Owner = this;
            page.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            page.ShowDialog();
            LoadBoostSettings();
        }

        private void BasicCleanup_Click(object sender, RoutedEventArgs e)
            => ShowCleanupPage(new BasicCleanupDialog());

        private void AdminCleanup_Click(object sender, RoutedEventArgs e)
            => ShowCleanupPage(new AdminCleanupDialog());

        private void RamCleanup_Click(object sender, RoutedEventArgs e)
            => ShowCleanupPage(new RamOptionsDialog());

        public void ShowAreYouSure_Click(object sender, RoutedEventArgs e)
        {
            var opts = PreferencesManager.ToBoostOptions();

            // Before anything else looks at them, including the "nothing chosen" test and
            // the confirmation list. Otherwise an unelevated run would list admin steps it
            // was about to attempt and could not complete - and would decide it had
            // something to do on the strength of them.
            if (!_isElevated) opts.ClearAdminActions();

            bool nothingChosen =
                !opts.CleanUserTemp && !opts.CleanBrowserCache && !opts.CleanDownloadsFolder &&
                !opts.CleanRecent && !opts.CleanWindowsTemp && !opts.CleanCrashDumps &&
                !opts.CleanDNSCache && !opts.CleanOldWindows && !opts.CleanRecycleBin &&
                !opts.BoostRam;

            if (nothingChosen)
            {
                MessageBox.Show(
                    "Nothing has been chosen for cleanup or boost!",
                    "No Options Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RestoreMainWindow();
                return;
            }

            WindowState = WindowState.Minimized;
            Hide();

            var confirm = new AreYouSure(opts);
            if (confirm.ShowDialog() != true)
            {
                RestoreMainWindow();
                return;
            }

            // Was derived from the licence, which meant a licensed but NON-elevated user
            // was passed isAdmin:true and the engine attempted admin-only operations.
            // Admin capability is elevation, so use the real thing.
            bool isAdminMode = _isElevated;

            if (ProgressDialog.Instance?.CleanupSummaryReady == true)
            {
                var dlg2 = new SuccessDialog(
                    CleanupHelper.TotalFilesDeleted,
                    CleanupHelper.TotalFoldersDeleted,
                    CleanupHelper.LastUsedRamMB,
                    CleanupHelper.TotalBytesFreed)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg2.ShowDialog();
            }

            ProgressDialog progressDialog = null;
            Dispatcher.Invoke(() =>
            {
                progressDialog = ProgressDialog.Instance ?? new ProgressDialog { Owner = this };
                if (!progressDialog.IsVisible)
                    progressDialog.Show();
            });

            Task.Run(() => CleanupHelper.ExecuteCleanup(opts, isAdmin: isAdminMode));
        }

        public void RestoreMainWindow()
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
            });
        }
        public void ToggleOverlayFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // If an overlay exists and is visible -> close it
                    if (_overlayInstance != null && _overlayInstance.IsVisible)
                    {
                        _overlayInstance.Close();
                        _overlayInstance = null;
                        return;
                    }

                    // If an overlay object exists but was hidden / not visible, discard and recreate
                    if (_overlayInstance != null && !_overlayInstance.IsVisible)
                    {
                        try { _overlayInstance.Close(); } catch { }
                        _overlayInstance = null;
                    }

                    // Create & show new overlay
                    PreferencesManager.LoadPreferences();
                    _resourceMonitor ??= new ResourceMonitorManager(1.0);
                    _resourceMonitor.ResourceUpdated -= OnResourceUpdated; // avoid duplicate
                    _resourceMonitor.ResourceUpdated += OnResourceUpdated;
                    _resourceMonitor.Start();

                    _overlayInstance = new OverlayWindow();
                    _overlayInstance.Closed += (_, __) =>
                    {
                        _overlayInstance = null;
                    };

                    // Seed overlay with latest stats
                    var snap = _resourceMonitor.LatestSnapshot;
                    float ramUsage = snap?.RamPercentUsed ?? 0f;
                    int threshold = PreferencesManager.GetAutoThreshold();
                    bool autoEn = PreferencesManager.GetAutoRamEnabled();
                    string lastMsg = PreferencesManager.GetLastRamBoostMessage();
                    int triggers = PreferencesManager.GetAutoTriggerCount();

                    string topProcess = "None";
                    try
                    {
                        var topProc = Process.GetProcesses()
                            .Where(p => !string.IsNullOrEmpty(p.ProcessName)
                                     && p.ProcessName != "Idle"
                                     && p.ProcessName != "System"
                                     && p.ProcessName != "Memory Compression")
                            .OrderByDescending(p =>
                            {
                                try { return p.WorkingSet64; } catch { return 0; }
                            })
                            .FirstOrDefault();

                        if (topProc != null)
                            topProcess = $"{topProc.ProcessName} ({topProc.WorkingSet64 / (1024 * 1024)} MB)";
                    }
                    catch { }

                    string status = (ramUsage >= threshold && autoEn) ? "Waiting" : "Idle";
                    _overlayInstance.UpdateOverlay(ramUsage, threshold, autoEn, lastMsg, triggers, topProcess, status);
                    _overlayInstance.Show();
                    _overlayInstance.Activate();
                }
                catch (Exception ex)
                {
                    LogHelper.Log("ToggleOverlayFromTray exception: " + ex);
                    App.ShowTrayNotification("Failed to toggle overlay.");
                }
            });
        }

        public void ShowAreYouSureAndClean() => ShowAreYouSure_Click(this, null);

        private void TrayButton_Click(object sender, RoutedEventArgs e) => Hide();

        // ---- Right-click menu ----------------------------------------------------
        // Mirrors the tray menu so the same options are reachable from the window.
        // This is also the settings surface - there is deliberately no Settings button.

        // Every Menu_*_Click handler that used to live here is gone. They existed only to
        // service the ContextMenu written out in MainWindow.xaml; that menu is now built
        // from Helpers\AppMenu.cs, which carries the actions itself so the tray renders
        // the same behaviour and not a second copy of it.

        /// <summary>
        /// Rebuilds the right-click menu each time it opens, from the shared definition
        /// in AppMenu that the tray icon also uses.
        ///
        /// Rebuilding rather than caching is what retires SyncThemeMenuChecks: the
        /// Appearance ticks, the conditional entries and the tooltips are all read at
        /// open time, so they cannot fall out of step with the state they report.
        /// </summary>
        private void MainWindow_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
        {
            ContextMenu = AppMenu.BuildWpf(AppMenu.Host.Window);
            ContextMenu.PlacementTarget = this;
            ContextMenu.IsOpen = true;
            e.Handled = true;
        }

        /// <summary>Opens the shared menu from the "Cleanup options..." entry.</summary>
        // The menu (window and tray alike) opens the same three pages this window does, so
        // they go through the same method and get the same refresh afterwards. Replaces
        // ShowCleanupOptions(), which opened the dialog-of-buttons that no longer exists.
        public void ShowBasicCleanup() => ShowCleanupPage(new BasicCleanupDialog());
        public void ShowAdminCleanup() => ShowCleanupPage(new AdminCleanupDialog());
        public void ShowRamCleanup() => ShowCleanupPage(new RamOptionsDialog());

        // ---- Automatic RAM boosting, on the main window -------------------------

        /// <summary>
        /// TRUE until preferences have been read in.
        ///
        /// Defaults to true, not false. The slider's Minimum is 60, so creating it coerces
        /// its value from 0 to 60 and raises ValueChanged during InitializeComponent -
        /// before anything has loaded. A guard that starts false lets that write the
        /// control defaults over the user's settings, which is exactly what RAM options
        /// did until it was fixed: opening the window switched automatic boosting off and
        /// left 60, the slider's minimum, in the file.
        /// </summary>
        private bool _loadingBoost = true;

        private void LoadBoostSettings()
        {
            _loadingBoost = true;
            try
            {
                var ram = PreferencesManager.Current.Ram;
                AutoBoostCheck.IsChecked = ram.AutoRam;
                AutoBoostSlider.Value = Math.Max(AutoBoostSlider.Minimum,
                                        Math.Min(AutoBoostSlider.Maximum, ram.AutoThreshold));
            }
            finally { _loadingBoost = false; }

            UpdateThresholdText();
            RefreshBoostState();
            RefreshAdminNote();
        }

        /// <summary>
        /// Says on the main window that the Admin page needs elevation, so that is known
        /// before opening it rather than by being told afterwards by a dialog.
        ///
        /// Read from the process's real elevation, not from a stored preference - there is
        /// nothing to keep in sync and nothing that can drift.
        /// </summary>
        private void RefreshAdminNote()
        {
            AdminNeedsElevationNote.Visibility = UacHelper.IsRunningAsAdmin()
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void SaveBoostSettings()
        {
            if (_loadingBoost) return;
            var ram = PreferencesManager.Current.Ram;
            ram.AutoRam = AutoBoostCheck.IsChecked == true;
            ram.AutoThreshold = (int)AutoBoostSlider.Value;
            ram.Remember = true;
            PreferencesManager.SavePreferences();
        }

        /// <summary>Greys the threshold when nothing automatic will use it, and reports a hold-off.</summary>
        private void RefreshBoostState()
        {
            bool on = AutoBoostCheck.IsChecked == true;
            ThresholdRow.IsEnabled = on;
            NoBoostListButton.IsEnabled = on;

            RefreshBlockedNote(cached: false);
        }

        /// <summary>
        /// The "paused" note under the threshold.
        ///
        /// <paramref name="cached"/> picks which question to ask. A user action - opening
        /// the window, closing the no-boost list, ticking the box - wants the immediate
        /// answer. The once-a-second tick wants the cached one, because the uncached
        /// version lists every running process.
        /// </summary>
        private void RefreshBlockedNote(bool cached)
        {
            if (AutoBoostBlockedNote == null) return;

            string heldOff = AutoBoostCheck.IsChecked == true
                ? (cached ? Tools.ToolRegistry.AutomaticMaintenanceHeldOffCached()
                          : Tools.ToolRegistry.AutomaticMaintenanceHeldOff())
                : null;

            // The summary already names the list and says it is paused, so there is no
            // prefix here. It reads the same in the overlay, in RAM options and in the
            // notification - one sentence, written once.
            AutoBoostBlockedNote.Text = heldOff ?? "";
            AutoBoostBlockedNote.Visibility = heldOff == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AutoBoost_Changed(object sender, RoutedEventArgs e)
        {
            SaveBoostSettings();
            RefreshBoostState();

            // Switching it ON is the moment a block matters: without this the user turns
            // automatic boosting on with a listed program running and finds out weeks later
            // that it never fired once.
            if (_loadingBoost || AutoBoostCheck.IsChecked != true) return;
            string heldOff = Tools.ToolRegistry.AutomaticMaintenanceHeldOff();
            if (heldOff != null)
                App.ShowTrayNotification($"{heldOff}. Manual boosts still work.");
        }

        /// <summary>
        /// The note names the no-boost list, so clicking it opens the no-boost list.
        /// Mikie's point: telling somebody a list they set up months ago is pausing
        /// something, and then making them go and find it, is half an answer.
        /// </summary>
        private void AutoBoostBlockedNote_Click(object sender, MouseButtonEventArgs e)
            => NoBoostListButton_Click(sender, e);

        private void AutoBoostThreshold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateThresholdText();
            SaveBoostSettings();
        }

        private static double _totalRamGb;

        /// <summary>
        /// "65%  (41 of 64 GB)". The percentage alone does not say whether a setting is
        /// sensible - the same number is a hair-trigger on 8 GB and unreachable on 64.
        /// </summary>
        private void UpdateThresholdText()
        {
            if (ThresholdText == null) return;
            int percent = (int)AutoBoostSlider.Value;

            if (_totalRamGb <= 0)
            {
                try
                {
                    _totalRamGb = new Microsoft.VisualBasic.Devices.ComputerInfo()
                                      .TotalPhysicalMemory / 1024.0 / 1024 / 1024;
                }
                catch { _totalRamGb = 0; }
            }

            ThresholdText.Text = _totalRamGb > 0
                ? $"{percent}%  ({_totalRamGb * percent / 100:F0} of {_totalRamGb:F0} GB)"
                : $"{percent}%";
        }

        /// <summary>
        /// Runs the checks and shows what they found.
        ///
        /// On the UI thread deliberately: the four checks take well under a second here
        /// (WMI is the slow part and the results are cached for the run), and a spinner
        /// plus a cancellation path would be more machinery than the wait justifies. If a
        /// check is ever added that has to wait on something, this is where that changes -
        /// and the button is disabled meanwhile so a second run cannot be started on top.
        /// </summary>
        private void SanityCheck_Click(object sender, RoutedEventArgs e)
        {
            SanityCheckButton.IsEnabled = false;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var report = SanityCheck.SanityRunner.Run();
                Mouse.OverrideCursor = null;

                new SanityCheckDialog(report)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }.ShowDialog();
            }
            catch (Exception ex)
            {
                LogHelper.Log("Sanity Check failed to run: " + ex);
                CustomMessageBox.Show(
                    "The checks could not be run: " + ex.Message,
                    "Sanity Check", CustomMessageBox.Kind.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                SanityCheckButton.IsEnabled = true;
                RefreshSanityCheckSummary();
            }
        }

        private void SanityChecksList_Click(object sender, RoutedEventArgs e)
        {
            new SanityCheckOptionsDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            }.ShowDialog();

            RefreshSanityCheckSummary();
        }

        /// <summary>
        /// The line beside the button, from the recorded result of the last run.
        ///
        /// "Never run" and "ran and found nothing" must not read the same. They look
        /// identical if you only print a count, and they mean opposite things - which is
        /// the founding complaint of this entire feature.
        /// </summary>
        private void RefreshSanityCheckSummary()
        {
            var last = SanityCheck.SanityRunner.LastRun();
            if (last?.LastRunLocal == null)
            {
                SanityCheckSummary.Text = "Not run yet.";
                return;
            }

            string when = DescribeAge(DateTime.Now - last.LastRunLocal.Value);
            SanityCheckSummary.Text = last.LastFindingCount == 0
                ? $"Last run {when}: nothing odd found."
                : last.LastFindingCount == 1
                    ? $"Last run {when}: 1 thing looks inconsistent."
                    : $"Last run {when}: {last.LastFindingCount} things look inconsistent.";
        }

        private static string DescribeAge(TimeSpan age)
        {
            if (age.TotalMinutes < 1) return "just now";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} min ago";
            if (age.TotalHours < 24) return $"{(int)age.TotalHours} hr ago";
            return age.TotalDays < 2 ? "yesterday" : $"{(int)age.TotalDays} days ago";
        }

        private void NoBoostListButton_Click(object sender, RoutedEventArgs e)
        {
            new ManageGamesDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            }.ShowDialog();

            // The list may have changed what is held off, and the cache the tray uses is
            // ten seconds stale by design.
            Tools.ToolRegistry.InvalidateCache();
            RefreshBoostState();
        }

        // Window_MouseLeftButtonDown/DragMove and ExitApp removed in 2.0. They existed
        // only because WindowStyle="None" meant the window had no title bar to drag and
        // no system close button, so both had to be hand-rolled. The real chrome
        // provides dragging, minimise and close.

        private void OpenLogFolder(object sender, EventArgs e) => LogManager.OpenLogFolder();

        private void ShowAbout(object sender, RoutedEventArgs e) =>
            new AboutWindow { Owner = this }.ShowDialog();
    }
}
