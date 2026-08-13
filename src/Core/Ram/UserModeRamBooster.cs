// File: Helpers/UserModeRamBooster.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace SystemOptimizer.Core.Ram
{
    public static class UserModeRamBooster
    {
        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);
        /// <summary>
        /// Empties the working set of each process and returns
        /// the approximate MB of RAM freed.
        /// </summary>
        public static int ClearAllProcessWorkingSets()
        {
            long before = 0, after = 0;
            foreach (var proc in Process.GetProcesses())
            {
                try { before += proc.WorkingSet64; } catch { }
            }
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    EmptyWorkingSet(proc.Handle);
                }
                catch { }
            }
            foreach (var proc in Process.GetProcesses())
            {
                try { after += proc.WorkingSet64; } catch { }
            }
            // freed in bytes → MB (fixed from GB)
            long freedBytes = before - after;
            return (int)Math.Max(0, Math.Round(freedBytes / 1024.0 / 1024.0));
        }
    }
}
