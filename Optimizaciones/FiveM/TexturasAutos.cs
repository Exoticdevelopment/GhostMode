using System.IO;
using System.Text.RegularExpressions;
using OptimizadorPC.Core;

namespace OptimizadorPC.Optimizaciones.FiveM
{
    // str_maxVehicleTextureRes (paso 4 del plan de docs/Investigacion-FiveM.pdf): el ajuste oficial de FiveM que
    // limita la resolución de las texturas de los autos (por defecto 1024). Bajarlo reduce la memoria usada a
    // cambio de calidad: la palanca más directa contra la "ciudad derretida" en servidores con muchos autos.
    //
    // Ghost Mode NO lo cambia: la pantalla fivem muestra el comando para pegarlo en la consola F8, y lee
    // fivem.cfg (sin escribirlo nunca) para enseñar el valor que FiveM tiene guardado.
    // "seta" = variable archivada: FiveM la guarda en %AppData%\CitizenFX\fivem.cfg (docs de Cfx.re)
    public static class TexturasAutos
    {
        public const string Variable = "str_maxVehicleTextureRes";
        public const int PorDefecto = 1024;
        public const int Recomendado = 512;

        public const string ComandoRecomendado = "seta str_maxVehicleTextureRes 512";
        public const string ComandoPorDefecto = "seta str_maxVehicleTextureRes 1024";   // para deshacerlo

        public static readonly string ArchivoCfg = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX", "fivem.cfg");

        // FiveM escribe: seta "str_maxVehicleTextureRes" "1024"   (con o sin comillas; set o seta)
        private static readonly Regex Linea = new(
            @"^\s*seta?\s+""?str_maxVehicleTextureRes""?\s+""?(\d+)""?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // El valor guardado en fivem.cfg, o null si el archivo o la línea no están
        public static int? LeerValor(string archivoCfg)
        {
            try
            {
                if (!File.Exists(archivoCfg))
                {
                    return null;
                }
                // La última que aparece es la que vale, como al ejecutar el archivo de arriba abajo
                Match? ultima = Linea.Matches(File.ReadAllText(archivoCfg)).LastOrDefault();
                return ultima != null && int.TryParse(ultima.Groups[1].Value, out int valor) ? valor : null;
            }
            catch (IOException ex)
            {
                Logger.Aviso($"No se pudo leer fivem.cfg: {ex.Message}");
                return null;
            }
        }
    }
}
