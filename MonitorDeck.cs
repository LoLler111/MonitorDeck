// Monitor Deck - controllo DDC/CI per monitor (ingresso, luminosita', contrasto, profili)
// Compatibile con il compilatore C# 5 incluso in Windows (.NET Framework 4.x).
// Compila con build.bat

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Monitor Deck")]
[assembly: System.Reflection.AssemblyProduct("Monitor Deck")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace MonitorDeck
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex m = new Mutex(true, "MonitorDeck_SingleInstance_v1", out created))
            {
                if (!created) return;
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    // ------------------------------------------------------------------ Win32

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint size, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool DestroyPhysicalMonitors(uint size, PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, out uint type, out uint current, out uint maximum);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 2;

        public static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int pref = 2; // DWMWCP_ROUND (Windows 11)
                DwmSetWindowAttribute(hwnd, 33, ref pref, 4);
                int dark = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ DDC/CI

    class MonitorEntry
    {
        public IntPtr Handle;
        public string Name;
        public string Device;
        public Rectangle Bounds;
        public override string ToString() { return Name; }
    }

    static class Ddc
    {
        static readonly List<Native.PHYSICAL_MONITOR[]> held = new List<Native.PHYSICAL_MONITOR[]>();

        // Da chiamare sempre dal thread DDC.
        public static List<MonitorEntry> Enumerate()
        {
            foreach (Native.PHYSICAL_MONITOR[] arr in held)
            {
                try { Native.DestroyPhysicalMonitors((uint)arr.Length, arr); } catch { }
            }
            held.Clear();

            List<IntPtr> hmons = new List<IntPtr>();
            Native.MonitorEnumProc proc = delegate(IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d) { hmons.Add(h); return true; };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);

            List<MonitorEntry> result = new List<MonitorEntry>();
            int n = 0;
            foreach (IntPtr hmon in hmons)
            {
                n++;
                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                Native.GetMonitorInfo(hmon, ref mi);

                string friendly = null;
                try
                {
                    Native.DISPLAY_DEVICE dd = new Native.DISPLAY_DEVICE();
                    dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
                    if (Native.EnumDisplayDevices(mi.szDevice, 0, ref dd, 0)) friendly = dd.DeviceString;
                }
                catch { }

                uint count;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out count) || count == 0) continue;
                Native.PHYSICAL_MONITOR[] arr = new Native.PHYSICAL_MONITOR[count];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hmon, count, arr)) continue;
                held.Add(arr);

                foreach (Native.PHYSICAL_MONITOR pm in arr)
                {
                    MonitorEntry e = new MonitorEntry();
                    e.Handle = pm.hPhysicalMonitor;
                    e.Device = mi.szDevice;
                    e.Bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                    string baseName = !string.IsNullOrEmpty(friendly) && friendly.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) < 0
                        ? friendly : ("Schermo " + n);
                    e.Name = baseName + "  ·  " + e.Bounds.Width + "×" + e.Bounds.Height;
                    result.Add(e);
                }
            }
            return result;
        }

        public static bool Get(IntPtr h, byte code, out uint cur, out uint max)
        {
            uint type;
            cur = 0; max = 0;
            if (h == IntPtr.Zero) return false;
            for (int i = 0; i < 2; i++)
            {
                try { if (Native.GetVCPFeatureAndVCPFeatureReply(h, code, out type, out cur, out max)) return true; }
                catch { return false; }
                Thread.Sleep(40);
            }
            return false;
        }

        public static bool Set(IntPtr h, byte code, uint value)
        {
            if (h == IntPtr.Zero) return false;
            for (int i = 0; i < 2; i++)
            {
                try { if (Native.SetVCPFeature(h, code, value)) return true; }
                catch { return false; }
                Thread.Sleep(40);
            }
            return false;
        }
    }

    // Tutte le chiamate DDC passano da un unico thread: non bloccano mai l'interfaccia.
    class DdcWorker
    {
        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();

        public DdcWorker()
        {
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Name = "DDC";
            t.Start();
        }

        void Run()
        {
            foreach (Action a in queue.GetConsumingEnumerable())
            {
                try { a(); } catch { }
            }
        }

        public void Post(Action a) { queue.Add(a); }
    }

    // ------------------------------------------------------------------ Config (file .ini accanto all'exe)

    class Config
    {
        public string Path;
        public int MonitorIndex = 0;
        public bool Pinned = false;
        public bool TopMost = true;
        public int X = int.MinValue, Y = int.MinValue;
        public List<KeyValuePair<string, uint>> Inputs = new List<KeyValuePair<string, uint>>();
        public List<KeyValuePair<uint, string>> Presets = new List<KeyValuePair<uint, string>>();
        public List<KeyValuePair<string, string>> Hotkeys = new List<KeyValuePair<string, string>>();

        const string DefaultIni =
"; Monitor Deck - impostazioni\r\n" +
"; Dopo una modifica salva il file e scegli \"Ricarica impostazioni\" dal menu dell'icona.\r\n" +
"\r\n" +
"[Generale]\r\n" +
"Monitor=0\r\n" +
"FinestraFissa=0\r\n" +
"SempreInPrimoPiano=1\r\n" +
"X=\r\n" +
"Y=\r\n" +
"\r\n" +
"[Ingressi]\r\n" +
"; Nome mostrato = valore VCP 60\r\n" +
"DisplayPort=15\r\n" +
"HDMI 1=17\r\n" +
"HDMI 2=19\r\n" +
"\r\n" +
"[Profili]\r\n" +
"; valore VCP 14 = nome mostrato (rinominali come vuoi dopo averli provati)\r\n" +
"1=sRGB\r\n" +
"5=6500K\r\n" +
"8=9300K\r\n" +
"11=Utente 1\r\n" +
"12=Utente 2\r\n" +
"\r\n" +
"[Scorciatoie]\r\n" +
"; Tasti = azione. Modificatori: Ctrl, Alt, Shift, Win\r\n" +
"; Azioni: ingresso:15   luminosita:+10   luminosita:-10   luminosita:30\r\n" +
";         contrasto:+5   contrasto:60   profilo:5   pannello\r\n" +
"Ctrl+Alt+1=ingresso:15\r\n" +
"Ctrl+Alt+2=ingresso:17\r\n" +
"Ctrl+Alt+3=ingresso:19\r\n" +
"Ctrl+Alt+Up=luminosita:+10\r\n" +
"Ctrl+Alt+Down=luminosita:-10\r\n" +
"Ctrl+Alt+0=luminosita:0\r\n" +
"Ctrl+Alt+M=pannello\r\n";

        public static Config Load()
        {
            Config c = new Config();
            string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            c.Path = System.IO.Path.Combine(dir, "MonitorDeck.ini");
            try { if (!File.Exists(c.Path)) File.WriteAllText(c.Path, DefaultIni, Encoding.UTF8); } catch { }

            string[] lines;
            try { lines = File.ReadAllLines(c.Path, Encoding.UTF8); }
            catch { lines = DefaultIni.Split(new string[] { "\r\n" }, StringSplitOptions.None); }

            string section = "";
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                int iv; uint uv;
                switch (section)
                {
                    case "generale":
                        string lk = k.ToLowerInvariant();
                        if (lk == "monitor" && int.TryParse(v, out iv)) c.MonitorIndex = iv;
                        else if (lk == "finestrafissa") c.Pinned = v == "1";
                        else if (lk == "sempreinprimopiano") c.TopMost = v == "1";
                        else if (lk == "x" && int.TryParse(v, out iv)) c.X = iv;
                        else if (lk == "y" && int.TryParse(v, out iv)) c.Y = iv;
                        break;
                    case "ingressi":
                        if (uint.TryParse(v, out uv)) c.Inputs.Add(new KeyValuePair<string, uint>(k, uv));
                        break;
                    case "profili":
                        if (uint.TryParse(k, out uv)) c.Presets.Add(new KeyValuePair<uint, string>(uv, v));
                        break;
                    case "scorciatoie":
                        c.Hotkeys.Add(new KeyValuePair<string, string>(k, v));
                        break;
                }
            }
            if (c.Inputs.Count == 0)
            {
                c.Inputs.Add(new KeyValuePair<string, uint>("DisplayPort", 15));
                c.Inputs.Add(new KeyValuePair<string, uint>("HDMI 1", 17));
                c.Inputs.Add(new KeyValuePair<string, uint>("HDMI 2", 19));
            }
            return c;
        }

        // Aggiorna solo la sezione [Generale] lasciando intatto il resto (commenti compresi).
        public void SaveGeneral()
        {
            Dictionary<string, string> vals = new Dictionary<string, string>();
            vals["Monitor"] = MonitorIndex.ToString(CultureInfo.InvariantCulture);
            vals["FinestraFissa"] = Pinned ? "1" : "0";
            vals["SempreInPrimoPiano"] = TopMost ? "1" : "0";
            vals["X"] = X == int.MinValue ? "" : X.ToString(CultureInfo.InvariantCulture);
            vals["Y"] = Y == int.MinValue ? "" : Y.ToString(CultureInfo.InvariantCulture);
            try
            {
                List<string> lines = File.Exists(Path) ? new List<string>(File.ReadAllLines(Path, Encoding.UTF8)) : new List<string>();
                int start = -1, end = lines.Count;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("[") && t.EndsWith("]"))
                    {
                        if (start >= 0) { end = i; break; }
                        if (t.Equals("[Generale]", StringComparison.OrdinalIgnoreCase)) start = i;
                    }
                }
                if (start < 0)
                {
                    lines.Insert(0, "");
                    lines.Insert(0, "[Generale]");
                    start = 0; end = 1;
                }
                HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = start + 1; i < end; i++)
                {
                    string t = lines[i].Trim();
                    int eq = t.IndexOf('=');
                    if (eq <= 0 || t.StartsWith(";")) continue;
                    string k = t.Substring(0, eq).Trim();
                    foreach (KeyValuePair<string, string> kv in vals)
                    {
                        if (kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase)) { lines[i] = kv.Key + "=" + kv.Value; done.Add(kv.Key); }
                    }
                }
                int insertAt = start + 1;
                foreach (KeyValuePair<string, string> kv in vals)
                {
                    if (!done.Contains(kv.Key)) { lines.Insert(insertAt, kv.Key + "=" + kv.Value); insertAt++; }
                }
                File.WriteAllLines(Path, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public string InputName(uint v)
        {
            foreach (KeyValuePair<string, uint> kv in Inputs) if (kv.Value == v) return kv.Key;
            return "Ingresso " + v;
        }

        public string PresetName(uint v)
        {
            foreach (KeyValuePair<uint, string> kv in Presets) if (kv.Key == v) return kv.Value;
            return "Profilo " + v;
        }
    }

    // ------------------------------------------------------------------ Tema e controlli disegnati a mano

    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(24, 24, 27);
        public static readonly Color Border = Color.FromArgb(58, 58, 64);
        public static readonly Color Card = Color.FromArgb(40, 40, 45);
        public static readonly Color CardHover = Color.FromArgb(52, 52, 58);
        public static readonly Color CardDown = Color.FromArgb(34, 34, 38);
        public static readonly Color Accent = Color.FromArgb(0, 210, 200);
        public static readonly Color AccentText = Color.FromArgb(8, 24, 24);
        public static readonly Color Text = Color.FromArgb(240, 240, 242);
        public static readonly Color Muted = Color.FromArgb(150, 150, 160);
        public static readonly Color Track = Color.FromArgb(56, 56, 62);
        public static readonly Color Warn = Color.FromArgb(255, 180, 90);

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

        public static string IconFontName()
        {
            using (Font f = new Font("Segoe Fluent Icons", 10f))
                if (f.Name == "Segoe Fluent Icons") return f.Name;
            return "Segoe MDL2 Assets";
        }
    }

    class PillButton : Control
    {
        bool selected, hover, down;
        public bool Ghost;      // nessuno sfondo finche' non ci passi sopra (pulsanti icona)
        public object Tag2;

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
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
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill;
            if (selected) fill = Theme.Accent;
            else if (down) fill = Theme.CardDown;
            else if (hover) fill = Theme.CardHover;
            else fill = Ghost ? Color.Empty : Theme.Card;
            if (fill != Color.Empty)
            {
                using (GraphicsPath p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height * 0.3f))
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillPath(b, p);
            }
            Color tc = selected ? Theme.AccentText : (Ghost && !hover ? Theme.Muted : ForeColor);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, tc,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    class Slider : Control
    {
        int min = 0, max = 100, val = 50;
        bool drag, hover;
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
        protected override void OnMouseWheel(MouseEventArgs e) { SetUser(val + (e.Delta > 0 ? 2 : -2)); base.OnMouseWheel(e); }

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

            using (GraphicsPath p = Theme.Round(new RectangleF(pad, cy - trackH / 2, w, trackH), trackH / 2))
            using (SolidBrush b = new SolidBrush(Theme.Track)) g.FillPath(b, p);
            if (tx - pad > 1)
            {
                using (GraphicsPath p = Theme.Round(new RectangleF(pad, cy - trackH / 2, tx - pad, trackH), trackH / 2))
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
            }
            float r = Height * ((drag || hover) ? 0.34f : 0.30f);
            using (SolidBrush b = new SolidBrush(Theme.Text)) g.FillEllipse(b, tx - r, cy - r, 2 * r, 2 * r);
            float ri = r * 0.45f;
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, tx - ri, cy - ri, 2 * ri, 2 * ri);
        }
    }

    // ------------------------------------------------------------------ Pannello principale (popup o finestra fissa)

    class DeckForm : Form
    {
        readonly TrayApp app;
        readonly float s;
        readonly Font fTitle, fSub, fSection, fValue, fButton, fIcon, fStatus;
        readonly List<PillButton> inputBtns = new List<PillButton>();
        readonly List<PillButton> presetBtns = new List<PillButton>();
        Slider slBright, slContrast;
        PillButton btnPin, btnTop, btnClose, btnRefresh;
        Rectangle rSub, rBrightVal, rContrastVal, rStatus;
        int yInputLbl, yBrightLbl, yContrastLbl, yPresetLbl;
        bool updating;
        public DateTime LastHidden = DateTime.MinValue;

        public DeckForm(TrayApp app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            KeyPreview = true;
            Text = "Monitor Deck";
            DoubleBuffered = true;
            try { Icon = app.AppIcon; } catch { }

            using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;

            fTitle = new Font("Segoe UI Semibold", 12.5f);
            fSub = new Font("Segoe UI", 8.5f);
            fSection = new Font("Segoe UI Semibold", 7.75f);
            fValue = new Font("Segoe UI Semibold", 9f);
            fButton = new Font("Segoe UI Semibold", 9.5f);
            fStatus = new Font("Segoe UI", 8f);
            fIcon = new Font(Theme.IconFontName(), 10f);

            BuildControls();
        }

        int S(float v) { return (int)Math.Round(v * s); }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        PillButton IconButton(string glyph, string tip)
        {
            PillButton b = new PillButton();
            b.Ghost = true;
            b.Font = fIcon;
            b.Text = glyph;
            b.Size = new Size(S(32), S(32));
            new ToolTip().SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        void BuildControls()
        {
            SuspendLayout();
            Controls.Clear();
            inputBtns.Clear();
            presetBtns.Clear();

            int W = S(344), pad = S(18), gap = S(8);
            int y = S(14);

            btnClose = IconButton("", "Chiudi");
            btnPin = IconButton("", "Finestra fissa");
            btnTop = IconButton("", "Sempre in primo piano");
            btnRefresh = IconButton("", "Aggiorna valori");
            btnClose.Location = new Point(W - pad - S(32) + S(6), y);
            btnPin.Location = new Point(btnClose.Left - S(34), y);
            btnTop.Location = new Point(btnPin.Left - S(34), y);
            btnRefresh.Location = new Point(btnTop.Left - S(34), y);
            btnClose.Click += delegate { HidePanel(); };
            btnPin.Click += delegate { app.SetPinned(!app.Cfg.Pinned); };
            btnTop.Click += delegate { app.SetTopMost(!app.Cfg.TopMost); };
            btnRefresh.Click += delegate { app.RefreshMonitors(); };

            rSub = new Rectangle(pad, y + S(28), W - 2 * pad - S(140), S(18));
            y += S(66);

            // Ingressi
            yInputLbl = y; y += S(22);
            int n = app.Cfg.Inputs.Count;
            int bw = (W - 2 * pad - gap * (n - 1)) / Math.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                PillButton b = new PillButton();
                b.Font = fButton;
                b.Text = app.Cfg.Inputs[i].Key;
                b.Tag2 = app.Cfg.Inputs[i].Value;
                b.Bounds = new Rectangle(pad + i * (bw + gap), y, bw, S(42));
                b.Click += delegate(object sender, EventArgs e) { app.SetInput((uint)((PillButton)sender).Tag2, false); };
                Controls.Add(b);
                inputBtns.Add(b);
            }
            y += S(42) + S(20);

            // Luminosita'
            yBrightLbl = y;
            rBrightVal = new Rectangle(W - pad - S(60), y - S(2), S(60), S(18));
            y += S(20);
            slBright = new Slider();
            slBright.Bounds = new Rectangle(pad - S(6), y, W - 2 * pad + S(12), S(30));
            slBright.ValueChanged += delegate { if (!updating) { app.SetBrightness(slBright.Value, false); Invalidate(rBrightVal); } };
            Controls.Add(slBright);
            y += S(30) + S(14);

            // Contrasto
            yContrastLbl = y;
            rContrastVal = new Rectangle(W - pad - S(60), y - S(2), S(60), S(18));
            y += S(20);
            slContrast = new Slider();
            slContrast.Bounds = new Rectangle(pad - S(6), y, W - 2 * pad + S(12), S(30));
            slContrast.ValueChanged += delegate { if (!updating) { app.SetContrast(slContrast.Value, false); Invalidate(rContrastVal); } };
            Controls.Add(slContrast);
            y += S(30) + S(16);

            // Profili
            if (app.Cfg.Presets.Count > 0)
            {
                yPresetLbl = y; y += S(22);
                int cols = 3;
                int pw = (W - 2 * pad - gap * (cols - 1)) / cols;
                for (int i = 0; i < app.Cfg.Presets.Count; i++)
                {
                    int r = i / cols, c = i % cols;
                    PillButton b = new PillButton();
                    b.Font = fButton;
                    b.Text = app.Cfg.Presets[i].Value;
                    b.Tag2 = app.Cfg.Presets[i].Key;
                    b.Bounds = new Rectangle(pad + c * (pw + gap), y + r * (S(36) + gap), pw, S(36));
                    b.Click += delegate(object sender, EventArgs e) { app.SetPreset((uint)((PillButton)sender).Tag2, false); };
                    Controls.Add(b);
                    presetBtns.Add(b);
                }
                int rows = (app.Cfg.Presets.Count + cols - 1) / cols;
                y += rows * S(36) + (rows - 1) * gap + S(16);
            }
            else yPresetLbl = -1;

            rStatus = new Rectangle(pad, y, W - 2 * pad, S(18));
            y += S(18) + S(14);

            ClientSize = new Size(W, y);
            ResumeLayout();
            ApplyState();
        }

        public void Rebuild() { BuildControls(); Invalidate(); }

        public void ApplyState()
        {
            updating = true;
            foreach (PillButton b in inputBtns) b.Selected = app.Input >= 0 && (uint)b.Tag2 == (uint)app.Input;
            foreach (PillButton b in presetBtns) b.Selected = app.Preset >= 0 && (uint)b.Tag2 == (uint)app.Preset;
            bool recent = (DateTime.Now - app.LastUserChange).TotalMilliseconds < 1500;
            if (!slBright.Dragging && !recent)
            {
                slBright.Maximum = app.BrightMax;
                if (app.Brightness >= 0) slBright.Value = app.Brightness;
            }
            if (!slContrast.Dragging && !recent)
            {
                slContrast.Maximum = app.ContrastMax;
                if (app.Contrast >= 0) slContrast.Value = app.Contrast;
            }
            btnPin.Text = app.Cfg.Pinned ? "" : "";
            btnPin.Selected = app.Cfg.Pinned;
            btnTop.Visible = app.Cfg.Pinned;
            btnTop.Selected = app.Cfg.Pinned && app.Cfg.TopMost;
            updating = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            int pad = S(18);
            TextRenderer.DrawText(g, "Monitor Deck", fTitle, new Point(pad - S(2), S(14)), Theme.Text, TextFormatFlags.NoPadding);
            string sub = app.CurrentMonitorName();
            if (app.Monitors.Count > 1) sub += "   ›";
            TextRenderer.DrawText(g, sub, fSub, rSub, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            Section(g, "INGRESSO", yInputLbl);
            Section(g, "LUMINOSITÀ", yBrightLbl);
            Section(g, "CONTRASTO", yContrastLbl);
            if (yPresetLbl >= 0) Section(g, "PROFILO COLORE", yPresetLbl);

            TextRenderer.DrawText(g, app.Brightness >= 0 ? slBright.Value.ToString() : "–", fValue, rBrightVal, Theme.Text, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, app.Contrast >= 0 ? slContrast.Value.ToString() : "–", fValue, rContrastVal, Theme.Text, TextFormatFlags.Right | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, app.Status, fStatus, rStatus, app.StatusIsError ? Theme.Warn : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        void Section(Graphics g, string text, int y)
        {
            TextRenderer.DrawText(g, text, fSection, new Point(S(18), y), Theme.Muted, TextFormatFlags.NoPadding);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (rSub.Contains(e.Location) && app.Monitors.Count > 1) { app.NextMonitor(); return; }
            if (app.Cfg.Pinned && e.Y < S(64))
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = rSub.Contains(e.Location) && app.Monitors.Count > 1 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (app.Cfg.Pinned) { app.Cfg.X = Left; app.Cfg.Y = Top; app.Cfg.SaveGeneral(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && !app.Cfg.Pinned) HidePanel();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!app.Cfg.Pinned) HidePanel();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HidePanel(); }
            base.OnFormClosing(e);
        }

        public void HidePanel()
        {
            if (!Visible) return;
            LastHidden = DateTime.Now;
            Hide();
        }

        public void ShowPanel()
        {
            Cfg_ApplyWindowMode();
            if (app.Cfg.Pinned)
            {
                Rectangle r = new Rectangle(app.Cfg.X, app.Cfg.Y, Width, Height);
                bool onScreen = false;
                foreach (Screen sc in Screen.AllScreens) if (sc.WorkingArea.IntersectsWith(r)) onScreen = true;
                if (app.Cfg.X == int.MinValue || !onScreen)
                {
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    r.X = wa.Left + (wa.Width - Width) / 2;
                    r.Y = wa.Top + (wa.Height - Height) / 2;
                }
                Location = r.Location;
            }
            else
            {
                Point c = Cursor.Position;
                Screen sc = Screen.FromPoint(c);
                Rectangle wa = sc.WorkingArea, b = sc.Bounds;
                int m = S(12);
                int x = Math.Max(wa.Left + m, Math.Min(c.X - Width / 2, wa.Right - Width - m));
                int y;
                if (wa.Bottom < b.Bottom) y = wa.Bottom - Height - m;      // barra in basso
                else if (wa.Top > b.Top) y = wa.Top + m;                    // barra in alto
                else y = Math.Max(wa.Top + m, Math.Min(c.Y - Height - m, wa.Bottom - Height - m));
                if (wa.Right < b.Right) x = wa.Right - Width - m;           // barra a destra
                else if (wa.Left > b.Left) x = wa.Left + m;                 // barra a sinistra
                Location = new Point(x, y);
            }
            Show();
            Activate();
            BringToFront();
        }

        public void Cfg_ApplyWindowMode()
        {
            ShowInTaskbar = app.Cfg.Pinned;
            TopMost = !app.Cfg.Pinned || app.Cfg.TopMost;
        }
    }

    // ------------------------------------------------------------------ OSD per le scorciatoie

    class OsdForm : Form
    {
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
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
                TextRenderer.DrawText(g, text, fText, new Rectangle(pad, (int)(12 * s), Width - 2 * pad, (int)(26 * s)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, value.ToString(), fText, new Rectangle(pad, (int)(12 * s), Width - 2 * pad, (int)(26 * s)), Theme.Accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                float th = 6 * s, ty = 48 * s, w = Width - 2 * pad;
                using (GraphicsPath p = Theme.Round(new RectangleF(pad, ty, w, th), th / 2))
                using (SolidBrush b = new SolidBrush(Theme.Track)) g.FillPath(b, p);
                float fw = w * value / max;
                if (fw > 1)
                    using (GraphicsPath p = Theme.Round(new RectangleF(pad, ty, fw, th), th / 2))
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
            }
            else
            {
                TextRenderer.DrawText(g, text, fText, ClientRectangle, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // ------------------------------------------------------------------ Scorciatoie globali

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

    // ------------------------------------------------------------------ Applicazione (icona nell'area di notifica)

    class TrayApp : ApplicationContext
    {
        public Config Cfg;
        public Icon AppIcon;
        public List<MonitorEntry> Monitors = new List<MonitorEntry>();
        public int Input = -1, Brightness = -1, Contrast = -1, Preset = -1;
        public int BrightMax = 100, ContrastMax = 100;
        public string Status = "Ricerca monitor…";
        public bool StatusIsError;
        public DateTime LastUserChange = DateTime.MinValue;

        readonly NotifyIcon tray;
        readonly DeckForm deck;
        readonly OsdForm osd;
        readonly HotkeyWindow hotkeys;
        readonly DdcWorker worker = new DdcWorker();
        readonly SynchronizationContext ui;
        readonly System.Windows.Forms.Timer pushTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
        readonly Dictionary<int, string> hotkeyActions = new Dictionary<int, string>();
        int pendingBright = -1, pendingContrast = -1;
        ToolStripMenuItem miPinned, miTop, miAutostart, miInputs;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public TrayApp()
        {
            Cfg = Config.Load();
            try { AppIcon = new Icon(Icon.ExtractAssociatedIcon(Application.ExecutablePath), SystemInformation.SmallIconSize); }
            catch { AppIcon = SystemIcons.Application; }

            deck = new DeckForm(this);
            IntPtr force = deck.Handle; // crea l'handle sul thread UI
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            osd = new OsdForm();

            tray = new NotifyIcon();
            tray.Icon = AppIcon;
            tray.Text = "Monitor Deck";
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePanel(); };
            tray.Visible = true;

            hotkeys = new HotkeyWindow();
            hotkeys.Pressed += OnHotkey;
            RegisterHotkeys();

            pushTimer.Interval = 60;
            pushTimer.Tick += delegate { FlushPending(); };
            pushTimer.Start();

            pollTimer.Interval = 4000;
            pollTimer.Tick += delegate
            {
                if (deck.Visible && (DateTime.Now - LastUserChange).TotalMilliseconds > 2500) ReadAll();
            };
            pollTimer.Start();

            RefreshMonitors();
            if (Cfg.Pinned) deck.ShowPanel();
        }

        // ---- menu

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Apri pannello", null, delegate { ShowPanel(); });
            miInputs = new ToolStripMenuItem("Ingresso");
            m.Items.Add(miInputs);
            m.Items.Add(new ToolStripSeparator());
            miPinned = new ToolStripMenuItem("Finestra fissa (secondo schermo)", null, delegate { SetPinned(!Cfg.Pinned); });
            miTop = new ToolStripMenuItem("Sempre in primo piano", null, delegate { SetTopMost(!Cfg.TopMost); });
            miAutostart = new ToolStripMenuItem("Avvia con Windows", null, delegate { SetAutostart(!GetAutostart()); });
            m.Items.Add(miPinned);
            m.Items.Add(miTop);
            m.Items.Add(miAutostart);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Modifica impostazioni…", null, delegate { try { System.Diagnostics.Process.Start("notepad.exe", "\"" + Cfg.Path + "\""); } catch { } });
            m.Items.Add("Ricarica impostazioni", null, delegate { ReloadConfig(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Esci", null, delegate { ExitApp(); });
            m.Opening += delegate
            {
                miPinned.Checked = Cfg.Pinned;
                miTop.Checked = Cfg.TopMost;
                miTop.Enabled = Cfg.Pinned;
                miAutostart.Checked = GetAutostart();
                miInputs.DropDownItems.Clear();
                foreach (KeyValuePair<string, uint> kv in Cfg.Inputs)
                {
                    uint v = kv.Value;
                    ToolStripMenuItem it = new ToolStripMenuItem(kv.Key, null, delegate { SetInput(v, false); });
                    it.Checked = Input >= 0 && (uint)Input == v;
                    miInputs.DropDownItems.Add(it);
                }
            };
            return m;
        }

        void ReloadConfig()
        {
            Config old = Cfg;
            Cfg = Config.Load();
            RegisterHotkeys();
            deck.Rebuild();
            if (Cfg.MonitorIndex != old.MonitorIndex) ReadAll();
        }

        void ExitApp()
        {
            if (deck.Visible && Cfg.Pinned) { Cfg.X = deck.Left; Cfg.Y = deck.Top; Cfg.SaveGeneral(); }
            tray.Visible = false;
            hotkeys.Dispose();
            ExitThread();
        }

        // ---- pannello

        void TogglePanel()
        {
            if (deck.Visible)
            {
                if (Cfg.Pinned) { deck.Activate(); deck.BringToFront(); }
                else deck.HidePanel();
                return;
            }
            if ((DateTime.Now - deck.LastHidden).TotalMilliseconds < 250) return; // click sull'icona che ha appena chiuso il popup
            ShowPanel();
        }

        void ShowPanel()
        {
            deck.ApplyState();
            deck.ShowPanel();
            ReadAll();
        }

        public void SetPinned(bool pinned)
        {
            bool wasVisible = deck.Visible;
            if (!pinned && deck.Visible) { Cfg.X = deck.Left; Cfg.Y = deck.Top; }
            Cfg.Pinned = pinned;
            if (pinned && wasVisible) { Cfg.X = deck.Left; Cfg.Y = deck.Top; }
            Cfg.SaveGeneral();
            deck.Cfg_ApplyWindowMode();
            deck.ApplyState();
            if (pinned) deck.ShowPanel();
            else deck.HidePanel();
        }

        public void SetTopMost(bool top)
        {
            Cfg.TopMost = top;
            Cfg.SaveGeneral();
            deck.Cfg_ApplyWindowMode();
            deck.ApplyState();
        }

        bool GetAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("MonitorDeck") != null;
            }
            catch { return false; }
        }

        void SetAutostart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("MonitorDeck", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("MonitorDeck", false);
                }
            }
            catch { }
        }

        // ---- stato monitor

        public string CurrentMonitorName()
        {
            MonitorEntry m = Current();
            return m != null ? m.Name : "Nessun monitor DDC/CI";
        }

        MonitorEntry Current()
        {
            if (Monitors.Count == 0) return null;
            int i = Math.Max(0, Math.Min(Cfg.MonitorIndex, Monitors.Count - 1));
            return Monitors[i];
        }

        IntPtr CurrentHandle()
        {
            MonitorEntry m = Current();
            return m != null ? m.Handle : IntPtr.Zero;
        }

        public void NextMonitor()
        {
            if (Monitors.Count < 2) return;
            Cfg.MonitorIndex = (Cfg.MonitorIndex + 1) % Monitors.Count;
            Cfg.SaveGeneral();
            Input = Brightness = Contrast = Preset = -1;
            Changed();
            ReadAll();
        }

        void Changed() { deck.ApplyState(); }

        void Ui(Action a) { ui.Post(delegate { a(); }, null); }

        public void RefreshMonitors()
        {
            SetStatus("Ricerca monitor…", false);
            worker.Post(delegate
            {
                List<MonitorEntry> list = Ddc.Enumerate();
                Ui(delegate
                {
                    Monitors = list;
                    if (Cfg.MonitorIndex >= Monitors.Count) Cfg.MonitorIndex = 0;
                    Changed();
                    ReadAll();
                });
            });
        }

        void ReadAll()
        {
            IntPtr h = CurrentHandle();
            if (h == IntPtr.Zero) { SetStatus("Nessun monitor DDC/CI trovato — DDC/CI è attivo nel menu del monitor?", true); return; }
            worker.Post(delegate
            {
                uint inCur, inMax, bCur, bMax, cCur, cMax, pCur, pMax;
                bool okIn = Ddc.Get(h, 0x60, out inCur, out inMax);
                bool okB = Ddc.Get(h, 0x10, out bCur, out bMax);
                bool okC = Ddc.Get(h, 0x12, out cCur, out cMax);
                bool okP = Ddc.Get(h, 0x14, out pCur, out pMax);
                Ui(delegate
                {
                    if (h != CurrentHandle()) return;
                    if (okIn) Input = (int)(inCur & 0xFF);
                    if (okB) { Brightness = (int)bCur; BrightMax = bMax > 0 ? (int)bMax : 100; }
                    if (okC) { Contrast = (int)cCur; ContrastMax = cMax > 0 ? (int)cMax : 100; }
                    if (okP) Preset = (int)pCur;
                    if (okB || okIn) SetStatus("Connesso", false);
                    else SetStatus("Il monitor non risponde — controlla che DDC/CI sia attivo", true);
                    Changed();
                });
            });
        }

        void SetStatus(string text, bool error)
        {
            Status = text;
            StatusIsError = error;
            if (deck.Visible) deck.Invalidate();
        }

        // ---- comandi

        public void SetInput(uint v, bool showOsd)
        {
            IntPtr h = CurrentHandle();
            Input = (int)v;
            LastUserChange = DateTime.Now;
            Changed();
            if (showOsd) Osd("Ingresso  ·  " + Cfg.InputName(v), -1, 0);
            worker.Post(delegate
            {
                bool ok = Ddc.Set(h, 0x60, v);
                Ui(delegate { SetStatus(ok ? "Ingresso: " + Cfg.InputName(v) : "Cambio ingresso non riuscito", !ok); });
            });
        }

        public void SetBrightness(int v, bool showOsd)
        {
            v = Math.Max(0, Math.Min(BrightMax, v));
            Brightness = v;
            pendingBright = v;
            LastUserChange = DateTime.Now;
            if (showOsd) { Changed(); Osd("Luminosità", v, BrightMax); }
        }

        public void SetContrast(int v, bool showOsd)
        {
            v = Math.Max(0, Math.Min(ContrastMax, v));
            Contrast = v;
            pendingContrast = v;
            LastUserChange = DateTime.Now;
            if (showOsd) { Changed(); Osd("Contrasto", v, ContrastMax); }
        }

        public void SetPreset(uint v, bool showOsd)
        {
            IntPtr h = CurrentHandle();
            Preset = (int)v;
            LastUserChange = DateTime.Now;
            Changed();
            if (showOsd) Osd("Profilo  ·  " + Cfg.PresetName(v), -1, 0);
            worker.Post(delegate
            {
                bool ok = Ddc.Set(h, 0x14, v);
                Ui(delegate
                {
                    SetStatus(ok ? "Profilo: " + Cfg.PresetName(v) : "Cambio profilo non riuscito", !ok);
                    LastUserChange = DateTime.Now.AddMilliseconds(-1000);
                });
            });
        }

        // Invia al monitor solo l'ultimo valore degli slider (niente coda di comandi mentre trascini)
        void FlushPending()
        {
            IntPtr h = CurrentHandle();
            if (pendingBright >= 0)
            {
                uint v = (uint)pendingBright; pendingBright = -1;
                worker.Post(delegate { Ddc.Set(h, 0x10, v); });
            }
            if (pendingContrast >= 0)
            {
                uint v = (uint)pendingContrast; pendingContrast = -1;
                worker.Post(delegate { Ddc.Set(h, 0x12, v); });
            }
        }

        void Osd(string text, int value, int max)
        {
            MonitorEntry m = Current();
            osd.Flash(text, value, max, m != null ? m.Bounds : Rectangle.Empty);
        }

        // ---- scorciatoie

        void RegisterHotkeys()
        {
            hotkeys.UnregisterAll();
            hotkeyActions.Clear();
            List<string> failed = new List<string>();
            int id = 1;
            foreach (KeyValuePair<string, string> kv in Cfg.Hotkeys)
            {
                uint mods; Keys key;
                if (!HotkeyWindow.Parse(kv.Key, out mods, out key)) { failed.Add(kv.Key + " (non valida)"); continue; }
                if (hotkeys.Register(id, mods, key)) hotkeyActions[id] = kv.Value;
                else failed.Add(kv.Key + " (già usata)");
                id++;
            }
            if (failed.Count > 0)
                tray.ShowBalloonTip(5000, "Monitor Deck", "Scorciatoie non registrate: " + string.Join(", ", failed.ToArray()), ToolTipIcon.Warning);
        }

        void OnHotkey(int id)
        {
            string action;
            if (hotkeyActions.TryGetValue(id, out action)) Execute(action);
        }

        void Execute(string action)
        {
            string a = action.Trim().ToLowerInvariant().Replace("à", "a");
            string arg = "";
            int c = a.IndexOf(':');
            if (c >= 0) { arg = a.Substring(c + 1).Trim(); a = a.Substring(0, c).Trim(); }
            uint uv;
            switch (a)
            {
                case "ingresso":
                case "input":
                    if (uint.TryParse(arg, out uv)) SetInput(uv, true);
                    break;
                case "luminosita":
                case "brightness":
                    SetBrightness(Relative(arg, Brightness < 0 ? 50 : Brightness), true);
                    break;
                case "contrasto":
                case "contrast":
                    SetContrast(Relative(arg, Contrast < 0 ? 50 : Contrast), true);
                    break;
                case "profilo":
                case "preset":
                    if (uint.TryParse(arg, out uv)) SetPreset(uv, true);
                    break;
                case "pannello":
                case "panel":
                    TogglePanel();
                    break;
            }
        }

        static int Relative(string arg, int current)
        {
            int v;
            if (arg.StartsWith("+") || arg.StartsWith("-"))
            {
                if (int.TryParse(arg, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v)) return current + v;
                return current;
            }
            return int.TryParse(arg, out v) ? v : current;
        }
    }
}
