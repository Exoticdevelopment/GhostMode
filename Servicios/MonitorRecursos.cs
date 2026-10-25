using System.Management;                 // WMI: contadores de la GPU
using System.Runtime.InteropServices;   // DllImport: llamar a funciones de Windows escritas en C
using System.Windows.Threading;         // DispatcherTimer
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // Una "foto" del uso de recursos en un momento. null = no se pudo medir (se muestra "—")
    public record MuestraRecursos(
        double? CpuPorcentaje,
        double? RamUsadaGb,
        double? RamLibreGb,
        double? RamPorcentaje,
        double? GpuPorcentaje);

    // Mide CPU, RAM y GPU cada 2 segundos y avisa con el evento Actualizado.
    // No es estático: cada pantalla crea el suyo, lo enciende al verse y lo apaga al salir,
    // así no gasta nada mientras nadie mira.
    public sealed class MonitorRecursos
    {
        public event Action<MuestraRecursos>? Actualizado;

        // DispatcherTimer = reloj que "suena" en el hilo de la interfaz
        private readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool midiendo = false;   // para no empezar una medición si la anterior no terminó

        // Para la CPU hacen falta DOS lecturas: el uso es la diferencia entre ambas
        private long? idleAnterior;
        private long? totalAnterior;

        // Si la GPU falla una vez (p. ej. Windows sin esos contadores), no lo volvemos a intentar
        private bool gpuDisponible = true;

        public MonitorRecursos()
        {
            reloj.Tick += async (s, e) => await MedirAsync();
        }

        public void Iniciar()
        {
            reloj.Start();
            _ = MedirAsync();   // una primera medición ya (_ = "no espero el resultado aquí")
        }

        public void Detener() => reloj.Stop();

        private async Task MedirAsync()
        {
            if (midiendo)
            {
                return;
            }
            midiendo = true;
            try
            {
                MuestraRecursos muestra = await Task.Run(Medir);   // en segundo plano
                Actualizado?.Invoke(muestra);                      // ya de vuelta en el hilo de la interfaz
            }
            finally
            {
                midiendo = false;
            }
        }

        private MuestraRecursos Medir()
        {
            double? cpu = MedirCpu();

            double? ramUsada = null, ramLibre = null, ramPorcentaje = null;
            var memoria = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memoria))
            {
                const double Gb = 1024.0 * 1024 * 1024;
                ramLibre = memoria.ullAvailPhys / Gb;
                ramUsada = (memoria.ullTotalPhys - memoria.ullAvailPhys) / Gb;
                ramPorcentaje = memoria.dwMemoryLoad;   // Windows ya lo da en %
            }

            return new MuestraRecursos(cpu, ramUsada, ramLibre, ramPorcentaje, MedirGpu());
        }

        // GetSystemTimes da el tiempo total que la CPU pasó ociosa, en el núcleo de Windows y en programas.
        // Uso = 1 - (ocioso / total) entre dos lecturas.
        private double? MedirCpu()
        {
            if (!GetSystemTimes(out long idle, out long kernel, out long user))
            {
                return null;
            }
            long total = kernel + user;   // "kernel" ya incluye el tiempo ocioso

            double? uso = null;
            if (idleAnterior != null && totalAnterior != null)
            {
                long difTotal = total - totalAnterior.Value;
                long difIdle = idle - idleAnterior.Value;
                if (difTotal > 0)
                {
                    uso = Math.Clamp(100.0 * (difTotal - difIdle) / difTotal, 0, 100);
                }
            }

            idleAnterior = idle;
            totalAnterior = total;
            return uso;   // la primera vez es null: todavía no hay con qué comparar
        }

        // Contadores "GPU Engine" de Windows: uso de cada motor de la GPU por proceso.
        // Sumamos los motores 3D (los que usan los juegos). Windows 10 1709 o más nuevo.
        private double? MedirGpu()
        {
            if (!gpuDisponible)
            {
                return null;
            }

            try
            {
                using var buscador = new ManagementObjectSearcher(
                    "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine " +
                    "WHERE Name LIKE '%engtype_3D'");
                double suma = buscador.Get().Cast<ManagementBaseObject>()
                                      .Sum(m => Convert.ToDouble(m["UtilizationPercentage"]));
                return Math.Min(suma, 100);   // varios procesos pueden sumar más de 100: lo limitamos
            }
            catch (Exception ex)
            {
                gpuDisponible = false;
                Logger.Aviso($"Contadores de GPU no disponibles: {ex.Message}");
                return null;
            }
        }

        // ===== Funciones de Windows (kernel32.dll) =====
        // DllImport = "esta función existe en esta DLL de Windows, llámala así".

        // Los tres tiempos llegan como FILETIME (64 bits); un long tiene el mismo tamaño.
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        // La estructura que rellena GlobalMemoryStatusEx. El orden de los campos debe ser EXACTO
        // al de Windows: por eso Sequential (en orden) y los nombres originales.
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;          // hay que rellenarlo con el tamaño de la estructura
            public uint dwMemoryLoad;      // % de memoria en uso
            public ulong ullTotalPhys;     // RAM total utilizable, en bytes
            public ulong ullAvailPhys;     // RAM libre
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
