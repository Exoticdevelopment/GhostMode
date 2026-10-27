using System.IO;   // DriveInfo: espacio libre del disco
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OptimizadorPC.Benchmark;
using OptimizadorPC.Core;
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Servicios;
// Alias: WPF y WPF-UI tienen los dos una clase "MessageBox". Así dejamos claro cuál usamos
using MessageBoxUi = Wpf.Ui.Controls.MessageBox;
using MessageBoxResultUi = Wpf.Ui.Controls.MessageBoxResult;

namespace OptimizadorPC.Paginas
{
    public partial class InicioPagina : Page
    {
        // Las optimizaciones de "Ghost Mode" (todas las seguras menos las de FiveM; ver AplicadorOptimizaciones)
        private static readonly IReadOnlyList<IOptimizacion> Seguras = AplicadorOptimizaciones.GhostMode;

        private readonly MonitorRecursos monitor = new();
        private DatosEquipo? equipo;      // hardware fijo (núcleos, GHz, RAM instalada, nombre de la GPU)
        private bool ghostActivo = false; // ¿están aplicadas todas las seguras?
        private bool trabajando = false;  // aplicando o revirtiendo ahora mismo
        private bool avisoDiscoDado = false; // el fallo del disco C: se anota en el log una sola vez, no cada 2 s

        public InicioPagina()
        {
            InitializeComponent();

            TxtCuantasSeguras.Text = Seguras.Count == 1 ? "+ 1 optimización segura" : $"+ {Seguras.Count} optimizaciones seguras";
            monitor.Actualizado += MostrarMuestra;

            Loaded += async (s, e) =>
            {
                AplicadorOptimizaciones.EstadoCambiado += AlCambiarEstado;
                HistorialBenchmarks.Cambiado += MostrarPuntuacion;
                MostrarPuntuacion();
                monitor.Iniciar();
                await LeerEstadoGhostAsync();
                equipo = await InfoSistema.ObtenerAsync();
                MostrarDatosFijos();
            };
            Unloaded += (s, e) =>
            {
                // Fuera de la pantalla no medimos nada: cero consumo
                monitor.Detener();
                AplicadorOptimizaciones.EstadoCambiado -= AlCambiarEstado;
                HistorialBenchmarks.Cambiado -= MostrarPuntuacion;
            };
        }

        // ===== Medidores =====

        // Lo que no cambia: textos de abajo de CPU y GPU
        private void MostrarDatosFijos()
        {
            if (equipo == null)
            {
                return;
            }

            var cpu = new List<string>();
            if (equipo.Nucleos != null)
            {
                cpu.Add($"{equipo.Nucleos} núcleos");
            }
            if (equipo.Ghz != null)
            {
                // "F2" = dos decimales; InfoSistema.Es = con coma, como en español: "2,40 GHz"
                cpu.Add($"{equipo.Ghz.Value.ToString("F2", InfoSistema.Es)} GHz");
            }
            MedCpu.Detalle = string.Join(", ", cpu);

            MedGpu.Detalle = equipo.Gpu ?? "GPU no detectada";
        }

        // Llega cada 2 segundos desde MonitorRecursos
        private void MostrarMuestra(MuestraRecursos m)
        {
            var es = InfoSistema.Es;

            MedCpu.Valor = m.CpuPorcentaje != null ? $"{m.CpuPorcentaje.Value.ToString("F0", es)} %" : "—";
            MedCpu.Porcentaje = m.CpuPorcentaje ?? 0;

            if (m.RamUsadaGb != null && m.RamLibreGb != null && m.RamPorcentaje != null)
            {
                // Total = la RAM instalada (8 GB) si la conocemos; si no, la que Windows puede usar
                string total = equipo?.RamGb != null
                    ? equipo.RamGb.Value.ToString(es)
                    : (m.RamUsadaGb.Value + m.RamLibreGb.Value).ToString("F1", es);
                MedRam.Valor = $"{m.RamUsadaGb.Value.ToString("F1", es)} / {total} GB";
                MedRam.Porcentaje = m.RamPorcentaje.Value;
                MedRam.Detalle = $"{m.RamPorcentaje.Value.ToString("F0", es)} % en uso, {m.RamLibreGb.Value.ToString("F1", es)} GB libres";
            }
            else
            {
                MedRam.Valor = "—";
                MedRam.Porcentaje = 0;
            }

            MedGpu.Valor = m.GpuPorcentaje != null ? $"{m.GpuPorcentaje.Value.ToString("F0", es)} %" : "—";
            MedGpu.Porcentaje = m.GpuPorcentaje ?? 0;

            // Temperatura de la GPU: la barra va de 0 a 100 °C y cambia de color según lo caliente que esté
            if (m.GpuTemperatura is double grados)
            {
                NivelTemperatura nivel = SensorTemperaturaGpu.Clasificar(grados);
                MedTemp.Valor = $"{grados.ToString("F0", es)} °C";
                MedTemp.Porcentaje = Math.Clamp(grados, 0, 100);
                MedTemp.Detalle = Formato.TextoNivel(nivel);
                MedTemp.ColorBarra = (Brush?)TryFindResource(Formato.ColorNivel(nivel));
            }
            else
            {
                MedTemp.Valor = "N/D";
                MedTemp.Porcentaje = 0;
                MedTemp.Detalle = "sin sensor: solo GPU NVIDIA (NVML)";
                MedTemp.ColorBarra = null;
            }

            MostrarDiscoC();
        }

