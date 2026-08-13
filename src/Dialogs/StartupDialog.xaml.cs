// File: Dialogs/StartupDialog.xaml.cs
using System.Windows;
using System.Windows.Input;
using SystemOptimizer.Core.Platform;
using SystemOptimizer.Shell;

namespace SystemOptimizer.Dialogs
{
    public partial class StartupDialog : Window
    {
        // What Windows reported when the dialog opened, so Save can tell whether the
        // startup entry needs writing at all. Both are read once: each one shells out to
        // schtasks, and re-reading them would also let the answer change underneath the
        // comparison.
        private readonly StartupManager.Mode _openedWithMode;
        private readonly bool _openedWithMinimised;

        public StartupDialog()
        {
            InitializeComponent();

            // Read the current state from Windows itself - the Run key and the task list -
            // rather than from preferences.json. A stored copy would drift the moment
            // anyone used Task Manager's Startup tab or Task Scheduler, and the dialog
            // would then be reporting our opinion rather than what actually happens.
            var mode = StartupManager.CurrentMode;
            _openedWithMode = mode;
            _openedWithMinimised = StartupManager.StartsMinimised;

            OffRadio.IsChecked = mode == StartupManager.Mode.Disabled;
            NormalRadio.IsChecked = mode == StartupManager.Mode.Normal;
            ElevatedRadio.IsChecked = mode == StartupManager.Mode.Elevated;
            MinimisedCheckbox.IsChecked = _openedWithMinimised;

            // Read from the Windows compatibility flag itself, never from a stored copy -
            // it can be changed from the file's Properties sheet, and a second copy would
            // start disagreeing the moment anyone did.
            AlwaysAdminCheckbox.IsEnabled = ElevationPreference.IsSupported;
            AlwaysAdminCheckbox.IsChecked = ElevationPreference.IsSupported
                                            && ElevationPreference.AlwaysRunAsAdmin;

            ApplyElevationLimits();
        }

        /// <summary>
        /// Greys out what cannot actually be applied, instead of letting it be chosen.
        ///
        /// The dialog used to say "the elevated option cannot be set or removed from here
        /// yet" while leaving that option live and clickable - so the obvious next move
        /// was to pick it, press Save, and be told no. A control that is offered should
        /// work; one that cannot should look like it cannot.
        ///
        /// Two different limits, because Apply refuses in two different cases:
        ///   - the elevated option itself always needs administrator rights;
        ///   - and once the scheduled task EXISTS, every option needs them, because
        ///     switching away from it means deleting that task. Without this, somebody
        ///     with elevated startup already set could not even change "start minimised",
        ///     and would have had to find that out by pressing Save.
        /// </summary>
        private void ApplyElevationLimits()
        {
            if (UacHelper.IsRunningAsAdmin())
            {
                StatusText.Text = "";
                ElevateLink.Visibility = Visibility.Collapsed;
                return;
            }

            bool taskExists = _openedWithMode == StartupManager.Mode.Elevated;

            ElevatedRadio.IsEnabled = false;
            if (taskExists)
            {
                OffRadio.IsEnabled = false;
                NormalRadio.IsEnabled = false;
                MinimisedCheckbox.IsEnabled = false;
                // Save stays ENABLED. The tick below it is a compatibility flag under
                // HKCU, which needs no administrator rights - disabling Save would make it
                // unsavable for a reason that has nothing to do with it.
                StatusText.Text = "System Optimizer is set to start as administrator. " +
                                  "Changing how it starts removes that scheduled task, " +
                                  "which needs administrator rights.";
            }
            else
            {
                StatusText.Text = "Starting as administrator needs administrator rights to " +
                                  "set up. The other options work as they are.";
            }

            ElevateLink.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Restarts elevated so the choice can actually be made.
        ///
        /// Passing this window lets the restart close it before posting the shutdown, so
        /// the modal stack unwinds - the same reason AdminCleanupDialog passes itself.
        /// </summary>
        private void ElevateLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ElevationPreference.RestartElevated(this);

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var mode = ElevatedRadio.IsChecked == true ? StartupManager.Mode.Elevated
                     : NormalRadio.IsChecked == true ? StartupManager.Mode.Normal
                     : StartupManager.Mode.Disabled;

            // The compatibility flag is applied first and separately: it is a different
            // mechanism from the logon entry, it needs no administrator rights, and it
            // must not be lost because the startup change happened to be refused.
            bool wantAlwaysAdmin = AlwaysAdminCheckbox.IsChecked == true;
            if (AlwaysAdminCheckbox.IsEnabled &&
                wantAlwaysAdmin != ElevationPreference.AlwaysRunAsAdmin &&
                !ElevationPreference.SetAlwaysRunAsAdmin(wantAlwaysAdmin))
            {
                // Put the tick back rather than leave it claiming something that did not
                // happen. A checkbox that lies is worse than one that refuses.
                AlwaysAdminCheckbox.IsChecked = !wantAlwaysAdmin;
                CustomMessageBox.Show(
                    "Windows would not accept the \"always run as administrator\" setting. " +
                    "You can also set it from the program's Properties, under Compatibility.",
                    "Could not change elevation setting",
                    CustomMessageBox.Kind.Error);
                return;
            }

            // Only write the startup entry if it actually changed. Two reasons, and the
            // first is a real refusal rather than an optimisation:
            //
            //   - Apply refuses EVERY change while the elevated task exists and we are not
            //     elevated, because switching away from it means deleting that task. The
            //     tick above needs no rights at all, so without this test, saving only the
            //     tick would be refused for a reason that has nothing to do with it - the
            //     dialog would stay open reporting an error about a control the user never
            //     touched, having already applied the one they did.
            //   - Apply's normal path clears both mechanisms and re-creates the wanted one.
            //     Pressing Save having changed nothing would delete and re-register the
            //     scheduled task for no reason.
            bool minimised = MinimisedCheckbox.IsChecked == true;
            bool startupUnchanged = mode == _openedWithMode && minimised == _openedWithMinimised;

            if (startupUnchanged || StartupManager.Apply(mode, minimised, out var error))
            {
                DialogResult = true;
                return;
            }

            // Stay open on failure. Closing would leave the radio buttons showing a
            // choice that was never applied.
            StatusText.Text = error;
            CustomMessageBox.Show(error, "Could not change startup", CustomMessageBox.Kind.Warning);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
