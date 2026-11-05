using System;
using System.Collections.Generic;
using System.Globalization;

namespace MonitorDeck
{
    enum VcpKind { Continuous, Choice, Action, Info }

    class VcpDef
    {
        public byte Code;
        public string Name;
        public string Group;
        public VcpKind Kind;
        public uint ActionValue = 1;
        public string ActionText = "Esegui";
        public bool Danger;
        public Dictionary<uint, string> Values = new Dictionary<uint, string>();
        public List<uint> Order = new List<uint>();
    }

    // Catalogo dei codici VCP (standard MCCS 2.2) con nomi in italiano.
    static class Vcp
    {
        public const byte Brightness = 0x10, Contrast = 0x12, Preset = 0x14, Input = 0x60, Volume = 0x62, Mute = 0x8D, Power = 0xD6;

        public static readonly byte[] MainCodes = { Brightness, Contrast, Input, Volume, Mute };
        public static readonly string[] Groups = { "Immagine", "Colore", "Audio", "Sistema", "Ripristino", "Informazioni" };
        public static readonly List<VcpDef> Ordered = new List<VcpDef>();
        static readonly Dictionary<byte, VcpDef> byCode = new Dictionary<byte, VcpDef>();

        static Vcp()
        {
            // Immagine
            Add(0x12, "Contrasto", "Immagine", VcpKind.Continuous);
            Add(0x6B, "Retroilluminazione", "Immagine", VcpKind.Continuous);
            Add(0x87, "Nitidezza", "Immagine", VcpKind.Continuous);
            Add(0x8A, "Saturazione", "Immagine", VcpKind.Continuous);
            Add(0x90, "Tonalità", "Immagine", VcpKind.Continuous);
            Add(0xDC, "Modalità immagine", "Immagine", VcpKind.Choice,
                0x00, "Standard", 0x01, "Produttività", 0x02, "Misto", 0x03, "Film", 0x04, "Utente",
                0x05, "Giochi", 0x06, "Sport", 0x07, "Professionale", 0x08, "Standard (consumo medio)",
                0x09, "Standard (consumo basso)", 0x0A, "Dimostrazione", 0xF0, "Contrasto dinamico");
            Add(0x14, "Profilo colore", "Immagine", VcpKind.Choice,
                0x01, "sRGB", 0x02, "Nativo", 0x03, "4000K", 0x04, "5000K", 0x05, "6500K", 0x06, "7500K",
                0x07, "8200K", 0x08, "9300K", 0x09, "10000K", 0x0A, "11500K", 0x0B, "Utente 1", 0x0C, "Utente 2", 0x0D, "Utente 3");
            Add(0x0C, "Temperatura colore", "Immagine", VcpKind.Continuous);

            // Colore
            Add(0x16, "Guadagno rosso", "Colore", VcpKind.Continuous);
            Add(0x18, "Guadagno verde", "Colore", VcpKind.Continuous);
            Add(0x1A, "Guadagno blu", "Colore", VcpKind.Continuous);
            Add(0x6C, "Livello nero rosso", "Colore", VcpKind.Continuous);
            Add(0x6E, "Livello nero verde", "Colore", VcpKind.Continuous);
            Add(0x70, "Livello nero blu", "Colore", VcpKind.Continuous);
            Add(0x59, "Saturazione rosso", "Colore", VcpKind.Continuous);
            Add(0x5A, "Saturazione giallo", "Colore", VcpKind.Continuous);
            Add(0x5B, "Saturazione verde", "Colore", VcpKind.Continuous);
            Add(0x5C, "Saturazione ciano", "Colore", VcpKind.Continuous);
            Add(0x5D, "Saturazione blu", "Colore", VcpKind.Continuous);
            Add(0x5E, "Saturazione magenta", "Colore", VcpKind.Continuous);
            Add(0x9B, "Tonalità rosso", "Colore", VcpKind.Continuous);
            Add(0x9C, "Tonalità giallo", "Colore", VcpKind.Continuous);
            Add(0x9D, "Tonalità verde", "Colore", VcpKind.Continuous);
            Add(0x9E, "Tonalità ciano", "Colore", VcpKind.Continuous);
            Add(0x9F, "Tonalità blu", "Colore", VcpKind.Continuous);
            Add(0xA0, "Tonalità magenta", "Colore", VcpKind.Continuous);

            // Audio
            Add(0x62, "Volume", "Audio", VcpKind.Continuous);
            Add(0x8D, "Audio", "Audio", VcpKind.Choice, 0x01, "Muto", 0x02, "Attivo");
            Add(0x8F, "Alti", "Audio", VcpKind.Continuous);
            Add(0x91, "Bassi", "Audio", VcpKind.Continuous);
            Add(0x93, "Bilanciamento", "Audio", VcpKind.Continuous);

            // Sistema
            Add(0x60, "Ingresso", "Sistema", VcpKind.Choice,
                0x01, "VGA 1", 0x02, "VGA 2", 0x03, "DVI 1", 0x04, "DVI 2", 0x05, "Composito 1", 0x06, "Composito 2",
                0x07, "S-Video 1", 0x08, "S-Video 2", 0x09, "Tuner 1", 0x0A, "Tuner 2", 0x0B, "Tuner 3",
                0x0C, "Component 1", 0x0D, "Component 2", 0x0E, "Component 3", 0x0F, "DisplayPort 1",
                0x10, "DisplayPort 2", 0x11, "HDMI 1", 0x12, "HDMI 2", 0x1B, "USB-C");
            Add(0xD6, "Alimentazione", "Sistema", VcpKind.Choice,
                0x01, "Acceso", 0x02, "Standby", 0x03, "Sospensione", 0x04, "Spento (risparmio)", 0x05, "Spento");
            Add(0xCC, "Lingua del menu", "Sistema", VcpKind.Choice,
                0x01, "Cinese (tradizionale)", 0x02, "Inglese", 0x03, "Francese", 0x04, "Tedesco", 0x05, "Italiano",
                0x06, "Giapponese", 0x07, "Coreano", 0x08, "Portoghese", 0x09, "Russo", 0x0A, "Spagnolo",
                0x0B, "Svedese", 0x0C, "Turco", 0x0D, "Cinese (semplificato)", 0x0E, "Portoghese (Brasile)",
                0x0F, "Arabo", 0x10, "Bulgaro", 0x11, "Croato", 0x12, "Ceco", 0x13, "Danese", 0x14, "Olandese",
                0x15, "Estone", 0x16, "Finlandese", 0x17, "Greco", 0x18, "Ebraico", 0x19, "Hindi", 0x1A, "Ungherese",
                0x1B, "Lettone", 0x1C, "Lituano", 0x1D, "Norvegese", 0x1E, "Polacco", 0x1F, "Rumeno", 0x20, "Serbo",
                0x21, "Slovacco", 0x22, "Sloveno", 0x23, "Thailandese", 0x24, "Ucraino", 0x25, "Vietnamita");
            Add(0xCA, "Menu a schermo (OSD)", "Sistema", VcpKind.Choice, 0x01, "Disattivato", 0x02, "Attivo");

            // Ripristino
            VcpDef d;
            d = Add(0x05, "Ripristina luminosità e contrasto", "Ripristino", VcpKind.Action); d.ActionText = "Ripristina";
            d = Add(0x08, "Ripristina colori", "Ripristino", VcpKind.Action); d.ActionText = "Ripristina";
            d = Add(0x04, "Ripristino impostazioni di fabbrica", "Ripristino", VcpKind.Action); d.ActionText = "Ripristina"; d.Danger = true;

            // Informazioni
            Add(0xC0, "Ore di utilizzo", "Informazioni", VcpKind.Info);
            Add(0xC9, "Firmware", "Informazioni", VcpKind.Info);
            Add(0xDF, "Versione MCCS", "Informazioni", VcpKind.Info);
        }

