using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonitorDeck
{
    // Cambio ingresso sui monitor LG che ignorano il comando standard (VCP 60): rispondono "ok"
    // ma restano dove sono. Accettano solo il comando del produttore F4 inviato sul canale di
    // servizio, cioe' con indirizzo sorgente 0x50 invece di 0x51, e l'API DDC/CI di Windows (dxva2)
    // non permette di cambiarlo. Con le schede NVIDIA si invia come scrittura I2C grezza via NvAPI.
    // Nota: una volta su un altro ingresso, il monitor non ascolta piu' la porta del PC, quindi per
    // tornare serve il tasto del monitor (o un comando dal dispositivo collegato all'altro ingresso).
    static class LgInput
    {
        // Monitor LG: l'EDID usa il codice produttore GSM (Goldstar)
        public static bool IsLg(MonitorEntry e)
        {
            return e != null && e.PnpId != null && e.PnpId.StartsWith("GSM", StringComparison.OrdinalIgnoreCase);
        }

        // Valore standard VCP 60 -> valore LG F4. Solo le porte con codice LG documentato.
        static bool Map(uint v60, out ushort lg)
        {
            switch (v60 & 0xFF)
            {
                case 0x0F: lg = 0xD0; return true;  // DisplayPort 1
                case 0x10: lg = 0xD1; return true;  // DisplayPort 2
                case 0x11: lg = 0x90; return true;  // HDMI 1
                case 0x12: lg = 0x91; return true;  // HDMI 2
            }
            lg = 0;
            return false;
        }

        // Da chiamare dal thread DDC, prima del comando standard. true = comando LG inviato.
        public static bool Set(MonitorEntry e, uint v60)
        {
            ushort lg;
            if (!IsLg(e) || string.IsNullOrEmpty(e.Device) || !Map(v60, out lg)) return false;
            Thread.Sleep(60); // pausa minima tra due comandi DDC/CI (prima e dopo)
            bool ok = NvI2c.SetVcp(e.Device, 0x50, 0xF4, lg);
            Thread.Sleep(60);
            return ok;
        }
    }

    // Scrittura DDC/CI su I2C tramite NvAPI (driver NVIDIA). Se il driver manca non fa nulla.
    static class NvI2c
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface64(uint id);
        [DllImport("nvapi.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface32(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DisplayIdByNameFn([MarshalAs(UnmanagedType.LPStr)] string name, out uint displayId);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GpuFromDisplayIdFn(uint displayId, out IntPtr gpu);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int I2cWriteFn(IntPtr gpu, ref NV_I2C_INFO info);

        // NV_I2C_INFO_V3
        [StructLayout(LayoutKind.Sequential)]
        struct NV_I2C_INFO
        {
            public uint version;
            public uint displayMask;
            public byte bIsDDCPort;
            public byte i2cDevAddress;
            public IntPtr pbI2cRegAddress;
            public uint regAddrSize;
            public IntPtr pbData;
            public uint cbSize;
            public uint i2cSpeed;
            public uint i2cSpeedKhz;
            public byte portId;
            public uint bIsPortIdSet;
        }

        static bool tried, ready;
        static DisplayIdByNameFn displayIdByName;
        static GpuFromDisplayIdFn gpuFromDisplayId;
        static I2cWriteFn i2cWrite;

        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = IntPtr.Size == 8 ? QueryInterface64(id) : QueryInterface32(id);
            return p == IntPtr.Zero ? null : (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        static bool Init()
        {
            if (tried) return ready;
            tried = true;
            try
            {
                InitFn init = Fn<InitFn>(0x0150E828);                       // NvAPI_Initialize
                displayIdByName = Fn<DisplayIdByNameFn>(0xAE457190);        // NvAPI_DISP_GetDisplayIdByDisplayName
                gpuFromDisplayId = Fn<GpuFromDisplayIdFn>(0x9EA74659);      // NvAPI_SYS_GetPhysicalGpuFromDisplayId
                i2cWrite = Fn<I2cWriteFn>(0xE812EB07);                      // NvAPI_I2CWrite
                ready = init != null && displayIdByName != null && gpuFromDisplayId != null && i2cWrite != null && init() == 0;
            }
            catch { ready = false; } // niente driver NVIDIA (nvapi64.dll assente)
            return ready;
        }

        // Set VCP DDC/CI verso il monitor collegato a "\\.\DISPLAYn"
        public static bool SetVcp(string gdiName, byte source, byte code, ushort value)
        {
            if (!Init()) return false;
            try
            {
                uint displayId;
                IntPtr gpu;
                if (displayIdByName(gdiName, out displayId) != 0) return false;
                if (gpuFromDisplayId(displayId, out gpu) != 0) return false;

                byte hi = (byte)(value >> 8), lo = (byte)value;
                // Pacchetto DDC/CI "Set VCP": lunghezza, comando 03, codice, valore; la checksum parte dall'indirizzo del monitor (6E)
                byte chk = (byte)(0x6E ^ source ^ 0x84 ^ 0x03 ^ code ^ hi ^ lo);
                byte[] reg = { source };
                byte[] data = { 0x84, 0x03, code, hi, lo, chk };
                GCHandle rh = GCHandle.Alloc(reg, GCHandleType.Pinned);
                GCHandle dh = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    NV_I2C_INFO info = new NV_I2C_INFO();
                    info.version = (uint)Marshal.SizeOf(typeof(NV_I2C_INFO)) | (3u << 16);
                    info.displayMask = displayId;
                    info.bIsDDCPort = 1;
                    info.i2cDevAddress = 0x6E;
                    info.pbI2cRegAddress = rh.AddrOfPinnedObject();
                    info.regAddrSize = 1;
                    info.pbData = dh.AddrOfPinnedObject();
                    info.cbSize = (uint)data.Length;
                    info.i2cSpeed = 0xFFFF;  // deprecato: va lasciato a 0xFFFF
                    info.i2cSpeedKhz = 4;    // NVAPI_I2C_SPEED_100KHZ
                    return i2cWrite(gpu, ref info) == 0;
                }
                finally { rh.Free(); dh.Free(); }
            }
            catch { return false; }
        }
    }
}
