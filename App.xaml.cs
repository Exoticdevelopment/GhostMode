using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OptimizadorPC.Core;
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

            // Color de acento = el verde del diseño (#22C55E). WPF-UI lo usa en la barrita
            // del elemento activo de la barra lateral, los interruptores, etc.
            // Si no, usaría el color de acento que el usuario tenga en Windows.
            ApplicationAccentColorManager.Apply(Color.FromRgb(0x22, 0xC5, 0x5E), ApplicationTheme.Dark);

            Logger.Info("=== OptimizadorPC iniciado ===");
            base.OnStartup(e);
        }
    }
}
