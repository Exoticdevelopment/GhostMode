using System.IO;
using Microsoft.Win32;
using OptimizadorPC.Core;
using OptimizadorPC.Gaming;
using OptimizadorPC.Servicios;   // DiagnosticoEquipo.TipoDiscoDe, TipoDisco

namespace OptimizadorPC.Optimizaciones.FiveM
{
    // Dónde está algo del juego y en qué tipo de disco. Que = "FiveM" o "GTA V"
    public record UbicacionEnDisco(string Que, string Carpeta, char Unidad, TipoDisco Tipo);

    // Paso 7 del plan de docs/Investigacion-FiveM.pdf: avisar si FiveM o GTA V están en un disco duro (HDD).
    // FiveM carga del disco los autos, la ropa y los mapas de cada servidor: un HDD (~0,1–0,2 GB/s frente a
    // ~2–7 GB/s de un SSD NVMe) no llega a tiempo y aparecen texturas sin cargar y el disco al 100 %.
    // Solo lee: no cambia nada, así que no hay nada que revertir
    public static class DiscoJuego
    {
        public const string Aviso = "Tu disco es el cuello de botella: un SSD cambia más que cualquier ajuste.";

        // ===== Unidad =====
        // (El tipo de disco de una unidad lo lee DiagnosticoEquipo.TipoDiscoDe, el mismo que el del disco de Windows)

        // La letra de unidad de una ruta ("D:\Juegos\GTA V" → 'D'). null si es de red o relativa
        public static char? UnidadDe(string ruta)
        {
            if (!Path.IsPathFullyQualified(ruta))
            {
                return null;
            }
            string? raiz = Path.GetPathRoot(ruta);
            return raiz is { Length: >= 2 } && raiz[1] == ':' && char.IsAsciiLetter(raiz[0])
                ? char.ToUpperInvariant(raiz[0])
                : null;
        }


        // ===== Dónde está GTA V =====

        // La carpeta de GTA V que usa FiveM de verdad: IVPath en CitizenFX.ini (junto a FiveM.app).
        // Solo se LEE: la investigación desaconseja editar CitizenFX.ini a mano
        public static string? LeerIvPath(string archivoIni)
        {
            try
            {
                if (!File.Exists(archivoIni))
                {
                    return null;
                }
                bool enGame = false;
                foreach (string bruta in File.ReadLines(archivoIni))
                {
                    string linea = bruta.Trim();
                    if (linea.StartsWith('['))
                    {
                        enGame = linea.Equals("[Game]", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (enGame && linea.StartsWith("IVPath=", StringComparison.OrdinalIgnoreCase))
                    {
                        string ruta = linea["IVPath=".Length..].Trim();
                        return ruta.Length > 0 ? ruta : null;
                    }
                }
            }
            catch (IOException ex)
            {
                Logger.Aviso($"No se pudo leer CitizenFX.ini: {ex.Message}");
            }
            return null;
        }

        public static string ArchivoIni(string exeFiveM) =>
            Path.Combine(Path.GetDirectoryName(exeFiveM)!, "FiveM.app", "CitizenFX.ini");

        // 1. la de FiveM (CitizenFX.ini)  2. Rockstar Games Launcher  3. Steam o Epic. null si no aparece
        public static string? CarpetaGta(string? exeFiveM)
        {
            if (exeFiveM != null && LeerIvPath(ArchivoIni(exeFiveM)) is string deFiveM && Directory.Exists(deFiveM))
            {
                return deFiveM;
            }
            try
            {
                using RegistryKey? rockstar = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V");
                if (rockstar?.GetValue("InstallFolder") is string carpeta && Directory.Exists(carpeta))
                {
                    return carpeta;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                Logger.Aviso($"No se pudo leer el registro del Rockstar Games Launcher: {ex.Message}");
            }
            // Steam y Epic: el juego cuya carpeta tiene GTA5.exe
            return DetectorJuegos.Detectar()
                .Select(j => j.Carpeta)
                .FirstOrDefault(c => c != null && File.Exists(Path.Combine(c, "GTA5.exe")));
        }

        // El aviso que se muestra, o null si nada está en un HDD (en un SSD, o desconocido: no se avisa de lo que no se sabe)
        public static string? TextoAviso(IReadOnlyList<UbicacionEnDisco> ubicaciones)
        {
            var enHdd = ubicaciones.Where(u => u.Tipo == TipoDisco.Hdd).ToList();
            if (enHdd.Count == 0)
            {
                return null;
            }
            string quien = enHdd.Count == 1 ? $"{enHdd[0].Que} está" : $"{string.Join(" y ", enHdd.Select(u => u.Que))} están";
            string unidades = string.Join(", ", enHdd.Select(u => $"{u.Unidad}:").Distinct());
            return $"{quien} en un disco duro (HDD, unidad {unidades}). {Aviso} " +
                   "Con un HDD (unos 0,1–0,2 GB/s) los autos, la ropa y los mapas del servidor no cargan a tiempo: " +
                   "texturas que no aparecen, tirones y el disco al 100 %.";
        }

        // ===== Revisión completa =====

        // FiveM y GTA V, cada uno con su disco. Solo lo que existe de verdad
        public static List<UbicacionEnDisco> Revisar()
        {
            string? exeFiveM = UbicacionFiveM.BuscarExe();
            var carpetas = new List<(string Que, string Carpeta)>();
            if (exeFiveM != null)
            {
                carpetas.Add(("FiveM", Path.GetDirectoryName(exeFiveM)!));
            }
            if (CarpetaGta(exeFiveM) is string gta)
            {
                carpetas.Add(("GTA V", gta));
            }

            var tipos = new Dictionary<char, TipoDisco>();   // una consulta por unidad, aunque estén los dos en la misma
            var resultado = new List<UbicacionEnDisco>();
            foreach (var (que, carpeta) in carpetas)
            {
                if (UnidadDe(carpeta) is not char unidad)
                {
                    continue;
                }
                if (!tipos.TryGetValue(unidad, out TipoDisco tipo))
                {
                    tipo = DiagnosticoEquipo.TipoDiscoDe(unidad);
                    tipos[unidad] = tipo;
                }
                resultado.Add(new UbicacionEnDisco(que, carpeta, unidad, tipo));
            }
            return resultado;
        }
    }
}
