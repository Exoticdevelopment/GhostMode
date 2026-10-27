using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;   // Shape (el cuadradito de estado)
using Microsoft.Win32;   // OpenFileDialog
using OptimizadorPC.Componentes;
using OptimizadorPC.Core;
using OptimizadorPC.Gaming;
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Paginas
{
    public partial class ModoGamingPagina : Page
    {
        // La sesión de la app: vigila siempre, aunque esta página no se vea
        private static SesionGaming Sesion => SesionGaming.Actual;

        private List<JuegoVM> juegos = [];
        // ObservableCollection = lista que avisa a la pantalla cuando se agrega o quita algo
        private readonly ObservableCollection<string> apps = [];

        public ModoGamingPagina()
        {
            InitializeComponent();
            ListaApps.ItemsSource = apps;

            int n = AplicadorOptimizaciones.GhostMode.Count;
            TxtOptimizaciones.Text = $"Las {n} del botón de Inicio, solo mientras dura el juego (las que ya tengas puestas no se tocan).";

            Loaded += async (s, e) =>
            {
                Sesion.Cambiado += MostrarEstado;
                MostrarOpciones();
                MostrarJuegos();
                // Volver a buscar: el usuario pudo instalar algo con la app abierta
                await Sesion.RecargarJuegosAsync();
                MostrarJuegos();
            };
            Unloaded += (s, e) => Sesion.Cambiado -= MostrarEstado;
        }

        // ===== Juegos =====

        private void MostrarJuegos()
        {
            foreach (var vm in juegos)
            {
                vm.CambioPedido -= AlCambiarAutomatico;
            }
            // El ícono se lee una vez por juego al mostrar la lista
            juegos = Sesion.Juegos
                .Select(j => new JuegoVM(j, Sesion.Config.EsAutomatico(j), IconoArchivo.De(j.Exe)))
                .ToList();
            foreach (var vm in juegos)
            {
                vm.CambioPedido += AlCambiarAutomatico;
            }

            ListaJuegos.ItemsSource = juegos;
            TxtCuantos.Text = juegos.Count == 1 ? "1 juego" : $"{juegos.Count} juegos";
            PanelVacio.Visibility = juegos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtVacio.Text = "> sin_juegos_detectados";
            TxtVacioDetalle.Visibility = Visibility.Visible;
            MostrarEstado();
        }

        // "> vigilando 2 juegos, cada 2 s"  o  "> ghost_mode activo: FiveM" (morado: es Ghost Mode encendido)
        private void MostrarEstado()
        {
            bool activa = Sesion.Activa;
            string pintura = activa ? "AcentoBrilloBrush" : "TextoSecundarioBrush";
            int vigilados = Sesion.Vigilados;

            TxtEstado.Text = activa
                ? $"> ghost_mode activo: {Sesion.JuegoActivo!.Nombre}"
                : vigilados == 0
                    ? "> en espera: activa la autoactivación de algún juego"
                    : $"> vigilando {vigilados} {(vigilados == 1 ? "juego" : "juegos")}, cada {SesionGaming.Intervalo.TotalSeconds:0} s";
            TxtEstado.SetResourceReference(TextBlock.ForegroundProperty, pintura);
            PuntoEstado.SetResourceReference(Shape.FillProperty, pintura);
            PastillaEstado.SetResourceReference(Border.BorderBrushProperty, activa ? "AcentoBrush" : "BordeHoverBrush");
        }

        private async void AlCambiarAutomatico(JuegoVM vm, bool activo)
        {
            try
            {
                await Sesion.CambiarAutomaticoAsync(vm.Juego, activo);
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo guardar la activación automática de {vm.Nombre}", ex);
                Avisos.Error("No se pudo guardar el cambio", "Revisa el log para más detalles.");
            }
            MostrarEstado();
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            var dialogo = new OpenFileDialog
            {
                Title = "Elige el .exe del juego",
                Filter = "Programas (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (dialogo.ShowDialog() != true)
            {
                return;
            }

            try
            {
                Juego juego = Sesion.AgregarManual(dialogo.FileName);
                MostrarJuegos();
                Avisos.Exito($"{juego.Nombre} agregado", "Ghost Mode se activará solo cuando lo abras.");
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo agregar el juego", ex);
                Avisos.Error("No se pudo agregar el juego", "Revisa el log para más detalles.");
            }
        }

        private async void BtnQuitar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: JuegoVM vm })
            {
                await Sesion.QuitarManualAsync(vm.Juego);
                MostrarJuegos();
            }
        }

        // ===== Opciones =====

        private void MostrarOpciones()
        {
            ConfigGaming c = Sesion.Config;
            IntPrioridad.IsChecked = c.PrioridadAlta;
            IntSuspender.IsChecked = c.SuspenderApps;
            IntLiberar.IsChecked = c.LiberarRam;
            IntOptimizaciones.IsChecked = c.OptimizacionesInicio;
            IntGrabacion.IsChecked = c.PausarGrabacion;
            IntNvidia.IsChecked = c.EnergiaNvidia;
            if (!PerfilNvidia.Sistema.Disponible)
            {
                IntNvidia.IsEnabled = false;
                TxtNvidia.Text = "No hay GPU NVIDIA (o su driver) en este equipo: esta opción no se usa.";
            }

            apps.Clear();
            foreach (string app in c.AppsSuspender)
            {
                apps.Add(app);
            }
        }

        // Cualquier interruptor de opciones: se guarda al momento. Vale para la próxima partida
        private void Opcion_Click(object sender, RoutedEventArgs e)
        {
            ConfigGaming c = Sesion.Config;
            c.PrioridadAlta = IntPrioridad.IsChecked == true;
            c.SuspenderApps = IntSuspender.IsChecked == true;
            c.LiberarRam = IntLiberar.IsChecked == true;
            c.OptimizacionesInicio = IntOptimizaciones.IsChecked == true;
            c.PausarGrabacion = IntGrabacion.IsChecked == true;
            c.EnergiaNvidia = IntNvidia.IsChecked == true;
            GuardarOpciones();

            if (sender is InterruptorEstado interruptor)
            {
                Logger.Info($"Modo gaming: opción «{NombreOpcion(interruptor)}» {(interruptor.IsChecked == true ? "encendida" : "apagada")}");
            }
        }

        private string NombreOpcion(InterruptorEstado i) =>
            i == IntPrioridad ? "prioridad alta" :
            i == IntSuspender ? "pausar apps" :
            i == IntLiberar ? "liberar memoria" :
            i == IntGrabacion ? "pausar la grabación de Game DVR" :
            i == IntNvidia ? "GPU NVIDIA a máximo rendimiento" : "optimizaciones de Inicio";

        private void BtnAgregarApp_Click(object sender, RoutedEventArgs e) => AgregarApp();

        private void TxtNuevaApp_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AgregarApp();
            }
        }

        private void AgregarApp()
        {
            string nombre = TxtNuevaApp.Text.Trim();
            if (nombre.Length > 0 && !nombre.Contains('.'))
            {
                nombre += ".exe";   // "spotify" → "spotify.exe"
            }

            string? motivo = SuspensorApps.MotivoRechazo(nombre);
            if (motivo == null && apps.Contains(nombre, StringComparer.OrdinalIgnoreCase))
            {
                motivo = $"{nombre} ya está en la lista.";
            }
            TxtErrorApp.Text = motivo ?? "";
            TxtErrorApp.Visibility = motivo != null ? Visibility.Visible : Visibility.Collapsed;
            if (motivo != null)
            {
                return;
            }

            apps.Add(nombre);
            TxtNuevaApp.Text = "";
            GuardarApps();
            Logger.Info($"Modo gaming: {nombre} agregado a las apps a pausar");
        }

        private void BtnQuitarApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string nombre })
            {
                apps.Remove(nombre);
                GuardarApps();
                Logger.Info($"Modo gaming: {nombre} quitado de las apps a pausar");
            }
        }

        private void BtnRestaurarLista_Click(object sender, RoutedEventArgs e)
        {
            apps.Clear();
            foreach (string app in SuspensorApps.PorDefecto)
            {
                apps.Add(app);
            }
            GuardarApps();
            Logger.Info("Modo gaming: lista de apps a pausar restaurada a la de por defecto");
        }

        private void GuardarApps()
        {
            Sesion.Config.AppsSuspender = apps.ToList();
            GuardarOpciones();
        }

        private void GuardarOpciones()
        {
            try
            {
                Sesion.GuardarConfig();
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo guardar modo_gaming.json", ex);
                Avisos.Error("No se pudo guardar el cambio", "Revisa el log para más detalles.");
            }
        }
    }
}
