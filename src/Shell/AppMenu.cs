// File: Helpers/AppMenu.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SystemOptimizer.Dialogs;
using Forms = System.Windows.Forms;
using Wpf = System.Windows.Controls;
using SystemOptimizer.Core.Cleanup;
using SystemOptimizer.Core.Ram;
using SystemOptimizer.Core.Settings;
using SystemOptimizer.Core.Platform;
using SystemOptimizer.Core.Logging;

namespace SystemOptimizer.Shell
{
    /// <summary>
    /// The application menu, defined once and rendered in both places it appears.
    ///
    /// There were once two menus. The window's lived in MainWindow.xaml as
    /// WPF markup; the tray's was built by hand in TrayIconManager out of WinForms
    /// ToolStripItems. They had drifted: the tray had no Appearance, no Cleanup options
    /// and no Keyboard shortcuts, the window had no Screenshot overlay and no No-boost
    /// mode, and the actions they did share had picked up three different sets of names -
    /// "Check Logs" against "Open logs folder", "Overlay Toggle" against "Toggle
    /// overlay", "Check Last RAM Result" against "Last RAM result" - along with two
    /// different capitalisation conventions. Nothing designs two names for one action.
    ///
    /// Exactly ONE difference is real and is kept: "Open System Optimizer" appears only
    /// in the tray, because a window cannot usefully offer to open itself.
    ///
    /// The menu is rebuilt each time it opens rather than built once and cached, so
    /// checkmarks, conditional entries and tooltips are always current. It is cheap -
    /// about twenty items - and it removes the class of bug where a state change and the
    /// menu that reports it get out of step.
    /// </summary>
    public static class AppMenu
    {
        public enum Host { Window, Tray }

        public sealed class Entry
        {
            public string Text;
            public Action Invoke;
            public List<Entry> Children;
            public string Gesture;              // "Ctrl+Shift+Alt+O"
            public Func<bool> Checked;          // tick state, evaluated at open time
            public Func<bool> Visible;          // conditional entries
            public bool Emphasis;               // drawn in the accent colour
            public string Tooltip;
            public Host? Only;                  // null = appears in both
            public bool IsSeparator;

            public static Entry Separator(Host? only = null) => new Entry { IsSeparator = true, Only = only };
        }

        // ---- shared plumbing ---------------------------------------------------

        private static MainWindow Main => System.Windows.Application.Current?.MainWindow as MainWindow;

        /// <summary>
        /// Marshals to the UI thread. The tray menu's handlers run on the WinForms
        /// message loop, and several of these open WPF dialogs, so this is not optional
        /// on that path - it is the difference between a dialog and an
        /// InvalidOperationException.
        /// </summary>
        private static void OnUi(Action action)
        {
            var app = System.Windows.Application.Current;
            if (app == null || app.Dispatcher.HasShutdownStarted) return;
            app.Dispatcher.Invoke(() => { try { action(); } catch (Exception ex) { LogHelper.Log("Menu action failed: " + ex); } });
        }

        private static void ShowDialog<T>(Func<T> make) where T : System.Windows.Window => OnUi(() =>
        {
            var dlg = make();
            dlg.Owner = Main;
            dlg.WindowStartupLocation = Main != null
                ? System.Windows.WindowStartupLocation.CenterOwner
                : System.Windows.WindowStartupLocation.CenterScreen;
            dlg.ShowDialog();
        });

        // ---- the definition ----------------------------------------------------

