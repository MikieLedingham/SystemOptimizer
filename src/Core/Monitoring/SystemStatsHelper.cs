// File: SystemStatsHelper.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
namespace SystemOptimizer.Core.Monitoring
{
    public class SystemStats

    {
        // CPU
        public string CpuName { get; set; }
        public float CpuUsage { get; set; }
        public int CpuCoreCount { get; set; }
        // RAM
        public float RamUsedMB { get; set; }
        public float RamTotalMB { get; set; }
        public float RamPercent { get; set; }
        // GPU
        public string GpuName { get; set; }
        // Disk
        public float DiskReadMBps { get; set; }
        public float DiskWriteMBps { get; set; }
        // Pagefile
        public float PagefilePercent { get; set; }
        // Network (all adapters)
        public float NetUpMBps { get; set; }
        public float NetDownMBps { get; set; }
        // Wi-Fi only
        public float WifiUpMBps { get; set; }
        public float WifiDownMBps { get; set; }
        // New network fields
        public IEnumerable<string> EthernetAdapters { get; set; }
        public string WifiSsid { get; set; }
        // Battery
        public float BatteryPercent { get; set; }
        // App count & uptime
        public int AppCount { get; set; }
        public string Uptime { get; set; }
        // NEW SYSTEM INFO
        public string WindowsVersion { get; set; }
        public string SystemArch { get; set; }
        public string CurrentUser { get; set; }
        public string MachineName { get; set; }
        public DateTime BootTime { get; set; }
        public float CDriveFreeGB { get; set; }
        public float CDriveTotalGB { get; set; }
    }
    public static class SystemStatsHelper
    {
        public static float GetRamUsagePercent()
        {
            return PerformanceInfo.GetUsedMemoryPercent();
        }

