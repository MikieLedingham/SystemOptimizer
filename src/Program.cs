using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using SystemOptimizer.Core.Platform;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Shell;

namespace SystemOptimizer
{
    public static class Program
    {
#if DEBUG
        // Toggle invasive debug UI here. Not const: a compile-time false makes the popup
        // body unreachable (CS0162), and the whole point of this switch is that it flips.
        // Debug-only, like the popups themselves - in Release it would be an unused field.
        private static readonly bool SHOW_POPUPS = false; // set true to get the numbered MessageBoxes again
#endif
        private const string LOG_FILE_NAME = "bootstrap.log";

        /// <summary>
        /// Where the bootstrap log goes, worked out on FIRST USE rather than at type load.
        ///
        /// It used to be AppDomain.CurrentDomain.BaseDirectory - next to the executable.
        /// That works from bin\ and breaks the moment the program is installed, because
        /// Program Files is not user-writable: the log would silently stop being written
        /// at exactly the point a user is most likely to need it, since this is the file
        /// that records failures happening BEFORE the application is up.
        ///
        /// Lazy, not a static field, for a reason that is easy to miss: a static field
        /// referencing AppPaths would run its static constructor when this type loads,
        /// and this type loads during the BUILD, when the guide-validation step runs the
        /// freshly compiled exe. Creating the user's application-data folder as a side
        /// effect of compiling is not acceptable. Deferring it means the build never
        /// touches it, because the guide step never logs.
        /// </summary>
        private static readonly Lazy<string> LogPath = new Lazy<string>(ResolveLogPath);

        private static string ResolveLogPath()
        {
            // %APPDATA%\System Optimizer\logs, with everything else the program writes.
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                return Path.Combine(AppPaths.LogsDir, LOG_FILE_NAME);
            }
            catch { }

            // If even that failed, the problem may BE the application-data folder, which
            // is precisely the kind of thing this log exists to capture. Fall back to the
            // temp folder - never beside the executable, which is the bug being fixed.
            try
            {
                string temp = Path.Combine(Path.GetTempPath(), "System Optimizer");
                Directory.CreateDirectory(temp);
                return Path.Combine(temp, LOG_FILE_NAME);
            }
            catch { return null; }   // nowhere writable: log nothing rather than crash
        }

