using System.IO;

namespace OptimizadorPC.Limpieza
{
    // TODAS las categorías de la pantalla Limpieza, en el orden en que se muestran
    public static class CatalogoLimpieza
    {
        // Los archivos modificados en las últimas 24 h no se tocan: un instalador o un juego
        // pueden estar usándolos aunque Windows no los marque "en uso"
        private static readonly TimeSpan UnDia = TimeSpan.FromHours(24);

        // Las carpetas base de Windows. Environment.GetFolderPath las da para el usuario actual
        private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string LocalLow = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
        private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        public static IReadOnlyList<ICategoriaLimpieza> Todas { get; } = new List<ICategoriaLimpieza>
        {
            new CarpetasLimpieza("temp-usuario", "Temporales del usuario",
                "Archivos que dejan las apps y los instaladores al terminar.", true, UnDia,
                Path.GetTempPath()),   // %LocalAppData%\Temp

            new CarpetasLimpieza("temp-windows", "Temporales de Windows",
                "Archivos temporales del sistema que ya no se usan.", true, UnDia,
                Path.Combine(Windows, "Temp")),

            // Solo la carpeta "Download": son los paquetes ya descargados. Windows Update NO se detiene
            // ni se desactiva; si necesita algo de nuevo, lo vuelve a descargar.
            new CarpetasLimpieza("windows-update", "Caché de Windows Update",
                "Paquetes descargados de actualizaciones ya instaladas.", true, UnDia,
                Path.Combine(Windows, "SoftwareDistribution", "Download")),

            // Desmarcada por defecto: al borrarla, los juegos recompilan sus shaders y dan tirones al principio
            new CarpetasLimpieza("shaders", "Caché de shaders",
                "DirectX, NVIDIA y AMD. Se regenera al volver a abrir tus juegos (con tirones al principio).", false, UnDia,
                Path.Combine(Local, "D3DSCache"),                                  // DirectX
                Path.Combine(Local, "NVIDIA", "DXCache"),                          // NVIDIA (drivers antiguos)
                Path.Combine(Local, "NVIDIA", "GLCache"),
                Path.Combine(LocalLow, "NVIDIA", "PerDriverVersion", "DXCache"),   // NVIDIA (drivers nuevos)
                Path.Combine(LocalLow, "NVIDIA", "PerDriverVersion", "GLCache"),
                Path.Combine(ProgramData, "NVIDIA Corporation", "NV_Cache"),
                Path.Combine(Local, "AMD", "DxCache"),                             // AMD
                Path.Combine(Local, "AMD", "DxcCache"),
                Path.Combine(Local, "AMD", "GLCache"),
                Path.Combine(Local, "AMD", "VkCache")),

            new Papelera(),
        };
    }
}
