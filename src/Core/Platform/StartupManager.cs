// File: Helpers/StartupManager.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Core.Platform
{
    /// <summary>
    /// Starting with Windows - optionally elevated, optionally straight to the tray.
    ///
    /// There are two mechanisms here rather than one, because Windows will not let a
    /// single mechanism do both jobs:
    ///
    /// NORMAL  HKCU\...\CurrentVersion\Run. Needs no administrator rights to set, never
    ///         prompts, and starts the app as a normal user.
    ///
    /// ELEVATED  A scheduled task with "run with highest privileges", triggered at logon.
    ///         The Run key CANNOT do this. Windows deliberately refuses to auto-elevate
    ///         from Run or the Startup folder - an executable carrying the RUNASADMIN
    ///         compatibility flag is silently SKIPPED at logon rather than prompting - so
    ///         "start with Windows as administrator" via the Run key does not half-work,
    ///         it does nothing at all. Task Scheduler is the supported route, and it is
    ///         the only way to start elevated without a UAC prompt.
    ///
    /// THE TRADE-OFF, PLAINLY: an elevated logon task means System Optimizer gets full
    /// administrator rights at every logon without asking. That is a real and permanent
    /// widening of what runs privileged on the machine, and anything able to trigger the
    /// task inherits it. It is a documented, ordinary Windows feature and the user's own
    /// choice to make - but it is a choice, which is why the dialog says so and why
    /// creating the task needs administrator rights up front.
    ///
    /// Both mechanisms record the exe PATH. Neither survives the app being moved, which
    /// is one more reason the installed build - fixed location, elevated installer that
    /// can register the task without a second prompt - is the better home for this.
    /// </summary>
    public static class StartupManager
    {
        public enum Mode { Disabled, Normal, Elevated }

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "System Optimizer";
        private const string TaskName = "System Optimizer Startup";

        /// <summary>Told to the app by both mechanisms, so it can start straight to the tray.</summary>
        public const string MinimisedArgument = "--minimised";
        public const string StartupArgument = "--startup";

        private static string ExePath => Environment.ProcessPath;

        public static Mode CurrentMode
        {
            get
            {
                if (TaskExists()) return Mode.Elevated;
                return ReadRunValue() != null ? Mode.Normal : Mode.Disabled;
            }
        }

        /// <summary>Whether the configured startup entry, whichever it is, asks for the tray.</summary>
        public static bool StartsMinimised
        {
            get
            {
                var command = TaskExists() ? ReadTaskCommand() : ReadRunValue();
                return command != null &&
                       command.IndexOf(MinimisedArgument, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>Elevated mode needs administrator rights to register or remove the task.</summary>
        public static bool NeedsElevationFor(Mode mode) =>
            (mode == Mode.Elevated || TaskExists()) && !UacHelper.IsRunningAsAdmin();

        public static bool Apply(Mode mode, bool minimised, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(ExePath))
            {
                error = "The location of the program could not be determined.";
                return false;
            }
            if (NeedsElevationFor(mode))
            {
                // Does not send the reader to another page any more. The Startup dialog
                // now disables what it cannot apply and offers the restart itself, so this
                // is a backstop for callers that are not that dialog rather than an
                // instruction to go hunting for a control somewhere else.
                error = "Changing this needs administrator rights.";
                return false;
            }

            try
            {
                // Always clear both, then set the one wanted. Leaving a stale Run value
                // behind a new scheduled task would start the app twice at logon - the
                // second copy would exit on the single-instance mutex, but only after
                // Windows had launched it.
                RemoveRunValue();
                if (TaskExists() && !RemoveTask(out error)) return false;

                switch (mode)
                {
                    case Mode.Normal:
                        return WriteRunValue(minimised, out error);
                    case Mode.Elevated:
                        return CreateTask(minimised, out error);
                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log("StartupManager.Apply failed: " + ex);
                error = ex.Message;
                return false;
            }
        }

        // ---- Run key -----------------------------------------------------------

        private static string ReadRunValue()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return key?.GetValue(RunValueName) as string;
            }
            catch { return null; }
        }

        private static bool WriteRunValue(bool minimised, out string error)
        {
            error = null;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    key.SetValue(RunValueName, Arguments(minimised, quoteExe: true), RegistryValueKind.String);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static void RemoveRunValue()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                    key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            catch (Exception ex) { LogHelper.Log("Removing Run value failed: " + ex); }
        }

        private static string Arguments(bool minimised, bool quoteExe)
        {
            var sb = new StringBuilder();
            if (quoteExe) sb.Append('"').Append(ExePath).Append('"');
            sb.Append(quoteExe ? " " : "").Append(StartupArgument);
            if (minimised) sb.Append(' ').Append(MinimisedArgument);
            return sb.ToString();
        }

        // ---- Scheduled task ----------------------------------------------------

        private static bool TaskExists() => RunSchTasks($"/query /tn \"{TaskName}\"", out _) == 0;

        private static string ReadTaskCommand()
        {
            if (RunSchTasks($"/query /tn \"{TaskName}\" /xml", out var xml) != 0) return null;
            return xml;
        }

        private static bool RemoveTask(out string error)
        {
            error = null;
            if (RunSchTasks($"/delete /tn \"{TaskName}\" /f", out var output) == 0) return true;
            error = "Could not remove the startup task: " + output;
            return false;
        }

        private static bool CreateTask(bool minimised, out string error)
        {
            error = null;
            // schtasks /create /xml is used rather than the flag form because the flags
            // cannot express a logon trigger and highest privileges together, and cannot
            // clear DisallowStartIfOnBatteries - which otherwise stops the app starting on
            // a laptop running on battery, silently.
            var file = Path.Combine(Path.GetTempPath(), "so-startup-task.xml");
            try
            {
                File.WriteAllText(file, TaskXml(minimised), Encoding.Unicode); // schtasks requires UTF-16
                int code = RunSchTasks($"/create /tn \"{TaskName}\" /xml \"{file}\" /f", out var output);
                if (code == 0) return true;
                error = "Windows would not create the startup task: " + output;
                return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { try { File.Delete(file); } catch { } }
        }

        private static string TaskXml(bool minimised)
        {
            string user = WindowsIdentityName();
            string args = Arguments(minimised, quoteExe: false);
            return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts System Optimizer with administrator rights when {user} logs on.</Description>
    <URI>\{TaskName}</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{Escape(user)}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{Escape(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{Escape(ExePath)}</Command>
      <Arguments>{Escape(args)}</Arguments>
    </Exec>
  </Actions>
</Task>";
        }

        private static string WindowsIdentityName()
        {
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                return id.Name;
        }

        private static string Escape(string s) =>
            s?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") ?? "";

        private static int RunSchTasks(string arguments, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }
}
