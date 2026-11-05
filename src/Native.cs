using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MonitorDeck
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

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

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetCapabilitiesStringLength(IntPtr hMonitor, out uint length);

        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, [Out] StringBuilder caps, uint length);

        // ---- Nomi "veri" dei monitor (come in Impostazioni > Schermo)

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology;
            public uint rotation; public uint scaling; public uint refreshNum; public uint refreshDen;
            public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        public struct DISPLAYCONFIG_MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public int type; public int size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags; public uint outputTechnology;
            public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
        [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint numMode, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topology);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME req);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME req);

        // ---- Finestre

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 2;

        // 2 = angoli arrotondati, 3 = arrotondati piccoli (Windows 11). Attiva anche la cornice scura.
        public static void RoundCorners(IntPtr hwnd, int pref)
        {
            try
            {
                DwmSetWindowAttribute(hwnd, 33, ref pref, 4);
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
            }
            catch { }
        }

        public static void RoundCorners(IntPtr hwnd) { RoundCorners(hwnd, 2); }

        public static void DarkScrollbars(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
        }

        public static void DragWindow(IntPtr hwnd)
        {
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
    }
}
