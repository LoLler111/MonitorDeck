using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorDeck
{
    // Applicazione: finestra + icona nell'area di notifica + scorciatoie + aggiornamenti
    class DeckApp : ApplicationContext
    {
        public const string ShowEventName = "MonitorDeck_ShowWindow_v2";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public Config Cfg;
        public Icon AppIcon, WindowIcon;
        public readonly List<MonitorState> Monitors = new List<MonitorState>();
        public string Status = "Ricerca monitor…";
        public bool StatusIsError;
        public DateTime LastUserChange = DateTime.MinValue;
        public ReleaseInfo Update;
        public bool UpdateBusy;
        public string UpdateText;
        public bool Exiting;

        readonly Dictionary<string, MonitorState> cache = new Dictionary<string, MonitorState>();
        readonly MainForm win;
        readonly OsdForm osd;
        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkeys;
        readonly DdcWorker worker = new DdcWorker();
        readonly SynchronizationContext ui;
        readonly System.Windows.Forms.Timer flushTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer displayTimer = new System.Windows.Forms.Timer();
        readonly Dictionary<int, string> hotkeyActions = new Dictionary<int, string>();
        readonly Dictionary<long, PendingWrite> pending = new Dictionary<long, PendingWrite>();
        EventWaitHandle showEvent;
        bool trayHintShown, balloonIsUpdate;
        string notifiedTag;

        class PendingWrite { public MonitorState St; public IntPtr H; public byte Code; public uint V; public DateTime Due; public long Key; }
        readonly Dictionary<long, PendingWrite> verifies = new Dictionary<long, PendingWrite>();

        public DeckApp(bool startHidden, bool justUpdated)
        {
            Cfg = Config.Load();
            ToolStripManager.Renderer = new DarkRenderer();
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                AppIcon = new Icon(exeIcon, SystemInformation.SmallIconSize);
                WindowIcon = exeIcon;
            }
            catch { AppIcon = WindowIcon = SystemIcons.Application; }

            win = new MainForm(this);
            IntPtr force = win.Handle; // crea l'handle sul thread UI
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            osd = new OsdForm();

            tray = new NotifyIcon();
            tray.Icon = AppIcon;
            tray.Text = AppInfo.Name;
            tray.ContextMenuStrip = BuildMenu(true);
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleWindow(); };
            tray.BalloonTipClicked += delegate
            {
                if (balloonIsUpdate && Update != null) { ShowWindow(); PromptUpdate(); }
                else ShowWindow();
            };
            tray.Visible = true;

            hotkeys = new HotkeyWindow();
            hotkeys.Pressed += OnHotkey;
            RegisterHotkeys();

            flushTimer.Interval = 60;
            flushTimer.Tick += delegate { FlushPending(); };
            flushTimer.Start();

            pollTimer.Interval = 4000;
            pollTimer.Tick += delegate { Poll(); };
            pollTimer.Start();

            displayTimer.Interval = 1500;
            displayTimer.Tick += delegate { displayTimer.Stop(); RefreshMonitors(); };
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            updateTimer.Interval = 8000;
            updateTimer.Tick += delegate
            {
                updateTimer.Interval = 6 * 60 * 60 * 1000;
                if (Cfg.AutoUpdate) CheckForUpdates(false);
            };
            updateTimer.Start();

            StartShowListener();
            if (GetAutostart()) SetAutostart(true); // aggiorna il percorso e aggiunge --tray

            RefreshMonitors();
            if (!startHidden) ShowWindow();
            if (justUpdated)
                Balloon("Monitor Deck aggiornato", "Ora usi la versione " + AppInfo.Version + ". Le novità sono nel menu → Novità di questa versione.", false);
        }

        // ------------------------------------------------------------ istanza unica: un secondo avvio mostra la finestra

        void StartShowListener()
        {
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                Thread t = new Thread(delegate()
                {
                    while (!Exiting)
                    {
                        try { showEvent.WaitOne(); } catch { return; }
                        if (!Exiting) Ui(ShowWindow);
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            catch { }
        }

        void Ui(Action a) { ui.Post(delegate { if (!Exiting) a(); }, null); }

        public void Changed() { win.RequestSync(); }

        // ------------------------------------------------------------ finestra

        public bool Expanded { get { return Cfg.Expanded; } }

        public void ShowWindow()
        {
            win.ShowWindow();
            Changed();
            if (Current != null)
            {
                ReadMain(Current);
                if (Expanded && !Current.DetailsRead) ReadDetails(Current);
            }
        }

        void ToggleWindow()
        {
            bool active = win.Visible && win.WindowState != FormWindowState.Minimized &&
                          (win.ContainsFocus || (DateTime.Now - win.LastDeactivate).TotalMilliseconds < 400);
            if (active) HideWindow();
            else ShowWindow();
        }

        void HideWindow()
        {
            SavePosition();
            win.Hide();
        }

        public void CloseWindow()
        {
            if (!Cfg.CloseToTray) { ExitApp(); return; }
            HideWindow();
            if (!trayHintShown)
            {
                trayHintShown = true;
                Balloon(AppInfo.Name, "L'app resta attiva qui. Clicca l'icona per riaprirla, tasto destro per uscire.", false);
            }
        }

        public void SavePosition()
        {
            if (!win.Visible || win.WindowState != FormWindowState.Normal) return;
            if (Cfg.X == win.Left && Cfg.Y == win.Top) return;
            Cfg.X = win.Left;
            Cfg.Y = win.Top;
            Cfg.SaveGeneral();
        }

        public void SetExpanded(bool on)
        {
            Cfg.Expanded = on;
            Cfg.SaveGeneral();
            Changed();
            MonitorState st = Current;
            if (on && st != null && !st.DetailsRead) ReadDetails(st);
        }

        public void SetVendorCodes(bool on)
        {
            Cfg.VendorCodes = on;
            Cfg.SaveGeneral();
            Changed();
        }

        public void SetTopMost(bool on)
        {
            Cfg.TopMost = on;
            Cfg.SaveGeneral();
            Changed();
        }

        void Balloon(string title, string text, bool isUpdate)
        {
            balloonIsUpdate = isUpdate;
            tray.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
        }

        // ------------------------------------------------------------ menu

        public ContextMenuStrip BuildMenu(bool forTray)
        {
            ContextMenuStrip m = Theme.NewMenu();
            ToolStripMenuItem miInputs = null, miMonitors;
            if (forTray)
            {
                ToolStripMenuItem open = new ToolStripMenuItem("Apri Monitor Deck", null, delegate { ShowWindow(); });
                open.Font = new Font(Theme.MenuFont, FontStyle.Bold);
                m.Items.Add(open);
                miInputs = new ToolStripMenuItem("Ingresso");
                Theme.StyleDropDown(miInputs.DropDown);
                m.Items.Add(miInputs);
            }
            miMonitors = new ToolStripMenuItem("Monitor");
            Theme.StyleDropDown(miMonitors.DropDown);
            m.Items.Add(miMonitors);
            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miTop = new ToolStripMenuItem("Sempre in primo piano", null, delegate { SetTopMost(!Cfg.TopMost); });
            ToolStripMenuItem miTray = new ToolStripMenuItem("Chiudi nell'area di notifica", null, delegate { Cfg.CloseToTray = !Cfg.CloseToTray; Cfg.SaveGeneral(); });
            ToolStripMenuItem miAuto = new ToolStripMenuItem("Avvia con Windows", null, delegate { SetAutostart(!GetAutostart()); });
            ToolStripMenuItem miVendor = new ToolStripMenuItem("Mostra codici del produttore", null, delegate { SetVendorCodes(!Cfg.VendorCodes); });
            m.Items.Add(miTop);
            m.Items.Add(miTray);
            m.Items.Add(miAuto);
            m.Items.Add(miVendor);
            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miUpd = new ToolStripMenuItem("Controlla aggiornamenti", null, delegate { CheckForUpdates(true); });
            ToolStripMenuItem miAutoUpd = new ToolStripMenuItem("Aggiornamenti automatici", null, delegate { Cfg.AutoUpdate = !Cfg.AutoUpdate; Cfg.SaveGeneral(); });
            m.Items.Add(miUpd);
            m.Items.Add(miAutoUpd);
            m.Items.Add("Novità di questa versione", null, delegate { OpenUrl(AppInfo.RepoUrl + "/releases/tag/v" + AppInfo.Version); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Modifica impostazioni…", null, delegate { try { Process.Start("notepad.exe", "\"" + Cfg.Path + "\""); } catch { } });
            m.Items.Add("Ricarica impostazioni", null, delegate { ReloadConfig(); });
            m.Items.Add("Informazioni su Monitor Deck", null, delegate { About(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Esci", null, delegate { ExitApp(); });

            m.Opening += delegate
            {
                miTop.Checked = Cfg.TopMost;
                miTray.Checked = Cfg.CloseToTray;
                miAuto.Checked = GetAutostart();
                miAutoUpd.Checked = Cfg.AutoUpdate;
                miVendor.Checked = Cfg.VendorCodes;
                miUpd.Text = Update != null ? "Aggiorna alla versione " + Update.VersionText + "…" : "Controlla aggiornamenti";
                miUpd.Enabled = !UpdateBusy;
                miMonitors.DropDownItems.Clear();
                FillMonitorItems(miMonitors.DropDownItems);
                if (miInputs != null)
                {
                    miInputs.DropDownItems.Clear();
                    MonitorState st = Current;
                    miInputs.Enabled = st != null && st.Supports(Vcp.Input);
                    if (st != null)
                    {
                        int cur = st.Cur(Vcp.Input);
                        foreach (KeyValuePair<string, uint> kv in InputsFor(st))
                        {
                            uint v = kv.Value;
                            ToolStripMenuItem it = new ToolStripMenuItem(kv.Key, null, delegate { SetChoice(Vcp.Input, v, false); });
                            it.Checked = cur >= 0 && (uint)cur == v;
                            miInputs.DropDownItems.Add(it);
                        }
                    }
                }
            };
            return m;
        }

        public void FillMonitorItems(ToolStripItemCollection items)
        {
            for (int i = 0; i < Monitors.Count; i++)
            {
                int idx = i;
                MonitorState st = Monitors[i];
                string label = (i + 1) + ".  " + MonitorName(st) + "   " + st.Entry.Bounds.Width + "×" + st.Entry.Bounds.Height;
                ToolStripMenuItem it = new ToolStripMenuItem(label, null, delegate { SelectMonitor(idx, true); });
                it.Checked = i == CurrentIndex;
                items.Add(it);
            }
            if (Monitors.Count > 0)
            {
                items.Add(new ToolStripSeparator());
                MonitorState cur = Current;
                items.Add(new ToolStripMenuItem("Rinomina «" + MonitorName(cur) + "»…", null, delegate { RenameMonitor(cur); }));
            }
            items.Add(new ToolStripMenuItem("Aggiorna elenco monitor", null, delegate { RefreshMonitors(); }));
        }

        void About()
        {
            bool open = DarkDialog.Ask(VisibleWin(), AppInfo.Name + " " + AppInfo.Version,
                "Controllo dei monitor via DDC/CI: ingresso, luminosità, volume e tutti gli altri comandi che il monitor mette a disposizione.\n\n" +
                AppInfo.RepoUrl.Replace("https://", ""),
                null, "Pagina GitHub", "Chiudi", false);
            if (open) OpenUrl(AppInfo.RepoUrl);
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch { }
        }

        Form VisibleWin() { return win.Visible ? win : null; }

        void ReloadConfig()
        {
            int oldIndex = Cfg.MonitorIndex;
            Cfg = Config.Load();
            RegisterHotkeys();
            Changed();
            if (Cfg.MonitorIndex != oldIndex) LoadMonitor(Current, true);
        }

        public void ExitApp()
        {
            if (Exiting) return;
            SavePosition();
            Exiting = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            tray.Visible = false;
            hotkeys.Dispose();
            try { if (showEvent != null) showEvent.Set(); } catch { }
            win.Close();
            ExitThread();
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
                    if (on) k.SetValue("MonitorDeck", "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue("MonitorDeck", false);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------ monitor

        public int CurrentIndex
        {
            get { return Monitors.Count == 0 ? -1 : Math.Max(0, Math.Min(Cfg.MonitorIndex, Monitors.Count - 1)); }
        }

        public MonitorState Current
        {
            get { int i = CurrentIndex; return i < 0 ? null : Monitors[i]; }
        }

        public void SelectMonitor(int index, bool flash)
        {
            if (Monitors.Count == 0) return;
            int n = Monitors.Count;
            index = ((index % n) + n) % n;
            bool changed = index != CurrentIndex;
            Cfg.MonitorIndex = index;
            Cfg.SaveGeneral();
            MonitorState st = Current;
            if (flash) osd.Flash(MonitorName(st), -1, 0, st.Entry.Bounds);
            if (!changed) return;
            Status = "Monitor: " + MonitorName(st);
            StatusIsError = false;
            Changed();
            LoadMonitor(st, false);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Ui(delegate { displayTimer.Stop(); displayTimer.Start(); });
        }

        public void RefreshMonitors()
        {
            SetStatus("Ricerca monitor…", false);
            worker.Post(delegate
            {
                List<MonitorEntry> list = Ddc.Enumerate();
                Ui(delegate
                {
                    string prevKey = Current != null ? Current.Key : null;
                    Monitors.Clear();
                    foreach (MonitorEntry e in list)
                    {
                        MonitorState st;
                        if (!cache.TryGetValue(e.Key, out st)) { st = new MonitorState(); cache[e.Key] = st; }
                        st.Entry = e;
                        Monitors.Add(st);
                    }
                    if (prevKey != null)
                        for (int i = 0; i < Monitors.Count; i++)
                            if (Monitors[i].Key == prevKey && Cfg.MonitorIndex != i) { Cfg.MonitorIndex = i; Cfg.SaveGeneral(); }
                    if (Monitors.Count == 0) SetStatus("Nessun monitor DDC/CI trovato", true);
                    Changed();
                    LoadMonitor(Current, true);
                });
            });
        }

        public void ReloadCurrent()
        {
            MonitorState st = Current;
            if (st == null) { RefreshMonitors(); return; }
            st.DetailsRead = false;
            Changed();
            LoadMonitor(st, true);
        }

        void LoadMonitor(MonitorState st, bool force)
        {
            if (st == null) return;
            ReadMain(st);
            if (!st.CapsTried) LoadCaps(st);
            else if (Expanded && (force || !st.DetailsRead)) ReadDetails(st);
        }

        void ReadMain(MonitorState st)
        {
            IntPtr h = st.Entry.Handle;
            worker.Post(delegate
            {
                Dictionary<byte, VcpReading> res = new Dictionary<byte, VcpReading>();
                foreach (byte c in Vcp.MainCodes) res[c] = Ddc.Read(h, c);
                Ui(delegate
                {
                    st.Merge(res);
                    st.MainRead = true;
                    if (st == Current)
                    {
                        bool any = false;
                        foreach (VcpReading r in res.Values) if (r.Ok) any = true;
                        if (any) { if (StatusIsError || Status.StartsWith("Ricerca") || Status.StartsWith("Monitor:")) SetStatus("Connesso", false); }
                        else SetStatus("Il monitor non risponde: DDC/CI è attivo nel suo menu?", true);
                    }
                    Changed();
                });
            });
        }

        void LoadCaps(MonitorState st)
        {
            if (st.CapsLoading) return;
            st.CapsLoading = true;
            IntPtr h = st.Entry.Handle;
            worker.Post(delegate
            {
                CapsInfo caps = CapsInfo.Parse(Ddc.GetCapabilities(h));
                Ui(delegate
                {
                    st.CapsLoading = false;
                    st.ApplyCaps(caps);
                    Changed();
                    if (Expanded && st == Current) ReadDetails(st);
                });
            });
        }

        void ReadDetails(MonitorState st)
        {
            if (st.DetailsLoading) return;
            if (!st.CapsTried) { LoadCaps(st); return; }
            st.DetailsLoading = true;
            IntPtr h = st.Entry.Handle;
            List<byte> codes = st.DetailCodes();
            worker.Post(delegate
            {
                Dictionary<byte, VcpReading> res = new Dictionary<byte, VcpReading>();
                foreach (byte c in codes) res[c] = Ddc.Read(h, c);
                Ui(delegate
                {
                    st.Merge(res);
                    st.DetailsRead = true;
                    st.DetailsLoading = false;
                    Changed();
                });
            });
        }

        void Poll()
        {
            MonitorState st = Current;
            if (st == null || !win.Visible || win.WindowState == FormWindowState.Minimized) return;
            if (!worker.IsIdle || pending.Count > 0 || verifies.Count > 0) return;
            if ((DateTime.Now - LastUserChange).TotalMilliseconds < 2500) return;
            ReadMain(st);
        }

        void SetStatus(string text, bool error)
        {
            Status = text;
            StatusIsError = error;
            win.UpdateValues();
        }

        // Ingressi da mostrare, senza quelli nascosti dall'utente
        public List<KeyValuePair<string, uint>> InputsFor(MonitorState st)
        {
            List<KeyValuePair<string, uint>> all = AllInputsFor(st);
            if (st == null) return all;
            List<uint> hidden = Cfg.HiddenInputsFor(st.Title);
            if (hidden.Count == 0) return all;
            List<KeyValuePair<string, uint>> list = new List<KeyValuePair<string, uint>>();
            foreach (KeyValuePair<string, uint> kv in all) if (!hidden.Contains(kv.Value)) list.Add(kv);
            return list;
        }

        public int HiddenInputCount(MonitorState st)
        {
            return st == null ? 0 : AllInputsFor(st).Count - InputsFor(st).Count;
        }

        public void HideInput(uint v)
        {
            MonitorState st = Current;
            if (st == null) return;
            if (InputsFor(st).Count <= 1) return; // almeno un ingresso resta visibile
            List<uint> hidden = new List<uint>(Cfg.HiddenInputsFor(st.Title));
            if (!hidden.Contains(v)) hidden.Add(v);
            Cfg.SetHiddenInputs(st.Title, hidden);
            SetStatus("Ingresso " + ChoiceName(st, Vcp.Input, v) + " nascosto", false);
            Changed();
        }

        public void ShowAllInputs()
        {
            MonitorState st = Current;
            if (st == null) return;
            Cfg.SetHiddenInputs(st.Title, new List<uint>());
            SetStatus("Tutti gli ingressi visibili", false);
            Changed();
        }

        // Con i nomi scelti dall'utente nella finestra (sezione [NomiIngressi]) al posto di quelli di base
        List<KeyValuePair<string, uint>> AllInputsFor(MonitorState st)
        {
            List<KeyValuePair<string, uint>> list = BaseInputsFor(st);
            if (st == null) return list;
            for (int i = 0; i < list.Count; i++)
            {
                string n = Cfg.InputRenameOrNull(st.Title, list[i].Value);
                if (n != null) list[i] = new KeyValuePair<string, uint>(n, list[i].Value);
            }
            return list;
        }

        List<KeyValuePair<string, uint>> BaseInputsFor(MonitorState st)
        {
            List<KeyValuePair<string, uint>> list = new List<KeyValuePair<string, uint>>();
            if (st == null) return list;

            // 1) sezione [Ingressi NomeMonitor] del file .ini: vince su tutto, nell'ordine scritto
            List<KeyValuePair<string, uint>> specific = Cfg.InputsForMonitor(st.Title, st.Entry.Name, st.Model);
            if (specific != null) return new List<KeyValuePair<string, uint>>(specific);

            // 2) ingressi dichiarati dal monitor, 3) elenco [Ingressi] del file .ini, 4) DP / HDMI 1 / HDMI 2
            List<uint> values = new List<uint>();
            if (st.CapsOk && st.CapValues.ContainsKey(Vcp.Input) && st.CapValues[Vcp.Input].Count > 0)
            {
                values.AddRange(st.CapValues[Vcp.Input]);
                values.Sort();
            }
            else foreach (KeyValuePair<string, uint> kv in Cfg.Inputs) values.Add(kv.Value);
            if (values.Count == 0) values.AddRange(new uint[] { 0x0F, 0x11, 0x12 });

            int cur = st.Cur(Vcp.Input);
            if (cur > 0 && !values.Contains((uint)cur)) values.Add((uint)cur);

            Dictionary<uint, string> auto = Vcp.InputNames(values);
            foreach (uint v in values)
                list.Add(new KeyValuePair<string, uint>(Cfg.InputNameOrNull(v) ?? auto[v], v));
            return list;
        }

        // Nome di base dell'ingresso (dal monitor o dal file .ini), senza la rinomina dell'utente
        public string BaseInputName(MonitorState st, uint v)
        {
            foreach (KeyValuePair<string, uint> kv in BaseInputsFor(st)) if (kv.Value == v) return kv.Key;
            return InputName(v);
        }

        public bool InputRenamed(MonitorState st, uint v)
        {
            return st != null && Cfg.InputRenameOrNull(st.Title, v) != null;
        }

        public void RenameInput(uint v)
        {
            MonitorState st = Current;
            if (st == null) return;
            string original = BaseInputName(st, v);
            string name = DarkDialog.Prompt(VisibleWin(), "Rinomina ingresso",
                "Nome da mostrare per questo ingresso di " + MonitorName(st) + ". Lascia vuoto per tornare a \"" + original + "\".",
                ChoiceName(st, Vcp.Input, v), "Rinomina");
            if (name == null) return;
            SetInputName(st, v, name);
        }

        public void SetInputName(MonitorState st, uint v, string name)
        {
            string original = BaseInputName(st, v);
            name = Config.CleanName(name);
            if (name != null && name == original) name = null;
            Cfg.SetInputRename(st.Title, v, name);
            SetStatus(name == null ? "Ingresso " + original + ": nome originale" : "Ingresso " + original + " rinominato in " + name, false);
            Changed();
        }

        // ------------------------------------------------------------ nomi dei monitor

        // Chiave nel file .ini: il nome del monitor; con due monitor uguali, anche l'uscita della scheda video
        string MonitorKey(MonitorState st)
        {
            int same = 0;
            foreach (MonitorState m in Monitors) if (string.Equals(m.Title, st.Title, StringComparison.OrdinalIgnoreCase)) same++;
            return same > 1 && !string.IsNullOrEmpty(st.Entry.Device) ? st.Title + "@" + st.Entry.Device : st.Title;
        }

        public string MonitorName(MonitorState st)
        {
            if (st == null) return "";
            return Cfg.MonitorNameOrNull(MonitorKey(st)) ?? st.Title;
        }

        public void RenameMonitor(MonitorState st)
        {
            if (st == null) return;
            string name = DarkDialog.Prompt(VisibleWin(), "Rinomina monitor",
                "Nome da mostrare per questo monitor. Lascia vuoto per tornare a \"" + st.Title + "\".",
                MonitorName(st), "Rinomina");
            if (name == null) return;
            name = Config.CleanName(name);
            if (name != null && name == st.Title) name = null;
            Cfg.SetMonitorName(MonitorKey(st), name);
            SetStatus("Monitor: " + MonitorName(st), false);
            Changed();
        }

        string InputName(uint v)
        {
            string n = Cfg.InputNameOrNull(v);
            if (n != null) return n;
            return Vcp.InputNames(new uint[] { v })[v];
        }

        // Nome da mostrare per un codice VCP: prima quello scelto nel file .ini, poi quello standard.
        public string CodeName(MonitorState st, byte code)
        {
            string n = Cfg.CodeName(code, st != null ? st.Title : null, st != null ? st.Model : null);
            if (n != null) return n;
            VcpDef d = Vcp.Get(code);
            return d != null ? d.Name : "Codice " + Vcp.Hex(code);
        }

        public string ChoiceName(MonitorState st, byte code, uint v)
        {
            if (code == Vcp.Input)
            {
                foreach (KeyValuePair<string, uint> kv in InputsFor(st)) if (kv.Value == v) return kv.Key;
                return InputName(v);
            }
            if (code == Vcp.Preset)
            {
                string n = Cfg.PresetNameOrNull(v);
                if (n != null) return n;
            }
            return Vcp.ValueName(code, v);
        }

        // ------------------------------------------------------------ comandi

        public void SetContinuous(byte code, int v, bool showOsd)
        {
            MonitorState st = Current;
            if (st == null) return;
            v = Math.Max(0, Math.Min(st.Max(code), v));
            st.SetLocal(code, (uint)v);
            LastUserChange = DateTime.Now;
            long key = ((long)st.Entry.Handle << 8) | code;
            PendingWrite p = new PendingWrite();
            p.St = st; p.H = st.Entry.Handle; p.Code = code; p.V = (uint)v; p.Key = key;
            pending[key] = p;
            Ddc.DemoLog("LEVA " + Vcp.Hex(code) + " -> " + v);
            if (showOsd)
            {
                Changed();
                VcpDef d = Vcp.Get(code);
                Osd(code == Vcp.Brightness ? "Luminosità" : d != null ? d.Name : Vcp.Hex(code), v, st.Max(code));
            }
        }

        // Invia al monitor solo l'ultimo valore degli slider (niente coda di comandi mentre trascini)
        void FlushPending()
        {
            if (!worker.IsIdle) return;
            if (pending.Count > 0)
            {
                List<PendingWrite> list = new List<PendingWrite>(pending.Values);
                pending.Clear();
                foreach (PendingWrite p in list)
                {
                    p.Due = DateTime.Now.AddMilliseconds(700); // verifica quando l'utente ha smesso di muovere la leva
                    verifies[p.Key] = p;
                }
                worker.Post(delegate { foreach (PendingWrite p in list) Ddc.Set(p.H, p.Code, p.V); });
                return;
            }
            VerifyWrites();
        }

        // A fine regolazione rilegge il valore dal monitor: se non l'ha applicato (comando perso), lo rimanda.
        void VerifyWrites()
        {
            if (verifies.Count == 0) return;
            List<PendingWrite> due = new List<PendingWrite>();
            foreach (PendingWrite p in verifies.Values) if (p.Due <= DateTime.Now) due.Add(p);
            if (due.Count == 0) return;
            foreach (PendingWrite p in due) verifies.Remove(p.Key);

            worker.Post(delegate
            {
                Dictionary<PendingWrite, VcpReading> results = new Dictionary<PendingWrite, VcpReading>();
                Dictionary<PendingWrite, bool> applied = new Dictionary<PendingWrite, bool>();
                foreach (PendingWrite p in due)
                {
                    VcpReading r = null;
                    bool ok = false;
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        Thread.Sleep(attempt == 0 ? 0 : 120 * attempt);
                        r = Ddc.Read(p.H, p.Code);
                        if (r.Ok && Math.Abs((long)r.Cur - p.V) <= 1) { ok = true; break; }
                        if (attempt < 3) Ddc.Set(p.H, p.Code, p.V);
                    }
                    Ddc.DemoLog("VERIFICA " + Vcp.Hex(p.Code) + " voluto=" + p.V + " letto=" + (r != null && r.Ok ? r.Cur.ToString() : "?") + (ok ? " OK" : " FALLITA"));
                    results[p] = r;
                    applied[p] = ok;
                }
                Ui(delegate
                {
                    bool failed = false;
                    foreach (PendingWrite p in due)
                    {
                        // l'utente ha ripreso a regolare nel frattempo: il valore nuovo vince
                        if (pending.ContainsKey(p.Key) || verifies.ContainsKey(p.Key)) continue;
                        VcpReading r = results[p];
                        if (r != null && r.Ok) p.St.SetFromMonitor(p.Code, r);
                        if (!applied[p]) failed = true;
                    }
                    if (failed) SetStatus("Il monitor non ha accettato il valore: la leva mostra quello reale", true);
                    Changed();
                });
            });
        }

        public void SetChoice(byte code, uint v, bool showOsd)
        {
            MonitorState st = Current;
            if (st == null) return;
            if (code == Vcp.Power && v != 1)
            {
                if (!DarkDialog.Ask(VisibleWin(), "Spegnere il monitor?",
                    "Con alcuni monitor, una volta spento via DDC/CI, non si può riaccendere da qui: servirà il tasto di accensione.",
                    null, "Spegni", "Annulla", true)) return;
            }
            IntPtr h = st.Entry.Handle;
            st.SetLocal(code, v);
            LastUserChange = DateTime.Now;
            Changed();
            string name = ChoiceName(st, code, v);
            VcpDef d = Vcp.Get(code);
            string label = d != null ? d.Name : Vcp.Hex(code);
            if (showOsd) Osd(label + "  ·  " + name, -1, 0);
            worker.Post(delegate
            {
                bool ok = Ddc.Set(h, code, v);
                Ui(delegate { SetStatus(ok ? label + ": " + name : "Comando non riuscito (" + label + ")", !ok); });
            });
        }

        public void ToggleMute()
        {
            MonitorState st = Current;
            if (st == null) return;
            bool muted = st.Cur(Vcp.Mute) == 1;
            SetChoice(Vcp.Mute, muted ? 2u : 1u, false);
        }

        public void RunAction(VcpDef d)
        {
            MonitorState st = Current;
            if (st == null) return;
            string body = d.Danger
                ? "Tutte le impostazioni del monitor torneranno ai valori di fabbrica. L'operazione non si può annullare."
                : "Il monitor riporterà questi valori alle impostazioni predefinite.";
            if (!DarkDialog.Ask(VisibleWin(), d.Name + "?", body, null, d.ActionText, "Annulla", d.Danger)) return;
            IntPtr h = st.Entry.Handle;
            byte code = d.Code;
            uint value = d.ActionValue;
            worker.Post(delegate
            {
                bool ok = Ddc.Set(h, code, value);
                Thread.Sleep(1500); // il monitor ha bisogno di un attimo prima di rispondere di nuovo
                Ui(delegate
                {
                    SetStatus(ok ? d.Name + ": fatto" : "Comando non riuscito", !ok);
                    st.DetailsRead = false;
                    LoadMonitor(st, true);
                });
            });
        }

        void Osd(string text, int value, int max)
        {
            MonitorState st = Current;
            osd.Flash(text, value, max, st != null ? st.Entry.Bounds : Rectangle.Empty);
        }

        // ------------------------------------------------------------ scorciatoie

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
                Balloon(AppInfo.Name, "Scorciatoie non registrate: " + string.Join(", ", failed.ToArray()), false);
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
            MonitorState st = Current;
            uint uv;
            switch (a)
            {
                case "ingresso":
                case "input":
                    if (uint.TryParse(arg, out uv)) SetChoice(Vcp.Input, uv, true);
                    else if (st != null) SwitchInput(st, arg);
                    break;
                case "luminosita":
                case "brightness":
                    if (st != null) SetContinuous(Vcp.Brightness, Relative(arg, st.Cur(Vcp.Brightness) < 0 ? 50 : st.Cur(Vcp.Brightness)), true);
                    break;
                case "contrasto":
                case "contrast":
                    if (st != null) SetContinuous(Vcp.Contrast, Relative(arg, st.Cur(Vcp.Contrast) < 0 ? 50 : st.Cur(Vcp.Contrast)), true);
                    break;
                case "volume":
                    if (st != null) SetContinuous(Vcp.Volume, Relative(arg, st.Cur(Vcp.Volume) < 0 ? 50 : st.Cur(Vcp.Volume)), true);
                    break;
                case "muto":
                case "mute":
                    if (st != null) { ToggleMute(); Osd(st.Cur(Vcp.Mute) == 1 ? "Audio disattivato" : "Audio attivo", -1, 0); }
                    break;
                case "profilo":
                case "preset":
                    if (uint.TryParse(arg, out uv)) SetChoice(Vcp.Preset, uv, true);
                    break;
                case "monitor":
                    int n;
                    if (arg == "successivo" || arg == "next" || arg == "") SelectMonitor(CurrentIndex + 1, true);
                    else if (arg == "precedente" || arg == "prev") SelectMonitor(CurrentIndex - 1, true);
                    else if (int.TryParse(arg, out n)) SelectMonitor(n - 1, true);
                    break;
                case "pannello":
                case "panel":
                case "finestra":
                case "window":
                    ToggleWindow();
                    break;
            }
        }

        // ingresso:HDMI 2 (per nome, come mostrato nella finestra) oppure ingresso:successivo
        void SwitchInput(MonitorState st, string arg)
        {
            List<KeyValuePair<string, uint>> inputs = InputsFor(st);
            if (inputs.Count == 0) return;
            if (arg == "successivo" || arg == "next" || arg == "precedente" || arg == "prev")
            {
                int cur = st.Cur(Vcp.Input), i = 0;
                for (int k = 0; k < inputs.Count; k++) if (cur >= 0 && inputs[k].Value == (uint)cur) i = k;
                i += (arg == "precedente" || arg == "prev") ? inputs.Count - 1 : 1;
                SetChoice(Vcp.Input, inputs[i % inputs.Count].Value, true);
                return;
            }
            string want = arg.Replace(" ", "");
            foreach (KeyValuePair<string, uint> kv in inputs)
                if (string.Equals(kv.Key.Replace(" ", ""), want, StringComparison.OrdinalIgnoreCase)) { SetChoice(Vcp.Input, kv.Value, true); return; }
            // nome originale di un ingresso rinominato nella finestra: la scorciatoia continua a funzionare
            foreach (KeyValuePair<string, uint> kv in inputs)
                if (string.Equals(BaseInputName(st, kv.Value).Replace(" ", ""), want, StringComparison.OrdinalIgnoreCase)) { SetChoice(Vcp.Input, kv.Value, true); return; }
            // "HDMI" su un monitor che ha "HDMI 1" e "HDMI 2" (o viceversa): prende la prima che inizia cosi'
            foreach (KeyValuePair<string, uint> kv in inputs)
                if (kv.Key.Replace(" ", "").StartsWith(want, StringComparison.OrdinalIgnoreCase)) { SetChoice(Vcp.Input, kv.Value, true); return; }
            Osd("Ingresso \"" + arg + "\" non trovato", -1, 0);
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

        // ------------------------------------------------------------ aggiornamenti

        public void CheckForUpdates(bool manual)
        {
            if (UpdateBusy) return;
            if (manual && Update != null) { PromptUpdate(); return; }
            UpdateBusy = true;
            if (manual) SetStatus("Controllo aggiornamenti…", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r = null;
                string err = null;
                try { r = Updater.FetchLatest(); }
                catch (Exception ex) { err = ex.Message; }
                Ui(delegate
                {
                    UpdateBusy = false;
                    if (err != null)
                    {
                        if (manual)
                        {
                            SetStatus("Controllo aggiornamenti non riuscito", true);
                            DarkDialog.Info(VisibleWin(), "Controllo non riuscito", "Non riesco a contattare GitHub.\n\n" + err);
                        }
                        return;
                    }
                    if (r != null && r.Version > Updater.Current)
                    {
                        Update = r;
                        Changed();
                        if (manual) { SetStatus("Nuova versione disponibile", false); PromptUpdate(); }
                        else if (notifiedTag != r.Tag)
                        {
                            notifiedTag = r.Tag;
                            Balloon("Aggiornamento disponibile", "Monitor Deck " + r.VersionText + " è pronto. Clicca qui per vedere le novità e aggiornare.", true);
                        }
                    }
                    else if (manual)
                    {
                        SetStatus("Nessun aggiornamento", false);
                        DarkDialog.Info(VisibleWin(), "Nessun aggiornamento", "Stai già usando l'ultima versione (" + AppInfo.Version + ").");
                    }
                });
            });
        }

        public void PromptUpdate()
        {
            if (Update == null || UpdateBusy) return;
            bool go = DarkDialog.Ask(VisibleWin(), "Monitor Deck " + Update.VersionText,
                "È disponibile una nuova versione (ora hai la " + AppInfo.Version + "). L'app si riavvierà da sola dopo l'aggiornamento.",
                Updater.NotesToText(Update.Notes), "Aggiorna ora", "Più tardi", false);
            if (go) InstallUpdate();
        }

        void InstallUpdate()
        {
            ReleaseInfo r = Update;
            UpdateBusy = true;
            UpdateText = "Download della versione " + r.VersionText + "…";
            SetStatus(UpdateText, false);
            Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string path = null, err = null;
                try { path = Updater.Download(r); }
                catch (Exception ex) { err = ex.Message; }
                Ui(delegate
                {
                    if (err == null)
                    {
                        try
                        {
                            Updater.ReplaceAndRestart(path);
                            ExitApp();
                            return;
                        }
                        catch (Exception ex) { err = ex.Message; }
                    }
                    UpdateBusy = false;
                    UpdateText = null;
                    SetStatus("Aggiornamento non riuscito", true);
                    Changed();
                    if (DarkDialog.Ask(VisibleWin(), "Aggiornamento non riuscito",
                        err + "\n\nPuoi scaricare la nuova versione dalla pagina GitHub e sostituire l'exe a mano.",
                        null, "Apri pagina", "Chiudi", false))
                        OpenUrl(r.PageUrl);
                });
            });
        }
    }
}
