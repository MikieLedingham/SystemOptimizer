// File: Helpers/CustomMessageBox.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SystemOptimizer.Shell
{
    /// <summary>
    /// The application's message box: themed, and part of the app rather than a stock
    /// white system dialog.
    ///
    /// The earlier version of this file was structurally broken and had been for some time.
    /// A botched edit had left a bare `{ { }; ... };` where a Click handler used to be, so
    /// the OK button had no handler at all, the "suppress" preference was written the
    /// instant Show() was called, and dialog.Close() ran BEFORE dialog.ShowDialog() -
    /// meaning every single call threw "Cannot set Visibility or call Show ... after a
    /// Window has closed". It compiled cleanly, which is why nobody noticed. It also
    /// painted itself with a random Clipboard.*.png from the old artwork, all of which was
    /// deleted, and it parsed preferences.json by hand with `dynamic`.
    ///
    /// Rewritten: colours come from the live palette via DynamicResource, so these boxes
    /// follow Appearance ▸ Dark/Light/Follow Windows with everything else, and the title
    /// bar is themed by ThemeManager's class handler like every other window.
    /// </summary>
    public static class CustomMessageBox
    {
        public enum Kind { Information, Warning, Error }

        public static void Show(string message, string title = "System Optimizer",
                                Kind kind = Kind.Information)
            => ShowCore(message, title, kind, null);

        /// <summary>
        /// Adds a "don't show this again" checkbox. Returns true if it was ticked, and
        /// leaves storing that to the caller - this class no longer writes preferences
        /// behind anyone's back.
        /// </summary>
        public static bool ShowWithSuppress(string message, string title,
                                            string suppressText, Kind kind = Kind.Information,
                                            IEnumerable<Choice> choices = null)
            => ShowCore(message, title, kind, suppressText, null, choices) == 1;

        /// <summary>
        /// Something the box SUGGESTS, that it can also carry out.
        ///
        /// A warning that says "use Restart as administrator now, or tick Always run as
        /// administrator" and then offers neither leaves the reader to go and find two
        /// controls they have just been told about. Advice a dialog can act on should be
        /// actionable in the dialog.
        ///
        /// The action runs AFTER the box closes, so it is free to open another window,
        /// close the one underneath, or shut the application down for an elevated restart
        /// without unwinding a modal it is still inside.
        /// </summary>
        public sealed class Choice
        {
            public string Text { get; set; }
            public Action Invoke { get; set; }
        }

        /// <summary>
        /// A yes/no confirmation with a named action button, returning true only if the
        /// user chose that action.
        ///
        /// Cancel is the DEFAULT: pressing Enter or Escape, or closing the window, all
        /// mean no. For the one irreversible operation in this application, the safe
        /// answer has to be the one you get by doing nothing.
        /// </summary>
        public static bool Confirm(string message, string title, string confirmText,
                                   Kind kind = Kind.Warning)
            => ShowCore(message, title, kind, null, confirmText) == Confirmed;

        // ShowCore returns a tri-state through two bools rather than an enum, to keep the
        // existing suppress-checkbox contract intact.
        private const int Confirmed = 1;

        private static bool ShowCore(string message, string title, Kind kind, string suppressText)
            => ShowCore(message, title, kind, suppressText, null, null) == 1;

        private static int ShowCore(string message, string title, Kind kind,
                                    string suppressText, string confirmText)
            => ShowCore(message, title, kind, suppressText, confirmText, null);

        private static int ShowCore(string message, string title, Kind kind,
                                    string suppressText, string confirmText,
                                    IEnumerable<Choice> choices)
        {
            // Material Icons glyphs: warning, error, info.
            string glyph = kind == Kind.Warning ? "" : kind == Kind.Error ? "" : "";
            string brushKey = kind == Kind.Warning ? "WarningBrush"
                            : kind == Kind.Error ? "ErrorBrush"
                            : "AccentBrush";

            var icon = new TextBlock
            {
                Text = glyph,
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0)
            };
            if (Application.Current?.TryFindResource("MaterialIcons") is FontFamily mi)
                icon.FontFamily = mi;
            icon.SetResourceReference(TextBlock.ForegroundProperty, brushKey);

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                FontSize = 14
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            // A Grid, NOT a horizontal StackPanel. A StackPanel measures its children with
            // infinite width along its orientation, so TextWrapping never engages and the
            // message runs straight off the edge of the window - which is exactly what it
            // did: "so they are sw", "or tick "Alw". A star column gives the TextBlock a
            // real width to wrap inside.
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 1);
            header.Children.Add(icon);
            header.Children.Add(text);

            var body = new StackPanel();
            body.Children.Add(header);

            // Actionable suggestions, indented to line up under the message rather than
            // under the icon. Deferred until after the box closes: see Choice.
            Action pending = null;
            if (choices != null)
            {
                foreach (var choice in choices)
                {
                    if (choice == null || string.IsNullOrWhiteSpace(choice.Text)) continue;

                    var link = new TextBlock
                    {
                        Text = choice.Text,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 14,
                        Margin = new Thickness(34, 10, 0, 0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        TextDecorations = TextDecorations.Underline
                    };
                    link.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

                    var act = choice.Invoke;
                    link.MouseLeftButtonUp += (_, __) => { pending = act; };
                    body.Children.Add(link);
                }
            }

            CheckBox suppress = null;
            if (!string.IsNullOrEmpty(suppressText))
            {
                suppress = new CheckBox
                {
                    Content = suppressText,
                    Margin = new Thickness(34, 14, 0, 0)
                };
                body.Children.Add(suppress);
            }

            bool confirmMode = !string.IsNullOrEmpty(confirmText);
            int answer = 0;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            if (confirmMode)
            {
                // Cancel first and to the LEFT, and it is both the default and the cancel
                // button: Enter, Escape and closing the window all mean no. The action
                // button is deliberately NOT default - the destructive choice should
                // require aiming at it, not just pressing Enter out of habit.
                var cancel = new Button
                {
                    Content = "Cancel",
                    Width = 90,
                    IsDefault = true,
                    IsCancel = true,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                if (Application.Current?.TryFindResource("PrimaryButtonStyle") is Style cancelStyle)
                    cancel.Style = cancelStyle;
                cancel.Click += (_, __) => { answer = 0; };
                buttons.Children.Add(cancel);

                var confirm = new Button
                {
                    Content = confirmText,
                    MinWidth = 90,
                    Padding = new Thickness(14, 0, 14, 0)
                };
                if (Application.Current?.TryFindResource("SecondaryButtonStyle") is Style confirmStyle)
                    confirm.Style = confirmStyle;
                confirm.Click += (_, __) => { answer = Confirmed; };
                buttons.Children.Add(confirm);
            }
            else
            {
                var ok = new Button
                {
                    Content = "OK",
                    Width = 90,
                    IsDefault = true,
                    IsCancel = true
                };
                if (Application.Current?.TryFindResource("PrimaryButtonStyle") is Style primary)
                    ok.Style = primary;
                buttons.Children.Add(ok);
            }
            body.Children.Add(buttons);

            var root = new Border { Padding = new Thickness(20), Child = body };

            var dialog = new Window
            {
                Title = title,
                Content = root,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = ActiveOwner()
            };
            if (dialog.Owner == null)
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.SetResourceReference(Window.BackgroundProperty, "WindowBackgroundBrush");
            // This window is built in code, so nothing else gives it the application font -
            // every other window sets it from XAML. Set here, on the window, because
            // FontFamily inherits down the tree: the children used to name "Segoe UI"
            // individually, which meant they would have quietly kept the old face if the
            // application font ever changed.
            dialog.SetResourceReference(Window.FontFamilyProperty, "AppFont");

            foreach (UIElement child in buttons.Children)
                if (child is Button b) b.Click += (_, __) => dialog.Close();

            // A chosen suggestion closes the box first, then acts.
            foreach (var child in body.Children)
                if (child is TextBlock link && link.Cursor == System.Windows.Input.Cursors.Hand)
                    link.MouseLeftButtonUp += (_, __) => dialog.Close();

            dialog.ShowDialog();

            // AFTER ShowDialog returns, so the modal stack has unwound. An action here may
            // open a window, close the one that raised this box, or restart the
            // application elevated - none of which is safe from inside a modal that is
            // still up. Doing it inline is how "Restart as administrator now" once threw
            // an InvalidOperationException at the caller.
            pending?.Invoke();

            if (confirmMode) return answer;
            return suppress?.IsChecked == true ? 1 : 0;
        }

        /// <summary>
        /// The window this box should sit over. Owning it keeps the box in front of the
        /// dialog that raised it and off the taskbar; a message box that can end up behind
        /// its own parent looks like a freeze.
        /// </summary>
        private static Window ActiveOwner()
        {
            var app = Application.Current;
            if (app == null) return null;
            foreach (Window w in app.Windows)
                if (w.IsActive && w.IsLoaded) return w;
            return app.MainWindow != null && app.MainWindow.IsLoaded ? app.MainWindow : null;
        }
    }
}
