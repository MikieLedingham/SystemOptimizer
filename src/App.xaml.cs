// File: App.xaml.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Shell;

namespace SystemOptimizer
{
    // ToastHelper.ShowModernToast lived here and drew a Windows 10 action-centre
    // toast via Microsoft.Toolkit.Uwp.Notifications. Nothing in the tree ever called
    // it - the only notification path in use is TrayIconManager.ShowNotification,
    // which uses the tray balloon. Removed along with its two packages
    // (Microsoft.Toolkit.Uwp.Notifications, deprecated, and
    // Microsoft.Windows.SDK.Contracts). Its .NET 8 successor,
    // CommunityToolkit.WinUI.Notifications, needs a Windows-SDK target framework
    // (net8.0-windows10.0.17763.0), which would raise the product's minimum OS to
    // Windows 10 1809 - a real cost, not worth paying for an unused method. See the
    // note at the bottom of SystemOptimizer.csproj.

    public partial class App : Application
    {
        // --- P/Invoke for bringing an existing window to front ---
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        // --- End P/Invoke ---

        protected override void OnStartup(StartupEventArgs e)
        {
            // Program.Main already handled mutex / elevation decisions and created MainWindow.
            base.OnStartup(e);

            try
            {
                // Prepare AppData directory
                Directory.CreateDirectory(AppPaths.Root);

                // Appearance is loaded in Program.Main, before any window is constructed -
                // OnStartup runs too late, once MainWindow already exists.

                // Use MainWindow created in Program.Main; fallback if somehow null
                var mw = Current.MainWindow as MainWindow;
                if (mw == null)
                {
                    mw = new MainWindow();
                    Current.MainWindow = mw;
                    mw.Show();
                }

                TrayIconManager.Initialize(mw);
                PreferencesManager.LoadPreferences();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"EXCEPTION in OnStartup:\n{ex.GetType().Name}\n{ex.Message}\n\n{ex.StackTrace}",
                    "STARTUP ERROR",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                // Let it try to run if MainWindow already exists; otherwise shutdown.
                if (Current.MainWindow == null)
                    Shutdown();
            }
        }

        // TryAddDisableClickThroughTrayItem and RefreshTrayClickThroughItem lived here:
        // two near-identical 40-line methods that bolted a "Disable Click Through Mode"
        // item onto the tray menu after it had been built, and kept it in sync by hand.
        // It is now an ordinary conditional entry in AppMenu, so it appears in the window
        // menu too - which previously had no way out of click-through at all.
        public static void RefreshTrayClickThroughItem() => TrayIconManager.RefreshTrayMenu();

        public static void BringExistingInstanceToFront()
        {
            try
            {
                var me = Process.GetCurrentProcess();
                foreach (var proc in Process.GetProcessesByName(me.ProcessName))
                {
                    if (proc.Id == me.Id) continue;

                    var h = proc.MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                        SetForegroundWindow(h);
                    }
                    break;
                }
            }
            catch { }
        }

        // Forwarding helpers
        public static void UpdateTrayIcon(int r, int c) => TrayIconManager.UpdateTrayIcon(r, c);
        public static void ShowTrayNotification(string msg) => TrayIconManager.ShowNotification(msg);
        public static void ShowLastRamResult() => TrayIconManager.ShowLastRamResult();

        public static void ShowAutoCleanWarnings()
        {
            var app = Current;
            if (app == null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                return;
            app.Dispatcher.Invoke(() => new Dialogs.AutoCleanWarningDialog().ShowDialog());
        }

        public static void ShowAbout()
        {
            var app = Current;
            if (app == null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                return;
            app.Dispatcher.Invoke(() => new Dialogs.AboutWindow().ShowDialog());
        }

        public static void ToggleOverlay()
        {
            var app = Current;
            if (app == null || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                return;

            app.Dispatcher.Invoke(() =>
            {
                if (Current.MainWindow is MainWindow m)
                    m.ToggleOverlayFromTray();
            });
        }

        public static void ShowOverlayDisplayOptions()
        {
            Current.Dispatcher.Invoke(() =>
            {
                var dlg = new Dialogs.OverlayOptionsDialog(PreferencesManager.Current.OverlayFields)
                {
                    Owner = Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Topmost = true
                };
                dlg.ShowDialog();
            });
        }

        public static bool ShouldShowRamWarnings() => false;

        // A second, duplicate copy of StripMarginRenderer lived here alongside the one in
        // Helpers. Both drew Images/MTstrip.png down the tray menu's left gutter. Removed
        // in 2.0 with the rest of the decorative art.
    }
}
