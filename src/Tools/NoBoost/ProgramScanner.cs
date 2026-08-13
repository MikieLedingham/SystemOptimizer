// File: Tools/NoBoost/ProgramScanner.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Tools.NoBoost
{
    /// <summary>
    /// Finds the applications on this computer, as things that can actually be matched
    /// against a running process.
    ///
    /// Replaces GameSearchHelper and GameScanner, which between them had the defect that
    /// made no-boost unable to work even once everything else was wired up:
    /// FindGamesOnSystem harvested PROGRAM FILES FOLDER NAMES and START MENU SHORTCUT
    /// NAMES. Those are not process names. "Crystal Disk Info" is a shortcut; the process
    /// is "DiskInfo64K". "Adobe" is a folder; no process is called that. Every entry the
    /// button produced was unmatchable, silently, and the list looked full and useful.
    ///
    /// The rule here is that NOTHING enters the list without a real executable behind it.
    /// Four sources, cheapest and highest-signal first:
    ///
    ///   1. Running processes. Guaranteed matchable - it is running, so its name is by
    ///      definition what we will compare against later.
    ///   2. Start Menu and Desktop shortcuts, RESOLVED to their target executable. This is
    ///      what makes "Crystal Disk Info" arrive as DiskInfo64K with its friendly name
    ///      kept for display. It is also how a person thinks about their installed
    ///      programs.
    ///   3. The usual game install roots, which often have no Start Menu entry.
    ///   4. Nothing else. A recursive sweep of Program Files returns thousands of updaters,
    ///      crash handlers and redistributables; a list nobody can find anything in is not
    ///      more useful than a short one.
    /// </summary>
    public static class ProgramScanner
    {
        /// <summary>Executable name fragments that are never the application itself.</summary>
        private static readonly string[] Junk =
        {
            "uninst", "unins00", "setup", "install", "update", "upgrade", "patch",
            "crashp", "crashhandler", "crashreport", "werfault", "helper", "service",
            "vcredist", "dotnet", "redist", "repair", "diagnostic", "reporter",
            "elevate", "launcher_", "bootstrap"
        };

        private static readonly string WindowsDir =
            Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        /// <summary>
        /// Windows' own components are not applications a person chooses to protect.
        ///
        /// Taking every running process put csrss, conhost, ctfmon, dasHost,
        /// BackgroundTaskHost, COM Surrogate and a wall of vendor service agents into the
        /// list. All of them are running, all of them matched, and not one is something
        /// anyone would tick - but they buried the six or seven entries that were. A list
        /// nobody can find their program in is no more useful than one that cannot match.
        ///
        /// The rule is where it lives, not what it is called: anything under %WINDIR% is a
        /// part of Windows. That is one line instead of a names blocklist that would need
        /// maintaining forever and would still miss whatever shipped last month.
        /// </summary>
        private static bool IsWindowsComponent(string exePath)
        {
            if (string.IsNullOrEmpty(WindowsDir) || string.IsNullOrEmpty(exePath)) return false;
            return exePath.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase);
        }

        public static List<NoBoostEntry> ScanComputer()
        {
            // Keyed on process name so the same application found twice - running AND with
            // a Start Menu shortcut - lands once, keeping whichever description is better.
            var found = new Dictionary<string, NoBoostEntry>(StringComparer.OrdinalIgnoreCase);

            AddRunningProcesses(found);
            AddShortcutTargets(found);
            AddGameFolders(found);

            return found.Values
                        .OrderBy(e => e.DisplayName ?? e.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        /// <summary>
        /// Executables under one folder the user picked, for an application installed
        /// somewhere the general scan does not look.
        /// </summary>
        public static List<NoBoostEntry> ScanFolder(string folder)
        {
            var found = new Dictionary<string, NoBoostEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var exe in Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories))
                    Add(found, exe, FriendlyName(exe));
            }
            catch (Exception ex) { LogHelper.Log("Scan folder failed: " + ex.Message); }

            return found.Values
                        .OrderBy(e => e.DisplayName ?? e.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        private static void Add(Dictionary<string, NoBoostEntry> into,
                                string exePath, string displayName)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return;

            string name;
            try { name = Path.GetFileNameWithoutExtension(exePath); }
            catch { return; }

            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) return;
            if (Junk.Any(j => name.IndexOf(j, StringComparison.OrdinalIgnoreCase) >= 0)) return;
            if (IsWindowsComponent(exePath)) return;

            if (into.TryGetValue(name, out var existing))
            {
                // Fill in anything the earlier sighting did not have.
                if (string.IsNullOrWhiteSpace(existing.ExePath)) existing.ExePath = exePath;
                if (string.IsNullOrWhiteSpace(existing.DisplayName)) existing.DisplayName = displayName;
                return;
            }

            into[name] = new NoBoostEntry
            {
                Name = name,
                DisplayName = string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase)
                              ? null : displayName,
                ExePath = exePath,
                Selected = false
            };
        }

        /// <summary>
        /// Whatever is running now. The most reliable source there is: no resolution, no
        /// guessing, and the name is literally the one the matcher will compare against.
        /// </summary>
        private static void AddRunningProcesses(Dictionary<string, NoBoostEntry> into)
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        string path = null;
                        // MainModule throws for protected and cross-bitness processes; the
                        // name alone is still a perfectly good match key.
                        try { path = p.MainModule?.FileName; } catch { }

                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            Add(into, path, FriendlyName(path));
                        }
                        else if (!string.IsNullOrWhiteSpace(p.ProcessName) &&
                                 p.MainWindowHandle != IntPtr.Zero)
                        {
                            // No readable path, so IsWindowsComponent cannot judge it -
                            // and unreadable MainModule usually means a protected system
                            // process, which is exactly what should not be here. Requiring
                            // a main window keeps the genuine applications this catches
                            // (some cross-bitness ones) and drops csrss and its relatives.
                            Add(into, p.ProcessName + ".exe", null);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex) { LogHelper.Log("Scan: running processes failed: " + ex.Message); }
        }

        /// <summary>
        /// Start Menu and Desktop shortcuts, resolved to the executable they point at.
        ///
        /// Resolving is the whole point. The shortcut's own name is what the user
        /// recognises and is useless for matching; its target is the opposite. Keeping both
        /// is what lets the list read "Crystal Disk Info (DiskInfo64K)".
        /// </summary>
        private static void AddShortcutTargets(Dictionary<string, NoBoostEntry> into)
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            };

            dynamic shell = null;
            try
            {
                var progId = Type.GetTypeFromProgID("WScript.Shell");
                if (progId != null) shell = Activator.CreateInstance(progId);
            }
            catch (Exception ex) { LogHelper.Log("Scan: shortcut resolver unavailable: " + ex.Message); }
            if (shell == null) return;

            try
            {
                foreach (var root in roots.Distinct())
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                    IEnumerable<string> links;
                    try { links = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
                    catch { continue; }

                    foreach (var link in links)
                    {
                        try
                        {
                            dynamic sc = shell.CreateShortcut(link);
                            string target = sc.TargetPath as string;

                            // Steam and Store entries point at steam:// or a protocol
                            // handler rather than an .exe. Nothing to match, so nothing to
                            // add - the game-folder pass below catches those.
                            if (string.IsNullOrWhiteSpace(target) ||
                                !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                !File.Exists(target))
                                continue;

                            Add(into, target, Path.GetFileNameWithoutExtension(link));
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { LogHelper.Log("Scan: shortcuts failed: " + ex.Message); }
            finally
            {
                try
                {
                    if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
                catch { }
            }
        }

        /// <summary>
        /// The usual game install roots. Kept from the old GameScanner because games are
        /// the case people most want covered and they frequently have no Start Menu entry.
        /// </summary>
        private static void AddGameFolders(Dictionary<string, NoBoostEntry> into)
        {
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            var roots = new[]
            {
                Path.Combine(pf86, "Steam", "steamapps", "common"),
                Path.Combine(pf, "Steam", "steamapps", "common"),
                Path.Combine(pf86, "Epic Games"),
                Path.Combine(pf86, "Origin Games"),
                Path.Combine(pf, "EA Games"),
                Path.Combine(pf86, "GOG Galaxy", "Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                             "AppData", "Local", "XboxGames"),
            };

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                foreach (var gameDir in SafeDirectories(root))
                {
                    // One level of executables per game, not a full recursive sweep: game
                    // folders contain engine tools, redistributables and crash handlers
                    // several levels down, none of which anyone wants in this list.
                    string best = SafeFiles(gameDir).FirstOrDefault();
                    if (best != null) Add(into, best, Path.GetFileName(gameDir));
                }
            }
        }

        private static IEnumerable<string> SafeDirectories(string path)
        {
            try { return Directory.EnumerateDirectories(path); }
            catch { return Enumerable.Empty<string>(); }
        }

        private static IEnumerable<string> SafeFiles(string path)
        {
            try
            {
                return Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly)
                                .Where(f => !Junk.Any(j =>
                                    Path.GetFileName(f).IndexOf(j, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            catch { return Enumerable.Empty<string>(); }
        }

        /// <summary>The executable's own product or file description, when it has one.</summary>
        private static string FriendlyName(string exePath)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                string name = info.FileDescription;
                if (string.IsNullOrWhiteSpace(name)) name = info.ProductName;
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
            catch { return null; }
        }
    }
}