        // Línea de abajo de la franja: espacio libre del disco C:. Es una llamada barata a Windows
        // (GetDiskFreeSpaceEx), así que se puede leer con cada muestra
        private void MostrarDiscoC()
        {
            try
            {
                var disco = new DriveInfo("C");
                if (!disco.IsReady)
                {
                    TxtDiscoC.Text = "disco c:  no disponible";
                    return;
                }
                long libre = disco.AvailableFreeSpace;
                long total = disco.TotalSize;
                double porcentaje = total > 0 ? 100.0 * libre / total : 0;
                TxtDiscoC.Text = $"disco c:  {Formato.Tamano(libre)} libres de {Formato.Tamano(total)} " +
                                 $"({porcentaje.ToString("F0", InfoSistema.Es)} % libre)";
            }
            catch (Exception ex)
            {
                // Sin disco C: o sin permiso: se dice, no se inventa un número
                TxtDiscoC.Text = "disco c:  no se pudo leer";
                if (!avisoDiscoDado)
                {
                    Logger.Aviso($"No se pudo leer el espacio libre de C: ({ex.Message})");
                    avisoDiscoDado = true;
                }
            }
        }

        // ===== Puntuación =====

        // El último benchmark real (de benchmarks.json). Si no hay ninguno, el mensaje de "Ejecuta un benchmark"
        private void MostrarPuntuacion()
        {
            ResultadoBenchmark? ultimo = HistorialBenchmarks.Ultimo;
            bool hay = ultimo != null;

            PanelSinBenchmark.Visibility = hay ? Visibility.Collapsed : Visibility.Visible;
            PanelConBenchmark.Visibility = hay ? Visibility.Visible : Visibility.Collapsed;
            // Tag = el dato de arriba a la derecha del panel; null = no se dibuja
            PanelPuntuacion.Tag = hay ? "último benchmark" : null;
            TxtBenchmarkDetalle.Visibility = hay ? Visibility.Visible : Visibility.Collapsed;
            if (ultimo == null)
            {
                return;
            }

            var vm = new BenchmarkVM(ultimo);
            TxtMejora.Text = vm.TextoMejora;
            // Si empeoró, en naranja: se muestra tal cual, sin maquillar
            TxtMejora.Foreground = (Brush)TryFindResource(vm.Mejoro ? "ExitoBrush" : "AvisoBrush");   // verde = resultado OK

            double maximo = Math.Max(ultimo.Antes.FpsPromedio, ultimo.Despues.FpsPromedio);
            BarraAntes.Value = 100 * ultimo.Antes.FpsPromedio / maximo;
            BarraDespues.Value = 100 * ultimo.Despues.FpsPromedio / maximo;
            TxtAntes.Text = $"{BenchmarkVM.Fps(ultimo.Antes.FpsPromedio)} FPS";
            TxtDespues.Text = $"{BenchmarkVM.Fps(ultimo.Despues.FpsPromedio)} FPS";
            TxtBenchmarkDetalle.Text = $"{vm.Juego}, {vm.Fecha}, 1% low {vm.Texto1Low}";
        }

        // ===== Ghost Mode =====

        // Ghost Mode está "ON" si TODAS las seguras están aplicadas en Windows
        private async Task LeerEstadoGhostAsync()
        {
            ghostActivo = await Task.Run(() => Seguras.All(EstaActivaSinFallar));
            MostrarEstadoGhost();
        }

        private static bool EstaActivaSinFallar(IOptimizacion optimizacion)
        {
            try
            {
                return optimizacion.EstaActiva();
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo leer el estado de {optimizacion.Id}", ex);
                return false;
            }
        }

