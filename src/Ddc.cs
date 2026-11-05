using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MonitorDeck
{
    class MonitorEntry
    {
        public IntPtr Handle;
        public string Key;          // identifica lo stesso monitor tra un'enumerazione e l'altra
        public string Name;
        public bool GoodName;       // false = nome generico ("Schermo 2")
        public string Device;
        public Rectangle Bounds;
        public bool Primary;
        public override string ToString() { return Name; }
    }

    class VcpReading
    {
        public bool Ok;
        public uint Cur, Max;
    }

    static class Ddc
    {
        static readonly List<Native.PHYSICAL_MONITOR[]> held = new List<Native.PHYSICAL_MONITOR[]>();

        // Modalita' dimostrativa (--demo): due monitor simulati, utile per provare l'interfaccia senza DDC/CI.
        public static bool Demo;
        static readonly Dictionary<long, uint> demoValues = new Dictionary<long, uint>();
        const string DemoCaps1 = "(prot(monitor)type(LCD)model(AW3423DWF)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08 10 12 14(01 05 08 0B 0C) 16 18 1A 52 60(0F 13 11) 62 8D(01 02) AC AE B2 B6 C6 C8 C9 CA(01 02) CC(02 03 04 05 0A) D6(01 04 05) DC(00 03 05 F0) DF E0 E1 E2(00 01) E3)mccs_ver(2.1)mswhql(1))";
        const string DemoCaps2 = "(prot(monitor)type(LCD)model(DELL U2720Q)vcp(02 04 05 08 10 12 14(05 08 0B) 60(0F 11 1B) AC AE B6 C6 C8 C9 D6(01 04 05) DF)mccs_ver(2.1))";

        static List<MonitorEntry> DemoEnumerate()
        {
            List<MonitorEntry> l = new List<MonitorEntry>();
            MonitorEntry a = new MonitorEntry();
            a.Handle = (IntPtr)1; a.Key = "demo1"; a.Name = "AW3423DWF"; a.GoodName = true; a.Primary = true;
            a.Bounds = new Rectangle(0, 0, 3440, 1440);
            MonitorEntry b = new MonitorEntry();
            b.Handle = (IntPtr)2; b.Key = "demo2"; b.Name = "DELL U2720Q"; b.GoodName = true;
            b.Bounds = new Rectangle(3440, 0, 3840, 2160);
            l.Add(a); l.Add(b);
            if (demoValues.Count == 0)
            {
                uint[] init = { 0x10, 70, 0x12, 75, 0x14, 5, 0x16, 100, 0x18, 98, 0x1A, 95, 0x60, 0x0F, 0x62, 35, 0x8D, 2,
                                0xC9, 0x0105, 0xCA, 2, 0xCC, 5, 0xD6, 1, 0xDC, 5, 0xDF, 0x0201, 0xE0, 40, 0xE1, 1, 0xC0, 1234 };
                for (int m = 1; m <= 2; m++)
                    for (int i = 0; i < init.Length; i += 2) demoValues[((long)m << 8) | init[i]] = init[i + 1];
            }
            return l;
        }

        static bool DemoSupports(IntPtr h, byte code)
        {
            CapsInfo c = CapsInfo.Parse(h == (IntPtr)1 ? DemoCaps1 : DemoCaps2);
            return c.Codes.Contains(code) || code == 0xC0;
        }

        // Da chiamare sempre dal thread DDC.
        public static List<MonitorEntry> Enumerate()
        {
            if (Demo) { Thread.Sleep(150); return DemoEnumerate(); }
            foreach (Native.PHYSICAL_MONITOR[] arr in held)
            {
                try { Native.DestroyPhysicalMonitors((uint)arr.Length, arr); } catch { }
            }
            held.Clear();

            Dictionary<string, string> friendly = FriendlyNames();

            List<IntPtr> hmons = new List<IntPtr>();
            Native.MonitorEnumProc proc = delegate(IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d) { hmons.Add(h); return true; };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);

            List<MonitorEntry> result = new List<MonitorEntry>();
            foreach (IntPtr hmon in hmons)
            {
                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(hmon, ref mi)) continue;

                uint count;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out count) || count == 0) continue;
                Native.PHYSICAL_MONITOR[] arr = new Native.PHYSICAL_MONITOR[count];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hmon, count, arr)) continue;
                held.Add(arr);

                string fname;
                friendly.TryGetValue(mi.szDevice ?? "", out fname);

                for (int i = 0; i < arr.Length; i++)
                {
                    Native.PHYSICAL_MONITOR pm = arr[i];
                    MonitorEntry e = new MonitorEntry();
                    e.Handle = pm.hPhysicalMonitor;
                    e.Device = mi.szDevice;
                    e.Bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                    e.Primary = (mi.dwFlags & 1) != 0;
                    e.Key = mi.szDevice + "#" + i + "|" + pm.szPhysicalMonitorDescription;
                    string desc = pm.szPhysicalMonitorDescription;
                    if (!string.IsNullOrEmpty(fname) && i == 0) { e.Name = fname; e.GoodName = true; }
                    else if (!string.IsNullOrEmpty(desc) && desc.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) < 0
                             && desc.IndexOf("generico", StringComparison.OrdinalIgnoreCase) < 0) { e.Name = desc; e.GoodName = true; }
                    result.Add(e);
                }
            }

            // Ordine da sinistra a destra, come sono disposti sulla scrivania
            result.Sort(delegate(MonitorEntry a, MonitorEntry b)
            {
                int c = a.Bounds.Left.CompareTo(b.Bounds.Left);
                return c != 0 ? c : a.Bounds.Top.CompareTo(b.Bounds.Top);
            });
            for (int i = 0; i < result.Count; i++)
                if (result[i].Name == null) result[i].Name = "Schermo " + (i + 1);
            return result;
        }

        // \\.\DISPLAY1 -> "AW3423DW"
        static Dictionary<string, string> FriendlyNames()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                uint np, nm;
                if (Native.GetDisplayConfigBufferSizes(2, out np, out nm) != 0) return map;
                Native.DISPLAYCONFIG_PATH_INFO[] paths = new Native.DISPLAYCONFIG_PATH_INFO[np];
                Native.DISPLAYCONFIG_MODE_INFO[] modes = new Native.DISPLAYCONFIG_MODE_INFO[nm];
                if (Native.QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return map;
                for (int i = 0; i < np; i++)
                {
                    Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME src = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    src.header.type = 1;
                    src.header.size = Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    src.header.adapterId = paths[i].sourceInfo.adapterId;
                    src.header.id = paths[i].sourceInfo.id;
                    if (Native.DisplayConfigGetDeviceInfo(ref src) != 0) continue;

                    Native.DISPLAYCONFIG_TARGET_DEVICE_NAME tgt = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    tgt.header.type = 2;
                    tgt.header.size = Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_TARGET_DEVICE_NAME));
                    tgt.header.adapterId = paths[i].targetInfo.adapterId;
                    tgt.header.id = paths[i].targetInfo.id;
                    if (Native.DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;

                    if (!string.IsNullOrEmpty(tgt.monitorFriendlyDeviceName) && !map.ContainsKey(src.viewGdiDeviceName))
                        map[src.viewGdiDeviceName] = tgt.monitorFriendlyDeviceName;
                }
            }
            catch { }
            return map;
        }

        public static VcpReading Read(IntPtr h, byte code)
        {
            VcpReading r = new VcpReading();
            uint cur, max;
            r.Ok = Get(h, code, out cur, out max);
            r.Cur = cur; r.Max = max;
            return r;
        }

        public static bool Get(IntPtr h, byte code, out uint cur, out uint max)
        {
            uint type;
            cur = 0; max = 0;
            if (h == IntPtr.Zero) return false;
            if (Demo)
            {
                Thread.Sleep(25);
                if (!DemoSupports(h, code)) return false;
                uint v;
                lock (demoValues) demoValues.TryGetValue(((long)h << 8) | code, out v);
                cur = v;
                max = Array.IndexOf(new byte[] { 0x60, 0x14, 0x8D, 0xC9, 0xDF, 0xCA, 0xCC, 0xD6, 0xDC, 0xE2 }, code) >= 0 ? 0u : 100u;
                if (code == 0xC0) { cur = 1234; max = 0; }
                return true;
            }
            for (int i = 0; i < 3; i++)
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
            if (Demo)
            {
                Thread.Sleep(50);
                lock (demoValues) demoValues[((long)h << 8) | code] = value;
                return true;
            }
            for (int i = 0; i < 3; i++)
            {
                try { if (Native.SetVCPFeature(h, code, value)) return true; }
                catch { return false; }
                Thread.Sleep(40);
            }
            return false;
        }

        // Stringa delle capacita' MCCS: elenca i comandi VCP supportati dal monitor (lenta, ~1 s).
        public static string GetCapabilities(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            if (Demo) { Thread.Sleep(600); return h == (IntPtr)1 ? DemoCaps1 : DemoCaps2; }
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    uint len;
                    if (Native.GetCapabilitiesStringLength(h, out len) && len > 0)
                    {
                        StringBuilder sb = new StringBuilder((int)len + 1);
                        if (Native.CapabilitiesRequestAndCapabilitiesReply(h, sb, len)) return sb.ToString();
                    }
                }
                catch { return null; }
                Thread.Sleep(100);
            }
            return null;
        }
    }

    // Tutte le chiamate DDC passano da un unico thread: non bloccano mai l'interfaccia.
    class DdcWorker
    {
        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        int pending;

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
                Interlocked.Decrement(ref pending);
            }
        }

        public bool IsIdle { get { return Thread.VolatileRead(ref pending) == 0; } }

        public void Post(Action a)
        {
            Interlocked.Increment(ref pending);
            queue.Add(a);
        }
    }
}
