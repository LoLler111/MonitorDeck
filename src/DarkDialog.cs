using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MonitorDeck
{
    // Finestra di dialogo nello stile dell'app (conferme, novita' della versione, messaggi)
    class DarkDialog : Form
    {
        readonly float s;
        readonly Font fTitle, fBody, fButton;
        readonly string title, body;
        Rectangle rTitle, rBody, rNotes;

        DarkDialog(string title, string body, string notes, string okText, string cancelText, bool danger)
        {
            this.title = title;
            this.body = body;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Text = title;
            using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;
            fTitle = new Font("Segoe UI Semibold", 12f);
            fBody = new Font("Segoe UI", 9.5f);
            fButton = new Font("Segoe UI Semibold", 9.5f);

            int W = S(notes != null ? 460 : 400), pad = S(22), y = S(20);
            rTitle = new Rectangle(pad, y, W - 2 * pad, S(26));
            y += S(36);
            if (!string.IsNullOrEmpty(body))
            {
                Size sz = TextRenderer.MeasureText(body, fBody, new Size(W - 2 * pad, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                rBody = new Rectangle(pad, y, W - 2 * pad, sz.Height + S(2));
                y += rBody.Height + S(14);
            }
            if (notes != null)
            {
                rNotes = new Rectangle(pad, y, W - 2 * pad, S(230));
                TextBox tb = new TextBox();
                tb.Multiline = true;
                tb.ReadOnly = true;
                tb.BorderStyle = BorderStyle.None;
                tb.ScrollBars = ScrollBars.Vertical;
                tb.BackColor = Theme.Card;
                tb.ForeColor = Theme.Text;
                tb.Font = fBody;
                tb.Text = notes;
                tb.TabStop = false;
                tb.Bounds = Rectangle.Inflate(rNotes, -S(12), -S(10));
                tb.HandleCreated += delegate { Native.DarkScrollbars(tb.Handle); };
                Controls.Add(tb);
                y += rNotes.Height + S(18);
            }
            else y += S(4);

            int bh = S(36), bw = S(132), x = W - pad;
            PillButton ok = new PillButton();
            ok.Font = fButton;
            ok.Text = okText;
            ok.Selected = true;
            if (danger) ok.SelectedFill = Theme.Danger;
            x -= bw;
            ok.Bounds = new Rectangle(x, y, bw, bh);
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);
            if (cancelText != null)
            {
                PillButton cancel = new PillButton();
                cancel.Font = fButton;
                cancel.Text = cancelText;
                x -= bw + S(8);
                cancel.Bounds = new Rectangle(x, y, bw, bh);
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(cancel);
            }
            y += bh + S(20);
            ClientSize = new Size(W, y);
        }

        int S(float v) { return (int)Math.Round(v * s); }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.RoundCorners(Handle); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Native.DragWindow(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, title, fTitle, rTitle, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (!rBody.IsEmpty)
                TextRenderer.DrawText(g, body, fBody, rBody, Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            if (!rNotes.IsEmpty) Theme.FillRound(g, rNotes, S(10), Theme.Card);
        }

        static IWin32Window OwnerOrNull(Form owner)
        {
            return owner != null && owner.Visible && owner.WindowState != FormWindowState.Minimized ? owner : null;
        }

        public static bool Ask(Form owner, string title, string body, string notes, string okText, string cancelText, bool danger)
        {
            using (DarkDialog d = new DarkDialog(title, body, notes, okText, cancelText, danger))
            {
                IWin32Window o = OwnerOrNull(owner);
                if (o != null) d.StartPosition = FormStartPosition.CenterParent;
                return d.ShowDialog(o) == DialogResult.OK;
            }
        }

        public static void Info(Form owner, string title, string body)
        {
            Ask(owner, title, body, null, "OK", null, false);
        }
    }
}
