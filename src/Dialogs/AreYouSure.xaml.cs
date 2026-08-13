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
            // EVERY option the engine can act on has to appear here, or this dialog asks
            // the user to approve less than what will happen. Thumbnail cache was missing:
            // it could be ticked in Basic cleanup, it ran, and the confirmation never
            // mentioned it. That is consent to the wrong thing, which is worse than an
            // ugly list - and it is the same fault as a control that claims to do
            // something it does not.
            //
            // If a cleanup option is ever added, it belongs on this list in the same
            // change. The harness check "confirmlist" fails the build-verification run if
            // an option exists that this dialog can never mention.
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
