using System.Windows;
using System.Windows.Media;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.Sistema;

namespace OptimizadorPC
{
    public partial class MainWindow : Window
    {
        // Fíjate: la variable es de tipo IOptimizacion (el contrato), no PlanEnergia.
        // Mañana podrías poner aquí cualquier otra optimización y la ventana funcionaría igual.
        private readonly IOptimizacion optimizacion = new PlanEnergia();

        public MainWindow()
        {
            InitializeComponent();   // construye la ventana a partir del XAML

            TxtNombre.Text = optimizacion.Nombre;
            TxtDescripcion.Text = optimizacion.Descripcion;
            ActualizarEstado();
        }

        private void BtnAplicar_Click(object sender, RoutedEventArgs e)
        {
            optimizacion.Aplicar();
            ActualizarEstado();
        }

        private void BtnRevertir_Click(object sender, RoutedEventArgs e)
        {
            optimizacion.Revertir();
            ActualizarEstado();
        }

        private void ActualizarEstado()
        {
            bool activa = optimizacion.EstaActiva();
            TxtEstado.Text = activa ? "● ACTIVA" : "○ Inactiva";
            TxtEstado.Foreground = activa ? Brushes.LimeGreen : Brushes.Gray;
        }
    }
}