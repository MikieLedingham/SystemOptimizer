// File: RamOptionsDialog.xaml.cs
using System;
using System.Windows;
using SystemOptimizer.Core.Monitoring;
using SystemOptimizer.Core.Settings;
namespace SystemOptimizer.Dialogs
{
    public partial class RamOptionsDialog : Window
    {
        public bool BoostRam { get; private set; }
        public bool AutoRam { get; private set; }
        public int RamThreshold { get; private set; }
        public bool RememberPreferences { get; private set; }
        private ResourceMonitorManager _resourceMonitor;
        public RamOptionsDialog()
        {
            InitializeComponent();
            CenterOnActiveScreen();
            this.Topmost = true;
            this.Activate();
            LoadSavedPreferences();
            // Live RAM usage
            _resourceMonitor = new ResourceMonitorManager();
            _resourceMonitor.ResourceUpdated += ResourceMonitor_ResourceUpdated;
            _resourceMonitor.Start();
        }
        private void ResourceMonitor_ResourceUpdated(ResourceMonitorManager.ResourceSnapshot snapshot)
        {
            Dispatcher.Invoke(() =>
            {
                RamTextBlock.Text = $"Free RAM: {Math.Round(snapshot.RamFreeGB, 1)} GB";
            });
        }
        private void ManageNoBoostButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ManageGamesDialog { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dlg.ShowDialog();
        }
        protected override void OnClosed(EventArgs e)
        {
            _resourceMonitor?.Stop();
            _resourceMonitor?.Dispose();
            base.OnClosed(e);
        }
        private void CenterOnActiveScreen()
        {
            try
            {
                var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
                this.Left = screen.WorkingArea.Left + (screen.WorkingArea.Width - this.Width) / 2;
                this.Top = screen.WorkingArea.Top + (screen.WorkingArea.Height - this.Height) / 2;
            }
            catch { }
        }
        /// <summary>
        /// TRUE until preferences have been read in. Not false-by-default, which cost the
        /// setting it was meant to protect.
        ///
        /// The slider's Minimum is 60, so creating it coerces its value from 0 to 60 and
        /// raises ValueChanged - during InitializeComponent, BEFORE LoadSavedPreferences
        /// runs and sets the guard. Save() therefore fired with the controls still at their
        /// defaults and wrote AutoRam=false, AutoThreshold=60 over whatever the user had.
        /// Opening the window was enough to switch automatic cleanup off, and the 60 left
        /// in the file is the slider's minimum rather than anything anyone chose.
        /// </summary>
        private bool _loading = true;

        private void LoadSavedPreferences()
        {
            _loading = true;
            try
            {
                var ram = PreferencesManager.Current.Ram;
                BoostRamCheckbox.IsChecked = ram.BoostRam;
                AutoRamCheckbox.IsChecked = ram.AutoRam;
                RamThresholdSlider.Value = ram.AutoThreshold;
            }
            finally { _loading = false; }

            ShowBlockedNote();
        }

        /// <summary>
        /// Writes every setting as it changes.
        ///
        /// There was an OK button and a "Remember these choices" box, and unlike the
        /// no-boost list's version this one was actively destructive: with Remember
        /// unticked, OK called ClearRamPreferences, which set AutoRam and BoostRam to false
        /// and reset the threshold to 85. Switching automatic cleanup on and pressing OK
        /// turned it straight back off. Same rule as the no-boost list now - the click is
        /// the commit.
        /// </summary>
        private void Save()
        {
            if (_loading) return;
            var ram = PreferencesManager.Current.Ram;
            ram.BoostRam = BoostRamCheckbox.IsChecked == true;
            ram.AutoRam = AutoRamCheckbox.IsChecked == true;
            ram.AutoThreshold = (int)RamThresholdSlider.Value;
            ram.Remember = true;
            PreferencesManager.SavePreferences();
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            Save();
            ShowBlockedNote();
        }

        private void Threshold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Save();

        /// <summary>
        /// Switching automatic cleanup ON is the other moment the block matters - this is
        /// the only place it is enabled - so it reports what is true right now rather than
        /// leaving the user to discover later that nothing ever fires.
        /// </summary>
        private void AutoRam_Changed(object sender, RoutedEventArgs e)
        {
            Save();
            ShowBlockedNote();

            string heldOff = Tools.ToolRegistry.AutomaticMaintenanceHeldOff();
            if (heldOff != null)
                App.ShowTrayNotification($"{heldOff}. Manual boosts still work.");
        }

        private void ShowBlockedNote()
        {
            string heldOff = AutoRamCheckbox.IsChecked == true
                ? Tools.ToolRegistry.AutomaticMaintenanceHeldOff()
                : null;

            BlockedNote.Text = heldOff ?? "";   // the summary is already a full sentence
            BlockedNote.Visibility = heldOff == null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Resets the three settings this page owns, and nothing else.
        ///
        /// The single "Clear all saved choices" this replaces did
        /// PreferencesManager.Current.Ram = new RamSection(), which also discarded
        /// LastBoostMessage, LastBoostTimeUtc, LastBoostAutomatic and AutoTriggerCount.
        /// Clearing cleanup selections therefore switched automatic boosting off and wiped
        /// the boost history - and now that automatic boosting has a control on the main
        /// window, that would be a visible setting changing itself for no stated reason.
        /// </summary>
        private void ClearSaved_Click(object sender, RoutedEventArgs e)
        {
            _loading = true;
            try
            {
                BoostRamCheckbox.IsChecked = false;
                AutoRamCheckbox.IsChecked = false;
                RamThresholdSlider.Value = new PreferencesManager.RamSection().AutoThreshold;
            }
            finally { _loading = false; }

            Save();
            ShowBlockedNote();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
