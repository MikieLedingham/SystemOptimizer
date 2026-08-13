// File: Shell/AppFont.cs
using System.Windows;

namespace SystemOptimizer.Shell
{
    /// <summary>
    /// The application font, for the parts of the program that are not WPF.
    ///
    /// Most of the app gets its font from the <c>AppFont</c> resource in Native.xaml by
    /// DynamicResource, so changing that one line changes every window. Three places
    /// cannot do that, because they are not WPF at all:
    ///
    ///   - the tray's context menu, which is WinForms and needs a System.Drawing.Font;
    ///   - the tray icon itself, whose digits are drawn with GDI+;
    ///   - and the Sanity Check guide, which is a web page (it keeps its own CSS stack,
    ///     since a browser needs fallbacks WPF does not).
    ///
    /// The first two read the name from here, so they follow the resource instead of
    /// naming a font of their own. Without this the tray menu and the tray icon would
    /// quietly stay in the old face after a font change - and the tray is the part of this
    /// app that is on screen all the time, so it is the worst possible place to leave
    /// behind. That is the same failure this whole change is about, one layer down.
    /// </summary>
    public static class AppFont
    {
        /// <summary>Used only if the resource cannot be reached - before App.xaml's
        /// dictionaries are loaded, or in a host that never loaded them.</summary>
        private const string Fallback = "Segoe UI";

        /// <summary>
        /// ONE family name that this machine actually has, e.g. "Segoe UI Variable Text".
        ///
        /// Not simply the resource's text. The resource is a WPF fallback list - "Segoe UI
        /// Variable Text, Segoe UI" - and WPF walks it, taking the first family present.
        /// GDI+ DOES NOT: System.Drawing.Font is handed a single family name, and given a
        /// comma-separated string it finds no such family and silently substitutes its own
        /// default. The tray menu and the tray icon's digits would have come out in
        /// Microsoft Sans Serif while every window used the right font, and nothing would
        /// have said so.
        ///
        /// So the list is walked here, the same way WPF walks it, and the first family the
        /// machine really has is returned. Read live rather than cached, because the
        /// resource is a DynamicResource everywhere else and a cached copy here would be
        /// the one that did not follow a change.
        /// </summary>
        public static string Name
        {
            get
            {
                string requested = (Application.Current?.TryFindResource("AppFont")
                                        as System.Windows.Media.FontFamily)?.Source;

                foreach (string candidate in (requested ?? Fallback).Split(','))
                {
                    string name = candidate.Trim().Trim('\'', '"');
                    if (name.Length > 0 && IsInstalled(name)) return name;
                }

                // Nothing in the list exists. Returning the first name anyway would hand
                // GDI+ something it will substitute for; the fallback at least resolves.
                return Fallback;
            }
        }

        private static bool IsInstalled(string family)
        {
            try
            {
                // Asked of GDI+, not WPF, because GDI+ is who has to draw with it. The two
                // do not always agree, and the one that matters here is the one that will
                // be handed this name.
                using var f = new System.Drawing.FontFamily(family);
                return true;
            }
            catch (System.ArgumentException) { return false; }
        }
    }
}
