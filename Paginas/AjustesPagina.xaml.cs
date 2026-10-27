using System.Reflection;   // Leer los datos del propio programa (nombre, versión)
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;   // Key, Keyboard
using OptimizadorPC.Componentes;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.Sistema;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Paginas
{
    public partial class AjustesPagina : Page
    {
        public AjustesPagina()
        {
            InitializeComponent();

            // El "ensamblado" = el propio programa. Sus datos salen del .csproj al compilar,
            // así la versión que se ve aquí es siempre la del .exe, sin escribirla a mano
            Assembly app = Assembly.GetExecutingAssembly();

            TxtNombre.Text = app.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Ghost Mode";

            // InformationalVersion puede venir como "1.0.0+a1b2c3d" (el código del commit): nos quedamos con "1.0.0"
            string? version = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            TxtVersion.Text = version?.Split('+')[0] ?? app.GetName().Version?.ToString() ?? "desconocida";

            Loaded += async (s, e) =>
            {
                MostrarAtajo();
                MostrarMpo();
                IntSecuencia.IsChecked = AjustesApp.Actual.SecuenciaArranque;
                // Lo que manda es la tarea del Programador de tareas, no un ajuste guardado
                IntInicioWindows.IsChecked = await Task.Run(() => InicioAutomatico.Existe());
            };
        }

        private static MainWindow? Ventana => Application.Current.MainWindow as MainWindow;
        private static AtajoGlobal? Atajo => App.Actual?.Atajo;

        // ===== Iniciar con Windows =====

        private async void IntInicioWindows_Click(object sender, RoutedEventArgs e)
        {
            bool activar = IntInicioWindows.IsChecked == true;
            IntInicioWindows.Estado = EstadoInterruptor.Aplicando;
            try
            {
                string exe = Environment.ProcessPath ?? throw new InvalidOperationException("No se conoce la ruta del .exe");
                await Task.Run(() =>
                {
                    if (activar)
                    {
                        InicioAutomatico.Activar(exe);
                    }
                    else
                    {
                        InicioAutomatico.Desactivar();
                    }
                });
                IntInicioWindows.Estado = EstadoInterruptor.Normal;
                Avisos.Exito(activar ? "Ghost Mode se iniciará con Windows" : "Ghost Mode ya no se iniciará con Windows",
                             activar ? "Se abrirá minimizado en la bandeja al iniciar sesión." : "Se borró la tarea del Programador de tareas.");
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo {(activar ? "activar" : "desactivar")} el inicio con Windows", ex);
                IntInicioWindows.Estado = EstadoInterruptor.Error;
                IntInicioWindows.IsChecked = InicioAutomatico.Existe();
                Avisos.Error("No se pudo cambiar el inicio con Windows", "Revisa el log para más detalles.");
            }
        }

        // ===== Atajo global =====

        private Combinacion? nueva;

        private void MostrarAtajo()
        {
            Combinacion? actual = Atajo?.Actual;
            TxtAtajo.Text = actual?.ToString() ?? "";
            TxtEstadoAtajo.Text = actual != null ? $"> activo: {actual}" : "> sin atajo";
            BtnGuardarAtajo.IsEnabled = false;
        }

        // Se "graba" la combinación pulsada en el cuadro (no se escribe texto)
        private void TxtAtajo_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            Key tecla = e.Key == Key.System ? e.SystemKey : e.Key;   // con Alt, WPF manda Key.System
            nueva = Combinacion.Crear(Keyboard.Modifiers, tecla);
            TxtAtajo.Text = nueva?.ToString() ?? "";
            TxtEstadoAtajo.Text = nueva != null ? "> pulsa [ guardar ] para usarlo" : "> mantén Ctrl, Alt o Win y pulsa una tecla";
            BtnGuardarAtajo.IsEnabled = nueva != null;
        }

        private void BtnGuardarAtajo_Click(object sender, RoutedEventArgs e) => CambiarAtajo(nueva);

        private void BtnQuitarAtajo_Click(object sender, RoutedEventArgs e) => CambiarAtajo(null);

        private void CambiarAtajo(Combinacion? combinacion)
        {
            if (Atajo is not AtajoGlobal atajo)
            {
                return;
            }
            Combinacion? anterior = atajo.Actual;
            if (!atajo.Registrar(combinacion))
            {
                atajo.Registrar(anterior);   // se queda el que había
                MostrarAtajo();
                TxtEstadoAtajo.Text = $"> {combinacion} ya lo usa otra app: elige otra combinación";
                return;
            }
            AjustesApp.Actual.Atajo = combinacion?.ToString();
            AjustesApp.Actual.Guardar();
            MostrarAtajo();
            Avisos.Exito(combinacion != null ? $"Atajo {combinacion} guardado" : "Atajo quitado",
                         combinacion != null ? "Pulsa la combinación para activar o desactivar Ghost Mode." : "Ghost Mode ya no responde a ningún atajo.");
        }

        private void BtnBienvenida_Click(object sender, RoutedEventArgs e) => Ventana?.MostrarBienvenida();

        // ===== Secuencia de arranque =====

        private void IntSecuencia_Click(object sender, RoutedEventArgs e)
        {
            AjustesApp.Actual.SecuenciaArranque = IntSecuencia.IsChecked == true;
            // Si no se pudo guardar, el interruptor lo dice (y en esta sesión vale lo elegido)
            IntSecuencia.Estado = AjustesApp.Actual.Guardar() ? EstadoInterruptor.Normal : EstadoInterruptor.Error;
        }

        // ===== Solución de problemas: MPO =====

        private void MostrarMpo()
        {
            IntMpo.IsChecked = SolucionMpo.EstaDesactivado();
            TxtMpo.Text = IntMpo.IsChecked == true ? "> MPO desactivado (efectivo tras reiniciar)" : "> MPO activado (lo normal)";
        }

        private async void IntMpo_Click(object sender, RoutedEventArgs e)
        {
            bool desactivar = IntMpo.IsChecked == true;
            IntMpo.Estado = EstadoInterruptor.Aplicando;
            try
            {
                bool hecho = await AplicadorOptimizaciones.CambiarAsync(
                    desactivar ? SolucionMpo.Desactivar : SolucionMpo.Revertir, Avisos.PreguntarContinuarSinPuntoAsync);
                IntMpo.Estado = EstadoInterruptor.Normal;
                if (hecho)
                {
                    Avisos.Info(desactivar ? "MPO desactivado" : "MPO restaurado", "Reinicia Windows para que se aplique.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo {(desactivar ? "desactivar" : "restaurar")} MPO", ex);
                IntMpo.Estado = EstadoInterruptor.Error;
                Avisos.Error("No se pudo cambiar MPO", "Revisa el log para más detalles.");
            }
            MostrarMpo();
        }

        // ===== Exportar diagnóstico =====

        private async void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            var dialogo = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar diagnóstico de Ghost Mode",
                FileName = ExportadorDiagnostico.NombreSugerido,
                Filter = "Archivo ZIP (*.zip)|*.zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dialogo.ShowDialog() != true)
            {
                return;
            }

            BtnExportar.IsEnabled = false;
            BtnExportar.Content = "[ exportando... ]";
            try
            {
                await ExportadorDiagnostico.CrearAsync(dialogo.FileName);
                Avisos.Exito("Diagnóstico exportado", $"{System.IO.Path.GetFileName(dialogo.FileName)} está listo para enviar.");
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo exportar el diagnóstico", ex);
                Avisos.Error("No se pudo exportar el diagnóstico", ex.Message);
            }
            finally
            {
                BtnExportar.IsEnabled = true;
                BtnExportar.Content = "[ exportar diagnóstico ]";
            }
        }

        // Los enlaces llevan la URL en su Tag
        private void Enlace_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url })
            {
                Enlaces.Abrir(url);
            }
        }
    }
}
