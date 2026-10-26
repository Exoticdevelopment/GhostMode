using System.Globalization;   // CultureInfo: formato de números en español ("2,40")
using System.Management;      // WMI: consultar el hardware de Windows
using System.Text.RegularExpressions;   // Regex: sacar el modelo corto de la CPU y la GPU
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // Datos del equipo que no cambian mientras la app está abierta.
    // "?" = puede quedar vacío (null) si Windows no lo informa: así nunca inventamos un dato.
    public record DatosEquipo(
        string? Cpu,         // "Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz"
        int? Nucleos,        // núcleos físicos (no hilos)
        double? Ghz,         // velocidad máxima
        int? RamGb,          // memoria instalada
        string? Gpu,         // "Intel(R) UHD Graphics 620"
        string Windows);     // "Windows 10" o "Windows 11"

    public static class InfoSistema
    {
        // Para mostrar números como en el diseño: "2,40 GHz" aunque Windows esté en inglés
        public static readonly CultureInfo Es = new("es-ES");

        // Guardamos la tarea: la primera llamada lee el hardware, las siguientes reciben lo ya leído
        private static Task<DatosEquipo>? tarea;

        // ??= = "si todavía es null, asígnale esto". Task.Run = leer en segundo plano
        public static Task<DatosEquipo> ObtenerAsync() => tarea ??= Task.Run(Leer);

        private static DatosEquipo Leer()
        {
            string? cpu = null;
            int? nucleos = null;
            double? ghz = null;
            int? ramGb = null;
            string? gpu = null;

            // Cada consulta en su propio try: si una falla, las demás siguen

            try
            {
                var procesadores = Consultar("SELECT Name, NumberOfCores, MaxClockSpeed FROM Win32_Processor");
                if (procesadores.Count > 0)
                {
                    cpu = procesadores[0]["Name"]?.ToString()?.Trim();
                    // Sum: si hay dos procesadores (raro en casa), se suman sus núcleos
                    nucleos = procesadores.Sum(p => Convert.ToInt32(p["NumberOfCores"]));
                    ghz = Convert.ToDouble(procesadores[0]["MaxClockSpeed"]) / 1000.0;   // viene en MHz
                }
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo leer la CPU", ex);
            }

            try
            {
                // Suma de los módulos de RAM instalados (en bytes)
                var modulos = Consultar("SELECT Capacity FROM Win32_PhysicalMemory");
                double bytes = modulos.Sum(m => Convert.ToDouble(m["Capacity"]));
                if (bytes > 0)
                {
                    ramGb = (int)Math.Round(bytes / (1024.0 * 1024 * 1024));   // bytes → GB
                }
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo leer la RAM", ex);
            }

            try
            {
                var nombres = Consultar("SELECT Name FROM Win32_VideoController")
                    .Select(v => v["Name"]?.ToString()?.Trim())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                // En portátiles suele haber dos: la integrada (Intel) y la dedicada. Preferimos la dedicada.
                gpu = nombres.FirstOrDefault(EsDedicada) ?? nombres.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo leer la GPU", ex);
            }

            // Windows 11 empieza en la compilación 22000 (los dos dicen "versión 10" por dentro)
            string windows = Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";

            var datos = new DatosEquipo(cpu, nucleos, ghz, ramGb, gpu, windows);
            Logger.Info($"equipo: {Resumen(datos)}");
            return datos;
        }

        // Una línea corta para el log: "i5-13420H · 8 núcleos · 16 GB · RTX 3050".
        // Lo que Windows no informó se omite (nunca se inventa)
        public static string Resumen(DatosEquipo d)
        {
            var partes = new List<string>();
            if (d.Cpu != null)
            {
                partes.Add(ModeloCorto(d.Cpu, ModelosCpu));
            }
            if (d.Nucleos != null)
            {
                partes.Add($"{d.Nucleos} núcleos");
            }
            if (d.RamGb != null)
            {
                partes.Add($"{d.RamGb} GB");
            }
            if (d.Gpu != null)
            {
                partes.Add(ModeloCorto(d.Gpu, ModelosGpu));
            }
            return partes.Count > 0 ? string.Join(" · ", partes) : "sin datos de hardware";
        }

        // El modelo dentro del nombre largo que da Windows:
        //   "13th Gen Intel(R) Core(TM) i5-13420H"         → "i5-13420H"
        //   "Intel(R) Core(TM) Ultra 7 155H"               → "Ultra 7 155H"
        //   "AMD Ryzen 5 5600X 6-Core Processor"           → "Ryzen 5 5600X"
        private static readonly Regex[] ModelosCpu =
        {
            new(@"\bi[3579]-\d{4,5}[A-Z]*\b"),
            new(@"\bUltra \d \d{3}[A-Z]*\b"),
            new(@"\bRyzen (Threadripper )?\d+ (PRO )?\d{4}[A-Z0-9]*\b"),
        };

        //   "NVIDIA GeForce RTX 3050 6GB Laptop GPU"       → "RTX 3050"
        //   "AMD Radeon RX 6600"                           → "RX 6600"
        //   "Intel(R) Arc(TM) A770 Graphics"               → "Arc A770" (tras quitar "(TM)")
        private static readonly Regex[] ModelosGpu =
        {
            new(@"\b(RTX|GTX|GT|MX|RX) ?[A-Z]?\d{3,4}\b"),
            new(@"\bArc [A-Z]\d{3}\b"),
        };

        // Si ningún patrón encaja ("Intel(R) UHD Graphics 620"), el nombre sin las marcas (R)/(TM)
        // y sin "CPU @ 2.40GHz": "Intel UHD Graphics 620"
        private static string ModeloCorto(string nombre, Regex[] patrones)
        {
            string limpio = Regex.Replace(nombre, @"\((R|TM)\)|\bCPU\b|@.*$", "", RegexOptions.IgnoreCase);
            limpio = Regex.Replace(limpio, @"\s+", " ").Trim();

            foreach (Regex patron in patrones)
            {
                Match m = patron.Match(limpio);
                if (m.Success)
                {
                    return m.Value;
                }
            }
            return limpio;
        }

        private static bool EsDedicada(string? nombre)
        {
            if (nombre == null)
            {
                return false;
            }
            string n = nombre.ToUpperInvariant();   // comparar sin importar mayúsculas
            return n.Contains("NVIDIA") || n.Contains("GEFORCE") || n.Contains("RADEON") || n.Contains("AMD");
        }

        // Ejecuta una consulta WMI y devuelve los resultados como lista
        private static List<ManagementBaseObject> Consultar(string consulta)
        {
            using var buscador = new ManagementObjectSearcher(consulta);
            return buscador.Get().Cast<ManagementBaseObject>().ToList();
        }
    }
}
