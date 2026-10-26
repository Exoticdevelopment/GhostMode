using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OptimizadorPC.Core;
using OptimizadorPC.Servicios;
using Wpf.Ui.Appearance;   // ApplicationAccentColorManager, ApplicationTheme

namespace OptimizadorPC
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Un error puede "escaparse" por tres caminos distintos. Ponemos una red en cada uno.

            // 1. Errores en el hilo de la interfaz (al pulsar un botón, por ejemplo).
            //    Aquí SÍ podemos rescatar la app: mostramos el error y seguimos funcionando.
            DispatcherUnhandledException += (s, ex) =>
            {
                Logger.Error("Error no controlado", ex.Exception);
                MessageBox.Show(ex.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;   // "ya me encargué": evita que la app se cierre
            };

            // 2. Errores en otros hilos (trabajo en segundo plano).
            //    Aquí NO se puede evitar el cierre, pero al menos queda anotado en el log.
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                Logger.Error("Error fatal en segundo plano", ex.ExceptionObject as Exception);
            };

            // 3. Errores dentro de un Task que nadie revisó con await.
            //    SetObserved() = "lo vimos", así no se pierde en silencio.
            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                Logger.Error("Error en tarea sin revisar", ex.Exception);
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

            var ventana = new MainWindow();
            MainWindow = ventana;
            // Cuando otra copia pida mostrarse, mostrar esta (el aviso llega en otro hilo: se pasa al de la interfaz)
            ThreadPool.RegisterWaitForSingleObject(pedirMostrar, (_, _) => Dispatcher.BeginInvoke(ventana.Mostrar),
                                                   null, Timeout.Infinite, executeOnlyOnce: false);
            if (!minimizado)
            {
                ventana.Show();
            }
            _ = ventana.IniciarSegundoPlanoAsync();
        }

        private Mutex? instanciaUnica;
        private EventWaitHandle? pedirMostrar;

        protected override void OnExit(ExitEventArgs e)
        {
            instanciaUnica?.Dispose();
            pedirMostrar?.Dispose();
            base.OnExit(e);
        }
    }
}
