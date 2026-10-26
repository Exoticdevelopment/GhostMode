using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OptimizadorPC.Core;
using OptimizadorPC.Gaming;
using OptimizadorPC.Servicios;
using Wpf.Ui.Appearance;   // ApplicationAccentColorManager, ApplicationTheme

namespace OptimizadorPC
{
    // La app vive aunque no haya ventana: el icono de la bandeja, el atajo global y Modo Gaming son de App.
    // La ventana principal se crea al abrirla y se DESTRUYE al cerrarla con la X (si Modo Gaming vigila juegos,
    // la app sigue en la bandeja). Así, en segundo plano no ocupa memoria con pantallas que nadie ve.
    public partial class App : Application
    {
        public static App Actual => (App)Current;

        public AtajoGlobal? Atajo { get; private set; }

        private MainWindow? ventana;
        private VentanaMensajes? mensajes;
        private IconoBandeja? bandeja;
        private bool saliendo = false;
        private Mutex? instanciaUnica;
        private EventWaitHandle? pedirMostrar;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Un error puede "escaparse" por tres caminos distintos. Ponemos una red en cada uno.

            // 1. Errores en el hilo de la interfaz (al pulsar un botón, por ejemplo).
            //    Aquí SÍ podemos rescatar la app: se anota con su traza completa, se avisa sin bloquear y se sigue.
            DispatcherUnhandledException += (s, ex) =>
            {
                Logger.Fallo("Error no controlado en la interfaz", ex.Exception);
                ex.Handled = true;   // "ya me encargué": evita que la app se cierre
                try
                {
                    Avisos.Error("Algo falló, pero Ghost Mode sigue funcionando",
                                 $"{ex.Exception.Message} · El detalle quedó en el log (Ajustes › exportar diagnóstico).");
                }
                catch (Exception otro)
                {
                    Logger.Error("Tampoco se pudo mostrar el aviso del error", otro);
                }
            };

            // 2. Errores en otros hilos (trabajo en segundo plano).
            //    Aquí NO se puede evitar el cierre: se anota y, antes de morir, se reanudan las apps que
            //    Modo Gaming hubiera pausado (si no, quedarían congeladas hasta volver a abrir Ghost Mode)
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                Logger.Fallo("Error fatal en segundo plano", ex.ExceptionObject as Exception);
                try
                {
                    SuspensorApps.ReanudarTodas();
                }
                catch (Exception otro)
                {
                    Logger.Error("No se pudieron reanudar las apps pausadas al cerrarse por error", otro);
                }
            };

