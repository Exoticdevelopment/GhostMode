using System.Windows;
using System.Windows.Controls;   // Button, TextBlock, Grid... para crear las filas por código
using System.Windows.Media;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.Sistema;

namespace OptimizadorPC
{
    public partial class MainWindow : Window
    {
        // TODAS las optimizaciones de la app. Para agregar una nueva, basta una línea aquí:
        // la ventana solo conoce el contrato IOptimizacion, no cada clase concreta.
        private readonly List<IOptimizacion> optimizaciones = new()
        {
            new PlanEnergia(),
            new GameMode(),
            new EfectosVisuales(),
        };

        // Lo que necesitamos recordar de cada fila para actualizarla después
        private class Fila
        {
            public required IOptimizacion Optimizacion;   // required = obligatorio al crearla
            public required TextBlock TxtEstado;
            public required Button BtnAplicar;
            public required Button BtnRevertir;
        }

        private readonly List<Fila> filas = new();

        // true mientras se aplica o revierte algo: bloquea todos los botones
        private bool ocupado = false;

        // El punto de restauración se pide UNA vez por sesión, antes de lo primero que se aplique
        private bool puntoRestauracionListo = false;

        public MainWindow()
        {
            InitializeComponent();

            foreach (var optimizacion in optimizaciones)
            {
                PanelLista.Children.Add(CrearFila(optimizacion));
            }
            ActualizarEstados();
        }

        // ---------- Botones ----------

        private async void BtnAplicarTodo_Click(object sender, RoutedEventArgs e)
        {
            await AplicarAsync(optimizaciones);
        }

        private void BtnDeshacerTodo_Click(object sender, RoutedEventArgs e)
        {
            Revertir(optimizaciones);
        }

        // ---------- Aplicar y revertir ----------

        private async Task AplicarAsync(IEnumerable<IOptimizacion> lista)
        {
            ocupado = true;
            ActualizarEstados();

            // try/finally: pase lo que pase, al final se desbloquean los botones
            try
            {
                if (!await AsegurarPuntoRestauracionAsync())
                {
                    TxtMensaje.Text = "Cancelado: no se aplicó nada.";
                    return;
                }

                int errores = 0;
                foreach (var optimizacion in lista)
                {
                    if (optimizacion.EstaActiva())
                    {
                        continue;   // ya estaba aplicada: no hay nada que hacer
                    }

                    // Cada una en su propio try: si una falla, las demás siguen
                    try
                    {
                        optimizacion.Aplicar();
                    }
                    catch (Exception ex)
                    {
                        errores++;
                        Logger.Error($"Falló al aplicar {optimizacion.Id}", ex);
                    }
                }

                TxtMensaje.Text = errores == 0
                    ? "Listo. Algunos efectos visuales se notan al cerrar sesión."
                    : $"Terminado con {errores} error(es). Revisa el log.";
            }
            finally
            {
                ocupado = false;
                ActualizarEstados();
            }
        }

        private void Revertir(IEnumerable<IOptimizacion> lista)
        {
            int errores = 0;
            foreach (var optimizacion in lista)
            {
                // Solo revertimos lo que la app cambió (lo que tiene "foto" guardada)
                if (!GestorEstado.TieneCambios(optimizacion.Id))
                {
                    continue;
                }

                try
                {
                    optimizacion.Revertir();
                }
                catch (Exception ex)
                {
                    errores++;
                    Logger.Error($"Falló al revertir {optimizacion.Id}", ex);
                }
            }

            TxtMensaje.Text = errores == 0
                ? "Cambios deshechos."
                : $"Terminado con {errores} error(es). Revisa el log.";
            ActualizarEstados();
        }

