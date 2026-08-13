// File: SanityCheck/Checks/PathEntryCheck.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SystemOptimizer.SanityCheck.Checks
{
    /// <summary>
    /// Every folder listed in PATH should exist.
    ///
    /// THE TWO FACTS: what PATH lists, and what is on disk. Nothing keeps them in step,
    /// and nothing ever complains - Windows silently skips entries that are not there.
    ///
    /// Mild, and included for a reason beyond tidiness: a dead PATH entry is the residue
    /// of an uninstall that did not finish, and it is the kind of thing that turns a
    /// five-minute "why can't it find that command" into an afternoon. Reported as
    /// information, never as a fault.
    /// </summary>
    public sealed class PathEntryCheck : IAnomalyCheck
    {
        public string Id => "PATH.DEAD_ENTRY";
        public string Title => "Folders listed in PATH";

        public Confidence Confidence => Confidence.Certain;
        public DateTime? ReviewBy => null;

        public bool DefaultEnabled => false; // OFF by default: tidiness residue rather than something underperforming, and the most developer-flavoured of the eight

        public CheckDoc Doc => new CheckDoc
        {
            Summary = "Checks that every folder in the PATH setting actually exists.",
            WhyItMatters =
                "PATH is the list of places Windows looks when you type a command without " +
                "giving its full location. Entries pointing at folders that are not there " +
                "are skipped silently. It costs almost nothing, but each one is a leftover " +
                "from something removed without tidying up, and they make the list harder " +
                "to reason about the day a command genuinely cannot be found.",
            WhenToIgnore = new[]
            {
                "The folder is on a drive that is not always connected, or on a network " +
                "share that is only mapped sometimes.",
                "You added it ahead of installing something, and the install is still to come.",
                "It belongs to a development tool you set up per-project, so the folder " +
                "exists only while you are working on that project.",
                "It is a work machine whose PATH is set by policy and not yours to change."
            },
            HowToConfirm = new[]
            {
                "Press Windows and type \"environment variables\", then open Edit the " +
                "system environment variables, Environment Variables.",
                "Look at Path in both lists. Entries that no longer exist are usually " +
                "shown in a different colour.",
                "Or run: $env:Path -split ';' | Where-Object { $_ -and -not (Test-Path $_) }"
            },
            Remedy = new[]
            {
                "Check the folder is not simply on a drive that is currently disconnected.",
                "Open Environment Variables as above.",
                "Select Path, choose Edit, and delete the lines that no longer exist. " +
                "Entries in the lower, system list need administrator rights.",
                "Sign out and back in, or restart, for the change to reach everything."
            },
            HowToVerify =
                "Open a new terminal and run: $env:Path -split ';' | " +
                "Where-Object { $_ -and -not (Test-Path $_) } - it should print nothing."
        };

        public AnomalyResult Evaluate(ProbeContext context)
        {
            var entries = new List<PathEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scope in new[] { EnvironmentVariableTarget.Machine,
                                          EnvironmentVariableTarget.User })
            {
                string raw;
                try { raw = Environment.GetEnvironmentVariable("Path", scope); }
                catch (Exception ex)
                {
                    return AnomalyResult.Inconclusive("The PATH setting could not be read (" + ex.Message + ").");
                }

                if (string.IsNullOrWhiteSpace(raw)) continue;

                foreach (var part in raw.Split(';'))
                {
                    string folder = part.Trim().Trim('"');
                    if (folder.Length == 0) continue;

                    try { folder = Environment.ExpandEnvironmentVariables(folder); }
                    catch { }

                    // The two lists overlap in the process's own PATH, and an entry in both
                    // is one entry as far as the user is concerned.
                    if (!seen.Add(folder.TrimEnd('\\'))) continue;

                    entries.Add(new PathEntry
                    {
                        Folder = folder,
                        Scope = scope == EnvironmentVariableTarget.Machine ? "system" : "your account",
                        Exists = SafeExists(folder)
                    });
                }
            }

            if (entries.Count == 0)
                return AnomalyResult.Inconclusive("The PATH setting is empty, which cannot be right.");

            foreach (var e in entries.Where(e => !e.Exists))
                context.Note($"PATH ({e.Scope}) points at missing {e.Folder}");

            return Decide(entries);
        }

        internal static AnomalyResult Decide(IReadOnlyList<PathEntry> entries)
        {
            var missing = entries.Where(e => !e.Exists).ToList();

            if (missing.Count == 0)
                return AnomalyResult.Pass(
                    $"PATH lists {entries.Count} folders",
                    "all of them exist");

            return AnomalyResult.Finding(
                $"PATH lists {entries.Count} folders",
                missing.Count == 1
                    ? $"1 of them does not exist: {missing[0].Folder}"
                    : $"{missing.Count} of them do not exist: " +
                      string.Join(", ", missing.Select(m => m.Folder)),
                "Windows skips these silently when looking for a command, so nothing has " +
                "gone wrong today. They are leftovers from software that was removed " +
                "without tidying up, and they are worth clearing while you know what they " +
                "were.");
        }

        internal sealed class PathEntry
        {
            public string Folder = "";
            public string Scope = "";
            public bool Exists;
        }

        private static bool SafeExists(string folder)
        {
            try { return Directory.Exists(folder); }
            catch { return true; }   // unreadable is not evidence of absence
        }
    }
}
