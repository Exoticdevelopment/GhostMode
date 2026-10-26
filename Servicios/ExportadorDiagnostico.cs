using System.IO;
using System.IO.Compression;   // ZipArchive
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // "[ exportar diagnóstico ]": un .zip para que los beta testers lo manden cuando algo falla.
    // Lleva log.txt (y log.1.txt), estado.json, la configuración y un resumen del equipo.
    //
    // Sin datos personales: antes de meter cada archivo se reemplazan el nombre de usuario de Windows,
    // el nombre del equipo y las direcciones IP de la red local. No se incluye nada más del disco.
    public static class ExportadorDiagnostico
    {
        // Los archivos de C:\ProgramData\OptimizadorPC que van dentro (si existen)
        private static readonly string[] Archivos =
            ["log.txt", "log.1.txt", "estado.json", "ajustes.json", "modo_gaming.json", "benchmarks.json"];

        public static string NombreSugerido => $"GhostMode-diagnostico-{DateTime.Now:yyyy-MM-dd-HHmm}.zip";

        public static async Task CrearAsync(string destino, string? carpetaDatos = null)
        {
            carpetaDatos ??= Logger.Carpeta;
            string equipo = await ResumenEquipoAsync();

            await Task.Run(() =>
            {
                // Se arma en un temporal y se mueve al final: si algo falla, no queda un .zip a medias
                string temporal = destino + ".tmp";
                using (var zip = ZipFile.Open(temporal, ZipArchiveMode.Create))
                {
                    foreach (string nombre in Archivos)
                    {
                        string ruta = Path.Combine(carpetaDatos, nombre);
                        if (File.Exists(ruta))
                        {
                            Agregar(zip, nombre, Anonimizar(LeerCompartido(ruta)));
                        }
                    }
                    Agregar(zip, "equipo.txt", Anonimizar(equipo));
                }
                File.Move(temporal, destino, overwrite: true);
            });
            Logger.Info($"Diagnóstico exportado ({new FileInfo(destino).Length / 1024} KB)");
        }

        // Reemplaza lo que identifica a la persona o al equipo
        public static string Anonimizar(string texto)
        {
            foreach (string perfil in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
                                          .Where(p => p.Length > 3))
            {
                texto = texto.Replace(perfil, @"C:\Users\<usuario>", StringComparison.OrdinalIgnoreCase);
                texto = texto.Replace(perfil.Replace('\\', '/'), "C:/Users/<usuario>", StringComparison.OrdinalIgnoreCase);
                texto = texto.Replace(perfil.Replace(@"\", @"\\"), @"C:\\Users\\<usuario>", StringComparison.OrdinalIgnoreCase);
            }
            texto = ReemplazarPalabra(texto, Environment.UserName, "<usuario>");
            texto = ReemplazarPalabra(texto, Environment.MachineName, "<equipo>");
            // IP de la red de casa (192.168.x.x, 10.x.x.x, 172.16-31.x.x): no aportan nada al diagnóstico
            return Regex.Replace(texto, @"\b(192\.168|10\.\d{1,3}|172\.(1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}\b", "<ip-local>");
        }

        private static string ReemplazarPalabra(string texto, string palabra, string por) =>
            palabra.Length < 2 ? texto
                : Regex.Replace(texto, $@"(?<![\p{{L}}\d]){Regex.Escape(palabra)}(?![\p{{L}}\d])", por, RegexOptions.IgnoreCase);

        private static async Task<string> ResumenEquipoAsync()
        {
            var s = new StringBuilder();
            s.AppendLine($"Ghost Mode {Assembly.GetExecutingAssembly().GetName().Version}");
            s.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm}");
            s.AppendLine($"Windows: {Environment.OSVersion.Version} ({(Environment.Is64BitOperatingSystem ? "64" : "32")} bits)");
            try
            {
                DatosEquipo e = await InfoSistema.ObtenerAsync();
                Diagnostico d = await DiagnosticoEquipo.LeerAsync();
                s.AppendLine($"Equipo: {InfoSistema.Resumen(e)}");
                s.AppendLine($"CPU: {e.Cpu} · {e.Nucleos} núcleos · {Environment.ProcessorCount} hilos");
                s.AppendLine($"RAM: {e.RamGb} GB");
                s.AppendLine($"GPU: {d.Gpu} · VRAM {d.VramGb:0.#} GB");
                s.AppendLine($"Temperatura GPU (NVML): {(SensorTemperaturaGpu.Sistema.Disponible ? "disponible" : "no disponible")}");
                s.AppendLine($"Disco de Windows: {d.DiscoSistema} · {d.LibreGb:0} GB libres de {d.TotalGb:0} GB");
            }
            catch (Exception ex)
            {
                s.AppendLine($"(no se pudo leer el hardware: {ex.Message})");
                Logger.Error("Diagnóstico: no se pudo leer el hardware para el resumen", ex);
            }
            return s.ToString();
        }

        // El log puede estar abriéndose para escribir mientras se copia: se lee permitiendo eso
        private static string LeerCompartido(string ruta)
        {
            using var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var lector = new StreamReader(flujo, Encoding.UTF8);
            return lector.ReadToEnd();
        }

        private static void Agregar(ZipArchive zip, string nombre, string contenido)
        {
            ZipArchiveEntry entrada = zip.CreateEntry(nombre, CompressionLevel.Optimal);
            using var escritor = new StreamWriter(entrada.Open(), new UTF8Encoding(false));
            escritor.Write(contenido);
        }
    }
}
