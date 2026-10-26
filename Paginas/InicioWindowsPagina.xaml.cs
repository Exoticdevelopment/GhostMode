using System.Windows;
using System.Windows.Controls;
using OptimizadorPC.Arranque;
using OptimizadorPC.Componentes;
using OptimizadorPC.Core;
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Paginas
{
    public partial class InicioWindowsPagina : Page
    {
        private List<ElementoArranqueVM> programas = [];
        private bool leyendo = false;

        public InicioWindowsPagina()
        {
            InitializeComponent();

            Loaded += async (s, e) =>
            {
                AplicadorOptimizaciones.EstadoCambiado += AlCambiarEstado;
                await Task.WhenAll(LeerAsync(), MostrarTiempoArranqueAsync());
            };
            Unloaded += (s, e) => AplicadorOptimizaciones.EstadoCambiado -= AlCambiarEstado;
        }

        // ===== Leer y mostrar =====

        private async Task LeerAsync()
        {
            if (leyendo)
            {
                return;
            }
            leyendo = true;
            BtnActualizar.IsEnabled = false;
            try
            {
                // Registro, carpetas, editores y WMI: todo en segundo plano
                ListaArranque lista = await Task.Run(GestorArranque.Listar);

                // Desuscribir las filas viejas antes de cambiarlas por las nuevas
                foreach (var vm in programas)
                {
                    vm.CambioPedido -= AlPedirCambio;
                }
                programas = lista.Elementos.Select(e => new ElementoArranqueVM(e)).ToList();
                foreach (var vm in programas)
                {
                    vm.CambioPedido += AlPedirCambio;
                }

                ListaProgramas.ItemsSource = programas;
                TxtVacio.Text = "No hay programas de inicio que se puedan cambiar.";
                TxtVacio.Visibility = programas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                TxtOcultos.Text = lista.Ocultos == 1
                    ? "1 elemento de Windows o del antivirus se oculta por seguridad."
                    : $"{lista.Ocultos} elementos de Windows o del antivirus se ocultan por seguridad.";
                TxtOcultos.Visibility = lista.Ocultos > 0 ? Visibility.Visible : Visibility.Collapsed;

                ActualizarResumen();
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo leer la lista de programas de inicio", ex);
                TxtVacio.Text = "No se pudo leer la lista de programas de inicio. Revisa el log.";
                TxtVacio.Visibility = Visibility.Visible;
            }
            finally
            {
                leyendo = false;
                BtnActualizar.IsEnabled = true;
            }
        }

        private async Task MostrarTiempoArranqueAsync()
        {
            TimeSpan? tiempo = await Task.Run(TiempoArranque.Leer);
            // F0 = sin decimales: "18 s". Si Windows no lo anotó: "No disponible" (nada inventado)
            TxtTiempo.Text = tiempo != null
                ? $"{tiempo.Value.TotalSeconds.ToString("F0", InfoSistema.Es)} s"
                : "No disponible";
        }

        // "2 de 6" y "4 apps": cuentan lo que está en Windows, no lo que el interruptor pide
        private void ActualizarResumen()
        {
            int habilitados = programas.Count(vm => vm.Elemento.Habilitado);
            int deshabilitados = programas.Count - habilitados;
            TxtHabilitados.Text = $"{habilitados} de {programas.Count}";
            TxtDeshabilitados.Text = deshabilitados == 1 ? "1 app" : $"{deshabilitados} apps";
        }

        // ===== Cambiar =====

        private async void BtnActualizar_Click(object sender, RoutedEventArgs e) => await LeerAsync();

        // "Deshacer todo" de la barra lateral también restaura estos programas: volver a leer
        private async void AlCambiarEstado()
        {
            // Si el cambio lo hicimos nosotros (una fila está "Aplicando"), ya actualizamos esa fila
            if (!programas.Any(vm => vm.Estado == EstadoInterruptor.Aplicando))
            {
                await LeerAsync();
            }
        }

        // El usuario movió un interruptor: el cambio se hace al momento
        private async void AlPedirCambio(ElementoArranqueVM vm, bool habilitar)
        {
            vm.Estado = EstadoInterruptor.Aplicando;   // ruedita naranja; el interruptor no se mueve mientras tanto
            try
            {
                ElementoArranque elemento = vm.Elemento;
                bool hecho = await AplicadorOptimizaciones.CambiarAsync(
                    () => GestorArranque.CambiarEstado(elemento, habilitar), Avisos.PreguntarContinuarSinPuntoAsync);

                if (!hecho)
                {
                    // El usuario canceló en el diálogo del punto de restauración: nada cambió
                    vm.Estado = EstadoInterruptor.Normal;
                    vm.MostrarReal();
                    return;
                }

                // with = copia del record cambiando solo Habilitado
                vm.Elemento = elemento with { Habilitado = habilitar };
                vm.Estado = EstadoInterruptor.Normal;
                Avisos.Exito(habilitar ? $"{vm.Nombre} se iniciará con Windows" : $"{vm.Nombre} ya no se iniciará con Windows",
                             "Se notará en el próximo inicio. Puedes revertirlo desde «Deshacer todo».");
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo cambiar {vm.Nombre} en el inicio de Windows", ex);
                vm.Estado = EstadoInterruptor.Error;
                vm.MostrarReal();   // el interruptor vuelve a lo que hay de verdad en Windows
                Avisos.Error($"No se pudo cambiar {vm.Nombre}", "Revisa el log para más detalles.");
            }
            ActualizarResumen();
        }
    }
}
