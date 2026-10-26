using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;   // DllImport

namespace OptimizadorPC.Red
{
    // El adaptador por el que sale el tráfico a internet
    public record InfoAdaptador(
        string Id,                         // {GUID} de Windows: así se llama su carpeta en el registro
        int Indice,                        // número de interfaz (lo usa netsh)
        string Nombre,                     // "Wi-Fi", "Ethernet"
        string Descripcion,                // "Realtek 8852BE ... WiFi 6"
        bool EsWifi,
        string? IpLocal,                   // 192.168.1.34
        long VelocidadBps,                 // velocidad del enlace que informa Windows
        IReadOnlyList<string> Dns)         // servidores DNS que usa ahora
    {
        // "650 Mbps", "1 Gbps"
        public string TextoVelocidad => VelocidadBps switch
        {
            <= 0 => "—",
            >= 1_000_000_000 when VelocidadBps % 1_000_000_000 == 0 => $"{VelocidadBps / 1_000_000_000} Gbps",
            >= 1_000_000 => $"{VelocidadBps / 1_000_000} Mbps",
            _ => $"{VelocidadBps / 1000} Kbps"
        };

        public string TextoTipo => EsWifi ? "Wi-Fi" : "Cable (Ethernet)";
    }

    public static class AdaptadorRed
    {
        // Windows elige por qué adaptador mandar algo a 1.1.1.1 con su tabla de rutas (GetBestInterface);
        // ese es el que usa internet. null = sin conexión
        public static InfoAdaptador? Activo()
        {
            uint destino = BitConverter.ToUInt32(IPAddress.Parse("1.1.1.1").GetAddressBytes(), 0);
            if (GetBestInterface(destino, out uint indice) != 0)
            {
                return null;
            }

            foreach (NetworkInterface tarjeta in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties ip = tarjeta.GetIPProperties();
                IPv4InterfaceProperties? v4;
                try
                {
                    v4 = ip.GetIPv4Properties();
                }
                catch (NetworkInformationException)
                {
                    continue;   // sin IPv4
                }
                if (v4 == null || v4.Index != indice)
                {
                    continue;
                }

                string? ipLocal = ip.UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
                return new InfoAdaptador(
                    tarjeta.Id, (int)indice, tarjeta.Name, tarjeta.Description,
                    tarjeta.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                    ipLocal, tarjeta.Speed,
                    ip.DnsAddresses.Select(d => d.ToString()).ToList());
            }
            return null;
        }

        [DllImport("iphlpapi.dll")]
        private static extern int GetBestInterface(uint destino, out uint indice);
    }
}
