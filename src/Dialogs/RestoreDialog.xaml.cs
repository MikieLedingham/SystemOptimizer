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
                // "Back where it belongs" is the outcome being reported, and an item a
                // parent folder restored - or a previous run already restored - is back.
                // Counting those as moved separately would overstate the work; counting
                // them as failures understates the result. They are their own line.
                int back = result.Restored + result.AlreadyBack;
                int total = s.Recycled.Count;

                // "1 were already back in place" and "Put back 75 of 75 items. 75 were
                // already back in place" were both wrong: one ungrammatical, the other
                // contradicting itself by announcing work it had not done. Restoring twice
                // is a perfectly ordinary thing to do, and the second time should read as
                // reassurance rather than as a puzzle.
                string Were(int n) => n == 1 ? "was" : "were";
                string Items(int n) => n == 1 ? "item" : "items";

                var sb = new StringBuilder();
                if (result.Restored == 0 && back == total && total > 0)
                {
                    sb.Append(total == 1
                        ? "That item was already back in place - nothing needed moving."
                        : $"All {total:N0} items were already back in place - nothing needed moving.");
                }
                else
                {
                    sb.Append($"Put back {back:N0} of {total:N0} {Items(total)}.");
                    if (result.AlreadyBack > 0)
                        sb.Append($" {result.AlreadyBack:N0} {Were(result.AlreadyBack)} already back in place.");
                }

                if (result.NotFound > 0)
                    sb.Append($" {result.NotFound:N0} {Were(result.NotFound)} no longer in the Recycle Bin.");
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
