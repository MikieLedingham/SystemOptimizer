// File: Dialogs/ManageGamesDialog.xaml.cs
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SystemOptimizer.Tools.NoBoost;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Shell;
// `using WpfAnimatedGif` was here for Images\game.gif, deleted with the rest of
// the old artwork. No ImageBehavior call survived it, so the package went
// too rather than being carried onto .NET 8.
namespace SystemOptimizer.Dialogs
{
    public partial class ManageGamesDialog : Window
    {
        private List<NoBoostEntry> _apps = new List<NoBoostEntry>();
        private readonly string _appsFilePath;
        // _spinnerGifPath went with the gif and the WpfAnimatedGif reference above; it
        // was never assigned after the artwork was deleted.
        public ManageGamesDialog()
        {
            InitializeComponent();
            _appsFilePath = AppPaths.AppsListFile;
            // Hide the scanning overlay by default to avoid blocking the UI.
            SpinnerOverlay.Visibility = Visibility.Collapsed;
            // Always refresh/rebind the list every time the window opens
            Loaded += (s, e) =>
            {
                LoadApps();
                EnableAllControls();
            };
        }
        /// <summary>
        /// Loads the saved list, or scans for one the first time.
        ///
        /// The first-run scan runs OFF the UI thread. It takes about thirteen seconds on a
        /// real machine - resolving several hundred Start Menu shortcuts through COM and
        /// reading version information from each executable - and this used to be called
        /// straight from Loaded, so the very first time anyone opened this window it froze
        /// solid with no spinner and no explanation.
        /// </summary>
        private async void LoadApps()
        {
            if (File.Exists(_appsFilePath))
            {
                _apps = NoBoostList.Load();
                PopulateAppsPanel();
                GamesStackPanel.UpdateLayout();
                EnableAllControls();
                return;
            }

            ShowSpinner(true);
            try
            {
                _apps = await Task.Run(() => ProgramScanner.ScanComputer());
                NoBoostList.Save(_apps);
            }
            finally
            {
                ShowSpinner(false);
                PopulateAppsPanel();
                GamesStackPanel.UpdateLayout();
                EnableAllControls();
            }
        }
        // Make all controls, checkboxes, and the stackpanel interactive
        private void EnableAllControls()
        {
            GamesStackPanel.IsEnabled = true;
            ClearAllChoicesButton.IsEnabled = true;
            foreach (var child in GamesStackPanel.Children)
            {
                if (child is CheckBox cb)
                    cb.IsEnabled = true;
            }
        }
        // SetAnimatedGif removed in 2.0. The scanning indicator was an animated
        // Images/game.gif driven by WpfAnimatedGif; it is now a themed text overlay.
        // Rebuilds the checkbox panel and events
        /// <summary>
        /// Rebuilds the list, with whatever is running now at the top under its own
        /// heading.
        ///
        /// This is the order that matches what people are actually doing. Someone opens
        /// this list because a particular program is running and they do not want their
        /// memory touched while it is - hunting for it alphabetically among two hundred
        /// and sixty entries is the wrong way round. It also makes the matching visible:
        /// an entry under "currently running" is one that provably WILL block, which the
        /// old list could never demonstrate because none of its entries could match.
        /// </summary>
        private void PopulateAppsPanel()
        {
            GamesStackPanel.Children.Clear();
            UpdateTickedSummary();

            string filter = FilterBox?.Text?.Trim() ?? "";
            var shown = string.IsNullOrEmpty(filter)
                ? _apps
                : _apps.Where(a => Matches(a, filter)).ToList();

            var running = RunningProcessNames();
            var live = shown.Where(a => running.Contains(a.Name)).ToList();
            var rest = shown.Where(a => !running.Contains(a.Name)).ToList();

            if (live.Count > 0)
            {
                AddHeading($"Currently running on this system  ({live.Count})");
                foreach (var app in live) AddRow(app);
            }
            if (rest.Count > 0)
            {
                AddHeading(live.Count > 0 ? $"Everything else  ({rest.Count})" : $"Programs found  ({rest.Count})");
                foreach (var app in rest) AddRow(app);
            }
            if (live.Count == 0 && rest.Count == 0)
                AddHeading($"Nothing matches \"{filter}\".");

            EnableAllControls();
        }

