// File: Dialogs/AboutWindow.xaml.cs
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
namespace SystemOptimizer.Dialogs
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionBlock.Text = $"Version {GetAppVersion()}";
            DescriptionBlock.Text = Core.AppInfo.Description;

            // Every outbound address in this window comes from Core.AppInfo. The markup
            // names none of them, so moving the repository is one edit rather than four
            // spread over two files and a tooltip.
            VisitButton.ToolTip = Core.AppInfo.RepoUrl.Replace("https://", string.Empty);
            PrivacyLink.NavigateUri = new Uri(Core.AppInfo.PrivacyUrl);
            SupportLink.NavigateUri = new Uri(Core.AppInfo.IssuesUrl);
        }

        /// <summary>
        /// Shows the licence from the program's own embedded copy. Was a Hyperlink to a
        /// GitHub URL that 404s - one of the five dead outbound links - so the terms of
        /// the software were unreadable from inside the software.
        /// </summary>
        private void LicenceLink_Click(object sender, RoutedEventArgs e)
        {
            new LicenceWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            }.ShowDialog();
        }

        // Was Assembly.GetEntryAssembly().GetName().Version, which printed "2.0.0.0" while
        // the main window's footer printed "2.0.0". Both readings were correct; the fault
        // was that nobody had decided which one the product's version is.
        private string GetAppVersion() => Core.AppInfo.Version;

        private void BtnVisitWebsite_Click(object sender, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo(Core.AppInfo.RepoUrl) { UseShellExecute = true });
        private void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo(Core.AppInfo.ReleasesUrl) { UseShellExecute = true });
        // "Copy system info" was removed from this window. Pressing it produced no
        // visible result, which raised the question of whether it was needed at all.
        //
        // It was not, on three counts. It copied three lines - version, OS name,
        // username - where Diagnostics produces about thirty fields across six
        // sections plus self-tests, and Diagnostics' own "Send to author" copies the
        // report, opens a new issue AND says that it has. It said nothing at all on
        // success, and spoke only when it failed, so working looked identical to
        // broken. And it copied Environment.UserName, which is precisely what the
        // Diagnostics report goes out of its way to redact: the weaker of the two
        // routes leaked the thing the careful one protects.
        //
        // The fix for "it needs a confirmation popup" is not to add one to a button
        // that duplicates a better feature badly. Ctrl+Shift+Alt+D is the route.

        // SupportEmail_RequestNavigate was deleted here. It was byte-for-byte the same as
        // PolicyLink_RequestNavigate below except for the error text, no XAML had wired it
        // up since the support address was replaced by the GitHub links, and there is no
        // longer an email address in this window for it to open.
        private void PolicyLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { MessageBox.Show("Unable to open the link.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            e.Handled = true;
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
