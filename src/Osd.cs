using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MonitorDeck
{
    // Piccolo riquadro a schermo per scorciatoie e cambio monitor
    class OsdForm : Form
    {
        readonly Timer timer = new Timer();
        readonly Font fText;
        readonly float s;
        string text = "";
        int value = -1, max = 100;

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;
            fText = new Font("Segoe UI Semibold", 11f);
            ClientSize = new Size((int)(280 * s), (int)(72 * s));
            timer.Interval = 1300;
            timer.Tick += delegate { timer.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; // NOACTIVATE | TOOLWINDOW | TRANSPARENT
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.RoundCorners(Handle); }

        public void Flash(string t, int v, int m, Rectangle monitorBounds)
        {
            text = t; value = v; max = Math.Max(1, m);
            int w = (int)(280 * s);
            if (v < 0)
            {
                Size sz = TextRenderer.MeasureText(t, fText);
                w = Math.Max((int)(200 * s), Math.Min((int)(520 * s), sz.Width + (int)(48 * s)));
            }
            ClientSize = new Size(w, (int)(72 * s));
            Rectangle b = monitorBounds.Width > 0 ? monitorBounds : Screen.PrimaryScreen.Bounds;
            Location = new Point(b.Left + (b.Width - Width) / 2, b.Bottom - Height - (int)(120 * s));
            Invalidate();
            if (!Visible) Show();
            timer.Stop();
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            int pad = (int)(18 * s);
            if (value >= 0)
            {
                Rectangle tr = new Rectangle(pad, (int)(12 * s), Width - 2 * pad, (int)(26 * s));
                TextRenderer.DrawText(g, text, fText, tr, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, value.ToString(), fText, tr, Theme.Accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                float th = 6 * s, ty = 48 * s, w = Width - 2 * pad;
                Theme.FillRound(g, new RectangleF(pad, ty, w, th), th / 2, Theme.Track);
                float fw = w * Math.Min(value, max) / max;
                if (fw > 1) Theme.FillRound(g, new RectangleF(pad, ty, fw, th), th / 2, Theme.Accent);
            }
            else
            {
                TextRenderer.DrawText(g, text, fText, ClientRectangle, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // Scorciatoie globali
    class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> Pressed;
        readonly List<int> ids = new List<int>();

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(int id, uint mods, Keys key)
        {
            if (Native.RegisterHotKey(Handle, id, mods, (uint)key)) { ids.Add(id); return true; }
            return false;
        }

        public void UnregisterAll()
        {
            foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
            ids.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose() { UnregisterAll(); DestroyHandle(); }

        public static bool Parse(string text, out uint mods, out Keys key)
        {
            mods = 0; key = Keys.None;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                string l = p.ToLowerInvariant();
                if (i < parts.Length - 1)
                {
                    if (l == "ctrl" || l == "control") mods |= 0x2;
                    else if (l == "alt") mods |= 0x1;
                    else if (l == "shift" || l == "maiusc") mods |= 0x4;
                    else if (l == "win") mods |= 0x8;
                    else return false;
                }
                else
                {
                    if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
                    else if (l == "su") p = "Up";
                    else if (l == "giu" || l == "giù") p = "Down";
                    else if (l == "sinistra") p = "Left";
                    else if (l == "destra") p = "Right";
                    try { key = (Keys)Enum.Parse(typeof(Keys), p, true); }
                    catch { return false; }
                }
            }
            return key != Keys.None;
        }
    }
}
