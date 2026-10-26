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
                await RefrescarAsync();
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

            TxtEstado.Text = instalado ? "FiveM detectado" : "FiveM no encontrado";
            TxtRuta.Text = instalado
                ? Path.GetDirectoryName(datos.Exe)
                : "Instálalo desde fivem.net y pulsa «Volver a buscar».";

            PastillaAbierto.Visibility = instalado ? Visibility.Visible : Visibility.Collapsed;
            TxtAbierto.Text = datos.Abierto ? "Abierto" : "Cerrado";
            // TryFindResource = buscar un color de Colores.xaml por su nombre
            TxtAbierto.Foreground = (Brush)TryFindResource(datos.Abierto ? "AvisoBrush" : "TextoSecundarioBrush");
            PastillaAbierto.Background = (Brush)TryFindResource(datos.Abierto ? "AvisoSuaveBrush" : "BordeBrush");
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

            TxtTamano.Text = datos.BytesCache != null ? FormatoTamano(datos.BytesCache.Value) : "—";
            ActualizarBotones();
        }

        private void ActualizarBotones()
        {
            bool instalado = datos.Exe != null;

            BtnAplicarGpu.Content = !gpu.TieneCambioPendiente ? "Sin cambios" : gpu.Deseada ? "Aplicar" : "Quitar";
            BtnAplicarGpu.IsEnabled = instalado && gpu.TieneCambioPendiente && !trabajando;

            BtnLimpiar.IsEnabled = instalado && !datos.Abierto && !trabajando && datos.BytesCache > 0;
        }

        // "1,4 GB" o "350 MB", con coma decimal como en español
        private static string FormatoTamano(long bytes)
        {
            double mb = bytes / 1048576.0;   // 1 MB = 1024 × 1024 bytes
            return mb >= 1024
                ? $"{(mb / 1024).ToString("F1", InfoSistema.Es)} GB"
                : $"{mb.ToString("F0", InfoSistema.Es)} MB";
        }

        // ===== Botones =====

        private async void BtnBuscar_Click(object sender, RoutedEventArgs e) => await RefrescarAsync();

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
                Content = $"Se borrarán {FormatoTamano(datos.BytesCache.Value)} de archivos descargados de los servidores. " +
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
            BtnLimpiar.Content = "Limpiando…";
            ActualizarBotones();
            try
            {
                ResultadoLimpieza resultado = await Task.Run(() => CacheFiveM.Limpiar(exe));

                string detalle = resultado.ArchivosEnUso > 0
                    ? $"{resultado.ArchivosEnUso} archivos estaban en uso y se dejaron."
                    : "FiveM volverá a descargar lo que necesite al entrar a un servidor.";
                Avisos.Exito($"Caché limpiada: {FormatoTamano(resultado.BytesLiberados)} liberados", detalle);
            }
            catch (Exception ex)
            {
                Logger.Error("Falló la limpieza de la caché de FiveM", ex);
                Avisos.Error("No se pudo limpiar la caché", ex.Message);
            }
            finally
            {
                trabajando = false;
                BtnLimpiar.Content = "Limpiar caché";
                await RefrescarAsync();   // volver a medir: el tamaño que se ve es el real
            }
        }
    }
}
