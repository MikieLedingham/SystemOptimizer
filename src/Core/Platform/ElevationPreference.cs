// File: Helpers/ElevationPreference.cs
using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Core.Platform
{
    /// <summary>
    /// "Always run as administrator" - persisted the way Windows itself persists it.
    ///
    /// This writes the same per-user compatibility flag that Explorer sets when you tick
    /// Properties ▸ Compatibility ▸ "Run this program as an administrator". Using the
    /// documented mechanism rather than inventing one has three concrete benefits: the
    /// setting is visible and reversible in the file's own property sheet, it needs no
    /// administrator rights to change (it lives in HKCU), and Windows applies it however
    /// the app is started - shortcut, Start menu, Run box, or Explorer.
    ///
    /// WHAT IT DOES NOT DO: it does not skip the UAC consent prompt. Windows will still
    /// ask on every launch. There is a well-known way around that - registering a
    /// scheduled task with "run with highest privileges" and launching the task instead -
    /// but that deliberately removes the consent step, which is a security decision for
    /// the user to take knowingly and not something to slip in behind a checkbox.
    ///
    /// The flag is keyed by full executable PATH, so it does not follow the app if it is
    /// moved or reinstalled elsewhere. That is Windows' behaviour, not ours.
    /// </summary>
    public static class ElevationPreference
    {
        private const string LayersKey =
            @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        private const string RunAsAdmin = "RUNASADMIN";

        /// <summary>
        /// The real .exe on disk. Environment.ProcessPath, NOT Assembly.Location: the
        /// latter is an empty string in a single-file publish, which is how 2.0 ships,
        /// and an empty path would silently write the flag against nothing.
        /// </summary>
        private static string ExePath => Environment.ProcessPath;

        public static bool IsSupported => !string.IsNullOrEmpty(ExePath);

        public static bool AlwaysRunAsAdmin
        {
            get
            {
                try
                {
                    if (!IsSupported) return false;
                    using (var key = Registry.CurrentUser.OpenSubKey(LayersKey))
                        return (key?.GetValue(ExePath) as string ?? "")
                               .Split(' ')
                               .Any(t => t.Equals(RunAsAdmin, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    LogHelper.Log("Reading elevation preference failed: " + ex);
                    return false;
                }
            }
        }

        /// <summary>Sets or clears the flag. Returns false if it could not be written.</summary>
        public static bool SetAlwaysRunAsAdmin(bool enabled)
        {
            try
            {
                if (!IsSupported) return false;
                using (var key = Registry.CurrentUser.CreateSubKey(LayersKey))
                {
                    if (key == null) return false;

                    // The value is a space-separated list of layer tokens and other
                    // compatibility settings may already be in it (a DPI override, a
                    // compatibility mode). Edit only our token and leave the rest alone -
                    // overwriting the whole value would quietly discard someone else's
                    // settings.
                    var tokens = (key.GetValue(ExePath) as string ?? "")
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => !t.Equals(RunAsAdmin, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (enabled)
                    {
                        // The leading "~" marks the entry as applying to this user; it is
                        // what Explorer writes and Windows expects to lead the list.
                        if (!tokens.Any(t => t == "~")) tokens.Insert(0, "~");
                        tokens.Add(RunAsAdmin);
                    }
                    else
                    {
                        tokens.RemoveAll(t => t == "~");
                    }

                    if (tokens.Count == 0)
                        key.DeleteValue(ExePath, throwOnMissingValue: false);
                    else
                        key.SetValue(ExePath, string.Join(" ", tokens), RegistryValueKind.String);
                }
                LogHelper.Log($"Always-run-as-administrator set to {enabled} for {ExePath}");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Log("Writing elevation preference failed: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Relaunches elevated and shuts this instance down.
        ///
        /// The old UacHelper.TryRestartAsAdmin started the new process and returned,
        /// leaving the unelevated one running - and because the single-instance mutex is
        /// Global with an Everyone ACL, the elevated copy saw it, decided it was a second
        /// instance, and exited. The user got a UAC prompt and no other effect whatsoever.
        /// The new process is passed --elevated-restart so Program.Main waits for this one
        /// to let the mutex go instead of bailing out immediately.
        /// </summary>
        /// <param name="closeFirst">
        /// The dialog this was invoked from, closed before the shutdown is posted.
        /// </param>
        public static bool RestartElevated(System.Windows.Window closeFirst = null)
        {
            try
            {
                if (!IsSupported) return false;
                Process.Start(new ProcessStartInfo(ExePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "--elevated-restart"
                });
            }
            catch (Exception ex)
            {
                // Almost always the user declining the UAC prompt (ERROR_CANCELLED).
                LogHelper.Log("Elevated restart declined or failed: " + ex.Message);
                return false;
            }

            // Unwind the modal stack BEFORE shutting down, and let the shutdown run after
            // the current message rather than during it.
            //
            // Application.Shutdown() closes every window immediately. Calling it straight
            // from a button inside a nested ShowDialog took down the dialog underneath -
            // OptionsDialog, which was merely Hidden while waiting - and when ShowDialog
            // returned, OptionsDialog.OpenAdmin ran Show() on a window that no longer
            // existed. WPF cannot re-show a closed Window, so it threw
            // InvalidOperationException all the way out to Program.Main.
            closeFirst?.Close();
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(() => System.Windows.Application.Current?.Shutdown()));
            return true;
        }
    }
}
