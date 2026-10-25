using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.Sistema;

namespace OptimizadorPC.Servicios
{
    // Lo que devuelve aplicar o revertir: si se canceló y cuáles fallaron
    public record ResultadoAplicacion(bool Cancelado, IReadOnlyList<IOptimizacion> Fallidas)
    {
        public bool TodoBien => !Cancelado && Fallidas.Count == 0;
    }

    // El "cerebro" que aplica y revierte. Lo usan el pie de la barra lateral,
    // la página Inicio y la página Optimizaciones: la lógica está en un solo lugar.
    public static class AplicadorOptimizaciones
    {
        // TODAS las optimizaciones de la app. Para agregar una nueva, basta una línea aquí
        public static IReadOnlyList<IOptimizacion> Todas { get; } = new List<IOptimizacion>
        {
            new PlanEnergia(),
            new GameMode(),
            new EfectosVisuales(),
        };

        // event = "aviso" al que otras partes se suscriben con +=.
        // Se dispara al terminar de aplicar o revertir, para que la pantalla se actualice sola.
        public static event Action? EstadoCambiado;

        // El punto de restauración se crea UNA vez por sesión, antes de lo primero que se aplique
        private static bool puntoRestauracionListo = false;

        // Candado: solo una operación a la vez (por si se pulsan dos botones seguidos).
        // SemaphoreSlim(1, 1) = deja pasar a uno; los demás esperan su turno.
        private static readonly SemaphoreSlim Candado = new(1, 1);

        // continuarSinPunto = función que la pantalla nos pasa para preguntarle al usuario
        // "¿seguir sin punto de restauración?". Así este archivo no necesita saber dibujar diálogos.
        public static async Task<ResultadoAplicacion> AplicarAsync(
            IEnumerable<IOptimizacion> lista,
            Func<Task<bool>> continuarSinPunto)
        {
            await Candado.WaitAsync();
            try
            {
                if (!await AsegurarPuntoRestauracionAsync(continuarSinPunto))
                {
                    return new ResultadoAplicacion(true, []);   // [] = lista vacía
                }

                var fallidas = new List<IOptimizacion>();
                foreach (var optimizacion in lista)
                {
                    try
                    {
                        // Task.Run = trabajar en segundo plano para que la ventana no se congele
                        await Task.Run(() =>
                        {
                            if (!optimizacion.EstaActiva())   // ya aplicada: nada que hacer
                            {
                                optimizacion.Aplicar();
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        // Si una falla, la anotamos y seguimos con las demás
                        fallidas.Add(optimizacion);
                        Logger.Error($"Falló al aplicar {optimizacion.Id}", ex);
                    }
                }
                return new ResultadoAplicacion(false, fallidas);
            }
            finally
            {
                Candado.Release();
                EstadoCambiado?.Invoke();   // ?. = solo si alguien está escuchando
            }
        }

        public static async Task<ResultadoAplicacion> RevertirAsync(IEnumerable<IOptimizacion> lista)
        {
            await Candado.WaitAsync();
            try
            {
                var fallidas = new List<IOptimizacion>();
                foreach (var optimizacion in lista)
                {
                    // Solo revertimos lo que la app cambió (lo que tiene "foto" guardada)
                    if (!GestorEstado.TieneCambios(optimizacion.Id))
                    {
                        continue;
                    }

                    try
                    {
                        await Task.Run(optimizacion.Revertir);
                    }
                    catch (Exception ex)
                    {
                        fallidas.Add(optimizacion);
                        Logger.Error($"Falló al revertir {optimizacion.Id}", ex);
                    }
                }
                return new ResultadoAplicacion(false, fallidas);
            }
            finally
            {
                Candado.Release();
                EstadoCambiado?.Invoke();
            }
        }

        public static Task<ResultadoAplicacion> DeshacerTodoAsync() => RevertirAsync(Todas);

        // Devuelve true si se puede seguir aplicando, false si el usuario canceló
        private static async Task<bool> AsegurarPuntoRestauracionAsync(Func<Task<bool>> continuarSinPunto)
        {
            if (puntoRestauracionListo)
            {
                return true;
            }

            // Este texto lo verá el usuario en "Restaurar sistema" de Windows
            ResultadoPunto resultado = await PuntoRestauracion.CrearAsync("Ghost Mode - antes de optimizar");

            if (resultado == ResultadoPunto.Fallo)
            {
                if (!await continuarSinPunto())
                {
                    Logger.Info("El usuario canceló al no poder crear el punto de restauración");
                    return false;
                }
                Logger.Aviso("El usuario decidió continuar sin punto de restauración");
            }

            puntoRestauracionListo = true;
            return true;
        }
    }
}
