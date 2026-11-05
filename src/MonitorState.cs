using System;
using System.Collections.Generic;

namespace MonitorDeck
{
    // Quello che sappiamo di un monitor: comandi supportati e ultimi valori letti.
    class MonitorState
    {
        public MonitorEntry Entry;
        public bool MainRead;
        public bool CapsTried, CapsOk, CapsLoading;
        public bool DetailsRead, DetailsLoading;
        public string Model, Mccs;
        public List<byte> Codes = new List<byte>();
        public Dictionary<byte, List<uint>> CapValues = new Dictionary<byte, List<uint>>();
        public readonly Dictionary<byte, VcpReading> Values = new Dictionary<byte, VcpReading>();
        readonly Dictionary<byte, DateTime> touched = new Dictionary<byte, DateTime>();

        public string Key { get { return Entry.Key; } }

        public string Title
        {
            get
            {
                if (Entry.GoodName) return Entry.Name;
                if (!string.IsNullOrEmpty(Model)) return Model;
                return Entry.Name;
            }
        }

        public void ApplyCaps(CapsInfo caps)
        {
            CapsTried = true;
            CapsOk = caps != null && caps.Codes.Count > 0;
            if (caps == null) return;
            Model = caps.Model;
            Mccs = caps.Mccs;
            Codes = caps.Codes;
            CapValues = caps.Values;
        }

        public bool ReadOk(byte code)
        {
            VcpReading r;
            return Values.TryGetValue(code, out r) && r.Ok;
        }

        public bool InCaps(byte code) { return CapsOk && Codes.Contains(code); }

        public bool Supports(byte code) { return InCaps(code) || ReadOk(code); }

        public int Cur(byte code)
        {
            VcpReading r;
            if (!Values.TryGetValue(code, out r) || !r.Ok) return -1;
            return code == Vcp.Input ? (int)(r.Cur & 0xFF) : (int)r.Cur;
        }

        public int Max(byte code)
        {
            VcpReading r;
            if (!Values.TryGetValue(code, out r) || !r.Ok || r.Max == 0) return 100;
            return (int)r.Max;
        }

        public VcpReading Reading(byte code)
        {
            VcpReading r;
            return Values.TryGetValue(code, out r) ? r : null;
        }

        public List<uint> ChoicesFor(byte code)
        {
            List<uint> l;
            if (CapValues.TryGetValue(code, out l) && l.Count > 0) return new List<uint>(l);
            VcpDef d = Vcp.Get(code);
            return d != null ? new List<uint>(d.Order) : new List<uint>();
        }

        public void SetLocal(byte code, uint v)
        {
            VcpReading r;
            if (!Values.TryGetValue(code, out r)) { r = new VcpReading(); r.Max = 100; Values[code] = r; }
            r.Cur = v;
            r.Ok = true;
            touched[code] = DateTime.Now;
        }

        // Valore cambiato dall'utente da poco: una lettura in corso non deve sovrascriverlo.
        public bool Recent(byte code)
        {
            DateTime t;
            return touched.TryGetValue(code, out t) && (DateTime.Now - t).TotalMilliseconds < 2500;
        }

        public void Merge(Dictionary<byte, VcpReading> res)
        {
            foreach (KeyValuePair<byte, VcpReading> kv in res)
            {
                if (Recent(kv.Key)) continue;
                VcpReading old;
                if (!kv.Value.Ok && Values.TryGetValue(kv.Key, out old) && old.Ok) continue; // un errore isolato non nasconde il controllo
                Values[kv.Key] = kv.Value;
            }
        }

        // Codici da leggere per il pannello "Tutti i controlli"
        public List<byte> DetailCodes()
        {
            List<byte> list = new List<byte>();
            if (CapsOk)
            {
                foreach (byte c in Codes)
                {
                    if (Vcp.IsMain(c)) continue;
                    VcpDef d = Vcp.Get(c);
                    if (d != null ? d.Kind != VcpKind.Action : Vcp.IsManufacturer(c)) list.Add(c);
                }
            }
            else
            {
                foreach (VcpDef d in Vcp.Ordered)
                    if (!Vcp.IsMain(d.Code) && d.Kind != VcpKind.Action) list.Add(d.Code);
            }
            if (!list.Contains(Vcp.Contrast) && !CapsOk) list.Add(Vcp.Contrast);
            return list;
        }
    }
}
