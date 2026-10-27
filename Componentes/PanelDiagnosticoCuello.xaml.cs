using System.Diagnostics;        // Process: comprobar que el juego sigue abierto
using System.Media;              // SystemSounds: aviso sonoro al terminar cada medición
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;  // DispatcherTimer: la cuenta atrás
using OptimizadorPC.Benchmark;
using OptimizadorPC.Core;
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Componentes
{
    // Una línea de "qué hacer" tal como se dibuja
    public record RecomendacionCuelloVM(string Marca, string Texto, Brush Color);

    // Diagnóstico CPU/GPU en tres pasos: medir tal como está → el usuario baja la resolución → medir otra vez.
    // El juego lo elige la página (ElegirJuego); mientras mide, la página bloquea su propio benchmark (EnMarchaCambiado)
    public partial class PanelDiagnosticoCuello : UserControl
    {
        private enum Paso { Inicio, Resolucion, Resultado }

        private const int SegundosParaVolver = 5;   // tiempo para volver al juego antes de medir

        public event Action? EnMarchaCambiado;
        public bool EnMarcha => cancelar != null;

        private Paso paso = Paso.Inicio;
        private int? procesoId;           // el juego elegido en la página
        private string? nombreJuego;
        private bool bloqueado;           // hay un benchmark en marcha en la página
        private CancellationTokenSource? cancelar;

        private MedicionFps? normal;      // la primera medición
        private int? procesoMedido;       // el juego de la primera medición (la segunda tiene que ser el mismo)

        // Cuenta atrás de la medición en curso
        private readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(1) };
        private DateTime inicioMedicion;
        private int segundosEtapa;        // 0 = etapa sin duración fija (cuenta atrás previa o descarga)

        public PanelDiagnosticoCuello()
        {
            InitializeComponent();
            reloj.Tick += (s, e) => ActualizarCuentaAtras();
            Actualizar();
        }

        // La página avisa del juego elegido. Si cambia entre las dos mediciones, la primera ya no vale
        public void ElegirJuego(int? id, string? nombre)
        {
            if (EnMarcha)
            {
                return;
            }
            procesoId = id;
            nombreJuego = nombre;
            if (paso == Paso.Resolucion && id != procesoMedido)
            {
                paso = Paso.Inicio;
                normal = null;
            }
            Actualizar();
        }

        // true mientras la página mide su benchmark: no se pueden medir los dos a la vez
        public bool Bloqueado
        {
            set
            {
                bloqueado = value;
                Actualizar();
            }
        }

        private void BtnMedirNormal_Click(object sender, RoutedEventArgs e) => _ = MedirAsync(primera: true);

        private void BtnMedirMinima_Click(object sender, RoutedEventArgs e) => _ = MedirAsync(primera: false);

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => cancelar?.Cancel();

        private void BtnEmpezarDeNuevo_Click(object sender, RoutedEventArgs e)
        {
            paso = Paso.Inicio;
            normal = null;
            Actualizar();
        }

        private async Task MedirAsync(bool primera)
        {
            int? id = primera ? procesoId : procesoMedido;
            if (id is not int pid || (!primera && normal == null))
            {
                return;
            }
            if (!SigueAbierto(pid))
            {
                Avisos.Error("El juego ya no está abierto", "Ábrelo, elígelo en el paso 1 y empieza el diagnóstico de nuevo.");
                paso = Paso.Inicio;
                Actualizar();
                return;
            }

            cancelar = new CancellationTokenSource();
            Actualizar();
            EnMarchaCambiado?.Invoke();
            try
            {
                // Tiempo para volver al juego: medir con Ghost Mode en primer plano daría otros FPS
                for (int quedan = SegundosParaVolver; quedan > 0; quedan--)
                {
                    MostrarEtapa($"Vuelve al juego: la medición empieza en {quedan} s", 0);
                    await Task.Delay(1000, cancelar.Token);
                }
                MostrarEtapa(primera ? "Midiendo con el juego tal como está…" : "Midiendo con la resolución mínima…",
                             DiagnosticoCuello.SegundosPorMedicion);

                MedicionFps medicion = await DiagnosticoCuello.MedirAsync(
                    pid, () => MostrarEtapa("Descargando PresentMon…", 0), cancelar.Token);

                if (primera)
                {
                    normal = medicion;
                    procesoMedido = pid;
                    paso = Paso.Resolucion;
                    TxtPrimeraMedicion.Text = $"> tal como está: {TextoMedicion(medicion)}";
                }
                else
                {
                    ResultadoCuello resultado = DiagnosticoCuello.Evaluar(normal!, medicion);
                    MostrarResultado(resultado);
                    paso = Paso.Resultado;
                    Logger.Info($"Diagnóstico CPU/GPU de {nombreJuego}: {normal!.FpsPromedio:F1} → {medicion.FpsPromedio:F1} FPS " +
                                $"({resultado.CambioPorcentaje:+0.0;-0.0} %) = {resultado.Cuello}");
                }
                SystemSounds.Asterisk.Play();   // el usuario está en el juego: un sonido le avisa
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Diagnóstico CPU/GPU cancelado por el usuario");
            }
            catch (Exception ex)
            {
                Logger.Error("Falló el diagnóstico CPU/GPU", ex);
                SystemSounds.Hand.Play();
                Avisos.Error("No se pudo medir", ex.Message);
            }
            finally
            {
                reloj.Stop();
                cancelar.Dispose();
                cancelar = null;
                Actualizar();
                EnMarchaCambiado?.Invoke();
            }
        }

        private static bool SigueAbierto(int pid)
        {
            try
            {
                using Process p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch (ArgumentException)
            {
                return false;   // ya no existe ningún proceso con ese número
            }
        }

        // ===== Dibujo =====

        private void Actualizar()
        {
            bool enMarcha = EnMarcha;
            PanelInicio.Visibility = paso == Paso.Inicio && !enMarcha ? Visibility.Visible : Visibility.Collapsed;
            PanelResolucion.Visibility = paso == Paso.Resolucion ? Visibility.Visible : Visibility.Collapsed;
            PanelResultado.Visibility = paso == Paso.Resultado && !enMarcha ? Visibility.Visible : Visibility.Collapsed;
            PanelProgreso.Visibility = enMarcha ? Visibility.Visible : Visibility.Collapsed;

            BtnMedirNormal.Visibility = paso == Paso.Inicio ? Visibility.Visible : Visibility.Collapsed;
            BtnMedirNormal.IsEnabled = !enMarcha && !bloqueado && procesoId != null;
            BtnMedirMinima.Visibility = paso == Paso.Resolucion ? Visibility.Visible : Visibility.Collapsed;
            BtnMedirMinima.IsEnabled = !enMarcha && !bloqueado;
            BtnCancelar.Visibility = enMarcha ? Visibility.Visible : Visibility.Collapsed;
            BtnEmpezarDeNuevo.Visibility = paso != Paso.Inicio && !enMarcha ? Visibility.Visible : Visibility.Collapsed;
            BtnEmpezarDeNuevo.Content = paso == Paso.Resultado ? "[ repetir diagnóstico ]" : "[ empezar de nuevo ]";
            // En el resultado es el único botón: sin el hueco que lo separa del de medir
            BtnEmpezarDeNuevo.Margin = new Thickness(paso == Paso.Resultado ? 0 : 12, 0, 0, 0);
            BtnEmpezarDeNuevo.IsEnabled = !bloqueado;
            TxtSinJuego.Visibility = paso == Paso.Inicio && procesoId == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MostrarEtapa(string texto, int segundos)
        {
            TxtEtapa.Text = texto;
            inicioMedicion = DateTime.Now;
            segundosEtapa = segundos;
            if (segundos > 0)
            {
                reloj.Start();
            }
            else
            {
                reloj.Stop();
            }
            ActualizarCuentaAtras();
        }

        private void ActualizarCuentaAtras()
        {
            int segundos = segundosEtapa;
            int pasados = Math.Min((int)(DateTime.Now - inicioMedicion).TotalSeconds, segundos);
            TxtRestante.Text = segundos > 0 ? $"{segundos - pasados} s restantes" : "";
            BarraProgreso.Value = segundos > 0 ? 100.0 * pasados / segundos : 0;
        }

        private void MostrarResultado(ResultadoCuello r)
        {
            string cambio = $"{r.CambioPorcentaje.ToString("+0;-0;0", InfoSistema.Es)} %";

            TxtCuello.Text = r.Cuello == CuelloBotella.CpuOServidor ? "> límite: cpu o servidor" : "> límite: gpu";
            TxtResNormal.Text = TextoMedicion(r.Normal);
            TxtResMinima.Text = TextoMedicion(r.ResolucionMinima);
            TxtResCambio.Text = cambio;
            TxtExplicacion.Text = DiagnosticoCuello.Explicacion(r.Cuello, cambio);

            Brush claro = (Brush)FindResource("TextoBrush");
            Brush tenue = (Brush)FindResource("TextoSecundarioBrush");
            ListaRecomendaciones.ItemsSource = DiagnosticoCuello.Recomendaciones(r.Cuello)
                .Select(x => x.Pendiente ? new RecomendacionCuelloVM("…", x.Texto, tenue)
                           : x.Sirve ? new RecomendacionCuelloVM("+", x.Texto, claro)
                           : new RecomendacionCuelloVM("×", x.Texto, tenue))
                .ToList();
        }

        private static string TextoMedicion(MedicionFps m) =>
            $"{BenchmarkVM.Fps(m.FpsPromedio)} FPS promedio, 1% low {BenchmarkVM.Fps(m.Fps1Low)}";
    }
}