        static VcpDef Add(byte code, string name, string group, VcpKind kind, params object[] values)
        {
            VcpDef d = new VcpDef();
            d.Code = code; d.Name = name; d.Group = group; d.Kind = kind;
            for (int i = 0; i + 1 < values.Length; i += 2)
            {
                uint v = Convert.ToUInt32(values[i]);
                d.Values[v] = (string)values[i + 1];
                d.Order.Add(v);
            }
            Ordered.Add(d);
            byCode[code] = d;
            return d;
        }

        public static VcpDef Get(byte code)
        {
            VcpDef d;
            return byCode.TryGetValue(code, out d) ? d : null;
        }

        public static bool IsMain(byte code)
        {
            return code == Brightness || code == Input || code == Volume || code == Mute;
        }

        public static bool IsManufacturer(byte code) { return code >= 0xE0; }

        // Nomi degli ingressi numerati sulle porte che il monitor ha davvero.
        // Molti monitor (es. Alienware/Dell) usano codici fuori standard come 0x13 per la seconda HDMI:
        // numerando per famiglia, 0x11 + 0x13 diventano "HDMI 1" e "HDMI 2"; una porta sola resta "HDMI".
        public static Dictionary<uint, string> InputNames(IEnumerable<uint> values)
        {
            List<uint> sorted = new List<uint>(values);
            sorted.Sort();
            Dictionary<string, List<uint>> fam = new Dictionary<string, List<uint>>();
            foreach (uint v in sorted)
            {
                string f = InputFamily(v);
                if (f == null) continue;
                if (!fam.ContainsKey(f)) fam[f] = new List<uint>();
                if (!fam[f].Contains(v)) fam[f].Add(v);
            }
            Dictionary<uint, string> names = new Dictionary<uint, string>();
            foreach (uint v in sorted)
            {
                if (names.ContainsKey(v)) continue;
                string f = InputFamily(v);
                if (f != null)
                {
                    List<uint> l = fam[f];
                    names[v] = l.Count == 1 ? f : f + " " + (l.IndexOf(v) + 1);
                    continue;
                }
                string n;
                names[v] = Get(Input).Values.TryGetValue(v, out n) ? n : "Ingresso " + v;
            }
            return names;
        }

