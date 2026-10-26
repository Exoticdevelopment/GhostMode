using System.Diagnostics;               // Process: ejecutar PresentMon
using System.IO;
using System.Net.Http;                  // HttpClient: descargar de GitHub
using System.Security.AccessControl;    // Permisos de la carpeta
using System.Security.Cryptography;     // SHA256: comprobar que el archivo es el original
using System.Security.Principal;        // SecurityIdentifier: "Administradores", "Usuarios"...
using System.Text.Json;
using System.Text.RegularExpressions;
using OptimizadorPC.Core;               // Logger

namespace OptimizadorPC.Benchmark
{
    // PresentMon: herramienta oficial y gratuita de Intel (licencia MIT) que mide cada frame que dibuja
    // un juego. NO va dentro del repositorio: se descarga la primera vez desde su GitHub oficial.
    // https://github.com/GameTechDev/PresentMon
    public static class PresentMon
    {
        private const string UrlUltimaVersion = "https://api.github.com/repos/GameTechDev/PresentMon/releases/latest";

        // La versión de consola para 64 bits se llama así: "PresentMon-2.6.0-x64.exe"
        private static readonly Regex NombreArchivo = new(@"^PresentMon-[\d.]+-x64\.exe$", RegexOptions.IgnoreCase);

        // C:\ProgramData\OptimizadorPC\herramientas\PresentMon.exe
        private static readonly string Carpeta = Path.Combine(Logger.Carpeta, "herramientas");
        private static readonly string Exe = Path.Combine(Carpeta, "PresentMon.exe");

        public static bool EstaDescargado => File.Exists(Exe);

        // ===== Descargar =====

