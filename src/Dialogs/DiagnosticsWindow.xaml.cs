// File: Dialogs/DiagnosticsWindow.xaml.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows;
using SystemOptimizer.Core.Monitoring;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Logging;
using SystemOptimizer.Shell;

namespace SystemOptimizer.Dialogs
{
    /// <summary>
    /// Diagnostics report, intended to be copied into a GitHub issue.
    ///
    /// 2.0: the self-tests used to sit behind a Run button, so a report copied straight
    /// after opening contained none of them. They run automatically on open now - the
    /// point of this window is producing one complete, pasteable report.
    /// </summary>
    public partial class DiagnosticsWindow : Window
    {
        public DiagnosticsWindow()
        {
            InitializeComponent();
            BuildReport();
        }

        private void BuildReport()
        {
            var sb = new StringBuilder();
            Section(sb, "SYSTEM OPTIMIZER");
            Safe(sb, "Version", GetVersionString);
            Safe(sb, "Build date", GetBuildDateString);
            Safe(sb, "Executable", GetExecutablePath);
            Safe(sb, "Elevated", () => IsRunAsAdmin() ? "yes" : "no");
            Safe(sb, "Theme", () => ThemeManager.CurrentTheme.ToString());

            Section(sb, "WINDOWS");
            // One line, not three. This used to report "OS" (the kernel platform string,
            // "Microsoft Windows NT 10.0.28020.0"), "OS name" (the WMI caption) and
            // "Build" separately. SystemStatsHelper.OsName carries strictly more than all
            // three combined - product, edition, feature update and build.UBR - and costs
            // one WMI query instead of two.
            Safe(sb, "Windows", () => SystemStatsHelper.OsName.Value);
            Safe(sb, ".NET runtime", () => Environment.Version.ToString());
            Safe(sb, "Process", () => Environment.Is64BitProcess ? "64-bit" : "32-bit");
            Safe(sb, "OS bitness", () => Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
            // Machine name is deliberately omitted rather than redacted - it tells a
            // maintainer nothing, and seeing it listed at all makes people uneasy.
            Safe(sb, "Logical CPUs", () => Environment.ProcessorCount.ToString());
            Safe(sb, "Uptime", GetUptime);
            Safe(sb, "Culture", () => System.Globalization.CultureInfo.CurrentCulture.Name);

            Section(sb, "MEMORY AND DISK");
            Safe(sb, "Physical memory", GetMemorySummary);
            Safe(sb, "System drive", GetSystemDriveSummary);

            Section(sb, "PATHS");
            Safe(sb, "Data folder", () => AppPaths.Root);
            Safe(sb, "Logs folder", () => AppPaths.LogsDir);
            Safe(sb, "preferences.json", () => File.Exists(AppPaths.PreferencesFile)
                ? $"present ({new FileInfo(AppPaths.PreferencesFile).Length} bytes)" : "missing");
            Safe(sb, "AppsList.json", () => File.Exists(AppPaths.AppsListFile)
                ? $"present ({new FileInfo(AppPaths.AppsListFile).Length} bytes)" : "missing");
            Safe(sb, "Log files", () => Directory.Exists(AppPaths.LogsDir)
                ? Directory.GetFiles(AppPaths.LogsDir).Length.ToString() : "0");

            Section(sb, "SETTINGS");
            Safe(sb, "Auto RAM cleanup", () => PreferencesManager.GetAutoRamEnabled() ? "on" : "off");
            Safe(sb, "Auto threshold", () => PreferencesManager.GetAutoThreshold() + "%");
            Safe(sb, "Last boost", () =>
            {
                var msg = PreferencesManager.GetLastRamBoostMessage();
                if (string.IsNullOrWhiteSpace(msg)) return "none recorded";
                var when = PreferencesManager.GetLastRamBoostTime();
                var kind = PreferencesManager.GetLastRamBoostWasAutomatic() ? "automatic" : "manual";
                return when == null ? $"{msg} ({kind})" : $"{msg} ({kind}, {when:yyyy-MM-dd HH:mm})";
            });
            Safe(sb, "Auto triggers", () => PreferencesManager.GetAutoTriggerCount().ToString());

            RunSelfTests(sb);

            sb.AppendLine();
            sb.AppendLine($"Report generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            InfoTextBox.Text = Redact(sb.ToString());
        }

        /// <summary>
        /// Strip the two personally identifying things this report would otherwise leak:
        /// the Windows account name - which appears inside every path under the user
        /// profile - and the PC name, which is frequently a real person's name.
        ///
        /// Done on the finished text rather than per field so nothing can be missed by
        /// adding a new probe later.
        /// </summary>
        private static string Redact(string report)
        {
            var user = Environment.UserName;
            var machine = Environment.MachineName;

            if (!string.IsNullOrWhiteSpace(user))
                report = System.Text.RegularExpressions.Regex.Replace(
                    report, System.Text.RegularExpressions.Regex.Escape(user), "<user>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!string.IsNullOrWhiteSpace(machine))
                report = System.Text.RegularExpressions.Regex.Replace(
                    report, System.Text.RegularExpressions.Regex.Escape(machine), "<pc>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return report;
        }

        private static void Section(StringBuilder sb, string name)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"=== {name} ===");
        }

        /// <summary>Never let one failing probe abort the whole report.</summary>
        private static void Safe(StringBuilder sb, string label, Func<string> get)
        {
            string value;
            try { value = get() ?? "(null)"; }
            catch (Exception ex) { value = "error: " + ex.Message; }
            sb.AppendLine($"{label,-18}: {value}");
        }

        private void RunSelfTests(StringBuilder sb)
        {
            Section(sb, "SELF TESTS");
            Check(sb, "Preferences load", () => PreferencesManager.LoadPreferences());
            Check(sb, "Preferences save", () => PreferencesManager.SavePreferences());
            Check(sb, "Log write", () => LogManager.WriteLog($"Diagnostics self-test {DateTime.Now:O}"));
            Check(sb, "Data folder writable", () =>
            {
                var probe = Path.Combine(AppPaths.Root, ".writetest");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
            });
            sb.AppendLine($"{"Overlay",-18}: {(OverlayWindow.Instance == null ? "not running" : "running")}");
        }

        private static void Check(StringBuilder sb, string label, Action act)
        {
            try { act(); sb.AppendLine($"{label,-18}: PASS"); }
            catch (Exception ex) { sb.AppendLine($"{label,-18}: FAIL - {ex.Message}"); }
        }

        private void RunTestButton_Click(object sender, RoutedEventArgs e) => BuildReport();

        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(InfoTextBox.Text); }
            catch { /* clipboard can be locked by another app */ }
        }

