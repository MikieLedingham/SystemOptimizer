// File: Helpers/TrayIconRenderer.cs
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SystemOptimizer.Shell
{
    /// <summary>
    /// Draws the RAM percentage straight into the tray icon at runtime.
    ///
    /// This once shipped 100 pre-rendered .ico files, one per percentage, picked by
    /// filename. That could never look right: an .ico is a fixed grid of pixel sizes, but
    /// the notification area asks for an icon sized to the current DPI - 16px at 100%,
    /// 20px at 125%, 24px at 150% - so on any scaled display Windows was resampling a
    /// 16px bitmap upward and the number came out soft. Three smaller faults came with it:
    /// the files are zero-padded (01.ico..09.ico) while the lookup built "{percent}.ico",
    /// so 1-9% never matched; there was no 0.ico; and the icon the tray started with,
    /// "RAM.ico", does not exist in the tree at all, so every launch showed the generic
    /// Windows application icon until the first update arrived.
    ///
    /// Drawing instead gives a crisp glyph at any DPI, colour that carries meaning,
    /// adaptation to a light or dark taskbar, and no files to ship.
    ///
    /// There is no Windows API that puts a number on a tray icon for you. Taskbar
    /// *buttons* can carry a small overlay icon (ITaskbarList3::SetOverlayIcon), and
    /// packaged apps get badge notifications, but neither applies to a notification-area
    /// icon. Rendering a bitmap and converting it to an HICON is the supported route.
    /// </summary>
    public static class TrayIconRenderer
    {
        // GetHicon hands back a raw HICON that the caller owns. Icon.FromHandle does NOT
        // take ownership, so the handle has to be destroyed by hand or it leaks for the
        // life of the process - and this runs once a second.
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        // Thresholds. Below Warn the icon is neutral and reads as "nothing to do here";
        // colour only appears when it means something, which is the same restraint the
        // Sanity Check design asks for.
        private const int WarnPercent = 75;
        private const int HighPercent = 90;

        private static readonly Color Amber = Color.FromArgb(0xFF, 0xB9, 0x00);
        private static readonly Color Red = Color.FromArgb(0xFF, 0x60, 0x4A);

        /// <summary>
        /// Renders <paramref name="percent"/> as a tray icon. The caller owns the result
        /// and must dispose it once it is no longer assigned to the NotifyIcon.
        /// </summary>
        public static Icon Render(int percent) => Render(percent, CurrentIconSize());

        /// <summary>
        /// The size the notification area wants right now, not a hardcoded 16: 16px at
        /// 100% DPI, 20px at 125%, 24px at 150%. Tracking this is the whole point - a
        /// fixed 16px .ico had to be resampled upward on any scaled display.
        /// </summary>
        public static int CurrentIconSize() =>
            Math.Max(16, System.Windows.Forms.SystemInformation.SmallIconSize.Width);

        /// <summary>Render at an explicit size. Separate so it can be exercised at every
        /// DPI step without changing the machine's display settings.</summary>
        public static Icon Render(int percent, int size)
        {
            percent = Math.Max(0, Math.Min(100, percent));

            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // Grid-fit hinting keeps strokes on whole pixels, which is what makes
                    // two digits legible at 16px. ClearType is wrong here: it assumes an
                    // opaque background and fringes colour over transparency.
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    string text = percent.ToString();
                    Color fg = percent >= HighPercent ? Red
                             : percent >= WarnPercent ? Amber
                             : TaskbarIsLight() ? Color.FromArgb(0x20, 0x20, 0x20)
                                                : Color.White;

                    // ONE StringFormat, used to measure AND to draw. Measuring with a
                    // different format than you draw with is how "48" came out as "4":
                    // the default format adds padding either side of the string, so the
                    // glyphs drew wider than they had been measured and the last digit
                    // fell outside the icon. Typographic has no such padding.
                    using (var format = new StringFormat(StringFormat.GenericTypographic)
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
                    })
                    using (var font = FitFont(g, text, size, format))
                    using (var brush = new SolidBrush(fg))
                    {
                        g.DrawString(text, font, brush, new RectangleF(0, 0, size, size), format);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (var temp = Icon.FromHandle(hIcon))
                        return (Icon)temp.Clone();   // clone owns its own copy of the bits
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        /// <summary>
        /// Largest font that fits the string in the icon box. "100" needs a smaller face
        /// than "7" does; picking one fixed size would either clip three digits or waste
        /// most of the square on one. Nothing is abbreviated - at 100% the number still
        /// reads 100, it is simply drawn narrower, and the colour carries the alarm.
        /// </summary>
        private static Font FitFont(Graphics g, string text, int size, StringFormat format)
        {
            // Digits sit inside the cap height, so a line box slightly taller than the
            // icon still draws entirely within it. Width is the binding constraint and is
            // held strictly; height is allowed the small overshoot that ascender and
            // descender space accounts for.
            for (float em = size; em > 4f; em -= 0.25f)
            {
                var candidate = new Font(AppFont.Name,em, FontStyle.Bold, GraphicsUnit.Pixel);
                var measured = g.MeasureString(text, candidate, new SizeF(int.MaxValue, int.MaxValue), format);
                if (measured.Width <= size && measured.Height <= size * 1.25f)
                    return candidate;
                candidate.Dispose();
            }
            return new Font(AppFont.Name,5f, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private static bool? _taskbarLight;

        /// <summary>
        /// SystemUsesLightTheme, NOT AppsUseLightTheme. Windows tracks the two separately
        /// and the taskbar follows the system one, so reusing ThemeManager's app-theme
        /// check here would paint dark digits on a dark taskbar for anyone running the
        /// common "light apps, dark taskbar" combination.
        /// </summary>
        private static bool TaskbarIsLight()
        {
            if (_taskbarLight.HasValue) return _taskbarLight.Value;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    _taskbarLight = key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
                }
            }
            catch
            {
                _taskbarLight = false;   // dark taskbar is the Windows default
            }
            return _taskbarLight.Value;
        }

        /// <summary>Forget the cached taskbar theme; call when Windows reports a settings change.</summary>
        public static void InvalidateTheme() => _taskbarLight = null;
    }
}
