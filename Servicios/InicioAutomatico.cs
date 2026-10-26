using System.Diagnostics;
using System.IO;
using System.Security;   // SecurityElement.Escape: texto seguro dentro del XML
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // "Iniciar con Windows (minimizado)" con una tarea del Programador de tareas:
    //   - se ejecuta al iniciar sesión el usuario actual, con privilegios máximos → no pide permiso
    //     de administrador (UAC) en cada arranque, cosa que una entrada en "Run" no puede evitar
    //   - abre Ghost Mode con --minimizado: directo a la bandeja, sin ventana
    //   - espera 20 s tras iniciar sesión para no competir con el arranque de Windows
    // Reversible: desactivarlo borra la tarea. Se crea y se borra con schtasks.exe (la herramienta de Windows).
    public static class InicioAutomatico
    {
        public const string NombrePorDefecto = "Ghost Mode (inicio con Windows)";
        public const string ArgumentoMinimizado = "--minimizado";

        public static bool Existe(string nombre = NombrePorDefecto) => Schtasks($"/Query /TN \"{nombre}\"", out _) == 0;

        // La ruta del .exe que abre la tarea (null si no existe)
        public static string? RutaConfigurada(string nombre = NombrePorDefecto)
        {
            if (Schtasks($"/Query /TN \"{nombre}\" /XML", out string xml) != 0)
            {
                return null;
            }
            Match m = Regex.Match(xml, "<Command>(.*?)</Command>");
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim('"') : null;
        }

        public static void Activar(string exe, string nombre = NombrePorDefecto)
        {
            string usuario = WindowsIdentity.GetCurrent().Name;   // EQUIPO\usuario
            string xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Author>Ghost Mode</Author>
                    <Description>Abre Ghost Mode minimizado en la bandeja al iniciar sesión. Se quita desde Ajustes de Ghost Mode.</Description>
                  </RegistrationInfo>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                      <UserId>{SecurityElement.Escape(usuario)}</UserId>
                      <Delay>PT20S</Delay>
                    </LogonTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Usuario">
                      <UserId>{SecurityElement.Escape(usuario)}</UserId>
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Actions Context="Usuario">
                    <Exec>
                      <Command>"{SecurityElement.Escape(exe)}"</Command>
                      <Arguments>{ArgumentoMinimizado}</Arguments>
                    </Exec>
                  </Actions>
                </Task>
                """;

            // schtasks lee la definición de un archivo XML en UTF-16
            string archivo = Path.Combine(Path.GetTempPath(), $"GhostModeTarea-{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(archivo, xml, Encoding.Unicode);
                if (Schtasks($"/Create /TN \"{nombre}\" /XML \"{archivo}\" /F", out string salida) != 0)
                {
                    throw new InvalidOperationException($"schtasks no pudo crear la tarea: {salida.Trim()}");
                }
            }
            finally
            {
                File.Delete(archivo);
            }
            Logger.Info($"Inicio con Windows activado (tarea «{nombre}» → {exe} {ArgumentoMinimizado})");
        }

        public static void Desactivar(string nombre = NombrePorDefecto)
        {
            if (!Existe(nombre))
            {
                return;
            }
            if (Schtasks($"/Delete /TN \"{nombre}\" /F", out string salida) != 0)
            {
                throw new InvalidOperationException($"schtasks no pudo borrar la tarea: {salida.Trim()}");
            }
            Logger.Info($"Inicio con Windows desactivado (tarea «{nombre}» borrada)");
        }

        // Si el .exe se movió de carpeta (p. ej. una versión nueva en otro sitio), la tarea apuntaría a la ruta
        // vieja: al arrancar se corrige sola
        public static void CorregirRuta(string exe, string nombre = NombrePorDefecto)
        {
            try
            {
                string? ruta = RutaConfigurada(nombre);
                if (ruta != null && !string.Equals(Path.GetFullPath(ruta), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Aviso($"La tarea de inicio apuntaba a {ruta}; se actualiza a {exe}");
                    Activar(exe, nombre);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo revisar la tarea de inicio con Windows", ex);
            }
        }

        private static int Schtasks(string argumentos, out string salida)
        {
            var configuracion = new ProcessStartInfo("schtasks", argumentos)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true            // sin ventana negra parpadeando
            };
            using var proceso = Process.Start(configuracion)!;
            Task<string> errores = proceso.StandardError.ReadToEndAsync();
            salida = proceso.StandardOutput.ReadToEnd();
            proceso.WaitForExit();
            salida += errores.Result;
            return proceso.ExitCode;
        }
    }
}
