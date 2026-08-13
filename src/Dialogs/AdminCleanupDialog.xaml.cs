// File: AdminCleanupDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Security.Principal;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Platform;
using SystemOptimizer.Shell;
namespace SystemOptimizer.Dialogs
{
    public partial class AdminCleanupDialog : Window
    {
        private readonly bool _isAdmin;

        /// <summary>TRUE until preferences have been read in. See BasicCleanupDialog.</summary>
        private bool _loading = true;

        public AdminCleanupDialog()
        {
            InitializeComponent();
            CenterOnActiveScreen();
            Topmost = true;
            // Check elevation once
            _isAdmin = IsRunAsAdmin();
            LoadPreferences();
            _loading = false;

            // Gate FIRST, warn second.
            //
            // These used to run the other way round, with a modal message box between
            // loading the ticks and disabling them. Anything that stops the modal
            // returning normally therefore leaves the page live and ticked, and the
            // window is not on screen yet while that modal is up, so there is no way to
            // see that it happened. The gate must not depend on a dialog.
            if (!_isAdmin)
            {
                DisableAdminOptions();

                // Raised from Loaded, not from here. The warning now OFFERS the two
                // actions it describes, and both need this window to exist: one restarts
                // the application and closes it on the way, the other ticks a checkbox on
                // it. Run from the constructor they would be acting on a window that has
                // not been shown, which is how the elevated restart previously threw
                // InvalidOperationException back at whoever opened this page.
                if (PreferencesManager.Current.Admin.ShowAdminWarning)
                    Loaded += WarnAboutElevationOnce;
            }
        }

        private void WarnAboutElevationOnce(object sender, RoutedEventArgs e)
        {
            Loaded -= WarnAboutElevationOnce;
            ShowUacWarningDialog();
        }
        public bool CleanOldWindows => OldWindowsCheckbox.IsChecked == true;
        public bool CleanRecycleBin => RecycleBinCheckbox.IsChecked == true;
        public bool CleanCrashDumps => CrashDumpsCheckbox.IsChecked == true;
        public bool CleanWindowsTemp => WindowsTempCheckbox.IsChecked == true;
        /// <summary>
        /// Every tick saves.
        ///
        /// This replaces an OK button that began with "if (!_isAdmin) return;" - so when
        /// the app was not elevated, pressing OK silently did nothing at all, and no change
        /// on this page could be saved. That included "Warn me when not running as
        /// administrator", the one setting somebody in that exact state is most likely to
        /// want to change.
        /// </summary>
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SavePreferences();
        }

        private void ClearSaved_Click(object sender, RoutedEventArgs e)
        {
            _loading = true;
            WindowsTempCheckbox.IsChecked = false;
            CrashDumpsCheckbox.IsChecked = false;
            OldWindowsCheckbox.IsChecked = false;
            RecycleBinCheckbox.IsChecked = false;
            _loading = false;
            SavePreferences();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void CenterOnActiveScreen()
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
            var wa = screen.WorkingArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;
        }
        private void LoadPreferences()
        {
            var admin = PreferencesManager.Current.Admin;
            WindowsTempCheckbox.IsChecked = admin.WindowsTemp;
            CrashDumpsCheckbox.IsChecked = admin.CrashDumps;
            OldWindowsCheckbox.IsChecked = admin.OldWindows;
            RecycleBinCheckbox.IsChecked = admin.RecycleBin;
            ShowAdminWarningCheckbox.IsChecked = admin.ShowAdminWarning;
            LoadElevationPreference();
        }

        /// <summary>
        /// The elevation setting is NOT read from preferences.json - it is read back from
        /// the Windows compatibility flag that is the actual setting. Storing our own copy
        /// would let the two disagree the moment anyone changed it from the file's property
        /// sheet, and the tick would then be reporting our opinion rather than the truth.
        /// </summary>
        private void LoadElevationPreference()
        {
            // Nothing to restart into if this process is already elevated.
            RestartAsAdminButton.Visibility = IsRunAsAdmin() || !ElevationPreference.IsSupported
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// Opens the page that owns every startup and elevation choice.
        ///
        /// This card used to carry the "Always run as administrator" tick itself. It
        /// worked, but a single checkbox reads as the complete set of options, so the
        /// other arrangements - start with Windows, elevated with no prompt via a
        /// scheduled task, minimised to the tray - would likely never be found. The tick
        /// now lives beside them.
        /// </summary>
        private void OpenStartup_Click(object sender, RoutedEventArgs e)
        {
            new StartupDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            }.ShowDialog();

            LoadElevationPreference();   // an elevated restart from there changes this page
        }

