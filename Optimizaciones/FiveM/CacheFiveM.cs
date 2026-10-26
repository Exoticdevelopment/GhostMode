using System.IO;
using OptimizadorPC.Core;       // Logger
using OptimizadorPC.Limpieza;   // BorradoSeguro, ResultadoLimpieza

namespace OptimizadorPC.Optimizaciones.FiveM
{
    // La caché de FiveM: archivos descargados de los servidores (mapas, coches, scripts...).
    // Con el tiempo ocupa varios GB y, si se corrompe, causa tirones o cierres. FiveM la vuelve a descargar.
    //
    // NO es una IOptimizacion: borrar no se puede "revertir", es una limpieza puntual.
    // Nunca se tocan "cache\game" (archivos del juego, pesan mucho) ni "game-storage".
    // El borrado en sí (sin seguir enlaces, saltando lo que está en uso) lo hace Limpieza/BorradoSeguro.
    public static class CacheFiveM
    {
        // Carpetas dentro de FiveM.app\data que se limpian enteras
        private static readonly string[] CarpetasCompletas = { "server-cache", "server-cache-priv" };

        // Dentro de "data\cache" se limpia todo MENOS estas subcarpetas
        private static readonly string[] Conservar = { "game" };

        // Tamaño total en bytes de lo que se limpiaría.
        // TimeSpan.Zero = cuenta todo, sin importar cuándo se modificó (FiveM está cerrado al limpiar)
        public static long Medir(string exe) =>
            Objetivos(exe).Sum(c => BorradoSeguro.Medir(c, TimeSpan.Zero));

        public static ResultadoLimpieza Limpiar(string exe)
        {
            if (UbicacionFiveM.EstaEnEjecucion())
            {
                throw new InvalidOperationException("Cierra FiveM antes de limpiar la caché");
            }

            Logger.Info("Limpiando la caché de FiveM");
            var total = ResultadoLimpieza.Vacio;
            foreach (string carpeta in Objetivos(exe))
            {
                total += BorradoSeguro.Borrar(carpeta, TimeSpan.Zero);
            }
            return total;
        }

        // Las carpetas concretas que se limpian (solo las que existen)
        private static List<string> Objetivos(string exe)
        {
            string datos = UbicacionFiveM.CarpetaDatos(exe);
            var lista = CarpetasCompletas.Select(c => Path.Combine(datos, c)).Where(Directory.Exists).ToList();

            string cache = Path.Combine(datos, "cache");
            if (Directory.Exists(cache))
            {
                // Archivos sueltos de "cache" no hay; sus subcarpetas sí, menos las que se conservan
                lista.AddRange(Directory.GetDirectories(cache)
                    .Where(d => !Conservar.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)));
            }
            return lista;
        }
    }
}
