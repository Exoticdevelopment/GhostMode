using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using OptimizadorPC.Core;
using OptimizadorPC.Pruebas.Infra;
using OptimizadorPC.Red;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Pruebas
{
    // Red contra el Windows real: el DNS se cambia de verdad en el adaptador activo y se comprueba que
    // la configuración del registro (IPv4 e IPv6, con sus subclaves) vuelve EXACTAMENTE a como estaba
    [Collection("Sistema")]
    public class RedPruebas
    {
        // Una dirección reservada para documentación (RFC 5737): nunca responde
        private static readonly IPAddress NadieContesta = IPAddress.Parse("192.0.2.1");

        // ---------- Adaptador ----------

        [Fact]
        public void Adaptador_DevuelveElQueUsaInternet()
        {
            InfoAdaptador? a = AdaptadorRed.Activo();

            Assert.NotNull(a);
            Assert.StartsWith("{", a!.Id);
            Assert.True(a.Indice > 0);
            Assert.True(IPAddress.TryParse(a.IpLocal, out _), $"IP local: {a.IpLocal}");
            Assert.True(a.VelocidadBps > 0);
            Assert.NotEmpty(a.Dns);
            Assert.True(GestorDns.TieneConfiguracion(a.Id, ipv6: false));
            // Coincide con lo que dice .NET de esa tarjeta
            NetworkInterface tarjeta = NetworkInterface.GetAllNetworkInterfaces().Single(n => n.Id == a.Id);
            Assert.Equal(OperationalStatus.Up, tarjeta.OperationalStatus);
            Assert.Equal(tarjeta.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, a.EsWifi);
        }

        [Theory]
        [InlineData(650_000_000, "650 Mbps")]
        [InlineData(1_000_000_000, "1 Gbps")]
        [InlineData(2_500_000_000, "2500 Mbps")]
        [InlineData(0, "—")]
        public void Adaptador_TextoDeVelocidad(long bps, string esperado)
        {
            var a = new InfoAdaptador("{x}", 1, "x", "x", false, null, bps, []);
            Assert.Equal(esperado, a.TextoVelocidad);
        }

        // ---------- Medir DNS ----------

        [Fact]
        public void Dns_LaConsultaSigueElFormatoDelRfc()
        {
            byte[] consulta = MedidorDns.CrearConsulta(0x1234, "google.com");
            byte[] esperado =
            [
                0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0,
                6, (byte)'g', (byte)'o', (byte)'o', (byte)'g', (byte)'l', (byte)'e', 3, (byte)'c', (byte)'o', (byte)'m', 0,
                0x00, 0x01, 0x00, 0x01
            ];
            Assert.Equal(esperado, consulta);
        }

        [Theory]
        [InlineData(new byte[] { 0x12, 0x34, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 }, true)]    // respuesta correcta
        [InlineData(new byte[] { 0x12, 0x34, 0x81, 0x83, 0, 1, 0, 0, 0, 0, 0, 0 }, true)]    // "no existe": también contestó
        [InlineData(new byte[] { 0x12, 0x34, 0x81, 0x82, 0, 1, 0, 0, 0, 0, 0, 0 }, false)]   // error del servidor
        [InlineData(new byte[] { 0x99, 0x99, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 }, false)]   // otro número
        [InlineData(new byte[] { 0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 }, false)]   // es una pregunta
        [InlineData(new byte[] { 0x12, 0x34, 0x81 }, false)]                                  // cortada
        public void Dns_ReconoceLasRespuestasValidas(byte[] respuesta, bool esperado)
        {
            Assert.Equal(esperado, MedidorDns.EsRespuestaValida(respuesta, 0x1234));
        }

        [Fact]
        public void Dns_MedianaYRecomendacionConDatosConocidos()
        {
            Assert.Equal(13, MedidorDns.Mediana([30, 12, 11, 13, 90]));   // 11 12 [13] 30 90
            Assert.Equal(15, MedidorDns.Mediana([10, 20]));
            Assert.Null(MedidorDns.Mediana([]));

            var latencias = new Dictionary<string, double?> { ["automatico"] = 24, ["cloudflare"] = 12, ["google"] = 18, ["quad9"] = null };
            Assert.Equal("cloudflare", MedidorDns.Recomendar(latencias));
            Assert.Null(MedidorDns.Recomendar(new Dictionary<string, double?> { ["a"] = null }));
        }

        [Fact]
        public async Task Dns_MideServidoresRealesYNoSeCuelgaSiNoContestan()
        {
            double? cloudflare = await MedidorDns.MedirAsync(IPAddress.Parse("1.1.1.1"));
            Assert.NotNull(cloudflare);
            Assert.InRange(cloudflare!.Value, 0.1, 1500);

            var reloj = System.Diagnostics.Stopwatch.StartNew();
            Assert.Null(await MedidorDns.MedirAsync(NadieContesta, consultas: 2, esperaMs: 300));
            Assert.True(reloj.ElapsedMilliseconds < 3000);
        }

        // ---------- Cambiar el DNS ----------

        [Fact]
        public void Dns_AplicarYRevertirDejaElRegistroExacto()
        {
            InfoAdaptador a = AdaptadorRed.Activo()!;
            Dictionary<string, string> antes = FotoRed(a.Id);
            ServidorDns? activoAntes = GestorDns.Activo(a);
            try
            {
                ServidorDns cloudflare = ServidorDns.Todos.Single(s => s.Id == "cloudflare");
                ServidorDns google = ServidorDns.Todos.Single(s => s.Id == "google");
                Assert.NotEqual(cloudflare, activoAntes);   // si ya estaba, la prueba no demostraría nada

                GestorDns.Aplicar(a, cloudflare);

                Assert.Equal("1.1.1.1,1.0.0.1", GestorDns.LeerNameServer(a.Id, ipv6: false));
                if (GestorDns.TieneConfiguracion(a.Id, ipv6: true))
                {
                    Assert.Equal("2606:4700:4700::1111 2606:4700:4700::1001",
                                 GestorDns.LeerNameServer(a.Id, ipv6: true)!.Replace(",", " "));
                }
                Assert.Equal(cloudflare, GestorDns.Activo(a));
                Assert.True(GestorEstado.TieneCambios(GestorDns.Modulo));
                // Windows usa de verdad el nuevo DNS
                Assert.Contains("1.1.1.1", AdaptadorRed.Activo()!.Dns);

                // Cambiar otra vez no pisa el original
                GestorDns.Aplicar(a, google);
                Assert.Equal(google, GestorDns.Activo(a));

                GestorDns.Revertir();

                Assert.Equal(antes, FotoRed(a.Id));
                Assert.Equal(activoAntes, GestorDns.Activo(a));
                Assert.False(GestorEstado.TieneCambios(GestorDns.Modulo));
            }
            finally
            {
                Rescatar(a, antes);
            }
        }

        [Fact]
        public void Dns_ElegirAutomaticoSiYaEraAutomaticoNoDejaNadaPendiente()
        {
            InfoAdaptador a = AdaptadorRed.Activo()!;
            Dictionary<string, string> antes = FotoRed(a.Id);
            Assert.Equal(ServidorDns.Automatico, GestorDns.Activo(a));   // este PC usa el DNS del router
            try
            {
                GestorDns.Aplicar(a, ServidorDns.Automatico);

                Assert.Equal(antes, FotoRed(a.Id));
                Assert.False(GestorEstado.TieneCambios(GestorDns.Modulo));
            }
            finally
            {
                Rescatar(a, antes);
            }
        }

        [Fact]
        public async Task Dns_DeshacerTodoTambienDevuelveElDns()
        {
            InfoAdaptador a = AdaptadorRed.Activo()!;
            Dictionary<string, string> antes = FotoRed(a.Id);
            try
            {
                GestorDns.Aplicar(a, ServidorDns.Todos.Single(s => s.Id == "quad9"));
                Assert.NotEqual(antes, FotoRed(a.Id));

                ResultadoAplicacion r = await AplicadorOptimizaciones.DeshacerTodoAsync();

                Assert.True(r.TodoBien, string.Join(", ", r.NombresFallidos));
                Assert.Equal(antes, FotoRed(a.Id));
                Assert.False(GestorEstado.TieneCambios(GestorDns.Modulo));
            }
            finally
            {
                Rescatar(a, antes);
            }
        }

        [Fact]
        public void Dns_LimpiarLaCacheFunciona()
        {
            GestorDns.LimpiarCache();   // lanza excepción si ipconfig /flushdns falla
        }

        // ---------- Ping ----------

        [Fact]
        public void Ping_EstadisticasConDatosConocidos()
        {
            EstadisticasPing e = EstadisticasPing.Calcular([10, 20, null, 30]);
            Assert.Equal(20, e.Promedio);
            Assert.Equal(10, e.Minimo);
            Assert.Equal(30, e.Maximo);
            Assert.Equal(25, e.PerdidaPorcentaje);
            Assert.Equal(10, e.Jitter);   // |20-10| y |30-20|: el hueco no cuenta
            Assert.Equal(4, e.Enviados);

            EstadisticasPing nada = EstadisticasPing.Calcular([null, null]);
            Assert.Null(nada.Promedio);
            Assert.Equal(100, nada.PerdidaPorcentaje);
            Assert.Equal(0, EstadisticasPing.Calcular([]).PerdidaPorcentaje);
        }

        [Fact]
        public async Task Ping_RealResponde1111YNoAUnaIpMuda()
        {
            using var ping = new Ping();
            double? ms = await PingEnVivo.PingAsync(ping, "1.1.1.1");
            Assert.NotNull(ms);
            Assert.InRange(ms!.Value, 0.5, 1000);

            Assert.Null(await PingEnVivo.PingAsync(ping, NadieContesta.ToString()));
            Assert.Null(await PingEnVivo.PingAsync(ping, "no-existe.ghostmode-prueba.invalid"));
        }

        [Theory]
        [InlineData("1.1.1.1", "1.1.1.1")]
        [InlineData("  123.45.67.89:30120 ", "123.45.67.89")]
        [InlineData("fivem://connect/123.45.67.89:30120", "123.45.67.89")]
        [InlineData("connect 123.45.67.89:30120", "123.45.67.89")]
        [InlineData("[2001:db8::1]:30120", "2001:db8::1")]
        [InlineData("2001:db8::1", "2001:db8::1")]
        [InlineData("Google.com", "google.com")]
        [InlineData("play.servidor.gg:30120", "play.servidor.gg")]
        [InlineData("cfx.re/join/abc123", null)]
        [InlineData("1.2", null)]
        [InlineData("123.45.67.89:99999", null)]
        [InlineData("hola", null)]
        [InlineData("", null)]
        public void Ping_EntiendeLoQueEscribeElUsuario(string texto, string? esperado)
        {
            Assert.Equal(esperado, PingEnVivo.NormalizarDestino(texto));
        }

        // ---------- Ayudas ----------

        // Todos los valores de las claves Tcpip y Tcpip6 del adaptador (y sus subclaves), como texto.
        // Se dejan fuera los datos del alquiler DHCP, que Windows renueva solo cuando quiere
        private static Dictionary<string, string> FotoRed(string id)
        {
            var foto = new Dictionary<string, string>();
            foreach (string raiz in new[] { @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\",
                                            @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\" })
            {
                using RegistryKey? clave = Registry.LocalMachine.OpenSubKey(raiz + id);
                if (clave != null)
                {
                    Fotografiar(clave, raiz + id, foto);
                }
            }
            return foto;
        }

        private static readonly string[] AlquilerDhcp = ["Lease", "LeaseObtainedTime", "LeaseTerminatesTime", "T1", "T2",
                                                         "Dhcpv6InformationObtainedTime", "Dhcpv6InformationRefreshTime"];

        private static void Fotografiar(RegistryKey clave, string ruta, Dictionary<string, string> foto)
        {
            foreach (string nombre in clave.GetValueNames().Where(n => !AlquilerDhcp.Contains(n)))
            {
                object? valor = clave.GetValue(nombre, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                string texto = valor switch
                {
                    byte[] b => Convert.ToHexString(b),
                    string[] l => string.Join("|", l),
                    _ => valor?.ToString() ?? ""
                };
                foto[$@"{ruta}\{nombre}"] = $"{clave.GetValueKind(nombre)}:{texto}";
            }
            foreach (string sub in clave.GetSubKeyNames())
            {
                using RegistryKey? hija = clave.OpenSubKey(sub);
                if (hija != null)
                {
                    Fotografiar(hija, $@"{ruta}\{sub}", foto);
                }
            }
        }

        // Red de seguridad si una prueba falla a mitad: volver a automático y quitar lo que no existía
        private static void Rescatar(InfoAdaptador a, Dictionary<string, string> antes)
        {
            if (GestorEstado.TieneCambios(GestorDns.Modulo))
            {
                GestorDns.Revertir();
            }
            Assert.Equal(antes, FotoRed(a.Id));
        }
    }
}
