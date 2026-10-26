using System.Windows;
using System.Windows.Media;
using OptimizadorPC.Core;
using OptimizadorPC.Servicios;
using Wpf.Ui.Controls;   // FluentWindow

namespace OptimizadorPC.Paginas
{
    public partial class BienvenidaVentana : FluentWindow
    {
        private int paso = 1;
        private bool diagnosticoLeido = false;

        public BienvenidaVentana()
        {
            InitializeComponent();
            MostrarPaso();
        }

        private void BtnAtras_Click(object sender, RoutedEventArgs e)
        {
            paso = Math.Max(1, paso - 1);
            MostrarPaso();
        }

        private async void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            if (paso == 3)
            {
                Close();
                return;
            }
            paso++;
            MostrarPaso();
            if (paso == 3 && !diagnosticoLeido)
            {
                await MostrarDiagnosticoAsync();
            }
        }

        private void MostrarPaso()
        {
            Paso1.Visibility = paso == 1 ? Visibility.Visible : Visibility.Collapsed;
            Paso2.Visibility = paso == 2 ? Visibility.Visible : Visibility.Collapsed;
            Paso3.Visibility = paso == 3 ? Visibility.Visible : Visibility.Collapsed;
            TxtPaso.Text = $"{paso} / 3";
            BtnAtras.Visibility = paso > 1 ? Visibility.Visible : Visibility.Hidden;
            BtnSiguiente.Content = paso == 3 ? "[ empezar ]" : "[ siguiente ]";
        }

        private async Task MostrarDiagnosticoAsync()
        {
            try
            {
                Diagnostico d = await DiagnosticoEquipo.LeerAsync();
                var es = InfoSistema.Es;

                TxtRam.Text = d.RamGb != null ? $"{d.RamGb} GB" : "no disponible";
                TxtGpu.Text = d.Gpu == null ? "no disponible"
                    : d.VramGb is double vram ? $"{d.Gpu} · {vram.ToString("0.#", es)} GB" : d.Gpu;
                TxtDisco.Text = d.DiscoSistema switch
                {
                    TipoDisco.Ssd => "SSD (donde está Windows)",
                    TipoDisco.Hdd => "HDD, disco duro (donde está Windows)",
                    _ => "no se pudo saber"
                };
                TxtEspacio.Text = d.LibreGb is double libre && d.TotalGb is double total
                    ? $"{libre.ToString("F0", es)} GB de {total.ToString("F0", es)} GB"
                    : "no disponible";

                ListaConsejos.ItemsSource = DiagnosticoEquipo.Recomendaciones(d).Select(c => new
                {
                    c.Texto,
                    Color = (Brush)FindResource(c.Nivel switch
                    {
                        NivelConsejo.Bien => "ExitoBrush",
                        NivelConsejo.Aviso => "AvisoBrush",
                        _ => "TextoSecundarioBrush"
                    })
                }).ToList();

                TxtLeyendo.Text = "Lo que ve Ghost Mode en tu equipo y qué esperar al jugar:";
                TablaEquipo.Visibility = Visibility.Visible;
                diagnosticoLeido = true;
                Logger.Info($"Diagnóstico de bienvenida: RAM {d.RamGb} GB · {d.Gpu} · disco {d.DiscoSistema} · {d.LibreGb:F0} GB libres");
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo hacer el diagnóstico de la bienvenida", ex);
                TxtLeyendo.Text = "No se pudo leer el equipo. Puedes seguir igualmente.";
            }
        }
    }
}
