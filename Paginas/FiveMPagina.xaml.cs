using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OptimizadorPC.Componentes;
using OptimizadorPC.Core;
using OptimizadorPC.Limpieza;   // ResultadoLimpieza
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Servicios;
// Alias: WPF y WPF-UI tienen los dos una clase "MessageBox". Así dejamos claro cuál usamos
using MessageBoxUi = Wpf.Ui.Controls.MessageBox;
using MessageBoxResultUi = Wpf.Ui.Controls.MessageBoxResult;

namespace OptimizadorPC.Paginas
{
    // Lo que se lee de FiveM de una vez, en segundo plano (disco y procesos pueden tardar)
    internal record DatosFiveM(string? Exe, bool Abierto, List<string> Ejecutables, long? BytesCache);

    public partial class FiveMPagina : Page
    {
        // El VM de la optimización de GPU: la misma tarjeta e interruptor que en Optimizaciones.
        // OfType<GpuDedicadaFiveM>() = "de la lista, la que sea de este tipo"
        private readonly OptimizacionVM gpu =
            new(AplicadorOptimizaciones.Todas.OfType<GpuDedicadaFiveM>().First());

        private DatosFiveM datos = new(null, false, [], null);
        private bool trabajando = false;   // aplicando o limpiando ahora mismo

        public FiveMPagina()
        {
            InitializeComponent();

            TarjetaGpu.DataContext = gpu;
            gpu.PropertyChanged += (s, e) => ActualizarBotones();

            Loaded += async (s, e) =>
            {
                AplicadorOptimizaciones.EstadoCambiado += AlCambiarEstado;
                await Task.WhenAll(RefrescarAsync(), MostrarDiagnosticoGraficosAsync(), MostrarDiscoAsync());
            };
            Unloaded += (s, e) => AplicadorOptimizaciones.EstadoCambiado -= AlCambiarEstado;
        }

        // ===== Leer y mostrar =====

        private async Task RefrescarAsync()
        {
            BtnBuscar.IsEnabled = false;
            datos = await Task.Run(LeerDatos);
            await gpu.LeerEstadoRealAsync();
            BtnBuscar.IsEnabled = true;
            Mostrar();
        }

        // Solo lectura: ¿FiveM o GTA V en un disco duro? (paso 7 del plan de docs/Investigacion-FiveM.pdf)
        private async Task MostrarDiscoAsync()
        {
            try
            {
                List<UbicacionEnDisco> ubicaciones = await Task.Run(DiscoJuego.Revisar);
                foreach (UbicacionEnDisco u in ubicaciones)
                {
                    Logger.Info($"Disco: {u.Que} en {u.Unidad}: ({u.Tipo})");
                }
                string? aviso = DiscoJuego.TextoAviso(ubicaciones);
                TxtAvisoDisco.Text = aviso ?? "";
                AvisoDisco.Visibility = aviso != null ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                // Sin aviso antes que un aviso falso
                Logger.Error("No se pudo revisar el disco de FiveM y GTA V", ex);
                AvisoDisco.Visibility = Visibility.Collapsed;
            }
        }

        // Solo lectura: equipo + settings.xml de GTA V (si existe) → recomendaciones
        private async Task MostrarDiagnosticoGraficosAsync()
        {
            try
            {
                Diagnostico equipo = await DiagnosticoEquipo.LeerAsync();
                AjustesGta? juego = await Task.Run(() => DiagnosticoGraficos.LeerSettings(DiagnosticoGraficos.RutaSettings));
                var es = InfoSistema.Es;
                int hilos = Environment.ProcessorCount;

                string grafica = equipo.Gpu == null ? "gráfica no detectada"
                    : equipo.VramGb is double v ? $"{equipo.Gpu}, {v.ToString("0.#", es)} GB VRAM" : equipo.Gpu;
                TxtEquipoGraficos.Text = $"{grafica}, RAM {equipo.RamGb?.ToString() ?? "?"} GB, CPU {hilos} hilos";
                TxtSettingsGta.Text = juego != null
                    ? "Ajustes leídos del settings.xml de GTA V. Ojo: FiveM podría no estar usando ese archivo en su versión actual."
                    : "No hay settings.xml de GTA V: recomendaciones solo según el equipo.";

                ListaConsejosGraficos.ItemsSource = DiagnosticoGraficos.Recomendaciones(equipo, hilos, juego).Select(c => new
                {
                    c.Texto,
                    Color = (Brush)FindResource(c.Nivel switch
                    {
                        NivelConsejo.Bien => "ExitoBrush",
                        NivelConsejo.Aviso => "AvisoBrush",
                        _ => "TextoSecundarioBrush"
                    })
                }).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo hacer el diagnóstico de gráficos de FiveM", ex);
                TxtEquipoGraficos.Text = "No se pudo leer el equipo. Revisa el log.";
            }
        }

