using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OptimizadorPC.Core;
using OptimizadorPC.ModelosVista;
using OptimizadorPC.Red;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Paginas
{
    public partial class RedPagina : Page
    {
        private readonly PingEnVivo ping = new();
        private InfoAdaptador? adaptador;
        private List<ServidorDnsVM> servidores = [];
        private bool trabajando = false;   // midiendo latencias
        private bool aplicando = false;    // cambiando el DNS desde esta página

        public RedPagina()
        {
            InitializeComponent();
            ping.Muestra += MostrarPing;

            Loaded += async (s, e) =>
            {
                AplicadorOptimizaciones.EstadoCambiado += AlCambiarEstado;
                ping.Iniciar(ping.Destino);
                MostrarDestino();
                await LeerAdaptadorAsync();
            };
            // Fuera de la pantalla no se hace ping: cero tráfico
            Unloaded += (s, e) =>
            {
                ping.Detener();
                AplicadorOptimizaciones.EstadoCambiado -= AlCambiarEstado;
            };
        }

        // ===== Adaptador y DNS =====

        private async Task LeerAdaptadorAsync()
        {
            adaptador = await Task.Run(AdaptadorRed.Activo);
            MostrarAdaptador();
            await MostrarServidoresAsync();
        }

        private void MostrarAdaptador()
        {
            bool conectado = adaptador != null;
            TxtConexion.Text = conectado ? $"{adaptador!.TextoTipo} · conectado" : "sin conexión";
            TxtConexion.SetResourceReference(TextBlock.ForegroundProperty, conectado ? "ActivoBrush" : "AvisoBrush");
            PastillaConexion.SetResourceReference(Border.BorderBrushProperty, conectado ? "ActivoBrush" : "AvisoBrush");

            TxtAdaptador.Text = conectado ? $"{adaptador!.Nombre} · {adaptador.Descripcion}" : "—";
            TxtTipo.Text = adaptador?.TextoTipo ?? "—";
            TxtIp.Text = adaptador?.IpLocal ?? "—";
            TxtVelocidad.Text = adaptador?.TextoVelocidad ?? "—";
            TxtDnsActual.Text = adaptador is { Dns.Count: > 0 } ? string.Join(" · ", adaptador.Dns) : "—";
            ConsejoWifi.Visibility = adaptador?.EsWifi == true ? Visibility.Visible : Visibility.Collapsed;

            BtnAplicar.IsEnabled = conectado;
            BtnMedir.IsEnabled = conectado;
        }

        private async Task MostrarServidoresAsync()
        {
            ServidorDns? activo = null;
            string[] router = [];
            if (adaptador != null)
            {
                InfoAdaptador a = adaptador;
                (activo, router) = await Task.Run(() => (GestorDns.Activo(a), GestorDns.DnsDelRouter(a)));
            }

            servidores = ServidorDns.Todos.Select(s => new ServidorDnsVM(
                s, s.EsAutomatico && router.Length > 0 ? $"router: {string.Join(" / ", router)}" : s.TextoDirecciones)
            {
                EsActivo = s == activo,
                Seleccionado = s == activo
            }).ToList();
            ListaDns.ItemsSource = servidores;
        }

        private async void BtnMedir_Click(object sender, RoutedEventArgs e)
        {
            if (adaptador == null || trabajando)
            {
                return;
            }
            trabajando = true;
            BtnMedir.IsEnabled = false;
            BtnMedir.Content = "[ midiendo... ]";
            try
            {
                string[] router = await Task.Run(() => GestorDns.DnsDelRouter(adaptador));
                var latencias = new Dictionary<string, double?>();

                // Uno detrás de otro (no a la vez) para que no se estorben en la misma conexión
                foreach (ServidorDnsVM vm in servidores)
                {
                    string? ip = vm.Servidor.EsAutomatico ? router.FirstOrDefault() : vm.Servidor.Ipv4[0];
                    vm.Latencia = "midiendo...";
                    double? ms = ip != null && IPAddress.TryParse(ip, out IPAddress? direccion)
                        ? await MedidorDns.MedirAsync(direccion)
                        : null;
                    latencias[vm.Servidor.Id] = ms;
                    vm.Latencia = ms != null ? $"{ms.Value.ToString("F0", InfoSistema.Es)} ms" : "sin respuesta";
                }

                string? mejor = MedidorDns.Recomendar(latencias);
                foreach (ServidorDnsVM vm in servidores)
                {
                    vm.EsRecomendado = vm.Servidor.Id == mejor;
                }
                TxtRecomendacion.Text = TextoRecomendacion(latencias, mejor);
                Logger.Info("Latencia DNS: " + string.Join(", ", servidores.Select(s => $"{s.Nombre} {s.Latencia}")));
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo medir la latencia de los DNS", ex);
                TxtRecomendacion.Text = "No se pudo medir. Revisa el log.";
            }
            finally
            {
                trabajando = false;
                BtnMedir.IsEnabled = adaptador != null;
                BtnMedir.Content = "[ medir latencia ]";
            }
        }

        // Con datos: el más rápido y cuánto gana frente al que está puesto. Si gana poco, se dice
        private string TextoRecomendacion(Dictionary<string, double?> latencias, string? mejor)
        {
            if (mejor == null)
            {
                return "Ningún servidor respondió. Revisa la conexión.";
            }
            ServidorDnsVM ganador = servidores.First(s => s.Servidor.Id == mejor);
            ServidorDnsVM? actual = servidores.FirstOrDefault(s => s.EsActivo);
            double msGanador = latencias[mejor]!.Value;

            if (actual == null || latencias.GetValueOrDefault(actual.Servidor.Id) is not double msActual)
            {
                return $"> recomendado: {ganador.Nombre} ({msGanador.ToString("F0", InfoSistema.Es)} ms, el más rápido desde tu conexión).";
            }
            if (actual == ganador)
            {
                return $"> ya usas el más rápido: {ganador.Nombre}. No hace falta cambiar nada.";
            }
            double diferencia = msActual - msGanador;
            return diferencia < MedidorDns.DiferenciaMinimaMs
                ? $"> {ganador.Nombre} es el más rápido, pero solo {diferencia.ToString("F0", InfoSistema.Es)} ms menos que {actual.Nombre}: no se notará. Puedes dejarlo como está."
                : $"> recomendado: {ganador.Nombre}, {diferencia.ToString("F0", InfoSistema.Es)} ms más rápido que {actual.Nombre} al resolver nombres.";
        }

        private async void BtnAplicar_Click(object sender, RoutedEventArgs e)
        {
            if (adaptador == null || servidores.FirstOrDefault(s => s.Seleccionado) is not ServidorDnsVM elegido)
            {
                return;
            }
            if (elegido.EsActivo)
            {
                Avisos.Info($"{elegido.Nombre} ya está puesto", "No hay nada que cambiar.");
                return;
            }

            InfoAdaptador a = adaptador;
            aplicando = true;
            BtnAplicar.IsEnabled = false;
            try
            {
                bool hecho = await AplicadorOptimizaciones.CambiarAsync(
                    () => GestorDns.Aplicar(a, elegido.Servidor), Avisos.PreguntarContinuarSinPuntoAsync);
                if (hecho)
                {
                    Avisos.Exito($"DNS cambiado a {elegido.Nombre}", "Puedes volver al anterior desde «Deshacer todo».");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo cambiar el DNS a {elegido.Nombre}", ex);
                Avisos.Error("No se pudo cambiar el DNS", "Revisa el log para más detalles.");
            }
            aplicando = false;
            await LeerAdaptadorAsync();
        }

        private async void BtnLimpiarCache_Click(object sender, RoutedEventArgs e)
        {
            BtnLimpiarCache.IsEnabled = false;
            try
            {
                await Task.Run(GestorDns.LimpiarCache);
                Avisos.Exito("Caché DNS vaciada", "Windows volverá a preguntar las direcciones al DNS.");
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo vaciar la caché DNS", ex);
                Avisos.Error("No se pudo vaciar la caché DNS", "Revisa el log para más detalles.");
            }
            BtnLimpiarCache.IsEnabled = true;
        }

        // «Deshacer todo» pudo devolver el DNS: volver a leer
        private async void AlCambiarEstado()
        {
            if (!aplicando)   // si lo cambiamos desde aquí, ya se vuelve a leer al terminar
            {
                await LeerAdaptadorAsync();
            }
        }

        // ===== Ping =====

        private void MostrarPing(double? ms)
        {
            var es = InfoSistema.Es;
            GraficaPing.Agregar(ms);

            EstadisticasPing e = EstadisticasPing.Calcular(ping.Muestras);
            TxtPromedio.Text = e.Promedio != null ? $"{e.Promedio.Value.ToString("F0", es)} ms" : "— ms";

            string pintura = e.PerdidaPorcentaje == 0 ? "ActivoBrush" : e.PerdidaPorcentaje < 5 ? "AvisoBrush" : "ErrorBrush";
            TxtPerdida.Text = e.Promedio == null ? "sin respuesta"
                : e.PerdidaPorcentaje == 0 ? "sin pérdida"
                : $"pérdida {e.PerdidaPorcentaje.ToString("F0", es)} %";
            TxtPerdida.SetResourceReference(TextBlock.ForegroundProperty, pintura);
            PastillaPerdida.SetResourceReference(Border.BorderBrushProperty, pintura);

            TxtDetallePing.Text = e.Promedio == null
                ? "El servidor no responde al ping (algunos lo bloquean)."
                : $"máx. {e.Maximo!.Value.ToString("F0", es)} ms · mín. {e.Minimo!.Value.ToString("F0", es)} ms\n" +
                  $"jitter {(e.Jitter ?? 0).ToString("F0", es)} ms · {e.Enviados} muestras";
            // Techo de la gráfica: el máximo con un 10 % de aire (lo mismo que calcula GraficaLinea)
            TxtTecho.Text = e.Maximo != null ? $"{(e.Maximo.Value * 1.1).ToString("F0", es)} ms" : "";
        }

        private void MostrarDestino() =>
            TxtDestino.Text = $"destino: {ping.Destino} · cada 1 s · últimos {PingEnVivo.Capacidad} s";

        private void BtnPingServidor_Click(object sender, RoutedEventArgs e) => CambiarDestino(TxtServidor.Text);

        private void BtnPingFiveM_Click(object sender, RoutedEventArgs e) => CambiarDestino(TxtFiveM.Text, fivem: true);

        private void TxtServidor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CambiarDestino(TxtServidor.Text);
            }
        }

        private void TxtFiveM_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CambiarDestino(TxtFiveM.Text, fivem: true);
            }
        }

        private void CambiarDestino(string texto, bool fivem = false)
        {
            string? destino = PingEnVivo.NormalizarDestino(texto);
            TxtErrorDestino.Visibility = destino == null ? Visibility.Visible : Visibility.Collapsed;
            if (destino == null)
            {
                TxtErrorDestino.Text = fivem
                    ? "Escribe la IP del servidor, por ejemplo 123.45.67.89:30120. Los enlaces cfx.re/join no dicen la IP: búscala en la lista de servidores de FiveM."
                    : "Escribe una IP (1.1.1.1) o un nombre (google.com).";
                return;
            }

            if (destino != ping.Destino)
            {
                GraficaPing.Vaciar();
                Logger.Info($"Ping en vivo: destino {destino}{(fivem ? " (servidor de FiveM)" : "")}");
            }
            ping.Iniciar(destino);
            MostrarDestino();
        }
    }
}
