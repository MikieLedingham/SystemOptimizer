// File: Dialogs/SanityCheckOptionsDialog.xaml.cs
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SystemOptimizer.SanityCheck;

namespace SystemOptimizer.Dialogs
{
    /// <summary>
    /// Which checks run on this PC.
    ///
    /// The answer to the fact that most checks are irrelevant to most machines. The check
    /// list can grow without every user paying for the growth, which is what makes it
    /// reasonable to keep writing checks at all.
    ///
    /// Save-on-tick, with no OK or Remember gate. That rule was learned the hard way here
    /// more than once - a "Remember these choices" box that made the real control do
    /// nothing, and an OK that RESET the settings when it was unticked. Mikie's words:
    /// "the check / uncheck by itself should be the save. if i missed it, others will."
    /// </summary>
    public partial class SanityCheckOptionsDialog : Window
    {
        private bool _loading = true;   // true during construction, always - see RamOptionsDialog

        public SanityCheckOptionsDialog()
        {
            InitializeComponent();
            Populate();
            _loading = false;
        }

        private void Populate()
        {
            _loading = true;
            try
            {
                ChecksPanel.Children.Clear();
                var selection = SanityRunner.Selection();

                foreach (var (check, enabled, offNote) in selection)
                {
                    var box = new CheckBox
                    {
                        Content = check.Title,
                        IsChecked = enabled,
                        Tag = check.Id,
                        Margin = new Thickness(0, 0, 0, 2)
                    };
                    box.Checked += Box_Changed;
                    box.Unchecked += Box_Changed;

                    // The summary is what makes the choice informed. It already exists as
                    // the check's own documentation, so there is no second description to
                    // drift away from the first.
                    var description = new TextBlock
                    {
                        Text = check.Doc.Summary,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(24, 0, 0, 0),
                        Style = (Style)FindResource("LabelTextStyle")
                    };

                    var card = new Border
                    {
                        Padding = new Thickness(12, 10, 12, 10),
                        Margin = new Thickness(0, 0, 0, 6),
                        CornerRadius = (CornerRadius)FindResource("CardCorner")
                    };
                    card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");

                    var stack = new StackPanel();
                    stack.Children.Add(box);
                    stack.Children.Add(description);

                    // Only on checks the user switched off themselves. It is the whole
                    // reason this list is worth opening a year later: a PC is not the same
                    // PC after new hardware, and somebody who silenced a finding because it
                    // was true-but-deliberate needs telling that they did.
                    if (!string.IsNullOrEmpty(offNote))
                    {
                        var note = new TextBlock
                        {
                            Text = offNote,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(24, 4, 0, 0),
                            Style = (Style)FindResource("LabelTextStyle")
                        };
                        note.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                        stack.Children.Add(note);
                    }

                    card.Child = stack;

                    ChecksPanel.Children.Add(card);
                }

                int on = selection.Count(s => s.Enabled);
                SummaryText.Text = $"{on} of {selection.Count} checks will run.";
            }
            finally { _loading = false; }
        }

        private void Box_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            var box = (CheckBox)sender;
            bool on = box.IsChecked == true;
            SanityRunner.SetEnabled((string)box.Tag, on);

            // The note appears when a check is switched off and disappears when it is
            // switched back on, so it has to be rebuilt here rather than only on open -
            // otherwise a note would sit under a ticked box until the window was reopened,
            // which is a readout that looks current and is wrong.
            Populate();
        }

        /// <summary>
        /// Back to what each check ships with - which is not "all of them". Scoped to this
        /// window only: the global "clear saved choices" this app used to have also wiped
        /// the boost history and switched automatic boosting off.
        /// </summary>
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            // Forgets the choices rather than writing the defaults back over them. The
            // difference shows: a check that ships switched off would otherwise come out
            // of a reset carrying a note saying the user turned it off, on the day they
            // did the opposite.
            SanityRunner.ResetSelection();
            Populate();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
