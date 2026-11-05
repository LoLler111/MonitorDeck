// Monitor Deck - controllo DDC/CI dei monitor (finestra + area di notifica)
// Compatibile con il compilatore C# 5 incluso in Windows (.NET Framework 4.x). Compila con build.bat

using System;
using System.Threading;
using System.Windows.Forms;

namespace MonitorDeck
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool updated = HasArg(args, "--updated");
            bool tray = HasArg(args, "--tray");
            Ddc.Demo = HasArg(args, "--demo");

            using (Mutex m = new Mutex(false, "MonitorDeck_SingleInstance_v1"))
            {
                bool owned;
                // Dopo un aggiornamento la versione vecchia sta ancora chiudendo: aspetto che liberi il posto.
                try { owned = m.WaitOne(updated ? 20000 : 0, false); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) { SignalExisting(); return; }

                try
                {
                    try { Native.SetProcessDPIAware(); } catch { }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Updater.CleanupOld();
                    Application.Run(new DeckApp(tray, updated));
                }
                finally
                {
                    try { m.ReleaseMutex(); } catch { }
                }
            }
        }

        static bool HasArg(string[] args, string a)
        {
            foreach (string s in args) if (string.Equals(s, a, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Un secondo avvio non apre un'altra copia: chiede a quella gia' attiva di mostrare la finestra.
        static void SignalExisting()
        {
            try
            {
                Native.AllowSetForegroundWindow(-1);
                using (EventWaitHandle e = EventWaitHandle.OpenExisting(DeckApp.ShowEventName)) e.Set();
            }
            catch { }
        }
    }
}
