// File: Dialogs/OverlayOptionsDialog.xaml.cs
using System.Linq;
using System.Windows;
using SystemOptimizer.Core.Settings;

namespace SystemOptimizer.Dialogs
{
    public partial class OverlayOptionsDialog : Window
    {
        /// <summary>
        /// TRUE until the checkboxes have been filled in from preferences.
        ///
        /// Defaults to true, not false. Setting IsChecked during construction raises
        /// Checked/Unchecked, and a guard that starts false lets those writes land on top
        /// of the user's real settings - which is exactly how opening RAM options used to
        /// switch automatic cleanup off.
        /// </summary>
        private bool _loading = true;

        public OverlayOptionsDialog(PreferencesManager.OverlayFieldsSettings initial)
        {
            InitializeComponent();
            Apply(initial);
            _loading = false;
        }

        /// <summary>
        /// Puts a set of settings onto the boxes. Guarded, because setting IsChecked
        /// raises Checked/Unchecked and Setting_Changed would otherwise write a partly
        /// filled form back over the settings being loaded, one box at a time.
        /// </summary>
        private void Apply(PreferencesManager.OverlayFieldsSettings initial)
        {
            bool wasLoading = _loading;
            _loading = true;
            try { Fill(initial); }
            finally { _loading = wasLoading; }
        }

        private void Fill(PreferencesManager.OverlayFieldsSettings initial)
        {
            CpuCheck.IsChecked = initial.Cpu;
            RamCheck.IsChecked = initial.Ram;
            DiskCheck.IsChecked = initial.Disk;
            NetworkCheck.IsChecked = initial.Network;
            WifiChk.IsChecked = initial.Wifi;
            BatteryCheck.IsChecked = initial.Battery;
            PagefileCheck.IsChecked = initial.Pagefile;
            AppCountCheck.IsChecked = initial.AppCount;
            UptimeCheck.IsChecked = initial.Uptime;
            WindowsVersionCheck.IsChecked = initial.WindowsVersion;
            ArchCheck.IsChecked = initial.Arch;
            UserCheck.IsChecked = initial.User;
            MachineCheck.IsChecked = initial.Machine;
            BootCheck.IsChecked = initial.Boot;
            CDriveCheck.IsChecked = initial.CDrive;
            GpuCheck.IsChecked = initial.Gpu;

            RamStatsCheck.IsChecked = initial.ShowRamBadge;
            MouseTravelCheck.IsChecked = initial.ShowMouseDistance;
            PrinterCheck.IsChecked = initial.ShowPrinterStatus;
            LaptopBatteryCheck.IsChecked = initial.ShowLaptopBattery;
            LastCleanCheck.IsChecked = initial.ShowLastClean;
            RecycleBinCheck.IsChecked = initial.ShowRecycleBin;
            LastCleanAgeCheck.IsChecked = initial.ShowLastCleanAge;
            BoostHeldOffCheck.IsChecked = initial.ShowBoostHeldOff;
            TopProcessCheck.IsChecked = initial.ShowTopProcess;
            LastBoostCheck.IsChecked = initial.ShowLastBoost;
            ThresholdCheck.IsChecked = initial.ShowThreshold;
        }

        /// <summary>
        /// Back to what a new installation shows.
        ///
        /// Needed because a changed default only reaches a machine that has never saved
        /// that setting - so without this, anybody with an existing preferences file could
        /// never see the shipped defaults at all.
        ///
        /// Scoped to the ROWS. Position, size, transparency, always-on-top and
        /// click-through are left alone: the application already had a global "clear saved
        /// choices" that reached too far, replacing the whole RAM section and taking the
        /// boost history and the automatic-boost setting with it.
        /// </summary>
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var defaults = new PreferencesManager.OverlayFieldsSettings();
            Apply(defaults);

            PreferencesManager.Current.OverlayFields = defaults;
            PreferencesManager.SavePreferences();

            if (Application.Current.Windows.OfType<OverlayWindow>().FirstOrDefault() is OverlayWindow overlay)
                overlay.RefreshOverlay();
        }

        /// <summary>
        /// Every tick writes, and the overlay redraws immediately.
        ///
        /// This replaces an OK/Cancel pair - the last one in the application. It was not
        /// destructive like the others (Cancel genuinely discarded), but it was the only
        /// remaining page where ticking a box did not count until a button was pressed,
        /// and the result of each tick is visible on the overlay the moment it happens.
        /// </summary>
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            PreferencesManager.Current.OverlayFields = new PreferencesManager.OverlayFieldsSettings
            {
                Cpu = CpuCheck.IsChecked == true,
                Ram = RamCheck.IsChecked == true,
                Disk = DiskCheck.IsChecked == true,
                Network = NetworkCheck.IsChecked == true,
                Wifi = WifiChk.IsChecked == true,
                Battery = BatteryCheck.IsChecked == true,
                Pagefile = PagefileCheck.IsChecked == true,
                AppCount = AppCountCheck.IsChecked == true,
                Uptime = UptimeCheck.IsChecked == true,
                WindowsVersion = WindowsVersionCheck.IsChecked == true,
                Arch = ArchCheck.IsChecked == true,
                User = UserCheck.IsChecked == true,
                Machine = MachineCheck.IsChecked == true,
                Boot = BootCheck.IsChecked == true,
                CDrive = CDriveCheck.IsChecked == true,
                Gpu = GpuCheck.IsChecked == true,
                ShowRamBadge = RamStatsCheck.IsChecked == true,
                ShowMouseDistance = MouseTravelCheck.IsChecked == true,
                ShowPrinterStatus = PrinterCheck.IsChecked == true,
                ShowLaptopBattery = LaptopBatteryCheck.IsChecked == true,
                ShowLastClean = LastCleanCheck.IsChecked == true,
                ShowRecycleBin = RecycleBinCheck.IsChecked == true,
                ShowLastCleanAge = LastCleanAgeCheck.IsChecked == true,
                ShowBoostHeldOff = BoostHeldOffCheck.IsChecked == true,
                ShowTopProcess = TopProcessCheck.IsChecked == true,
                ShowLastBoost = LastBoostCheck.IsChecked == true,
                ShowThreshold = ThresholdCheck.IsChecked == true
            };
            PreferencesManager.SavePreferences();

            // Redraw any open overlay so the tick and what is on screen agree.
            if (Application.Current.Windows.OfType<OverlayWindow>().FirstOrDefault() is OverlayWindow overlay)
                overlay.RefreshOverlay();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // Deleted with the OK button: the Result property and its OverlayFields class, the
        // four ShowRamBadge/ShowMouseDistance/ShowPrinterStatus/ShowLaptopBattery
        // auto-properties, and DiskCheck_Checked - an EMPTY method body that no XAML ever
        // wired up. Neither caller of this dialog read Result or DialogResult; both simply
        // called ShowDialog, so all of it was dead weight around a value nobody collected.
    }
}