            // 3. Errores dentro de un Task que nadie revisó con await.
            //    SetObserved() = "lo vimos", así no se pierde en silencio ni cierra la app.
            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                Logger.Fallo("Error en tarea sin revisar", ex.Exception);
                ex.SetObserved();
            };

            // Color de acento = el morado de la pantera (#8B5CF6). WPF-UI lo usa en la barrita
            // del elemento activo de la barra lateral, los interruptores, etc.
            // Si no, usaría el color de acento que el usuario tenga en Windows.
            ApplicationAccentColorManager.Apply(Color.FromRgb(0x8B, 0x5C, 0xF6), ApplicationTheme.Dark);

            // Una sola copia de Ghost Mode a la vez (dos vigilando juegos y pausando apps se pisarían).
            // Si ya hay una abierta (p. ej. en la bandeja), se le pide que se muestre y esta se cierra
            instanciaUnica = new Mutex(true, @"Local\GhostMode-Exoticdevelopment", out bool soyLaPrimera);
            pedirMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\GhostMode-Exoticdevelopment-Mostrar");
            if (!soyLaPrimera)
            {
                pedirMostrar.Set();
                Shutdown();
                return;
            }

            bool minimizado = e.Args.Contains(InicioAutomatico.ArgumentoMinimizado);
            Logger.Info(minimizado ? "=== Ghost Mode iniciado (minimizado en la bandeja) ===" : "=== Ghost Mode iniciado ===");
            base.OnStartup(e);

            // Cuando otra copia pida mostrarse, mostrar esta (el aviso llega en otro hilo: se pasa al de la interfaz)
            ThreadPool.RegisterWaitForSingleObject(pedirMostrar, (_, _) => Dispatcher.BeginInvoke(MostrarVentana),
                                                   null, Timeout.Infinite, executeOnlyOnce: false);
            CrearBandejaYAtajo();
            if (!minimizado)
            {
                MostrarVentana();
            }
            _ = IniciarSegundoPlanoAsync(minimizado);
        }

        // ===== Ventana =====

        // Abrir desde la bandeja (o desde una segunda copia de la app que se intentó abrir)
        public void MostrarVentana()
        {
            if (ventana == null)
            {
                ventana = new MainWindow();
                ventana.Closed += (s, e) => AlCerrarVentana();
                MainWindow = ventana;
            }
            ventana.Show();
            if (ventana.WindowState == WindowState.Minimized)
            {
                ventana.WindowState = WindowState.Normal;
            }
            ventana.Activate();
        }

        // La X de la ventana:
        //   - si Modo Gaming vigila juegos → la app sigue en la bandeja (sin ventana) y continúa vigilando
        //   - si no → Ghost Mode se cierra del todo
        private void AlCerrarVentana()
        {
            ventana = null;
            MainWindow = null;
            if (saliendo)
            {
                return;
            }
            if (bandeja == null || SesionGaming.Actual.Vigilados == 0)
            {
                _ = SalirAsync();
                return;
            }

            if (!AjustesApp.Actual.AvisoBandejaMostrado)
            {
                bandeja.Notificar("Ghost Mode sigue aquí",
                                  "Está vigilando tus juegos desde la bandeja. Clic derecho en el icono para salir.");
                AjustesApp.Actual.AvisoBandejaMostrado = true;
                AjustesApp.Actual.Guardar();
            }
            MemoriaPropia.Liberar("ventana cerrada, sigue en la bandeja");
        }

        // "Salir" del menú de la bandeja, o cerrar la ventana sin juegos vigilados.
        // Con una partida en marcha, primero se deshace la sesión de juego (reanudar apps pausadas, etc.)
        public async Task SalirAsync()
        {
            if (saliendo)
            {
                return;
            }
            saliendo = true;
            ventana?.Close();

            SesionGaming.Actual.Detener();
            if (SesionGaming.QuedoAlgoPendiente())
            {
                try
                {
                    await SesionGaming.Actual.TerminarAsync("Ghost Mode se cerró");
                }
                catch (Exception ex)
                {
                    // Lo que quede, se deshace al volver a abrir Ghost Mode (SesionGaming.RecuperarAsync)
                    Logger.Error("No se pudo deshacer la sesión de Modo Gaming al cerrar", ex);
                }
            }

            Atajo?.Dispose();
            bandeja?.Dispose();
            mensajes?.Dispose();
            Logger.Info("=== Ghost Mode cerrado ===");
            Shutdown();
        }

        // ===== Segundo plano: bandeja, atajo y Modo Gaming =====

        private void CrearBandejaYAtajo()
        {
            try
            {
                mensajes = new VentanaMensajes();
                bandeja = new IconoBandeja(mensajes) { GhostActivo = InterruptorGhost.EstaActivo };
                bandeja.Abrir += MostrarVentana;
                bandeja.AlternarGhost += async () => await InterruptorGhost.AlternarAsync("la bandeja");
                bandeja.DeshacerTodo += async () =>
                {
                    MostrarVentana();
                    await ventana!.DeshacerTodoAsync();
                };
                bandeja.Salir += async () => await SalirAsync();

                Atajo = new AtajoGlobal(mensajes);
                Atajo.Pulsado += async () => await InterruptorGhost.AlternarAsync("el atajo de teclado");
                if (!Atajo.Registrar(Combinacion.Leer(AjustesApp.Actual.Atajo)) && AjustesApp.Actual.Atajo != null)
                {
                    Avisos.Error($"El atajo {AjustesApp.Actual.Atajo} no está disponible",
                                 "Otra app ya lo usa. Elige otro en Ajustes.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo crear el icono de la bandeja o el atajo global", ex);
            }
        }

        private async Task IniciarSegundoPlanoAsync(bool minimizado)
        {
            try
            {
                if (Environment.ProcessPath is string exe)
                {
                    await Task.Run(() => InicioAutomatico.CorregirRuta(exe));
                }
                // Modo Gaming vigila desde que se abre la app. Antes, deshace lo que hubiera quedado de una
                // sesión anterior que terminó mal (apps pausadas, prioridades...)
                await SesionGaming.Actual.IniciarAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo iniciar Modo Gaming", ex);
            }

            if (minimizado && ventana == null)
            {
                MemoriaPropia.Liberar("arranque en la bandeja");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            instanciaUnica?.Dispose();
            pedirMostrar?.Dispose();
            base.OnExit(e);
        }
    }
}
