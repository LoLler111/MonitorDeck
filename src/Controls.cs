using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MonitorDeck
{
    // Pulsante arrotondato disegnato a mano
    class PillButton : Control
    {
        bool selected, hover, down;
        public bool Ghost;              // nessuno sfondo finche' non ci passi sopra (pulsanti icona)
        public object Tag2;
        public Color? TextColor;        // colore del testo forzato (es. rosso per le azioni pericolose)
        public Color? SelectedFill;

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            ForeColor = Theme.Text;
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        MouseButtons pressed;
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button; down = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        // i controlli WinForms generano Click anche col tasto destro: qui conta solo il sinistro
        protected override void OnClick(EventArgs e) { if (pressed == MouseButtons.Left) base.OnClick(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; if (!IsDisposed) Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill;
            if (selected) fill = SelectedFill ?? Theme.Accent;
            else if (down) fill = Theme.CardDown;
            else if (hover) fill = Theme.CardHover;
            else fill = Ghost ? Color.Empty : Theme.Card;
            if (fill != Color.Empty)
                Theme.FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height * 0.3f, fill);
            Color tc;
            if (selected) tc = Theme.AccentText;
            else if (TextColor.HasValue) tc = TextColor.Value;
            else tc = Ghost && !hover ? Theme.Muted : ForeColor;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, tc,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    // Pulsante a tendina: testo a sinistra, icona a destra
    class DropButton : Control
    {
        bool hover, down;
        public string Glyph = Glyphs.Down;
        public Font GlyphFont;

        public DropButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            ForeColor = Theme.Text;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        MouseButtons pressed;
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button; down = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        // i controlli WinForms generano Click anche col tasto destro: qui conta solo il sinistro
        protected override void OnClick(EventArgs e) { if (pressed == MouseButtons.Left) base.OnClick(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; if (!IsDisposed) Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = down ? Theme.CardDown : hover ? Theme.CardHover : Theme.Card;
            Theme.FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height * 0.3f, fill);
            int pad = Height / 3;
            int gw = Height - pad;
            Rectangle tr = new Rectangle(pad, 0, Width - pad - gw, Height);
            TextRenderer.DrawText(g, Text, Font, tr, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (GlyphFont != null && !string.IsNullOrEmpty(Glyph))
                TextRenderer.DrawText(g, Glyph, GlyphFont, new Rectangle(Width - gw - pad / 2, 0, gw, Height), hover ? Theme.Text : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    class Slider : Control
    {
        int min = 0, max = 100, val = 50;
        bool drag, hover;
        public bool WheelAdjusts = true;   // false: la rotellina scorre il pannello che lo contiene
        public event EventHandler ValueChanged;

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public bool Dragging { get { return drag; } }
        public int Maximum { get { return max; } set { max = Math.Max(1, value); Value = val; Invalidate(); } }
        public int Value
        {
            get { return val; }
            set { int v = Math.Max(min, Math.Min(max, value)); if (v != val) { val = v; Invalidate(); } }
        }

        void SetUser(int v)
        {
            v = Math.Max(min, Math.Min(max, v));
            if (v == val) return;
            val = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        void FromMouse(int x)
        {
            float pad = Height / 2f;
            float f = (x - pad) / Math.Max(1f, Width - 2 * pad);
            SetUser((int)Math.Round(min + f * (max - min)));
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Capture = true; FromMouse(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) FromMouse(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (WheelAdjusts)
            {
                int step = Math.Max(1, max / 50);
                SetUser(val + (e.Delta > 0 ? step : -step));
                HandledMouseEventArgs h = e as HandledMouseEventArgs;
                if (h != null) h.Handled = true;
            }
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = Math.Max(1, max / 50);
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) SetUser(val + step);
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) SetUser(val - step);
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float pad = Height / 2f;
            float trackH = Math.Max(4f, Height / 6f);
            float cy = Height / 2f;
            float w = Width - 2 * pad;
            float f = (float)(val - min) / Math.Max(1, max - min);
            float tx = pad + w * f;

            Theme.FillRound(g, new RectangleF(pad, cy - trackH / 2, w, trackH), trackH / 2, Theme.Track);
            if (tx - pad > 1) Theme.FillRound(g, new RectangleF(pad, cy - trackH / 2, tx - pad, trackH), trackH / 2, Enabled ? Theme.Accent : Theme.Muted);
            float r = Height * ((drag || hover) ? 0.34f : 0.30f);
            using (SolidBrush b = new SolidBrush(Theme.Text)) g.FillEllipse(b, tx - r, cy - r, 2 * r, 2 * r);
            float ri = r * 0.45f;
            using (SolidBrush b = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted)) g.FillEllipse(b, tx - ri, cy - ri, 2 * ri, 2 * ri);
        }
    }

    // Pannello che disegna testi e forme da una lista: niente Label, niente sfarfallio.
    class PaintedPanel : Control
    {
        public class Item
        {
            public Rectangle R;
            public Func<string> Text;
            public Font Font;
            public Func<Color> Color;
            public TextFormatFlags Flags;
            public Action<Graphics, Rectangle> Draw;
        }

        public readonly List<Item> Items = new List<Item>();

        public PaintedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
        }

        public void Reset()
        {
            Items.Clear();
            while (Controls.Count > 0) Controls[0].Dispose();
        }

        public Item AddText(Rectangle r, string text, Font f, Color c, TextFormatFlags fl)
        {
            return AddText(r, delegate { return text; }, f, delegate { return c; }, fl);
        }

        public Item AddText(Rectangle r, Func<string> text, Font f, Func<Color> c, TextFormatFlags fl)
        {
            Item it = new Item();
            it.R = r; it.Text = text; it.Font = f; it.Color = c; it.Flags = fl | TextFormatFlags.NoPadding;
            Items.Add(it);
            return it;
        }

        public Item AddDraw(Rectangle r, Action<Graphics, Rectangle> draw)
        {
            Item it = new Item();
            it.R = r; it.Draw = draw;
            Items.Add(it);
            return it;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Item it in Items)
            {
                if (!e.ClipRectangle.IntersectsWith(it.R)) continue;
                if (it.Draw != null) it.Draw(g, it.R);
                else TextRenderer.DrawText(g, it.Text(), it.Font, it.R, it.Color(), it.Flags);
            }
        }
    }

    // Contenitore con scorrimento verticale e barra sottile disegnata a mano
    class ScrollHost : Control
    {
        public readonly Control Content;
        readonly int gutter;
        int offset;
        bool dragThumb, hoverBar;
        int dragY, dragOffset;

        public ScrollHost(Control content, int gutter)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            this.gutter = gutter;
            Content = content;
            Controls.Add(content);
            content.Location = Point.Empty;
            content.SizeChanged += delegate { ScrollTo(offset); Invalidate(); };
        }

        public int ContentWidth { get { return Math.Max(10, Width - gutter); } }
        int MaxOffset { get { return Math.Max(0, Content.Height - Height); } }

        public void ScrollTo(int o)
        {
            o = Math.Max(0, Math.Min(MaxOffset, o));
            if (o == offset && Content.Top == -o) return;
            offset = o;
            Content.Top = -offset;
            Invalidate();
        }

        public void ScrollBy(int d) { ScrollTo(offset + d); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Content.Width = ContentWidth;
            ScrollTo(offset);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta * Math.Max(40, Height / 6) / 120);
            HandledMouseEventArgs h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
            base.OnMouseWheel(e);
        }

        Rectangle Thumb()
        {
            if (MaxOffset <= 0) return Rectangle.Empty;
            int th = Math.Max(gutter * 3, Height * Height / Math.Max(1, Content.Height));
            int ty = (int)((long)(Height - th) * offset / MaxOffset);
            return new Rectangle(Width - gutter, ty, gutter, th);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Rectangle t = Thumb();
            if (t.IsEmpty || e.X < Width - gutter) return;
            if (t.Contains(e.Location)) { dragThumb = true; dragY = e.Y; dragOffset = offset; Capture = true; }
            else ScrollBy(e.Y < t.Top ? -Height * 4 / 5 : Height * 4 / 5);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hb = e.X >= Width - gutter;
            if (hb != hoverBar) { hoverBar = hb; Invalidate(); }
            if (!dragThumb) return;
            Rectangle t = Thumb();
            int free = Math.Max(1, Height - t.Height);
            ScrollTo(dragOffset + (int)((long)(e.Y - dragY) * MaxOffset / free));
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragThumb = false; Capture = false; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverBar = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle t = Thumb();
            if (t.IsEmpty) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = (hoverBar || dragThumb) ? gutter * 0.55f : gutter * 0.35f;
            RectangleF r = new RectangleF(t.Right - w - gutter * 0.15f, t.Top + 2, w, t.Height - 4);
            Theme.FillRound(g, r, w / 2, (hoverBar || dragThumb) ? Theme.Muted : Theme.Border);
        }
    }
}
