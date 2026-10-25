using System.Windows;
using OptimizadorPC.Core;

namespace OptimizadorPC
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Si algo falla, lo anotamos y mostramos el error en vez de cerrar la app sin explicación
            DispatcherUnhandledException += (s, ex) =>
            {
                Logger.Error("Error no controlado", ex.Exception);
                MessageBox.Show(ex.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            Logger.Info("=== OptimizadorPC iniciado ===");
            base.OnStartup(e);
        }
    }
}