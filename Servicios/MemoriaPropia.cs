using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;   // DllImport
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // Cuando Ghost Mode se queda en la bandeja (sin ventana), devuelve a Windows la memoria que ya no usa:
    //   1. el recolector de .NET compacta y suelta lo que ocupaban las pantallas cerradas
    //   2. se le pide a Windows que saque de la RAM las páginas de Ghost Mode que no se están usando
    //      (SetProcessWorkingSetSize con -1, -1; solo afecta a ESTE proceso, nunca a otros programas).
    //      Si luego hacen falta, Windows las vuelve a traer solo.
    public static class MemoriaPropia
    {
        public static void Liberar(string motivo)
        {
            try
            {
                using Process yo = Process.GetCurrentProcess();
                long antes = yo.WorkingSet64;
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
                yo.Refresh();
                Logger.Info($"Memoria de Ghost Mode liberada ({motivo}): {antes / 1048576} → {yo.WorkingSet64 / 1048576} MB en uso");
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo liberar la memoria de Ghost Mode", ex);
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr proceso, nint minimo, nint maximo);
    }
}
