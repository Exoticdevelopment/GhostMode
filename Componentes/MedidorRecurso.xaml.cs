using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;   // Brush
using Wpf.Ui.Controls;   // SymbolRegular (la lista de iconos)

namespace OptimizadorPC.Componentes
{
    // Se usa así:  <componentes:MedidorRecurso Etiqueta="RAM" Icono="Ram20" Valor="4,6 / 8 GB" .../>
    // Cada propiedad es una DependencyProperty para poder escribirla en XAML o cambiarla desde código.
    public partial class MedidorRecurso : UserControl
    {
        // Cuántas celdas tiene la barra de texto: 20 = cada celda vale un 5 %
        private const int Celdas = 20;

        public MedidorRecurso()
        {
            InitializeComponent();
            DibujarBarra();
        }

        // Escribe la barra: tantas "|" como toque según Porcentaje, y "·" en el resto.
        // new string('|', 7) = "|||||||"
        private void DibujarBarra()
        {
            int llenas = (int)Math.Round(Math.Clamp(Porcentaje, 0, 100) / 100 * Celdas);
            RunLleno.Text = new string('|', llenas);
            RunVacio.Text = new string('·', Celdas - llenas);
        }

        // "CPU", "RAM"...
        public static readonly DependencyProperty EtiquetaProperty =
            DependencyProperty.Register(nameof(Etiqueta), typeof(string), typeof(MedidorRecurso), new PropertyMetadata(""));

        public string Etiqueta
        {
            get => (string)GetValue(EtiquetaProperty);
            set => SetValue(EtiquetaProperty, value);
        }

        // El número grande: "24 %", "4,6 / 8 GB". "—" mientras no hay dato
        public static readonly DependencyProperty ValorProperty =
            DependencyProperty.Register(nameof(Valor), typeof(string), typeof(MedidorRecurso), new PropertyMetadata("—"));

        public string Valor
        {
            get => (string)GetValue(ValorProperty);
            set => SetValue(ValorProperty, value);
        }

        // Texto pequeño de abajo: "4 núcleos · 2,40 GHz"
        public static readonly DependencyProperty DetalleProperty =
            DependencyProperty.Register(nameof(Detalle), typeof(string), typeof(MedidorRecurso), new PropertyMetadata(""));

        public string Detalle
        {
            get => (string)GetValue(DetalleProperty);
            set => SetValue(DetalleProperty, value);
        }

        // Relleno de la barra, de 0 a 100
        public static readonly DependencyProperty PorcentajeProperty =
            DependencyProperty.Register(nameof(Porcentaje), typeof(double), typeof(MedidorRecurso),
                                        // El segundo dato = qué hacer cuando cambia: volver a dibujar la barra
                                        new PropertyMetadata(0.0, (d, e) => ((MedidorRecurso)d).DibujarBarra()));

        public double Porcentaje
        {
            get => (double)GetValue(PorcentajeProperty);
            set => SetValue(PorcentajeProperty, value);
        }

        // Color de la barra (p. ej. ámbar o rojo si la GPU está caliente). null = el normal
        public static readonly DependencyProperty ColorBarraProperty =
            DependencyProperty.Register(nameof(ColorBarra), typeof(Brush), typeof(MedidorRecurso), new PropertyMetadata(null));

        public Brush? ColorBarra
        {
            get => (Brush?)GetValue(ColorBarraProperty);
            set => SetValue(ColorBarraProperty, value);
        }

        // Icono de la esquina
        public static readonly DependencyProperty IconoProperty =
            DependencyProperty.Register(nameof(Icono), typeof(SymbolRegular), typeof(MedidorRecurso),
                                        new PropertyMetadata(SymbolRegular.Empty));

        public SymbolRegular Icono
        {
            get => (SymbolRegular)GetValue(IconoProperty);
            set => SetValue(IconoProperty, value);
        }
    }
}
