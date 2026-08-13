// File: Dialogs/LicenceWindow.xaml.cs
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Dialogs
{
    /// <summary>
    /// The licence and the third-party notices, read from the program's own copy.
    ///
    /// The About box's "Licence (MIT)" link used to open a GitHub URL that returns 404,
    /// so a user could not read the terms of the software they were running. Reading them
    /// should not require a working internet connection or a repository that is not
    /// public yet - and for the icon font's Apache 2.0 attribution, the notice needs to
    /// travel with the binary rather than living only in a repository the user may never
    /// see.
    ///
    /// The text is embedded from the repository's OWN files at build time, so what this
    /// window shows and what the repository presents as authoritative cannot be two
    /// different documents.
    /// </summary>
    public partial class LicenceWindow : Window
    {
        private const string LicenceResource = "SystemOptimizer.LICENSE";
        private const string NoticesResource = "SystemOptimizer.THIRD-PARTY-NOTICES.md";

        public LicenceWindow()
        {
            InitializeComponent();
            LicenceText.Text = BuildText();
        }

        private static string BuildText()
        {
            var text = new StringBuilder();

            string licence = Read(LicenceResource);
            string notices = Read(NoticesResource);

            if (licence == null && notices == null)
            {
                // Says which document is missing rather than showing an empty window. An
                // empty licence window is indistinguishable from "there are no terms".
                return "The licence text could not be read from this copy of the program.\r\n\r\n" +
                       "It is also published with the source code, in LICENSE and " +
                       "THIRD-PARTY-NOTICES.md.";
            }

            if (licence != null) text.Append(licence.TrimEnd()).Append("\r\n\r\n");
            if (notices != null)
            {
                text.Append(new string('=', 70)).Append("\r\n\r\n");
                text.Append(notices.TrimEnd()).Append("\r\n");
            }
            return text.ToString();
        }

        private static string Read(string resourceName)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly()
                                           .GetManifestResourceStream(resourceName);
                if (stream == null) return null;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                LogHelper.Log($"Licence resource '{resourceName}' could not be read: {ex.Message}");
                return null;
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(LicenceText.Text); }
            catch (Exception ex) { LogHelper.Log("Copying the licence failed: " + ex.Message); }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
