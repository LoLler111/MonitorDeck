using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MonitorDeck
{
    // Impostazioni nel file MonitorDeck.ini accanto all'exe
    class Config
    {
        public string Path;
        public int MonitorIndex = 0;
        public bool TopMost = false;
        public bool Expanded = false;
        public bool CloseToTray = true;
        public bool AutoUpdate = true;
        public int X = int.MinValue, Y = int.MinValue;
        public List<KeyValuePair<string, uint>> Inputs = new List<KeyValuePair<string, uint>>();
        public Dictionary<string, List<KeyValuePair<string, uint>>> MonitorInputs = new Dictionary<string, List<KeyValuePair<string, uint>>>();
        public List<KeyValuePair<uint, string>> Presets = new List<KeyValuePair<uint, string>>();
        public List<KeyValuePair<string, string>> Hotkeys = new List<KeyValuePair<string, string>>();

        const string DefaultIni =
"; Monitor Deck - impostazioni\r\n" +
"; Dopo una modifica salva il file e scegli \"Ricarica impostazioni\" dal menu.\r\n" +
"\r\n" +
"[Generale]\r\n" +
"Monitor=0\r\n" +
"SempreInPrimoPiano=0\r\n" +
"Espanso=0\r\n" +
"ChiudiNellaTray=1\r\n" +
"AggiornamentiAutomatici=1\r\n" +
"X=\r\n" +
"Y=\r\n" +
"\r\n" +
"[Ingressi]\r\n" +
"; Nome mostrato = valore VCP 60 (decimale).\r\n" +
"; Gli ingressi vengono letti dal monitor: qui puoi rinominarli, oppure elencarli se il monitor non li comunica.\r\n" +
"; Per un monitor specifico crea una sezione come [Ingressi AW3423DW] con il nome del monitor.\r\n" +
"DisplayPort=15\r\n" +
"HDMI 1=17\r\n" +
"HDMI 2=18\r\n" +
"\r\n" +
"[Profili]\r\n" +
"; valore VCP 14 = nome mostrato\r\n" +
"1=sRGB\r\n" +
"5=6500K\r\n" +
"8=9300K\r\n" +
"11=Utente 1\r\n" +
"12=Utente 2\r\n" +
"\r\n" +
"[Scorciatoie]\r\n" +
"; Tasti = azione. Modificatori: Ctrl, Alt, Shift, Win\r\n" +
"; Azioni: ingresso:15   luminosita:+10   luminosita:30   contrasto:+5   volume:+5   volume:-5\r\n" +
";         muto   profilo:5   monitor:successivo   monitor:2   finestra\r\n" +
"Ctrl+Alt+1=ingresso:15\r\n" +
"Ctrl+Alt+2=ingresso:17\r\n" +
"Ctrl+Alt+3=ingresso:18\r\n" +
"Ctrl+Alt+Up=luminosita:+10\r\n" +
"Ctrl+Alt+Down=luminosita:-10\r\n" +
"Ctrl+Alt+0=luminosita:0\r\n" +
"Ctrl+Alt+M=finestra\r\n";

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
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                string ls = section.ToLowerInvariant();
                int iv; uint uv;
                if (ls == "generale")
                {
                    string lk = k.ToLowerInvariant();
                    if (lk == "monitor" && int.TryParse(v, out iv)) c.MonitorIndex = iv;
                    else if (lk == "sempreinprimopiano") c.TopMost = v == "1";
                    else if (lk == "espanso") c.Expanded = v == "1";
                    else if (lk == "chiudinellatray") c.CloseToTray = v != "0";
                    else if (lk == "aggiornamentiautomatici") c.AutoUpdate = v != "0";
                    else if (lk == "x" && int.TryParse(v, out iv)) c.X = iv;
                    else if (lk == "y" && int.TryParse(v, out iv)) c.Y = iv;
                }
                else if (ls == "ingressi")
                {
                    if (uint.TryParse(v, out uv)) c.Inputs.Add(new KeyValuePair<string, uint>(k, uv));
                }
                else if (ls.StartsWith("ingressi ") || ls.StartsWith("ingressi:"))
                {
                    string mon = section.Substring(9).Trim();
                    if (mon.Length == 0 || !uint.TryParse(v, out uv)) continue;
                    List<KeyValuePair<string, uint>> list;
                    if (!c.MonitorInputs.TryGetValue(mon, out list)) { list = new List<KeyValuePair<string, uint>>(); c.MonitorInputs[mon] = list; }
                    list.Add(new KeyValuePair<string, uint>(k, uv));
                }
                else if (ls == "profili")
                {
                    if (uint.TryParse(k, out uv)) c.Presets.Add(new KeyValuePair<uint, string>(uv, v));
                }
                else if (ls == "scorciatoie")
                {
                    c.Hotkeys.Add(new KeyValuePair<string, string>(k, v));
                }
            }
            return c;
        }

        // Aggiorna solo la sezione [Generale] lasciando intatto il resto (commenti compresi).
        public void SaveGeneral()
        {
            List<KeyValuePair<string, string>> vals = new List<KeyValuePair<string, string>>();
            vals.Add(new KeyValuePair<string, string>("Monitor", MonitorIndex.ToString(CultureInfo.InvariantCulture)));
            vals.Add(new KeyValuePair<string, string>("SempreInPrimoPiano", TopMost ? "1" : "0"));
            vals.Add(new KeyValuePair<string, string>("Espanso", Expanded ? "1" : "0"));
            vals.Add(new KeyValuePair<string, string>("ChiudiNellaTray", CloseToTray ? "1" : "0"));
            vals.Add(new KeyValuePair<string, string>("AggiornamentiAutomatici", AutoUpdate ? "1" : "0"));
            vals.Add(new KeyValuePair<string, string>("X", X == int.MinValue ? "" : X.ToString(CultureInfo.InvariantCulture)));
            vals.Add(new KeyValuePair<string, string>("Y", Y == int.MinValue ? "" : Y.ToString(CultureInfo.InvariantCulture)));
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
                        if (kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase)) { lines[i] = kv.Key + "=" + kv.Value; done.Add(kv.Key); }
                }
                int insertAt = start + 1;
                foreach (KeyValuePair<string, string> kv in vals)
                    if (!done.Contains(kv.Key)) { lines.Insert(insertAt, kv.Key + "=" + kv.Value); insertAt++; }
                File.WriteAllLines(Path, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public string InputNameOrNull(uint v)
        {
            foreach (KeyValuePair<string, uint> kv in Inputs) if (kv.Value == v) return kv.Key;
            return null;
        }

        public string PresetNameOrNull(uint v)
        {
            foreach (KeyValuePair<uint, string> kv in Presets) if (kv.Key == v) return kv.Value;
            return null;
        }

        public List<KeyValuePair<string, uint>> InputsForMonitor(params string[] names)
        {
            foreach (KeyValuePair<string, List<KeyValuePair<string, uint>>> kv in MonitorInputs)
                foreach (string n in names)
                    if (!string.IsNullOrEmpty(n) && n.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            return null;
        }
    }
}
