using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.FiveM;

namespace OptimizadorPC.Gaming
{
    // Busca los juegos instalados leyendo los archivos que dejan las propias tiendas:
    //   Steam: steamapps\libraryfolders.vdf (dónde están las bibliotecas) y un appmanifest_*.acf por juego
    //   Epic:  C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item (un JSON por juego)
    //   FiveM: lo mismo que usa su página (UbicacionFiveM)
    // Solo lee: no abre las tiendas ni toca nada.
    public static class DetectorJuegos
    {
        // Steamworks Common Redistributables: DirectX, Visual C++... no es un juego
        private const string AppIdRedistribuibles = "228980";

        // Herramientas de Steam que aparecen como "apps" pero no son juegos
        private static readonly string[] HerramientasSteam = ["Steamworks", "Proton", "Steam Linux Runtime", "SteamVR"];

        // .exe de una carpeta de juego que NO son el juego: instaladores, reporte de errores, antitrampas...
        private static readonly Regex NoEsElJuego = new(
            @"unins|setup|install|redist|dxsetup|directx|crash|report|helper|easyanticheat|battleye|_be\.exe$|_eac|" +
            @"prereq|dotnet|cefsharp|webhelper|updater|cleanup|uploader|launcherpatcher",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<Juego> Detectar()
        {
            var juegos = new List<Juego>();
            Probar("Steam", () => { if (CarpetaSteam() is string steam) { juegos.AddRange(JuegosSteam(steam)); } });
            Probar("Epic Games", () => juegos.AddRange(JuegosEpic(CarpetaManifiestosEpic)));
            Probar("FiveM", () => { if (JuegoFiveM() is Juego fivem) { juegos.Add(fivem); } });

            // DistinctBy = un juego por carpeta (por si dos tiendas apuntan al mismo sitio)
            List<Juego> unicos = juegos.DistinctBy(j => j.Id).OrderBy(j => j.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
            Logger.Info($"Modo gaming: {unicos.Count} juegos detectados" +
                        (unicos.Count > 0 ? $" ({string.Join(", ", unicos.Select(j => j.Nombre))})" : ""));
            return unicos;
        }

        // Una tienda que falla (archivo dañado, sin permisos) no impide ver las demás
        private static void Probar(string tienda, Action buscar)
        {
            try
            {
                buscar();
            }
            catch (Exception ex)
            {
                Logger.Aviso($"No se pudieron leer los juegos de {tienda}: {ex.Message}");
            }
        }

        // ===== Steam =====

        // Steam anota su carpeta en el registro con barras "/" (c:/program files (x86)/steam)
        public static string? CarpetaSteam()
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return clave?.GetValue("SteamPath") is string ruta && Directory.Exists(ruta) ? Path.GetFullPath(ruta) : null;
        }

        public static List<Juego> JuegosSteam(string carpetaSteam)
        {
            // La carpeta de Steam siempre es una biblioteca; libraryfolders.vdf dice si hay más (otros discos)
            var bibliotecas = new List<string> { carpetaSteam };
            string vdf = Path.Combine(carpetaSteam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                bibliotecas.AddRange(ValoresVdf(File.ReadAllText(vdf), "path"));
            }

            var juegos = new List<Juego>();
            foreach (string biblioteca in bibliotecas.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string steamapps = Path.Combine(biblioteca, "steamapps");
                if (!Directory.Exists(steamapps))
                {
                    continue;
                }
                foreach (string acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    string texto = File.ReadAllText(acf);
                    string? appId = ValoresVdf(texto, "appid").FirstOrDefault();
                    string? nombre = ValoresVdf(texto, "name").FirstOrDefault();
                    string? carpeta = ValoresVdf(texto, "installdir").FirstOrDefault();
                    if (appId == null || nombre == null || carpeta == null || appId == AppIdRedistribuibles ||
                        HerramientasSteam.Any(h => nombre.StartsWith(h, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string instalacion = Path.Combine(steamapps, "common", carpeta);
                    // Steam no anota qué .exe es el juego: se elige el más grande de la carpeta.
                    // Solo sirve para el ícono y la ruta que se muestra; para saber si está abierto se usa la carpeta
                    if (Directory.Exists(instalacion) && EjecutablePrincipal(instalacion) is string exe)
                    {
                        juegos.Add(new Juego(nombre, exe, instalacion, OrigenJuego.Steam));
                    }
                }
            }
            return juegos;
        }

        // Los .vdf/.acf de Steam son pares "clave" "valor" entre comillas, con llaves para anidar.
        // Las barras de las rutas van escapadas ("C:\\Juegos"). Devuelve los valores de esa clave, en orden
        public static IEnumerable<string> ValoresVdf(string texto, string clave)
        {
            foreach (Match m in Regex.Matches(texto, @"""((?:[^""\\]|\\.)*)""\s+""((?:[^""\\]|\\.)*)"""))
            {
                if (string.Equals(m.Groups[1].Value, clave, StringComparison.OrdinalIgnoreCase))
                {
                    yield return Regex.Unescape(m.Groups[2].Value);
                }
            }
        }

        // ===== Epic Games =====

        public static readonly string CarpetaManifiestosEpic = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        public static List<Juego> JuegosEpic(string carpetaManifiestos)
        {
            var juegos = new List<Juego>();
            if (!Directory.Exists(carpetaManifiestos))
            {
                return juegos;
            }

            foreach (string item in Directory.EnumerateFiles(carpetaManifiestos, "*.item"))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(item));
                JsonElement raiz = doc.RootElement;

                string? nombre = Texto(raiz, "DisplayName");
                string? carpeta = Texto(raiz, "InstallLocation");
                string? lanzador = Texto(raiz, "LaunchExecutable");
                bool incompleto = raiz.TryGetProperty("bIsIncompleteInstall", out JsonElement inc) && inc.ValueKind == JsonValueKind.True;
                // AppCategories: "games", "applications", "engines" (Unreal Engine)... solo juegos
                bool esJuego = !raiz.TryGetProperty("AppCategories", out JsonElement categorias) ||
                               categorias.ValueKind != JsonValueKind.Array ||
                               categorias.EnumerateArray().Any(c => c.GetString() == "games");

                if (nombre == null || carpeta == null || lanzador == null || incompleto || !esJuego)
                {
                    continue;
                }
                string exe = Path.GetFullPath(Path.Combine(carpeta, lanzador));
                if (File.Exists(exe))
                {
                    juegos.Add(new Juego(nombre, exe, Path.GetFullPath(carpeta), OrigenJuego.Epic));
                }
            }
            return juegos;
        }

        private static string? Texto(JsonElement raiz, string propiedad) =>
            raiz.TryGetProperty(propiedad, out JsonElement valor) && valor.ValueKind == JsonValueKind.String &&
            valor.GetString() is { Length: > 0 } texto ? texto : null;

        // ===== FiveM =====

        // Su carpeta (%LocalAppData%\FiveM) incluye FiveM.exe y el proceso del juego, que vive en FiveM.app
        public static Juego? JuegoFiveM() =>
            UbicacionFiveM.BuscarExe() is string exe
                ? new Juego("FiveM", exe, Path.GetDirectoryName(exe)!, OrigenJuego.FiveM)
                : null;

        // ===== Utilidades =====

        // El .exe más grande de la carpeta (hasta 3 niveles), sin contar instaladores y similares
        public static string? EjecutablePrincipal(string carpeta)
        {
            var opciones = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 3,
                IgnoreInaccessible = true
            };
            return Directory.EnumerateFiles(carpeta, "*.exe", opciones)
                .Where(exe => !NoEsElJuego.IsMatch(Path.GetFileName(exe)))
                .Select(exe => new FileInfo(exe))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }
    }
}
