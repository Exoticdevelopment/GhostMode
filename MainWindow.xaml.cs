using OptimizadorPC.Paginas;
using Wpf.Ui.Controls;   // FluentWindow

namespace OptimizadorPC
{
    // La ventana principal ahora hereda de FluentWindow (de WPF-UI), no de Window
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            // Al abrir la ventana, mostrar la página Inicio.
            // Loaded = cuando la ventana ya está construida y lista para dibujarse
            Loaded += (s, e) => Navegacion.Navigate(typeof(InicioPagina));
        }
    }
}
