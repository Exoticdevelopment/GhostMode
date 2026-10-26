namespace OptimizadorPC.Servicios
{
    // Textos que se repiten en varias pantallas
    public static class Formato
    {
        // "640 MB", "1.960 MB" → hasta 10 GB en MB (como el diseño); a partir de ahí "12,4 GB".
        // N0 = número sin decimales con separador de miles; F1 = un decimal. InfoSistema.Es = estilo español
        public static string Tamano(long bytes)
        {
            double mb = bytes / 1048576.0;   // 1 MB = 1024 × 1024 bytes
            return mb >= 10240
                ? $"{(mb / 1024).ToString("F1", InfoSistema.Es)} GB"
                : $"{mb.ToString("N0", InfoSistema.Es)} MB";
        }

        // Temperatura de la GPU: lo que se escribe debajo del valor y el color (nombre en Colores.xaml)
        public static string TextoNivel(NivelTemperatura nivel) => nivel switch
        {
            NivelTemperatura.Critica => "muy alta · más de 85 °C",
            NivelTemperatura.Alta => "alta · de 75 a 85 °C",
            _ => "normal · menos de 75 °C"
        };

        public static string ColorNivel(NivelTemperatura nivel) => nivel switch
        {
            NivelTemperatura.Critica => "ErrorBrush",
            NivelTemperatura.Alta => "AvisoBrush",
            _ => "TextoBrush"   // el mismo color que las barras de CPU, RAM y GPU
        };
    }
}
