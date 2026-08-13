// File: SanityCheck/ProbeContext.cs
using System;
using System.Collections.Generic;
using System.Management;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// What a check is handed to observe the machine with.
    ///
    /// It exists mainly to make the Inconclusive rule easy to obey. Every helper here
    /// reports FAILURE with a reason rather than returning an empty result, because an
    /// empty result is indistinguishable from a genuine absence - and a check that cannot
    /// tell "there are no network adapters" from "I could not ask about network adapters"
    /// will eventually report Pass on a machine it never managed to look at.
    ///
    /// Queries are cached for the life of one run. Several checks ask WMI the same
    /// questions, WMI is slow, and asking twice can also give two different answers
    /// mid-run - which would let one check pass and another fail on the same fact.
    /// </summary>
    public sealed class ProbeContext
    {
        private readonly Dictionary<string, object> _cache = new(StringComparer.Ordinal);
        private readonly List<string> _log = new();

        /// <summary>Everything the run noticed, for the diagnostics report.</summary>
        public IReadOnlyList<string> Log => _log;

        public void Note(string message)
        {
            _log.Add(message);
            LogHelper.Log("[SanityCheck] " + message);
        }

        /// <summary>
        /// Runs a WMI query, or explains why it could not.
        ///
        /// The out parameter is the text that goes straight into
        /// AnomalyResult.Inconclusive, so it is written to be read by the user, not by
        /// whoever is debugging WMI.
        /// </summary>
        public bool TryQuery(string scope, string query,
                             out List<ManagementBaseObject> rows, out string reason)
        {
            string key = scope + "|" + query;
            if (_cache.TryGetValue(key, out var cached))
            {
                if (cached is List<ManagementBaseObject> hit) { rows = hit; reason = null; return true; }
                rows = null; reason = (string)cached; return false;
            }

            rows = new List<ManagementBaseObject>();
            reason = null;
            try
            {
                using var searcher = new ManagementObjectSearcher(new ManagementScope(scope),
                                                                  new ObjectQuery(query));
                foreach (ManagementBaseObject row in searcher.Get())
                    rows.Add(row);

                _cache[key] = rows;
                return true;
            }
            catch (ManagementException ex)
            {
                // The common ones are worth naming. "Invalid class" means this Windows
                // build does not expose the thing at all, which is a real answer about the
                // machine rather than a defect - and telling the two apart is the point.
                reason = ex.ErrorCode == ManagementStatus.InvalidClass
                    ? $"This version of Windows does not provide {ClassOf(query)}."
                    : $"Windows refused the query for {ClassOf(query)} ({ex.ErrorCode}).";
            }
            catch (UnauthorizedAccessException)
            {
                reason = $"Reading {ClassOf(query)} needs permissions this program does not have.";
            }
            catch (Exception ex)
            {
                reason = $"Reading {ClassOf(query)} failed: {ex.Message}";
            }

            _cache[key] = reason;
            Note($"query failed: {query} -> {reason}");
            rows = null;
            return false;
        }

        /// <summary>The default CIM namespace, where almost everything lives.</summary>
        public bool TryQuery(string query, out List<ManagementBaseObject> rows, out string reason)
            => TryQuery(@"\\.\root\cimv2", query, out rows, out reason);

        /// <summary>
        /// Reads a property, treating "present but null" as absent. WMI returns null for
        /// properties a driver did not fill in, and a null read as 0 is how a check comes
        /// to compare a real number against a made-up one.
        /// </summary>
        public static bool TryValue<T>(ManagementBaseObject row, string property, out T value)
        {
            value = default;
            try
            {
                object raw = row[property];
                if (raw == null) return false;
                value = (T)Convert.ChangeType(raw, typeof(T));
                return true;
            }
            catch { return false; }
        }

        private static string ClassOf(string query)
        {
            int from = query.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
            if (from < 0) return "system information";
            string rest = query.Substring(from + 6).Trim();
            int space = rest.IndexOf(' ');
            return space > 0 ? rest.Substring(0, space) : rest;
        }
    }
}
