// File: Dialogs/SanityCheckDialog.xaml.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SystemOptimizer.Core.Logging;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.SanityCheck;

namespace SystemOptimizer.Dialogs
{
    /// <summary>
    /// What Sanity Check found.
    ///
    /// The one rule this window exists to obey: EVERY FINDING SHOWS BOTH OBSERVED FACTS.
    /// "Your network is slow" is worthless. "This adapter supports 2.5 Gb. The link
    /// negotiated 1 Gb." is the entire product - it is what lets somebody decide in two
    /// seconds whether it matters to them, without trusting us at all.
    ///
    /// It is also why findings are not styled as errors. Nothing here has failed; the
    /// honest register is "this looks inconsistent, here is how to tell whether it matters
    /// to you", and red boxes would say something the checks cannot support.
    /// </summary>
    public partial class SanityCheckDialog : Window
    {
        private SanityReport _report;

        public SanityCheckDialog(SanityReport report)
        {
            InitializeComponent();
            Show(report);
        }

        private void Show(SanityReport report)
        {
            _report = report;
            HeadlineText.Text = report.Headline;

            SubText.Text = report.Disabled.Count == 0
                ? "Each check compares two things that should agree. Findings are not faults - " +
                  "read when to ignore them before changing anything."
                : $"Each check compares two things that should agree. " +
                  $"{report.Disabled.Count} {(report.Disabled.Count == 1 ? "check is" : "checks are")} " +
                  "not running - see the bottom of this list.";

            ResultsPanel.Children.Clear();

            foreach (var finding in report.Findings.OrderBy(f => (int)f.Confidence))
                ResultsPanel.Children.Add(FindingCard(finding));

            // Passes are shown, not hidden. A check that ran and agreed is evidence, and
            // seeing WHAT it compared is what makes a quiet result believable rather than
            // just silent.
            var quiet = report.Outcomes
                .Where(o => o.Result.Verdict != Verdict.Finding)
                .ToList();

            if (quiet.Count > 0)
                ResultsPanel.Children.Add(QuietSection(quiet));

            if (report.Disabled.Count > 0)
                ResultsPanel.Children.Add(DisabledSection());
        }

        private UIElement FindingCard(CheckOutcome outcome)
        {
            var stack = new StackPanel();

            stack.Children.Add(Heading(outcome.Title));

            // The two facts, labelled and side by side. This is the whole point.
            stack.Children.Add(FactLine("Expected", outcome.Result.Expected));
            stack.Children.Add(FactLine("Found", outcome.Result.Actual));

            stack.Children.Add(new TextBlock
            {
                Text = outcome.Result.Why,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Style = (Style)FindResource("LabelTextStyle")
            });

            // A finding that only says "Probable" tells the reader nothing useful. Say what
            // it means: that we are inferring, and they should check.
            if (outcome.Confidence != Confidence.Certain)
                stack.Children.Add(new TextBlock
                {
                    Text = "This one involves a judgement rather than a straight reading, so " +
                           "it is worth confirming yourself before acting on it.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                    Style = (Style)FindResource("LabelTextStyle")
                });

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };
            actions.Children.Add(Link("What this means, and when to ignore it",
                                      () => OpenGuide(outcome.Id)));
            actions.Children.Add(Link("Not a problem on this PC",
                                      () => DismissFinding(outcome), leftMargin: 18));
            stack.Children.Add(actions);

            return Card(stack);
        }

        private UIElement FactLine(string label, string text)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            // A star column, not Auto: a TextBlock in a horizontal StackPanel never wraps,
            // because the panel measures its children with infinite width along its
            // orientation. That already cost this app a dialog with text running off the
            // edge, so nothing here lays text out in a horizontal stack.
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var name = new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("LabelTextStyle"),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(name, 0);

            var value = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Grid.SetColumn(value, 1);

