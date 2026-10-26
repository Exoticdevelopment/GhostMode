using System.IO;
using OptimizadorPC.Core;   // Logger

namespace OptimizadorPC.Limpieza
{
    // Lo que devuelve una limpieza: cuánto se liberó y cuántos archivos no se pudieron borrar (en uso)
    public record ResultadoLimpieza(long BytesLiberados, int ArchivosEnUso)
    {
        public static readonly ResultadoLimpieza Vacio = new(0, 0);

        // Para sumar resultados de varias carpetas:  total = total + resultado
        public static ResultadoLimpieza operator +(ResultadoLimpieza a, ResultadoLimpieza b) =>
            new(a.BytesLiberados + b.BytesLiberados, a.ArchivosEnUso + b.ArchivosEnUso);
    }

    // Mide y borra el CONTENIDO de una carpeta (la carpeta en sí se queda) con estas protecciones:
    // - no entra en "accesos directos" de carpeta (junctions/enlaces), que podrían apuntar fuera
    // - los archivos en uso se saltan sin error
    // - opcionalmente, no toca archivos modificados hace poco (un instalador puede estar usándolos)
    public static class BorradoSeguro
    {
        // Opciones para recorrer carpetas:
        // - RecurseSubdirectories: entrar también en las subcarpetas
        // - IgnoreInaccessible: saltar lo que no se puede leer en vez de fallar
        // - AttributesToSkip ReparsePoint: NO seguir enlaces, así nunca se borra nada fuera de la carpeta
        private static readonly EnumerationOptions Recorrido = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        // Bytes que se liberarían. antiguedadMinima = solo cuentan los archivos más viejos que eso
        public static long Medir(string carpeta, TimeSpan antiguedadMinima)
        {
            long total = 0;
            foreach (FileInfo archivo in Candidatos(carpeta, antiguedadMinima))
            {
                try
                {
                    total += archivo.Length;
                }
                catch (IOException)
                {
                    // Se borró mientras medíamos: no cuenta
                }
            }
            return total;
        }

        public static ResultadoLimpieza Borrar(string carpeta, TimeSpan antiguedadMinima)
        {
            long liberados = 0;
            int enUso = 0;

            foreach (FileInfo archivo in Candidatos(carpeta, antiguedadMinima))
            {
                try
                {
                    long tamano = archivo.Length;
                    archivo.Attributes = FileAttributes.Normal;   // por si es de solo lectura
                    archivo.Delete();
                    liberados += tamano;
                }
                // when = "solo atrapa estos dos tipos de error"; cualquier otro sí se avisa
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    enUso++;   // abierto por otro programa o protegido: se deja y seguimos
                }
            }
            BorrarCarpetasVacias(carpeta);

            Logger.Info($"Limpieza de {carpeta}: {liberados / 1048576} MB liberados, {enUso} archivos en uso sin borrar");
            return new ResultadoLimpieza(liberados, enUso);
        }

        // Los archivos de la carpeta que se pueden borrar.
        // yield return = va entregando los archivos de uno en uno, sin armar antes una lista enorme
        private static IEnumerable<FileInfo> Candidatos(string carpeta, TimeSpan antiguedadMinima)
        {
            var directorio = new DirectoryInfo(carpeta);
            // Si no existe, o si la propia carpeta es un enlace, no hay nada que hacer
            if (!directorio.Exists || directorio.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                yield break;
            }

            DateTime limite = DateTime.UtcNow - antiguedadMinima;
            // using = cerrar el recorrido al terminar, aunque quien nos llama pare antes de tiempo
            using IEnumerator<FileInfo> archivos = directorio.EnumerateFiles("*", Recorrido).GetEnumerator();
            while (true)
            {
                // Avanzar puede fallar si otro programa borra una subcarpeta mientras recorremos:
                // en ese caso paramos aquí con lo que llevemos.
                // (C# no deja poner "yield return" dentro de un try con catch: por eso avanzamos aquí
                // y entregamos el archivo fuera del try)
                try
                {
                    if (!archivos.MoveNext())
                    {
                        break;
                    }
                }
                catch (IOException)
                {
                    break;
                }

                if (archivos.Current.LastWriteTimeUtc <= limite)
                {
                    yield return archivos.Current;
                }
            }
        }

        // Borra las subcarpetas que quedaron vacías, de la más profunda a la menos.
        // Directory.Delete sin "recursive" solo borra carpetas vacías: si queda algo dentro, falla y se deja.
        private static void BorrarCarpetasVacias(string carpeta)
        {
            if (!Directory.Exists(carpeta))
            {
                return;
            }

            List<string> subcarpetas;
            try
            {
                subcarpetas = Directory.EnumerateDirectories(carpeta, "*", Recorrido)
                    .OrderByDescending(d => d.Length)   // las rutas más largas = las más profundas
                    .ToList();
            }
            catch (IOException)
            {
                return;
            }

            foreach (string sub in subcarpetas)
            {
                try
                {
                    Directory.Delete(sub);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // No está vacía (algo en uso) o protegida: se deja
                }
            }
        }
    }
}
