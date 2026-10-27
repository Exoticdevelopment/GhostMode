using System.IO;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.FiveM;

namespace OptimizadorPC.Servicios
{
    // Una línea de la secuencia de arranque: "[ ok ] nvml", "[ -- ] fivem no instalado"...
    public enum ResultadoComprobacion { Ok, NoDisponible, Fallo, Estado }

    public record LineaArranque(ResultadoComprobacion Resultado, string Texto)
    {
        public string Marca => Resultado switch
        {
            ResultadoComprobacion.Ok => "[ ok ] ",
            ResultadoComprobacion.NoDisponible => "[ -- ] ",
            ResultadoComprobacion.Fallo => "[fail] ",
            _ => "> "
        };
    }

    // Lo que la secuencia de arranque escribe. Cada línea sale de lo que la app DE VERDAD cargó o encontró:
    // nada inventado. Todas empiezan a la vez en segundo plano y cada una devuelve su línea al terminar.
    // No hacen trabajo extra: NVML y NVAPI se cargan una sola vez (Lazy) y la app las usa después igual
    public static class ComprobacionesArranque
    {
        public static IReadOnlyList<Task<LineaArranque>> Iniciar() => new[]
        {
            Comprobar("ajustes.json", Ajustes),
            Comprobar("estado.json", Estado),
            Comprobar("nvml", Nvml),
            Comprobar("nvapi", Nvapi),
            Comprobar("fivem", FiveM),
            Comprobar("ghost_mode", Ghost),
        };

        // En segundo plano y sin tumbar nada: si una comprobación falla, su línea dice "[fail]"
        private static Task<LineaArranque> Comprobar(string nombre, Func<LineaArranque> comprobacion) => Task.Run(() =>
        {
            try
            {
                return comprobacion();
            }
            catch (Exception ex)
            {
                Logger.Error($"Arranque: no se pudo comprobar {nombre}", ex);
                return new LineaArranque(ResultadoComprobacion.Fallo, nombre);
            }
        });

        private static LineaArranque Ajustes() => File.Exists(AjustesApp.ArchivoPorDefecto)
            ? new(ResultadoComprobacion.Ok, "ajustes.json")
            : new(ResultadoComprobacion.NoDisponible, "ajustes.json  valores por defecto");

        private static LineaArranque Estado()
        {
            if (GestorEstado.ArchivoDanado)
            {
                return new(ResultadoComprobacion.Fallo, "estado.json  dañado, se empieza vacío");
            }
            if (!GestorEstado.ArchivoExiste)
            {
                return new(ResultadoComprobacion.NoDisponible, "estado.json  sin cambios previos");
            }
            int n = GestorEstado.ModulosGuardados;
            return new(ResultadoComprobacion.Ok, n switch
            {
                0 => "estado.json",
                1 => "estado.json  1 cambio guardado",
                _ => $"estado.json  {n} cambios guardados"
            });
        }

        private static LineaArranque Nvml() => SensorTemperaturaGpu.Sistema.Disponible
            ? new(ResultadoComprobacion.Ok, "nvml")
            : new(ResultadoComprobacion.NoDisponible, "nvml  sin gpu nvidia");

        private static LineaArranque Nvapi() => PerfilNvidia.Sistema.Disponible
            ? new(ResultadoComprobacion.Ok, "nvapi")
            : new(ResultadoComprobacion.NoDisponible, "nvapi  sin gpu nvidia");

        private static LineaArranque FiveM() => UbicacionFiveM.BuscarExe() != null
            ? new(ResultadoComprobacion.Ok, "fivem")
            : new(ResultadoComprobacion.NoDisponible, "fivem no instalado");

        // La última: el estado real de Ghost Mode, leído de Windows
        private static LineaArranque Ghost() =>
            new(ResultadoComprobacion.Estado, InterruptorGhost.EstaActivo() ? "ghost_mode: on" : "ghost_mode: off");
    }
}
