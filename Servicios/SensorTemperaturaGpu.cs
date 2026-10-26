using System.IO;
using System.Runtime.InteropServices;   // NativeLibrary, Marshal: usar una DLL de C sin referencias fijas
using OptimizadorPC.Core;               // Logger

namespace OptimizadorPC.Servicios
{
    // Cómo de caliente está la GPU, para el color de la barra
    public enum NivelTemperatura
    {
        Normal,   // menos de 75 °C
        Alta,     // de 75 a 85 °C: ámbar
        Critica   // más de 85 °C: rojo
    }

    // Temperatura de la GPU NVIDIA con NVML: la librería oficial de NVIDIA que viene con su driver
    // (nvml.dll; es la misma que usa nvidia-smi). Sin drivers extra ni programas de terceros.
    // Si no hay GPU NVIDIA, no está la DLL o algo falla, Leer() devuelve null y la app sigue como si nada.
    //
    // NVML se carga "a mano" (NativeLibrary) y no con [DllImport]: así, si la DLL no existe,
    // no salta un error al llamar, simplemente no hay temperatura.
    public sealed class SensorTemperaturaGpu
    {
        public const double LimiteAlta = 75;
        public const double LimiteCritica = 85;

        // Dónde instala NVIDIA la DLL: System32 (drivers actuales) o NVSMI (drivers antiguos).
        // Siempre con la ruta completa: si se pidiera solo "nvml.dll", Windows también la buscaría
        // en la carpeta de la app, y alguien podría dejar ahí una falsa que correría como administrador
        public static readonly string[] RutasOficiales =
        {
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        };

        // El que usa la app. Lazy = se crea la primera vez que se pide
        private static readonly Lazy<SensorTemperaturaGpu> sistema = new(() => new SensorTemperaturaGpu(RutasOficiales));
        public static SensorTemperaturaGpu Sistema => sistema.Value;

        // ---------- Funciones de nvml.dll (nvml.h) ----------
        // Todas devuelven un código: 0 = NVML_SUCCESS
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlShutdown();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetCount(out uint cantidad);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetHandleByIndex(uint indice, out IntPtr dispositivo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetTemperature(IntPtr dispositivo, int sensor, out uint grados);

        private const int NvmlTemperaturaGpu = 0;   // NVML_TEMPERATURE_GPU: el sensor del chip

        private readonly IReadOnlyList<string> rutas;
        private readonly object candado = new();   // la pueden leer dos pantallas a la vez
        private bool iniciado = false;              // ya se intentó cargar (bien o mal): no se repite
        private NvmlDeviceGetTemperature? leerTemperatura;
        private IntPtr dispositivo;

        // rutas = dónde buscar nvml.dll (la app usa RutasOficiales; las pruebas, otras)
        public SensorTemperaturaGpu(IEnumerable<string> rutas)
        {
            this.rutas = rutas.ToList();
        }

        // ¿Se pudo cargar NVML y hay una GPU NVIDIA?
        public bool Disponible
        {
            get
            {
                lock (candado)
                {
                    Iniciar();
                    return leerTemperatura != null;
                }
            }
        }

        // Grados °C de la primera GPU NVIDIA, o null si no se puede leer
        public double? Leer()
        {
            lock (candado)
            {
                Iniciar();
                if (leerTemperatura == null)
                {
                    return null;
                }
                try
                {
                    // Si falla una lectura suelta, esa vez no hay dato; la siguiente se vuelve a intentar
                    return leerTemperatura(dispositivo, NvmlTemperaturaGpu, out uint grados) == 0 ? grados : null;
                }
                catch (Exception ex)
                {
                    Logger.Aviso($"NVML: no se pudo leer la temperatura de la GPU: {ex.Message}");
                    return null;
                }
            }
        }

        public static NivelTemperatura Clasificar(double grados) =>
            grados > LimiteCritica ? NivelTemperatura.Critica
            : grados >= LimiteAlta ? NivelTemperatura.Alta
            : NivelTemperatura.Normal;

        // Carga la DLL, busca las funciones, arranca NVML y toma la primera GPU. Solo una vez
        private void Iniciar()
        {
            if (iniciado)
            {
                return;
            }
            iniciado = true;

            string? ruta = rutas.FirstOrDefault(File.Exists);
            if (ruta == null)
            {
                Logger.Info("Temperatura de GPU: no hay nvml.dll (sin GPU NVIDIA o sin su driver)");
                return;
            }

            IntPtr libreria = IntPtr.Zero;
            try
            {
                if (!NativeLibrary.TryLoad(ruta, out libreria))
                {
                    Logger.Aviso($"Temperatura de GPU: no se pudo cargar {ruta}");
                    return;
                }

                var iniciar = Funcion<NvmlInit>(libreria, "nvmlInit_v2");
                var cerrar = Funcion<NvmlShutdown>(libreria, "nvmlShutdown");
                var contar = Funcion<NvmlDeviceGetCount>(libreria, "nvmlDeviceGetCount_v2");
                var obtener = Funcion<NvmlDeviceGetHandleByIndex>(libreria, "nvmlDeviceGetHandleByIndex_v2");
                var temperatura = Funcion<NvmlDeviceGetTemperature>(libreria, "nvmlDeviceGetTemperature");
                if (iniciar == null || cerrar == null || contar == null || obtener == null || temperatura == null)
                {
                    Logger.Aviso($"Temperatura de GPU: {ruta} no es una NVML válida (faltan funciones)");
                    return;
                }

                int codigo = iniciar();
                if (codigo != 0)
                {
                    Logger.Aviso($"Temperatura de GPU: NVML no arrancó (código {codigo})");
                    return;
                }
                if (contar(out uint cantidad) != 0 || cantidad == 0 || obtener(0, out IntPtr gpu) != 0)
                {
                    Logger.Aviso("Temperatura de GPU: NVML no encontró ninguna GPU NVIDIA");
                    cerrar();
                    return;
                }

                dispositivo = gpu;
                leerTemperatura = temperatura;
                // Al cerrar la app, avisamos a NVML (lo pide su documentación)
                AppDomain.CurrentDomain.ProcessExit += (s, e) => cerrar();
                Logger.Info("Temperatura de GPU: leyendo con NVML (driver de NVIDIA)");
            }
            catch (Exception ex)
            {
                // P. ej. una DLL dañada o de otra arquitectura: sin temperatura, pero la app sigue
                Logger.Aviso($"Temperatura de GPU no disponible: {ex.Message}");
                leerTemperatura = null;
            }
            finally
            {
                // Si al final no se usa, la DLL se descarga
                if (leerTemperatura == null && libreria != IntPtr.Zero)
                {
                    NativeLibrary.Free(libreria);
                }
            }
        }

        // La función "nombre" de la DLL como un método de C#, o null si la DLL no la tiene
        private static T? Funcion<T>(IntPtr libreria, string nombre) where T : Delegate =>
            NativeLibrary.TryGetExport(libreria, nombre, out IntPtr puntero)
                ? Marshal.GetDelegateForFunctionPointer<T>(puntero)
                : null;
    }
}
