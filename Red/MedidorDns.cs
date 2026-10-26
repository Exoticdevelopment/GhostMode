using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OptimizadorPC.Red
{
    // Mide cuánto tarda un servidor DNS en contestar, con consultas DNS de verdad (no un ping):
    // se le pregunta la dirección de sitios conocidos y se cronometra la respuesta.
    // Se usan sitios muy visitados porque es lo que pasa al navegar o al entrar a un servidor:
    // así se mide la red hasta el DNS y su rapidez, no lo que tarda en investigar un dominio raro.
    public static class MedidorDns
    {
        private static readonly string[] Dominios = ["google.com", "cloudflare.com", "microsoft.com", "youtube.com", "discord.com"];

        // Mediana de varias consultas, en ms; null si no contestó ninguna
        public static async Task<double?> MedirAsync(IPAddress servidor, int consultas = 5, int esperaMs = 1500)
        {
            var tiempos = new List<double>();
            for (int i = 0; i < consultas; i++)
            {
                if (await ConsultarAsync(servidor, Dominios[i % Dominios.Length], esperaMs) is double ms)
                {
                    tiempos.Add(ms);
                }
            }
            return Mediana(tiempos);
        }

        // Una consulta: null si no responde a tiempo o la respuesta no es válida
        public static async Task<double?> ConsultarAsync(IPAddress servidor, string dominio, int esperaMs = 1500)
        {
            ushort id = (ushort)Random.Shared.Next(1, 65535);
            byte[] consulta = CrearConsulta(id, dominio);

            using var udp = new UdpClient(servidor.AddressFamily);
            using var limite = new CancellationTokenSource(esperaMs);
            try
            {
                udp.Connect(servidor, 53);
                var reloj = Stopwatch.StartNew();
                await udp.SendAsync(consulta, limite.Token);
                // Puede llegar basura u otra respuesta: se espera la que tiene nuestro número
                while (true)
                {
                    UdpReceiveResult r = await udp.ReceiveAsync(limite.Token);
                    if (EsRespuestaValida(r.Buffer, id))
                    {
                        return reloj.Elapsed.TotalMilliseconds;
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException)
            {
                return null;
            }
        }

        // El mensaje DNS (RFC 1035): cabecera de 12 bytes + la pregunta "¿dirección IPv4 (A) de dominio?"
        public static byte[] CrearConsulta(ushort id, string dominio)
        {
            var bytes = new List<byte>
            {
                (byte)(id >> 8), (byte)id,   // número de consulta
                0x01, 0x00,                  // "consulta normal, resuélvela tú" (recursión)
                0x00, 0x01,                  // 1 pregunta
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };
            // "google.com" se escribe 6 g o o g l e 3 c o m 0
            foreach (string parte in dominio.Split('.'))
            {
                bytes.Add((byte)parte.Length);
                bytes.AddRange(Encoding.ASCII.GetBytes(parte));
            }
            bytes.Add(0);
            bytes.AddRange([0x00, 0x01, 0x00, 0x01]);   // tipo A, clase IN
            return bytes.ToArray();
        }

        // Es la respuesta a NUESTRA consulta (mismo número), es una respuesta (no otra pregunta)
        // y el servidor no dio error (0 = bien, 3 = "ese dominio no existe": también es contestar)
        public static bool EsRespuestaValida(byte[] r, ushort id)
        {
            if (r.Length < 12 || r[0] != (byte)(id >> 8) || r[1] != (byte)id || (r[2] & 0x80) == 0)
            {
                return false;
            }
            int codigo = r[3] & 0x0F;
            return codigo is 0 or 3;
        }

        public static double? Mediana(IReadOnlyCollection<double> valores)
        {
            if (valores.Count == 0)
            {
                return null;
            }
            double[] orden = valores.Order().ToArray();
            int medio = orden.Length / 2;
            return orden.Length % 2 == 1 ? orden[medio] : (orden[medio - 1] + orden[medio]) / 2;
        }

        // El más rápido de los que contestaron (id del servidor), o null si ninguno
        public static string? Recomendar(IReadOnlyDictionary<string, double?> latencias) =>
            latencias.Where(kv => kv.Value != null).OrderBy(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();

        // Menos de esto no se nota: no vale la pena cambiar de DNS
        public const double DiferenciaMinimaMs = 5;
    }
}
