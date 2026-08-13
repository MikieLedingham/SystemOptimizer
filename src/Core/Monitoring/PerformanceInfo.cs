using System;
using System.Diagnostics;
using System.Linq;

namespace SystemOptimizer.Core.Monitoring
{
    public static class PerformanceInfo
    {
        public static float GetUsedMemoryPercent()
        {
            var totalMemory = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
            var availableMemory = new Microsoft.VisualBasic.Devices.ComputerInfo().AvailablePhysicalMemory;
            return (float)((totalMemory - availableMemory) * 100.0 / totalMemory);
        }

        public static string GetTopMemoryProcess()
        {
            try
            {
                var processes = Process.GetProcesses();
                var topProcess = processes
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .OrderByDescending(p =>
                    {
                        try
                        {
                            return p.WorkingSet64;
                        }
                        catch
                        {
                            return 0;
                        }
                    })
                    .FirstOrDefault();

                if (topProcess != null)
                    return $"{topProcess.ProcessName} ({topProcess.WorkingSet64 / (1024 * 1024)} MB)";
                else
                    return "N/A";
            }
            catch
            {
                return "N/A";
            }
        }
    }
}