        static string InputFamily(uint v)
        {
            if (v == 0x01 || v == 0x02) return "VGA";
            if (v == 0x03 || v == 0x04) return "DVI";
            if (v == 0x0F || v == 0x10) return "DisplayPort";
            if (v >= 0x11 && v <= 0x14) return "HDMI";
            if (v == 0x1B) return "USB-C";
            return null;
        }

        public static string Hex(uint v) { return "0x" + v.ToString("X2"); }

        public static string ValueName(byte code, uint v)
        {
            VcpDef d = Get(code);
            string n;
            if (d != null && d.Values.TryGetValue(v, out n)) return n;
            return "Valore " + Hex(v);
        }

        public static string FormatInfo(byte code, VcpReading r)
        {
            if (r == null || !r.Ok) return "–";
            switch (code)
            {
                case 0xC0:
                    uint hours = (r.Max << 16) | (r.Cur & 0xFFFF);
                    return hours.ToString("N0", CultureInfo.GetCultureInfo("it-IT")) + " h";
                case 0xC9:
                    return (r.Cur >> 8) + "." + (r.Cur & 0xFF).ToString("D2");
                case 0xDF:
                    return (r.Cur >> 8) + "." + (r.Cur & 0xFF);
            }
            return r.Cur.ToString();
        }
    }

    // Interpreta la stringa delle capacita', es.  (prot(monitor)type(LCD)model(AW3423DW)vcp(02 10 12 14(01 05 08) 60(0F 11 12))mccs_ver(2.1))
    class CapsInfo
    {
        public string Model;
        public string Mccs;
        public readonly List<byte> Codes = new List<byte>();
        public readonly Dictionary<byte, List<uint>> Values = new Dictionary<byte, List<uint>>();

        public static CapsInfo Parse(string caps)
        {
            if (string.IsNullOrEmpty(caps)) return null;
            CapsInfo info = new CapsInfo();
            info.Model = Section(caps, "model");
            info.Mccs = Section(caps, "mccs_ver");
            string vcp = Section(caps, "vcp");
            if (vcp == null) return null;

            int i = 0, n = vcp.Length;
            int last = -1;
            while (i < n)
            {
                char c = vcp[i];
                if (IsHex(c))
                {
                    int st = i;
                    while (i < n && IsHex(vcp[i])) i++;
                    int v;
                    if (int.TryParse(vcp.Substring(st, i - st), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) && v >= 0 && v <= 0xFF)
                    {
                        last = v;
                        if (!info.Codes.Contains((byte)v)) info.Codes.Add((byte)v);
                    }
                    else last = -1;
                }
                else if (c == '(')
                {
                    int depth = 1, st = ++i;
                    while (i < n && depth > 0) { if (vcp[i] == '(') depth++; else if (vcp[i] == ')') depth--; i++; }
                    string inner = vcp.Substring(st, Math.Max(0, i - st - 1));
                    if (last >= 0)
                    {
                        List<uint> list = new List<uint>();
                        foreach (string tok in inner.Split(new char[] { ' ', '\t', '\r', '\n', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            uint v;
                            if (uint.TryParse(tok, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) && !list.Contains(v)) list.Add(v);
                        }
                        info.Values[(byte)last] = list;
                    }
                }
                else i++;
            }
            return info;
        }

        static bool IsHex(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }

        static string Section(string s, string name)
        {
            int from = 0;
            while (true)
            {
                int p = s.IndexOf(name, from, StringComparison.OrdinalIgnoreCase);
                if (p < 0) return null;
                from = p + name.Length;
                if (p > 0 && (char.IsLetterOrDigit(s[p - 1]) || s[p - 1] == '_')) continue;
                int q = from;
                while (q < s.Length && s[q] == ' ') q++;
                if (q >= s.Length || s[q] != '(') continue;
                int depth = 1, st = q + 1, i = st;
                while (i < s.Length && depth > 0) { if (s[i] == '(') depth++; else if (s[i] == ')') depth--; i++; }
                return s.Substring(st, Math.Max(0, i - st - (depth == 0 ? 1 : 0))).Trim();
            }
        }
    }
}
