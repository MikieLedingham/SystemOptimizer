// File: BasicCleanupDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Interop;
using SystemOptimizer.Core.Settings;
namespace SystemOptimizer.Dialogs
{
    public partial class BasicCleanupDialog : Window
    {
        /// <summary>
        /// TRUE until preferences have been read in.
        ///
        /// Defaults to true, not false. A guard that starts false protects nothing during
        /// construction, which is exactly when a control can raise a change event off its
        /// own default and write that over the user's saved settings.
        /// </summary>
        private bool _loading = true;

        public BasicCleanupDialog()
        {
            InitializeComponent();
            CenterOnActiveScreen();
            Topmost = true;
            LoadPreferences();
            _loading = false;
        }
        public bool CleanTempFiles => TempFilesCheckbox?.IsChecked == true;
        public bool CleanBrowserCache => BrowserCacheCheckbox?.IsChecked == true;
        public bool CleanDownloadsFolder => DownloadsFolderCheckbox?.IsChecked == true;
        public bool CleanRecent => RecentCheckbox?.IsChecked == true;

        /// <summary>
        /// Every tick saves. This replaces an OK button that, with "Remember these
        /// choices" unticked, called ClearPreferences and threw away everything the user
        /// had just selected - the same fault shape as the no-boost list's dead Save
        /// button and RAM options resetting itself on open.
        /// </summary>
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SavePreferences();
        }

        private void ClearSaved_Click(object sender, RoutedEventArgs e)
        {
            // The boxes visibly empty, which is the confirmation. The old version popped a
            // message box afterwards to say it had happened, in front of the page already
            // showing that it had.
            _loading = true;
            TempFilesCheckbox.IsChecked = false;
            BrowserCacheCheckbox.IsChecked = false;
            DownloadsFolderCheckbox.IsChecked = false;
            RecentCheckbox.IsChecked = false;
            DNSCacheCheckbox.IsChecked = false;
            _loading = false;
            SavePreferences();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void CenterOnActiveScreen()
        {
            var helper = new WindowInteropHelper(this);
            var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
            var area = screen.WorkingArea;
            this.Left = area.Left + (area.Width - this.Width) / 2;
            this.Top = area.Top + (area.Height - this.Height) / 2;
        }
        private void LoadPreferences()
        {
            var basic = PreferencesManager.Current.Basic;
            TempFilesCheckbox.IsChecked = basic.TempFiles;
            BrowserCacheCheckbox.IsChecked = basic.BrowserCache;
            DownloadsFolderCheckbox.IsChecked = basic.Downloads;
            RecentCheckbox.IsChecked = basic.Recent;
            DNSCacheCheckbox.IsChecked = basic.DNSCache;
        }
        private void SavePreferences()
        {
            var basic = PreferencesManager.Current.Basic;
            basic.TempFiles = TempFilesCheckbox.IsChecked == true;
            basic.BrowserCache = BrowserCacheCheckbox.IsChecked == true;
            basic.Downloads = DownloadsFolderCheckbox.IsChecked == true;
            basic.Recent = RecentCheckbox.IsChecked == true;
            basic.DNSCache = DNSCacheCheckbox.IsChecked == true;
            // Remember is written true and never read by this window again. The flag stays
            // on the section only because the JSON on disk carries it; the behaviour it
            // used to gate is gone.
            basic.Remember = true;
            PreferencesManager.SavePreferences();
        }
    }
}
