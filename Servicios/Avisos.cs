using OptimizadorPC.Core;  // PuntoRestauracion, ResultadoPunto
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
            // Ventana oculta en la bandeja o detrás de otra (p. ej. jugando): el toast de dentro no se vería.
            // Se manda como notificación de Windows desde el icono de la bandeja
            System.Windows.Window? ventana = System.Windows.Application.Current?.MainWindow;
            if (IconoBandeja.Actual != null && (ventana == null || !ventana.IsVisible || !ventana.IsActive))
            {
                IconoBandeja.Actual.Notificar(titulo, mensaje);
                return;
            }

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
        // (o porque se activó la protección y ya hay punto, o porque el usuario decidió seguir sin él)
        public static async Task<bool> PreguntarContinuarSinPuntoAsync()
        {
            bool ofrecerActivar = true;   // tras un intento fallido ya no se vuelve a ofrecer

            // while (true) = repetir hasta que un "return" nos saque
            while (true)
            {
                var dialogo = new MessageBoxUi
                {
                    Title = "No se pudo crear el punto de restauración",
                    Content = ofrecerActivar
                        ? "Windows no permitió crear el punto de restauración. Lo más habitual es que la " +
                          "Protección del sistema esté desactivada.\n\nGhost Mode puede activarla ahora (es una " +
                          "función de seguridad de Windows y se quedará activada). Los cambios de Ghost Mode " +
                          "se pueden deshacer igualmente desde «Deshacer todo»."
                        : "Tampoco se pudo crear el punto con la Protección del sistema activada. Revisa el log " +
                          "para más detalles.\n\nLos cambios de Ghost Mode se pueden deshacer igualmente desde " +
                          "«Deshacer todo». ¿Quieres continuar?",
                    // Un botón con texto vacío no se muestra: así quitamos "Activar" en el segundo intento
                    PrimaryButtonText = ofrecerActivar ? "Activar protección" : "Continuar sin punto",
                    SecondaryButtonText = ofrecerActivar ? "Continuar sin punto" : "",
                    CloseButtonText = "Cancelar"
                };

                MessageBoxResultUi respuesta = await dialogo.ShowDialogAsync();

                // "Continuar sin punto" es el botón principal o el secundario según la vuelta
                bool continuarSinPunto = ofrecerActivar
                    ? respuesta == MessageBoxResultUi.Secondary
                    : respuesta == MessageBoxResultUi.Primary;
                if (continuarSinPunto)
                {
                    return true;
                }
                if (!ofrecerActivar || respuesta != MessageBoxResultUi.Primary)
                {
                    return false;   // Cancelar o la X
                }

                // Pulsó "Activar protección": activarla y volver a intentar el punto
                ofrecerActivar = false;
                if (await PuntoRestauracion.ActivarProteccionAsync())
                {
                    ResultadoPunto punto = await PuntoRestauracion.CrearAsync("Ghost Mode - antes de optimizar");
                    if (punto != ResultadoPunto.Fallo)
                    {
                        Exito("Protección del sistema activada", "Se creó el punto de restauración. Aplicando cambios…");
                        return true;
                    }
                }
                // Si algo falló, el bucle vuelve a preguntar, ya sin la opción de activar
            }
        }
    }
}