        /// <summary>
        /// Matches the name a person reads, the process name behind it, and the path it is
        /// installed at.
        ///
        /// The path matters because people search by MAKER. Mikie typed "dell" and got
        /// nothing, which was strictly true - his one user-facing Dell application is
        /// called "Alienware Command Center" and its process is AWCC, so the word appears
        /// in neither - and useless as an answer. It lives under a Dell folder, and that is
        /// what he was actually asking about.
        /// </summary>
        private static bool Matches(NoBoostEntry app, string filter) =>
            (app.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (app.DisplayName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (app.ExePath ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterPrompt.Visibility = string.IsNullOrEmpty(FilterBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            PopulateAppsPanel();
        }

        /// <summary>
        /// States what is ticked, and says plainly when nothing is - because "no-boost mode
        /// is on" and "no-boost mode will ever do anything" are different claims, and an
        /// empty selection quietly means the second one is false.
        /// </summary>
        private void UpdateTickedSummary()
        {
            // A list from before the scanner rework holds shortcut and folder names with no
            // executable behind them - "Crystal Disk Info" where the process is
            // DiskInfo64K - so not one entry can ever match and nothing will ever pause.
            // It looks completely normal, ticks like normal, and does nothing, which is the
            // exact failure the whole feature just came out of. Say so, rather than leaving
            // it to be discovered by the boost that never gets held off.
            if (_apps.Count > 0 && _apps.All(a => string.IsNullOrWhiteSpace(a.ExePath)))
            {
                TickedSummary.Text =
                    "This list was built by an earlier version and cannot match running " +
                    "programs. Press \"Rescan this computer\" to rebuild it. Ticks on these " +
                    "old entries cannot be carried over, so you will need to set them again.";
                TickedSummary.SetResourceReference(ForegroundProperty, "WarningBrush");
                return;
            }

            var ticked = _apps.Where(a => a.Selected).ToList();
            if (ticked.Count == 0)
            {
                TickedSummary.Text = "Nothing is ticked, so automatic RAM boosting is never paused.";
                TickedSummary.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
                return;
            }

            var names = ticked.Select(a => a.DisplayName ?? a.Name).OrderBy(n => n).ToList();
            string list = names.Count <= 6
                ? string.Join(", ", names)
                : string.Join(", ", names.Take(6)) + $" and {names.Count - 6} more";

            TickedSummary.Text = $"Pausing for {ticked.Count}: {list}";
            TickedSummary.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        }

        private void AddHeading(string text)
        {
            var heading = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, GamesStackPanel.Children.Count == 0 ? 0 : 12, 0, 4)
            };
            heading.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
            GamesStackPanel.Children.Add(heading);
        }

        private static HashSet<string> RunningProcessNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    try { if (!string.IsNullOrEmpty(p.ProcessName)) names.Add(p.ProcessName); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return names;
        }

        private void AddRow(NoBoostEntry app)
        {
            {
                var cb = new CheckBox
                {
                    Content = app.Label,
                    IsChecked = app.Selected,
                    Tag = app,
                    IsEnabled = true
                };
                // Ticking IS saving. There used to be a Save button, and it only wrote the
                // file when a separate "Remember these choices" box was also ticked - which
                // nothing ever ticked by default. Mikie ticked an application, and whether
                // he pressed Save or not, the result was the same: nothing was written.
                // A settings list that needs a second confirmation to mean anything will
                // lose people's choices, so the click is the commit.
                cb.Checked += (s, e) => SetSelected((NoBoostEntry)cb.Tag, true);
                cb.Unchecked += (s, e) => SetSelected((NoBoostEntry)cb.Tag, false);
                GamesStackPanel.Children.Add(cb);
            }
        }
        /// <summary>
        /// Records a tick and writes it out. Also invalidates the Tools cache, so the tray
        /// tooltip and the next automatic boost see the change immediately rather than up
        /// to ten seconds later - a user who ticks the app they are running should not have
        /// to wonder whether it took.
        /// </summary>
        private void SetSelected(NoBoostEntry app, bool selected)
        {
            if (app == null || app.Selected == selected) return;
            app.Selected = selected;
            NoBoostList.Save(_apps);
            Tools.ToolRegistry.InvalidateCache();

            // Refresh the summary but NOT the list. The summary was only rebuilt by
            // PopulateAppsPanel, which does not run on a tick, so it kept saying "nothing
            // is ticked" until the window was closed and reopened - the setting had saved,
            // and the one line reporting it had not caught up.
            //
            // Rebuilding the whole list here instead would be worse: entries would jump
            // between the running and everything-else groups under the cursor and the
            // scroll position would reset, mid-tick.
            UpdateTickedSummary();
        }

        /// <summary>
        /// A fresh scan REPLACES the list, keeping the ticks of anything still found.
        /// That is what makes "rescan from scratch" mean what it says - entries written by
        /// the old scanner, which recorded shortcut and folder names that could never match
        /// a process, are dropped rather than accumulated alongside the good ones.
        /// </summary>
        private void ReplaceWith(List<NoBoostEntry> found)
        {
            var ticked = new HashSet<string>(
                _apps.Where(a => a.Selected).Select(a => a.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in found)
                if (ticked.Contains(entry.Name)) entry.Selected = true;

            // Ticks are carried across by process name, so an entry from the old scanner
            // cannot survive a rescan: it was stored as "Crystal Disk Info" and comes back
            // as "DiskInfo64K", and nothing links the two. That is unavoidable - the old
            // entry never had an executable to identify it by - but losing a choice in
            // silence is not. Name what was dropped so it can be ticked again.
            var lost = ticked.Where(t => !found.Any(f =>
                           string.Equals(f.Name, t, StringComparison.OrdinalIgnoreCase)))
                       .OrderBy(t => t).ToList();

            _apps = found;
            Persist();

            if (lost.Count > 0)
                CustomMessageBox.Show(
                    $"The list was rebuilt with {found.Count} programs.\n\n" +
                    (lost.Count == 1
                        ? $"One program you had ticked could not be matched to anything running or installed, so it was cleared: {lost[0]}."
                        : $"{lost.Count} programs you had ticked could not be matched and were cleared: {string.Join(", ", lost)}.") +
                    "\n\nIf they are still installed, find them in the list and tick them again.",
                    "Rescan complete",
                    CustomMessageBox.Kind.Warning);
        }

        /// <summary>A folder scan ADDS to the list rather than replacing it.</summary>
        private void MergeIn(List<NoBoostEntry> found)
        {
            var byName = _apps.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in found)
            {
                if (byName.TryGetValue(entry.Name, out var existing))
                {
                    if (string.IsNullOrWhiteSpace(existing.ExePath)) existing.ExePath = entry.ExePath;
                    if (string.IsNullOrWhiteSpace(existing.DisplayName)) existing.DisplayName = entry.DisplayName;
                    continue;
                }
                _apps.Add(entry);
                byName[entry.Name] = entry;
            }
            Persist();
        }

        private void Persist()
        {
            NoBoostList.Save(_apps);
            Tools.ToolRegistry.InvalidateCache();
            PopulateAppsPanel();
        }

        // Async search button handler
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSpinner(true);
            SearchButton.Content = "Scanning...";
            try
            {
                var found = await Task.Run(() => ProgramScanner.ScanComputer());
                ReplaceWith(found);
            }
            finally
            {
                // Restores the label the XAML actually sets. This used to put back "Scan
                // Regular Folders" - an older name - so the button silently RENAMED itself
                // the first time it was used and never went back.
                SearchButton.Content = "Rescan this computer";
                ShowSpinner(false);
            }
        }