            grid.Children.Add(name);
            grid.Children.Add(value);
            return grid;
        }

        private UIElement QuietSection(System.Collections.Generic.List<CheckOutcome> quiet)
        {
            var stack = new StackPanel();
            stack.Children.Add(Heading(
                quiet.Count == 1 ? "The other check" : $"The other {quiet.Count} checks"));

            foreach (var outcome in quiet)
            {
                string detail = outcome.Result.Verdict switch
                {
                    Verdict.Pass => $"{outcome.Result.Expected}, and {outcome.Result.Actual}.",
                    // Never dressed up as a pass. A check that could not read one of its two
                    // facts has not agreed with anything - saying otherwise is exactly the
                    // rot this design exists to prevent.
                    Verdict.Inconclusive => "Could not be answered. " + outcome.Result.InconclusiveReason,
                    _ => outcome.Result.InconclusiveReason
                };

                string prefix = outcome.Result.Verdict switch
                {
                    Verdict.Pass => "Fine",
                    Verdict.Inconclusive => "Unknown",
                    _ => "Not applicable"
                };

                stack.Children.Add(new TextBlock
                {
                    Text = $"{outcome.Title} - {prefix.ToLowerInvariant()}. {Sentence(detail)}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                    Style = (Style)FindResource("LabelTextStyle")
                });
            }

            return Card(stack);
        }

        private UIElement DisabledSection()
        {
            var stack = new StackPanel();
            stack.Children.Add(Heading("Checks that are not running"));

            foreach (var disabled in _report.Disabled)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"{disabled.Title} - {disabled.Reason}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                    Style = (Style)FindResource("LabelTextStyle")
                });

                // Both self-disabling mechanisms are deliberately sticky, so there has to be
                // a way back that is not "delete a file we never mentioned". A feature that
                // switches itself off with no visible off switch is indistinguishable from a
                // broken one.
                if (disabled.Reinstatable)
                {
                    string id = disabled.Id;
                    var link = Link("Turn this check back on", () => Reinstate(id));
                    link.Margin = new Thickness(0, 2, 0, 4);
                    stack.Children.Add(link);
                }
            }

            return Card(stack);
        }

        /// <summary>
        /// Capitalises the first letter.
        ///
        /// Expected and Actual are written as fragments on purpose - "the memory is rated
        /// for 6400 MT/s" reads correctly beside a label and inside a sentence, which is
        /// where they are used most. Starting a sentence with one gives "Memory speed -
        /// fine. the memory is rated...", so the one place that does start a sentence with
        /// them fixes it here rather than making every check write two versions.
        /// </summary>
        private static string Sentence(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        /// <summary>
        /// A card heading. Foreground is set explicitly rather than inherited: without it
        /// the heading picks up the window's default and renders DIMMER than the text
        /// underneath it, which reads as disabled and puts the emphasis exactly backwards.
        /// Caught by looking at the rendered window, not by reading the code.
        /// </summary>
        private TextBlock Heading(string text)
        {
            var heading = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            return heading;
        }

        private Border Card(UIElement content)
        {
            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = (CornerRadius)FindResource("CardCorner"),
                Child = content
            };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            return border;
        }

        private TextBlock Link(string text, Action onClick, double leftMargin = 0)
        {
            var link = new TextBlock
            {
                Text = text,
                Cursor = Cursors.Hand,
                TextDecorations = TextDecorations.Underline,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(leftMargin, 0, 0, 0),
                Style = (Style)FindResource("LabelTextStyle")
            };
            link.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            link.MouseLeftButtonUp += (_, __) => onClick();
            return link;
        }

        private void DismissFinding(CheckOutcome outcome)
        {
            SanityRunner.Dismiss(outcome.Id);

            Shell.CustomMessageBox.Show(
                "This finding is hidden for now. If you dismiss it a second time it will " +
                "be muted on this PC for good - you can turn it back on from this window.",
                "Dismissed",
                Shell.CustomMessageBox.Kind.Information);

            RunAgain_Click(null, null);
        }

        private void Reinstate(string checkId)
        {
            SanityRunner.Reinstate(checkId);
            RunAgain_Click(null, null);
        }

        /// <summary>
        /// Opens the guide at this check's own anchor. The Id doubles as the anchor, so
        /// there is no second mapping to fall out of step with the checks.
        /// </summary>
        private void OpenGuide(string checkId)
        {
            try
            {
                // WRITTEN HERE, NOW, from the same registry the checks came from - not
                // shipped as a file beside the executable and not embedded as a resource.
                //
                // Embedding was the obvious way to keep the publish a single file, but it
                // cannot work: the guide is generated by running the built program, so it
                // does not exist until after the compile that would have to embed it.
                // Generating on demand is better than both anyway. It keeps the publish to
                // one file, it cannot go stale against the running build because there is
                // no stored copy to go stale, and it writes somewhere user-writable rather
                // than into Program Files.
                //
                // Rewritten every time on purpose. It is a few milliseconds, and a cached
                // copy is the only way this could ever disagree with the checks.
                var problems = GuideWriter.Write(AppPaths.GuideDir, CheckRegistry.All,
                                                 Shell.AppFont.Name);
                if (problems.Count > 0)
                {
                    // Unreachable through a normal build - this is exactly what the build
                    // step refuses to let through - so say so plainly rather than pretend.
                    LogHelper.Log("Sanity Check guide is incomplete: " + string.Join("; ", problems));
                    Shell.CustomMessageBox.Show(
                        "The guide could not be produced because a check's documentation is " +
                        "incomplete. This should not be possible in a released build.",
                        "Guide unavailable", Shell.CustomMessageBox.Kind.Warning);
                    return;
                }

                string target = new Uri(Path.Combine(AppPaths.GuideDir, "index.html")).AbsoluteUri +
                                (string.IsNullOrEmpty(checkId) ? "" : "#" + checkId);
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogHelper.Log("Opening the Sanity Check guide failed: " + ex);
                Shell.CustomMessageBox.Show(
                    "The guide could not be opened: " + ex.Message,
                    "Guide unavailable", Shell.CustomMessageBox.Kind.Warning);
            }
        }

        private void GuideLink_Click(object sender, MouseButtonEventArgs e) => OpenGuide(null);

        /// <summary>
        /// Opens the list, then runs again so the window cannot sit there showing results
        /// from a set of checks the user has just changed.
        /// </summary>
        private void ChooseChecks_Click(object sender, RoutedEventArgs e)
        {
            new SanityCheckOptionsDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            }.ShowDialog();

            RunAgain_Click(null, null);
        }

        private void RunAgain_Click(object sender, RoutedEventArgs e)
        {
            RunAgainButton.IsEnabled = false;
            try { Show(SanityRunner.Run()); }
            finally { RunAgainButton.IsEnabled = true; }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
