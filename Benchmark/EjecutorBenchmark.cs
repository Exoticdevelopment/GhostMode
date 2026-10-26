using OptimizadorPC.Core;
using OptimizadorPC.Servicios;   // AplicadorOptimizaciones

namespace OptimizadorPC.Benchmark
{
    // En qué paso va el benchmark (la pantalla lo muestra)
    public enum EtapaBenchmark
    {
        Descargando,       // bajando PresentMon (solo la primera vez)
        MidiendoAntes,     // 60 s "sin optimizar"
        Aplicando,         // aplicando Ghost Mode
        Asentando,         // unos segundos para que los cambios hagan efecto
        MidiendoDespues    // 60 s "con Ghost Mode"
    }

    // El flujo completo: medir → aplicar Ghost Mode → medir → guardar
    public static class EjecutorBenchmark
    {
        public const int SegundosPorMedicion = 60;
        private static readonly TimeSpan Asentar = TimeSpan.FromSeconds(5);

        // Las optimizaciones de Ghost Mode que aún no están activas: son las que se medirán.
        // Si no queda ninguna, no hay "antes y después" que comparar
        public static List<IOptimizacion> Pendientes() =>
            AplicadorOptimizaciones.GhostMode.Where(o => !EstaActivaSinFallar(o)).ToList();

        // progreso: la pantalla recibe la etapa en la que estamos
        public static async Task<ResultadoBenchmark> EjecutarAsync(
            int procesoId, string juego, List<IOptimizacion> pendientes,
            IProgress<EtapaBenchmark> progreso, CancellationToken cancelar)
        {
            if (!PresentMon.EstaDescargado)
            {
                progreso.Report(EtapaBenchmark.Descargando);
                await PresentMon.DescargarAsync(cancelar);
            }

            Logger.Info($"Benchmark de {juego} (proceso {procesoId}): {pendientes.Count} optimizaciones por aplicar");

            progreso.Report(EtapaBenchmark.MidiendoAntes);
            MedicionFps antes = await PresentMon.MedirAsync(procesoId, SegundosPorMedicion, cancelar);

            progreso.Report(EtapaBenchmark.Aplicando);
            // El punto de restauración ya se preparó antes de empezar: aquí no se le pregunta nada al usuario
            // (está jugando). Por eso "continuar sin punto" responde siempre que sí.
            ResultadoAplicacion aplicacion = await AplicadorOptimizaciones.AplicarAsync(pendientes, () => Task.FromResult(true));
            List<IOptimizacion> aplicadas = pendientes.Except(aplicacion.Fallidas).ToList();
            if (aplicadas.Count == 0)
            {
                throw new InvalidOperationException("No se pudo aplicar ninguna optimización. Revisa el log.");
            }

            progreso.Report(EtapaBenchmark.Asentando);
            await Task.Delay(Asentar, cancelar);

            progreso.Report(EtapaBenchmark.MidiendoDespues);
            MedicionFps despues = await PresentMon.MedirAsync(procesoId, SegundosPorMedicion, cancelar);

            var resultado = new ResultadoBenchmark(DateTime.Now, juego, antes, despues,
                                                   aplicadas.Select(o => o.Nombre).ToList());
            HistorialBenchmarks.Agregar(resultado);
            return resultado;
        }

        private static bool EstaActivaSinFallar(IOptimizacion optimizacion)
        {
            try
            {
                return optimizacion.EstaActiva();
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo leer el estado de {optimizacion.Id}", ex);
                return false;
            }
        }
    }
}