        private void RestartAsAdminButton_Click(object sender, RoutedEventArgs e)
        {
            // Passing `this` lets the restart close this dialog before it posts the
            // shutdown, so the modal stack unwinds properly - see RestartElevated.
            // Returns false when the user declines the UAC prompt, which is a normal
            // answer and not worth a dialog of its own.
            ElevationPreference.RestartElevated(this);
        }
        private void SavePreferences()
        {
            var admin = PreferencesManager.Current.Admin;
            // Save all option states
            admin.WindowsTemp = WindowsTempCheckbox.IsChecked == true;
            admin.CrashDumps = CrashDumpsCheckbox.IsChecked == true;
            admin.OldWindows = OldWindowsCheckbox.IsChecked == true;
            admin.RecycleBin = RecycleBinCheckbox.IsChecked == true;
            // admin.DNSCache and admin.ThumbnailCache are deliberately NOT written here
            // any more. Those settings live on Basic now, and the v3 migration moved the
            // stored values across; writing them from this page would put back a second
            // source for a value that is supposed to have exactly one.
            admin.Remember = true;
            admin.ShowAdminWarning = ShowAdminWarningCheckbox.IsChecked == true;
            PreferencesManager.SavePreferences();
        }
        private bool IsRunAsAdmin()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
        private void ShowUacWarningDialog()
        {
            // Was a bare Window with a TextBlock in it and no Background set, so Windows
            // painted it its default white - a white 18pt box in front of a dark app - and
            // SizeToContent.WidthAndHeight with no MaxWidth let it grow to whatever width
            // the longest line wanted. It is now the app's own themed message box.
            // The prose no longer names two controls and leave the reader to find them.
            // It says what is true, and the two suggestions below it are the actions.
            var msg = "System Optimizer is not running as administrator.\n\n" +
                      "The cleanups on this page need elevation, so they are switched off. " +
                      "Everything else works normally.";

            var choices = new[]
            {
                new CustomMessageBox.Choice
                {
                    Text = "Restart System Optimizer as administrator now",
                    // Runs after the box has closed, with this window shown, so the
                    // restart can close it and unwind the modal stack properly.
                    Invoke = () => ElevationPreference.RestartElevated(this)
                },
                new CustomMessageBox.Choice
                {
                    // OPENS the page, rather than making the change.
                    //
                    // The right call: setting the compatibility flag from
                    // here silently picks one of several answers on the user's behalf.
                    // "Start with Windows" is where the real choice lives - run at logon
                    // or not, elevated or not, by Run key or by scheduled task, with the
                    // security trade-off of each spelled out. A one-line shortcut that
                    // quietly commits to one of those is a worse offer than a door to all
                    // of them.
                    Text = "Choose how System Optimizer starts, including as administrator",
                    Invoke = () =>
                    {
                        var dlg = new StartupDialog
                        {
                            Owner = IsLoaded ? this : null,
                            WindowStartupLocation = IsLoaded
                                ? WindowStartupLocation.CenterOwner
                                : WindowStartupLocation.CenterScreen
                        };
                        dlg.ShowDialog();
                        LoadElevationPreference();   // the page may now disagree with itself
                    }
                }
            };

            bool suppress = CustomMessageBox.ShowWithSuppress(
                msg,
                "Administrator rights needed",
                "Don't warn me about this again",
                CustomMessageBox.Kind.Warning,
                choices);

            // The old text ended by explaining that the warning keeps appearing "while
            // this option is enabled" without offering any way to turn it off from here.
            if (suppress)
            {
                PreferencesManager.Current.Admin.ShowAdminWarning = false;
                PreferencesManager.SavePreferences();
                ShowAdminWarningCheckbox.IsChecked = false;
            }
        }
        /// <summary>
        /// Unelevated, this page shows nothing ticked and nothing clickable.
        ///
        /// It used to disable the boxes but leave them TICKED, so a selection made during
        /// an elevated session was still displayed in an unelevated one - the page claimed
        /// it was going to flush the DNS cache while being incapable of doing so.
        ///
        /// The ticks are cleared for DISPLAY ONLY. _loading suppresses the save, so the
        /// stored choices survive and come back the moment the app runs elevated again.
        /// Persisting the clear would silently destroy real settings just because the app
        /// happened to be opened without admin rights.
        /// </summary>
        // AlwaysRunAsAdminFromWarning was here: it set the compatibility flag directly
        // from the warning box. Removed deliberately - a suggestion that commits to
        // one answer takes the other answers away. The link opens "Start with Windows"
        // instead, where all of them are presented with their trade-offs.

        private void DisableAdminOptions()
        {
            var gray = Brushes.LightGray;
            _loading = true;
            try
            {
                foreach (var cb in new[] {
                    WindowsTempCheckbox, CrashDumpsCheckbox,
                    OldWindowsCheckbox, RecycleBinCheckbox })
                {
                    cb.IsChecked = false;
                    cb.IsEnabled = false;
                    cb.Foreground = gray;
                }
            }
            finally { _loading = false; }
            // Nothing else is disabled. "Clear saved choices", the warning toggle and the
            // elevation controls all still work without admin rights - and the elevation
            // controls are the entire point of being on this page while not elevated.
        }
    }
}
