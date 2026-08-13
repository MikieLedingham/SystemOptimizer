// File: ProgressDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SystemOptimizer.Core.Cleanup;
using SystemOptimizer.Core.Settings;
namespace SystemOptimizer.Dialogs
{
    public partial class ProgressDialog : Window
    {
        private DispatcherTimer autoCloseTimer;
        public static ProgressDialog Instance { get; private set; }  // Singleton instance
        public bool CleanupSummaryReady { get; set; } = false;
        public bool RamWasCleaned { get; set; } = false;
        public ProgressDialog()
        {
            InitializeComponent();
            Instance = this;
            // Position window to the bottom-right side of the screen
            Loaded += (s, e) =>
            {
                Left = SystemParameters.WorkArea.Width - Width - 10;
                Top = SystemParameters.WorkArea.Height - Height - 10;
                Topmost = false;
            };
        }
        public void InitializeProgress(int totalSteps)
        {
            Dispatcher.Invoke(() =>
            {
                StatusList.Items.Clear();
                ActivityText.Text = "";
                TallyText.Text = "";
            });
        }

        private DateTime _lastActivityPaint = DateTime.MinValue;

        /// <summary>
        /// The live line under the list: what is being looked at, and the running totals.
        ///
        /// Throttled to about twelve repaints a second. The walk visits tens of thousands
        /// of files, and marshalling every one of them to the UI thread would make the
        /// progress display itself the slowest part of the cleanup - the report would cost
        /// more than the work.
        ///
        /// <paramref name="force"/> bypasses the throttle for stage changes and the final
        /// figure, which must never be dropped just because they arrived too soon after
        /// the previous paint.
        /// </summary>
        public void ReportActivity(string stage, string detail, int examined, int selected, bool force = false)
        {
            if (!force && (DateTime.UtcNow - _lastActivityPaint).TotalMilliseconds < 80) return;
            _lastActivityPaint = DateTime.UtcNow;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ActivityText.Text = string.IsNullOrEmpty(detail) ? stage : $"{stage}  -  {detail}";
                TallyText.Text = selected > 0
                    ? $"{examined:N0} examined, {selected:N0} to recycle"
                    : $"{examined:N0} examined";
            }));
        }

        /// <summary>The Recycle Bin phase, which has a known total to count against.</summary>
        public void ReportRecycling(int done, int total)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ActivityText.Text = "Moving to the Recycle Bin";
                TallyText.Text = $"{done:N0} of {total:N0}";
            }));
        }
        // Foregrounds are resource references, not literal brushes. These used to be
        // Brushes.Black and Brushes.DarkRed, which are invisible on the 2.0 dark surface;
        // SetResourceReference also keeps them correct if the theme changes mid-cleanup.
        public void AddStep(string label)
        {
            Dispatcher.Invoke(() =>
            {
                var step = new TextBlock
                {
                    Text = $"✔ {label} cleaned."
                };
                step.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
                StatusList.Items.Add(step);
                StatusList.ScrollIntoView(step);
            });
        }
        /// <summary>
        /// Something the user should know that is not a failure - files in use, for
        /// instance. Deliberately not a cross: see LogNote in CleanupHelper.
        /// </summary>
        public void AddNote(string note)
        {
            Dispatcher.Invoke(() =>
            {
                var line = new TextBlock
                {
                    Text = $"• {note}."
                };
                line.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
                StatusList.Items.Add(line);
                StatusList.ScrollIntoView(line);
            });
        }
        public void AddError(string label)
        {
            Dispatcher.Invoke(() =>
            {
                var error = new TextBlock
                {
                    // The caller's text already says what went wrong. Appending "failed."
                    // produced rows reading "Shell delete failed (0x00000020) failed."
                    Text = $"✖ {label}"
                };
                error.SetResourceReference(ForegroundProperty, "ErrorBrush");
                StatusList.Items.Add(error);
                StatusList.ScrollIntoView(error);
            });
        }
        public void ShowFinalSummary()
        {
            Dispatcher.Invoke(() =>
            {
                var spacer = new TextBlock { Text = " " };
                var final = new TextBlock
                {
                    // The window resizes now, so this no longer needs hard line breaks
                    // to fit a fixed-width panel.
                    // "All selected areas have been optimised" said nothing about what
                    // happened to the files, and the sentence it replaced sat directly
                    // above a summary claiming space had been freed. Nothing has left the
                    // machine at this point, so this says where things went instead.
                    Text = "Cleanup complete. Everything found was moved to the Recycle Bin, "
                         + "so nothing has been permanently deleted. "
                         + "The full log is available from the right-click menu.",
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                };
                final.SetResourceReference(ForegroundProperty, "SuccessBrush");
                StatusList.Items.Add(spacer);
                StatusList.Items.Add(final);
                StatusList.ScrollIntoView(final);
                CleanupSummaryReady = true;
            });
        }
        public void MarkComplete()
        {
            Dispatcher.Invoke(() =>
            {
                ShowFinalSummary();
                autoCloseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                autoCloseTimer.Tick += (s, e) =>
                {
                    autoCloseTimer.Stop();
                    Close();
                    if (RamWasCleaned)
                    {
                        string msg = PreferencesManager.GetLastRamBoostMessage();
                        new RamBoostResult(msg).ShowDialog();
                    }
                    else
                    {
                        var dlg = new SuccessDialog(
                            CleanupHelper.TotalFilesDeleted,
                            CleanupHelper.TotalFoldersDeleted,
                            CleanupHelper.LastUsedRamMB,
                            CleanupHelper.TotalBytesFreed)
                        {
                            Owner = Application.Current.MainWindow
                        };
                        dlg.ShowDialog();
                    }
                };
                autoCloseTimer.Start();
            });
        }
        protected override void OnClosed(EventArgs e)
        {
            Instance = null;
            base.OnClosed(e);
        }
    }
}
