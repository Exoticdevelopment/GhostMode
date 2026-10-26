using System.Windows;
using System.Windows.Media.Imaging;   // BitmapDecoder: leer las imágenes del .ico
using OptimizadorPC.Core;
using OptimizadorPC.Gaming;
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

            // Loaded = la primera vez que la ventana se muestra (si se arrancó minimizado, al abrirla desde la bandeja).
            // Las páginas se crean recién aquí: en la bandeja no ocupan memoria
            Loaded += async (s, e) =>
            {
                Navegacion.Navigate(typeof(InicioPagina));   // empezar en Inicio
                MarcarSeleccion();   // por si la primera navegación no lanza SelectionChanged
                ActualizarPie();
                await MostrarHardwareAsync();
            };

            // La bienvenida, cuando la ventana ya se ve (en Loaded aún no está en pantalla y quedaría detrás)
            ContentRendered += (s, e) => MostrarBienvenidaSiHaceFalta();

            Closing += AlCerrar;

            // Cada vez que se aplique o revierta algo (desde cualquier pantalla), refrescar el pie.
            // Al cerrar la ventana nos "desuscribimos" para no dejar el aviso colgado.
            AplicadorOptimizaciones.EstadoCambiado += ActualizarPie;
            Closed += (s, e) => AplicadorOptimizaciones.EstadoCambiado -= ActualizarPie;
        }

        // ===== Segundo plano: bandeja, atajo y Modo Gaming =====

        private VentanaMensajes? mensajes;
        private IconoBandeja? bandeja;
        private AtajoGlobal? atajo;
        private bool saliendo = false;            // "Salir" de verdad (no ocultar en la bandeja)
        private bool cerrandoTrasDeshacer = false;

        public AtajoGlobal? Atajo => atajo;

        // Lo llama App al arrancar, se muestre o no la ventana (con --minimizado no se muestra)
        public async Task IniciarSegundoPlanoAsync()
        {
            try
            {
                mensajes = new VentanaMensajes();
                bandeja = new IconoBandeja(mensajes) { GhostActivo = InterruptorGhost.EstaActivo };
                bandeja.Abrir += Mostrar;
                bandeja.AlternarGhost += async () => await InterruptorGhost.AlternarAsync("la bandeja");
                bandeja.DeshacerTodo += async () =>
                {
                    Mostrar();
                    await DeshacerTodoAsync();
                };
                bandeja.Salir += Salir;

                atajo = new AtajoGlobal(mensajes);
                atajo.Pulsado += async () => await InterruptorGhost.AlternarAsync("el atajo de teclado");
                if (!atajo.Registrar(Combinacion.Leer(AjustesApp.Actual.Atajo)) && AjustesApp.Actual.Atajo != null)
                {
                    Avisos.Error($"El atajo {AjustesApp.Actual.Atajo} no está disponible",
                                 "Otra app ya lo usa. Elige otro en Ajustes.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo crear el icono de la bandeja o el atajo global", ex);
            }

            if (Environment.ProcessPath is string exe)
            {
                await Task.Run(() => InicioAutomatico.CorregirRuta(exe));
            }
            await IniciarModoGamingAsync();
        }

        // Abrir desde la bandeja (o desde una segunda copia de la app que se intentó abrir)
        public void Mostrar()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        public void Salir()
        {
            saliendo = true;
            Close();
        }

        // La X de la ventana:
        //   - si Modo Gaming vigila juegos → se oculta en la bandeja y sigue vigilando
        //   - si no (o con "Salir") → se cierra de verdad, deshaciendo antes la sesión de juego si hay una
        private async void AlCerrar(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!saliendo && bandeja != null && SesionGaming.Actual.Vigilados > 0)
            {
                e.Cancel = true;
                Hide();
                if (!AjustesApp.Actual.AvisoBandejaMostrado)
                {
                    bandeja.Notificar("Ghost Mode sigue aquí",
                                      "Está vigilando tus juegos desde la bandeja. Clic derecho en el icono para salir.");
                    AjustesApp.Actual.AvisoBandejaMostrado = true;
                    AjustesApp.Actual.Guardar();
                }
                return;
            }

            SesionGaming.Actual.Detener();
            if (!cerrandoTrasDeshacer && SesionGaming.QuedoAlgoPendiente())
            {
                // Con una partida en marcha: primero se deshace la sesión de juego y después se cierra
                e.Cancel = true;   // "todavía no": se cierra al terminar de deshacer
                cerrandoTrasDeshacer = true;
                try
                {
                    await SesionGaming.Actual.TerminarAsync("Ghost Mode se cerró");
                }
                catch (Exception ex)
                {
                    // Lo que quede, se deshace al volver a abrir Ghost Mode (SesionGaming.RecuperarAsync)
                    Logger.Error("No se pudo deshacer la sesión de Modo Gaming al cerrar", ex);
                }
                Close();
                return;
            }

            atajo?.Dispose();
            bandeja?.Dispose();
            mensajes?.Dispose();
            Logger.Info("=== Ghost Mode cerrado ===");
            Application.Current.Shutdown();
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

            // Cada vez que se aplique o revierta algo (desde cualquier pantalla), refrescar el pie.
            // Al cerrar la ventana nos "desuscribimos" para no dejar el aviso colgado.
            AplicadorOptimizaciones.EstadoCambiado += ActualizarPie;
            Closed += (s, e) => AplicadorOptimizaciones.EstadoCambiado -= ActualizarPie;
        }

        // Modo Gaming vigila en segundo plano desde que se abre la app. Antes, deshace lo que hubiera
        // quedado de una sesión anterior que terminó mal (apps pausadas, prioridades...)
        private static async Task IniciarModoGamingAsync()
        {
            try
            {
                await SesionGaming.Actual.IniciarAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo iniciar Modo Gaming", ex);
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

            TxtEquipo.Text = $"HOST · {equipo.Windows.ToUpper()}";

            // Solo mostramos lo que se pudo leer: nada de datos inventados
            var partes = new List<string>();
            if (equipo.RamGb != null)
            {
                partes.Add($"RAM {equipo.RamGb} GB");
            }
            if (equipo.Nucleos != null)
            {
                partes.Add($"CPU {equipo.Nucleos} núcleos");
            }
            // string.Join = unir las partes con " · " entre medio
            TxtHardware.Text = partes.Count > 0 ? string.Join(" · ", partes) : "hardware no disponible";
        }

        // Enlaces del pie (la URL va en el Tag del botón)
        private void Enlace_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url })
            {
                Enlaces.Abrir(url);
            }
        }

        private async void BtnDeshacerTodo_Click(object sender, RoutedEventArgs e) => await DeshacerTodoAsync();

        // También desde el menú de la bandeja
        private async Task DeshacerTodoAsync()
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