        /// <summary>
        /// Adds one executable by hand. Scanning was the only way into this list, so an
        /// application the scanner did not find could not be added at all.
        /// </summary>
        private void AddProgramButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick a program to add to the no-boost list",
                Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (picker.ShowDialog(this) != true) return;

            // The list matches on process name - the executable's filename without its
            // extension - so that is what Name holds. ExePath is kept too, being exact.
            var name = System.IO.Path.GetFileNameWithoutExtension(picker.FileName);
            if (string.IsNullOrWhiteSpace(name)) return;

            if (_apps.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                CustomMessageBox.Show($"\"{name}\" is already in the list.", "No-boost list");
                return;
            }

            _apps.Add(new NoBoostEntry { Name = name, ExePath = picker.FileName, Selected = true });
            NoBoostList.Save(_apps);
            Tools.ToolRegistry.InvalidateCache();
            PopulateAppsPanel();
        }
        private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder to search for Applications"
            })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ShowSpinner(true);
                    AddFolderButton.Content = "Scanning...";
                    try
                    {
                        var found = await Task.Run(() => ProgramScanner.ScanFolder(dialog.SelectedPath));
                        MergeIn(found);
                    }
                    finally
                    {
                        // Same rename bug as SearchButton: this put back the old label
                        // "Add Specific Folder" instead of the one the XAML defines.
                        AddFolderButton.Content = "Scan a folder...";
                        ShowSpinner(false);
                    }
                }
            }
        }
        // Clear just user selections (keep list)
        private void ClearAllChoices_Click(object sender, RoutedEventArgs e)
        {
            foreach (var app in _apps)
                app.Selected = false;
            NoBoostList.Save(_apps);
            Tools.ToolRegistry.InvalidateCache();
            PopulateAppsPanel();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Nothing to commit here any more - every tick, scan and clear writes as it
            // happens. The Save button it replaced only wrote the file when a separate
            // "Remember these choices" box was also ticked, and nothing ever ticked that by
            // default, so pressing Save wrote nothing at all.
            this.Close();
        }
        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            // Writes an empty list rather than deleting the file. Deleting it made
            // LoadApps fall through to a full scan the next time the window opened, so
            // "Clear list" silently repopulated itself and looked like it had not worked.
            _apps = new List<NoBoostEntry>();
            NoBoostList.Save(_apps);
            Tools.ToolRegistry.InvalidateCache();
            PopulateAppsPanel();
        }
        // Spinner logic using the GIF
        private void ShowSpinner(bool show)
        {
            SpinnerOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            SetButtonsEnabled(!show);
            // --- Always enable after spinner hides ---
            if (!show)
            {
                GamesStackPanel.IsEnabled = true;
            }
        }
        private void SetButtonsEnabled(bool enabled)
        {
            AddFolderButton.IsEnabled = enabled;
            SearchButton.IsEnabled = enabled;
            ClearAllChoicesButton.IsEnabled = enabled;
            // All other input controls remain interactive
        }
    }
}