        private static List<Entry> Define() => new List<Entry>
        {
            new Entry { Text = "Open System Optimizer", Only = Host.Tray, Invoke = () => OnUi(() => Main?.RestoreMainWindow()) },
            Entry.Separator(Host.Tray),

            // "Boost RAM now" used to sit here and called exactly the same BoostRam() as
            // "Quick RAM boost" in the Shortcuts submenu. Two names for one action; the
            // submenu's reads better, so this one went.
            new Entry { Text = "Clean now", Invoke = () => OnUi(() => Main?.ShowAreYouSureAndClean()) },
            // The undo. Every cleanup log has promised "Restore a previous clean" since the
            // engine started writing manifests; this is the first release where the words
            // point at something the user can actually open.
            new Entry { Text = "Restore a previous clean...", Invoke = () => ShowDialog(() => new RestoreDialog()) },
            // Beside "Clean now" rather than in Shortcuts: it is a thing the user does to
            // the machine, not a hotkey for something they already know about. Runs the
            // checks first and hands the result to the window, so the window is never
            // asked to show a report that does not exist yet.
            new Entry { Text = "Check my PC...", Invoke = () => ShowDialog(() => new SanityCheckDialog(SanityCheck.SanityRunner.Run())) },
            Entry.Separator(),

            // Was a single "Cleanup options..." opening a dialog whose only content was
            // three buttons. The three pages are on the main window now, so the menu names
            // them directly - and the tray, which has no main window to look at, gains the
            // same reach rather than a detour through a window that no longer exists.
            new Entry { Text = "Basic cleanup...", Invoke = () => OnUi(() => Main?.ShowBasicCleanup()) },
            new Entry { Text = "Admin cleanup...", Invoke = () => OnUi(() => Main?.ShowAdminCleanup()) },
            new Entry { Text = "RAM cleanup...",   Invoke = () => OnUi(() => Main?.ShowRamCleanup()) },
            new Entry { Text = "Start with Windows...", Invoke = () => ShowDialog(() => new StartupDialog()) },
            new Entry { Text = "Overlay options...", Invoke = () => ShowDialog(() => new OverlayOptionsDialog(PreferencesManager.Current.OverlayFields)) },
            new Entry
            {
                Text = "Screenshot overlay",
                // Only offered when there is an overlay to photograph. In the tray menu
                // this used to be permanently present and silently did nothing.
                Visible = () => OverlayIsOpen(),
                Invoke = () => OnUi(() => System.Windows.Application.Current.Windows
                                            .OfType<OverlayWindow>().FirstOrDefault()?.CaptureOverlayToClipboard())
            },
            Entry.Separator(),

            new Entry
            {
                Text = "Appearance",
                Children = new List<Entry>
                {
                    new Entry { Text = "Dark",           Checked = () => ThemeManager.CurrentTheme == ThemeManager.Theme.Dark,   Invoke = () => OnUi(() => ThemeManager.ApplyTheme(ThemeManager.Theme.Dark)) },
                    new Entry { Text = "Light",          Checked = () => ThemeManager.CurrentTheme == ThemeManager.Theme.Light,  Invoke = () => OnUi(() => ThemeManager.ApplyTheme(ThemeManager.Theme.Light)) },
                    new Entry { Text = "Follow Windows", Checked = () => ThemeManager.CurrentTheme == ThemeManager.Theme.System, Invoke = () => OnUi(() => ThemeManager.ApplyTheme(ThemeManager.Theme.System)) },
                }
            },
            // "No-boost list..." was here too, duplicating the Shortcuts submenu entry.
            new Entry
            {
                Text = "No-boost mode",
                // Only meaningful while automatic RAM boosting is on - with AutoRam off
                // there is nothing for it to suppress.
                Visible  = () => PreferencesManager.Current.Ram.AutoRam,
                Checked  = () => Tools.NoBoost.NoBoostMode.Enabled,
                Emphasis = Tools.NoBoost.NoBoostMode.Enabled,
                Tooltip  = NoBoostTooltip(),
                Invoke   = ToggleNoBoost
            },
            new Entry
            {
                // The escape hatch. With click-through on, the overlay ignores the mouse,
                // so its own checkbox cannot be used to turn it back off. App.xaml.cs used
                // to bolt this onto the tray menu after the fact, in two near-identical
                // methods, which meant the window menu had no way out at all.
                Text = "Disable click-through",
                Visible = () => PreferencesManager.Current.Overlay.ClickThrough,
                Emphasis = true,
                Invoke = DisableClickThrough
            },
            new Entry
            {
                Text = "RAM warnings",
                Visible = () => UacHelper.IsRunningAsAdmin() && AutoRamMonitorHelper.AutoRamWarningShown,
                Emphasis = true,
                Invoke = () => OnUi(App.ShowAutoCleanWarnings)
            },
            Entry.Separator(),

            // Doubles as the keyboard-shortcut reference: every item shows its hotkey and
            // also performs the action.
            //
            // It is also the single home for the five entries that used to appear at the
            // top level as well - Boost RAM now (which called the same action as Quick RAM
            // boost under a worse name), No-boost list, Open logs folder, Toggle overlay
            // and Last RAM result. Every one of those was one action wearing two names in
            // one menu. All are Ctrl+Shift+Alt + key.
            new Entry
            {
                Text = "Shortcuts",
                Children = new List<Entry>
                {
                    new Entry { Text = "Toggle overlay",       Gesture = "Ctrl+Shift+Alt+O", Invoke = () => OnUi(App.ToggleOverlay) },
                    new Entry { Text = "Quick RAM boost",      Gesture = "Ctrl+Shift+Alt+B", Invoke = BoostRam },
                    new Entry { Text = "Last RAM result",      Gesture = "Ctrl+Shift+Alt+S", Invoke = () => OnUi(App.ShowLastRamResult) },
                    new Entry { Text = "No-boost list",        Gesture = "Ctrl+Shift+Alt+G", Invoke = () => ShowDialog(() => new ManageGamesDialog()) },
                    new Entry { Text = "Open logs folder",     Gesture = "Ctrl+Shift+Alt+L", Invoke = OpenLogs },
                    new Entry { Text = "Diagnostics",          Gesture = "Ctrl+Shift+Alt+D", Invoke = () => ShowDialog(() => new DiagnosticsWindow()) },
                    new Entry { Text = "Advanced admin tools", Gesture = "Ctrl+Shift+Alt+T", Invoke = () => ShowDialog(() => new AdminToolsDialog()) },
                }
            },
            Entry.Separator(),

            new Entry { Text = "Open Task Manager", Invoke = () => Try(() => Process.Start("taskmgr.exe")) },
            new Entry { Text = "Restart Explorer",  Invoke = RestartExplorer },
            Entry.Separator(),

            new Entry { Text = "About", Invoke = () => ShowDialog(() => new AboutWindow()) },
            new Entry { Text = "Exit",  Invoke = () => OnUi(() => System.Windows.Application.Current?.Shutdown()) },
        };

