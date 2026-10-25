using Wpf.Ui.Controls;   // Snackbar, SnackbarPresenter, SymbolIcon, ControlAppearance
// Alias: WPF y WPF-UI tienen los dos una clase "MessageBox". Así dejamos claro cuál usamos
using MessageBoxUi = Wpf.Ui.Controls.MessageBox;
using MessageBoxResultUi = Wpf.Ui.Controls.MessageBoxResult;

namespace OptimizadorPC.Servicios
{
    // Mensajes que comparten todas las pantallas:
    // - el "toast" de abajo (Snackbar) que confirma o avisa de un error
    // - el diálogo de "¿seguir sin punto de restauración?"
    public static class Avisos
    {
        // El sitio de la ventana donde aparecen los toasts. Lo asigna MainWindow al arrancar
        public static SnackbarPresenter? Presentador { get; set; }

        public static void Exito(string titulo, string mensaje) =>
            Mostrar(titulo, mensaje, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);

        public static void Error(string titulo, string mensaje) =>
            Mostrar(titulo, mensaje, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);

        public static void Info(string titulo, string mensaje) =>
            Mostrar(titulo, mensaje, ControlAppearance.Secondary, SymbolRegular.Info24);

        private static void Mostrar(string titulo, string mensaje, ControlAppearance aspecto, SymbolRegular icono)
        {
            if (Presentador == null)   // la ventana aún no está lista: no hay dónde mostrarlo
            {
                return;
            }

            var toast = new Snackbar(Presentador)
            {
                Title = titulo,
                Content = mensaje,
                Appearance = aspecto,
                Icon = new SymbolIcon(icono) { FontSize = 24 },
                Timeout = TimeSpan.FromSeconds(6),   // se oculta solo a los 6 segundos
                IsCloseButtonEnabled = true          // la X para cerrarlo antes
            };
            toast.Show();
        }

        // Se pasa a AplicadorOptimizaciones.AplicarAsync. Devuelve true = "seguir de todos modos"
        public static async Task<bool> PreguntarContinuarSinPuntoAsync()
        {
            var dialogo = new MessageBoxUi
            {
                Title = "No se pudo crear el punto de restauración",
                Content = "Windows no permitió crear el punto de restauración (puede que la Protección del sistema " +
                          "esté desactivada).\n\nLos cambios de Ghost Mode se pueden deshacer igualmente desde " +
                          "«Deshacer todo». ¿Quieres continuar?",
                PrimaryButtonText = "Continuar",
                CloseButtonText = "Cancelar"
            };
            return await dialogo.ShowDialogAsync() == MessageBoxResultUi.Primary;
        }
    }
}
