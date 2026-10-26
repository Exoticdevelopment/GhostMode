using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // Activar/desactivar Ghost Mode (las optimizaciones seguras del botón de Inicio) desde la bandeja o el atajo.
    // El botón de Inicio pide confirmación; aquí no, porque el atajo y el menú ya son una orden explícita.
    // El punto de restauración se sigue creando (y, si falla, se pregunta) igual que desde Inicio.
    public static class InterruptorGhost
    {
        private static IReadOnlyList<IOptimizacion> Seguras => AplicadorOptimizaciones.GhostMode;
        private static bool ocupado = false;

        public static bool EstaActivo() => Seguras.Count > 0 && Seguras.All(EstaActivaSinFallar);

        public static async Task AlternarAsync(string origen)
        {
            if (ocupado)
            {
                return;   // doble pulsación del atajo mientras aplica: se ignora
            }
            ocupado = true;
            try
            {
                bool activo = await Task.Run(EstaActivo);
                Logger.Info($"Ghost Mode: {(activo ? "desactivar" : "activar")} desde {origen}");

                ResultadoAplicacion r = activo
                    ? await AplicadorOptimizaciones.RevertirAsync(Seguras)
                    : await AplicadorOptimizaciones.AplicarAsync(
                        await Task.Run(() => Seguras.Where(o => !EstaActivaSinFallar(o)).ToList()),
                        Avisos.PreguntarContinuarSinPuntoAsync);

                if (r.Cancelado)
                {
                    return;
                }
                if (!r.TodoBien)
                {
                    Avisos.Error(activo ? "No se pudo desactivar todo Ghost Mode" : "No se pudo activar todo Ghost Mode",
                                 $"Fallaron: {string.Join(", ", r.NombresFallidos)}. Revisa el log.");
                    return;
                }
                if (activo)
                {
                    Avisos.Exito("> ghost_mode: OFF", "Windows volvió a la configuración anterior.");
                }
                else
                {
                    Avisos.Exito("> ghost_mode: ON", $"{Seguras.Count} optimizaciones seguras aplicadas. Se deshacen con el mismo atajo.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Ghost Mode: falló al alternar desde {origen}", ex);
                Avisos.Error("No se pudo cambiar Ghost Mode", "Revisa el log para más detalles.");
            }
            finally
            {
                ocupado = false;
            }
        }

        private static bool EstaActivaSinFallar(IOptimizacion o)
        {
            try
            {
                return o.EstaActiva();
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo leer el estado de {o.Id}", ex);
                return false;
            }
        }
    }
}
