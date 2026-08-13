using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace SystemOptimizer.Dialogs
{
    public partial class SuccessDialog : Window
    {
        /// <summary>
        /// totalFiles: count of files deleted
        /// totalFolders: count of folders deleted
        /// ramFreedMb:   MB of RAM reclaimed
        /// bytesFreed:   total bytes freed on disk
        /// </summary>
        public SuccessDialog(int totalFiles, int totalFolders, int ramFreedMb, long bytesFreed)
        {
            InitializeComponent();
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Labels live in XAML now, so these carry the value only.
            FilesDeletedText.Text = totalFiles.ToString("N0");
            FoldersDeletedText.Text = totalFolders.ToString("N0");
            RamFreedText.Text = $"{ramFreedMb:N0} MB";
            SpaceFreedText.Text = FormatBytes(bytesFreed);

            BuildBreakdown();

            // With nothing recycled, the note and the button would be pointing at a bin
            // this run did not put anything in. A common outcome: run it twice in a row
            // and the second finds only what something still has open.
            if (totalFiles == 0 && totalFolders == 0)
            {
                RecycleNote.Visibility = Visibility.Collapsed;
                OpenBinButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Opens the Recycle Bin so the user can check the work before committing to it.
        ///
        /// shell:RecycleBinFolder rather than a path: the bin is a shell namespace made up
        /// of per-user, per-volume folders, and C:\$Recycle.Bin is an implementation
        /// detail that opens as a bare directory listing of $I and $R files.
        /// </summary>
        /// <summary>
        /// This run's own log, shown as a button once it is set.
        ///
        /// A settable property rather than a constructor parameter, deliberately: the
        /// verification harness builds this window through reflection with a fixed
        /// argument array, and adding an optional parameter has broken reflection callers
        /// on this project twice already.
        /// </summary>
        public string RunLogPath
        {
            get => _runLogPath;
            set
            {
                _runLogPath = value;
                OpenLogButton.Visibility =
                    !string.IsNullOrEmpty(value) && System.IO.File.Exists(value)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
        }
        private string _runLogPath;

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_runLogPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Core.Logging.LogHelper.Log("Open log failed: " + ex);
            }
        }

        private void OpenBin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Core.Logging.LogHelper.Log("Open Recycle Bin failed: " + ex);
            }
        }

        /// <summary>
        /// One row per cleanup area that actually removed something, largest first.
        ///
        /// Built here rather than declared in XAML because which areas ran depends on what
        /// was ticked, and an area that removed nothing is worth omitting rather than
        /// showing as a zero - a list of zeroes reads as failure.
        /// </summary>
        private void BuildBreakdown()
        {
            var areas = Core.Cleanup.CleanupHelper.ByArea
                .Where(a => a.Value.Files > 0)
                .OrderByDescending(a => a.Value.Bytes)
                .ToList();

            if (areas.Count == 0) return;

            BreakdownHeader.Visibility = Visibility.Visible;
            BreakdownPanel.Visibility = Visibility.Visible;

            int row = 0;
            foreach (var area in areas)
            {
                BreakdownGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var name = new TextBlock
                {
                    // The stages are named for what they were doing at the time
                    // ("Scanning browser caches"); here they name what was taken.
                    Text = Friendly(area.Key),
                    Margin = new Thickness(0, 3, 0, 3),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                name.SetResourceReference(StyleProperty, "LabelTextStyle");
                Grid.SetRow(name, row);
                Grid.SetColumn(name, 0);
                BreakdownGrid.Children.Add(name);

                var value = new TextBlock
                {
                    Text = $"{area.Value.Files:N0}   {FormatBytes(area.Value.Bytes)}",
                    Margin = new Thickness(12, 3, 0, 3),
                    FontWeight = FontWeights.SemiBold
                };
                value.SetResourceReference(StyleProperty, "BodyTextStyle");
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 1);
                BreakdownGrid.Children.Add(value);

                row++;
            }
        }

        private static string Friendly(string stage) => stage switch
        {
            "Scanning temporary files" => "Temporary files",
            "Scanning Windows temp" => "Windows temp",
            "Scanning browser caches" => "Browser cache",
            "Scanning downloads" => "Downloads",
            "Scanning recent items" => "Recent items",
            _ => string.IsNullOrWhiteSpace(stage) ? "Other" : stage
        };
        // Handler for the OK button (wired as Ok_Click in XAML)
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private static string FormatBytes(long bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;
            const double TB = GB * 1024.0;
            if (bytes >= TB) return $"{bytes / TB:F2} TB";
            if (bytes >= GB) return $"{bytes / GB:F2} GB";
            if (bytes >= MB) return $"{bytes / MB:F1} MB";
            if (bytes >= KB) return $"{bytes / KB:F1} KB";
            return $"{bytes} B";
        }
    }
}
