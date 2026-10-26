using OptimizadorPC.Arranque;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Optimizaciones.Sistema;

namespace OptimizadorPC.Servicios
{
    // Lo que devuelve aplicar o revertir: si se canceló y cuáles fallaron.
    // OtrosFallos = cosas que no son IOptimizacion (p. ej. "Programas de inicio") y no se pudieron deshacer
    public record ResultadoAplicacion(bool Cancelado, IReadOnlyList<IOptimizacion> Fallidas,
                                      IReadOnlyList<string>? OtrosFallos = null)
    {
        public bool TodoBien => !Cancelado && Fallidas.Count == 0 && (OtrosFallos?.Count ?? 0) == 0;

        // Los nombres de todo lo que falló, para mostrarlos juntos
        public IEnumerable<string> NombresFallidos => Fallidas.Select(o => o.Nombre).Concat(OtrosFallos ?? []);
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
            new GpuDedicadaFiveM(),   // Categoría FiveM: solo aparece en la página FiveM
        };

        // "Ghost Mode" = todas las de riesgo Seguro. Las Moderadas se eligen a mano en Optimizaciones.
        // Las de FiveM quedan fuera: solo tienen sentido si FiveM está instalado, y van en su propia página.
        // La usan el botón grande del Inicio y el benchmark.
        public static IReadOnlyList<IOptimizacion> GhostMode { get; } =
            Todas.Where(o => o.Riesgo == Riesgo.Seguro && o.Categoria != Categoria.FiveM).ToList();

        // event = "aviso" al que otras partes se suscriben con +=.
        // Se dispara al terminar de aplicar o revertir, para que la pantalla se actualice sola.
        public static event Action? EstadoCambiado;

        // El punto de restauración se crea UNA vez por sesión, antes de lo primero que se aplique
        private static bool puntoRestauracionListo = false;

        // Para que la pantalla sepa si mostrar "Se creará un punto de restauración..."
        public static bool PuntoRestauracionListo => puntoRestauracionListo;

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

        // Para cambios que no son una IOptimizacion (p. ej. un programa de inicio): mismo candado,
        // mismo punto de restauración y mismo aviso EstadoCambiado. Devuelve false si el usuario canceló.
        // Si el cambio falla, la excepción llega a quien llamó, que decide cómo avisar.
        public static async Task<bool> CambiarAsync(Action cambio, Func<Task<bool>> continuarSinPunto)
        {
            await Candado.WaitAsync();
            try
            {
                if (!await AsegurarPuntoRestauracionAsync(continuarSinPunto))
                {
                    return false;
                }
                await Task.Run(cambio);
                return true;
            }
            finally
            {
                Candado.Release();
                EstadoCambiado?.Invoke();
            }
        }

        // Deshace las optimizaciones y, además, los programas de inicio que se deshabilitaron
        public static async Task<ResultadoAplicacion> DeshacerTodoAsync()
        {
            ResultadoAplicacion resultado = await RevertirAsync(Todas);
            if (!GestorEstado.TieneCambios(GestorArranque.Modulo))
            {
                return resultado;
            }

            await Candado.WaitAsync();
            try
            {
                await Task.Run(GestorArranque.RestaurarTodo);
                return resultado;
            }
            catch (Exception ex)
            {
                Logger.Error("Falló al restaurar los programas de inicio", ex);
                return resultado with { OtrosFallos = ["Programas de inicio de Windows"] };   // with = copia cambiando eso
            }
            finally
            {
                Candado.Release();
                EstadoCambiado?.Invoke();
            }
        }

        // Crea el punto de restauración ya, antes de algo largo (el benchmark), para no tener que
        // preguntar nada a mitad de camino. Devuelve false si el usuario canceló.
        public static async Task<bool> PrepararPuntoRestauracionAsync(Func<Task<bool>> continuarSinPunto)
        {
            await Candado.WaitAsync();
            try
            {
                return await AsegurarPuntoRestauracionAsync(continuarSinPunto);
            }
            finally
            {
                Candado.Release();
            }
        }

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
