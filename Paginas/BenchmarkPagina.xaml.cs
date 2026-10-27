using System.Diagnostics;        // Process: la lista de programas abiertos
using System.Media;              // SystemSounds: aviso sonoro al terminar
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;  // DispatcherTimer: la cuenta atrás
using OptimizadorPC.Benchmark;
using OptimizadorPC.Core;
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Servicios;
// Alias: WPF y WPF-UI tienen los dos una clase "MessageBox". Así dejamos claro cuál usamos
using MessageBoxUi = Wpf.Ui.Controls.MessageBox;
using MessageBoxResultUi = Wpf.Ui.Controls.MessageBoxResult;

namespace OptimizadorPC.Paginas
{
    // Un programa con ventana que se puede medir. ToString = lo que muestra la lista desplegable
    internal record ProcesoJuego(int Id, string Nombre, string Titulo)
    {
        public override string ToString() => $"{Nombre}  —  {Titulo}";
    }

    public partial class BenchmarkPagina : Page
    {
        private const int Asentar = 5;   // segundos entre "aplicar" y "medir después" (igual que EjecutorBenchmark)
        private static readonly int TotalSegundos = EjecutorBenchmark.SegundosPorMedicion * 2 + Asentar;

        private CancellationTokenSource? cancelar;   // != null mientras hay un benchmark en marcha
        private List<IOptimizacion> pendientes = [];

        // Cuenta atrás de la etapa actual
        private readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(1) };
        private DateTime inicioEtapa;
        private int segundosEtapa;
        private int segundosPrevios;   // los de las etapas ya terminadas (para la barra total)

        public BenchmarkPagina()
        {
            InitializeComponent();
            reloj.Tick += (s, e) => ActualizarCuentaAtras();
            // Mientras el diagnóstico CPU/GPU mide, el benchmark espera (y al revés): PresentMon mide uno a la vez
            Diagnostico.EnMarchaCambiado += ActualizarBotones;

            Loaded += async (s, e) =>
            {
                CargarProcesos();
                MostrarHistorial();
                await LeerPendientesAsync();
            };
        }

        // ===== Paso 1: el juego =====