        private void MostrarEstadoGhost()
        {
            BtnGhost.IsEnabled = !trabajando && Seguras.Count > 0;

            if (trabajando)
            {
                TxtBotonGhost.Text = "[ APLICANDO... ]";
                TxtBotonGhostDetalle.Text = "no cierres la app";
                return;
            }

            // TryFindResource = buscar un color de Colores.xaml por su nombre.
            // Morado = Ghost Mode encendido (el mismo color que el botón ON); apagado, gris
            PuntoEstado.Fill = (Brush)TryFindResource(ghostActivo ? "AcentoBrush" : "TextoSecundarioBrush");
            Banner.Foreground = (Brush)TryFindResource(ghostActivo ? "AcentoBrush" : "BannerApagadoBrush");
            TxtEstado.Text = ghostActivo ? "> ghost_mode: on" : "> ghost_mode: off";
            TxtEstado.Foreground = (Brush)TryFindResource(ghostActivo ? "AcentoBrilloBrush" : "TextoBrush");

            // Tag="on" hace que el estilo del botón lo rellene de morado (InicioPagina.xaml)
            BtnGhost.Tag = ghostActivo ? "on" : "off";
            TxtBotonGhost.Text = ghostActivo ? "[ GHOST_MODE: ON ]" : "[ ACTIVAR GHOST MODE ]";
            TxtBotonGhostDetalle.Text = ghostActivo ? "clic para desactivar" : "un clic, pide confirmación";
        }

        // Se aplicó o deshizo algo desde otra parte (Optimizaciones, "Deshacer todo")
        private async void AlCambiarEstado()
        {
            if (!trabajando)
            {
                await LeerEstadoGhostAsync();
            }
        }

        private async void BtnGhost_Click(object sender, RoutedEventArgs e)
        {
            if (ghostActivo)
            {
                await DesactivarAsync();
            }
            else
            {
                await ActivarAsync();
            }
        }

        private async Task ActivarAsync()
        {
            // Solo las que faltan: las que ya están activas no cuentan como cambio
            List<IOptimizacion> pendientes = await Task.Run(() => Seguras.Where(o => !EstaActivaSinFallar(o)).ToList());
            if (pendientes.Count == 0)
            {
                await LeerEstadoGhostAsync();
                return;
            }

            string lista = string.Join("\n", pendientes.Select(o => $"• {o.Nombre}"));
            var confirmar = new MessageBoxUi
            {
                Title = "¿Activar Ghost Mode?",
                Content = $"Se aplicarán {pendientes.Count} optimizaciones seguras:\n{lista}\n\n" +
                          "Antes se creará un punto de restauración. Podrás deshacerlas en cualquier momento.",
                PrimaryButtonText = "Activar Ghost Mode",
                CloseButtonText = "Cancelar"
            };
            if (await confirmar.ShowDialogAsync() != MessageBoxResultUi.Primary)
            {
                return;
            }

            ResultadoAplicacion resultado;
            trabajando = true;
            MostrarEstadoGhost();
            try
            {
                resultado = await AplicadorOptimizaciones.AplicarAsync(pendientes, Avisos.PreguntarContinuarSinPuntoAsync);
            }
            finally
            {
                trabajando = false;
                await LeerEstadoGhostAsync();
            }

            if (resultado.Cancelado)
            {
                return;
            }
            if (resultado.Fallidas.Count > 0)
            {
                string nombres = string.Join(", ", resultado.Fallidas.Select(o => o.Nombre));
                Avisos.Error($"No se pudieron aplicar {resultado.Fallidas.Count} de {pendientes.Count} optimizaciones",
                             $"{nombres}. Revisa el log para más detalles.");
            }
            else
            {
                Avisos.Exito($"Ghost Mode activado — {pendientes.Count} optimizaciones aplicadas",
                             "Puedes revertir los cambios desde «Deshacer todo».");
            }
        }

        private async Task DesactivarAsync()
        {
            var confirmar = new MessageBoxUi
            {
                Title = "¿Desactivar Ghost Mode?",
                Content = "Se desharán los cambios seguros que aplicó Ghost Mode. " +
                          "Lo que ya venía así en Windows antes de usar la app no se toca.",
                PrimaryButtonText = "Desactivar",
                CloseButtonText = "Cancelar"
            };
            if (await confirmar.ShowDialogAsync() != MessageBoxResultUi.Primary)
            {
                return;
            }

            ResultadoAplicacion resultado;
            trabajando = true;
            MostrarEstadoGhost();
            try
            {
                resultado = await AplicadorOptimizaciones.RevertirAsync(Seguras);
            }
            finally
            {
                trabajando = false;
                await LeerEstadoGhostAsync();
            }

            if (resultado.Fallidas.Count > 0)
            {
                string nombres = string.Join(", ", resultado.Fallidas.Select(o => o.Nombre));
                Avisos.Error("No se pudo desactivar todo", $"{nombres}. Revisa el log para más detalles.");
            }
            else if (ghostActivo)
            {
                // Todo sigue activo porque ya venía así en Windows: lo decimos en vez de fingir
                Avisos.Info("Ghost Mode sigue activo",
                            "Estas optimizaciones ya estaban activadas en Windows antes de usar Ghost Mode, así que no se tocan.");
            }
            else
            {
                Avisos.Exito("Ghost Mode desactivado", "Se restauró la configuración que tenías antes.");
            }
        }
    }
}
