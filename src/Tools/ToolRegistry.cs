// File: Tools/ToolRegistry.cs
using System;
using System.Collections.Generic;
using SystemOptimizer.Tools.NoBoost;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Tools
{
    /// <summary>
    /// Every Tool the product ships with, and the one place Core asks them anything.
    ///
    /// The list below is the ONLY line in the application that names a concrete Tool.
    /// Removing a Tool is deleting its folder and deleting its entry here; adding one is
    /// the same in reverse. That is the whole structural claim of the Tools idea, and it
    /// is worth keeping literally true rather than approximately true.
    /// </summary>
    public static class ToolRegistry
    {
        private static readonly ITool[] Registered =
        {
            new NoBoostTool(),
        };

        public static IReadOnlyList<ITool> All => Registered;

        /// <summary>
        /// The first reason automatic maintenance should hold off, or null if none.
        ///
        /// A Tool that throws is logged and ignored rather than allowed to stop the app
        /// working. An optional feature failing must not take Core down with it - the
        /// worst it should cost is that its objection goes unheard.
        /// </summary>
        private static string _cachedReason;
        private static DateTime _cachedAt = DateTime.MinValue;

        /// <summary>
        /// The same answer, throttled, for callers that ask repeatedly.
        ///
        /// The tray icon repaints once a second and needs to know whether anything is
        /// holding maintenance off. Answering honestly costs a full process enumeration,
        /// which is far too much to do every second just to pick a colour. Ten seconds is
        /// well inside the sixty-second automatic-boost interval, so the icon can never
        /// disagree with what actually happens for long.
        /// </summary>
        public static string AutomaticMaintenanceHeldOffCached()
        {
            if ((DateTime.UtcNow - _cachedAt) < TimeSpan.FromSeconds(10)) return _cachedReason;
            _cachedReason = AutomaticMaintenanceHeldOff();
            _cachedAt = DateTime.UtcNow;
            return _cachedReason;
        }

        /// <summary>Forget the cached answer, so the next ask is fresh.</summary>
        public static void InvalidateCache() => _cachedAt = DateTime.MinValue;

        public static string AutomaticMaintenanceHeldOff()
        {
            foreach (var tool in Registered)
            {
                try
                {
                    string reason = tool.HoldOffReason();
                    if (!string.IsNullOrWhiteSpace(reason)) return reason;
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"Tool '{tool.Name}' failed while being asked to hold off: {ex}");
                }
            }
            return null;
        }
    }
}
