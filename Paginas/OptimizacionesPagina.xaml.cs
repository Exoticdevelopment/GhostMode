using System.Windows;
using System.Windows.Controls;
using OptimizadorPC.Componentes;
using OptimizadorPC.Core;
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Paginas
{
    public partial class OptimizacionesPagina : Page
    {
        // Un VM por cada optimización de la app. Select = "convertir cada una en un VM"
        private readonly List<OptimizacionVM> todas =
            AplicadorOptimizaciones.Todas.Select(o => new OptimizacionVM(o)).ToList();

        private bool soloSeguros = false;
        private bool aplicando = false;   // mientras es true, ignoramos nuestro propio aviso EstadoCambiado

        public OptimizacionesPagina()
        {
            InitializeComponent();

            // Cada vez que cambia algo de una tarjeta (interruptor, estado), refrescar el botón y el contador
            foreach (var vm in todas)
            {
                vm.PropertyChanged += (s, e) => ActualizarResumen();
            }

            Loaded += async (s, e) =>
            {
                AplicadorOptimizaciones.EstadoCambiado += AlCambiarEstado;
                MostrarCategorias();
                await LeerEstadoRealAsync();
                await MostrarSubtituloAsync();
            };
            // Al salir de la página nos desuscribimos para no recibir avisos de una página que ya no se ve
            Unloaded += (s, e) => AplicadorOptimizaciones.EstadoCambiado -= AlCambiarEstado;
        }

        // Las optimizaciones que se ven con el filtro actual
        private List<OptimizacionVM> Visibles() =>
            todas.Where(vm => !soloSeguros || vm.Riesgo == Riesgo.Seguro).ToList();

        // Agrupa las visibles por categoría. Las categorías sin optimizaciones no aparecen
        private void MostrarCategorias()
        {
            ListaCategorias.ItemsSource = Visibles()
                .GroupBy(vm => vm.Categoria)
                .OrderBy(g => g.Key)   // en el orden del enum: Energía, Gráficos, Sistema, Entrada
                .Select(g => new GrupoCategoria(GrupoCategoria.NombreDe(g.Key), GrupoCategoria.IconoDe(g.Key), g.ToList()))
                .ToList();
            ActualizarResumen();
        }

        // Pone cada interruptor igual a lo que Windows tiene de verdad.
        // Task.WhenAll = consultar todas a la vez y esperar a que terminen
        private async Task LeerEstadoRealAsync()
        {
            await Task.WhenAll(todas.Select(vm => vm.LeerEstadoRealAsync()));
            ActualizarResumen();
        }

        // "Ajustes pensados para 8 GB de RAM y una CPU de 4 núcleos." con los datos reales del equipo
        private async Task MostrarSubtituloAsync()
        {
            DatosEquipo equipo = await InfoSistema.ObtenerAsync();

            if (equipo.RamGb != null && equipo.Nucleos != null)
            {
                TxtSubtitulo.Text = $"Ajustes pensados para {equipo.RamGb} GB de RAM y una CPU de {equipo.Nucleos} núcleos.";
            }
            else
            {
                TxtSubtitulo.Text = "Ajustes pensados para equipos con pocos recursos.";
            }
        }

        // Botón "Aplicar N cambios", contador, aviso naranja y línea del punto de restauración
        private void ActualizarResumen()
        {
            List<OptimizacionVM> visibles = Visibles();
            int pendientes = todas.Count(vm => vm.TieneCambioPendiente);

            BtnAplicar.Content = pendientes switch
            {
                0 => "Sin cambios",
                1 => "Aplicar 1 cambio",
                _ => $"Aplicar {pendientes} cambios"
            };
            BtnAplicar.IsEnabled = pendientes > 0 && !aplicando;

            TxtSeleccionados.Text = $"{visibles.Count(vm => vm.Deseada)} de {visibles.Count} seleccionados";

            AvisoModerado.Visibility = visibles.Any(vm => vm.EsModerado) ? Visibility.Visible : Visibility.Collapsed;
            FilaPuntoRestauracion.Visibility =
                AplicadorOptimizaciones.PuntoRestauracionListo ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Filtro_Checked(object sender, RoutedEventArgs e)
        {
            // El primer Checked llega durante InitializeComponent, cuando la página aún no está armada
            if (!IsLoaded)
            {
                return;
            }

            soloSeguros = FiltroSeguros.IsChecked == true;

            // Lo que se oculta no debe aplicarse "a escondidas": devolvemos su interruptor a como está en Windows
            if (soloSeguros)
            {
                foreach (var vm in todas.Where(vm => vm.EsModerado && vm.TieneCambioPendiente))
                {
                    vm.Deseada = vm.Aplicada;
                }
            }
            MostrarCategorias();
        }

        // Alguien aplicó o deshizo cambios desde otro sitio (p. ej. "Deshacer todo" de la barra lateral)
        private async void AlCambiarEstado()
        {
            if (aplicando)
            {
                return;
            }
            await LeerEstadoRealAsync();
        }

        private async void BtnAplicar_Click(object sender, RoutedEventArgs e)
        {
            // ToList = "congelar" las listas ahora, antes de que los interruptores cambien
            var aActivar = todas.Where(vm => vm.TieneCambioPendiente && vm.Deseada).ToList();
            var aQuitar = todas.Where(vm => vm.TieneCambioPendiente && !vm.Deseada).ToList();
            var afectadas = aActivar.Concat(aQuitar).ToList();
            if (afectadas.Count == 0)
            {
                return;
            }

            aplicando = true;
            foreach (var vm in afectadas)
            {
                vm.Estado = EstadoInterruptor.Aplicando;   // ruedita naranja
            }
            ActualizarResumen();

            bool cancelado = false;
            var fallidas = new List<IOptimizacion>();
            try
            {
                if (aActivar.Count > 0)
                {
                    ResultadoAplicacion resultado = await AplicadorOptimizaciones.AplicarAsync(
                        aActivar.Select(vm => vm.Optimizacion), Avisos.PreguntarContinuarSinPuntoAsync);
                    cancelado = resultado.Cancelado;
                    fallidas.AddRange(resultado.Fallidas);
                }
                if (!cancelado && aQuitar.Count > 0)
                {
                    ResultadoAplicacion resultado = await AplicadorOptimizaciones.RevertirAsync(
                        aQuitar.Select(vm => vm.Optimizacion));
                    fallidas.AddRange(resultado.Fallidas);
                }

                if (cancelado)
                {
                    // El usuario canceló: no se tocó nada y sus interruptores se quedan como los dejó
                    foreach (var vm in afectadas)
                    {
                        vm.Estado = EstadoInterruptor.Normal;
                    }
                    return;
                }

                // Volver a preguntar a Windows: lo que se muestra es siempre lo real, no lo que esperábamos.
                // (Mientras Estado sea Aplicando, cambiar Deseada no borra nada)
                await Task.WhenAll(afectadas.Select(vm => vm.LeerEstadoRealAsync()));
                foreach (var vm in afectadas)
                {
                    vm.Estado = fallidas.Contains(vm.Optimizacion) ? EstadoInterruptor.Error : EstadoInterruptor.Normal;
                }
            }
            finally
            {
                aplicando = false;
                ActualizarResumen();
            }

            InformarResultado(afectadas.Count, fallidas, aQuitar);
        }

        private static void InformarResultado(int total, List<IOptimizacion> fallidas, List<OptimizacionVM> aQuitar)
        {
            if (fallidas.Count > 0)
            {
                string nombres = string.Join(", ", fallidas.Select(o => o.Nombre));
                Avisos.Error($"No se pudieron aplicar {fallidas.Count} de {total} cambios",
                             $"{nombres}. Revisa el log para más detalles.");
                return;
            }

            // Ghost Mode solo revierte lo que él mismo cambió. Si el ajuste ya venía así en Windows,
            // sigue activado: lo decimos claramente en vez de fingir que se apagó.
            var yaVenian = aQuitar.Where(vm => vm.Aplicada).ToList();
            if (yaVenian.Count > 0)
            {
                string nombres = string.Join(", ", yaVenian.Select(vm => vm.Nombre));
                Avisos.Info("Algunos ajustes siguen activos",
                            $"{nombres}: ya estaban así en Windows antes de usar Ghost Mode, así que no se tocan.");
                return;
            }

            Avisos.Exito(total == 1 ? "Cambio aplicado" : $"{total} cambios aplicados",
                         "Puedes revertirlos cuando quieras desde «Deshacer todo».");
        }
    }
}
