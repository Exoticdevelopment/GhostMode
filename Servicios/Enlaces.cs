using System.Diagnostics;   // Process: abrir otro programa
using OptimizadorPC.Core;   // Logger

namespace OptimizadorPC.Servicios
{
    // Abre enlaces web en el navegador predeterminado.
    //
    // OJO: Ghost Mode se ejecuta como ADMINISTRADOR. Si abriéramos la URL directamente, el navegador
    // también se abriría como administrador (mala idea: una web tendría un navegador con permisos de más).
    // Por eso se la pasamos a explorer.exe: el Explorador que ya está abierto (sin permisos de administrador)
    // es quien abre el navegador, con los permisos normales del usuario.
    public static class Enlaces
    {
        public static void Abrir(string url)
        {
            // Solo direcciones web: nunca abrir otra cosa (un archivo, un programa) por error
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Aviso($"Enlace ignorado (no es https): {url}");
                return;
            }

            try
            {
                Process.Start("explorer.exe", url)?.Dispose();
                Logger.Info($"Abriendo en el navegador: {url}");
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo abrir {url}", ex);
                Avisos.Error("No se pudo abrir el enlace", url);
            }
        }
    }
}
