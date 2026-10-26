using System.Diagnostics;
using System.IO;
using OptimizadorPC.Core;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Gaming
{
    // El "cerebro" de Modo Gaming: une la lista de juegos, el vigilante y las cuatro opciones.
    //   Se abre un juego con activación automática → aplica las opciones activas y avisa con un toast.
    //   Se cierra (y no queda otro abierto)        → deshace todo lo que aplicó.
    //
    // Todo lo que cambia queda anotado en GestorEstado ANTES de cambiarlo (módulos "modo-gaming-*"):
    // si Ghost Mode se cierra o falla a mitad de partida, al volver a abrirse Recuperar lo deshace.
    public sealed class SesionGaming
    {
        // Qué aplicó la sesión: el juego y las optimizaciones de Inicio que NO estaban ya aplicadas
        // (las que el usuario tenía puestas de antes no se tocan al terminar)
        public const string Modulo = "modo-gaming-sesion";

        // Cada cuánto se mira la lista de procesos
        public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);

        // La de la app (las pruebas crean la suya con otro archivo de configuración)
        public static SesionGaming Actual { get; } = new();

        public ConfigGaming Config { get; }
        private readonly string archivoConfig;

        private readonly VigilanteJuegos vigilante = new();
        private List<Juego> detectados = [];

        // Un cambio a la vez: abrir y cerrar juegos muy seguidos no se mezcla
        private readonly SemaphoreSlim turno = new(1, 1);
        // Los avisos del vigilante llegan en un hilo de fondo; se pasan al hilo de la interfaz con esto
        private SynchronizationContext? contexto;
        private bool prioridadEnSesion;

        // El juego por el que se activó (null = Modo Gaming en espera)
        public Juego? JuegoActivo { get; private set; }
        public bool Activa => JuegoActivo != null;

        // Para que la pantalla se actualice (llega en el hilo de la interfaz)
        public event Action? Cambiado;

        public SesionGaming(string? archivoConfig = null)
        {
            this.archivoConfig = archivoConfig ?? ConfigGaming.ArchivoPorDefecto;
            Config = ConfigGaming.Cargar(this.archivoConfig);

            vigilante.JuegoIniciado += (juego, procesos) => EnHiloPropio(() => AlIniciarJuegoAsync(juego, procesos));
            vigilante.ProcesoNuevo += (juego, proceso) => EnHiloPropio(() => AlAparecerProcesoAsync(proceso));
            vigilante.JuegoCerrado += juego => EnHiloPropio(() => AlCerrarJuegoAsync(juego));
        }

        // ===== Arranque de la app =====

        // Se llama una vez al abrir Ghost Mode (desde el hilo de la interfaz)
        public async Task IniciarAsync()
        {
            contexto = SynchronizationContext.Current;
            await RecuperarAsync();
            await RecargarJuegosAsync();
            vigilante.Iniciar(Intervalo);
        }

        public void Detener() => vigilante.Detener();

        // Si la última vez Ghost Mode se cerró con un juego abierto (o falló), deshacer lo que quedó:
        // reanudar apps pausadas, devolver prioridades y revertir las optimizaciones de esa sesión
        public async Task RecuperarAsync()
        {
            if (!QuedoAlgoPendiente())
            {
                return;
            }
            Logger.Aviso("Modo gaming: la sesión anterior no terminó bien; deshaciendo lo que quedó aplicado");
            await turno.WaitAsync();
            try
            {
                await DeshacerAsync();
            }
            finally
            {
                turno.Release();
            }
        }

        public static bool QuedoAlgoPendiente() =>
            GestorEstado.TieneCambios(Modulo) || GestorEstado.TieneCambios(SuspensorApps.Modulo) ||
            GestorEstado.TieneCambios(PrioridadJuego.Modulo) || GestorEstado.TieneCambios(GrabacionFondo.Modulo);

        // ===== Lista de juegos =====

        // Detectados + agregados a mano
        public IReadOnlyList<Juego> Juegos =>
            detectados.Concat(Config.JuegosManuales
                                    .Select(m => new Juego(m.Nombre, m.Exe, null, OrigenJuego.Manual))
                                    .Where(m => detectados.All(d => d.Id != m.Id)))
                      .ToList();

        public async Task RecargarJuegosAsync()
        {
            detectados = await Task.Run(DetectorJuegos.Detectar);
            ActualizarVigilancia();
        }

        public async Task CambiarAutomaticoAsync(Juego juego, bool activo)
        {
            Config.Automatico[juego.Id] = activo;
            GuardarConfig();
            ActualizarVigilancia();
            Logger.Info($"Modo gaming: activación automática {(activo ? "encendida" : "apagada")} para {juego.Nombre}");
            await TerminarSiNoQuedaJuegoAsync();
        }

        // "[ agregar juego ]": el nombre sale de las propiedades del .exe (o de su nombre de archivo)
        public Juego AgregarManual(string exe)
        {
            exe = Path.GetFullPath(exe);
            if (Juegos.FirstOrDefault(j => j.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)) is Juego existente)
            {
                return existente;
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exe);
            string nombre = new[] { info.FileDescription, info.ProductName }
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim() ?? Path.GetFileNameWithoutExtension(exe);

            var juego = new Juego(nombre, exe, null, OrigenJuego.Manual);
            Config.JuegosManuales.Add(new JuegoManual(nombre, exe));
            Config.Automatico[juego.Id] = true;
            GuardarConfig();
            ActualizarVigilancia();
            Logger.Info($"Modo gaming: juego agregado a mano: {nombre} ({exe})");
            return juego;
        }

        public async Task QuitarManualAsync(Juego juego)
        {
            Config.JuegosManuales.RemoveAll(m => m.Exe.Equals(juego.Exe, StringComparison.OrdinalIgnoreCase));
            Config.Automatico.Remove(juego.Id);
            GuardarConfig();
            ActualizarVigilancia();
            Logger.Info($"Modo gaming: juego quitado de la lista: {juego.Nombre}");
            await TerminarSiNoQuedaJuegoAsync();
        }

        // Si se apagó (o quitó) el juego de la sesión en marcha y no queda otro vigilado abierto, se deshace ya
        private async Task TerminarSiNoQuedaJuegoAsync()
        {
            if (Activa && vigilante.JuegosAbiertos.Count == 0)
            {
                await TerminarAsync("se apagó la activación automática");
            }
        }

        public void GuardarConfig() => Config.Guardar(archivoConfig);

        // Cuántos juegos se están vigilando (los que tienen activación automática)
        public int Vigilados => Juegos.Count(Config.EsAutomatico);

        private void ActualizarVigilancia() => vigilante.Vigilar(Juegos.Where(Config.EsAutomatico));

        // Para las pruebas: una vuelta del vigilante sin esperar al reloj, y el trabajo que lanzó
        public void RevisarAhora() => vigilante.Revisar();
        public Task TrabajoEnCurso { get; private set; } = Task.CompletedTask;

        // ===== Al abrir y cerrar un juego =====

        public async Task AlIniciarJuegoAsync(Juego juego, IReadOnlyList<ProcesoJuego> procesos)
        {
            await turno.WaitAsync();
            try
            {
                if (Activa)
                {
                    // Ya hay una sesión (otro juego abierto): solo prioridad para los procesos de este
                    await AlAparecerProcesosAsync(procesos);
                    return;
                }

                JuegoActivo = juego;
                GestorEstado.GuardarOriginal(Modulo, "juego", juego.Nombre);
                ConfigGaming c = Config;
                prioridadEnSesion = c.PrioridadAlta;
                var hecho = new List<string>();

                // 1. Memoria en espera: primero, mientras el juego aún está cargando
                if (c.LiberarRam && await Task.Run(MemoriaStandby.LiberarSiHaceFalta))
                {
                    hecho.Add("memoria liberada");
                }

                // 2. Prioridad alta
                if (c.PrioridadAlta && await Task.Run(() => procesos.Count(PrioridadJuego.Subir)) > 0)
                {
                    hecho.Add("prioridad alta");
                }

                // 3. Pausar apps de fondo (nunca las del juego ni su launcher)
                if (c.SuspenderApps)
                {
                    var delJuego = procesos.Select(p => p.Pid).ToHashSet();
                    List<string> pausadas = await Task.Run(() => SuspensorApps.Suspender(c.AppsSuspender, delJuego));
                    if (pausadas.Count > 0)
                    {
                        hecho.Add(pausadas.Count == 1 ? "1 app pausada" : $"{pausadas.Count} apps pausadas");
                    }
                }

                // 4. Las optimizaciones de Inicio que falten
                if (c.OptimizacionesInicio)
                {
                    List<IOptimizacion> faltan = await Task.Run(() => AplicadorOptimizaciones.GhostMode.Where(o => !EstaActiva(o)).ToList());
                    if (faltan.Count > 0)
                    {
                        // Anotar ANTES de aplicar: al terminar se revierten solo estas
                        GestorEstado.GuardarOriginal(Modulo, "optimizaciones", string.Join(",", faltan.Select(o => o.Id)));
                        ResultadoAplicacion r = await AplicadorOptimizaciones.AplicarTemporalAsync(faltan);
                        int bien = faltan.Count - r.Fallidas.Count;
                        if (bien > 0)
                        {
                            hecho.Add(bien == 1 ? "1 optimización" : $"{bien} optimizaciones");
                        }
                    }
                }

                // 5. Grabación de Game DVR en pausa. Va DESPUÉS de las optimizaciones (y se deshace antes):
                //    las dos tocan GameDVR_Enabled y así cada una devuelve el valor que encontró
                if (c.PausarGrabacion && await Task.Run(GrabacionFondo.Apagar))
                {
                    hecho.Add("grabación de Game DVR en pausa");
                }

                string resumen = hecho.Count > 0 ? string.Join(" · ", hecho) : "nada que cambiar con las opciones elegidas";
                Logger.Info($"> ghost_mode activado para {juego.Nombre}: {resumen}");
                Avisos.Info($"> ghost_mode activado para {juego.Nombre}", $"{resumen}. Al cerrar el juego se deshace solo.");
                Cambiado?.Invoke();
            }
            finally
            {
                turno.Release();
            }
        }

        private async Task AlAparecerProcesoAsync(ProcesoJuego proceso)
        {
            await turno.WaitAsync();
            try
            {
                await AlAparecerProcesosAsync([proceso]);
            }
            finally
            {
                turno.Release();
            }
        }

        private async Task AlAparecerProcesosAsync(IReadOnlyList<ProcesoJuego> procesos)
        {
            if (Activa && prioridadEnSesion)
            {
                await Task.Run(() => procesos.Count(PrioridadJuego.Subir));
            }
        }

        public async Task AlCerrarJuegoAsync(Juego juego)
        {
            // Si queda otro juego de la lista abierto, la sesión sigue
            if (!Activa || vigilante.JuegosAbiertos.Count > 0)
            {
                return;
            }
            await TerminarAsync($"{juego.Nombre} se cerró");
        }

        // Deshace la sesión: al cerrar el juego, con «Deshacer todo» o al cerrar Ghost Mode
        public async Task TerminarAsync(string motivo)
        {
            await turno.WaitAsync();
            try
            {
                if (!Activa && !QuedoAlgoPendiente())
                {
                    return;
                }
                string nombre = JuegoActivo?.Nombre ?? GestorEstado.ObtenerOriginal(Modulo, "juego") ?? "el juego";
                await DeshacerAsync();
                Logger.Info($"> ghost_mode desactivado ({motivo}): ajustes de la sesión de {nombre} restaurados");
                Avisos.Exito("> ghost_mode desactivado", $"{motivo}: se restauraron los ajustes de la sesión.");
            }
            finally
            {
                turno.Release();
            }
        }

        // Reanuda, devuelve prioridades y revierte las optimizaciones que aplicó la sesión
        private async Task DeshacerAsync()
        {
            await Task.Run(() =>
            {
                SuspensorApps.ReanudarTodas();
                PrioridadJuego.RestaurarTodas();
                try
                {
                    GrabacionFondo.Restaurar();   // antes que las optimizaciones (orden inverso al de aplicar)
                }
                catch (Exception ex)
                {
                    Logger.Error("Modo gaming: no se pudo restaurar la grabación de Game DVR", ex);
                }
            });

            string ids = GestorEstado.ObtenerOriginal(Modulo, "optimizaciones") ?? "";
            List<IOptimizacion> aplicadas = AplicadorOptimizaciones.Todas
                .Where(o => ids.Split(',').Contains(o.Id) && GestorEstado.TieneCambios(o.Id)).ToList();
            if (aplicadas.Count > 0)
            {
                ResultadoAplicacion r = await AplicadorOptimizaciones.RevertirAsync(aplicadas);
                foreach (IOptimizacion o in r.Fallidas)
                {
                    Logger.Error($"Modo gaming: no se pudo revertir {o.Nombre}; queda en «Deshacer todo»");
                }
            }

            GestorEstado.Limpiar(Modulo);
            JuegoActivo = null;
            prioridadEnSesion = false;
            Cambiado?.Invoke();
        }

        private static bool EstaActiva(IOptimizacion o)
        {
            try
            {
                return o.EstaActiva();
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo leer el estado de {o.Id}", ex);
                return true;   // si no se sabe, no se toca
            }
        }

        // Los avisos del vigilante llegan en un hilo de fondo: el trabajo se hace en el hilo de la interfaz
        // (allí viven los toasts y los avisos de EstadoCambiado). Sin interfaz (pruebas), se hace ahí mismo
        private void EnHiloPropio(Func<Task> trabajo)
        {
            var terminado = new TaskCompletionSource();
            TrabajoEnCurso = terminado.Task;

            async void Hacer(object? _)
            {
                try
                {
                    await trabajo();
                }
                catch (Exception ex)
                {
                    Logger.Error("Modo gaming: falló al aplicar o deshacer la sesión", ex);
                }
                finally
                {
                    terminado.SetResult();
                }
            }

            if (contexto != null)
            {
                contexto.Post(Hacer, null);
            }
            else
            {
                Hacer(null);
            }
        }
    }
}
