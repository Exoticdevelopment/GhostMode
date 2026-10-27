using System.Windows;
using System.Windows.Controls;

namespace OptimizadorPC.Componentes
{
    // Banner ASCII de Inicio. Muestra la versión grande si cabe entera en el ancho disponible;
    // si no (ventana estrecha), la compacta. Nunca se corta ni se escala: el arte ASCII escalado se deforma
    public partial class BannerGhost : UserControl
    {
        // Lo que mide la versión grande. Es igual para todos los banners: se calcula una vez.
        // Lo usa también MainWindow para abrir con un ancho en el que quepa
        private static double anchoGrande = 0;

        public static double AnchoGrande
        {
            get
            {
                if (anchoGrande == 0)
                {
                    new BannerGhost().MedirGrande();
                }
                return anchoGrande;
            }
        }

        public BannerGhost()
        {
            InitializeComponent();
            SizeChanged += (s, e) => ElegirVersion(e.NewSize.Width);
        }

        // Measure con espacio infinito = "¿cuánto mides si nadie te limita?"
        private void MedirGrande()
        {
            Grande.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            anchoGrande = Grande.DesiredSize.Width;
        }

        private void ElegirVersion(double disponible)
        {
            if (anchoGrande == 0)
            {
                MedirGrande();
            }
            bool cabe = disponible >= anchoGrande;
            Grande.Visibility = cabe ? Visibility.Visible : Visibility.Collapsed;
            Compacta.Visibility = cabe ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
