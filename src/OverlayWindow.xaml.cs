// File: OverlayWindow.xaml.cs
using System;
using System.Diagnostics;
using System.Linq;
using System.Printing;
// `using System.Management.Instrumentation` used to sit here. It has no .NET 8
// equivalent and was flagged as a migration blocker - but no type from that
// namespace was ever used. The printer panel below is System.Printing
// (LocalPrintServer/PrintQueue), which ships with the WPF desktop runtime.
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Drawing.Printing;
using System.Windows.Documents;
using SystemOptimizer.Core.Cleanup;
using SystemOptimizer.Core.Monitoring;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Platform;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer
{
    public partial class OverlayWindow : Window
    {
        /// <summary>
        /// Resolve a palette brush by key. Used instead of literal Brushes.* so the
        /// overlay's status colours follow the active theme.
        /// </summary>
        private static System.Windows.Media.Brush Brush(string key)
            => System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush
               ?? System.Windows.Media.Brushes.Gray;

        public static OverlayWindow Instance { get; private set; }
        private System.Windows.Point _lastMousePos = new System.Windows.Point(double.NaN, double.NaN);
        private double _mouseDistancePixelsToday = 0;
        private DateTime _mouseDistanceDate = DateTime.Today;
        private Timer _timer;
        private bool _isClickThrough;
        private bool _suppressOpacitySlider = true;

        /// <summary>
        /// The overlay handles the same Ctrl+Shift+Alt hotkeys as MainWindow. Without
        /// this, Ctrl+Shift+Alt+O only toggled the overlay while the MAIN window had
        /// focus - so clicking the overlay made its own hotkey stop working.
        /// </summary>
        private void OverlayWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            const ModifierKeys required = ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt;
            if ((Keyboard.Modifiers & required) != required) return;

            switch (e.Key)
            {
                case Key.O:                       // toggle this overlay off
                    Close();
                    e.Handled = true;
                    break;

                case Key.S:
                    App.ShowLastRamResult();
                    e.Handled = true;
                    break;

                case Key.L:
                    LogManager.OpenLogFolder();
                    e.Handled = true;
                    break;

                case Key.B:
                    try { CleanupHelper.RunRamOnlyQuickTrim(UacHelper.IsRunningAsAdmin()); }
                    catch (Exception ex) { LogHelper.Log("Overlay hotkey B exception: " + ex); }
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// How long ago, in the largest unit that is still honest.
        ///
        /// "3 days ago" rather than a timestamp: the question this line answers is whether
        /// the last clean is stale, and a date makes the reader do that arithmetic
        /// themselves every time they glance at it.
        /// </summary>
        private static string Ago(DateTime when)
        {
            var span = DateTime.Now - when;
            if (span < TimeSpan.Zero) return "just now";          // clock changed under us
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hr ago";
            int days = (int)span.TotalDays;
            return days == 1 ? "yesterday" : $"{days} days ago";
        }

        private static string FormatSize(long bytes)
        {
            const double KB = 1024.0, MB = KB * 1024, GB = MB * 1024;
            if (bytes >= GB) return $"{bytes / GB:F2} GB";
            if (bytes >= MB) return $"{bytes / MB:F0} MB";
            if (bytes >= KB) return $"{bytes / KB:F0} KB";
            return $"{bytes} B";
        }

        public void CaptureOverlayToClipboard()
        {
            var renderTarget = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(this);
            Clipboard.SetImage(renderTarget);
        }

        private void UpdateMouseDistance()
        {
            var pos = System.Windows.Forms.Control.MousePosition;
            var screenPoint = new System.Windows.Point(pos.X, pos.Y);
            if (!double.IsNaN(_lastMousePos.X) && !double.IsNaN(_lastMousePos.Y) && DateTime.Today == _mouseDistanceDate)
            {
                double dx = screenPoint.X - _lastMousePos.X;
                double dy = screenPoint.Y - _lastMousePos.Y;
                _mouseDistancePixelsToday += Math.Sqrt(dx * dx + dy * dy);
            }
            else if (DateTime.Today != _mouseDistanceDate)
            {
                _mouseDistancePixelsToday = 0;
                _mouseDistanceDate = DateTime.Today;
            }
            _lastMousePos = screenPoint;
            double miles = _mouseDistancePixelsToday / 96.0 / 63360.0;
            if (MouseDistanceText != null)
                MouseDistanceText.Text = $"Mouse Travel: {miles:F3} miles";
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this);
            if (!double.IsNaN(_lastMousePos.X) && !double.IsNaN(_lastMousePos.Y))
            {
                var dx = pos.X - _lastMousePos.X;
                var dy = pos.Y - _lastMousePos.Y;
                _mouseDistancePixelsToday += Math.Sqrt(dx * dx + dy * dy);
            }
            _lastMousePos = pos;
        }

        private void CheckMouseDistanceReset(object sender, EventArgs e)
        {
            if (DateTime.Today != _mouseDistanceDate)
            {
                _mouseDistancePixelsToday = 0;
                _mouseDistanceDate = DateTime.Today;
            }
        }

        public OverlayWindow()
        {
            Instance = this;
            InitializeComponent();
            PreferencesManager.LoadPreferences();
            _suppressOpacitySlider = true;

            this.MouseMove += Overlay_MouseMove;
            CompositionTarget.Rendering += CheckMouseDistanceReset;

            this.Loaded += OverlayWindow_Loaded;
            LoadWindowPosition();
            LoadOverlaySettings();

            this.LocationChanged += OverlayWindow_LocationOrSizeChanged;
            this.SizeChanged += OverlayWindow_LocationOrSizeChanged;

            _timer = new Timer(1500);
            _timer.Elapsed += (s, e) =>
            {
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    try { Dispatcher.Invoke(() => UpdateStats()); }
                    catch { }
                }
            };
            _timer.Start();

            _suppressOpacitySlider = false;
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // A first-run "hold Ctrl and scroll to zoom" tooltip was here, and an almost
            // identical one in OnContentRendered - two copies with different wording, both
            // gated on the same flag, so only whichever ran first ever appeared. Both are
            // gone, along with the flag that remembered them.
            //
            // They were also redundant: the overlay carries a standing line at the bottom
            // saying where the display options are and that Ctrl+scroll resizes the text.
            // A hint that is always visible beats one shown once and never again, which
            // arrives before the reader has any use for it and cannot be recalled.
            ApplyOverlayFieldVisibility();
        }

        /// <summary>
        /// The single place a row's visibility is decided from the user's choices.
        ///
        /// There were two of these - this one, and a fifteen-row copy that omitted the RAM
        /// badge, mouse travel, printer status, laptop battery and Wi-Fi. Which ran
        /// depended on the route in, so a tick could take effect or not depending on how
        /// the overlay had been refreshed. One method now, called from every route.
        /// </summary>
        private void ApplyOverlayFieldVisibility()
        {
            var prefs = PreferencesManager.Current.OverlayFields;

            CpuRow.Visibility = prefs.Cpu ? Visibility.Visible : Visibility.Collapsed;
            RamRow.Visibility = prefs.Ram ? Visibility.Visible : Visibility.Collapsed;
            DiskRow.Visibility = prefs.Disk ? Visibility.Visible : Visibility.Collapsed;
            NetworkRow.Visibility = prefs.Network ? Visibility.Visible : Visibility.Collapsed;
            // WifiROW, not WifiText. Collapsing only the text left the row's icon sitting
            // there with nothing beside it - the third time this application has hidden a
            // TextBlock and left its StackPanel drawing a bullet in empty space. The
            // duplicate method deleted above collapsed the row correctly; this one, the
            // fuller of the two, had the bug. Hide the ROW.
            WifiRow.Visibility = prefs.Wifi ? Visibility.Visible : Visibility.Collapsed;
            BatteryRow.Visibility = prefs.Battery ? Visibility.Visible : Visibility.Collapsed;
            PagefileRow.Visibility = prefs.Pagefile ? Visibility.Visible : Visibility.Collapsed;
            AppCountRow.Visibility = prefs.AppCount ? Visibility.Visible : Visibility.Collapsed;
            UptimeRow.Visibility = prefs.Uptime ? Visibility.Visible : Visibility.Collapsed;
            WindowsVersionRow.Visibility = prefs.WindowsVersion ? Visibility.Visible : Visibility.Collapsed;
            ArchRow.Visibility = prefs.Arch ? Visibility.Visible : Visibility.Collapsed;
            UserRow.Visibility = prefs.User ? Visibility.Visible : Visibility.Collapsed;
            MachineRow.Visibility = prefs.Machine ? Visibility.Visible : Visibility.Collapsed;
            // No BootRow any more: the Boot toggle is honoured inside the combined uptime
            // line, which shows the duration, the timestamp, or both.
            CDriveRow.Visibility = prefs.CDrive ? Visibility.Visible : Visibility.Collapsed;
            GpuRow.Visibility = prefs.Gpu ? Visibility.Visible : Visibility.Collapsed;
            RamHeroRow.Visibility = prefs.ShowRamBadge ? Visibility.Visible : Visibility.Collapsed;
            TopProcessRow.Visibility = prefs.ShowTopProcess ? Visibility.Visible : Visibility.Collapsed;
            LastBoostRow.Visibility = prefs.ShowLastBoost ? Visibility.Visible : Visibility.Collapsed;
            ThresholdRow.Visibility = prefs.ShowThreshold ? Visibility.Visible : Visibility.Collapsed;
            MouseDistanceRow.Visibility = prefs.ShowMouseDistance ? Visibility.Visible : Visibility.Collapsed;
            PrinterStatusList.Visibility = prefs.ShowPrinterStatus ? Visibility.Visible : Visibility.Collapsed;
            BatteryHealthRow.Visibility = prefs.ShowLaptopBattery ? Visibility.Visible : Visibility.Collapsed;
        }


        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                const double step = 0.1, min = 0.5, max = 3.0;
                var border = (Border)this.Content;
                var scale = (ScaleTransform)border.LayoutTransform;
                double zoom = scale.ScaleX + (e.Delta > 0 ? step : -step);
                zoom = Math.Max(min, Math.Min(max, zoom));
                scale.ScaleX = scale.ScaleY = zoom;
                e.Handled = true;
            }
        }

        private void OverlayWindow_LocationOrSizeChanged(object sender, EventArgs e)
        {
            PreferencesManager.SaveOverlayPosition(new Rect(Left, Top, Width, Height));
        }

        /// <summary>
        /// Brings every open overlay up to date after a RAM boost, whoever performed it.
        ///
        /// This used to be a private method inside CleanupHelper, called by the two MANUAL
        /// boost paths and by nothing else. The automatic path recorded the boost and then
        /// pushed only the trigger count, so "Last Boost" kept showing the previous figure
        /// until something unrelated happened to refresh it - the one boost the user did
        /// not perform themselves was the one the overlay did not report.
        ///
        /// It lives on the overlay now because that is what it knows about, and because
        /// Core.Ram should not have to reach into the cleanup engine to redraw a label.
        /// </summary>
        public static void RefreshAllAfterRamBoost()
        {
            try
            {
                var overlays = System.Windows.Application.Current?.Windows
                    .OfType<OverlayWindow>()
                    .ToList();
                if (overlays == null || overlays.Count == 0) return;

                foreach (var overlay in overlays)
                {
                    try
                    {
                        overlay.Dispatcher.Invoke(() =>
                        {
                            if (!overlay.IsVisible) return;
                            if (overlay.LastBoostText != null)
                                overlay.LastBoostText.Text =
                                    $"Last Boost: {PreferencesManager.GetLastRamBoostMessage()}";
                            overlay.UpdateStats();
                        });
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Log("Overlay update exception: " + ex);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// The trigger count lives in the auto-boost status line now, which UpdateStats
        /// rebuilds from preferences - so there is no number to push in. Kept because
        /// AutoRamMonitorHelper calls it immediately after a boost, and refreshing at that
        /// moment is still worth doing.
        /// </summary>
        public void SetAutoTriggerCount(int count) => UpdateStats();

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // The second copy of the one-time zoom tip was here. See OverlayWindow_Loaded.

            _suppressOpacitySlider = true;
            LoadOverlaySettings();
            _suppressOpacitySlider = false;

            // Refresh stat visibility in case settings changed after construction
            ApplyOverlayFieldVisibility();
        }

        private void LoadOverlaySettings()
        {
            double opacity = PreferencesManager.GetOverlayOpacity();
            this.Opacity = opacity;
            OpacitySlider.Value = opacity;

            bool alwaysOnTop = PreferencesManager.GetOverlayAlwaysOnTop();
            AlwaysOnTopCheckBox.IsChecked = alwaysOnTop;
            this.Topmost = alwaysOnTop;

            bool clickThrough = PreferencesManager.GetOverlayClickThrough();
            ClickThroughCheckBox.IsChecked = clickThrough;
            ApplyClickThrough(clickThrough);
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressOpacitySlider) return;
            double newOpacity = OpacitySlider.Value;
            this.Opacity = newOpacity;
            PreferencesManager.SetOverlayOpacity(newOpacity);
        }
        private void AlwaysOnTopCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isChecked = AlwaysOnTopCheckBox.IsChecked == true;
            this.Topmost = isChecked;
            PreferencesManager.SetOverlayAlwaysOnTop(isChecked);
        }
        private void ClickThroughCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool enable = ClickThroughCheckBox.IsChecked == true;
            PreferencesManager.SetOverlayClickThrough(enable);
            if (enable)
            {
                App.ShowTrayNotification(
                  "Click Through Mode Enabled – Overlay is now non-interactive.\n" +
                  "To disable, right-click the tray icon and choose 'Disable Click Through Mode'.");
            }
            else
            {
                App.ShowTrayNotification(
                  "Click Through Mode Disabled – Overlay is now interactive again.");
            }
            App.RefreshTrayClickThroughItem();
            // apply style change a bit later to avoid focus issues
            if (enable)
            {
                Dispatcher.BeginInvoke(new Action(() => ApplyClickThrough(true)),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                ApplyClickThrough(false);
            }
        }
        private void ApplyClickThrough(bool enable)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enable)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE,
                    style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
                this.IsHitTestVisible = false;
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE,
                    style & ~WS_EX_TRANSPARENT);
                this.IsHitTestVisible = true;
            }
            _isClickThrough = enable;
        }
        public void UpdateStats()
        {
            var stats = SystemStatsHelper.GetStats();
            UpdateMouseDistance();

            // --- CPU USAGE ---
            CpuUsageText.Text = $"CPU: {stats.CpuName} ({stats.CpuUsage:F1}% / {stats.CpuCoreCount} cores)";
            CpuUsageText.Foreground =
                stats.CpuUsage > 90 ? Brush("ErrorBrush") :
                stats.CpuUsage > 70 ? Brush("WarningBrush") :
                                      Brush("TextPrimaryBrush");

            // --- RAM ---
            RamText.Text = $"RAM Used: {stats.RamUsedMB:F0} MB ({stats.RamPercent:F1}%)";
            RamText.Foreground =
                stats.RamPercent < 85 ? Brush("SuccessBrush") :
                stats.RamPercent < 95 ? Brush("WarningBrush") :
                                        Brush("ErrorBrush");

            // --- DISK ---
            DiskReadText.Text = $"{stats.DiskReadMBps:F1} MB/s";
            DiskReadText.Foreground = Brush("TextPrimaryBrush");
            DiskWriteText.Text = $"{stats.DiskWriteMBps:F1} MB/s";
            DiskWriteText.Foreground = Brush("TextPrimaryBrush");

            // --- ETHERNET ---
            string ethernetStatus = "Disconnected";
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var liveEth = interfaces.FirstOrDefault(ni =>
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet &&
                    ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
                if (liveEth != null)
                    ethernetStatus = "Connected";
            }
            catch { }
            NetworkText.Text = $"Ethernet: {ethernetStatus} ↑ {stats.NetUpMBps:F2} | ↓ {stats.NetDownMBps:F2} MB/s";
            NetworkText.Foreground = ethernetStatus == "Connected" ? Brush("TextPrimaryBrush") : Brush("WarningBrush");

            // --- WIFI ---
            if (WifiText != null)
            {
                WifiText.Text = $"Wi-Fi: {stats.WifiSsid}    ↑ {stats.WifiUpMBps:F2} | ↓ {stats.WifiDownMBps:F2} MB/s";
                WifiText.Foreground = Brush("TextPrimaryBrush");
            }

            // --- BATTERY ---
            if (stats.BatteryPercent >= 0)
            {
                BatteryText.Text = $"Battery @ {stats.BatteryPercent:F0}%";
                BatteryText.Foreground =
                    stats.BatteryPercent < 20 ? Brush("ErrorBrush") :
                    stats.BatteryPercent < 40 ? Brush("WarningBrush") :
                                                 Brush("TextPrimaryBrush");
            }
            else
            {
                BatteryText.Text = "Battery: N/A";
                BatteryText.Foreground = Brush("TextPrimaryBrush");
            }

            // --- PAGEFILE ---
            PagefileText.Text = $"Pagefile Used: {stats.PagefilePercent:F1}%";
            PagefileText.Foreground =
                stats.PagefilePercent > 90 ? Brush("WarningBrush") : Brush("TextPrimaryBrush");

            // --- APP COUNT ---
            AppCountText.Text = $"Applications Running: {stats.AppCount}";
            AppCountText.Foreground = Brush("TextPrimaryBrush");

            // --- UPTIME, WITH BOOT TIME FOLDED IN ---
            //
            // These were two rows saying one thing: uptime is arithmetic on boot time.
            // They share a line now, and BOTH TOGGLES STILL WORK - uptime alone gives the
            // duration, boot alone gives the timestamp, together they give both. Folding
            // two lines into one should not quietly cost somebody a setting they chose.
            var fields = PreferencesManager.Current?.OverlayFields;
            bool wantUptime = fields?.Uptime != false;
            bool wantBoot = fields?.Boot != false;

            UptimeText.Text =
                wantUptime && wantBoot ? $"Uptime: {stats.Uptime} (since {stats.BootTime})" :
                wantUptime ? $"Uptime: {stats.Uptime}" :
                wantBoot ? $"Booted: {stats.BootTime}" :
                             "";
            UptimeRow.Visibility = (wantUptime || wantBoot) ? Visibility.Visible : Visibility.Collapsed;

            if (TimeSpan.TryParse(stats.Uptime, out var up) && up.TotalDays > 7)
                UptimeText.Foreground = Brushes.LightBlue;
            else
                UptimeText.Foreground = Brush("TextPrimaryBrush");

            // --- SYSTEM INFO ---
            if (WindowsVersionText != null)
            {
                WindowsVersionText.Text = $"Windows: {stats.WindowsVersion}";
                WindowsVersionText.Foreground = Brush("TextPrimaryBrush");
            }
            if (ArchText != null)
            {
                ArchText.Text = $"Architecture: {stats.SystemArch}";
                ArchText.Foreground = Brush("TextPrimaryBrush");
            }
            if (UserText != null)
            {
                UserText.Text = $"User Name: {stats.CurrentUser}";
                UserText.Foreground = Brush("TextPrimaryBrush");
            }
            if (MachineText != null)
            {
                MachineText.Text = $"PC Name: {stats.MachineName}";
                MachineText.Foreground = Brush("TextPrimaryBrush");
            }

            // --- C: DRIVE ---
            if (CDriveText != null)
            {
                CDriveText.Text = $"C:\\ Free: {stats.CDriveFreeGB:F1}GB / {stats.CDriveTotalGB:F1}GB";
                var usedPct = stats.CDriveTotalGB > 0
                    ? 100 - (stats.CDriveFreeGB / stats.CDriveTotalGB * 100)
                    : 0;
                CDriveText.Foreground =
                    usedPct > 90 ? Brush("ErrorBrush") :
                    usedPct > 80 ? Brush("WarningBrush") :
                                   Brush("TextPrimaryBrush");
            }

            // --- GPU ---
            if (GpuNameText != null)
            {
                GpuNameText.Text = $"GPU: {stats.GpuName}";
                GpuNameText.Foreground = Brush("TextPrimaryBrush");
            }

            // --- LAST BOOST ---
            if (LastBoostText != null)
            {
                LastBoostText.Text = $"Last Boost: {PreferencesManager.GetLastRamBoostMessage()}";
                LastBoostText.Foreground = Brush("SuccessBrush");
            }

            // --- LAST CLEAN ---
            if (LastCleanRow != null)
            {
                var last = Core.Cleanup.CleanHistory.LastCleanSummary();
                if (PreferencesManager.Current?.OverlayFields?.ShowLastClean == true &&
                    last.HasValue && last.Value.Files > 0)
                {
                    LastCleanRow.Visibility = Visibility.Visible;

                    // "Waiting in the bin" stops being true the moment the bin is emptied,
                    // and this line would otherwise go on claiming that eleven thousand
                    // files are sitting there recoverable when they have been destroyed.
                    // A standing readout that quietly outlives its own fact is exactly
                    // what the frozen threshold line was.
                    //
                    // What is STILL THERE, not what was moved. Deleting some of them by
                    // hand used to leave this line reporting the original figure for ever.
                    // The count comes from intersecting the run's manifest with the bin's
                    // own index, recomputed on a background thread and only when the bin
                    // actually changes - see CleanHistory.LastCleanStillInBin.
                    var remaining = Core.Cleanup.CleanHistory.LastCleanStillInBin();

                    if (remaining.HasValue && remaining.Value.Files == 0)
                    {
                        // Nothing of that run survives, whether the bin was emptied or the
                        // files were picked out one by one.
                        LastCleanText.Text = $"Last clean: {last.Value.Files:N0} files, none left in the bin";
                    }
                    else
                    {
                        // Until the first background pass finishes there is no measured
                        // answer, so the manifest total stands in. It is the right number
                        // the moment a clean finishes, which is when this line matters
                        // most and when nothing has yet been taken out by hand.
                        var shown = remaining ?? (last.Value.Files, last.Value.Bytes);
                        LastCleanText.Text =
                            $"Last clean: {shown.Files:N0} files waiting in the bin, " +
                            $"{FormatSize(shown.Bytes)} to reclaim";
                    }
                }
                else
                {
                    LastCleanRow.Visibility = Visibility.Collapsed;
                }
            }

            // --- AUTO RAM: THRESHOLD, ENABLED, TRIGGER COUNT ---
            //
            // These three were written ONLY by UpdateOverlay, which runs when the overlay
            // is opened from the main window - so they showed whatever was true at that
            // moment and never changed again. Mikie moved the threshold from 70% to 40%
            // on the main window and the overlay carried on reporting 70% indefinitely.
            //
            // A readout that updates only on open is worse than no readout: it does not
            // look stale, it looks current and wrong.
            var ram = PreferencesManager.Current?.Ram;
            if (ram != null)
            {
                if (ThresholdText != null) ThresholdText.Text = $"Threshold: {ram.AutoThreshold}%";

                // The "Auto Enabled: True" row is deleted from the XAML entirely. The
                // status line below says whether it is on AND says something useful
                // beside it. Collapsing just its TEXT, which is what I did first, left
                // the row and its icon behind - a bullet with nothing next to it.
            }

            // --- AUTOMATIC RAM BOOST: ALWAYS SAYS SOMETHING WHILE IT IS ON ---
            //
            // This row used to appear only when a boost was being held off, so for the
            // vast majority of the time it was absent - and absence says nothing. Mikie's
            // point: while automatic boosting is switched on, the line should be there,
            // carrying whatever is worth knowing at that moment.
            //
            //   held off      Paused - 'No-boost' list has 4 running programs   (warning)
            //   never run     Auto RAM boost on - has not run yet
            //   has run       Auto RAM boost on - last ran 2 hr ago
            //
            // Switched off, there is nothing to report and the row disappears entirely.
            if (BoostHeldOffRow != null)
            {
                bool show = PreferencesManager.Current?.OverlayFields?.ShowBoostHeldOff == true
                            && PreferencesManager.Current?.Ram?.AutoRam == true;

                if (!show)
                {
                    BoostHeldOffRow.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // The CACHED ask. This runs about once a second and the uncached
                    // version walks the running process list; the tray uses the cached one
                    // for the same reason. Ten seconds sits well inside the sixty-second
                    // automatic boost interval, so it cannot disagree for long.
                    string heldOff = Tools.ToolRegistry.AutomaticMaintenanceHeldOffCached();
                    var lastAuto = PreferencesManager.GetLastAutoBoostTime();

                    // The trigger count is folded in here rather than carrying its own
                    // row. "Auto Triggers: 0" beside "has not run yet" was the same fact
                    // twice, and once it HAS run the count only means anything next to
                    // when it last did.
                    int runs = ram?.AutoTriggerCount ?? 0;
                    string ranWhen = lastAuto.HasValue
                        ? $"Auto RAM boost on - last ran {Ago(lastAuto.Value)}"
                        : "Auto RAM boost on - has not run yet";
                    if (lastAuto.HasValue && runs > 1) ranWhen += $" ({runs} times)";

                    BoostHeldOffRow.Visibility = Visibility.Visible;
                    BoostHeldOffText.Text = heldOff ?? ranWhen;   // heldOff is already a sentence

                    // Amber only while something is actually being held off. Green for the
                    // ordinary "on and working" case, so the warning colour keeps meaning
                    // something rather than being the permanent colour of this line.
                    BoostHeldOffText.SetResourceReference(ForegroundProperty,
                        heldOff != null ? "WarningBrush" : "SuccessBrush");
                    BoostHeldOffIcon.SetResourceReference(ForegroundProperty,
                        heldOff != null ? "WarningBrush" : "SuccessBrush");
                    // Written as escape sequences, not literal glyphs: two separate
                    // encoding incidents in this rebuild came from non-ASCII characters
                    // sitting in source files. E002 is Material's warning, E8F4 its check.
                    BoostHeldOffIcon.Text = heldOff != null ? "\uE002" : "\uE8F4";
                }
            }

            // --- LAST CLEAN, HOW LONG AGO ---
            if (LastCleanAgeRow != null)
            {
                var last = Core.Cleanup.CleanHistory.LastCleanSummary();
                if (PreferencesManager.Current?.OverlayFields?.ShowLastCleanAge == true && last.HasValue)
                {
                    LastCleanAgeRow.Visibility = Visibility.Visible;
                    LastCleanAgeText.Text = $"Last cleaned: {Ago(last.Value.When)}";
                }
                else
                {
                    LastCleanAgeRow.Visibility = Visibility.Collapsed;
                }
            }

            // --- WHAT IS IN THE RECYCLE BIN RIGHT NOW ---
            if (RecycleBinRow != null)
            {
                if (PreferencesManager.Current?.OverlayFields?.ShowRecycleBin == true)
                {
                    var (bytes, items) = Core.Cleanup.RecycleBin.CurrentContents();
                    if (bytes < 0)
                    {
                        // The shell would not answer. Say so rather than print a zero we
                        // did not measure - this line's whole job is to be trusted.
                        RecycleBinRow.Visibility = Visibility.Visible;
                        RecycleBinText.Text = "Recycle Bin: could not be read";
                    }
                    else if (items == 0)
                    {
                        RecycleBinRow.Visibility = Visibility.Visible;
                        RecycleBinText.Text = "Recycle Bin: empty";
                    }
                    else
                    {
                        RecycleBinRow.Visibility = Visibility.Visible;
                        RecycleBinText.Text =
                            $"Recycle Bin: {items:N0} items, {FormatSize(bytes)} total to reclaim";
                    }
                }
                else
                {
                    RecycleBinRow.Visibility = Visibility.Collapsed;
                }
            }

            // --- RAM CLEANING HERO BADGE ---
            if (RamHeroRow != null)
            {
                if (PreferencesManager.Current?.OverlayFields?.ShowRamBadge == true)
                {
                    RamHeroRow.Visibility = Visibility.Visible;
                    double gbFreed = PreferencesManager.Current.TotalRamFreedMB / 1024.0;
                    RamHeroText.Text = $"Total RAM freed: {gbFreed:F1} GB";
                }
                else
                {
                    RamHeroRow.Visibility = Visibility.Collapsed;
                }
            }

            // --- MOUSE DISTANCE TRACKER ---
            if (MouseDistanceRow != null)
            {
                if (PreferencesManager.Current?.OverlayFields?.ShowMouseDistance == true)
                {
                    MouseDistanceRow.Visibility = Visibility.Visible;
                    double miles = _mouseDistancePixelsToday / 96.0 / 63360.0;
                    MouseDistanceText.Text = $"Mouse Travel: {miles:F3} miles";
                }
                else
                {
                    MouseDistanceRow.Visibility = Visibility.Collapsed;
                }
            }

            // --- PRINTER STATUS ---
            if (PreferencesManager.Current.OverlayFields.ShowPrinterStatus)
            {
                if (PrinterStatusList != null)
                {
                    var server = new LocalPrintServer();
                    var queues = server.GetPrintQueues(new[]
                    {
                        EnumeratedPrintQueueTypes.Local,
                        EnumeratedPrintQueueTypes.Connections
                    });

                    var list = queues
                        .Where(q => !q.FullName.Contains("PDF", StringComparison.OrdinalIgnoreCase))
                        .Select(q =>
                        {
                            bool isDefault = q.FullName == server.DefaultPrintQueue.FullName;
                            bool isOffline = (q.QueueStatus & PrintQueueStatus.Offline) != 0;
                            string tag = isOffline ? "Offline" : "Online";
                            if (isDefault) tag = "Default, " + tag;
                            return $"{q.FullName} [{tag}]";
                        })
                        .ToList();

                    if (list.Any())
                    {
                        PrinterStatusList.Visibility = Visibility.Visible;
                        PrinterStatusList.ItemsSource = list;
                    }
                    else
                    {
                        PrinterStatusList.Visibility = Visibility.Collapsed;
                    }
                }
            }
            else
            {
                if (PrinterStatusList != null)
                    PrinterStatusList.Visibility = Visibility.Collapsed;
            }

            // --- BATTERY HEALTH & CYCLE COUNT ---
            if (BatteryHealthRow != null)
            {
                try
                {
                    var searcher = new System.Management.ManagementObjectSearcher(
                        "SELECT DesignCapacity, FullChargeCapacity, BatteriesCharged, CycleCount FROM Win32_Battery");
                    var results = searcher.Get().Cast<System.Management.ManagementObject>().ToList();
                    if (results.Count > 0)
                    {
                        var bat = results.First();
                        var design = Convert.ToDouble(bat["DesignCapacity"] ?? 0);
                        var full = Convert.ToDouble(bat["FullChargeCapacity"] ?? 0);
                        var cycles = Convert.ToInt32(bat["CycleCount"] ?? 0);
                        if (design > 0 && full > 0)
                        {
                            double healthPct = full / design * 100.0;
                            BatteryHealthRow.Visibility = Visibility.Visible;
                            BatteryHealthText.Text = $"Health: {healthPct:F0}%   Cycles: {cycles}";
                        }
                        else
                        {
                            BatteryHealthRow.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        BatteryHealthRow.Visibility = Visibility.Collapsed;
                    }
                }
                catch
                {
                    BatteryHealthRow.Visibility = Visibility.Collapsed;
                }
            }

            // Licence row removed in 2.0 - the product is free.

            if (LastBoostText != null)
                LastBoostText.Text = $"Last Boost: {PreferencesManager.GetLastRamBoostMessage()}";
        }

        public void UpdateOverlay(float ramUsage, int threshold, bool autoEnabled, string lastBoost, int triggerCount, string topProcess, string status)
        {
            TopProcessText.Text = $"Top Process: {topProcess}";
            LastBoostText.Text = $"Last Boost: {PreferencesManager.GetLastRamBoostMessage()}";
            
            ThresholdText.Text = $"Threshold: {PreferencesManager.Current.Ram.AutoThreshold}%";
            var stats = SystemStatsHelper.GetStats();
            if (WifiText != null)
                WifiText.Text = $"Wi-Fi: {stats.WifiSsid}";
        }

        private void LoadWindowPosition()
        {
            var rect = EnsureOnScreen(PreferencesManager.GetOverlayPosition());
            Left = rect.Left;
            Top = rect.Top;
            Width = rect.Width;
            Height = rect.Height;
        }

        /// <summary>
        /// Drags a saved position back onto a real display if it no longer lands on one.
        ///
        /// The position is restored verbatim from preferences, which is fine until the
        /// display it was saved on stops existing - unplug a second monitor, or dock and
        /// undock a laptop, and the overlay reopens at coordinates nobody can see. There
        /// is no visible symptom to work from: Show() succeeds, no exception is raised,
        /// the window genuinely is open, and the only way out is to hand-edit
        /// preferences.json. Anything not overlapping the desktop by at least a corner
        /// gets centred on the primary display instead.
        /// </summary>
        private static Rect EnsureOnScreen(Rect rect)
        {
            const double MinVisible = 80;   // enough of a corner to grab with the mouse

            var desktop = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            double w = rect.Width  > 0 ? rect.Width  : 532;
            double h = rect.Height > 0 ? rect.Height : 652;

            var visible = Rect.Intersect(desktop, new Rect(rect.Left, rect.Top, w, h));
            if (!visible.IsEmpty && visible.Width >= MinVisible && visible.Height >= MinVisible)
                return new Rect(rect.Left, rect.Top, w, h);

            LogHelper.Log($"Overlay position {rect} is off-screen; recentring.");
            return new Rect(
                SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width  - w) / 2,
                SystemParameters.WorkArea.Top  + (SystemParameters.WorkArea.Height - h) / 2,
                w, h);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _timer?.Stop();
            _timer?.Dispose();
            PreferencesManager.SaveOverlayPosition(new Rect(Left, Top, Width, Height));
            PreferencesManager.SetOverlayOpacity(this.Opacity);
            PreferencesManager.SetOverlayAlwaysOnTop(AlwaysOnTopCheckBox.IsChecked == true);
            PreferencesManager.SetOverlayClickThrough(ClickThroughCheckBox.IsChecked == true);
        }

        private void OverlayWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && IsHitTestVisible)
                DragMove();
        }

        public void RefreshOverlayFields() => Dispatcher.Invoke(ApplyOverlayFieldVisibility);

        // A SECOND copy of ApplyOverlayFieldVisibility lived here: fifteen rows where the
        // real one has twenty-one, so it silently ignored the RAM badge, mouse travel,
        // printer status, laptop battery and the Wi-Fi text. Two methods doing one job,
        // one of them a subset of the other, and which of them ran depended on which code
        // path reached it. Deleted - there is now one.

        public void RefreshOverlay()
        {
            // APPLY THE FIELD VISIBILITY. This did not happen, and the omission meant
            // ticking a box in Overlay options changed nothing on an open overlay:
            // Setting_Changed calls this, and this called only UpdateOverlay, which sets
            // four pieces of text and touches no row's visibility at all. The dialog says
            // "changes are saved as you make them" and they were - they just could not be
            // seen until the overlay was closed and reopened.
            //
            // (A `var fields = ...` sat here, read from preferences and never passed
            // anywhere. The line that looked like it was doing this job was the evidence
            // that nothing was.)
            ApplyOverlayFieldVisibility();

            UpdateOverlay(
                ramUsage: SystemStatsHelper.GetRamUsagePercent(),
                threshold: PreferencesManager.Current.Ram.AutoThreshold,
                autoEnabled: PreferencesManager.Current.Ram.AutoRam,
                lastBoost: PreferencesManager.GetLastRamBoostMessage(),
                triggerCount: PreferencesManager.Current.Ram.AutoTriggerCount,
                topProcess: SystemStatsHelper.GetTopProcess(),
                status: "Refreshed"
            );
        }
    }
}
