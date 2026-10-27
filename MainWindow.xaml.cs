using System.Windows;
using System.Windows.Media.Imaging;   // BitmapDecoder: leer las imágenes del .ico
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

            // Decirle a Avisos dónde mostrar los toasts
            Avisos.Presentador = PresentadorAvisos;

            Logo.Source = ImagenDelIcono(68);   // 34 px en pantalla, nítido aun con escalado al 200 %

            // Cada vez que cambia la sección elegida, poner el ">" delante de su nombre
            Navegacion.SelectionChanged += (s, e) => MarcarSeleccion();

            // Loaded = cuando la ventana ya está construida y lista para dibujarse. (La ventana solo existe mientras
            // está abierta: al cerrarla con la X se destruye y App la vuelve a crear desde la bandeja)
            Loaded += async (s, e) =>
            {
                Navegacion.Navigate(typeof(InicioPagina));   // empezar en Inicio
                MarcarSeleccion();   // por si la primera navegación no lanza SelectionChanged
                ActualizarPie();
                await MostrarHardwareAsync();
            };

            // La bienvenida, cuando la ventana ya se ve (en Loaded aún no está en pantalla y quedaría detrás)
            ContentRendered += (s, e) => MostrarBienvenidaSiHaceFalta();

            // Cada vez que se aplique o revierta algo (desde cualquier pantalla), refrescar el pie.
            // Al cerrar la ventana nos "desuscribimos" para no dejar el aviso colgado.
            AplicadorOptimizaciones.EstadoCambiado += ActualizarPie;
            Closed += (s, e) =>
            {
                AplicadorOptimizaciones.EstadoCambiado -= ActualizarPie;
                Avisos.Presentador = null;   // sin ventana, los avisos salen desde la bandeja
            };
        }

        private void MostrarBienvenidaSiHaceFalta()
        {
            if (AjustesApp.Actual.BienvenidaVista)
            {
                return;
            }
            AjustesApp.Actual.BienvenidaVista = true;
            AjustesApp.Actual.Guardar();
            MostrarBienvenida();
        }

        public void MostrarBienvenida()
        {
            try
            {
                new BienvenidaVentana { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo mostrar la bienvenida", ex);
            }
        }

        // Un .ico trae la misma imagen en varios tamaños (16, 24, 32... 256 px). Si se carga desde XAML,
        // WPF suele quedarse con la primera (16 px) y a 40 px se vería borrosa.
        // Aquí elegimos la más pequeña que mida al menos "minimo" px (el doble del tamaño en pantalla = nítida aun con escalado al 200 %).
        private static BitmapFrame ImagenDelIcono(int minimo)
        {
            var decodificador = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/ghostmode.ico"),
                                                     BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decodificador.Frames
                .OrderBy(f => f.PixelWidth)
                .FirstOrDefault(f => f.PixelWidth >= minimo)
                ?? decodificador.Frames.OrderBy(f => f.PixelWidth).Last();   // si ninguno llega, el más grande
        }

        // "> inicio" el elegido, "  inicio" el resto (los espacios mantienen el texto alineado).
        // El nombre base de cada ítem está en su Tag (MainWindow.xaml).
        // OJO: en el contenido de botones e ítems, WPF toma "_" como marca de tecla de acceso (como en "_Archivo")
        // y se lo come: "modo_gaming" se vería "modogaming". Escrito doble ("__") se muestra un "_" normal
        private void MarcarSeleccion()
        {
            foreach (var item in Navegacion.MenuItems.OfType<NavigationViewItem>())
            {
                string nombre = (item.Tag as string ?? "").Replace("_", "__");
                bool elegido = ReferenceEquals(item, Navegacion.SelectedItem);
                item.Content = elegido ? $"> {nombre}" : $"  {nombre}";

                // El elegido tiene fondo morado oscuro: texto claro (sobre morado nunca va gris ni morado).
                // WPF-UI no tiene un color propio para el texto del elegido, por eso se pone aquí.
                // ClearValue = los demás vuelven al gris del tema (y al blanco al pasar el ratón)
                if (elegido)
                {
                    item.SetResourceReference(ForegroundProperty, "TextoBrush");
                }
                else
                {
                    item.ClearValue(ForegroundProperty);
                }
            }
        }

        // "Cambios guardados" = hay valores originales guardados que se pueden deshacer
        private void ActualizarPie()
        {
            bool hayCambios = GestorEstado.HayCambiosPendientes;

            TxtCambios.Text = hayCambios ? "cambios_guardados" : "sin_cambios";
            IconoCambios.Visibility = hayCambios ? Visibility.Visible : Visibility.Collapsed;
            BtnDeshacerTodo.IsEnabled = hayCambios;
        }

        private async Task MostrarHardwareAsync()
        {
            DatosEquipo equipo = await InfoSistema.ObtenerAsync();

            TxtEquipo.Text = equipo.Windows.ToLower();

            // Solo mostramos lo que se pudo leer: nada de datos inventados
            var partes = new List<string>();
            if (equipo.RamGb != null)
            {
                partes.Add($"ram {equipo.RamGb} GB");
            }
            if (equipo.Nucleos != null)
            {
                partes.Add($"cpu {equipo.Nucleos} núcleos");
            }
            // string.Join = unir las partes; "\n" = salto de línea: un dato por línea, como en una consola
            TxtHardware.Text = partes.Count > 0 ? string.Join("\n", partes) : "hardware no disponible";
        }

        private async void BtnDeshacerTodo_Click(object sender, RoutedEventArgs e) => await DeshacerTodoAsync();

        // También desde el menú de la bandeja
        public async Task DeshacerTodoAsync()
        {
            if (!GestorEstado.HayCambiosPendientes)
            {
                Avisos.Info("No hay cambios que deshacer", "Windows está como antes de usar Ghost Mode.");
                return;
            }

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
            ResultadoAplicacion resultado;
            try
            {
                resultado = await AplicadorOptimizaciones.DeshacerTodoAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Deshacer todo falló", ex);
                Avisos.Error("No se pudo deshacer todo", "Revisa el log para más detalles.");
                ActualizarPie();
                return;
            }
            // (el pie se actualiza solo gracias al evento EstadoCambiado)

            if (!resultado.TodoBien)
            {
                string nombres = string.Join("\n• ", resultado.NombresFallidos);
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
