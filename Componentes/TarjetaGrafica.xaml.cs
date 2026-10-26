using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;   // SymbolRegular (la lista de iconos)

namespace OptimizadorPC.Componentes
{
    // Se usa así:  <componentes:TarjetaGrafica Titulo="CPU" Icono="DeveloperBoard20" EtiquetaMaximo="100 %"/>
    // y desde el código, cada segundo:  TarjetaCpu.Valor = "24 %"; TarjetaCpu.Agregar(24);
    // Igual que MedidorRecurso: cada propiedad es una DependencyProperty para poder enlazarla.
    public partial class TarjetaGrafica : UserControl
    {
        public TarjetaGrafica()
        {
            InitializeComponent();
        }

        // Un punto más en la gráfica (null = sin dato: se deja un hueco)
        public void Agregar(double? porcentaje) => Grafica.Agregar(porcentaje);

        public static readonly DependencyProperty TituloProperty =
            DependencyProperty.Register(nameof(Titulo), typeof(string), typeof(TarjetaGrafica), new PropertyMetadata(""));

        public string Titulo
        {
            get => (string)GetValue(TituloProperty);
            set => SetValue(TituloProperty, value);
        }

        // El número grande de la derecha: "24 %". "—" mientras no hay dato
        public static readonly DependencyProperty ValorProperty =
            DependencyProperty.Register(nameof(Valor), typeof(string), typeof(TarjetaGrafica), new PropertyMetadata("—"));

        public string Valor
        {
            get => (string)GetValue(ValorProperty);
            set => SetValue(ValorProperty, value);
        }

        // Texto pequeño debajo del título: "126 procesos"
        public static readonly DependencyProperty DetalleProperty =
            DependencyProperty.Register(nameof(Detalle), typeof(string), typeof(TarjetaGrafica), new PropertyMetadata(""));

        public string Detalle
        {
            get => (string)GetValue(DetalleProperty);
            set => SetValue(DetalleProperty, value);
        }

        // Lo que vale el techo de la gráfica: "100 %", "16 GB"
        public static readonly DependencyProperty EtiquetaMaximoProperty =
            DependencyProperty.Register(nameof(EtiquetaMaximo), typeof(string), typeof(TarjetaGrafica), new PropertyMetadata(""));

        public string EtiquetaMaximo
        {
            get => (string)GetValue(EtiquetaMaximoProperty);
            set => SetValue(EtiquetaMaximoProperty, value);
        }

        public static readonly DependencyProperty IconoProperty =
            DependencyProperty.Register(nameof(Icono), typeof(SymbolRegular), typeof(TarjetaGrafica),
                                        new PropertyMetadata(SymbolRegular.Empty));

        public SymbolRegular Icono
        {
            get => (SymbolRegular)GetValue(IconoProperty);
            set => SetValue(IconoProperty, value);
        }
    }
}
