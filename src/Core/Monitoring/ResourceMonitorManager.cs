// File: Helpers/ResourceMonitorManager.cs
using System;
using System.Windows.Threading;
using Microsoft.VisualBasic.Devices;
using System.Diagnostics;
namespace SystemOptimizer.Core.Monitoring
{
    public class ResourceMonitorManager : IDisposable
    {
        public class ResourceSnapshot
        {
            public int CpuUsage { get; set; }
            public int RamPercentUsed { get; set; }
            public double RamFreeGB { get; set; }
            public double RamUsedGB { get; set; }
            public double RamTotalGB { get; set; }
            // Add more properties later (GPU, temp, battery, etc.)
        }
        private readonly DispatcherTimer _timer;
        private readonly PerformanceCounter _cpuCounter;
        private readonly ComputerInfo _compInfo;
        private readonly double _totalRamGB;
        public event Action<ResourceSnapshot> ResourceUpdated;
        public ResourceMonitorManager(double intervalSeconds = 1.0)
        {
            _compInfo = new ComputerInfo();
            _totalRamGB = _compInfo.TotalPhysicalMemory / 1024.0 / 1024.0 / 1024.0;
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(intervalSeconds)
            };
            _timer.Tick += (s, e) => RaiseUpdate();
        }
        public ResourceSnapshot LatestSnapshot { get; set; }
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        // Deleted here: Initialize(), an empty method described in its own comment as a
        // "No-op placeholder to satisfy early overlay activation" - it satisfied nothing,
        // because nothing called it; and CurrentSnapshot, which returned a private field
        // that was NEVER ASSIGNED, so it always returned null. The compiler said so
        // (CS0649) on every build. Callers use LatestSnapshot, which is a real property.
        private void RaiseUpdate()
        {
            try
            {
                int cpu = (int)Math.Round(_cpuCounter.NextValue());
                ulong avail = _compInfo.AvailablePhysicalMemory;
                double ramFreeGB = avail / 1024.0 / 1024.0 / 1024.0;
                double ramUsedGB = _totalRamGB - ramFreeGB;
                int ramPercent = (int)Math.Round((ramUsedGB / _totalRamGB) * 100.0);
                ResourceUpdated?.Invoke(new ResourceSnapshot
                {
                    CpuUsage = cpu,
                    RamPercentUsed = ramPercent,
                    RamFreeGB = ramFreeGB,
                    RamUsedGB = ramUsedGB,
                    RamTotalGB = _totalRamGB
                });
            }
            catch
            {
                // Optional: handle/log errors
            }
        }
        public void Dispose()
        {
            _timer.Stop();
            _cpuCounter?.Dispose();
        }
    }
}