        // Devuelve true si se puede seguir aplicando, false si el usuario canceló
        private async Task<bool> AsegurarPuntoRestauracionAsync()
        {
            if (puntoRestauracionListo)
            {
                return true;
            }

            TxtMensaje.Text = "Creando punto de restauración (puede tardar un minuto)...";
            ResultadoPunto resultado = await PuntoRestauracion.CrearAsync("OptimizadorPC - antes de optimizar");

            if (resultado == ResultadoPunto.Fallo)
            {
                MessageBoxResult respuesta = MessageBox.Show(
                    "No se pudo crear un punto de restauración.\n" +
                    "Normalmente es porque la Protección del sistema está desactivada.\n\n" +
                    "¿Aplicar de todas formas? Siempre podrás deshacer los cambios con \"Deshacer todo\".",
                    "Punto de restauración",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (respuesta != MessageBoxResult.Yes)
                {
                    Logger.Info("El usuario canceló al no poder crear el punto de restauración");
                    return false;
                }
                Logger.Aviso("El usuario decidió continuar sin punto de restauración");
            }

            puntoRestauracionListo = true;
            return true;
        }

        // ---------- Construcción y refresco de la lista ----------

        // Crea una fila:  [Nombre / Descripción]   [Estado]   [Aplicar] [Revertir]
        private Border CrearFila(IOptimizacion optimizacion)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Columna 0: nombre y descripción, uno debajo del otro
            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(new TextBlock
            {
                Text = optimizacion.Nombre,
                Foreground = Brushes.White,
                FontSize = 15
            });
            textos.Children.Add(new TextBlock
            {
                Text = optimizacion.Descripcion,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 16, 0)
            });

            var txtEstado = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 90 };
            var btnAplicar = new Button { Content = "Aplicar", Width = 90, Height = 32, Margin = new Thickness(8, 0, 0, 0) };
            var btnRevertir = new Button { Content = "Revertir", Width = 90, Height = 32, Margin = new Thickness(8, 0, 0, 0) };

            // Cada botón actúa solo sobre SU optimización.
            // new[] { optimizacion } = una lista de un solo elemento, para reutilizar los mismos métodos
            btnAplicar.Click += async (s, e) => await AplicarAsync(new[] { optimizacion });
            btnRevertir.Click += (s, e) => Revertir(new[] { optimizacion });

            // Colocar cada elemento en su columna
            Grid.SetColumn(textos, 0);
            Grid.SetColumn(txtEstado, 1);
            Grid.SetColumn(btnAplicar, 2);
            Grid.SetColumn(btnRevertir, 3);
            grid.Children.Add(textos);
            grid.Children.Add(txtEstado);
            grid.Children.Add(btnAplicar);
            grid.Children.Add(btnRevertir);

            filas.Add(new Fila
            {
                Optimizacion = optimizacion,
                TxtEstado = txtEstado,
                BtnAplicar = btnAplicar,
                BtnRevertir = btnRevertir
            });

            // Border = la "tarjeta" con fondo y esquinas redondeadas alrededor de la fila
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x23)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 10),
                Child = grid
            };
        }

        // Vuelve a preguntar el estado de cada optimización y ajusta textos y botones
        private void ActualizarEstados()
        {
            bool algunaInactiva = false;

            foreach (var fila in filas)
            {
                bool activa = fila.Optimizacion.EstaActiva();
                if (!activa)
                {
                    algunaInactiva = true;
                }

                fila.TxtEstado.Text = activa ? "● Activa" : "○ Inactiva";
                fila.TxtEstado.Foreground = activa ? Brushes.LimeGreen : Brushes.Gray;

                // Aplicar: solo si no está activa. Revertir: solo si la app guardó algo que deshacer
                fila.BtnAplicar.IsEnabled = !ocupado && !activa;
                fila.BtnRevertir.IsEnabled = !ocupado && GestorEstado.TieneCambios(fila.Optimizacion.Id);
            }

            BtnAplicarTodo.IsEnabled = !ocupado && algunaInactiva;
            BtnDeshacerTodo.IsEnabled = !ocupado && GestorEstado.HayCambiosPendientes;
        }
    }
}
