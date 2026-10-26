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
    }
}