        public static string GetTopProcess()
        {
            return PerformanceInfo.GetTopMemoryProcess();
        }
        // CPU / RAM / Disk counters
        private static PerformanceCounter cpuCounter = new("Processor", "% Processor Time", "_Total");
        private static PerformanceCounter ramCounter = new("Memory", "Available MBytes");
        private static PerformanceCounter diskReadCounter = new("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
        private static PerformanceCounter diskWriteCounter = new("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
        // Aggregate network counters
        private static PerformanceCounter netSentCounter, netReceivedCounter;
        // Wi-Fi‐only counters
        private static PerformanceCounter wifiSentCounter, wifiReceivedCounter;
        private static readonly object _lock = new();

        /// <summary>
        /// The Windows version as a person would name it, e.g.
        /// "Windows 11 Pro Insider Preview 26H1 (build 28020.2623)".
        ///
        /// The overlay used to show Environment.OSVersion.VersionString, which renders as
        /// "Microsoft Windows NT 10.0.28020.0" - the kernel's platform string. It names
        /// neither the product nor the edition, still says 10.0 on Windows 11 (that major
        /// version was never bumped), and reads like a fault.
        ///
        /// Sources are chosen for being right rather than convenient:
        ///  - WMI Caption knows the real product AND edition ("Microsoft Windows 11 Pro").
        ///  - The registry's ProductName does NOT. On this very machine, running Windows
        ///    11, it reads "Windows 10 Pro" - Microsoft never updated it, and anything
        ///    trusting it reports the wrong OS. It is used only as a last-resort fallback,
        ///    and even then the 11-vs-10 call is made on build number, never on that name.
        ///  - DisplayVersion carries the feature update ("26H1"); ReleaseId is its stale
        ///    predecessor and still says "2009" here.
        ///  - UBR is the patch level after the build, and appears nowhere in OSVersion.
        ///
        /// Computed once: the operating system does not change while the app is running,
        /// and this is read by the overlay every second.
        /// </summary>
        public static readonly Lazy<string> OsName = new(BuildOsName);

        private static string BuildOsName()
        {
            var v = Environment.OSVersion.Version;
            string build = $"build {v.Build}";

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                           @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key?.GetValue("UBR") is int ubr) build = $"build {v.Build}.{ubr}";

                    string product = null;
                    try
                    {
                        using (var s = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
                            foreach (ManagementObject o in s.Get())
                            { product = o["Caption"]?.ToString(); break; }
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(product))
                    {
                        // WMI unavailable. Rebuild the name by hand, taking the edition
                        // from the registry but the 10-vs-11 decision from the build
                        // number, which is the only thing that tells the truth.
                        var edition = key?.GetValue("ProductName") as string ?? "Windows";
                        var suffix = edition.Replace("Windows 10", "").Replace("Windows 11", "").Trim();
                        product = (v.Build >= 22000 ? "Windows 11" : "Windows 10") +
                                  (suffix.Length > 0 ? " " + suffix : "");
                    }

                    product = product.Replace("Microsoft ", "").Trim();

                    var display = key?.GetValue("DisplayVersion") as string;
                    return string.IsNullOrWhiteSpace(display)
                        ? $"{product} ({build})"
                        : $"{product} {display} ({build})";
                }
            }
            catch
            {
                return $"{(v.Build >= 22000 ? "Windows 11" : "Windows 10")} ({build})";
            }
        }
        // TickCount64, not TickCount: the 32-bit counter wraps to negative after ~24.9
        // days of uptime, which put bootTime in the FUTURE and made the overlay's uptime
        // field nonsense on any machine left running for a month. TickCount64 needs
        // .NET Core or later, so this could only be fixed once 2.0 left Framework 4.7.2.
        private static readonly DateTime bootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        static SystemStatsHelper()
        {
            // — pull all perf-counter instance names once
            var category = new PerformanceCounterCategory("Network Interface");
            var allInstances = category.GetInstanceNames();
            // — 1) Aggregate stats: pick first “real” NIC
            var primaryInst = allInstances
              .FirstOrDefault(name =>
                  !name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) &&
                  !name.Contains("isatap", StringComparison.OrdinalIgnoreCase));
            if (primaryInst != null)
            {
                netSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", primaryInst);
                netReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", primaryInst);
            }
            // — 2) Wi-Fi only: locate actual wireless adapter by GUID
            var wifiNic = NetworkInterface
              .GetAllNetworkInterfaces()
              .FirstOrDefault(n =>
                  n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                  n.OperationalStatus == OperationalStatus.Up);
            if (wifiNic != null)
            {
                // try matching the GUID (Id) inside the perf-counter name
                var guidText = wifiNic.Id.ToString();
                string wifiPerfInst = allInstances
                  .FirstOrDefault(inst => inst.IndexOf(guidText, StringComparison.OrdinalIgnoreCase) >= 0);
                // fallback: match by part of the Description
                if (wifiPerfInst == null)
                {
                    var desc = wifiNic.Description;
                    wifiPerfInst = allInstances
                      .FirstOrDefault(inst => inst.IndexOf(desc, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (wifiPerfInst != null)
                {
                    wifiSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", wifiPerfInst);
                    wifiReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", wifiPerfInst);
                }
                // else leave wifiSentCounter/wifiReceivedCounter null
            }
        }
        public static SystemStats GetStats()
        {
            lock (_lock)
            {
                var stats = new SystemStats();
                // ─ CPU ───────────────────────────────────────────
                stats.CpuName = GetCpuName();
                stats.CpuUsage = cpuCounter.NextValue();
                stats.CpuCoreCount = Environment.ProcessorCount;
                // ─ RAM ───────────────────────────────────────────
                float availMB = ramCounter.NextValue();
                float totalMB = GetTotalPhysicalMemoryMB();
                stats.RamUsedMB = totalMB - availMB;
                stats.RamTotalMB = totalMB;
                stats.RamPercent = totalMB > 0 ? stats.RamUsedMB / totalMB * 100f : 0f;
                // ─ GPU ───────────────────────────────────────────
                stats.GpuName = GetGpuName();
                // ─ DISK ──────────────────────────────────────────
                stats.DiskReadMBps = diskReadCounter.NextValue() / (1024f * 1024f);
                stats.DiskWriteMBps = diskWriteCounter.NextValue() / (1024f * 1024f);
                // ─ NETWORK (aggregate) ───────────────────────────
                stats.NetUpMBps = netSentCounter?.NextValue() / (1024f * 1024f) ?? 0f;
                stats.NetDownMBps = netReceivedCounter?.NextValue() / (1024f * 1024f) ?? 0f;
                // ─ NETWORK (Wi-Fi only via IPv4 stats) ─────────────────────────────────────────
                var wifiIf = NetworkInterface
                    .GetAllNetworkInterfaces()
                    .FirstOrDefault(n =>
                        n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                        n.OperationalStatus == OperationalStatus.Up);
                if (wifiIf != null)
                {
                    var stats4 = wifiIf.GetIPv4Statistics();
                    var now = DateTime.UtcNow;
                    var key = wifiIf.Id;            // unique per-machine
                    long sentBytes = stats4.BytesSent;
                    long recvBytes = stats4.BytesReceived;
                    if (_wifiHistory.TryGetValue(key, out var old))
                    {
                        var dtSeconds = (now - old.time).TotalSeconds;
                        if (dtSeconds > 0)
                        {
                            // compute MB/s
                            stats.WifiUpMBps = (sentBytes - old.sent) / (1024f * 1024f) / (float)dtSeconds;
                            stats.WifiDownMBps = (recvBytes - old.received) / (1024f * 1024f) / (float)dtSeconds;
                        }
                    }
                    else
                    {
                        // first sample => we can't compute a rate yet
                        stats.WifiUpMBps = stats.WifiDownMBps = 0f;
                    }
                    // store for next iteration
                    _wifiHistory[key] = (sentBytes, recvBytes, now);
                }
                else
                {
                    stats.WifiUpMBps = 0f;
                    stats.WifiDownMBps = 0f;
                }
                // ─ Ethernet adapters list ────────────────────────
                stats.EthernetAdapters = NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(n =>
                        n.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                     && n.OperationalStatus == OperationalStatus.Up)
                    .Select(n => n.Name)
                    .ToArray();
                // ─ Wi-Fi SSID (try WlanInterop, then netsh fallback) ─────────────────
                string ssid = WlanInterop.GetConnectedSsid();
                if (string.IsNullOrEmpty(ssid) || ssid == "[None]")
                {
                    try
                    {
                        var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                        {
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();
                        // match a line like "    SSID                   : MyNetwork"
                        var m = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
                        if (m.Success)
                            ssid = m.Groups[1].Value.Trim();
                    }
                    catch
                    {
                        ssid = "[None]";
                    }
                }
                stats.WifiSsid = ssid;
                // ─ BATTERY ────────────────────────────────────────
                try
                {
                    stats.BatteryPercent = SystemInformation.PowerStatus.BatteryLifePercent * 100f;
                }
                catch
                {
                    stats.BatteryPercent = -1f;
                }
                // ─ PAGEFILE ───────────────────────────────────────
                stats.PagefilePercent = GetPagefileUsage();
                // ─ APP COUNT ──────────────────────────────────────
                stats.AppCount = Process
                    .GetProcesses()
                    .Count(p => !string.IsNullOrEmpty(p.MainWindowTitle));
                // ─ UPTIME & BOOT ─────────────────────────────────
                stats.Uptime = (DateTime.Now - bootTime).ToString(@"dd\.hh\:mm\:ss");
                stats.BootTime = bootTime;
                // ─ SYSTEM INFO ───────────────────────────────────
                stats.WindowsVersion = OsName.Value;
                stats.SystemArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
                stats.CurrentUser = Environment.UserName;
                stats.MachineName = Environment.MachineName;
                try
                {
                    var c = DriveInfo.GetDrives()
                                     .FirstOrDefault(d => d.Name == @"C:\" && d.IsReady);
                    if (c != null)
                    {
                        stats.CDriveFreeGB = c.TotalFreeSpace / (1024f * 1024f * 1024f);
                        stats.CDriveTotalGB = c.TotalSize / (1024f * 1024f * 1024f);
                    }
                }
                catch
                {
                    stats.CDriveFreeGB = stats.CDriveTotalGB = 0f;
                }
                return stats;
            }
        }
        #region ── Private Helpers ────────────────────────────────────────
        private static readonly Dictionary<string, (long sent, long received, DateTime time)> _wifiHistory
    = new Dictionary<string, (long, long, DateTime)>();
        private static string GetCpuName()
        {
            try
            {
                using var mc = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (ManagementObject o in mc.Get())
                    return o["Name"]?.ToString().Trim() ?? "Unknown";
            }
            catch { }
            return "Unknown";
        }
        private static string GetGpuName()
        {
            try
            {
                using var mc = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject o in mc.Get())
                {
                    var nm = o["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(nm) && !nm.Contains("Basic"))
                        return nm;
                }
            }
            catch { }
            return "Unknown";
        }
        private static float GetTotalPhysicalMemoryMB()
        {
            try
            {
                using var mc = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject o in mc.Get())
                    return Convert.ToUInt64(o["TotalPhysicalMemory"]) / (1024f * 1024f);
            }
            catch { }
            return 0f;
        }
        private static float GetPagefileUsage()
        {
            try
            {
                using var mc = new ManagementObjectSearcher(
                    "SELECT CurrentUsage, PeakUsage FROM Win32_PageFileUsage");
                foreach (ManagementObject o in mc.Get())
                {
                    float used = Convert.ToUInt32(o["CurrentUsage"]);
                    float peak = Convert.ToUInt32(o["PeakUsage"]);
                    return peak > 0 ? used / peak * 100f : 0f;
                }
            }
            catch { }
            return 0f;
        }
        #endregion
    }
}
