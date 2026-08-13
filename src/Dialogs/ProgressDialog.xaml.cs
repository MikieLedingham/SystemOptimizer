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

        /// <summary>
        /// This run's own log file, so the summary can offer to open it. Set by the engine
        /// once the log has actually been written; empty until then, and the link is only
        /// shown when the file is really there.
        /// </summary>
        public string RunLogPath { get; set; }
        public ProgressDialog()
        {
            InitializeComponent();
            Instance = this;

            // Deliberately NOT repositioned. This used to shove itself into the
            // bottom-right corner on Loaded, which silently overrode
            // WindowStartupLocation - so setting that in the markup did nothing and the
            // window appeared in the corner regardless. The run is quick and the list
            // carries the only account of what was skipped, left alone or refused, so it
            // belongs in the middle of the screen where it will be read.
            Loaded += (s, e) => Topmost = false;
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
        /// <summary>
        /// The move is ONE batched shell operation, so this can only report "about to" and
        /// "finished" - there is no per-file callback to count, and there is deliberately
        /// no intention of making one call per file to obtain a number.
        ///
        /// It used to say "0 of 4,716" for the whole operation, which on a large run meant
        /// minutes of a counter frozen at zero. That is indistinguishable from a crash, and
        /// it was read as one. A progress readout that cannot progress is worse than none:
        /// it invites the user to kill a program that is working.
        ///
        /// So while the operation runs, say what is happening and that it takes a while.
        /// The count appears when there is a real one to show.
        /// </summary>
        public void ReportRecycling(int done, int total)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (done == 0 && total > 0)
                {
                    ActivityText.Text = $"Moving {total:N0} items to the Recycle Bin";
                    TallyText.Text = "One Windows operation - this can take several minutes";
                }
                else
                {
                    ActivityText.Text = "Moving to the Recycle Bin";
                    TallyText.Text = $"{done:N0} of {total:N0}";
                }
            }));
        }
        // Foregrounds are resource references, not literal brushes. These used to be
        // Brushes.Black and Brushes.DarkRed, which are invisible on the 2.0 dark surface;
        // SetResourceReference also keeps them correct if the theme changes mid-cleanup.
        /// <summary>
        /// A completed stage of the SCAN, not a completed removal.
        ///
        /// This used to read "X cleaned." and it appeared while the walk was still going -
        /// before a single file had moved, because everything moves later in one batch.
        /// So the list claimed six areas were cleaned and then began the work, and a step
        /// that turned out to remove nothing had already ticked. The summary at the end is
        /// where the real figures live, and it is the only place that can honestly say
        /// what was removed.
        /// </summary>
        public void AddStep(string label)
        {
            Dispatcher.Invoke(() =>
            {
                var step = new TextBlock
                {
                    Text = $"✔ {label}",
                    TextWrapping = TextWrapping.Wrap
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
                    Text = $"• {note}.",
                    TextWrapping = TextWrapping.Wrap
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
                    Text = $"✖ {label}",
                    TextWrapping = TextWrapping.Wrap
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
                         + "so nothing has been permanently deleted.",
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                };
                final.SetResourceReference(ForegroundProperty, "SuccessBrush");
                StatusList.Items.Add(spacer);
                StatusList.Items.Add(final);

                // The log link is NOT here. It was, and it was useless: this window closes
                // itself five seconds later, so the offer expired before it could be
                // taken. It lives on the summary window, which waits for the user.
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
                            Owner = Application.Current.MainWindow,
                            // Handed on from this window, which is where the engine put it.
                            // The summary is the one that waits for the user, so it is the
                            // one that can usefully offer the log.
                            RunLogPath = RunLogPath
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
