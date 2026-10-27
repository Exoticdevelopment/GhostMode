using System.Windows;
using System.Windows.Controls;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Paginas
{
    public partial class MonitorPagina : Page
    {
        // Cada 1 segundo y con disco, procesos y consumo propio
        private readonly MonitorRecursos monitor = new(TimeSpan.FromSeconds(1), detallado: true);
        private DatosEquipo? equipo;
        private bool pausado = false;

        public MonitorPagina()
        {
            InitializeComponent();
            monitor.Actualizado += MostrarMuestra;

            Loaded += async (s, e) =>
            {
                if (!pausado)
                {
                    monitor.Iniciar();
                }
                equipo = await InfoSistema.ObtenerAsync();
                MostrarDatosFijos();
            };
            // Fuera de la pantalla no medimos nada: cero consumo
            Unloaded += (s, e) => monitor.Detener();
        }

        // Lo que no cambia: procesador, GPU, RAM instalada
        private void MostrarDatosFijos()
        {
            if (equipo == null)
            {
                return;
            }

            // "Intel Core i5-13420H, 8 núcleos / 12 hilos". ProcessorCount = hilos que ve Windows
            var partes = new List<string>();
            if (equipo.Cpu != null)
            {
                partes.Add(equipo.Cpu);
            }
            partes.Add(equipo.Nucleos != null
                ? $"{equipo.Nucleos} núcleos / {Environment.ProcessorCount} hilos"
                : $"{Environment.ProcessorCount} hilos");
            TxtProcesador.Text = string.Join(", ", partes);

            TarjetaGpu.Detalle = equipo.Gpu ?? "GPU no detectada";
            if (equipo.RamGb != null)
            {
                TarjetaRam.EtiquetaMaximo = $"{equipo.RamGb} GB";
            }
        }

        // Llega cada segundo desde MonitorRecursos
        private void MostrarMuestra(MuestraRecursos m)
        {
            var es = InfoSistema.Es;

            // CPU
            TarjetaCpu.Valor = m.CpuPorcentaje != null ? $"{m.CpuPorcentaje.Value.ToString("F0", es)} %" : "—";
            TarjetaCpu.Detalle = m.Procesos != null ? $"{m.Procesos} procesos" : "";
            TarjetaCpu.Agregar(m.CpuPorcentaje);

            // RAM: la gráfica va en % (el techo es la RAM total)
            if (m.RamUsadaGb != null && m.RamLibreGb != null && m.RamPorcentaje != null)
            {
                string total = equipo?.RamGb != null
                    ? equipo.RamGb.Value.ToString(es)
                    : (m.RamUsadaGb.Value + m.RamLibreGb.Value).ToString("F1", es);
                TarjetaRam.Valor = $"{m.RamUsadaGb.Value.ToString("F1", es)} / {total} GB";
                TarjetaRam.Detalle = $"{m.RamPorcentaje.Value.ToString("F0", es)} % en uso, " +
                                     $"{m.RamLibreGb.Value.ToString("F1", es)} GB disponibles";
            }
            else
            {
                TarjetaRam.Valor = "—";
            }
            TarjetaRam.Agregar(m.RamPorcentaje);

            // GPU
            TarjetaGpu.Valor = m.GpuPorcentaje != null ? $"{m.GpuPorcentaje.Value.ToString("F0", es)} %" : "—";
            TarjetaGpu.Agregar(m.GpuPorcentaje);

            // Temperatura de la GPU (°C; la gráfica va de 0 a 100 °C)
            TarjetaTemp.Valor = m.GpuTemperatura != null ? $"{m.GpuTemperatura.Value.ToString("F0", es)} °C" : "N/D";
            TarjetaTemp.Detalle = m.GpuTemperatura != null
                ? Formato.TextoNivel(SensorTemperaturaGpu.Clasificar(m.GpuTemperatura.Value))
                : "sin sensor: solo GPU NVIDIA (NVML)";
            TarjetaTemp.Agregar(m.GpuTemperatura is double grados ? Math.Clamp(grados, 0, 100) : null);

            // Disco: tiempo activo + lectura/escritura
            TarjetaDisco.Valor = m.DiscoPorcentaje != null ? $"{m.DiscoPorcentaje.Value.ToString("F0", es)} %" : "—";
            TarjetaDisco.Detalle = m.DiscoLecturaMBs != null && m.DiscoEscrituraMBs != null
                ? $"lectura {m.DiscoLecturaMBs.Value.ToString("F1", es)} MB/s, escritura {m.DiscoEscrituraMBs.Value.ToString("F1", es)} MB/s"
                : "contadores de disco no disponibles";
            TarjetaDisco.Agregar(m.DiscoPorcentaje);

            // Pie: lo que gasta Ghost Mode. "<1 %" en vez de "0 %", que parecería que no mide
            if (m.AppRamMb != null)
            {
                string cpu = m.AppCpuPorcentaje == null ? "—"
                    : m.AppCpuPorcentaje < 1 ? "<1 %"
                    : $"{m.AppCpuPorcentaje.Value.ToString("F0", es)} %";
                TxtConsumo.Text = $"consumo de ghost_mode: {m.AppRamMb.Value.ToString("F0", es)} MB de RAM, {cpu} de CPU";
            }

            // Qué sensores no responden (la CPU la primera vez es null: aún no hay con qué comparar)
            var sinDatos = new List<string>();
            if (m.GpuPorcentaje == null)
            {
                sinDatos.Add("GPU");
            }
            if (m.DiscoPorcentaje == null)
            {
                sinDatos.Add("disco");
            }
            if (m.GpuTemperatura == null)
            {
                sinDatos.Add("temperatura");
            }
            TxtSensores.Text = sinDatos.Count == 0
                ? "todos los sensores responden"
                : $"sin datos de: {string.Join(", ", sinDatos)}";
        }

        private void BtnPausar_Click(object sender, RoutedEventArgs e)
        {
            pausado = !pausado;
            if (pausado)
            {
                monitor.Detener();
            }
            else
            {
                monitor.Iniciar();
            }

            BtnPausar.Content = pausado ? "[ reanudar ]" : "[ pausar ]";
            TxtVivo.Text = pausado ? "en pausa" : "en vivo, cada 1 s";
            // TryFindResource = buscar un color de Colores.xaml por su nombre. En pausa, todo en gris
            var color = (System.Windows.Media.Brush)TryFindResource(pausado ? "TextoSecundarioBrush" : "TextoBrush");
            PuntoVivo.Fill = color;
            TxtVivo.Foreground = color;
        }
    }
}
