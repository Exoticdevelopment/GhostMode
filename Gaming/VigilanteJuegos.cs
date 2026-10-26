using OptimizadorPC.Core;

namespace OptimizadorPC.Gaming
{
    // Mira cada 2 segundos qué programas están abiertos y avisa cuando un juego de la lista se abre o se cierra.
    //
    // Por qué un sondeo y no eventos de WMI: los eventos de WMI también sondean por dentro (y más caro).
    // Aquí cada vuelta es una "foto" de procesos de Windows (Toolhelp32) y la ruta del .exe solo se pregunta
    // la primera vez que aparece cada proceso; el resto de vueltas sale de la memoria. Unos milisegundos cada 2 s.
    //
    // Los avisos llegan desde un hilo de fondo: quien los escuche debe pasarlos al hilo de la interfaz.
    public sealed class VigilanteJuegos : IDisposable
    {
        // Primer proceso de un juego: el juego "se abrió"
        public event Action<Juego, IReadOnlyList<ProcesoJuego>>? JuegoIniciado;
        // Otro proceso del mismo juego que apareció después (para darle también prioridad alta)
        public event Action<Juego, ProcesoJuego>? ProcesoNuevo;
        // Ya no queda ningún proceso del juego
        public event Action<Juego>? JuegoCerrado;

        private IReadOnlyList<Juego> juegos = [];
        private readonly object candado = new();

        // Lo ya visto: clave "PID:inicio" → ruta del .exe (null = no se pudo leer, p. ej. procesos protegidos)
        private Dictionary<string, string?> rutas = new();
        // Por cada juego abierto, las claves de sus procesos
        private readonly Dictionary<string, HashSet<string>> abiertos = new();

        private CancellationTokenSource? cancelar;

        // Qué juegos vigilar (solo los que tienen "activación automática")
        public void Vigilar(IEnumerable<Juego> lista)
        {
            lock (candado)
            {
                juegos = lista.ToList();
                // Un juego que ya no se vigila deja de contar como abierto (sin avisar de cierre: no se activó por él)
                foreach (string id in abiertos.Keys.Where(id => juegos.All(j => j.Id != id)).ToList())
                {
                    abiertos.Remove(id);
                }
            }
        }

        public IReadOnlyList<Juego> JuegosAbiertos
        {
            get
            {
                lock (candado)
                {
                    return juegos.Where(j => abiertos.ContainsKey(j.Id)).ToList();
                }
            }
        }

        public void Iniciar(TimeSpan intervalo)
        {
            Detener();
            cancelar = new CancellationTokenSource();
            CancellationToken token = cancelar.Token;

            // PeriodicTimer = reloj que "espera hasta el próximo tic" sin ocupar un hilo mientras espera
            _ = Task.Run(async () =>
            {
                using var reloj = new PeriodicTimer(intervalo);
                try
                {
                    do
                    {
                        try
                        {
                            Revisar();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Modo gaming: falló la revisión de procesos", ex);
                        }
                    }
                    while (await reloj.WaitForNextTickAsync(token));
                }
                catch (OperationCanceledException)
                {
                    // Detener(): fin normal
                }
            });
        }

        public void Detener()
        {
            cancelar?.Cancel();
            cancelar?.Dispose();
            cancelar = null;
        }

        public void Dispose() => Detener();

        // Una vuelta: mira los procesos y lanza los avisos que toquen. Pública para las pruebas
        public void Revisar()
        {
            var iniciados = new List<(Juego, List<ProcesoJuego>)>();
            var nuevos = new List<(Juego, ProcesoJuego)>();
            var cerrados = new List<Juego>();

            lock (candado)
            {
                // Foto actual: clave → ruta, reutilizando las rutas ya conocidas
                var ahora = new Dictionary<string, string?>();
                var pidDe = new Dictionary<string, (int Pid, long Inicio)>();
                foreach (ProcesoVivo p in Procesos.Listar())
                {
                    if (p.Pid <= 4 || p.Pid == Environment.ProcessId || Procesos.Inicio(p.Pid) is not long inicio)
                    {
                        continue;   // 0 y 4 = "Idle" y "System"; sin hora de inicio = no se puede abrir
                    }
                    string clave = Procesos.Clave(p.Pid, inicio);
                    ahora[clave] = rutas.TryGetValue(clave, out string? ruta) ? ruta : Procesos.Ruta(p.Pid);
                    pidDe[clave] = (p.Pid, inicio);
                }
                rutas = ahora;   // los cerrados desaparecen solos de la memoria

                foreach (Juego juego in juegos)
                {
                    var claves = ahora.Where(kv => kv.Value != null && juego.EsSuyo(kv.Value)).Select(kv => kv.Key).ToHashSet();
                    bool estaba = abiertos.TryGetValue(juego.Id, out HashSet<string>? antes);

                    if (claves.Count == 0)
                    {
                        if (estaba)
                        {
                            abiertos.Remove(juego.Id);
                            cerrados.Add(juego);
                        }
                        continue;
                    }

                    ProcesoJuego Crear(string c) => new(pidDe[c].Pid, pidDe[c].Inicio, ahora[c]!);
                    if (!estaba)
                    {
                        iniciados.Add((juego, claves.Select(Crear).ToList()));
                    }
                    else
                    {
                        nuevos.AddRange(claves.Except(antes!).Select(c => (juego, Crear(c))));
                    }
                    abiertos[juego.Id] = claves;
                }
            }

            // Los avisos, fuera del candado: quien escucha puede tardar
            foreach (var (juego, procesos) in iniciados)
            {
                Logger.Info($"Modo gaming: {juego.Nombre} se abrió ({procesos.Count} procesos)");
                JuegoIniciado?.Invoke(juego, procesos);
            }
            foreach (var (juego, proceso) in nuevos)
            {
                ProcesoNuevo?.Invoke(juego, proceso);
            }
            foreach (Juego juego in cerrados)
            {
                Logger.Info($"Modo gaming: {juego.Nombre} se cerró");
                JuegoCerrado?.Invoke(juego);
            }
        }
    }
}
