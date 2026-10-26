using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;   // DispatcherTimer

namespace OptimizadorPC.Red
{
    // Resumen de las últimas muestras. null = no hay respuestas con qué calcular
    public record EstadisticasPing(double? Promedio, double? Minimo, double? Maximo, double? Jitter,
                                   double PerdidaPorcentaje, int Enviados)
    {
        // Jitter = cuánto varía el ping de una respuesta a la siguiente (lo que se nota como "tirones")
        public static EstadisticasPing Calcular(IReadOnlyList<double?> muestras)
        {
            List<double> recibidas = muestras.Where(m => m != null).Select(m => m!.Value).ToList();
            double perdida = muestras.Count == 0 ? 0 : 100.0 * (muestras.Count - recibidas.Count) / muestras.Count;
            double? jitter = recibidas.Count < 2
                ? null
                : recibidas.Zip(recibidas.Skip(1), (a, b) => Math.Abs(b - a)).Average();
            return new EstadisticasPing(
                recibidas.Count > 0 ? recibidas.Average() : null,
                recibidas.Count > 0 ? recibidas.Min() : null,
                recibidas.Count > 0 ? recibidas.Max() : null,
                jitter, perdida, muestras.Count);
        }
    }

    // Hace ping cada segundo al destino y guarda los últimos 60 resultados (null = sin respuesta).
    // Como el Monitor: solo mide mientras alguien lo enciende (la página Red a la vista)
    public sealed class PingEnVivo
    {
        public const int Capacidad = 60;
        public const int EsperaMs = 1000;

        public event Action<double?>? Muestra;

        private readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly List<double?> muestras = new();
        private readonly Ping ping = new();
        private bool midiendo = false;

        public string Destino { get; private set; } = "1.1.1.1";
        public IReadOnlyList<double?> Muestras => muestras;

        public PingEnVivo()
        {
            reloj.Tick += async (s, e) => await MedirAsync();
        }

        public void Iniciar(string destino)
        {
            if (destino != Destino)
            {
                muestras.Clear();   // otro servidor: empezar de cero
            }
            Destino = destino;
            reloj.Start();
            _ = MedirAsync();
        }

        public void Detener() => reloj.Stop();

        private async Task MedirAsync()
        {
            if (midiendo)
            {
                return;   // el anterior aún espera respuesta
            }
            midiendo = true;
            try
            {
                double? ms = await PingAsync(ping, Destino);
                muestras.Add(ms);
                if (muestras.Count > Capacidad)
                {
                    muestras.RemoveAt(0);
                }
                Muestra?.Invoke(ms);
            }
            finally
            {
                midiendo = false;
            }
        }

        // Un ping (ICMP, lo mismo que el comando ping de Windows). null = sin respuesta en 1 s
        public static async Task<double?> PingAsync(Ping ping, string destino)
        {
            try
            {
                PingReply r = await ping.SendPingAsync(destino, EsperaMs);
                // RoundtripTime viene en ms enteros; 0 = menos de 1 ms (red local)
                return r.Status == IPStatus.Success ? Math.Max(r.RoundtripTime, 0.5) : null;
            }
            catch (PingException)
            {
                return null;   // p. ej. el nombre no existe
            }
        }

        // Lo que escribe el usuario → la dirección a la que hacer ping. Acepta lo que muestra FiveM:
        // "123.45.67.89:30120", "fivem://connect/123.45.67.89:30120", "[2001:db8::1]:30120" o un nombre.
        // null = no es una dirección (p. ej. un enlace cfx.re/join, que no dice la IP)
        public static string? NormalizarDestino(string texto)
        {
            string t = texto.Trim();
            foreach (string prefijo in new[] { "fivem://connect/", "connect " })
            {
                if (t.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
                {
                    t = t[prefijo.Length..].Trim();
                }
            }
            if (t.Length == 0 || t.Contains('/') || t.Contains(' '))
            {
                return null;
            }

            // [IPv6]:puerto
            if (t.StartsWith('[') && t.IndexOf(']') is int cierre and > 0)
            {
                t = t[1..cierre];
            }
            // IPv4 o nombre con :puerto (un solo ":"; una IPv6 tiene varios)
            else if (t.Count(c => c == ':') == 1)
            {
                string[] partes = t.Split(':');
                if (!int.TryParse(partes[1], out int puerto) || puerto is < 1 or > 65535)
                {
                    return null;
                }
                t = partes[0];
            }

            if (IPAddress.TryParse(t, out IPAddress? ip))
            {
                // "1.2" también es una IPv4 válida para .NET: se exigen los 4 números
                return ip.AddressFamily == AddressFamily.InterNetwork && t.Count(c => c == '.') != 3 ? null : ip.ToString();
            }
            return Uri.CheckHostName(t) == UriHostNameType.Dns && t.Contains('.') ? t.ToLowerInvariant() : null;
        }
    }
}
