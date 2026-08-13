// File: Helpers/ThemeManager.cs
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using SystemOptimizer.Core.Settings;

namespace SystemOptimizer.Shell
{
    /// <summary>
    /// Swaps the colour palette at runtime.
    ///
    /// 2.0 defaults to Dark. Structure lives in Themes/Native.xaml and references every
    /// brush via DynamicResource, so replacing the merged palette dictionary re-themes
    /// open windows immediately - no restart, no rebuilding views.
    ///
    /// Replaces the earlier version, which pointed at Themes/SkinnedTheme.xaml and
    /// ProfessionalTheme.xaml. Those files were never registered in the csproj, so they
    /// were never copied to the output folder and the loader could never have found
    /// them. Nothing called into it either.
    /// </summary>
    public static class ThemeManager
    {
        public enum Theme { Dark, Light, System }

        private const string DarkPath  = "pack://application:,,,/Themes/Palette.Dark.xaml";
        private const string LightPath = "pack://application:,,,/Themes/Palette.Light.xaml";

        private static ResourceDictionary _currentPalette;

        /// <summary>Dark is the 2.0 default.</summary>
        public static Theme CurrentTheme { get; private set; } = Theme.Dark;

        public static void ApplyTheme(Theme theme)
        {
            bool useDark = theme == Theme.System ? !WindowsPrefersLight() : theme == Theme.Dark;

            var dict = new ResourceDictionary
            {
                Source = new Uri(useDark ? DarkPath : LightPath, UriKind.Absolute)
            };

            var app = Application.Current;
            if (app == null) return;

            // Drop every palette currently merged, including the Palette.Dark.xaml that
            // App.xaml declares. Matching on the URI rather than tracking one reference
            // matters: the startup dictionary is not the one this class created.
            var stale = app.Resources.MergedDictionaries
                           .Where(d => d.Source != null &&
                                       d.Source.OriginalString.IndexOf("Palette.", StringComparison.OrdinalIgnoreCase) >= 0)
                           .ToList();
            foreach (var d in stale)
                app.Resources.MergedDictionaries.Remove(d);

            // APPEND, do not insert. WPF resolves merged dictionaries in reverse order,
            // so the last one added wins. Inserting at index 0 put the new palette behind
            // the one App.xaml declares, which is why switching to Light previously
            // changed only the title bar and left the window content dark.
            app.Resources.MergedDictionaries.Add(dict);
            _currentPalette = dict;

            CurrentTheme = theme;
            SavePreference(theme);

            // The title bar is drawn by Windows, not WPF, so a dark client area would
            // otherwise sit under a light caption. Apply to every open window.
            foreach (Window w in app.Windows)
                ApplyTitleBar(w, useDark);
        }

        // DWMWA_USE_IMMERSIVE_DARK_MODE. 20 on Windows 10 2004+ and Windows 11;
        // earlier 10 builds used 19. Try 20 first, fall back to 19.
        private const int DwmDarkModeCurrent = 20;
        private const int DwmDarkModeLegacy = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private static bool _hooked;

        /// <summary>
        /// Theme the caption of every window the app opens, now and in future.
        /// Registering a class handler once beats calling ApplyTitleBar from each window's
        /// Loaded - which is what left every dialog with a light title bar above a dark
        /// client area, and which any new window would have forgotten too.
        /// </summary>
        public static void HookNewWindows()
        {
            if (_hooked) return;
            _hooked = true;
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, e) =>
                {
                    if (s is not Window w) return;
                    ApplyTitleBar(w);
                    ApplyIcon(w);
                }));
        }

        private static System.Windows.Media.ImageSource _appIcon;

        /// <summary>
        /// Gives every window the same title-bar icon.
        ///
        /// Only MainWindow ever set one, so every other window fell back to whatever
        /// Windows chose - the owner's icon for an owned dialog, the process default
        /// otherwise - and the result was that different windows in the same application
        /// showed different icons. Mikie reported it as headers not matching, and it is
        /// the reason a screenshot of one dialog shows a rocket and another does not.
        ///
        /// Set here rather than in seventeen XAML files so a future window cannot forget,
        /// and so changing the icon is one edit. The URI names the assembly explicitly: a
        /// bare pack URI resolves against the ENTRY assembly, which breaks the moment this
        /// code is loaded by anything else - a test harness, for one.
        /// </summary>
        private static void ApplyIcon(Window window)
        {
            try
            {
                if (window.Icon != null) return;   // an explicit choice wins
                _appIcon ??= new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/SystemOptimizer;component/SO.ico", UriKind.Absolute));
                window.Icon = _appIcon;
            }
            catch { /* a missing icon is cosmetic; never take a window down for it */ }
        }

        /// <summary>Match a window's title bar to the current theme. Safe to call anytime.</summary>
        public static void ApplyTitleBar(Window window) =>
            ApplyTitleBar(window, CurrentTheme == Theme.System ? !WindowsPrefersLight() : CurrentTheme == Theme.Dark);

        private static void ApplyTitleBar(Window window, bool dark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return; // not shown yet; caller retries after load

                int on = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DwmDarkModeCurrent, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DwmDarkModeLegacy, ref on, sizeof(int));
            }
            catch { /* pre-Windows-10, or DWM unavailable - light caption is acceptable */ }
        }

        /// <summary>True when Windows is set to light mode for apps.</summary>
        private static bool WindowsPrefersLight()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
                }
            }
            catch
            {
                return false; // default to dark if the value cannot be read
            }
        }

        public static void LoadLastThemeOrDefault()
        {
            var theme = Theme.Dark;
            try
            {
                if (File.Exists(AppPaths.ThemeFile))
                {
                    var pref = File.ReadAllText(AppPaths.ThemeFile).Trim();
                    if (Enum.TryParse(pref, true, out Theme parsed))
                        theme = parsed;
                }
            }
            catch { /* fall through to Dark */ }

            ApplyTheme(theme);
        }

        private static void SavePreference(Theme theme)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.ThemeFile));
                File.WriteAllText(AppPaths.ThemeFile, theme.ToString());
            }
            catch { }
        }
    }
}
