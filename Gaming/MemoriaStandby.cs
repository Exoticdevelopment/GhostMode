using System.Management;                 // WMI: contadores de memoria
using System.Runtime.InteropServices;   // DllImport
using OptimizadorPC.Core;

namespace OptimizadorPC.Gaming
{
    // Memoria en espera ("standby") = archivos que Windows dejó en RAM por si se vuelven a usar.
    // Windows la reparte sola cuando un programa pide memoria, así que normalmente NO hace falta tocarla.
    // Solo ayuda cuando casi no queda memoria libre de verdad y Windows tarda en reciclar la de espera
    // (pequeños tirones al cargar). Por eso se libera ÚNICAMENTE si la libre es baja.
    //
    // Se usa la misma llamada de Windows que RAMMap (Sysinternals, de Microsoft): "vaciar la lista en espera".
    // No borra datos de nadie: solo descarta copias en caché que se pueden volver a leer del disco.
    public static class MemoriaStandby
    {
        public const long Mb = 1024L * 1024;

        // Mismos umbrales que usan las herramientas conocidas (p. ej. ISLC):
        // menos de 1 GB libre y más de 1 GB en espera que recuperar
        public const long LibreMinima = 1024 * Mb;
        public const long EsperaMinima = 1024 * Mb;

        public record EstadoMemoria(long LibreBytes, long EsperaBytes);

        public static bool HaceFalta(EstadoMemoria m) => m.LibreBytes < LibreMinima && m.EsperaBytes > EsperaMinima;

        // Libre = páginas libres + puestas a cero (lo que el Administrador de tareas llama "Libre" en Memoria)
        public static EstadoMemoria? Leer()
        {
            try
            {
                using var buscador = new ManagementObjectSearcher(
                    "SELECT FreeAndZeroPageListBytes, StandbyCacheCoreBytes, StandbyCacheNormalPriorityBytes, " +
                    "StandbyCacheReserveBytes FROM Win32_PerfRawData_PerfOS_Memory");
                foreach (ManagementObject m in buscador.Get().Cast<ManagementObject>())
                {
                    using (m)
                    {
                        long Valor(string nombre) => Convert.ToInt64(m[nombre] ?? 0L);
                        return new EstadoMemoria(
                            Valor("FreeAndZeroPageListBytes"),
                            Valor("StandbyCacheCoreBytes") + Valor("StandbyCacheNormalPriorityBytes") + Valor("StandbyCacheReserveBytes"));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Aviso($"No se pudo leer la memoria en espera: {ex.Message}");
            }
            return null;
        }

        // true si se liberó; false si no hacía falta o no se pudo
        public static bool LiberarSiHaceFalta()
        {
            if (Leer() is not EstadoMemoria antes)
            {
                return false;
            }
            if (!HaceFalta(antes))
            {
                Logger.Info($"Modo gaming: memoria en espera sin tocar ({antes.LibreBytes / Mb} MB libres, no hace falta)");
                return false;
            }

            bool hecho = Liberar();
            EstadoMemoria? despues = Leer();
            Logger.Info(hecho
                ? $"Modo gaming: memoria en espera liberada ({antes.LibreBytes / Mb} → {despues?.LibreBytes / Mb} MB libres)"
                : "Modo gaming: Windows no permitió liberar la memoria en espera");
            return hecho;
        }

        public static bool Liberar()
        {
            // Hace falta el privilegio "perfilar un solo proceso" (lo tienen los administradores, pero apagado)
            if (!ActivarPrivilegio("SeProfileSingleProcessPrivilege"))
            {
                return false;
            }
            int orden = MemoryPurgeStandbyList;
            return NtSetSystemInformation(SystemMemoryListInformation, ref orden, sizeof(int)) == 0;
        }

        private static bool ActivarPrivilegio(string nombre)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
            {
                return false;
            }
            try
            {
                if (!LookupPrivilegeValue(null, nombre, out long luid))
                {
                    return false;
                }
                var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                // AdjustTokenPrivileges devuelve true aunque no tenga el privilegio: hay que mirar el último error
                return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) &&
                       Marshal.GetLastWin32Error() == 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }

        // ===== Funciones de Windows =====

        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
        private const uint TOKEN_QUERY = 0x8;
        private const uint SE_PRIVILEGE_ENABLED = 0x2;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int clase, ref int datos, int largo);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr proceso, uint acceso, out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string? sistema, string nombre, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool desactivarTodos, ref TOKEN_PRIVILEGES nuevo,
                                                         int largo, IntPtr anterior, IntPtr largoAnterior);
    }
}
