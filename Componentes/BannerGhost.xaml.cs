using System.Windows;
using System.Windows.Controls;

namespace OptimizadorPC.Componentes
{
    // Banner ASCII de Inicio. Muestra la versión grande si cabe entera en el ancho disponible;
    // si no (ventana estrecha), la compacta. Nunca se corta ni se escala: el arte ASCII escalado se deforma
    public partial class BannerGhost : UserControl
    {
        private double anchoGrande = 0;   // lo que mide la versión grande (se calcula una vez)

        public BannerGhost()
        {
            InitializeComponent();
            SizeChanged += (s, e) => ElegirVersion(e.NewSize.Width);
        }

        private void ElegirVersion(double disponible)
        {
            if (anchoGrande == 0)
            {
                // Measure con espacio infinito = "¿cuánto mides si nadie te limita?"
                Grande.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                anchoGrande = Grande.DesiredSize.Width;
            }
            bool cabe = disponible >= anchoGrande;
            Grande.Visibility = cabe ? Visibility.Visible : Visibility.Collapsed;
            Compacta.Visibility = cabe ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
