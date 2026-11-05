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
        public bool VendorCodes = false;
        public int X = int.MinValue, Y = int.MinValue;
        // Nomi dei codici VCP: chiave "" = tutti i monitor, altrimenti nome del monitor
        public Dictionary<string, Dictionary<byte, string>> CodeNames = new Dictionary<string, Dictionary<byte, string>>(StringComparer.OrdinalIgnoreCase);
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
"CodiciProduttore=0\r\n" +
"X=\r\n" +
"Y=\r\n" +
"\r\n" +
"[Ingressi]\r\n" +
"; Gli ingressi vengono letti dal monitor: qui puoi rinominarli (Nome = valore VCP 60, decimale).\r\n" +
"; Per un solo monitor crea una sezione col suo nome, es. [Ingressi AW3423DWF]: elenca gli ingressi in quell'ordine.\r\n" +
";PC=15\r\n" +
";Console=17\r\n" +
"\r\n" +
"[Profili]\r\n" +
"; Nomi dei profili colore: valore VCP 14 = nome mostrato\r\n" +
";11=Gioco\r\n" +
"\r\n" +
"[Nomi]\r\n" +
"; Nome di un comando nel pannello \"Tutti i controlli\": codice VCP esadecimale = nome.\r\n" +
"; Utile per i codici del produttore (E0-FF), che i produttori non documentano.\r\n" +
"; Per un solo monitor usa una sezione come [Nomi AW3423DWF].\r\n" +
";E0=Modalità console\r\n" +
"\r\n" +
"[Scorciatoie]\r\n" +
"; Tasti = azione. Modificatori: Ctrl, Alt, Shift, Win\r\n" +
"; Azioni: ingresso:HDMI 1   ingresso:17   ingresso:successivo   luminosita:+10   luminosita:30\r\n" +
";         contrasto:+5   volume:+5   muto   profilo:5   monitor:successivo   monitor:2   finestra\r\n" +
"Ctrl+Alt+1=ingresso:DisplayPort\r\n" +
"Ctrl+Alt+2=ingresso:HDMI 1\r\n" +
"Ctrl+Alt+3=ingresso:HDMI 2\r\n" +
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
            Migrate(c.Path);

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
                    else if (lk == "codiciproduttore") c.VendorCodes = v == "1";
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
                else if (ls == "nomi" || ls.StartsWith("nomi ") || ls.StartsWith("nomi:"))
                {
                    string mon = ls == "nomi" ? "" : section.Substring(5).Trim();
                    string hex = k.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? k.Substring(2) : k;
                    int code;
                    if (v.Length == 0 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code) || code < 0 || code > 0xFF) continue;
                    Dictionary<byte, string> map;
                    if (!c.CodeNames.TryGetValue(mon, out map)) { map = new Dictionary<byte, string>(); c.CodeNames[mon] = map; }
                    map[(byte)code] = v;
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

        // Il file creato dalla 2.0.0 aveva ingressi e scorciatoie con valori fissi (18 = HDMI 2 standard),
        // sbagliati sui monitor che usano altri codici. Se l'utente non li ha toccati, passo ai nomi.
        static void Migrate(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string t = File.ReadAllText(path, Encoding.UTF8);
                string o = t;
                string oldKeys = "Ctrl+Alt+1=ingresso:15\r\nCtrl+Alt+2=ingresso:17\r\nCtrl+Alt+3=ingresso:18\r\n";
                if (t.Contains(oldKeys))
                    t = t.Replace(oldKeys, "Ctrl+Alt+1=ingresso:DisplayPort\r\nCtrl+Alt+2=ingresso:HDMI 1\r\nCtrl+Alt+3=ingresso:HDMI 2\r\n");
                string oldInputs = "DisplayPort=15\r\nHDMI 1=17\r\nHDMI 2=18\r\n";
                if (t.Contains(oldInputs))
                    t = t.Replace(oldInputs, ";PC=15\r\n;Console=17\r\n");
                if (t != o) File.WriteAllText(path, t, Encoding.UTF8);
            }
            catch { }
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
            vals.Add(new KeyValuePair<string, string>("CodiciProduttore", VendorCodes ? "1" : "0"));
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

        public string CodeName(byte code, params string[] monitorNames)
        {
            string n;
            foreach (KeyValuePair<string, Dictionary<byte, string>> kv in CodeNames)
            {
                if (kv.Key.Length == 0) continue;
                foreach (string m in monitorNames)
                    if (!string.IsNullOrEmpty(m) && m.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0 && kv.Value.TryGetValue(code, out n)) return n;
            }
            Dictionary<byte, string> all;
            if (CodeNames.TryGetValue("", out all) && all.TryGetValue(code, out n)) return n;
            return null;
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