        private static DatosFiveM LeerDatos()
        {
            string? exe = UbicacionFiveM.BuscarExe();
            if (exe == null)
            {
                return new DatosFiveM(null, false, [], null);
            }

            long? bytes;
            try
            {
                bytes = CacheFiveM.Medir(exe);
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo medir la caché de FiveM", ex);
                bytes = null;   // se mostrará "—": mejor que un número inventado
            }
            return new DatosFiveM(exe, UbicacionFiveM.EstaEnEjecucion(), UbicacionFiveM.EjecutablesJuego(exe), bytes);
        }

        private void Mostrar()
        {
            bool instalado = datos.Exe != null;

            TxtEstado.Text = instalado ? "> fivem detectado" : "> fivem no encontrado";
            TxtRuta.Text = instalado
                ? Path.GetDirectoryName(datos.Exe)
                : "Instálalo desde fivem.net y pulsa «[ volver a buscar ]».";

            PastillaAbierto.Visibility = instalado ? Visibility.Visible : Visibility.Collapsed;
            TxtAbierto.Text = datos.Abierto ? "abierto" : "cerrado";
            // TryFindResource = buscar un color de Colores.xaml por su nombre
            TxtAbierto.Foreground = (Brush)TryFindResource(datos.Abierto ? "AvisoBrush" : "TextoSecundarioBrush");
            PastillaAbierto.BorderBrush = (Brush)TryFindResource(datos.Abierto ? "AvisoBrush" : "BordeHoverBrush");
            AvisoAbierto.Visibility = datos.Abierto ? Visibility.Visible : Visibility.Collapsed;

            // Sin FiveM, las tarjetas se ven apagadas y no responden
            ZonaAjustes.IsEnabled = instalado;
            ZonaAjustes.Opacity = instalado ? 1 : 0.5;

            // "Se aplica a: FiveM.exe, FiveM_b3095_GTAProcess.exe"
            TxtEjecutables.Text = instalado
                ? "Se aplica a: " + string.Join(", ", datos.Ejecutables.Select(Path.GetFileName))
                : "";
            if (instalado && datos.Ejecutables.Count == 1)
            {
                // Solo FiveM.exe: el proceso del juego aparece la primera vez que se entra a un servidor
                TxtEjecutables.Text += ". Entra una vez a un servidor y vuelve a aplicar para incluir el juego.";
            }

            TxtTamano.Text = datos.BytesCache != null ? Formato.Tamano(datos.BytesCache.Value) : "—";
            ActualizarBotones();
        }

        private void ActualizarBotones()
        {
            bool instalado = datos.Exe != null;

            BtnAplicarGpu.Content = !gpu.TieneCambioPendiente ? "[ sin cambios ]" : gpu.Deseada ? "[ aplicar ]" : "[ quitar ]";
            BtnAplicarGpu.IsEnabled = instalado && gpu.TieneCambioPendiente && !trabajando;

            BtnLimpiar.IsEnabled = instalado && !datos.Abierto && !trabajando && datos.BytesCache > 0;
        }

        // ===== Botones =====

