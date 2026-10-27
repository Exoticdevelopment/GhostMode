using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OptimizadorPC.Core;
using OptimizadorPC.Limpieza;
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Servicios;
// Alias: WPF y WPF-UI tienen los dos una clase "MessageBox". Así dejamos claro cuál usamos
using MessageBoxUi = Wpf.Ui.Controls.MessageBox;
using MessageBoxResultUi = Wpf.Ui.Controls.MessageBoxResult;

namespace OptimizadorPC.Paginas
{
    public partial class LimpiezaPagina : Page
    {
        // Una fila por cada categoría del catálogo
        private readonly List<CategoriaLimpiezaVM> categorias =
            CatalogoLimpieza.Todas.Select(c => new CategoriaLimpiezaVM(c)).ToList();

        private bool trabajando = false;   // analizando o limpiando ahora mismo
        private bool analizado = false;    // ¿ya se pulsó "Analizar" alguna vez?

        public LimpiezaPagina()
        {
            InitializeComponent();

            ListaCategorias.ItemsSource = categorias;
            // Al marcar o desmarcar una casilla, o al terminar de medir, recalcular el total
            foreach (var vm in categorias)
            {
                vm.PropertyChanged += (s, e) => ActualizarResumen();
            }

            Loaded += (s, e) =>
            {
                MostrarDisco();
                ActualizarResumen();
            };
        }

        // ===== Mostrar =====

        // "unidad c:  42,8 GB libres de 237 GB" con la barra de lo usado. Datos de Windows, al momento
        private void MostrarDisco()
        {
            try
            {
                // El disco donde está Windows ("C:\")
                var disco = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
                double libresGb = disco.AvailableFreeSpace / 1073741824.0;   // 1 GB = 1024³ bytes
                double totalGb = disco.TotalSize / 1073741824.0;

                TxtDisco.Text = $"unidad {disco.Name.TrimEnd('\\').ToLower()}  {libresGb.ToString("F1", InfoSistema.Es)} GB libres " +
                                $"de {totalGb.ToString("F0", InfoSistema.Es)} GB";
                BarraDisco.Value = (1 - libresGb / totalGb) * 100;
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo leer el espacio del disco", ex);
                TxtDisco.Text = "espacio del disco no disponible";
                BarraDisco.Value = 0;
            }
        }

        private void ActualizarResumen()
        {
            List<CategoriaLimpiezaVM> marcadas = categorias.Where(vm => vm.Seleccionada).ToList();
            long totalMarcado = marcadas.Sum(vm => vm.Bytes ?? 0);   // ?? 0 = si no se midió, cuenta como 0

            // Tag = el dato de arriba a la derecha del panel "categorías"
            PanelCategorias.Tag = $"{marcadas.Count} de {categorias.Count} marcadas";

            if (analizado)
            {
                TxtRecuperable.Text = Formato.Tamano(categorias.Sum(vm => vm.Bytes ?? 0));
                TxtTotal.Text = $"Total seleccionado: {Formato.Tamano(totalMarcado)}";
            }

            // La nota solo tiene sentido si la caché de shaders está desmarcada
            TxtNotaShaders.Visibility = categorias.Any(vm => vm.Categoria.Id == "shaders" && !vm.Seleccionada)
                ? Visibility.Visible
                : Visibility.Collapsed;

            BtnAnalizar.IsEnabled = !trabajando;
            BtnLimpiar.IsEnabled = !trabajando && analizado && totalMarcado > 0;
        }

        // Color del cuadradito y texto del estado del análisis; con hora, "a las 14:32" arriba a la derecha del panel
        private void MostrarEstado(string texto, string color, bool conHora)
        {
            TxtAnalisis.Text = texto;
            // TryFindResource = buscar un color de Colores.xaml por su nombre
            IconoAnalisis.Fill = (Brush)TryFindResource(color);
            PanelResumen.Tag = conHora ? $"a las {DateTime.Now:HH:mm}" : null;
        }

        // ===== Analizar =====

        private async void BtnAnalizar_Click(object sender, RoutedEventArgs e) => await AnalizarAsync();

