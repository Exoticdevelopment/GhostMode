using OptimizadorPC.Arranque;
using OptimizadorPC.Core;
using OptimizadorPC.Gaming;
using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Optimizaciones.Sistema;
using OptimizadorPC.Red;

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

                return new ResultadoAplicacion(false, await AplicarListaAsync(lista));
            }
            finally
            {
                Candado.Release();
                EstadoCambiado?.Invoke();   // ?. = solo si alguien está escuchando
            }
        }

        // Modo Gaming: cambios temporales que la sesión de juego deshace sola al cerrar el juego.
        // Sin punto de restauración: crearlo justo al abrir un juego le quitaría disco y CPU mientras carga,
        // y además no hace falta: la sesión anota todo en GestorEstado antes de aplicar
        public static async Task<ResultadoAplicacion> AplicarTemporalAsync(IEnumerable<IOptimizacion> lista)
        {
            await Candado.WaitAsync();
            try
            {
                return new ResultadoAplicacion(false, await AplicarListaAsync(lista));
            }
            finally
            {
                Candado.Release();
                EstadoCambiado?.Invoke();
            }
        }

        // Aplica una a una; si una falla, la anota y sigue con las demás. Devuelve las que fallaron.
        // Cada paso deja su línea en el log según ocurre: el panel de log lo muestra en vivo
        private static async Task<List<IOptimizacion>> AplicarListaAsync(IEnumerable<IOptimizacion> lista)
        {
            var fallidas = new List<IOptimizacion>();
            foreach (var optimizacion in lista)
            {
                try
                {
                    // Task.Run = trabajar en segundo plano para que la ventana no se congele
                    await Task.Run(() =>
                    {
                        if (optimizacion.EstaActiva())   // ya aplicada: nada que hacer
                        {
                            Logger.Info($"Ya estaba activa: {optimizacion.Nombre}");
                            return;
                        }
                        Logger.Info($"Aplicando {optimizacion.Nombre}...");
                        optimizacion.Aplicar();
                        Logger.Ok($"Aplicada: {optimizacion.Nombre}");
                    });
                }
                catch (Exception ex)
                {
                    fallidas.Add(optimizacion);
                    Logger.Error($"Falló al aplicar {optimizacion.Id}", ex);
                }
            }
            return fallidas;
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
                        Logger.Info($"Revirtiendo {optimizacion.Nombre}...");
                        await Task.Run(optimizacion.Revertir);
                        Logger.Ok($"Revertida: {optimizacion.Nombre}");
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

        // Deshace las optimizaciones y, además, la sesión de Modo Gaming (apps pausadas, prioridades),
        // el DNS, la solución de MPO y los programas de inicio que se deshabilitaron
        public static async Task<ResultadoAplicacion> DeshacerTodoAsync()
        {
            // Primero la sesión de juego: reanuda las apps pausadas y revierte lo suyo
            await SesionGaming.Actual.TerminarAsync("Deshacer todo");

            ResultadoAplicacion resultado = await RevertirAsync(Todas);

            // Lo que no es una IOptimizacion: (módulo en GestorEstado, cómo deshacerlo, nombre si falla)
            var otros = new (string Modulo, Action Deshacer, string Nombre)[]
            {
                (GestorDns.Modulo, GestorDns.Revertir, "Servidor DNS"),
                (SolucionMpo.Modulo, SolucionMpo.Revertir, "MPO (solución de parpadeos)"),
                (GestorArranque.Modulo, GestorArranque.RestaurarTodo, "Programas de inicio de Windows"),
            };
            var fallos = new List<string>();
            foreach (var (modulo, deshacer, nombre) in otros.Where(o => GestorEstado.TieneCambios(o.Modulo)))
            {
                await Candado.WaitAsync();
                try
                {
                    await Task.Run(deshacer);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Falló al restaurar: {nombre}", ex);
                    fallos.Add(nombre);
                }
                finally
                {
                    Candado.Release();
                    EstadoCambiado?.Invoke();
                }
            }
            return fallos.Count > 0 ? resultado with { OtrosFallos = fallos } : resultado;   // with = copia cambiando eso
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
                // Ese diálogo también puede activar la Protección del sistema y crear el punto: el log debe decir cuál fue
                if (PuntoRestauracion.Ultimo is ResultadoPunto.Creado or ResultadoPunto.YaHabiaUnoReciente)
                {
                    Logger.Info("Punto de restauración listo tras activar la Protección del sistema");
                }
                else
                {
                    Logger.Aviso("El usuario decidió continuar sin punto de restauración");
                }
            }

            puntoRestauracionListo = true;
            return true;
        }
    }
}