        // "[ copiar ]" de la guía "¿es el servidor?": el comando va en Tag
        private void Copiar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string comando })
            {
                return;
            }
            try
            {
                Clipboard.SetText(comando);
                Avisos.Info($"Copiado: {comando}", "Pégalo en la consola de FiveM (F8) con Ctrl+V.");
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                // Otro programa tiene el portapapeles abierto en este momento
                Logger.Aviso($"No se pudo copiar al portapapeles: {ex.Message}");
                Avisos.Error("No se pudo copiar", "Otro programa está usando el portapapeles. Inténtalo de nuevo.");
            }
        }

        // "Volver a buscar" también revisa el disco: FiveM o GTA V pueden haberse instalado o movido
        private async void BtnBuscar_Click(object sender, RoutedEventArgs e) => await Task.WhenAll(RefrescarAsync(), MostrarDiscoAsync());

        // "Deshacer todo" de la barra lateral también revierte la GPU de FiveM
        private async void AlCambiarEstado()
        {
            if (!trabajando)
            {
                await gpu.LeerEstadoRealAsync();
            }
        }

        private async void BtnAplicarGpu_Click(object sender, RoutedEventArgs e)
        {
            bool activar = gpu.Deseada;
            trabajando = true;
            gpu.Estado = EstadoInterruptor.Aplicando;   // ruedita naranja
            ActualizarBotones();

            ResultadoAplicacion resultado;
            try
            {
                resultado = activar
                    ? await AplicadorOptimizaciones.AplicarAsync([gpu.Optimizacion], Avisos.PreguntarContinuarSinPuntoAsync)
                    : await AplicadorOptimizaciones.RevertirAsync([gpu.Optimizacion]);

                if (resultado.Cancelado)
                {
                    gpu.Estado = EstadoInterruptor.Normal;   // no se tocó nada; el interruptor se queda como lo dejó
                    return;
                }

                // Lo que se muestra es siempre lo real: volvemos a preguntar a Windows
                await gpu.LeerEstadoRealAsync();
                gpu.Estado = resultado.Fallidas.Count > 0 ? EstadoInterruptor.Error : EstadoInterruptor.Normal;
            }
            finally
            {
                trabajando = false;
                ActualizarBotones();
            }

            if (resultado.Fallidas.Count > 0)
            {
                Avisos.Error("No se pudo cambiar la GPU de FiveM", "Revisa el log para más detalles.");
            }
            else if (!activar && gpu.Aplicada)
            {
                Avisos.Info("La GPU dedicada sigue activa",
                            "Ya estaba configurada así en Windows antes de usar Ghost Mode, así que no se toca.");
            }
            else if (activar)
            {
                Avisos.Exito("GPU dedicada activada para FiveM", "Se notará la próxima vez que abras FiveM.");
            }
            else
            {
                Avisos.Exito("GPU de FiveM restaurada", "Se dejó como estaba antes de usar Ghost Mode.");
            }
        }

        private async void BtnLimpiar_Click(object sender, RoutedEventArgs e)
        {
            if (datos.Exe == null || datos.BytesCache == null)
            {
                return;
            }

            var confirmar = new MessageBoxUi
            {
                Title = "¿Limpiar la caché de FiveM?",
                Content = $"Se borrarán {Formato.Tamano(datos.BytesCache.Value)} de archivos descargados de los servidores. " +
                          "No se puede deshacer, pero FiveM vuelve a descargar lo que necesite (la primera entrada a " +
                          "cada servidor tardará un poco más).\n\nLos archivos del juego no se tocan.",
                PrimaryButtonText = "Limpiar",
                CloseButtonText = "Cancelar"
            };
            if (await confirmar.ShowDialogAsync() != MessageBoxResultUi.Primary)
            {
                return;
            }

            string exe = datos.Exe;
            trabajando = true;
            BtnLimpiar.Content = "[ limpiando… ]";
            ActualizarBotones();
            try
            {
                ResultadoLimpieza resultado = await Task.Run(() => CacheFiveM.Limpiar(exe));

                string detalle = resultado.ArchivosEnUso > 0
                    ? $"{resultado.ArchivosEnUso} archivos estaban en uso y se dejaron."
                    : "FiveM volverá a descargar lo que necesite al entrar a un servidor.";
                Avisos.Exito($"Caché limpiada: {Formato.Tamano(resultado.BytesLiberados)} liberados", detalle);
            }
            catch (Exception ex)
            {
                Logger.Error("Falló la limpieza de la caché de FiveM", ex);
                Avisos.Error("No se pudo limpiar la caché", ex.Message);
            }
            finally
            {
                trabajando = false;
                BtnLimpiar.Content = "[ limpiar caché ]";
                await RefrescarAsync();   // volver a medir: el tamaño que se ve es el real
            }
        }
    }
}
