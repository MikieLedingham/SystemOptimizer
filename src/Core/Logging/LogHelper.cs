// File: Helpers/LogHelper.cs
using System;
using System.IO;
using SystemOptimizer.Core.Settings;
namespace SystemOptimizer.Core.Logging
{
    public static class LogHelper
    {
        private static string LogFile => AppPaths.GeneralLogFile;
        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, $"{DateTime.Now:u} {message}\n");
            }
            catch { }
        }
    }
}
