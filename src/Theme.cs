using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MonitorDeck
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(24, 24, 27);
        public static readonly Color Border = Color.FromArgb(58, 58, 64);
        public static readonly Color Card = Color.FromArgb(40, 40, 45);
        public static readonly Color CardHover = Color.FromArgb(52, 52, 58);
        public static readonly Color CardDown = Color.FromArgb(34, 34, 38);
        public static readonly Color Accent = Color.FromArgb(0, 210, 200);
        public static readonly Color AccentDim = Color.FromArgb(0, 84, 80);
        public static readonly Color AccentText = Color.FromArgb(8, 24, 24);
        public static readonly Color Banner = Color.FromArgb(16, 52, 52);
        public static readonly Color BannerHover = Color.FromArgb(20, 64, 64);
        public static readonly Color Text = Color.FromArgb(240, 240, 242);
        public static readonly Color Muted = Color.FromArgb(150, 150, 160);
        public static readonly Color Track = Color.FromArgb(56, 56, 62);
        public static readonly Color Warn = Color.FromArgb(255, 180, 90);
        public static readonly Color Danger = Color.FromArgb(250, 110, 110);

        static string iconFontName;

        public static string IconFontName
        {
            get
            {
                if (iconFontName == null)
                {
                    using (Font f = new Font("Segoe Fluent Icons", 10f))
                        iconFontName = f.Name == "Segoe Fluent Icons" ? f.Name : "Segoe MDL2 Assets";
                }
                return iconFontName;
            }
        }

        public static readonly Font MenuFont = new Font("Segoe UI", 9.5f);
        static Font menuIconFont;
        public static Font MenuIconFont
        {
            get { if (menuIconFont == null) menuIconFont = new Font(IconFontName, 8.5f); return menuIconFont; }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static ContextMenuStrip NewMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = MenuFont;
            m.ShowImageMargin = true;
            StyleDropDown(m);
            return m;
        }

        public static void StyleDropDown(ToolStripDropDown dd)
        {
            dd.Font = MenuFont;
            dd.BackColor = Card;
            dd.ForeColor = Text;
            dd.HandleCreated += delegate { Native.RoundCorners(dd.Handle, 3); };
        }
    }

    static class Glyphs
    {
        public const string Close = "";
        public const string Minimize = "";
        public const string More = "";
        public const string Refresh = "";
        public const string Prev = "";
        public const string Next = "";
        public const string Down = "";
        public const string Brightness = "";
        public const string Volume = "";
        public const string Mute = "";
        public const string Download = "";
        public const string Check = "";
        public const string Pin = "";
        public const string Unpin = "";
    }

    // Menu scuri coerenti con la finestra
    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.CardHover; } }
        public override Color MenuItemSelected { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.CardHover; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Card; } }
        public override Color CheckBackground { get { return Theme.AccentDim; } }
        public override Color CheckSelectedBackground { get { return Theme.AccentDim; } }
        public override Color CheckPressedBackground { get { return Theme.AccentDim; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            if (r.Width <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box = Rectangle.Inflate(r, 1, 1);
            Theme.FillRound(e.Graphics, box, 4, Theme.AccentDim);
            TextRenderer.DrawText(e.Graphics, Glyphs.Check, Theme.MenuIconFont, box, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
