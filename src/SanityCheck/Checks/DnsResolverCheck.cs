// File: SanityCheck/Checks/DnsResolverCheck.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SystemOptimizer.SanityCheck.Checks
{
    /// <summary>
    /// Name lookups should go to the DNS server you set, not to one you did not.
    ///
    /// THE TWO FACTS: the DNS servers offered by one active connection, and the DNS
    /// servers offered by another. Both read from the network stack, both true, and when
    /// they disagree the machine resolves names through whichever connection Windows
    /// happens to rank first - a decision no dialog ever shows you.
    ///
    /// This is the one that comes straight from the incident the whole feature was built
    /// from: a PC that had quietly stopped using its own configured DNS server. Everything
    /// worked. Every indicator was green. Requests were simply going somewhere else.
    ///
    /// WHY IT MAKES NO NETWORK CALL. The obvious stronger test is to ask a name and see
    /// who answers - but 2.0 makes no outbound calls at all, which is most of its privacy
    /// policy, and a product whose job is noticing suspicious configuration must not be
    /// the thing that phones out about what it found. Sending a query would also announce
    /// this machine to whichever resolver has been substituted, which is precisely the
    /// party you would least want to tell. So it compares what the machine already knows.
    ///
    /// WHY Probable RATHER THAN Certain, which decides whether it may interrupt: the
    /// disagreement itself is certain and directly observed. What is NOT observed is which
    /// resolver actually wins - that depends on interface metrics, and on this machine
    /// every default route reports a metric of zero, so the ordering is not readable.
    /// Claiming a winner would be inference dressed as observation. Probable also keeps it
    /// from interrupting, which matters because a VPN going up and down is ordinary.
    /// </summary>
    public sealed class DnsResolverCheck : IAnomalyCheck
    {
        public string Id => "NET.DNS_MISMATCH";
        public string Title => "Where name lookups go";

        public Confidence Confidence => Confidence.Probable;
        public DateTime? ReviewBy => null;

        public bool DefaultEnabled => true;  // every PC resolves names; Probable, so it can never interrupt

        public CheckDoc Doc => new CheckDoc
        {
            Summary = "Checks whether more than one network connection is handing out a " +
                      "different DNS server, so name lookups may not go where you set them.",
            WhyItMatters =
                "DNS turns names into addresses, and whichever server does it sees every " +
                "site you visit and decides what each name resolves to. When two active " +
                "connections offer different servers, Windows picks one by its own ranking " +
                "and never tells you which. Browsing works either way - so an ad blocker or " +
                "filtered DNS you set up yourself can sit bypassed for months, or a VPN can " +
                "keep resolving your traffic long after you thought it was finished.",
            WhenToIgnore = new[]
            {
                "You are connected to a VPN on purpose and want it to handle name lookups - " +
                "that is usually the entire point of the VPN, and this will be reported " +
                "the whole time it is connected.",
                "You are on a work laptop whose corporate VPN or agent is supposed to " +
                "resolve internal names.",
                "You have two connections up deliberately - wired and wireless - and do " +
                "not care which one resolves names.",
                "The extra connection belongs to virtual machine or container software " +
                "that only resolves names for its own guests."
            },
            HowToConfirm = new[]
            {
                "Run: Get-DnsClientServerAddress -AddressFamily IPv4",
                "Each connection is listed with the servers it offers. Compare them.",
                "To see which connections could carry traffic: Get-NetRoute " +
                "-DestinationPrefix '0.0.0.0/0'"
            },
            Remedy = new[]
            {
                "Decide which server you want doing the lookups.",
                "If it is a VPN you are no longer using, disconnect it properly rather than " +
                "just closing its window - a tunnel left up keeps its DNS server in place.",
                "If a connection should not be resolving names at all, disable it in " +
                "Settings, Network and internet, Advanced network settings.",
                "If you want a specific server, set it on the connection you actually use, " +
                "in that connection's properties under Edit DNS server assignment."
            },
            HowToVerify =
                "Run Get-DnsClientServerAddress -AddressFamily IPv4 again. Only the " +
                "connection you chose should be offering a DNS server for general use."
        };

        public AnomalyResult Evaluate(ProbeContext context)
        {
            // Which connections could carry ordinary traffic, taken from the routing table
            // rather than guessed.
            //
            // The first version of this asked whether the connection had a gateway address,
            // which is wrong in the exact case this check exists for: a WireGuard or
            // OpenVPN tunnel is point-to-point and has NO gateway - its default route's
            // next hop is 0.0.0.0. So the guard meant to exclude virtual switches excluded
            // the VPN instead, and the check reported a confident Pass on a machine that
            // was demonstrably resolving through one. Caught only by running it against a
            // machine whose answer was already known.
            if (!context.TryQuery(
                    "SELECT InterfaceIndex FROM Win32_IP4RouteTable WHERE Destination='0.0.0.0'",
                    out var routes, out string routeFailure))
                return AnomalyResult.Inconclusive(
                    "Which connections carry internet traffic could not be established. " + routeFailure);

            var carriers = new HashSet<int>();
            foreach (var route in routes)
                if (ProbeContext.TryValue<int>(route, "InterfaceIndex", out int index))
                    carriers.Add(index);

            if (carriers.Count == 0)
                return AnomalyResult.NotApplicable(
                    "No connection currently has a route to the internet, so nothing is " +
                    "competing to resolve names.");

            List<Resolver> resolvers;
            try
            {
                resolvers = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => ToResolver(n, carriers))
                    .Where(r => r != null)
                    .ToList();
            }
            catch (NetworkInformationException ex)
            {
                return AnomalyResult.Inconclusive(
                    "The network connections could not be read (" + ex.Message + ").");
            }

            if (resolvers.Count == 0)
                return AnomalyResult.NotApplicable(
                    "No active connection is offering a DNS server, so there is nothing to compare.");

            foreach (var r in resolvers)
                context.Note($"{r.Name}: DNS {string.Join(", ", r.Servers)}");

            return Decide(resolvers);
        }

        private static Resolver ToResolver(NetworkInterface nic, HashSet<int> carriers)
        {
            var properties = nic.GetIPProperties();

            int index;
            try { index = properties.GetIPv4Properties()?.Index ?? -1; }
            catch (NetworkInformationException) { return null; }   // no IPv4 on this one

            // Only connections that actually carry internet traffic compete to resolve
            // names. This is what keeps a developer's Hyper-V and WSL switches quiet
            // without also silencing VPN tunnels.
            if (!carriers.Contains(index)) return null;

            var servers = properties.DnsAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (servers.Count == 0) return null;

            return new Resolver { Name = nic.Name, Servers = servers };
        }

        internal static AnomalyResult Decide(IReadOnlyList<Resolver> resolvers)
        {
            // Group connections by the SET of servers they offer, sorted here rather than
            // relying on the caller to have sorted first. Two connections listing the same
            // two servers in a different order are not a disagreement - the primary and
            // secondary swapping places is not a machine resolving somewhere unexpected -
            // and a decision that silently depends on its input already being tidy is one
            // that behaves differently for the harness than for the app.
            var groups = resolvers
                .GroupBy(r => string.Join(",", r.Servers.OrderBy(s => s, StringComparer.Ordinal)),
                         StringComparer.Ordinal)
                .ToList();

            if (groups.Count == 1)
            {
                var only = groups[0];
                string names = Join(only.Select(r => r.Name));
                int count = only.Count();
                return AnomalyResult.Pass(
                    $"name lookups are set to {Join(only.First().Servers)}",
                    count == 1 ? $"only {names} is offering a DNS server"
                  : count == 2 ? $"{names} both offer the same server"
                               : $"{names} all offer the same server");
            }

            // Most connections agreeing and one disagreeing is the interesting shape, so
            // report the odd one out rather than an unordered list.
            var ordered = groups.OrderByDescending(g => g.Count()).ToList();
            var majority = ordered[0];
            var oddOnes = ordered.Skip(1).SelectMany(g => g).ToList();

            string oddNames = Join(oddOnes.Select(r => r.Name));
            string oddServers = Join(oddOnes.SelectMany(r => r.Servers).Distinct(StringComparer.Ordinal));

            return AnomalyResult.Finding(
                $"{Join(majority.Select(r => r.Name))} " +
                $"{(majority.Count() == 1 ? "uses" : "use")} {Join(majority.First().Servers)}",
                $"{oddNames} {(oddOnes.Count == 1 ? "offers" : "offer")} {oddServers} instead",
                "More than one active connection is handing out a different DNS server. " +
                "Windows ranks them itself and does not show which one wins, so name " +
                "lookups may not be going where you set them. Everything still works - " +
                "which is exactly why this can go unnoticed for months.");
        }

        internal sealed class Resolver
        {
            public string Name = "";
            public List<string> Servers = new();
        }

        private static string Join(IEnumerable<string> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return "nothing";
            if (list.Count == 1) return list[0];
            return string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
        }
    }
}
