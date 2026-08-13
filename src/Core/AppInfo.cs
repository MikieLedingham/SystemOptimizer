// File: Core/AppInfo.cs
using System.Reflection;

namespace SystemOptimizer.Core
{
    /// <summary>
    /// What the program calls itself. One source, because there were three and they
    /// disagreed.
    ///
    /// The main window read <c>AssemblyInformationalVersion</c> and printed "2.0.0";
    /// About and Diagnostics read <c>AssemblyName.Version</c> and printed "2.0.0.0". Both
    /// were correct readings of different attributes, which is the fault: nobody had
    /// decided which one the product's version IS, so the answer depended on which window
    /// you opened.
    ///
    /// The informational version is the right one to show. It is the string the csproj
    /// sets deliberately (&lt;Version&gt;), it is not padded to four parts by the build,
    /// and it is what a person would say out loud. AssemblyVersion still exists as
    /// 2.0.0.0 for the loader, which is its job.
    /// </summary>
    public static class AppInfo
    {
        /// <summary>"2.0.0". The version to show a human, anywhere one is shown.</summary>
        public static string Version { get; } = Resolve();

        /// <summary>
        /// The one-sentence description of what the product is, from the csproj.
        ///
        /// Read from the assembly rather than written into the About window, for the same
        /// reason as the version above: the sentence also appears in the file's own
        /// properties, and two copies of it would eventually describe two different
        /// products. Adding Sanity Check to one and not the other is precisely how that
        /// would have started.
        /// </summary>
        public static string Description { get; } =
            typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
            ?? "Windows cleanup, RAM boost, system overlay and Sanity Check.";

        /// <summary>
        /// Where the program lives. One constant, for the same reason the version is:
        /// the base URL once had FOUR copies - a const here, a const in Diagnostics, and
        /// two literals in About's markup, one of which was a tooltip nobody would think
        /// to update. Every one of them pointed somewhere that returned 404.
        ///
        /// The program still makes no network call of any kind. These are strings handed to
        /// the shell to open in the user's browser, which is the whole of the update
        /// feature and is deliberately all of it.
        /// </summary>
        public const string RepoUrl = "https://github.com/MikieLedingham/SystemOptimizer";

        /// <summary>The latest release, for the About window's "Check for updates".</summary>
        public const string ReleasesUrl = RepoUrl + "/releases/latest";

        /// <summary>The issue list, for About's "Support" link.</summary>
        public const string IssuesUrl = RepoUrl + "/issues";

        /// <summary>A blank issue, for Diagnostics' "Send to author".</summary>
        public const string NewIssueUrl = IssuesUrl + "/new";

        /// <summary>The privacy policy as published, for About's "Privacy" link.</summary>
        public const string PrivacyUrl = RepoUrl + "/blob/main/PRIVACY.md";

        private static string Resolve()
        {
            var assembly = typeof(AppInfo).Assembly;

            // Deliberately this assembly rather than the entry assembly: under the smoke
            // harness the entry assembly is the harness, and About would have reported the
            // test tool's version instead of the product's.
            string informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // The SDK appends "+<commit sha>" unless told not to, and the csproj does
                // tell it not to. Trimmed anyway, so a change to that setting cannot put a
                // forty-character hash in the window footer again - which it once did.
                int plus = informational.IndexOf('+');
                return plus > 0 ? informational.Substring(0, plus) : informational;
            }

            return assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }
}
