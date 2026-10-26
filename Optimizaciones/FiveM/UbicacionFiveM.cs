using System.Diagnostics;   // Process (ver qué programas están abiertos)
using System.IO;
using Microsoft.Win32;      // Registro de Windows
using OptimizadorPC.Core;   // Logger

namespace OptimizadorPC.Optimizaciones.FiveM
{
    // Dónde está FiveM en este PC y si está abierto.
    // FiveM se instala por usuario: FiveM.exe y, a su lado, la carpeta "FiveM.app" con todos sus datos.
    public static class UbicacionFiveM
    {
        // Devuelve la ruta completa de FiveM.exe, o null si no está instalado.
        // Se busca cada vez (no se guarda) porque el usuario puede instalarlo con la app abierta.
        public static string? BuscarExe()
        {
            // 1. Al instalarse, FiveM registra los enlaces "fivem://" (para unirse a servidores desde la web).
            //    Ese registro dice exactamente dónde está FiveM.exe, aunque se haya instalado en otra carpeta.
            //    El valor tiene esta forma:  "C:\Users\Ana\AppData\Local\FiveM\FiveM.exe" "%1"
            try
            {
                using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(@"Software\Classes\fivem\shell\open\command");
                if (clave?.GetValue(null) is string comando)   // null = el valor "(Predeterminado)"
                {
                    string ruta = PrimeraRutaEntreComillas(comando);
                    if (EsInstalacionValida(ruta))
                    {
                        return ruta;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Aviso($"No se pudo leer el registro de FiveM: {ex.Message}");
            }

            // 2. Si no, la carpeta donde se instala por defecto: %LocalAppData%\FiveM
            string porDefecto = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.exe");
            return EsInstalacionValida(porDefecto) ? porDefecto : null;
        }

        // La carpeta "data" de FiveM, donde están la caché y los ejecutables del juego
        public static string CarpetaDatos(string exe) =>
            Path.Combine(Path.GetDirectoryName(exe)!, "FiveM.app", "data");

        // Los programas que usan la GPU al jugar: FiveM.exe y el proceso del juego.
        // El juego se llama "FiveM_b3095_GTAProcess.exe" (el número cambia según la versión
        // que pida cada servidor), así que buscamos todos los que haya en la carpeta "subprocess".
        public static List<string> EjecutablesJuego(string exe)
        {
            var lista = new List<string> { exe };

            string subprocesos = Path.Combine(CarpetaDatos(exe), "cache", "subprocess");
            if (Directory.Exists(subprocesos))
            {
                lista.AddRange(Directory.GetFiles(subprocesos, "*GTAProcess.exe"));
            }
            return lista;
        }

        // ¿Hay algún proceso de FiveM abierto? (FiveM, FiveM_b3095_GTAProcess, FiveM_ChromeBrowser...)
        public static bool EstaEnEjecucion()
        {
            Process[] procesos = Process.GetProcesses();
            try
            {
                return procesos.Any(p => p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                // Cada Process guarda recursos de Windows: hay que liberarlos al terminar
                foreach (var p in procesos)
                {
                    p.Dispose();
                }
            }
        }

        // FiveM.exe tiene que existir Y tener al lado su carpeta FiveM.app
        private static bool EsInstalacionValida(string ruta) =>
            ruta.Length > 0 && File.Exists(ruta) &&
            Directory.Exists(Path.Combine(Path.GetDirectoryName(ruta)!, "FiveM.app"));

        // De  "C:\...\FiveM.exe" "%1"  saca  C:\...\FiveM.exe
        private static string PrimeraRutaEntreComillas(string comando)
        {
            comando = comando.Trim();
            if (comando.StartsWith('"'))
            {
                int cierre = comando.IndexOf('"', 1);
                return cierre > 1 ? comando[1..cierre] : "";   // [1..cierre] = el texto entre las dos comillas
            }
            // Sin comillas: hasta el primer espacio
            int espacio = comando.IndexOf(' ');
            return espacio > 0 ? comando[..espacio] : comando;
        }
    }
}
