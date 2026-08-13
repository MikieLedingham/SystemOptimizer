// File: Helpers/TrayIconManager.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SystemOptimizer.Dialogs;
using System.Collections.Generic;
using Microsoft.Win32;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Platform;
namespace SystemOptimizer.Shell
{
    public static class TrayIconManager
    {
        private static NotifyIcon _trayIcon;
        private static ContextMenuStrip _trayMenu;
        private static MainWindow _mainWindowRef;
        private static bool _isAdmin;
        // Was previously handed the LICENCE state by App.OnStartup while the parameter was
        // named isAdminMode, so a licensed non-elevated user was treated as admin. Admin
        // capability is elevation - read it directly. (Licensing removed in 2.0.)
        public static void Initialize(MainWindow mainWindow)
        {
            _isAdmin = UacHelper.IsRunningAsAdmin();
            _mainWindowRef = mainWindow;
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            _trayMenu = BuildMenu();
            _trayIcon = new NotifyIcon
            {
                Icon = InitialIcon(),
                Visible = true,
                Text = "System Optimizer",
                ContextMenuStrip = _trayMenu
            };

            // A DPI change or a light/dark switch changes both the size Windows wants and
            // the colour that will be legible on the taskbar. Without this the icon keeps
            // whatever it was drawn at until the percentage happens to move - which on a
            // machine with plenty of RAM can be a very long time.
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _trayIcon.DoubleClick += (s, e) => _mainWindowRef?.RestoreMainWindow();
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    _mainWindowRef?.RestoreMainWindow();
            };
            Tools.NoBoost.NoBoostMode.Changed += RefreshTrayMenu;
        }
        public static ContextMenuStrip ContextMenu => _trayMenu;
        /// <summary>
        /// The tray menu is the SHARED menu, rendered as WinForms controls.
        ///
        /// This method used to build the whole thing by hand - about 200 lines of
        /// ToolStripItems - alongside a second, different menu written out in
        /// MainWindow.xaml. They drifted, which is the entire reason AppMenu exists.
        /// Rebuilt on every open so conditional entries and ticks stay honest.
        /// </summary>
        private static ContextMenuStrip BuildMenu()
        {
            var menu = AppMenu.BuildWinForms(AppMenu.Host.Tray);
            menu.Opening += (_, __) =>
            {
                var fresh = AppMenu.BuildWinForms(AppMenu.Host.Tray);
                menu.Items.Clear();
                menu.Items.AddRange(fresh.Items.Cast<ToolStripItem>().ToArray());
            };
            return menu;
        }

        public static void RefreshTrayMenu(bool _ = false)
        {
            if (_trayIcon != null)
                _trayIcon.ContextMenuStrip = BuildMenu();
        }
        // The percentage currently drawn, so a steady reading does not redraw the icon
        // every second. -1 means "nothing drawn yet".
        private static int _drawnPercent = -1;

        public static void UpdateTrayIcon(int ramPercent, int cpuPercent)
        {
            if (_trayIcon == null)
                return;
            try
            {
                if (ramPercent != _drawnPercent)
                {
                    SetIcon(TrayIconRenderer.Render(ramPercent));
                    _drawnPercent = ramPercent;
                }
                int freePercent = 100 - ramPercent;
                string text = $"System Optimizer - CPU: {cpuPercent}% | RAM Free: {freePercent}%";

                // Says so when automatic boosting is being held off. The icon's colour is
                // left alone: it means RAM pressure, and one channel cannot carry two
                // meanings without both becoming ambiguous.
                //
                // Asked through the registry rather than the no-boost tool directly, so the
                // tray reports ANY tool holding maintenance off without knowing which tools
                // exist. Cached, because answering costs a process enumeration and this runs
                // once a second.
                string heldOff = Tools.ToolRegistry.AutomaticMaintenanceHeldOffCached();
                if (heldOff != null)
                    text += $"\r\nAuto RAM boost blocked - {heldOff}";

                _trayIcon.Text = Truncate(text);
            }
            catch { }
        }

        /// <summary>
        /// NotifyIcon.Text throws above 127 characters, and the blocked note appends a
        /// program name of unknown length to it. A tooltip is never worth an exception.
        /// </summary>
        private static string Truncate(string text) =>
            text.Length <= 127 ? text : text.Substring(0, 124) + "...";

        /// <summary>
        /// Swaps the tray icon and disposes the one it replaces. NotifyIcon does not own
        /// the Icon it is given, so without this every redraw would strand a GDI handle.
        /// </summary>
        private static void SetIcon(Icon icon)
        {
            var old = _trayIcon.Icon;
            _trayIcon.Icon = icon;
            old?.Dispose();
        }
        public static void ShowNotification(string message)
        {
            if (_trayIcon == null) return;
            _trayIcon.BalloonTipTitle = "System Optimizer";
            _trayIcon.BalloonTipText = message;
            _trayIcon.ShowBalloonTip(3000);
        }
        public static void ShowLastRamResult()
        {
            if (_mainWindowRef != null)
            {
                _mainWindowRef.Dispatcher.Invoke(() =>
                {
                    string message = PreferencesManager.GetLastRamBoostMessage();
                    new RamBoostResult(message).ShowDialog();
                });
            }
        }
        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.VisualStyle) return;
            TrayIconRenderer.InvalidateTheme();
            Redraw();
        }

        private static void OnDisplaySettingsChanged(object sender, EventArgs e) => Redraw();

        /// <summary>Force the icon to be drawn again at the current size and theme.</summary>
        private static void Redraw()
        {
            if (_trayIcon == null || _drawnPercent < 0) return;
            try { SetIcon(TrayIconRenderer.Render(_drawnPercent)); }
            catch { }
        }

        public static void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                var icon = _trayIcon.Icon;
                _trayIcon.Dispose();
                icon?.Dispose();
                _trayIcon = null;
                _drawnPercent = -1;
            }
        }
        /// <summary>
        /// The icon to start with, drawn from a live reading rather than guessed.
        ///
        /// This used to be LoadTrayIcon("RAM.ico"), searching two folders for a file that
        /// does not exist anywhere in the tree - so it always fell through to
        /// SystemIcons.Application and every launch briefly showed the generic Windows
        /// icon. Reading the percentage here means the tray is correct from the first
        /// frame instead of a second later.
        /// </summary>
        private static Icon InitialIcon()
        {
            try
            {
                var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
                double total = ci.TotalPhysicalMemory;
                double free = ci.AvailablePhysicalMemory;
                int used = total > 0 ? (int)Math.Round((total - free) / total * 100.0) : 0;
                _drawnPercent = used;
                return TrayIconRenderer.Render(used);
            }
            catch
            {
                return TrayIconRenderer.Render(0);
            }
        }
        // A private GameInfo lived here for deserialising the no-boost list back when this
        // class built the tray menu itself. That moved to AppMenu, and the list is now read
        // in exactly one place - Tools/NoBoost/NoBoostList.cs - so this shadow copy went.
        // It was also missing ExePath, so anything it read silently lost that field.
    }
}
