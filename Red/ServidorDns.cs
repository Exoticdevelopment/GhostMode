namespace OptimizadorPC.Red
{
    // Un servidor DNS de la lista. Automático = sin direcciones: las que dé el router (DHCP)
    public record ServidorDns(string Id, string Nombre, string[] Ipv4, string[] Ipv6)
    {
        public bool EsAutomatico => Ipv4.Length == 0;

        // "1.1.1.1 / 1.0.0.1" o "asignado por el router"
        public string TextoDirecciones => EsAutomatico ? "asignado por el router" : string.Join(" / ", Ipv4);

        public static readonly ServidorDns Automatico = new("automatico", "Automático", [], []);

        // Direcciones oficiales de cada servicio (principal y secundaria, IPv4 e IPv6)
        public static readonly IReadOnlyList<ServidorDns> Todos =
        [
            Automatico,
            new("cloudflare", "Cloudflare", ["1.1.1.1", "1.0.0.1"], ["2606:4700:4700::1111", "2606:4700:4700::1001"]),
            new("google", "Google", ["8.8.8.8", "8.8.4.4"], ["2001:4860:4860::8888", "2001:4860:4860::8844"]),
            new("quad9", "Quad9", ["9.9.9.9", "149.112.112.112"], ["2620:fe::fe", "2620:fe::9"]),
        ];
    }
}
