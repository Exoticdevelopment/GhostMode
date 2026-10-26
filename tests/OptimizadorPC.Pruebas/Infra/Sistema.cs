using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using OptimizadorPC.Core;

// Las pruebas cambian cosas reales de Windows (plan de energía, registro, estado.json):
// nunca deben correr dos a la vez
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OptimizadorPC.Pruebas.Infra
{
    // Todas las clases de prueba usan [Collection("Sistema")] y comparten este fixture:
    // al empezar guarda una copia exacta de estado.json y al terminar la deja como estaba
    [CollectionDefinition("Sistema")]
    public class ColeccionSistema : ICollectionFixture<EstadoSistemaFixture> { }

    public sealed class EstadoSistemaFixture : IDisposable
    {
        public static readonly string ArchivoEstado = Path.Combine(Logger.Carpeta, "estado.json");
        private static readonly string CarpetaHerramientas = Path.Combine(Logger.Carpeta, "herramientas");

        private readonly byte[]? estadoOriginal;
        private readonly bool habiaHerramientas;

        public EstadoSistemaFixture()
        {
            estadoOriginal = File.Exists(ArchivoEstado) ? File.ReadAllBytes(ArchivoEstado) : null;
            habiaHerramientas = Directory.Exists(CarpetaHerramientas);
        }

        public void Dispose()
        {
            if (estadoOriginal == null)
            {
                File.Delete(ArchivoEstado);
            }
            else
            {
                File.WriteAllBytes(ArchivoEstado, estadoOriginal);
            }
            Ayudas.RecargarGestorEstado();

            // PresentMon se descarga en las pruebas: si no estaba antes, se quita
            if (!habiaHerramientas && Directory.Exists(CarpetaHerramientas))
            {
                Directory.Delete(CarpetaHerramientas, recursive: true);
            }
        }
    }

    // Un valor del registro tal cual: si existe, su tipo y su contenido
    public record ValorRegistro(bool Existe, RegistryValueKind Tipo, string? Texto);

    public static class Ayudas
    {
        // GestorEstado lee estado.json una sola vez (campo estático). Para simular "cerrar y abrir la app"
        // volvemos a ejecutar su Cargar() privado y lo ponemos en su campo "datos"
        public static void RecargarGestorEstado()
        {
            Type tipo = typeof(GestorEstado);
            MethodInfo cargar = tipo.GetMethod("Cargar", BindingFlags.NonPublic | BindingFlags.Static)!;
            FieldInfo datos = tipo.GetField("datos", BindingFlags.NonPublic | BindingFlags.Static)!;
            datos.SetValue(null, cargar.Invoke(null, null));
        }

        // La lista privada "Ajustes" de un módulo: así las pruebas usan exactamente la de la app
        public static AjusteRegistro[] AjustesDe<T>() =>
            (AjusteRegistro[])typeof(T).GetField("Ajustes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        public static ValorRegistro LeerCrudo(string ruta, string nombre)
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(ruta);
            object? valor = clave?.GetValue(nombre, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (clave == null || valor == null)
            {
                return new ValorRegistro(false, RegistryValueKind.None, null);
            }
            string texto = valor switch
            {
                byte[] bytes => Convert.ToHexString(bytes),
                string[] lineas => "[" + string.Join("|", lineas) + "]",
                _ => valor.ToString()!
            };
            return new ValorRegistro(true, clave.GetValueKind(nombre), texto);
        }

        public static Dictionary<string, ValorRegistro> Foto(IEnumerable<AjusteRegistro> ajustes) =>
            ajustes.ToDictionary(a => $@"{a.Ruta}\{a.Nombre}", a => LeerCrudo(a.Ruta, a.Nombre));

        // Deja cada valor como en la foto (red de seguridad si una prueba falla a mitad)
        public static void RestaurarFoto(IEnumerable<AjusteRegistro> ajustes, Dictionary<string, ValorRegistro> foto)
        {
            foreach (var a in ajustes)
            {
                ValorRegistro antes = foto[$@"{a.Ruta}\{a.Nombre}"];
                if (LeerCrudo(a.Ruta, a.Nombre) == antes)
                {
                    continue;
                }
                if (!antes.Existe)
                {
                    using RegistryKey? k = Registry.CurrentUser.OpenSubKey(a.Ruta, writable: true);
                    k?.DeleteValue(a.Nombre, throwOnMissingValue: false);
                }
                else
                {
                    using RegistryKey k = Registry.CurrentUser.CreateSubKey(a.Ruta);
                    object dato = antes.Tipo switch
                    {
                        RegistryValueKind.DWord => int.Parse(antes.Texto!),
                        RegistryValueKind.QWord => long.Parse(antes.Texto!),
                        RegistryValueKind.Binary => Convert.FromHexString(antes.Texto!),
                        RegistryValueKind.MultiString when antes.Texto == "[]" => Array.Empty<string>(),
                        RegistryValueKind.MultiString => antes.Texto![1..^1].Split('|'),
                        _ => antes.Texto!
                    };
                    k.SetValue(a.Nombre, dato, antes.Tipo);
                }
            }
        }

        // ---------- powercfg ----------

        public static string Powercfg(string argumentos)
        {
            var configuracion = new ProcessStartInfo("powercfg", argumentos)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proceso = Process.Start(configuracion)!;
            string salida = proceso.StandardOutput.ReadToEnd();
            proceso.WaitForExit();
            return salida;
        }

        public static string PlanActivo() =>
            Regex.Match(Powercfg("/getactivescheme"), @"[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}").Value.ToLower();

        // Todos los planes que existen (para comprobar que no se crea ni se borra ninguno)
        public static List<string> Planes() =>
            Regex.Matches(Powercfg("/list"), @"[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}")
                 .Select(m => m.Value.ToLower()).Order().ToList();

        // Carpeta temporal propia de una prueba (dentro de %TEMP%, con un nombre que no choca con nada)
        public static string CarpetaTemporal()
        {
            string carpeta = Path.Combine(Path.GetTempPath(), "GhostModePrueba-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(carpeta);
            return carpeta;
        }
    }
}
