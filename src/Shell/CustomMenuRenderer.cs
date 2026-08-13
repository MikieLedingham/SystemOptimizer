// File: Helpers/CustomMenuRenderer.cs
using System.Drawing;
using System.Windows.Forms;
using Media = System.Windows.Media;

namespace SystemOptimizer.Shell
{
    /// <summary>
    /// Paints the tray menu with the same palette as the rest of the app.
    ///
    /// This file used to be an empty placeholder ("leave this file nearly empty!"), so
    /// the tray menu rendered in the stock WinForms Professional style - a light menu
    /// hanging off a dark application. WinForms does not follow the Windows dark theme
    /// and never has; the colours have to be supplied.
    ///
    /// They are read from the live WPF palette rather than hardcoded, so the tray menu
    /// tracks Appearance ▸ Dark/Light/Follow Windows along with everything else. Two
    /// menus that are the same menu should not be two different colours.
    /// </summary>
    public class CustomMenuRenderer : ToolStripProfessionalRenderer
    {
        /// <summary>Marker put on ToolStripItems that should draw in the accent colour.</summary>
        public const string EmphasisTag = "emphasis";

        public CustomMenuRenderer() : base(new PaletteColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = (e.Item.Tag as string) == EmphasisTag
                ? Brush("AccentBrush", Color.FromArgb(0xFF, 0x60, 0x4A))
                : e.Item.Enabled
                    ? Brush("TextPrimaryBrush", Color.White)
                    : Brush("TextDisabledBrush", Color.Gray);
            base.OnRenderItemText(e);
        }

        // ToolStripProfessionalRenderer draws the drop-down border from MenuBorder, but
        // the body background comes from here.
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Brush("MenuBackgroundBrush", Color.FromArgb(0x20, 0x20, 0x20))))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        /// <summary>
        /// Pulls a colour out of the merged WPF palette by resource key. Falls back to a
        /// dark-theme constant if the resource is missing or the app is not up yet -
        /// which is the case while the tray icon is being built during startup.
        /// </summary>
        internal static Color Brush(string key, Color fallback)
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app?.TryFindResource(key) is Media.SolidColorBrush b)
                    return Color.FromArgb(b.Color.A, b.Color.R, b.Color.G, b.Color.B);
            }
            catch { }
            return fallback;
        }

        private sealed class PaletteColors : ProfessionalColorTable
        {
            private static Color Bg    => Brush("MenuBackgroundBrush", Color.FromArgb(0x20, 0x20, 0x20));
            private static Color Hover => Brush("MenuHoverBrush",      Color.FromArgb(0x2F, 0x2F, 0x2F));
            private static Color Line  => Brush("SeparatorBrush",      Color.FromArgb(0x3A, 0x3A, 0x3A));
            private static Color Edge  => Brush("ControlBorderBrush",  Color.FromArgb(0x3A, 0x3A, 0x3A));

            public override Color ToolStripDropDownBackground => Bg;
            public override Color MenuBorder => Edge;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Hover;
            public override Color MenuItemPressedGradientMiddle => Hover;
            public override Color MenuItemPressedGradientEnd => Hover;
            public override Color SeparatorDark => Line;
            public override Color SeparatorLight => Line;
            // The image margin is switched off on this menu, but the gradient is still
            // painted behind sub-menus if it is left at the default light grey.
            public override Color ImageMarginGradientBegin => Bg;
            public override Color ImageMarginGradientMiddle => Bg;
            public override Color ImageMarginGradientEnd => Bg;
            public override Color CheckBackground => Hover;
            public override Color CheckSelectedBackground => Hover;
        }
    }
}
