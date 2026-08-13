// File: Dialogs/RestoreDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using SystemOptimizer.Core.Cleanup;
using SystemOptimizer.Core.Logging;
using SystemOptimizer.Shell;

namespace SystemOptimizer.Dialogs
{
    /// <summary>
    /// Undo a cleanup.
    ///
    /// The engine has recorded a manifest per run since the plan-then-apply rewrite, and
    /// RecycleBin.Restore has been able to put files back for just as long - but nothing
    /// in the product ever called either of them. Every cleanup log ended with "Undo this
    /// run with Restore a previous clean", naming a feature that could not be reached from
    /// anywhere in the interface. Promising something and not providing it is the same
    /// fault as the old "Delete restore points" checkbox that no code implemented.
    /// </summary>
    public partial class RestoreDialog : Window
    {
        private List<CleanSession> _sessions = new List<CleanSession>();

        public RestoreDialog()
        {
            InitializeComponent();
            LoadSessions();
        }

        private void LoadSessions()
        {
            _sessions = CleanHistory.Recent();
            SessionList.Items.Clear();
            foreach (var s in _sessions)
                SessionList.Items.Add(s.Summary);

            if (_sessions.Count != 0) return;

            DetailText.Text = "No cleanups have been recorded yet.\n\n" +
                              "The last " + CleanHistory.KeepSessions + " runs are kept, and " +
                              "each one can be undone from here for as long as the files " +
                              "remain in the Recycle Bin.";
            RestoreButton.IsEnabled = false;
        }

        private CleanSession Selected =>
            SessionList.SelectedIndex >= 0 && SessionList.SelectedIndex < _sessions.Count
                ? _sessions[SessionList.SelectedIndex]
                : null;

        private void SessionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var s = Selected;
            RestoreButton.IsEnabled = s != null && s.Recycled.Count > 0;
            if (s == null) { DetailText.Text = "Select a cleanup on the left."; return; }

            var sb = new StringBuilder();
            sb.AppendLine($"{s.StartedLocal:dddd d MMMM yyyy, HH:mm}");
            sb.AppendLine();
            sb.AppendLine($"{s.FileCount:N0} files and {s.FolderCount:N0} folders, " +
                          $"{s.BytesRecycled / (1024.0 * 1024.0):N1} MB.");

            if (s.Steps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Cleaned:");
                foreach (var step in s.Steps) sb.AppendLine("   " + step);
            }

            // Shown because it is the honest other half of the report: what the cleaner
            // decided NOT to touch is as much a part of what it did as what it removed.
            if (s.Skipped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Left alone:");
                foreach (var skip in s.Skipped) sb.AppendLine("   " + skip);
            }

            if (s.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Problems:");
                foreach (var err in s.Errors) sb.AppendLine("   " + err);
            }

            sb.AppendLine();
            sb.AppendLine("Anything already emptied from the Recycle Bin cannot be put " +
                          "back, and will be reported as no longer there.");

            DetailText.Text = sb.ToString();
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var s = Selected;
            if (s == null) return;

            RestoreButton.IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var result = RecycleBin.Restore(s.Recycled.Select(i => i.Path));

                // Report all three outcomes, not just the happy one. "Restored 900" while
                // silently dropping 304 that had been emptied from the bin would be the
                // kind of half-truth this whole feature exists to avoid.
                var sb = new StringBuilder();
                sb.Append($"Put back {result.Restored:N0} of {s.Recycled.Count:N0} items.");
                if (result.NotFound > 0)
                    sb.Append($" {result.NotFound:N0} were no longer in the Recycle Bin.");
                if (result.Problems.Count > 0)
                    sb.Append($" {result.Problems.Count:N0} could not be moved back.");

                ResultText.Text = sb.ToString();
                ResultText.Visibility = Visibility.Visible;

                foreach (var problem in result.Problems.Take(20))
                    LogHelper.Log("Restore problem: " + problem);

                CustomMessageBox.Show(
                    sb.ToString(),
                    "Restore",
                    result.Problems.Count > 0 || result.NotFound > 0
                        ? CustomMessageBox.Kind.Warning
                        : CustomMessageBox.Kind.Information);
            }
            catch (Exception ex)
            {
                LogHelper.Log("Restore failed: " + ex);
                CustomMessageBox.Show("The restore could not be completed:\n\n" + ex.Message,
                                      "Restore", CustomMessageBox.Kind.Error);
            }
            finally
            {
                Cursor = null;
                RestoreButton.IsEnabled = Selected?.Recycled.Count > 0;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
