using System.IO;
using OptimizadorPC.Core;   // Logger

namespace OptimizadorPC.Optimizaciones.FiveM
{
    // Lo que devuelve la limpieza: cuánto se liberó y cuántos archivos no se pudieron borrar (en uso)
    public record ResultadoLimpieza(long BytesLiberados, int ArchivosEnUso);

    // La caché de FiveM: archivos descargados de los servidores (mapas, coches, scripts...).
    // Con el tiempo ocupa varios GB y, si se corrompe, causa tirones o cierres. FiveM la vuelve a descargar.
    //
    // NO es una IOptimizacion: borrar no se puede "revertir", es una limpieza puntual.
    // Nunca se tocan "cache\game" (archivos del juego, pesan mucho) ni "game-storage".
    public static class CacheFiveM
    {
        // Carpetas dentro de FiveM.app\data que se limpian enteras
        private static readonly string[] CarpetasCompletas = { "server-cache", "server-cache-priv" };

        // Dentro de "data\cache" se limpia todo MENOS estas subcarpetas
        private static readonly string[] Conservar = { "game" };

        // Opciones para recorrer carpetas:
        // - RecurseSubdirectories: entrar también en las subcarpetas
        // - IgnoreInaccessible: saltar lo que no se puede leer en vez de fallar
        // - AttributesToSkip ReparsePoint: NO entrar en "accesos directos" de carpeta (junctions/enlaces),
        //   que podrían apuntar fuera de FiveM. Así nunca se borra nada fuera de la caché.
        private static readonly EnumerationOptions Recorrido = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        // Tamaño total en bytes de lo que se limpiaría
        public static long Medir(string exe) =>
            Objetivos(exe).Sum(TamanoCarpeta);

        public static ResultadoLimpieza Limpiar(string exe)
        {
            if (UbicacionFiveM.EstaEnEjecucion())
            {
                throw new InvalidOperationException("Cierra FiveM antes de limpiar la caché");
            }

            Logger.Info("Limpiando la caché de FiveM");
            long liberados = 0;
            int enUso = 0;

            foreach (string carpeta in Objetivos(exe))
            {
                foreach (string archivo in Directory.EnumerateFiles(carpeta, "*", Recorrido))
                {
                    try
                    {
                        long tamano = new FileInfo(archivo).Length;
                        File.SetAttributes(archivo, FileAttributes.Normal);   // por si es de solo lectura
                        File.Delete(archivo);
                        liberados += tamano;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        enUso++;   // abierto por otro programa: se deja y seguimos
                    }
                }
                BorrarCarpetasVacias(carpeta);
            }

            Logger.Info($"Caché de FiveM: {liberados / 1048576} MB liberados, {enUso} archivos en uso sin borrar");
            return new ResultadoLimpieza(liberados, enUso);
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
                    .Where(d => !Conservar.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                    .Where(d => !new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)));
            }
            return lista;
        }

        private static long TamanoCarpeta(string carpeta)
        {
            long total = 0;
            foreach (string archivo in Directory.EnumerateFiles(carpeta, "*", Recorrido))
            {
                try
                {
                    total += new FileInfo(archivo).Length;
                }
                catch (IOException)
                {
                    // Se borró mientras medíamos: no cuenta
                }
            }
            return total;
        }

        // Borra las subcarpetas que quedaron vacías, de la más profunda a la menos.
        // Directory.Delete sin "recursive" solo borra carpetas vacías: si queda algo dentro, falla y se deja.
        private static void BorrarCarpetasVacias(string carpeta)
        {
            var subcarpetas = Directory.EnumerateDirectories(carpeta, "*", Recorrido)
                .OrderByDescending(d => d.Length);   // las rutas más largas = las más profundas
            foreach (string sub in subcarpetas)
            {
                try
                {
                    Directory.Delete(sub);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // No está vacía (algo en uso): se deja
                }
            }
        }
    }
}
