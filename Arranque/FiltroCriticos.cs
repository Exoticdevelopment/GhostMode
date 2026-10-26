using System.IO;
using System.Text.RegularExpressions;   // Regex: buscar palabras completas
using System.Management;    // WMI: preguntar a Windows qué antivirus tiene registrados
using OptimizadorPC.Core;   // Logger

namespace OptimizadorPC.Arranque
{
    // Decide qué elementos de inicio NO se muestran nunca, para que no se puedan desactivar:
    // componentes de Windows y antivirus/firewall. Ante la duda, se oculta.
    public static class FiltroCriticos
    {
        private static readonly string CarpetaWindows =
            Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // Carpetas de Microsoft Defender y Seguridad de Windows
        private static readonly string[] CarpetasSeguridadWindows =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Security"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender"),
        };

        // Nombres de entradas de Windows conocidas (por si su ruta no se pudo leer)
        private static readonly string[] NombresWindows = { "SecurityHealth", "WindowsDefender", "Windows Defender" };

        // Red de seguridad extra: fabricantes de antivirus. Se busca en el editor y en la ruta.
        // \b = "borde de palabra": así "eset" encuentra "ESET" pero no "reset" ni "preset"
        private static readonly Regex Antivirus = new(
            @"\b(avast|avg|avira|bitdefender|eset|kaspersky|mcafee|norton|symantec|gen digital|sophos|" +
            @"trend micro|malwarebytes|panda security|f-secure|webroot|comodo|emsisoft|g data|quick heal|" +
            @"k7 computing|360 total security|windows defender)\b",
            RegexOptions.IgnoreCase);

        // Carpetas de los antivirus y firewalls que Windows tiene registrados. Se leen una vez (Lazy = la
        // primera vez que se piden), porque WMI tarda y no cambian mientras la app está abierta
        private static readonly Lazy<List<string>> CarpetasAntivirus = new(LeerCentroSeguridad);

        public static bool EsCritico(string nombre, string? exe, string editor)
        {
            if (NombresWindows.Contains(nombre, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Antivirus.IsMatch($"{editor} {exe}"))
            {
                return true;
            }

            if (exe == null)
            {
                return false;   // ruta desconocida: lo decide el nombre/editor de arriba
            }

            // Todo lo que arranca desde C:\Windows es parte del sistema (o de un driver)
            if (EstaDentro(exe, CarpetaWindows) || CarpetasSeguridadWindows.Any(c => EstaDentro(exe, c)))
            {
                return true;
            }

            return CarpetasAntivirus.Value.Any(c => EstaDentro(exe, c));
        }

        // ¿"ruta" está dentro de "carpeta"? (sin distinguir mayúsculas, como Windows)
        private static bool EstaDentro(string ruta, string carpeta) =>
            ruta.StartsWith(carpeta.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

        // El "Centro de seguridad" de Windows sabe qué antivirus y firewall hay instalados
        // (los mismos que muestra Seguridad de Windows). Nos quedamos con sus carpetas
        private static List<string> LeerCentroSeguridad()
        {
            var carpetas = new List<string>();
            foreach (string clase in new[] { "AntiVirusProduct", "FirewallProduct", "AntiSpywareProduct" })
            {
                try
                {
                    using var buscador = new ManagementObjectSearcher(@"root\SecurityCenter2",
                        $"SELECT pathToSignedProductExe, pathToSignedReportingExe FROM {clase}");
                    foreach (ManagementBaseObject producto in buscador.Get())
                    {
                        using (producto)
                        {
                            foreach (string propiedad in new[] { "pathToSignedProductExe", "pathToSignedReportingExe" })
                            {
                                // Vienen así: "%ProgramFiles%\AVAST Software\Avast\wsc_proxy.exe"
                                if (producto[propiedad] is string ruta && ruta.Length > 0)
                                {
                                    string? carpeta = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(ruta));
                                    if (!string.IsNullOrEmpty(carpeta))
                                    {
                                        carpetas.Add(carpeta);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // En Windows Server no existe SecurityCenter2: seguimos con las otras protecciones
                    Logger.Aviso($"No se pudo leer {clase} del Centro de seguridad: {ex.Message}");
                }
            }
            return carpetas;
        }
    }
}