        private async Task AnalizarAsync()
        {
            trabajando = true;
            MostrarEstado("analizando…", "AvisoBrush", false);
            foreach (var vm in categorias)
            {
                vm.Midiendo = true;
            }
            ActualizarResumen();

            // Medir todas a la vez, cada una en segundo plano. Task.WhenAll = esperar a que terminen todas
            await Task.WhenAll(categorias.Select(MedirAsync));

            trabajando = false;
            analizado = true;
            BtnAnalizar.Content = "[ analizar de nuevo ]";
            bool algunaFallo = categorias.Any(vm => vm.Bytes == null);
            MostrarEstado(algunaFallo ? "análisis completado con avisos" : "análisis completado",
                          algunaFallo ? "AvisoBrush" : "ExitoBrush", true);
            MostrarDisco();
            ActualizarResumen();
        }

        private static async Task MedirAsync(CategoriaLimpiezaVM vm)
        {
            try
            {
                vm.Bytes = await Task.Run(vm.Categoria.Medir);
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo medir {vm.Categoria.Id}", ex);
                vm.Bytes = null;   // se verá "—": mejor que un número inventado
            }
            finally
            {
                vm.Midiendo = false;
            }
        }

        // ===== Limpiar =====

        private async void BtnLimpiar_Click(object sender, RoutedEventArgs e)
        {
            // Solo las marcadas que tienen algo que borrar
            List<CategoriaLimpiezaVM> aLimpiar = categorias.Where(vm => vm.Seleccionada && vm.Bytes > 0).ToList();
            if (aLimpiar.Count == 0)
            {
                return;
            }

            long total = aLimpiar.Sum(vm => vm.Bytes!.Value);   // ! = "sé que aquí no es null"
            string lista = string.Join("\n", aLimpiar.Select(vm => $"• {vm.Nombre}: {Formato.Tamano(vm.Bytes!.Value)}"));
            var confirmar = new MessageBoxUi
            {
                Title = $"¿Liberar {Formato.Tamano(total)}?",
                Content = $"Se borrará:\n{lista}\n\nEsto no se puede deshacer. No se tocan documentos, descargas " +
                          "ni partidas guardadas.",
                PrimaryButtonText = "Limpiar",
                CloseButtonText = "Cancelar"
            };
            if (await confirmar.ShowDialogAsync() != MessageBoxResultUi.Primary)
            {
                return;
            }

            trabajando = true;
            BtnLimpiar.Content = "[ limpiando… ]";
            MostrarEstado("limpiando…", "AvisoBrush", false);
            ActualizarResumen();

            Logger.Info($"Limpieza: {string.Join(", ", aLimpiar.Select(vm => vm.Categoria.Id))}");
            var resultado = ResultadoLimpieza.Vacio;
            var fallidas = new List<string>();
            foreach (var vm in aLimpiar)
            {
                vm.Midiendo = true;   // "Midiendo…" en la fila mientras se limpia
                try
                {
                    resultado += await Task.Run(vm.Categoria.Limpiar);
                }
                catch (Exception ex)
                {
                    // Una categoría falla (p. ej. la papelera): se anota y seguimos con las demás
                    Logger.Error($"Falló la limpieza de {vm.Categoria.Id}", ex);
                    fallidas.Add(vm.Nombre);
                }
                vm.Midiendo = false;
            }

            BtnLimpiar.Content = "[ limpiar seleccionados ]";
            trabajando = false;
            InformarResultado(resultado, fallidas);

            // Volver a medir: lo que se muestra es siempre lo real, no lo que esperábamos
            await AnalizarAsync();
        }

        private static void InformarResultado(ResultadoLimpieza resultado, List<string> fallidas)
        {
            string enUso = resultado.ArchivosEnUso > 0
                ? $"{resultado.ArchivosEnUso} archivos estaban en uso y se dejaron."
                : "Todo lo seleccionado se borró.";

            if (fallidas.Count > 0)
            {
                Avisos.Error($"{Formato.Tamano(resultado.BytesLiberados)} liberados, pero algo falló",
                             $"No se pudo limpiar: {string.Join(", ", fallidas)}. Revisa el log para más detalles.");
            }
            else
            {
                Avisos.Exito($"{Formato.Tamano(resultado.BytesLiberados)} liberados", enUso);
            }
        }
    }
}
