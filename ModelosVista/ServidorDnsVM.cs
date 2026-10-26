using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;   // ObservableObject, [ObservableProperty]
using OptimizadorPC.Red;

namespace OptimizadorPC.ModelosVista
{
    // Una tarjeta de la fila "Servidor DNS" de la página Red
    public partial class ServidorDnsVM : ObservableObject
    {
        public ServidorDns Servidor { get; }

        public ServidorDnsVM(ServidorDns servidor, string textoDirecciones)
        {
            Servidor = servidor;
            TextoDirecciones = textoDirecciones;
        }

        public string Nombre => Servidor.Nombre;
        public string TextoDirecciones { get; }

        // "12 ms", "sin respuesta" o "—" (sin medir)
        [ObservableProperty]
        public partial string Latencia { get; set; } = "— ms";

        // Es el que tiene puesto Windows ahora
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VisibleActivo))]
        public partial bool EsActivo { get; set; }

        // El más rápido de la última medición
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VisibleRecomendado))]
        public partial bool EsRecomendado { get; set; }

        // Elegido para «[ aplicar dns ]»
        [ObservableProperty]
        public partial bool Seleccionado { get; set; }

        public Visibility VisibleActivo => EsActivo ? Visibility.Visible : Visibility.Collapsed;
        public Visibility VisibleRecomendado => EsRecomendado ? Visibility.Visible : Visibility.Collapsed;
    }
}