        // ---- actions -----------------------------------------------------------

        private static void Try(Action a) { try { a(); } catch (Exception ex) { LogHelper.Log("Menu action failed: " + ex); } }

        private static bool OverlayIsOpen() =>
            System.Windows.Application.Current?.Windows.OfType<OverlayWindow>().Any(w => w.IsVisible) == true;

        private static void BoostRam() => Try(() =>
        {
            CleanupHelper.RunRamOnlyQuickTrim(UacHelper.IsRunningAsAdmin());
            OnUi(App.ShowLastRamResult);
            PreferencesManager.SavePreferences();
        });

        private static void OpenLogs() => Try(LogManager.OpenLogFolder);

        private static void DisableClickThrough() => OnUi(() =>
        {
            PreferencesManager.Current.Overlay.ClickThrough = false;
            PreferencesManager.SavePreferences();
            var overlay = System.Windows.Application.Current.Windows
                .OfType<OverlayWindow>().FirstOrDefault(w => w.IsVisible);
            if (overlay?.ClickThroughCheckBox != null)
            {
                overlay.ClickThroughCheckBox.IsChecked = false;
                overlay.ClickThroughCheckBox.RaiseEvent(new System.Windows.RoutedEventArgs(
                    System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent));
            }
        });

        private static void RestartExplorer() => Try(() =>
        {
            foreach (var proc in Process.GetProcessesByName("explorer"))
                proc.Kill();
            Process.Start("explorer.exe");
        });

        // Was a fourth hand-rolled reader of AppsList.json. There were three others and
        // two of them disagreed about the file's shape - see Tools/NoBoost/NoBoostList.cs.
        private static List<string> NoBoostApps() =>
            Tools.NoBoost.NoBoostList.Selected().Select(e => e.Name).ToList();

        private static string NoBoostTooltip()
        {
            var apps = NoBoostApps();
            return apps.Count == 0
                ? "No apps are listed, so automatic RAM cleanup will carry on as normal. Add apps to the no-boost list to make this do anything."
                : "Pauses automatic RAM cleanup whenever these are running: " + string.Join(", ", apps);
        }

        private static void ToggleNoBoost() => Try(() =>
        {
            Tools.NoBoost.NoBoostMode.Enabled = !Tools.NoBoost.NoBoostMode.Enabled;

            if (!Tools.NoBoost.NoBoostMode.Enabled)
            {
                App.ShowTrayNotification("No-boost mode off. Automatic RAM boost resumes as normal.");
                return;
            }

            if (NoBoostApps().Count == 0)
            {
                App.ShowTrayNotification("No-boost mode on, but no apps are listed. Automatic RAM cleanup will carry on until you add some.");
                return;
            }

            // Says whether it is blocking something RIGHT NOW rather than describing what
            // it would do in principle. This fires once, here, when the mode is switched
            // on - the automatic boost path itself stays silent, so a blocked machine does
            // not produce a notification every sixty seconds. A blocker that starts later
            // is therefore not announced; it just quietly holds boosts off, which is the
            // behaviour asked for.
            // The counted summary, not a single name: with several ticked applications
            // running this used to name whichever happened to be found first.
            string blocked = Tools.NoBoost.NoBoostTool.BlockedSummary();
            App.ShowTrayNotification(blocked != null
                ? $"{blocked}. Manual boosts still work."
                : "No-boost mode on. RAM cleanup pauses whenever your chosen apps are running.");
        });

        // ---- renderers ---------------------------------------------------------