        private void CargarProcesos()
        {
            int? elegido = (ListaProcesos.SelectedItem as ProcesoJuego)?.Id;
            int yo = Environment.ProcessId;

            Process[] procesos = Process.GetProcesses();
            var conVentana = new List<ProcesoJuego>();
            foreach (Process p in procesos)
            {
                try
                {
                    // Solo programas con ventana visible y título (los juegos la tienen); nunca Ghost Mode
                    if (p.Id != yo && p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                    {
                        conVentana.Add(new ProcesoJuego(p.Id, p.ProcessName, p.MainWindowTitle));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // El proceso se cerró mientras lo mirábamos o es del sistema: se salta (es lo normal, no se anota).
                    // Cualquier otro error no se traga aquí: llega al registro de errores de la app
                }
                finally
                {
                    p.Dispose();
                }
            }

            ListaProcesos.ItemsSource = conVentana.OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
            ListaProcesos.SelectedItem = conVentana.FirstOrDefault(p => p.Id == elegido);
            ActualizarBotones();
        }

        private void BtnRefrescar_Click(object sender, RoutedEventArgs e) => CargarProcesos();

        private void ListaProcesos_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActualizarBotones();

        // ===== Paso 2: el plan =====

        // Qué optimizaciones de Ghost Mode faltan: son las que se aplicarán entre las dos mediciones
        private async Task LeerPendientesAsync()
        {
            pendientes = await Task.Run(EjecutorBenchmark.Pendientes);

            TxtPlan.Text = pendientes.Count == 0
                ? "Ghost Mode ya está activo, así que no hay un \"antes\" sin optimizar que medir. " +
                  "Desactívalo en Inicio y vuelve aquí."
                : $"Ghost Mode medirá {EjecutorBenchmark.SegundosPorMedicion} s sin optimizar, aplicará " +
                  $"{string.Join(", ", pendientes.Select(o => o.Nombre))} y medirá otros " +
                  $"{EjecutorBenchmark.SegundosPorMedicion} s. Juega con normalidad en la misma zona durante unos 2 minutos.";
            TxtPresentMon.Text = PresentMon.EstaDescargado
                ? "Los FPS se miden con PresentMon (Intel, licencia MIT)."
                : "La primera vez se descargará PresentMon (Intel, licencia MIT, ~1 MB) desde su GitHub oficial.";
            ActualizarBotones();
        }

        private void ActualizarBotones()
        {
            bool enMarcha = cancelar != null;
            bool diagnosticando = Diagnostico.EnMarcha;
            BtnIniciar.IsEnabled = !enMarcha && !diagnosticando && ListaProcesos.SelectedItem != null && pendientes.Count > 0;
            BtnRefrescar.IsEnabled = !enMarcha && !diagnosticando;
            ListaProcesos.IsEnabled = !enMarcha && !diagnosticando;

            var juego = ListaProcesos.SelectedItem as ProcesoJuego;
            Diagnostico.ElegirJuego(juego?.Id, juego?.Nombre);
            Diagnostico.Bloqueado = enMarcha;
            BtnCancelar.Visibility = enMarcha ? Visibility.Visible : Visibility.Collapsed;
            PanelProgreso.Visibility = enMarcha ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== Ejecutar =====

        private async void BtnIniciar_Click(object sender, RoutedEventArgs e)
        {
            if (ListaProcesos.SelectedItem is not ProcesoJuego juego)
            {
                return;
            }

            var confirmar = new MessageBoxUi
            {
                Title = $"¿Medir {juego.Nombre}?",
                Content = "Al pulsar «Empezar», vuelve al juego y juega con normalidad unos 2 minutos, " +
                          "a ser posible en la misma zona.\n\nEn medio se aplicarán estas optimizaciones (se " +
                          $"quedan aplicadas; se deshacen con «Deshacer todo»):\n" +
                          string.Join("\n", pendientes.Select(o => $"• {o.Nombre}")) +
                          "\n\nSonará un aviso de Windows al terminar.",
                PrimaryButtonText = "Empezar",
                CloseButtonText = "Cancelar"
            };
            if (await confirmar.ShowDialogAsync() != MessageBoxResultUi.Primary)
            {
                return;
            }

            // El punto de restauración AHORA: así no aparece ningún diálogo en mitad de la partida
            if (!await AplicadorOptimizaciones.PrepararPuntoRestauracionAsync(Avisos.PreguntarContinuarSinPuntoAsync))
            {
                return;
            }

            cancelar = new CancellationTokenSource();
            segundosPrevios = 0;
            ActualizarBotones();
            reloj.Start();

            // Progress<T> = recibe los avisos de etapa y los entrega en el hilo de la interfaz
            var progreso = new Progress<EtapaBenchmark>(MostrarEtapa);
            try
            {
                ResultadoBenchmark resultado = await EjecutorBenchmark.EjecutarAsync(
                    juego.Id, juego.Nombre, pendientes, progreso, cancelar.Token);

                var vm = new BenchmarkVM(resultado);
                MostrarResultado(vm);
                MostrarHistorial();
                SystemSounds.Asterisk.Play();   // el usuario está en el juego: un sonido le avisa
                Avisos.Exito($"Benchmark terminado: {vm.TextoMejora} FPS promedio", $"{vm.TextoPromedio} · 1% low {vm.Texto1Low}");
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Benchmark cancelado por el usuario");
                Avisos.Info("Benchmark cancelado", "No se guardó ningún resultado. Si ya se aplicaron optimizaciones, siguen activas.");
            }
            catch (Exception ex)
            {
                Logger.Error("Falló el benchmark", ex);
                SystemSounds.Hand.Play();
                Avisos.Error("No se pudo completar el benchmark", ex.Message);
            }
            finally
            {
                reloj.Stop();
                cancelar.Dispose();
                cancelar = null;
                await LeerPendientesAsync();   // tras aplicar, ya no queda nada pendiente: se muestra la verdad
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => cancelar?.Cancel();

        // Texto y duración de cada etapa
        private void MostrarEtapa(EtapaBenchmark etapa)
        {
            segundosPrevios += segundosEtapa;   // la etapa anterior ya terminó
            (string texto, int segundos) = etapa switch
            {
                EtapaBenchmark.Descargando => ("Descargando PresentMon…", 0),
                EtapaBenchmark.MidiendoAntes => ("Midiendo sin optimizar…", EjecutorBenchmark.SegundosPorMedicion),
                EtapaBenchmark.Aplicando => ("Aplicando Ghost Mode…", 0),
                EtapaBenchmark.Asentando => ("Esperando a que los cambios hagan efecto…", Asentar),
                _ => ("Midiendo con Ghost Mode…", EjecutorBenchmark.SegundosPorMedicion)
            };
            if (etapa == EtapaBenchmark.Descargando)
            {
                segundosPrevios = 0;   // la descarga no cuenta para la barra
            }
            TxtEtapa.Text = texto;
            inicioEtapa = DateTime.Now;
            segundosEtapa = segundos;
            ActualizarCuentaAtras();
        }

        private void ActualizarCuentaAtras()
        {
            int pasados = Math.Min((int)(DateTime.Now - inicioEtapa).TotalSeconds, segundosEtapa);
            TxtRestante.Text = segundosEtapa > 0 ? $"{segundosEtapa - pasados} s restantes" : "";
            BarraProgreso.Value = 100.0 * (segundosPrevios + pasados) / TotalSegundos;
        }

        // ===== Resultado e historial =====

        private void MostrarHistorial()
        {
            // Los más nuevos arriba
            var filas = HistorialBenchmarks.Todos.Reverse().Select(r => new BenchmarkVM(r)).ToList();
            ListaHistorial.ItemsSource = filas;
            TxtSinHistorial.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (TarjetaResultado.Visibility != Visibility.Visible && filas.Count > 0)
            {
                MostrarResultado(filas[0]);
            }
        }

        private void FilaHistorial_Click(object sender, RoutedEventArgs e)
        {
            // DataContext de la fila = su BenchmarkVM
            if (sender is FrameworkElement { DataContext: BenchmarkVM vm })
            {
                MostrarResultado(vm);
            }
        }

        private void MostrarResultado(BenchmarkVM vm)
        {
            ResultadoBenchmark r = vm.Resultado;
            TarjetaResultado.Visibility = Visibility.Visible;

            TxtResJuego.Text = vm.Juego;
            TxtResFecha.Text = $"{vm.Fecha}, {EjecutorBenchmark.SegundosPorMedicion} s por medición, " +
                               $"{r.Antes.Frames + r.Despues.Frames} frames medidos";
            TxtResMejora.Text = vm.TextoMejora;   // la etiqueta "fps promedio" ya está debajo, en el XAML
            // TryFindResource = buscar un color de Colores.xaml por su nombre. Verde = mejoró (resultado OK)
            TxtResMejora.Foreground = (Brush)TryFindResource(vm.Mejoro ? "ExitoBrush" : "AvisoBrush");

            // Las 4 barras comparten escala: la más alta llena la barra
            double maximo = new[] { r.Antes.FpsPromedio, r.Despues.FpsPromedio, r.Antes.Fps1Low, r.Despues.Fps1Low }.Max();
            BarraPromAntes.Value = 100 * r.Antes.FpsPromedio / maximo;
            BarraPromDespues.Value = 100 * r.Despues.FpsPromedio / maximo;
            BarraLowAntes.Value = 100 * r.Antes.Fps1Low / maximo;
            BarraLowDespues.Value = 100 * r.Despues.Fps1Low / maximo;
            TxtPromAntes.Text = $"{BenchmarkVM.Fps(r.Antes.FpsPromedio)} FPS";
            TxtPromDespues.Text = $"{BenchmarkVM.Fps(r.Despues.FpsPromedio)} FPS";
            TxtLowAntes.Text = $"{BenchmarkVM.Fps(r.Antes.Fps1Low)} FPS";
            TxtLowDespues.Text = $"{BenchmarkVM.Fps(r.Despues.Fps1Low)} FPS ({vm.TextoMejora1Low})";

            GraficaFps.MostrarSeries(r.Despues.FpsPorSegundo, r.Antes.FpsPorSegundo);
            double techo = r.Antes.FpsPorSegundo.Concat(r.Despues.FpsPorSegundo).DefaultIfEmpty(0).Max();
            TxtEscala.Text = $"máximo: {BenchmarkVM.Fps(techo)} FPS en un segundo; cada punto es un segundo";

            TxtResOptimizaciones.Text = $"Optimizaciones aplicadas entre las dos mediciones: {vm.TextoOptimizaciones}.";
        }
    }
}
