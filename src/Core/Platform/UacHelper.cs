// File: Helpers/UacHelper.cs
using System;
using System.Diagnostics;
using System.Security.Principal;
namespace SystemOptimizer.Core.Platform
{
    public static class UacHelper
    {
        /// <summary>
        /// Returns true if the current process is running elevated (in an Administrator context).
        /// </summary>
        public static bool IsRunningAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        /// <summary>
        /// Attempts to restart the current executable with Administrator privileges.
        /// Returns true if the UAC prompt was shown (and the process launched), false if the user cancelled or an error occurred.
        /// </summary>
        public static bool TryRestartAsAdmin()
        {
            // Path to the current executable
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                             ?? throw new InvalidOperationException("Could not determine executable path.");
            try
            {
                var psi = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                // user declined UAC or something else went wrong
                return false;
            }
        }
    }
}
