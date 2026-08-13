// File: SanityCheck/GuideWriter.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace SystemOptimizer.SanityCheck
{
    /// <summary>
    /// Renders the check registry to the user guide.
    ///
    /// GENERATED, NEVER WRITTEN BESIDE THE CODE. Documentation maintained separately from
    /// what it documents drifts, always - and drifting documentation is the same failure
    /// class this whole feature exists to notice, so shipping it here would be
    /// embarrassing as well as wrong. Every check carries its own CheckDoc, and this turns
    /// them into a page.
    ///
    /// It runs at BUILD time (see the EmitSanityGuide target in the csproj) and returns a
    /// non-zero exit code if any check's documentation is incomplete, which fails the
    /// build. The rule that earns its keep: WhenToIgnore must be non-empty. Every check
    /// has users for whom the finding is a deliberate choice - the 2.5 Gb adapter on a
    /// 1 Gb home network on this very machine is one - and an author who cannot name those
    /// users has not finished thinking about the check.
    ///
    /// LOCAL, NOT ONLINE, and privacy is the first reason rather than convenience:
    /// fetching an online help page for NET.DNS_MISMATCH would tell a third party that
    /// this machine's name lookups look hijacked. A product that exists to notice
    /// suspicious configuration must not broadcast what it found. It also cannot go stale
    /// against the installed build, and it still works when the finding IS the broken
    /// network.
    /// </summary>
    public static class GuideWriter
    {
        /// <summary>
        /// Writes guide/index.html into <paramref name="outputDirectory"/>.
        /// Returns every documentation problem found; an empty list means it wrote.
        /// </summary>
        /// <param name="fontFamily">
        /// The application font, so the guide reads in the same face as the program that
        /// produced it. Passed in rather than looked up, because this lives on the Core
        /// side and the font resource is presentation - and because the build-time
        /// validation run has no Application to ask.
        /// </param>
        public static IReadOnlyList<string> Write(string outputDirectory,
                                                  IReadOnlyList<IAnomalyCheck> checks,
                                                  string fontFamily = "Segoe UI")
        {
            // Validate ALL of them before writing anything, so one rebuild reports every
            // problem rather than one per attempt.
            var problems = new List<string>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var check in checks)
            {
                if (string.IsNullOrWhiteSpace(check.Id))
                    problems.Add($"{check.GetType().Name}: Id is empty.");
                else if (!seenIds.Add(check.Id))
                    // Ids are guide anchors AND the keys quarantine state is stored under.
                    // Two checks sharing one would silently share a mute.
                    problems.Add($"{check.Id}: two checks share this Id.");

                if (check.Doc == null) problems.Add($"{check.Id}: no CheckDoc at all.");
                else problems.AddRange(check.Doc.Validate(check.Id));
            }

            if (problems.Count > 0) return problems;

            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "index.html"),
                              Render(checks, fontFamily), new UTF8Encoding(false));
            return Array.Empty<string>();
        }

        private static string Render(IReadOnlyList<IAnomalyCheck> checks, string fontFamily)
        {
            var html = new StringBuilder();
            html.Append($$"""
                <!doctype html>
                <html lang="en">
                <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Sanity Check - System Optimizer</title>
                <style>
                  :root { color-scheme: light dark; }
                  /* The application's own font, so the guide reads like the program that
                     wrote it. The fallbacks after it are for the browser's sake, not the
                     app's - a web page has to survive a font the machine may not have. */
                  body { font-family: "{{E(fontFamily)}}", system-ui, sans-serif; line-height: 1.6;
                         max-width: 46rem; margin: 0 auto; padding: 2rem 1.25rem 4rem; }
                  h1 { font-size: 1.75rem; margin-bottom: .25rem; }
                  h2 { font-size: 1.25rem; margin-top: 2.5rem; padding-top: 1.25rem;
                       border-top: 1px solid rgba(128,128,128,.35); }
                  h3 { font-size: 1rem; margin-bottom: .25rem; }
                  .lede { opacity: .8; margin-top: 0; }
                  .id { font-family: Consolas, monospace; font-size: .8rem; opacity: .6;
                        display: block; font-weight: normal; }
                  .summary { font-size: 1.05rem; }
                  ul { padding-left: 1.25rem; }
                  nav ul { list-style: none; padding-left: 0; }
                  nav li { margin: .3rem 0; }
                  footer { margin-top: 3rem; font-size: .85rem; opacity: .7; }
                </style>
                </head>
                <body>
                <h1>Sanity Check</h1>
                <p class="lede">What each check looks at, why it matters, and when the
                answer is that nothing is wrong.</p>

                <p>Sanity Check does not look for things that are broken. Plenty of tools
                already do that, and a broken thing announces itself. It looks for things
                that are <em>working correctly and still wrong</em> - a network adapter
                running at a fraction of its speed, memory running slower than it is rated
                for, a graphics card sitting idle while the screen is driven by something
                else. In every one of those cases nothing has failed, no indicator is red,
                and nothing will ever tell you.</p>

                <p>Each check compares <strong>two facts observed separately</strong> and
                reports when they disagree. That is the only kind of check that can notice
                this sort of thing, and it is why every finding shows you both facts rather
                than a verdict. If a check cannot read one of its two facts, it says so
                instead of guessing - and if it cannot read them three times running, it
                switches itself off rather than nagging you about its own blind spot.</p>

                <p><strong>A finding is not a fault.</strong> Every check below lists the
                situations where the thing it found is deliberate and correct. Read that
                part first.</p>

                <nav><h2 style="border:0;padding:0;margin-top:2rem">The checks</h2><ul>

                """);

            foreach (var check in checks)
                html.Append($"<li><a href=\"#{E(check.Id)}\">{E(check.Title)}</a> - {E(check.Doc.Summary)}</li>\n");

            html.Append("</ul></nav>\n");

            foreach (var check in checks)
            {
                html.Append($"""

                    <h2 id="{E(check.Id)}">{E(check.Title)}<span class="id">{E(check.Id)}</span></h2>
                    <p class="summary">{E(check.Doc.Summary)}</p>

                    <h3>Why it matters</h3>
                    <p>{E(check.Doc.WhyItMatters)}</p>

                    <h3>When to ignore it</h3>
                    {List(check.Doc.WhenToIgnore)}

                    <h3>How to confirm it yourself</h3>
                    {List(check.Doc.HowToConfirm)}

                    <h3>What to do</h3>
                    {Steps(check.Doc.Remedy)}

                    <h3>How to tell it worked</h3>
                    <p>{E(check.Doc.HowToVerify)}</p>

                    """);
            }

            html.Append($"""

                <footer>
                <p>Generated from System Optimizer's own check list when the program was
                built, so this page always describes the version you have. {checks.Count}
                {(checks.Count == 1 ? "check" : "checks")} documented.</p>
                </footer>
                </body>
                </html>

                """);

            return html.ToString();
        }

        private static string List(IEnumerable<string> items) =>
            "<ul>\n" + string.Concat(items.Select(i => $"<li>{E(i)}</li>\n")) + "</ul>";

        private static string Steps(IEnumerable<string> items) =>
            "<ol>\n" + string.Concat(items.Select(i => $"<li>{E(i)}</li>\n")) + "</ol>";

        private static string E(string text) => WebUtility.HtmlEncode(text ?? "");
    }
}