        /// <summary>
        /// Copy the report and open a new GitHub issue. The report goes via the clipboard
        /// rather than the URL because a query string long enough to hold it would be
        /// truncated by the browser.
        /// </summary>
        private void SendToAuthorButton_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(InfoTextBox.Text); } catch { }
            try
            {
                Process.Start(new ProcessStartInfo(Core.AppInfo.NewIssueUrl) { UseShellExecute = true });
                CustomMessageBox.Show(
                    "The report has been copied to your clipboard and a new GitHub issue has been opened.\n\n" +
                    "Describe what happened, then paste the report with Ctrl+V.",
                    "Send to author");
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show("Could not open the browser:\n" + ex.Message, "Send to author");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e) => LogManager.OpenLogFolder();

        // === HELPERS ===
        // The same version every other window shows. A diagnostics report that quotes a
        // different version from the About box is worse than one that quotes none.
        private string GetVersionString() => Core.AppInfo.Version;

        /// <summary>
        /// Assembly.Location returns an EMPTY STRING in a single-file publish, which is
        /// how 2.0 ships. Reporting "" for the executable path would be merely useless;
        /// feeding "" to FileInfo below threw. Environment.ProcessPath is the
        /// single-file-safe answer and names the real .exe on disk either way.
        /// </summary>
        private static string GetExecutablePath()
            => Environment.ProcessPath ?? "unknown";

        private string GetBuildDateString()
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "unknown";
            return new FileInfo(path).LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        }

        private static string GetOsCaption() => WmiScalar("Win32_OperatingSystem", "Caption");
        private static string GetOsBuild() => WmiScalar("Win32_OperatingSystem", "BuildNumber");

        /// <summary>
        /// Was a WMI Win32_OperatingSystem.LastBootUpTime query, because the 32-bit
        /// Environment.TickCount wraps after ~25 days and would have understated uptime
        /// on exactly the long-running machines a diagnostics report cares about, and
        /// TickCount64 did not exist on .NET Framework 4.7.2. On .NET 8 it does, so the
        /// WMI round trip is gone.
        /// </summary>
        private static string GetUptime()
        {
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var boot = DateTime.Now - up;
            return $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m (since {boot:yyyy-MM-dd HH:mm})";
        }

        private static string WmiScalar(string cls, string prop)
        {
            using (var s = new ManagementObjectSearcher($"SELECT {prop} FROM {cls}"))
                foreach (ManagementObject o in s.Get())
                    return o[prop]?.ToString() ?? "unknown";
            return "unknown";
        }

        private static string GetMemorySummary()
        {
            var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
            double totalGb = ci.TotalPhysicalMemory / 1024.0 / 1024 / 1024;
            double freeGb = ci.AvailablePhysicalMemory / 1024.0 / 1024 / 1024;
            return $"{totalGb:F1} GB total, {freeGb:F1} GB free";
        }

        private static string GetSystemDriveSummary()
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
            return $"{d.Name} {d.TotalFreeSpace / 1024.0 / 1024 / 1024:F1} GB free of {d.TotalSize / 1024.0 / 1024 / 1024:F1} GB";
        }

        private bool IsRunAsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
