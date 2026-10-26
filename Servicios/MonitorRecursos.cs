using System.Diagnostics;               // PerformanceCounter (disco, procesos), Process (consumo propio)
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
        double? GpuPorcentaje,
        // Solo con "detallado" (pantalla Monitor). "= null" = si no se indican, quedan vacíos
        double? DiscoPorcentaje = null,      // tiempo activo del disco, como en el Administrador de tareas
        double? DiscoLecturaMBs = null,
        double? DiscoEscrituraMBs = null,
        int? Procesos = null,
        double? AppRamMb = null,             // lo que consume el propio Ghost Mode
        double? AppCpuPorcentaje = null);

    // Mide CPU, RAM y GPU cada cierto tiempo (2 s por defecto) y avisa con el evento Actualizado.
    // Con "detallado" mide además disco, número de procesos y el consumo de la propia app.
    // No es estático: cada pantalla crea el suyo, lo enciende al verse y lo apaga al salir,
    // así no gasta nada mientras nadie mira.
    public sealed class MonitorRecursos
    {
        public event Action<MuestraRecursos>? Actualizado;

        // DispatcherTimer = reloj que "suena" en el hilo de la interfaz
        private readonly DispatcherTimer reloj = new();
        private readonly bool detallado;
        private bool midiendo = false;   // para no empezar una medición si la anterior no terminó

        // Para la CPU hacen falta DOS lecturas: el uso es la diferencia entre ambas
        private long? idleAnterior;
        private long? totalAnterior;

        // Si la GPU falla una vez (p. ej. Windows sin esos contadores), no lo volvemos a intentar
        private bool gpuDisponible = true;

        // Contadores de Windows para el disco y los procesos. Se crean la primera vez (son lentos de crear)
        // y, si fallan una vez, no se vuelven a intentar: se muestra "—"
        private PerformanceCounter? discoOcioso, discoLectura, discoEscritura, procesos;
        private bool contadoresDisponibles = true;

        // Para el % de CPU de la propia app: tiempo de CPU usado y hora de la lectura anterior
        private TimeSpan? cpuAppAnterior;
        private DateTime horaAnterior;

        // ?? = "si no me pasan intervalo, 2 segundos"
        public MonitorRecursos(TimeSpan? intervalo = null, bool detallado = false)
        {
            reloj.Interval = intervalo ?? TimeSpan.FromSeconds(2);
            this.detallado = detallado;
            reloj.Tick += async (s, e) => await MedirAsync();
        }

        public void Iniciar()
        {
            reloj.Start();
            _ = MedirAsync();   // una primera medición ya (_ = "no espero el resultado aquí")
        }

        public void Detener()
        {
            reloj.Stop();
            // Soltar los contadores de Windows: si se vuelve a Iniciar, se crean de nuevo
            foreach (PerformanceCounter? contador in new[] { discoOcioso, discoLectura, discoEscritura, procesos })
            {
                contador?.Dispose();
            }
            discoOcioso = discoLectura = discoEscritura = procesos = null;
        }

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

            var muestra = new MuestraRecursos(cpu, ramUsada, ramLibre, ramPorcentaje, MedirGpu());
            return detallado ? MedirDetalle(muestra) : muestra;
        }

        // Disco, procesos y consumo propio. "with" = copia de la muestra con estos datos añadidos
        private MuestraRecursos MedirDetalle(MuestraRecursos muestra)
        {
            var (appRam, appCpu) = MedirApp();
            muestra = muestra with { AppRamMb = appRam, AppCpuPorcentaje = appCpu };

            if (!contadoresDisponibles)
            {
                return muestra;
            }
            try
            {
                // "_Total" = todos los discos físicos juntos. Los nombres en inglés valen en cualquier idioma de Windows
                discoOcioso ??= new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", readOnly: true);
                discoLectura ??= new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", readOnly: true);
                discoEscritura ??= new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", readOnly: true);
                procesos ??= new PerformanceCounter("System", "Processes", readOnly: true);

                const double Mb = 1024.0 * 1024;
                return muestra with
                {
                    // Tiempo activo = 100 - tiempo ocioso. La primera lectura de cada contador da 0: es normal
                    DiscoPorcentaje = Math.Clamp(100 - discoOcioso.NextValue(), 0, 100),
                    DiscoLecturaMBs = discoLectura.NextValue() / Mb,
                    DiscoEscrituraMBs = discoEscritura.NextValue() / Mb,
                    Procesos = (int)procesos.NextValue()
                };
            }
            catch (ObjectDisposedException)
            {
                // Se llamó a Detener mientras medíamos: no es un fallo, la próxima vez se crean de nuevo
                return muestra;
            }
            catch (Exception ex)
            {
                contadoresDisponibles = false;
                Logger.Aviso($"Contadores de disco/procesos no disponibles: {ex.Message}");
                return muestra;
            }
        }

        // RAM y CPU de Ghost Mode (para el pie del Monitor: "Consumo de Ghost Mode: 38 MB RAM · <1 % CPU")
        private (double? RamMb, double? CpuPorcentaje) MedirApp()
        {
            using Process yo = Process.GetCurrentProcess();
            double ramMb = yo.WorkingSet64 / (1024.0 * 1024);

            TimeSpan cpuAhora = yo.TotalProcessorTime;
            DateTime ahora = DateTime.UtcNow;
            double? cpu = null;
            if (cpuAppAnterior != null)
            {
                // CPU usada / (tiempo pasado × núcleos) = parte de TODA la CPU que gastó la app
                double transcurrido = (ahora - horaAnterior).TotalMilliseconds * Environment.ProcessorCount;
                if (transcurrido > 0)
                {
                    cpu = Math.Clamp(100 * (cpuAhora - cpuAppAnterior.Value).TotalMilliseconds / transcurrido, 0, 100);
                }
            }
            cpuAppAnterior = cpuAhora;
            horaAnterior = ahora;
            return (ramMb, cpu);
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