        public static async Task DescargarAsync(CancellationToken cancelar)
        {
            Logger.Info("Descargando PresentMon desde GitHub");
            PrepararCarpeta();

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            // GitHub rechaza las peticiones sin "User-Agent"
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostMode");

            // 1. Preguntar a GitHub cuál es la última versión y qué archivos trae
            string json = await http.GetStringAsync(UrlUltimaVersion, cancelar);
            using JsonDocument documento = JsonDocument.Parse(json);
            string version = documento.RootElement.GetProperty("tag_name").GetString() ?? "?";

            JsonElement? elegido = null;
            foreach (JsonElement archivo in documento.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (NombreArchivo.IsMatch(archivo.GetProperty("name").GetString() ?? ""))
                {
                    elegido = archivo;
                    break;
                }
            }
            if (elegido == null)
            {
                throw new InvalidOperationException($"La versión {version} de PresentMon no trae el .exe de consola para 64 bits");
            }

            // 2. La "huella" (SHA-256) que publica GitHub para ese archivo: "sha256:b2a706bc..."
            string huellaEsperada = elegido.Value.TryGetProperty("digest", out JsonElement digest)
                ? (digest.GetString() ?? "").Replace("sha256:", "", StringComparison.OrdinalIgnoreCase)
                : "";
            if (huellaEsperada.Length != 64)
            {
                // Sin huella no podemos comprobar que el archivo sea el original: mejor no usarlo
                throw new InvalidOperationException("GitHub no publicó la huella SHA-256 de PresentMon; no se puede verificar la descarga");
            }

            // 3. Descargar a un archivo temporal, comprobar la huella y solo entonces dejarlo como PresentMon.exe
            string url = elegido.Value.GetProperty("browser_download_url").GetString()!;
            string temporal = Exe + ".descarga";
            byte[] bytes = await http.GetByteArrayAsync(url, cancelar);
            string huella = Convert.ToHexString(SHA256.HashData(bytes));
            if (!huella.Equals(huellaEsperada, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La descarga de PresentMon no coincide con la huella de GitHub; se descartó");
            }

            await File.WriteAllBytesAsync(temporal, bytes, cancelar);
            File.Move(temporal, Exe, overwrite: true);
            Logger.Info($"PresentMon {version} descargado y verificado (SHA-256 {huella})");
        }

        // La carpeta "herramientas" solo la pueden modificar Administradores y el sistema.
        // Ghost Mode ejecuta PresentMon como administrador: si un usuario normal pudiera cambiar
        // el .exe, podría colar otro programa que se ejecutaría con permisos de administrador.
        private static void PrepararCarpeta()
        {
            var carpeta = Directory.CreateDirectory(Carpeta);

            var permisos = new DirectorySecurity();
            // true, false = no heredar los permisos de ProgramData (que dejan crear archivos a los usuarios)
            permisos.SetAccessRuleProtection(true, false);
            const InheritanceFlags Heredar = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            permisos.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, Heredar, PropagationFlags.None, AccessControlType.Allow));
            permisos.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, Heredar, PropagationFlags.None, AccessControlType.Allow));
            permisos.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute, Heredar, PropagationFlags.None, AccessControlType.Allow));
            carpeta.SetAccessControl(permisos);
        }

        // ===== Medir =====

        // Captura los frames del proceso durante "segundos" y devuelve el resultado ya calculado
        public static async Task<MedicionFps> MedirAsync(int procesoId, int segundos, CancellationToken cancelar)
        {
            if (!EstaDescargado)
            {
                throw new InvalidOperationException("PresentMon no está descargado");
            }

            string csv = Path.Combine(Carpeta, $"captura-{Guid.NewGuid():N}.csv");
            var configuracion = new ProcessStartInfo
            {
                FileName = Exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Opciones documentadas en README-ConsoleApplication.md del repositorio de PresentMon
            foreach (string argumento in new[]
            {
                "--process_id", procesoId.ToString(),   // solo el juego elegido
                "--output_file", csv,
                "--timed", segundos.ToString(),
                "--terminate_after_timed",              // cerrarse solo al acabar el tiempo
                "--terminate_on_proc_exit",             // ...o si el juego se cierra antes
                "--session_name", "GhostModeBenchmark",
                "--stop_existing_session",              // por si quedó una sesión colgada de otra vez
                "--no_console_stats"
            })
            {
                configuracion.ArgumentList.Add(argumento);
            }

            Logger.Info($"PresentMon: midiendo el proceso {procesoId} durante {segundos} s");
            using var proceso = Process.Start(configuracion)!;
            Task<string> salida = proceso.StandardOutput.ReadToEndAsync(cancelar);
            Task<string> errores = proceso.StandardError.ReadToEndAsync(cancelar);

            try
            {
                // Margen de 30 s sobre lo pedido: si PresentMon se cuelga, no esperamos para siempre
                using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
                limite.CancelAfter(TimeSpan.FromSeconds(segundos + 30));
                await proceso.WaitForExitAsync(limite.Token);
            }
            catch (OperationCanceledException)
            {
                // Cancelado por el usuario o colgado: cerrarlo y no dejar el CSV a medias
                if (!proceso.HasExited)
                {
                    proceso.Kill(entireProcessTree: true);
                }
                BorrarSinFallar(csv);
                throw;
            }

            string mensaje = $"{await salida}\n{await errores}".Trim();
            try
            {
                if (proceso.ExitCode != 0 || !File.Exists(csv))
                {
                    Logger.Error($"PresentMon terminó con código {proceso.ExitCode}: {mensaje}");
                    throw new InvalidOperationException($"PresentMon no pudo medir (código {proceso.ExitCode}). Revisa el log.");
                }

                MedicionFps medicion = AnalizadorFrames.Analizar(csv);
                // Si el juego se cerró a mitad, la captura dura mucho menos de lo pedido: no vale
                if (medicion.Segundos < segundos * 0.8)
                {
                    throw new InvalidOperationException(
                        $"La captura solo duró {medicion.Segundos:F0} s de {segundos}. ¿Se cerró o se pausó el juego?");
                }
                Logger.Info($"PresentMon: {medicion.Frames} frames, {medicion.FpsPromedio:F1} FPS promedio, " +
                            $"{medicion.Fps1Low:F1} FPS 1% low");
                return medicion;
            }
            finally
            {
                BorrarSinFallar(csv);   // el CSV ya no hace falta: los números quedan en benchmarks.json
            }
        }

        private static void BorrarSinFallar(string archivo)
        {
            try
            {
                File.Delete(archivo);
            }
            catch (IOException)
            {
                // Si no se puede borrar ahora, no pasa nada: es un archivo temporal
            }
        }
    }
}
