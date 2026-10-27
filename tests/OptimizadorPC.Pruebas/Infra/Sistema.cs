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
    // estado.json AISLADO: antes de que corra cualquier prueba (ModuleInitializer = al cargar este .dll),
    // GestorEstado pasa a usar un estado.json propio en una carpeta temporal. Así las pruebas:
    //   - nunca leen los cambios reales del usuario (p. ej. Ghost Mode activado de verdad)
    //   - nunca los pisan ni los deshacen: «Deshacer todo» solo ve lo que apuntó la propia prueba
    // Al terminar el proceso de pruebas, la carpeta se borra
    public static class EstadoAislado
    {
        public static readonly string Carpeta =
            Path.Combine(Path.GetTempPath(), "GhostModePruebas-" + Guid.NewGuid().ToString("N"));

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Aislar()
        {
            Directory.CreateDirectory(Carpeta);
            GestorEstado.UsarArchivo(Path.Combine(Carpeta, "estado.json"));
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try
                {
                    Directory.Delete(Carpeta, recursive: true);
                }
                catch (IOException)
                {
                    // Algo lo tiene abierto: queda en %TEMP% y Windows lo limpia
                }
            };
        }

        // ¿GestorEstado está usando el estado aislado? Las pruebas que hacen algo peligroso
        // («Deshacer todo») lo comprueban antes, como cinturón de seguridad
        public static bool Activo =>
            Path.GetFullPath(GestorEstado.RutaArchivo).StartsWith(Path.GetFullPath(Carpeta), StringComparison.OrdinalIgnoreCase);

        // Un estado.json nuevo y vacío solo para una prueba (dentro de la carpeta aislada)
        public static void Nuevo() =>
            GestorEstado.UsarArchivo(Path.Combine(Carpeta, Guid.NewGuid().ToString("N"), "estado.json"));
    }

    // Todas las clases de prueba usan [Collection("Sistema")] y comparten este fixture
    [CollectionDefinition("Sistema")]
    public class ColeccionSistema : ICollectionFixture<EstadoSistemaFixture> { }

    public sealed class EstadoSistemaFixture : IDisposable
    {
        // El estado.json que usan las pruebas: el aislado, nunca el de C:\ProgramData
        public static string ArchivoEstado => GestorEstado.RutaArchivo;
        private static readonly string CarpetaHerramientas = Path.Combine(Logger.Carpeta, "herramientas");

        private readonly bool habiaHerramientas;

        public EstadoSistemaFixture()
        {
            habiaHerramientas = Directory.Exists(CarpetaHerramientas);
        }

        public void Dispose()
        {
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
        public static AjusteRegistro[] AjustesDe<T>() => AjustesDe(typeof(T));

        // Para clases estáticas (no se pueden usar como <T>)
        public static AjusteRegistro[] AjustesDe(Type tipo) =>
            (AjusteRegistro[])tipo.GetField("Ajustes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

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

        // ---------- Partir de "Windows sin Ghost Mode" ----------
        // Si el usuario tiene Ghost Mode activado, aplicar no cambiaría nada y la prueba no demostraría nada.
        // Estas ayudas dejan el punto de partida "sin aplicar" SOLO durante la prueba; quien las usa guarda
        // antes la foto real (PlanActivo, Foto) y la devuelve en su finally

        public const string AltoRendimiento = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string Equilibrado = "381b4222-f694-41f0-9685-ff5bb260df2e";

        // Si el plan activo es Alto rendimiento, cambia a otro que ya exista (Equilibrado si está; si no,
        // el primero distinto). Nunca crea ni borra planes. Devuelve el plan del que parte la prueba
        public static string PartirSinAltoRendimiento()
        {
            string actual = PlanActivo();
            if (actual != AltoRendimiento)
            {
                return actual;
            }
            List<string> planes = Planes();
            string otro = planes.Contains(Equilibrado) ? Equilibrado : planes.First(p => p != AltoRendimiento);
            Powercfg($"/setactive {otro}");
            Assert.Equal(otro, PlanActivo());
            return otro;
        }

        // Si los valores de registro de una optimización ya están puestos, los quita (solo el registro:
        // estas optimizaciones no avisan a Windows, así que no cambia nada en pantalla). RestaurarFoto los devuelve
        public static void PartirSinAplicar(IOptimizacion modulo, IEnumerable<AjusteRegistro> ajustes)
        {
            if (!modulo.EstaActiva())
            {
                return;
            }
            foreach (var a in ajustes)
            {
                using RegistryKey? k = Registry.CurrentUser.OpenSubKey(a.Ruta, writable: true);
                k?.DeleteValue(a.Nombre, throwOnMissingValue: false);
            }
            Assert.False(modulo.EstaActiva());
        }

        // Carpeta temporal propia de una prueba (dentro de %TEMP%, con un nombre que no choca con nada)
        public static string CarpetaTemporal()
        {
            string carpeta = Path.Combine(Path.GetTempPath(), "GhostModePrueba-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(carpeta);
            return carpeta;
        }
    }
}
