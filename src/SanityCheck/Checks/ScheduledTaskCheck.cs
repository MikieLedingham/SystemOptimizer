// File: SanityCheck/Checks/ScheduledTaskCheck.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemOptimizer.SanityCheck.Checks
{
    /// <summary>
    /// A task that is switched on, and due, should have run at least once.
    ///
    /// THE TWO FACTS: the schedule says it should have started by now, and the run history
    /// says it never has. Two separate readings from the same service that ought to agree
    /// and sometimes flatly do not.
    ///
    /// THIS IS THE ONE THE WHOLE FEATURE COMES FROM. The originating case was a backup
    /// integration that had never worked once in its entire installed life while reporting
    /// itself healthy the whole time. Windows shows such a task as "Ready", which reads
    /// like readiness and actually means "not currently running". Nothing anywhere says
    /// "this has been scheduled for two years and has never once started".
    ///
    /// Read through the Task Scheduler COM service rather than by parsing schtasks output,
    /// because that output is localised and this project has already shipped one bug that
    /// depended on an English string (restoring from the Recycle Bin by verb name, which
    /// silently did nothing on non-English Windows).
    /// </summary>
    public sealed class ScheduledTaskCheck : IAnomalyCheck
    {
        public string Id => "TASK.NEVER_RAN";
        public string Title => "Scheduled tasks that have never run";

        // Probable, not Certain, so it cannot interrupt. The two readings are direct, but
        // "should have run by now" is a judgement: a task can legitimately never start
        // because it also waits for idle, for mains power, or for a network that is not
        // there. Those conditions are not read here, so the inference is not airtight.
        public Confidence Confidence => Confidence.Probable;
        public DateTime? ReviewBy => null;

        /// <summary>
        /// How long a task must have been due before never having run is worth mentioning.
        /// Thirty days rather than one, so that something set up last week - or a monthly
        /// job that simply has not come round yet - is never reported.
        /// </summary>
        internal const int GraceDays = 30;

        public bool DefaultEnabled => true;  // the case this whole feature came from

        public CheckDoc Doc => new CheckDoc
        {
            Summary = "Checks for scheduled tasks that are switched on and long overdue, " +
                      "but have never actually run.",
            WhyItMatters =
                "Backups, updaters and maintenance jobs usually run as scheduled tasks. " +
                "Windows lists such a task as Ready, which sounds reassuring and only means " +
                "it is not running at this instant. A task that was scheduled two years ago " +
                "and has never once started looks exactly the same as one that ran an hour " +
                "ago. If something you rely on runs this way, this is how you find out it " +
                "never has.",
            WhenToIgnore = new[]
            {
                "The task is meant to be started by its program rather than by the clock - " +
                "some installers register a task purely so the program can trigger it later.",
                "It only runs when conditions you have never met are true, such as the PC " +
                "being idle for an hour, or on mains power, or on a particular network.",
                "It belongs to software you have stopped using and no longer care about.",
                "It is a fallback that is supposed to never run - a repair or recovery task " +
                "that only fires when something else has already gone wrong."
            },
            HowToConfirm = new[]
            {
                "Press Windows, type Task Scheduler, and open it.",
                "Find the task by the name shown here. The Last Run Time column reads " +
                "30/11/1999 or similar when a task has never run.",
                "Check the Triggers tab to see when it was supposed to start, and the " +
                "Conditions tab for requirements such as idle time or mains power."
            },
            Remedy = new[]
            {
                "Decide first whether you still want the thing this task does. If not, the " +
                "answer is to remove or disable it, not to make it run.",
                "In Task Scheduler, select the task and choose Run to try it by hand. " +
                "Whatever happens next is the useful information.",
                "If it fails, the History tab says why - most often the program has been " +
                "removed, or the account it runs as no longer has a valid password.",
                "If it succeeds by hand but never on schedule, look at the Conditions tab. " +
                "Requiring idle time or mains power is the usual reason."
            },
            HowToVerify =
                "After the next scheduled time, open Task Scheduler and check that Last Run " +
                "Time shows a real date and Last Run Result is 0x0."
        };

        public AnomalyResult Evaluate(ProbeContext context)
        {
            var readings = new List<TaskReading>();
            try
            {
                var type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null)
                    return AnomalyResult.Inconclusive("The Task Scheduler service is not available on this PC.");

                dynamic service = Activator.CreateInstance(type);
                service.Connect();
                Collect(service.GetFolder("\\"), readings, context);
            }
            catch (Exception ex)
            {
                return AnomalyResult.Inconclusive("The scheduled tasks could not be read (" + ex.Message + ").");
            }

            if (readings.Count == 0)
                return AnomalyResult.NotApplicable(
                    "This PC has no scheduled tasks of its own with a timed trigger.");

            foreach (var r in readings.Where(r => r.NeverRan))
                context.Note($"task {r.Path} never ran; due since {r.DueSince:d}");

            return Decide(readings, DateTime.Now);
        }

        /// <summary>
        /// Walks the task tree, skipping Windows' own.
        ///
        /// \Microsoft is excluded deliberately and it is a scope decision, not laziness:
        /// it holds several hundred tasks, many of which are SUPPOSED never to run - repair
        /// paths, telemetry that respects a setting, features not installed. Reporting them
        /// would bury the one task the user actually cares about, which is the failure this
        /// design bends hardest to avoid.
        /// </summary>
        private static void Collect(dynamic folder, List<TaskReading> readings, ProbeContext context)
        {
            string path = folder.Path;
            if (path.StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase)) return;

            foreach (dynamic task in folder.GetTasks(0))
            {
                try
                {
                    if (!task.Enabled) continue;

                    DateTime? dueSince = OldestTimedTriggerStart(task);
                    if (dueSince == null) continue;   // nothing timed: not this check's business

                    DateTime lastRun = task.LastRunTime;

                    readings.Add(new TaskReading
                    {
                        Name = task.Name,
                        Path = task.Path,
                        // Task Scheduler reports "never" as 30 December 1899, the COM epoch.
                        NeverRan = lastRun.Year < 2000,
                        DueSince = dueSince.Value
                    });
                }
                catch (Exception ex)
                {
                    // One unreadable task must not cost the whole check. Recorded so a
                    // machine where MOST of them fail shows up as odd rather than as clean.
                    context.Note("a scheduled task could not be read: " + ex.Message);
                }
            }

            foreach (dynamic child in folder.GetFolders(0))
                Collect(child, readings, context);
        }

        /// <summary>
        /// The earliest start time among this task's clock-based triggers, or null if it
        /// has none. A task triggered by logon, by boot, by an event or on demand has no
        /// "should have happened by now", so it cannot contradict anything.
        /// </summary>
        private static DateTime? OldestTimedTriggerStart(dynamic task)
        {
            DateTime? oldest = null;
            foreach (dynamic trigger in task.Definition.Triggers)
            {
                try
                {
                    if (!trigger.Enabled) continue;

                    // 2 TIME, 3 DAILY, 4 WEEKLY, 5 MONTHLY, 6 MONTHLYDOW. Everything else
                    // (event, boot, logon, idle, registration, session change) is not timed.
                    int type = (int)trigger.Type;
                    if (type is not (2 or 3 or 4 or 5 or 6)) continue;

                    string boundary = trigger.StartBoundary as string;
                    if (string.IsNullOrWhiteSpace(boundary)) continue;
                    if (!DateTime.TryParse(boundary, out DateTime start)) continue;

                    if (oldest == null || start < oldest) oldest = start;
                }
                catch { }
            }
            return oldest;
        }

        internal static AnomalyResult Decide(IReadOnlyList<TaskReading> readings, DateTime now)
        {
            var overdue = readings
                .Where(r => r.NeverRan)
                .Where(r => (now - r.DueSince).TotalDays >= GraceDays)
                .OrderBy(r => r.DueSince)
                .ToList();

            if (overdue.Count == 0)
                return AnomalyResult.Pass(
                    $"{readings.Count} scheduled {(readings.Count == 1 ? "task is" : "tasks are")} " +
                    "switched on with a timed trigger",
                    "all of them have run at least once, or are not due yet");

            var worst = overdue[0];
            int days = (int)(now - worst.DueSince).TotalDays;

            return AnomalyResult.Finding(
                overdue.Count == 1
                    ? $"\"{worst.Name}\" has been scheduled to run since {worst.DueSince:d}"
                    : $"{overdue.Count} scheduled tasks have been due for a long time, " +
                      $"the oldest being \"{worst.Name}\" since {worst.DueSince:d}",
                overdue.Count == 1
                    ? $"it has never run, in {days} days"
                    : "none of them has ever run",
                "Windows lists a task like this as Ready, which only means it is not running " +
                "right now - it looks the same as one that ran an hour ago. Something set " +
                "this up expecting it to run, and it never has. If it is a backup or an " +
                "updater, it has not been doing its job since the day it was installed.");
        }

        internal sealed class TaskReading
        {
            public string Name = "";
            public string Path = "";
            public bool NeverRan;
            public DateTime DueSince;
        }
    }
}
