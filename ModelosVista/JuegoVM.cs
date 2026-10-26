using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;   // ObservableObject, [ObservableProperty]
using OptimizadorPC.Gaming;

namespace OptimizadorPC.ModelosVista
{
    // Una fila de la lista de juegos de Modo Gaming.
    // El interruptor "activación automática" avisa a la página con CambioPedido (igual que ElementoArranqueVM)
    public partial class JuegoVM : ObservableObject
    {
        public Juego Juego { get; }

        private bool cambiandoDesdeCodigo = false;

        public JuegoVM(Juego juego, bool automatico, ImageSource? icono)
        {
            Juego = juego;
            Icono = icono;
            cambiandoDesdeCodigo = true;
            Automatico = automatico;
            cambiandoDesdeCodigo = false;
        }

        public string Nombre => Juego.Nombre;
        public string Ruta => Juego.Exe;
        public ImageSource? Icono { get; }

        // Sin ícono propio: se muestra el genérico
        public Visibility VisibleIconoGenerico => Icono == null ? Visibility.Visible : Visibility.Collapsed;

        // "steam", "epic", "fivem", "manual" (pastilla junto al nombre)
        public string TextoOrigen => Juego.Origen.ToString().ToLowerInvariant();

        // Solo los agregados a mano tienen "[ quitar ]"
        public Visibility VisibleQuitar => Juego.Origen == OrigenJuego.Manual ? Visibility.Visible : Visibility.Collapsed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextoAutomatico))]
        public partial bool Automatico { get; set; }

        public string TextoAutomatico => Automatico ? "activada" : "desactivada";

        public event Action<JuegoVM, bool>? CambioPedido;

        partial void OnAutomaticoChanged(bool value)
        {
            if (!cambiandoDesdeCodigo)
            {
                CambioPedido?.Invoke(this, value);
            }
        }
    }
}
