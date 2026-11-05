using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace MonitorDeck
{
    // Finestra principale: colonna compatta (comandi principali) + pannello espandibile con tutti i controlli.
    class MainForm : Form
    {
        readonly DeckApp app;
        readonly float s;
        public readonly Font fTitle, fName, fSub, fSection, fValue, fButton, fRow, fStatus, fIcon, fIconBig;
        readonly PaintedPanel left, right, details;
        readonly ScrollHost scroll;
        readonly ToolTip tips = new ToolTip();
        readonly ContextMenuStrip moreMenu;
        string mainSig, detailsSig;
        int compactH;
        bool syncQueued, updating, positioned;
        public DateTime LastDeactivate = DateTime.MinValue;

        readonly List<PillButton> inputBtns = new List<PillButton>();
        Slider slBright, slVol;
        PillButton btnMute, btnTop;
        DropButton btnExpand;
        Banner banner;
        MonitorCard card;
        PaintedPanel.Item statusItem, versionItem;
        readonly Dictionary<byte, Slider> dSliders = new Dictionary<byte, Slider>();
        readonly Dictionary<byte, DropButton> dDrops = new Dictionary<byte, DropButton>();

        public MainForm(DeckApp app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            BackColor = Theme.Border;   // il bordo da 1 px e' lo spazio lasciato libero dai pannelli
            ForeColor = Theme.Text;
            KeyPreview = true;
            Text = AppInfo.Name;
            DoubleBuffered = true;
            TopMost = app.Cfg.TopMost;
            try { Icon = app.WindowIcon; } catch { }

            using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;

            fTitle = new Font("Segoe UI Semibold", 12.5f);
            fName = new Font("Segoe UI Semibold", 10.5f);
            fSub = new Font("Segoe UI", 8.5f);
            fSection = new Font("Segoe UI Semibold", 7.75f);
            fValue = new Font("Segoe UI Semibold", 9f);
            fButton = new Font("Segoe UI Semibold", 9.5f);
            fRow = new Font("Segoe UI", 9.5f);
            fStatus = new Font("Segoe UI", 8f);
            fIcon = new Font(Theme.IconFontName, 10f);
            fIconBig = new Font(Theme.IconFontName, 12f);

            left = new PaintedPanel();
            right = new PaintedPanel();
            details = new PaintedPanel();
            scroll = new ScrollHost(details, S(10));
            right.Controls.Add(scroll);
            Controls.Add(left);
            Controls.Add(right);
            left.MouseDown += HeaderMouseDown;
            right.MouseDown += HeaderMouseDown;

            moreMenu = app.BuildMenu(false);
            BuildRightHeader();
            Sync();
        }

        public int S(float v) { return (int)Math.Round(v * s); }
        int CompactW { get { return S(340); } }
        int DetailsW { get { return S(372); } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;          // CS_DROPSHADOW
                cp.Style |= 0x00020000 | 0x00080000;  // WS_MINIMIZEBOX | WS_SYSMENU: riduci a icona dalla barra delle applicazioni
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < S(52)) Native.DragWindow(Handle);
        }

        // ------------------------------------------------------------ aggiornamento dell'interfaccia

        // Le modifiche arrivano da eventi e thread diversi: si raggruppano in un solo aggiornamento.
        public void RequestSync()
        {
            if (syncQueued || !IsHandleCreated || IsDisposed) return;
            syncQueued = true;
            BeginInvoke((Action)delegate { syncQueued = false; Sync(); });
        }

        public void Sync()
        {
            if (IsDisposed) return;
            SuspendLayout();
            string ms = MainSignature();
            if (ms != mainSig) { mainSig = ms; BuildLeft(); }
            if (app.Expanded)
            {
                string ds = DetailsSignature();
                if (ds != detailsSig) { detailsSig = ds; BuildDetails(); }
            }
            UpdateValues();
            ApplySize();
            ResumeLayout();
        }

        string MainSignature()
        {
            StringBuilder sb = new StringBuilder();
            MonitorState st = app.Current;
            sb.Append(app.Monitors.Count).Append('|').Append(app.Expanded).Append('|').Append(app.Update != null);
            if (st != null)
            {
                sb.Append('|').Append(st.Key).Append(st.MainRead)
                  .Append(st.Supports(Vcp.Input)).Append(st.Supports(Vcp.Brightness))
                  .Append(st.Supports(Vcp.Volume)).Append(st.Supports(Vcp.Mute));
                foreach (KeyValuePair<string, uint> kv in app.InputsFor(st)) sb.Append(kv.Key).Append(kv.Value);
            }
            return sb.ToString();
        }

        public void UpdateValues()
        {
            MonitorState st = app.Current;
            updating = true;
            if (st != null)
            {
                int inp = st.Cur(Vcp.Input);
                foreach (PillButton b in inputBtns) b.Selected = inp >= 0 && (uint)b.Tag2 == (uint)inp;
                SyncSlider(slBright, st, Vcp.Brightness);
                SyncSlider(slVol, st, Vcp.Volume);
                if (btnMute != null)
                {
                    bool muted = st.Cur(Vcp.Mute) == 1;
                    btnMute.Text = muted ? Glyphs.Mute : Glyphs.Volume;
                    btnMute.TextColor = muted ? Theme.Warn : (Color?)null;
                    tips.SetToolTip(btnMute, muted ? "Riattiva audio" : "Disattiva audio");
                }
                foreach (KeyValuePair<byte, Slider> kv in dSliders) SyncSlider(kv.Value, st, kv.Key);
                foreach (KeyValuePair<byte, DropButton> kv in dDrops)
                {
                    int v = st.Cur(kv.Key);
                    kv.Value.Text = v >= 0 ? app.ChoiceName(st, kv.Key, (uint)v) : "–";
                }
            }
            if (btnTop != null)
            {
                btnTop.Selected = app.Cfg.TopMost;
                btnTop.Text = app.Cfg.TopMost ? Glyphs.Pin : Glyphs.Unpin;
            }
            if (btnExpand != null) btnExpand.Glyph = app.Expanded ? Glyphs.Prev : Glyphs.Next;
            if (TopMost != app.Cfg.TopMost) TopMost = app.Cfg.TopMost;
            updating = false;
            left.Invalidate(true);
            if (app.Expanded) details.Invalidate();
        }

        void SyncSlider(Slider sl, MonitorState st, byte code)
        {
            if (sl == null || sl.Dragging || st.Recent(code)) return;
            sl.Maximum = st.Max(code);
            int v = st.Cur(code);
            if (v >= 0) sl.Value = v;
        }

        void ApplySize()
        {
            int h = compactH;
            if (app.Expanded)
            {
                int want = S(56) + details.Height + S(14) + 2;
                int maxH = S(640);
                if (IsHandleCreated) maxH = Math.Min(maxH, Screen.FromHandle(Handle).WorkingArea.Height - S(40));
                h = Math.Max(compactH, Math.Min(want, maxH));
            }
            int w = app.Expanded ? CompactW + DetailsW : CompactW;

            left.Bounds = new Rectangle(1, 1, CompactW - 1, h - 2);
            if (statusItem != null)
            {
                int sy = h - 2 - S(32);
                statusItem.R = new Rectangle(statusItem.R.X, sy, statusItem.R.Width, statusItem.R.Height);
                versionItem.R = new Rectangle(versionItem.R.X, sy, versionItem.R.Width, versionItem.R.Height);
            }
            right.Visible = app.Expanded;
            right.Bounds = new Rectangle(CompactW + 1, 1, DetailsW - 2, h - 2);
            scroll.Bounds = new Rectangle(S(12), S(56), right.Width - S(12) - S(4), right.Height - S(56) - S(10));

            if (ClientSize.Width != w || ClientSize.Height != h)
            {
                ClientSize = new Size(w, h);
                EnsureOnScreen();
            }
        }

        void EnsureOnScreen()
        {
            if (!Visible) return;
            Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
            int x = Left, y = Top;
            if (x + Width > wa.Right) x = Math.Max(wa.Left, wa.Right - Width);
            if (y + Height > wa.Bottom) y = Math.Max(wa.Top, wa.Bottom - Height);
            if (x != Left || y != Top) Location = new Point(x, y);
        }

        // ------------------------------------------------------------ colonna compatta

        PillButton IconBtn(Control parent, string glyph, string tip, int x, int y)
        {
            PillButton b = new PillButton();
            b.Ghost = true;
            b.Font = fIcon;
            b.Text = glyph;
            b.Bounds = new Rectangle(x, y, S(32), S(32));
            tips.SetToolTip(b, tip);
            parent.Controls.Add(b);
            return b;
        }

        void Section(PaintedPanel p, string text, int x, int y)
        {
            p.AddText(new Rectangle(x, y, S(220), S(16)), text, fSection, Theme.Muted, TextFormatFlags.Left);
        }

        void BuildLeft()
        {
            left.Reset();
            inputBtns.Clear();
            slBright = slVol = null;
            btnMute = null;
            btnExpand = null;
            banner = null;

            MonitorState st = app.Current;
            int W = CompactW - 1, pad = S(18), gap = S(8);
            left.Width = W;

            // Intestazione
            left.AddText(new Rectangle(pad - S(1), S(15), S(170), S(26)), AppInfo.Name, fTitle, Theme.Text, TextFormatFlags.Left);
            int bx = W - S(10) - S(32), by = S(11);
            PillButton bClose = IconBtn(left, Glyphs.Close, "Chiudi", bx, by);
            bClose.Click += delegate { app.CloseWindow(); };
            bx -= S(34);
            PillButton bMin = IconBtn(left, Glyphs.Minimize, "Riduci a icona", bx, by);
            bMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            bx -= S(34);
            PillButton bMore = IconBtn(left, Glyphs.More, "Menu", bx, by);
            bMore.Click += delegate { moreMenu.Show(bMore, new Point(0, bMore.Height + S(2))); };
            bx -= S(34);
            btnTop = IconBtn(left, Glyphs.Unpin, "Sempre in primo piano", bx, by);
            btnTop.Click += delegate { app.SetTopMost(!app.Cfg.TopMost); };

            int y = S(56);
            card = new MonitorCard(this, app);
            card.Bounds = new Rectangle(pad, y, W - 2 * pad, S(62));
            left.Controls.Add(card);
            y += S(62) + S(20);

            if (st == null)
            {
                left.AddText(new Rectangle(pad, y, W - 2 * pad, S(56)),
                    "Nessun monitor compatibile trovato. Controlla che DDC/CI sia attivo nel menu del monitor, poi aggiorna l'elenco.",
                    fRow, Theme.Muted, TextFormatFlags.WordBreak);
                y += S(60);
                PillButton r = new PillButton();
                r.Font = fButton;
                r.Text = "Aggiorna elenco";
                r.Bounds = new Rectangle(pad, y, S(150), S(36));
                r.Click += delegate { app.RefreshMonitors(); };
                left.Controls.Add(r);
                y += S(36) + S(20);
            }
            else if (!st.MainRead)
            {
                left.AddText(new Rectangle(pad, y, W - 2 * pad, S(20)), "Lettura del monitor…", fRow, Theme.Muted, TextFormatFlags.Left);
                y += S(20) + S(20);
            }
            else
            {
                bool any = false;
                List<KeyValuePair<string, uint>> inputs = app.InputsFor(st);
                if (st.Supports(Vcp.Input) && inputs.Count > 0)
                {
                    any = true;
                    Section(left, "INGRESSO", pad, y);
                    y += S(22);
                    int n = inputs.Count;
                    int cols = n <= 3 ? n : (n == 4 ? 2 : 3);
                    int bw = (W - 2 * pad - gap * (cols - 1)) / cols;
                    for (int i = 0; i < n; i++)
                    {
                        int r = i / cols, c = i % cols;
                        PillButton b = new PillButton();
                        b.Font = fButton;
                        b.Text = inputs[i].Key;
                        b.Tag2 = inputs[i].Value;
                        b.Bounds = new Rectangle(pad + c * (bw + gap), y + r * (S(38) + gap), bw, S(38));
                        b.Click += delegate(object sender, EventArgs e) { app.SetChoice(Vcp.Input, (uint)((PillButton)sender).Tag2, false); };
                        left.Controls.Add(b);
                        inputBtns.Add(b);
                    }
                    int rows = (n + cols - 1) / cols;
                    y += rows * S(38) + (rows - 1) * gap + S(20);
                }
                if (st.Supports(Vcp.Brightness))
                {
                    any = true;
                    y = SliderRow("LUMINOSITÀ", Glyphs.Brightness, Vcp.Brightness, y, false, out slBright);
                }
                if (st.Supports(Vcp.Volume))
                {
                    any = true;
                    y = SliderRow("VOLUME", Glyphs.Volume, Vcp.Volume, y, st.Supports(Vcp.Mute), out slVol);
                }
                if (!any)
                {
                    left.AddText(new Rectangle(pad, y, W - 2 * pad, S(40)),
                        "Il monitor non risponde ai comandi principali. Prova \"Tutti i controlli\".",
                        fRow, Theme.Muted, TextFormatFlags.WordBreak);
                    y += S(40) + S(16);
                }
            }

            if (st != null)
            {
                btnExpand = new DropButton();
                btnExpand.Font = fButton;
                btnExpand.GlyphFont = fIcon;
                btnExpand.Text = "Tutti i controlli";
                btnExpand.Bounds = new Rectangle(pad, y, W - 2 * pad, S(38));
                btnExpand.Click += delegate { app.SetExpanded(!app.Expanded); };
                left.Controls.Add(btnExpand);
                y += S(38) + S(14);
            }

            if (app.Update != null)
            {
                banner = new Banner(this, app);
                banner.Bounds = new Rectangle(pad, y, W - 2 * pad, S(44));
                left.Controls.Add(banner);
                y += S(44) + S(12);
            }

            statusItem = left.AddText(new Rectangle(pad, y, W - 2 * pad - S(50), S(18)),
                delegate { return app.Status; }, fStatus,
                delegate { return app.StatusIsError ? Theme.Warn : Theme.Muted; },
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            versionItem = left.AddText(new Rectangle(W - pad - S(50), y, S(50), S(18)), "v" + AppInfo.Version, fStatus,
                Color.FromArgb(100, 100, 110), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            y += S(18) + S(14);
            compactH = y + 2;
        }

        int SliderRow(string label, string glyph, byte code, int y, bool muteButton, out Slider result)
        {
            int W = CompactW - 1, pad = S(18);
            Slider sl = new Slider();
            Section(left, label, pad, y);
            left.AddText(new Rectangle(W - pad - S(60), y - S(2), S(60), S(18)),
                delegate { return app.Current != null && app.Current.Cur(code) >= 0 ? sl.Value.ToString() : "–"; },
                fValue, delegate { return Theme.Text; }, TextFormatFlags.Right);
            y += S(20);
            if (muteButton)
            {
                btnMute = IconBtn(left, glyph, "Disattiva audio", pad - S(8), y - S(1));
                btnMute.Font = fIconBig;
                btnMute.Click += delegate { app.ToggleMute(); };
            }
            else
            {
                left.AddText(new Rectangle(pad - S(8), y, S(32), S(30)), glyph, fIconBig, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            sl.Bounds = new Rectangle(pad + S(26), y, W - 2 * pad - S(26) + S(6), S(30));
            sl.ValueChanged += delegate
            {
                if (updating) return;
                app.SetContinuous(code, sl.Value, false);
                left.Invalidate();
            };
            left.Controls.Add(sl);
            result = sl;
            return y + S(30) + S(16);
        }

        // ------------------------------------------------------------ pannello "Tutti i controlli"

        void BuildRightHeader()
        {
            int W = DetailsW - 2;
            right.AddText(new Rectangle(S(18), S(15), S(220), S(26)), "Tutti i controlli", fName, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            PillButton bClose = IconBtn(right, Glyphs.Prev, "Riduci", W - S(10) - S(32), S(11));
            bClose.Click += delegate { app.SetExpanded(false); };
            PillButton bRefresh = IconBtn(right, Glyphs.Refresh, "Rileggi i valori dal monitor", W - S(10) - S(32) - S(34), S(11));
            bRefresh.Click += delegate { app.ReloadCurrent(); };
        }

        List<KeyValuePair<string, List<VcpDef>>> PlanDetails(MonitorState st)
        {
            List<KeyValuePair<string, List<VcpDef>>> plan = new List<KeyValuePair<string, List<VcpDef>>>();
            foreach (string group in Vcp.Groups)
            {
                List<VcpDef> rows = new List<VcpDef>();
                foreach (VcpDef d in Vcp.Ordered)
                    if (d.Group == group && !Vcp.IsMain(d.Code) && Show(st, d)) rows.Add(d);
                if (rows.Count > 0) plan.Add(new KeyValuePair<string, List<VcpDef>>(group, rows));
            }
            List<VcpDef> mf = new List<VcpDef>();
            if (st.CapsOk)
            {
                foreach (byte c in st.Codes)
                {
                    if (!Vcp.IsManufacturer(c)) continue;
                    VcpDef d = new VcpDef();
                    d.Code = c;
                    d.Name = "Codice " + Vcp.Hex(c);
                    d.Kind = st.CapValues.ContainsKey(c) && st.CapValues[c].Count > 0 ? VcpKind.Choice : VcpKind.Continuous;
                    if (Show(st, d)) mf.Add(d);
                }
            }
            if (mf.Count > 0) plan.Add(new KeyValuePair<string, List<VcpDef>>("Avanzate · codici del produttore", mf));
            return plan;
        }

        static bool Show(MonitorState st, VcpDef d)
        {
            switch (d.Kind)
            {
                case VcpKind.Continuous: return st.ReadOk(d.Code) && st.Max(d.Code) > 0 && (!st.CapsOk || st.InCaps(d.Code) || st.ReadOk(d.Code));
                case VcpKind.Choice: return st.InCaps(d.Code) || st.ReadOk(d.Code);
                case VcpKind.Action: return st.InCaps(d.Code);
                case VcpKind.Info: return st.ReadOk(d.Code);
            }
            return false;
        }

        string DetailsSignature()
        {
            MonitorState st = app.Current;
            if (st == null) return "none";
            StringBuilder sb = new StringBuilder();
            sb.Append(st.Key).Append('|').Append(st.DetailsRead).Append('|');
            if (st.DetailsRead)
                foreach (KeyValuePair<string, List<VcpDef>> g in PlanDetails(st))
                {
                    sb.Append(g.Key).Append(':');
                    foreach (VcpDef d in g.Value) sb.Append(d.Code).Append(d.Kind).Append(st.Max(d.Code)).Append(',');
                }
            return sb.ToString();
        }

        void BuildDetails()
        {
            details.Reset();
            dSliders.Clear();
            dDrops.Clear();
            int cw = DetailsW - 2 - S(12) - S(4) - S(10);
            details.Width = cw;
            int L = S(6), y = S(2);
            MonitorState st = app.Current;

            if (st == null || !st.DetailsRead)
            {
                details.AddText(new Rectangle(L, y, cw - L, S(40)),
                    st == null ? "Nessun monitor selezionato." : "Lettura dei controlli disponibili…\nCi vuole qualche secondo.",
                    fRow, Theme.Muted, TextFormatFlags.WordBreak);
                details.Height = y + S(44);
                return;
            }

            List<KeyValuePair<string, List<VcpDef>>> plan = PlanDetails(st);
            if (plan.Count == 0)
            {
                details.AddText(new Rectangle(L, y, cw - L, S(40)), "Questo monitor non espone altri controlli via DDC/CI.", fRow, Theme.Muted, TextFormatFlags.WordBreak);
                details.Height = y + S(44);
                return;
            }

            foreach (KeyValuePair<string, List<VcpDef>> g in plan)
            {
                string title = g.Key.ToUpperInvariant();
                int tw = TextRenderer.MeasureText(title, fSection, Size.Empty, TextFormatFlags.NoPadding).Width;
                int gy = y;
                details.AddText(new Rectangle(L, gy, cw - L, S(16)), title, fSection, Theme.Muted, TextFormatFlags.Left);
                details.AddDraw(new Rectangle(L + tw + S(10), gy + S(7), cw - L - tw - S(10), 2), delegate(Graphics gr, Rectangle r)
                {
                    using (Pen p = new Pen(Theme.Track)) gr.DrawLine(p, r.Left, r.Top, r.Right, r.Top);
                });
                y += S(26);
                foreach (VcpDef d in g.Value) y = DetailRow(st, d, y, cw, L);
                y += S(12);
            }
            details.Height = y;
        }

        int DetailRow(MonitorState st, VcpDef d, int y, int cw, int L)
        {
            byte code = d.Code;
            switch (d.Kind)
            {
                case VcpKind.Continuous:
                {
                    Slider sl = new Slider();
                    sl.WheelAdjusts = false;
                    details.AddText(new Rectangle(L, y, cw - L - S(60), S(18)), d.Name, fRow, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    details.AddText(new Rectangle(cw - S(60) - S(4), y, S(60), S(18)), delegate { return sl.Value.ToString(); }, fValue,
                        delegate { return Theme.Text; }, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    y += S(20);
                    sl.Bounds = new Rectangle(0, y, cw, S(28));
                    sl.ValueChanged += delegate
                    {
                        if (updating) return;
                        app.SetContinuous(code, sl.Value, false);
                        details.Invalidate();
                    };
                    details.Controls.Add(sl);
                    dSliders[code] = sl;
                    return y + S(28) + S(10);
                }
                case VcpKind.Choice:
                {
                    details.AddText(new Rectangle(L, y, cw - L - S(180), S(34)), d.Name, fRow, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    DropButton db = new DropButton();
                    db.Font = fRow;
                    db.GlyphFont = fIcon;
                    db.Bounds = new Rectangle(cw - S(176) - S(4), y, S(176), S(34));
                    db.Click += delegate { ShowChoiceMenu(db, code); };
                    details.Controls.Add(db);
                    dDrops[code] = db;
                    return y + S(34) + S(8);
                }
                case VcpKind.Action:
                {
                    details.AddText(new Rectangle(L, y, cw - L - S(140), S(34)), d.Name, fRow, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                    PillButton pb = new PillButton();
                    pb.Font = fButton;
                    pb.Text = d.ActionText;
                    if (d.Danger) pb.TextColor = Theme.Danger;
                    pb.Bounds = new Rectangle(cw - S(124) - S(4), y, S(124), S(34));
                    pb.Click += delegate { app.RunAction(d); };
                    details.Controls.Add(pb);
                    return y + S(34) + S(8);
                }
                case VcpKind.Info:
                {
                    details.AddText(new Rectangle(L, y, cw / 2, S(22)), d.Name, fRow, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    details.AddText(new Rectangle(cw / 2, y, cw / 2 - S(4), S(22)),
                        delegate { MonitorState cur = app.Current; return cur != null ? Vcp.FormatInfo(code, cur.Reading(code)) : "–"; },
                        fValue, delegate { return Theme.Text; }, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    return y + S(24);
                }
            }
            return y;
        }

        void ShowChoiceMenu(DropButton db, byte code)
        {
            MonitorState st = app.Current;
            if (st == null) return;
            ContextMenuStrip m = Theme.NewMenu();
            int cur = st.Cur(code);
            foreach (uint v in st.ChoicesFor(code))
            {
                uint val = v;
                ToolStripMenuItem it = new ToolStripMenuItem(app.ChoiceName(st, code, v));
                it.Checked = cur >= 0 && (uint)cur == v;
                it.Click += delegate { app.SetChoice(code, val, false); };
                m.Items.Add(it);
            }
            if (m.Items.Count == 0) { m.Dispose(); return; }
            m.MinimumSize = new Size(db.Width, 0);
            m.Closed += delegate { BeginInvoke((Action)delegate { m.Dispose(); }); };
            m.Show(db, new Point(0, db.Height + S(2)));
        }

        public void ShowMonitorMenu(Control anchor)
        {
            ContextMenuStrip m = Theme.NewMenu();
            app.FillMonitorItems(m.Items);
            m.Closed += delegate { BeginInvoke((Action)delegate { m.Dispose(); }); };
            m.Show(anchor, new Point(0, anchor.Height + S(2)));
        }

        // ------------------------------------------------------------ mostra / nascondi

        public void ShowWindow()
        {
            if (!positioned)
            {
                positioned = true;
                Sync();
                Rectangle r = new Rectangle(app.Cfg.X, app.Cfg.Y, Width, Height);
                bool onScreen = false;
                foreach (Screen sc in Screen.AllScreens) if (sc.WorkingArea.IntersectsWith(r)) onScreen = true;
                if (app.Cfg.X == int.MinValue || !onScreen)
                {
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    r.X = wa.Left + (wa.Width - CompactW) / 2;
                    r.Y = wa.Top + (wa.Height - Height) / 2;
                }
                Location = r.Location;
            }
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
            EnsureOnScreen();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            LastDeactivate = DateTime.Now;
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            app.SavePosition();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !app.Exiting)
            {
                e.Cancel = true;
                app.CloseWindow();
            }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) app.ReloadCurrent();
            else if (e.Control && e.KeyCode == Keys.Tab) app.SelectMonitor(app.CurrentIndex + (e.Shift ? -1 : 1), true);
        }
    }

    // Scheda con il monitor selezionato e le frecce per passare agli altri
    class MonitorCard : Control
    {
        readonly MainForm f;
        readonly DeckApp app;
        readonly PillButton prev, next;
        bool hover;

        public MonitorCard(MainForm f, DeckApp app)
        {
            this.f = f;
            this.app = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            prev = Arrow(Glyphs.Prev, "Monitor precedente");
            next = Arrow(Glyphs.Next, "Monitor successivo");
            prev.Click += delegate { app.SelectMonitor(app.CurrentIndex - 1, true); };
            next.Click += delegate { app.SelectMonitor(app.CurrentIndex + 1, true); };
            bool many = app.Monitors.Count > 1;
            prev.Visible = next.Visible = many;
        }

        PillButton Arrow(string glyph, string tip)
        {
            PillButton b = new PillButton();
            b.Ghost = true;
            b.Font = f.fIcon;
            b.Text = glyph;
            new ToolTip().SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int sz = f.S(34);
            prev.Bounds = new Rectangle(f.S(8), (Height - sz) / 2, sz, sz);
            next.Bounds = new Rectangle(Width - f.S(8) - sz, (Height - sz) / 2, sz, sz);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) f.ShowMonitorMenu(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = f.S(12);
            Theme.FillRound(g, r, rad, Theme.Card);
            if (hover)
                using (GraphicsPath p = Theme.Round(r, rad))
                using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);

            MonitorState st = app.Current;
            string name, sub;
            if (st == null) { name = "Nessun monitor DDC/CI"; sub = "Clicca per aggiornare l'elenco"; }
            else
            {
                name = st.Title;
                sub = st.Entry.Bounds.Width + "×" + st.Entry.Bounds.Height;
                if (st.Entry.Primary) sub += "  ·  principale";
                if (app.Monitors.Count > 1) sub += "  ·  " + (app.CurrentIndex + 1) + " di " + app.Monitors.Count;
            }
            int side = f.S(48);
            TextRenderer.DrawText(g, name, f.fName, new Rectangle(side, f.S(10), Width - 2 * side, f.S(24)), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, sub, f.fSub, new Rectangle(side, f.S(34), Width - 2 * side, f.S(18)), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    // Avviso "nuova versione disponibile"
    class Banner : Control
    {
        readonly MainForm f;
        readonly DeckApp app;
        bool hover;

        public Banner(MainForm f, DeckApp app)
        {
            this.f = f;
            this.app = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (!app.UpdateBusy) app.PromptUpdate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), f.S(12), hover ? Theme.BannerHover : Theme.Banner);
            int pad = f.S(14);
            TextRenderer.DrawText(g, Glyphs.Download, f.fIcon, new Rectangle(pad - f.S(2), 0, f.S(20), Height), Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            string text = app.UpdateBusy && app.UpdateText != null ? app.UpdateText
                : (app.Update != null ? "Versione " + app.Update.VersionText + " disponibile" : "");
            TextRenderer.DrawText(g, text, f.fRow, new Rectangle(pad + f.S(26), 0, Width - pad * 2 - f.S(110), Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (!app.UpdateBusy)
                TextRenderer.DrawText(g, "Aggiorna", f.fButton, new Rectangle(Width - pad - f.S(90), 0, f.S(90), Height), Theme.Accent,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
