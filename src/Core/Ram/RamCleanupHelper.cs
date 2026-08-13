// File: RamCleanupHelper.cs
using Microsoft.VisualBasic.Devices;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SystemOptimizer.Core.Settings;
namespace SystemOptimizer.Core.Ram
{
    public static class RamCleanupHelper
    {
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr procHandle, int minSize, int maxSize);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
        public static string GetTopRamProcess()
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName) && p.WorkingSet64 > 0)
                    .OrderByDescending(p => p.WorkingSet64);
                var top = processes.FirstOrDefault();
                if (top != null)
                {
                    long mb = top.WorkingSet64 / (1024 * 1024);
                    return $"{top.ProcessName} ({mb} MB)";
                }
            }
            catch { }
            return "Unknown";
        }
        /// <summary>
        /// Performs RAM cleanup and returns the amount of MB actually recovered.
        /// Always clamps values to safe, sane limits.
        /// </summary>
        public static double PerformRamCleanup()
        {
            var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
            ulong freeBefore = computerInfo.AvailablePhysicalMemory;
            foreach (Process process in Process.GetProcesses())
            {
                try { if (!process.HasExited) NativeMethods.EmptyWorkingSet(process.Handle); }
                catch { }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            ulong freeAfter = (new Microsoft.VisualBasic.Devices.ComputerInfo()).AvailablePhysicalMemory;
            // THIS LINE IS CRITICAL!
            double freedMB = (freeAfter - freeBefore) / 1024.0 / 1024.0;
            if (freedMB < 0) freedMB = 0;
            return Math.Round(freedMB); // This is the true MB
        }
        // Add this helper (if not present)
        internal static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("psapi.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool EmptyWorkingSet(IntPtr hProcess);
        }
        public static long GetUsedMemoryInMB()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref memStatus);
            ulong totalPhys = memStatus.ullTotalPhys;
            ulong availPhys = memStatus.ullAvailPhys;
            ulong usedPhys = totalPhys - availPhys;
            return (long)(usedPhys / (1024 * 1024));
        }
        public static string FormatMemoryMB(long mb)
        {
            if (mb >= 1024)
            {
                double gb = mb / 1024.0;
                return $"{gb:F2} GB";
            }
            else
            {
                return $"{mb} MB";
            }
        }
        public static double BoostRAM()
        {
            try
            {
                ComputerInfo computerInfo = new ComputerInfo();
                ulong availableBefore = computerInfo.AvailablePhysicalMemory;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
                foreach (Process proc in Process.GetProcesses())
                {
                    try
                    {
                        EmptyWorkingSet(proc.Handle);
                    }
                    catch { }
                }
                Thread.Sleep(500);
                ulong availableAfter = new ComputerInfo().AvailablePhysicalMemory;
                double freedBytes = (double)(availableAfter - availableBefore);
                return freedBytes / (1024 * 1024 * 1024); // GB
            }
            catch
            {
                return 0;
            }
        }
        public static void RecordManualBoost(int mb)
        {
            // Increment total RAM freed (create if missing in your preferences)
            PreferencesManager.Current.TotalRamFreedMB += mb;
            PreferencesManager.SetLastRamBoostMessage($"{mb} MB Recovered");
            PreferencesManager.SavePreferences();
        }
    }
}
