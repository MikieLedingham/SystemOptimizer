// File: SanityCheck/Checks/StartupEntryCheck.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace SystemOptimizer.SanityCheck.Checks
{
    /// <summary>
    /// Everything Windows is told to start at logon should still be there.
    ///
    /// THE TWO FACTS: what the Run keys say to start, and whether that file exists. Two
    /// different subsystems, and nothing keeps them in step - uninstallers routinely
    /// remove the program and leave the instruction behind.
    ///
    /// Harmless in itself, which is the point: Windows tries, fails, and says nothing, so
    /// the entry sits there for years. It matters because it is the visible half of a
    /// question worth asking - if this instruction has been quietly failing since an
    /// uninstall, what else did that uninstall leave behind?
    /// </summary>
    public sealed class StartupEntryCheck : IAnomalyCheck
    {
        public string Id => "STARTUP.MISSING_FILE";
        public string Title => "Startup entries that point at nothing";

        public Confidence Confidence => Confidence.Certain;
        public DateTime? ReviewBy => null;

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public bool DefaultEnabled => true;  // every PC, and Startup apps is somewhere people already look

        public CheckDoc Doc => new CheckDoc
        {
            Summary = "Checks that every program Windows starts at sign-in is still installed.",
            WhyItMatters =
                "When a program is uninstalled its instruction to start at sign-in is often " +
                "left behind. Windows tries to run it at every sign-in, fails, and reports " +
                "nothing at all. It is not doing any harm, but it is a leftover from " +
                "something that did not clean up after itself, and it will sit there " +
                "forever unless somebody looks.",
            WhenToIgnore = new[]
            {
                "The program lives on a drive that is not always connected - an external " +
                "or network drive - so it is missing now and present when you plug it in.",
                "You are between installs, or mid-way through moving a program.",
                "The entry belongs to something portable that you run from a USB stick.",
                "Security software has quarantined the file and you want the entry left " +
                "alone until you have dealt with that."
            },
            HowToConfirm = new[]
            {
                "Open Task Manager, Startup apps. A leftover usually shows with no " +
                "publisher and no icon.",
                "Or run: reg query \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\"",
                "Check the path in the entry to see whether that file is really there."
            },
            Remedy = new[]
            {
                "Confirm the program really is gone, and is not just on a disconnected drive.",
                "Open Task Manager, Startup apps, find the entry and choose Disable. That " +
                "is enough, and it is reversible.",
                "If you would rather remove it outright, delete the matching value under " +
                "the Run key shown above. Entries under HKEY_LOCAL_MACHINE need " +
                "administrator rights.",
                "System Optimizer does not change these for you: startup entries are the " +
                "sort of thing worth looking at before removing."
            },
            HowToVerify =
                "Sign out and back in, then look at Task Manager, Startup apps. The entry " +
                "should be gone or disabled."
        };

        public AnomalyResult Evaluate(ProbeContext context)
        {
            var entries = new List<Entry>();
            string failure = null;

            foreach (var (root, label) in new[]
                     {
                         (Registry.CurrentUser, "this user"),
                         (Registry.LocalMachine, "all users")
                     })
            {
                try
                {
                    using var key = root.OpenSubKey(RunKey);
                    if (key == null) continue;

                    foreach (var name in key.GetValueNames())
                    {
                        string command = key.GetValue(name) as string;
                        if (string.IsNullOrWhiteSpace(command)) continue;

                        string path = ExecutablePath(command);
                        if (path == null) continue;   // nothing that looks like a path

                        entries.Add(new Entry
                        {
                            Name = name,
                            Scope = label,
                            Path = path,
                            Exists = SafeExists(path)
                        });
                    }
                }
                catch (Exception ex) { failure = ex.Message; }
            }

            if (entries.Count == 0)
                return failure != null
                    ? AnomalyResult.Inconclusive("The startup entries could not be read (" + failure + ").")
                    : AnomalyResult.NotApplicable("Nothing is set to start at sign-in.");

            foreach (var e in entries.Where(e => !e.Exists))
                context.Note($"startup entry {e.Name} points at missing {e.Path}");

            return Decide(entries);
        }

        internal static AnomalyResult Decide(IReadOnlyList<Entry> entries)
        {
            var missing = entries.Where(e => !e.Exists).ToList();

            if (missing.Count == 0)
                return AnomalyResult.Pass(
                    $"{entries.Count} {(entries.Count == 1 ? "program is" : "programs are")} set to start at sign-in",
                    "all of them are still installed");

            string what = missing.Count == 1
                ? $"\"{missing[0].Name}\" is missing"
                : $"{missing.Count} of them are missing: " +
                  string.Join(", ", missing.Select(m => $"\"{m.Name}\""));

            return AnomalyResult.Finding(
                $"{entries.Count} {(entries.Count == 1 ? "program is" : "programs are")} set to start at sign-in",
                what,
                missing.Count == 1
                    ? $"Windows is told to start {missing[0].Path} at every sign-in, and that " +
                      "file is not there. It has been failing silently ever since it was " +
                      "removed - usually an uninstall that did not tidy up after itself."
                    : "Windows is told to start these at every sign-in and the files are not " +
                      "there, so it has been failing silently ever since they were removed - " +
                      "usually uninstalls that did not tidy up after themselves.");
        }

        internal sealed class Entry
        {
            public string Name = "";
            public string Scope = "";
            public string Path = "";
            public bool Exists;
        }

        /// <summary>
        /// Pulls the program out of a Run command line.
        ///
        /// These are command lines, not paths, and the shapes vary: a quoted path with
        /// arguments after it, a bare path with no spaces, or an unquoted path WITH spaces
        /// - which is genuinely ambiguous and is why the unquoted case cuts at ".exe"
        /// rather than at the first space. Getting this wrong in the careless direction
        /// would report a missing file for a program that is sitting right there.
        /// </summary>
        internal static string ExecutablePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            command = Environment.ExpandEnvironmentVariables(command.Trim());

            string path;
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                if (end <= 1) return null;
                path = command.Substring(1, end - 1);
            }
            else
            {
                int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe >= 0) path = command.Substring(0, exe + 4);
                // No quotes and no .exe: something we have not understood. Reporting on a
                // path we are not sure we parsed would be worse than saying nothing.
                else if (!command.Contains(' ')) path = command;
                else return null;
            }

            // A bare name with no folder - "rundll32.exe shell32.dll,..." is a common
            // startup entry - is NOT missing. Windows finds it through PATH, while
            // File.Exists would resolve it against the working directory and say no. That
            // would be a confident finding about a program sitting in system32, which is
            // the worst kind of wrong this check could be.
            return System.IO.Path.IsPathRooted(path) ? path : null;
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); }
            catch { return true; }   // unreadable is not evidence of absence
        }
    }
}
