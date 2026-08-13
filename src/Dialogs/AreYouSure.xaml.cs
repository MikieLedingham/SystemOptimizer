using System.Collections.Generic;
using System.Windows;
using SystemOptimizer.Core.Cleanup;
namespace SystemOptimizer.Dialogs
{
    public partial class AreYouSure : Window
    {
        private readonly BoostOptions _opts;
        public AreYouSure(BoostOptions opts)
        {
            InitializeComponent();
            _opts = opts;
            LoadSummary();
        }
        public AreYouSure(BoostOptions opts, bool isAdmin)
            : this(opts)
        {
            // isAdmin available if you want to conditionally include items
        }
        private void LoadSummary()
        {
            var items = new List<string>();
            void Add(string name, bool enabled)
            {
                if (enabled)
                    items.Add(name);
            }
            // Basic options
            Add("Clean Temp Files", _opts.CleanUserTemp);
            Add("Clean Windows Temp", _opts.CleanWindowsTemp);
            Add("Clean Recent Items", _opts.CleanRecent);
            Add("Clean Downloads (unused for 30 days)", _opts.CleanDownloadsFolder);
            Add("Clean Browser Cache", _opts.CleanBrowserCache);
            Add("Clean DNS Cache", _opts.CleanDNSCache);
            // Admin options
            Add("Clean Crash Dumps", _opts.CleanCrashDumps);
            Add("Clean Old Windows Installs", _opts.CleanOldWindows);
            Add("Empty Recycle Bin (permanent)", _opts.CleanRecycleBin);
            // RAM
            Add("Boost RAM Now", _opts.BoostRam);
            SummaryItemsControl.ItemsSource = items;
        }
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
