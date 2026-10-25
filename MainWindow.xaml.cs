using System.Windows;
using OptimizadorPC.Core;
using OptimizadorPC.Paginas;
using OptimizadorPC.Servicios;
using Wpf.Ui.Controls;   // FluentWindow
// Alias: WPF y WPF-UI tienen los dos una clase "MessageBox". Así dejamos claro cuál usamos
using MessageBoxUi = Wpf.Ui.Controls.MessageBox;
using MessageBoxResultUi = Wpf.Ui.Controls.MessageBoxResult;

namespace OptimizadorPC
{
    // La ventana principal ahora hereda de FluentWindow (de WPF-UI), no de Window
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            // Loaded = cuando la ventana ya está construida y lista para dibujarse
            Loaded += async (s, e) =>
            {
                Navegacion.Navigate(typeof(InicioPagina));   // empezar en Inicio
                ActualizarPie();
                await MostrarHardwareAsync();
            };

            // Cada vez que se aplique o revierta algo (desde cualquier pantalla), refrescar el pie.
            // Al cerrar la ventana nos "desuscribimos" para no dejar el aviso colgado.
            AplicadorOptimizaciones.EstadoCambiado += ActualizarPie;
            Closed += (s, e) => AplicadorOptimizaciones.EstadoCambiado -= ActualizarPie;
        }

        // "Cambios guardados" = hay valores originales guardados que se pueden deshacer
        private void ActualizarPie()
        {
            bool hayCambios = GestorEstado.HayCambiosPendientes;

            TxtCambios.Text = hayCambios ? "Cambios guardados" : "Sin cambios aplicados";
            IconoCambios.Visibility = hayCambios ? Visibility.Visible : Visibility.Collapsed;
            BtnDeshacerTodo.IsEnabled = hayCambios;
        }

        private async Task MostrarHardwareAsync()
        {
            DatosEquipo equipo = await InfoSistema.ObtenerAsync();

            TxtEquipo.Text = $"EQUIPO LOCAL · {equipo.Windows.ToUpper()}";

            // Solo mostramos lo que se pudo leer: nada de datos inventados
            var partes = new List<string>();
            if (equipo.RamGb != null)
            {
                partes.Add($"{equipo.RamGb} GB RAM");
            }
            if (equipo.Nucleos != null)
            {
                partes.Add($"CPU de {equipo.Nucleos} núcleos");
            }
            // string.Join = unir las partes con " · " entre medio
            TxtHardware.Text = partes.Count > 0 ? string.Join(" · ", partes) : "Hardware no disponible";
        }

        private async void BtnDeshacerTodo_Click(object sender, RoutedEventArgs e)
        {
            var confirmar = new MessageBoxUi
            {
                Title = "¿Deshacer todos los cambios?",
                Content = "Windows volverá a la configuración que tenía antes de usar Ghost Mode.",
                PrimaryButtonText = "Deshacer todo",
                CloseButtonText = "Cancelar"
            };
            if (await confirmar.ShowDialogAsync() != MessageBoxResultUi.Primary)
            {
                return;
            }

            BtnDeshacerTodo.IsEnabled = false;   // evitar doble clic mientras trabaja
            ResultadoAplicacion resultado = await AplicadorOptimizaciones.DeshacerTodoAsync();
            // (el pie se actualiza solo gracias al evento EstadoCambiado)

            if (resultado.Fallidas.Count > 0)
            {
                // Select = sacar el Nombre de cada optimización fallida
                string nombres = string.Join("\n• ", resultado.Fallidas.Select(o => o.Nombre));
                await new MessageBoxUi
                {
                    Title = "No se pudo deshacer todo",
                    Content = $"Estos cambios siguen aplicados:\n• {nombres}\n\nRevisa el log para más detalles.",
                    CloseButtonText = "Entendido"
                }.ShowDialogAsync();
            }
        }
    }
}
