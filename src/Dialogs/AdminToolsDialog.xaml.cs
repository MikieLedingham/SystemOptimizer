using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SystemOptimizer.Dialogs;
using SystemOptimizer.Core.Cleanup;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Platform;
using SystemOptimizer.Core.Logging;
using SystemOptimizer.Shell;

namespace SystemOptimizer
{
    /// <summary>
    /// Direct access to the admin-only cleanup targets, skipping the options-then-confirm
    /// route. 2.0 gives it a working Run now button - before this it could only report
    /// checkbox states to a caller, and it had no callers.
    /// </summary>
    public partial class AdminToolsDialog : Window
    {
        public bool CleanWindowsTemp => WindowsTempCheckBox.IsChecked == true;
        public bool CleanCrashDumps => CrashDumpsCheckBox.IsChecked == true;
        public bool CleanOldWindows => OldWindowsCheckBox.IsChecked == true;
        public bool CleanRecycleBin => RecycleBinCheckBox.IsChecked == true;

        public AdminToolsDialog()
        {
            InitializeComponent();
            LoadFromPreferences();
            if (!UacHelper.IsRunningAsAdmin())
                ElevationNotice.Visibility = Visibility.Visible;
        }

        /// <summary>Start from the same saved Admin section the cleanup flow uses.</summary>
        private void LoadFromPreferences()
        {
            var a = PreferencesManager.Current.Admin;
            WindowsTempCheckBox.IsChecked = a.WindowsTemp;
            CrashDumpsCheckBox.IsChecked = a.CrashDumps;
            OldWindowsCheckBox.IsChecked = a.OldWindows;
            RecycleBinCheckBox.IsChecked = a.RecycleBin;
        }

        private BoostOptions BuildOptions() => new BoostOptions
        {
            CleanWindowsTemp = CleanWindowsTemp,
            CleanCrashDumps = CleanCrashDumps,
            CleanOldWindows = CleanOldWindows,
            CleanRecycleBin = CleanRecycleBin
        };

        private bool AnythingSelected()
            => CleanWindowsTemp || CleanCrashDumps
            || CleanOldWindows || CleanRecycleBin;

        private void RunNow_Click(object sender, RoutedEventArgs e)
        {
            if (!AnythingSelected())
            {
                CustomMessageBox.Show("Nothing is selected.", "Admin tools");
                return;
            }

            bool isAdmin = UacHelper.IsRunningAsAdmin();
            if (!isAdmin)
            {
                CustomMessageBox.Show(
                    "These actions need administrator rights. Restart System Optimizer as administrator to run them.",
                    "Administrator required");
                return;
            }

            var opts = BuildOptions();

            // Persist the selection so the main cleanup flow agrees with what ran here.
            // Only the four this page owns. It must not write Admin.DNSCache or
            // Admin.ThumbnailCache: those moved to Basic, and writing them here would put
            // back the second source the move existed to remove.
            var a = PreferencesManager.Current.Admin;
            a.WindowsTemp = CleanWindowsTemp;
            a.CrashDumps = CleanCrashDumps;
            a.OldWindows = CleanOldWindows;
            a.RecycleBin = CleanRecycleBin;
            PreferencesManager.SavePreferences();

            var progress = ProgressDialog.Instance ?? new ProgressDialog { Owner = this };
            if (!progress.IsVisible) progress.Show();

            Task.Run(() =>
            {
                try { CleanupHelper.ExecuteCleanup(opts, isAdmin: true); }
                catch (Exception ex) { LogHelper.Log("AdminToolsDialog RunNow exception: " + ex); }
            });

            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