        [STAThread]
        public static void Main(string[] args)
        {
            // FIRST, before the log file, the mutex, WPF or anything else.
            //
            // The build runs this to generate the Sanity Check guide, so it has to be able
            // to do that and exit without becoming an application: no window, no tray icon,
            // and crucially no single-instance mutex - a build kicked off while System
            // Optimizer is open would otherwise decide it was a duplicate, raise the
            // running copy to the foreground and produce no guide.
            //
            // It must not write bootstrap.log either: the build has no business creating
            // the user's application-data folder. See LogPath, which is deferred so that
            // merely loading this type during the build does not do it.
            if (args.Length >= 1 && args[0] == "--emit-guide")
            {
                Environment.Exit(EmitGuide(args.Length >= 2 ? args[1] : null));
                return;
            }

            SafeLog("=== Program.Main START ===");
            try
            {
                // ---- Environment / elevation detection (NO auto-elevation) ----
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                bool isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
                SafeLog($"Elevation: isElevated={isElevated}; args=[{string.Join(" ", args)}]");

                DebugPopup("1: Entered Main()");

                // ---- Single-instance global mutex (covers admin + non-admin) ----
                var msec = new MutexSecurity();
                msec.AddAccessRule(new MutexAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    MutexRights.FullControl,
                    AccessControlType.Allow));

                bool createdNew;
                // .NET dropped the Mutex(bool, string, out bool, MutexSecurity)
                // constructor this once used; MutexAcl.Create is its replacement and
                // takes the same arguments. The ACL matters: the mutex has to be
                // visible across the elevation boundary, because UacHelper can relaunch
                // this process as admin and a second instance must still be detected.
                using (var mutex = MutexAcl.Create(
                           initiallyOwned: true,
                           name: @"Global\SystemOptimizer_SINGLE_INSTANCE_MUTEX",
                           createdNew: out createdNew,
                           mutexSecurity: msec))
                {
                    DebugPopup($"2: Mutex createdNew = {createdNew}");
                    SafeLog($"Mutex createdNew={createdNew}");

                    // A restart-as-administrator is a handover, not a second instance. The
                    // outgoing process starts this one and only then shuts down, so for a
                    // moment both exist and the mutex is still held. Bailing out here - as
                    // the old code did - meant the user approved a UAC prompt and got
                    // nothing at all. Wait for the handover to finish instead.
                    if (!createdNew && args.Contains("--elevated-restart"))
                    {
                        SafeLog("Elevated restart: waiting for the outgoing instance to release the mutex.");
                        for (int i = 0; i < 50 && !createdNew; i++)
                        {
                            Thread.Sleep(100);
                            try
                            {
                                createdNew = mutex.WaitOne(TimeSpan.Zero);
                            }
                            catch (AbandonedMutexException)
                            {
                                // The outgoing process died without releasing the mutex -
                                // killed, or crashed. The wait still SUCCEEDED and this
                                // process now owns it; the exception only reports that the
                                // previous owner did not tidy up. Letting it escape meant
                                // the incoming instance died in the fatal-error handler
                                // instead of starting, which is the opposite of a handover.
                                SafeLog("Previous instance abandoned the mutex; taking ownership.");
                                createdNew = true;
                            }
                        }
                        SafeLog($"Elevated restart: mutex acquired={createdNew}");
                    }

                    if (!createdNew)
                    {
                        SafeLog("Another instance detected – attempting to bring to front and exiting.");
                        TryBringExistingToFront();
                        return;
                    }

                    // ---- Create App + MainWindow explicitly (StartupUri removed) ----
                    DebugPopup("3: Creating App()");
                    var app = new App();

                    DebugPopup("4: Initializing Application resources");
                    try
                    {
                        app.InitializeComponent();
                    }
                    catch (Exception initEx)
                    {
                        SafeLog("InitializeComponent exception: " + initEx);
                        throw;
                    }

                    // Apply the saved palette BEFORE any window is constructed. This used
                    // to run in App.OnStartup, which Program.Main only reaches at app.Run()
                    // - after MainWindow has already been created and shown. The window was
                    // therefore built against the default Dark palette and its title bar
                    // themed dark, so choosing Light restyled the controls but left the
                    // window background and caption wrong.
                    try
                    {
                        ThemeManager.LoadLastThemeOrDefault();
                        ThemeManager.HookNewWindows();
                    }
                    catch (Exception themeEx) { SafeLog("Theme load exception: " + themeEx); }

                    DebugPopup("5: Creating MainWindow()");
                    MainWindow mainWindow;
                    try
                    {
                        mainWindow = new MainWindow();
                    }
                    catch (Exception mwEx)
                    {
                        SafeLog("MainWindow ctor exception: " + mwEx);
                        throw;
                    }

                    app.MainWindow = mainWindow;

                    DebugPopup("6: Showing MainWindow()");
                    bool startMinimised = args.Contains(StartupManager.MinimisedArgument);
                    try
                    {
                        // Even when starting to the tray the window is shown once, then
                        // hidden. Never showing it at all leaves WPF with a window that has
                        // no handle - the tray icon's Restore would have to create one - and
                        // showing it Minimised with ShowInTaskbar off means nothing appears
                        // on screen or the taskbar while that happens, so there is no flash.
                        if (startMinimised)
                        {
                            SafeLog("Starting minimised to the notification area.");
                            mainWindow.WindowState = System.Windows.WindowState.Minimized;
                            mainWindow.ShowInTaskbar = false;
                            mainWindow.Show();
                            mainWindow.Hide();
                            mainWindow.WindowState = System.Windows.WindowState.Normal;
                            mainWindow.ShowInTaskbar = true;
                        }
                        else
                        {
                            mainWindow.Show();
                        }
                    }
                    catch (Exception showEx)
                    {
                        SafeLog("MainWindow.Show exception: " + showEx);
                        throw;
                    }

                    // The --post-license argument was handled here: it toasted "License
                    // activated. All features unlocked." Licensing was removed entirely in
                    // 2.0, so nothing could ever pass that flag - and if anything had, the
                    // message would have announced the unlocking of features that are not
                    // locked. Removed with the rest of the licensing residue.

                    DebugPopup("7: Entering app.Run()");
                    SafeLog("Entering app.Run()");
                    try
                    {
                        app.Run();       // uses app.MainWindow
                    }
                    catch (Exception runEx)
                    {
                        SafeLog("app.Run exception: " + runEx);
                        throw;
                    }

                    SafeLog("app.Run() exited normally.");
                }
            }
            catch (Exception ex)
            {
                SafeLog("FATAL in Program.Main: " + ex);
                TryShowError("Fatal bootstrap error:\n" + ex);
            }
            finally
            {
                SafeLog("=== Program.Main END ===");
            }
        }

        // --------------------------------------------------------------------
        // Helper: Bring existing instance to foreground (graceful if missing)
        // --------------------------------------------------------------------
        private static void TryBringExistingToFront()
        {
            try
            {
                var appClass = Type.GetType("SystemOptimizer.App");
                var method = appClass?.GetMethod(
                    "BringExistingInstanceToFront",
                    BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    method.Invoke(null, null);
                    SafeLog("Called App.BringExistingInstanceToFront()");
                    return;
                }
                SafeLog("BringExistingInstanceToFront() not found. (No-op).");
            }
            catch (Exception ex)
            {
                SafeLog("TryBringExistingToFront() exception: " + ex);
            }
        }

        /// <summary>
        /// Writes the Sanity Check guide. Returns a process exit code, because the build
        /// step that calls this treats a non-zero code as a failed build.
        ///
        /// That is the whole point of doing it here rather than at runtime: a check whose
        /// documentation cannot name a single user for whom its finding is a deliberate
        /// choice does not get to ship, and the build says so by name.
        /// </summary>
        private static int EmitGuide(string outputDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Console.Error.WriteLine("--emit-guide needs an output directory.");
                    return 2;
                }

                var problems = SanityCheck.GuideWriter.Write(
                    outputDirectory, SanityCheck.CheckRegistry.All);

                if (problems.Count == 0)
                {
                    Console.WriteLine($"Sanity Check guide written to {outputDirectory} " +
                                      $"({SanityCheck.CheckRegistry.All.Count} checks).");
                    return 0;
                }

                // Reported in MSBuild's error format so they appear as build errors with
                // the rest, rather than as text somebody has to go looking for.
                foreach (var problem in problems)
                    Console.Error.WriteLine("error SANITY001: " + problem);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error SANITY002: the guide could not be written: " + ex.Message);
                return 3;
            }
        }

        // --------------------------------------------------------------------
        // Logging / Debug UI helpers
        // --------------------------------------------------------------------
        private static void DebugPopup(string msg)
        {
#if DEBUG
            if (SHOW_POPUPS)
            {
                try { MessageBox.Show(msg, "Debug", MessageBoxButton.OK, MessageBoxImage.Information); }
                catch { /* ignore */ }
            }
            SafeLog(msg);
#endif
        }

        private static void TryShowError(string msg)
        {
            try { MessageBox.Show(msg, "SystemOptimizer", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch { /* ignore */ }
        }

        private static void SafeLog(string text)
        {
            try
            {
                string path = LogPath.Value;
                if (path == null) return;   // nowhere writable

                int pid = Environment.ProcessId;
                File.AppendAllText(path,
                    $"{DateTime.Now:O} [{pid}] {text}{Environment.NewLine}");
            }
            catch
            {
                // ignore logging failures
            }
        }
    }
}
