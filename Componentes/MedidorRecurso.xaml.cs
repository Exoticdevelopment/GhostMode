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
        public MedidorRecurso()
        {
            InitializeComponent();
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
            DependencyProperty.Register(nameof(Porcentaje), typeof(double), typeof(MedidorRecurso), new PropertyMetadata(0.0));

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