        /// <summary>
        /// The entries this host should show, with separators tidied.
        ///
        /// Several entries are conditional - No-boost mode, RAM warnings, Disable
        /// click-through, Screenshot overlay - so whole groups can vanish, and a group
        /// that vanishes would otherwise leave its separator behind: two rules stacked on
        /// each other, or one floating at the top or bottom of the menu. Collapsing them
        /// here means neither renderer, nor anyone adding an entry later, has to think
        /// about it.
        /// </summary>
        private static List<Entry> For(Host host, IEnumerable<Entry> entries)
        {
            var visible = entries
                .Where(e => (e.Only == null || e.Only == host) && (e.Visible == null || e.Visible()))
                .ToList();

            var result = new List<Entry>();
            foreach (var e in visible)
            {
                if (e.IsSeparator && (result.Count == 0 || result[result.Count - 1].IsSeparator))
                    continue;                       // leading, or one already there
                result.Add(e);
            }
            while (result.Count > 0 && result[result.Count - 1].IsSeparator)
                result.RemoveAt(result.Count - 1);  // trailing
            return result;
        }

        /// <summary>The window's right-click menu.</summary>
        public static Wpf.ContextMenu BuildWpf(Host host = Host.Window)
        {
            var menu = new Wpf.ContextMenu();
            foreach (var item in BuildWpfItems(host, Define()))
                menu.Items.Add(item);
            return menu;
        }

        private static List<object> BuildWpfItems(Host host, List<Entry> entries)
        {
            var result = new List<object>();
            foreach (var e in For(host, entries))
            {
                if (e.IsSeparator)
                {
                    var sep = new System.Windows.Controls.Separator();
                    if (System.Windows.Application.Current?.TryFindResource("MenuSeparatorStyle") is System.Windows.Style s)
                        sep.Style = s;
                    result.Add(sep);
                    continue;
                }

                var mi = new Wpf.MenuItem { Header = e.Text };
                if (!string.IsNullOrEmpty(e.Gesture)) mi.InputGestureText = e.Gesture;
                if (!string.IsNullOrEmpty(e.Tooltip)) mi.ToolTip = e.Tooltip;
                if (e.Checked != null) { mi.IsCheckable = true; mi.IsChecked = e.Checked(); }
                if (e.Emphasis && System.Windows.Application.Current?.TryFindResource("AccentBrush") is System.Windows.Media.Brush b)
                    mi.Foreground = b;

                if (e.Children != null)
                    foreach (var child in BuildWpfItems(host, e.Children))
                        mi.Items.Add(child);
                else if (e.Invoke != null)
                {
                    var action = e.Invoke;
                    mi.Click += (_, __) => action();
                }
                result.Add(mi);
            }
            return result;
        }

        /// <summary>The tray icon's menu. Same entries, WinForms controls.</summary>
        public static Forms.ContextMenuStrip BuildWinForms(Host host = Host.Tray)
        {
            // ShowImageMargin=false removes the left gutter. This once painted Images/MTstrip.png
            // down it; that strip is gone and there are no item icons, so it has nothing
            // left to show.
            var menu = new Forms.ContextMenuStrip
            {
                ShowImageMargin = false,
                Renderer = new CustomMenuRenderer(),
                // From the AppFont resource, not a literal: this menu is WinForms, so it
                // cannot use a DynamicResource and would otherwise be the one piece of the
                // app left in the old face after a font change.
                Font = new System.Drawing.Font(AppFont.Name, 10F)
            };
            foreach (var item in BuildWinFormsItems(host, Define()))
                menu.Items.Add(item);
            return menu;
        }

        private static List<Forms.ToolStripItem> BuildWinFormsItems(Host host, List<Entry> entries)
        {
            var result = new List<Forms.ToolStripItem>();
            foreach (var e in For(host, entries))
            {
                if (e.IsSeparator) { result.Add(new Forms.ToolStripSeparator()); continue; }

                var mi = new Forms.ToolStripMenuItem(e.Text);
                if (!string.IsNullOrEmpty(e.Gesture)) mi.ShortcutKeyDisplayString = e.Gesture;
                if (!string.IsNullOrEmpty(e.Tooltip)) mi.ToolTipText = e.Tooltip;
                if (e.Checked != null) mi.Checked = e.Checked();
                // Emphasis is flagged, not coloured, here: the colour has to come from the
                // renderer so it tracks the theme rather than being baked in.
                if (e.Emphasis) mi.Tag = CustomMenuRenderer.EmphasisTag;

                if (e.Children != null)
                    mi.DropDownItems.AddRange(BuildWinFormsItems(host, e.Children).ToArray());
                else if (e.Invoke != null)
                {
                    var action = e.Invoke;
                    mi.Click += (_, __) => action();
                }
                result.Add(mi);
            }
            return result;
        }
    }
}
