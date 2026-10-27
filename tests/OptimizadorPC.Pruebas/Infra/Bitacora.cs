using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using OptimizadorPC.Core;
using OptimizadorPC.Servicios;

// Todas las pruebas pasan por RegistrarPrueba (Before/After), sin decorar cada clase
[assembly: OptimizadorPC.Pruebas.Infra.RegistrarPrueba]

namespace OptimizadorPC.Pruebas.Infra
{
    // Bitácora de cada ejecución de las pruebas, para cazar las intermitentes:
    //   tests\OptimizadorPC.Pruebas\TestResults\bitacora-AAAAMMDD-HHMMSS.log
    // Arriba, el entorno (¿Ghost Mode abierto? ¿activado de verdad? plan de energía); luego cada prueba con
    // su hora de inicio y su duración. El resultado (y el error y la traza de las que fallan) está en el
    // informe .trx de la misma carpeta (pruebas.runsettings). Cada inicio también va a log.txt como
    // "[pruebas] ▶ ...", para saber qué prueba escribió las líneas que vienen detrás
    public static class Bitacora
    {
        public static readonly string Carpeta = CarpetaResultados();
        public static readonly string Archivo = Path.Combine(Carpeta, $"bitacora-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        private static readonly object candado = new();
        private static bool conCabecera = false;

        public static void Escribir(string linea)
        {
            lock (candado)
            {
                if (!conCabecera)
                {
                    conCabecera = true;
                    Directory.CreateDirectory(Carpeta);
                    File.AppendAllText(Archivo, Cabecera());
                }
                File.AppendAllText(Archivo, $"{DateTime.Now:HH:mm:ss.fff}  {linea}{Environment.NewLine}");
            }
        }

        // Lo que más pesa en una intermitente: si la app real está abierta (usa el mismo registro y el mismo
        // plan de energía que las pruebas) y en qué estado estaba Windows al empezar
        private static string Cabecera()
        {
            var lineas = new List<string>
            {
                $"=== Pruebas de Ghost Mode, {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===",
                $"estado.json (aislado): {GestorEstado.RutaArchivo}",
                $"plan de energía al empezar: {Intentar(Ayudas.PlanActivo)}",
                $"Ghost Mode activado en Windows: {Intentar(() => InterruptorGhost.EstaActivo() ? "sí" : "no")}",
            };
            Process[] abiertas = Process.GetProcessesByName("OptimizadorPC");
            lineas.Add(abiertas.Length == 0
                ? "app Ghost Mode abierta: no"
                : $"app Ghost Mode abierta: SÍ (PID {string.Join(", ", abiertas.Select(p => p.Id))}). Si se usa mientras " +
                  "corren las pruebas, las dos tocan el mismo registro y el mismo plan de energía y pueden chocar");
            foreach (Process p in abiertas)
            {
                p.Dispose();
            }
            return string.Join(Environment.NewLine, lineas) + Environment.NewLine + Environment.NewLine;
        }

        private static string Intentar(Func<string> leer)
        {
            try
            {
                return leer();
            }
            catch (Exception ex)
            {
                return $"no se pudo leer ({ex.GetType().Name}: {ex.Message})";
            }
        }

        // tests\OptimizadorPC.Pruebas\TestResults, a partir de ESTE archivo fuente: vale aunque las pruebas
        // se compilen en otra carpeta
        private static string CarpetaResultados([CallerFilePath] string archivo = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivo)!, "..", "TestResults"));
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RegistrarPrueba : Xunit.Sdk.BeforeAfterTestAttribute
    {
        // Las pruebas no corren en paralelo (Infra/Sistema.cs): basta un reloj para la que está en marcha
        private static readonly Stopwatch reloj = new();

        public override void Before(MethodInfo metodo)
        {
            string nombre = Nombre(metodo);
            Bitacora.Escribir($"▶ {nombre}");
            Logger.Info($"[pruebas] ▶ {nombre}");
            reloj.Restart();
        }

        public override void After(MethodInfo metodo)
        {
            reloj.Stop();
            Bitacora.Escribir($"■ {Nombre(metodo)}  ({reloj.ElapsedMilliseconds} ms)");
        }

        private static string Nombre(MethodInfo metodo) => $"{metodo.DeclaringType?.Name}.{metodo.Name}";
    }
}
