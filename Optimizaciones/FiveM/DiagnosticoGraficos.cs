using System.Globalization;
using System.IO;
using System.Xml.Linq;
using OptimizadorPC.Core;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Optimizaciones.FiveM
{
    // Lo que se leyó del settings.xml de GTA V (null = no aparece en el archivo)
    public record AjustesGta(
        int? Texturas,               // TextureQuality: 0 normal, 1 alta, 2 muy alta
        int? Hierba,                 // GrassQuality: 0 normal, 1 alta, 2 muy alta, 3 ultra
        int? Sombras,                // ShadowQuality
        int? Msaa,                   // MSAA: 0, 2, 4 u 8
        int? ReflejosMsaa,           // ReflectionMSAA: 0, 2, 4 u 8
        double? Densidad,            // CityDensity: población, 0 a 1
        double? DistanciaExtendida); // MaxLodScale: escalado de distancia extendido, 0 a 1

    // Diagnóstico de gráficos para FiveM, SOLO LECTURA: no cambia nada.
    // Cruza el equipo (VRAM, RAM, hilos de CPU) con los ajustes de GTA V, si están, y da recomendaciones claras
    // para PC modestos. Ojo (investigación de docs/investigacion-fivem.md): hay informes de que FiveM ya no lee
    // ese settings.xml en builds recientes, así que la pantalla lo dice y las recomendaciones de hardware valen igual.
    public static class DiagnosticoGraficos
    {
        public static string RutaSettings => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games", "GTA V", "settings.xml");

        // null si no existe o no se puede leer
        public static AjustesGta? LeerSettings(string ruta)
        {
            try
            {
                if (!File.Exists(ruta))
                {
                    return null;
                }
                XElement? graficos = XDocument.Load(ruta).Root?.Element("graphics");
                if (graficos == null)
                {
                    return null;
                }
                string? Valor(string nombre) => graficos.Element(nombre)?.Attribute("value")?.Value;
                int? Entero(string n) => int.TryParse(Valor(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
                double? Decimal(string n) => double.TryParse(Valor(n), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

                return new AjustesGta(Entero("TextureQuality"), Entero("GrassQuality"), Entero("ShadowQuality"),
                                      Entero("MSAA"), Entero("ReflectionMSAA"), Decimal("CityDensity"), Decimal("MaxLodScale"));
            }
            catch (Exception ex)
            {
                Logger.Aviso($"No se pudo leer el settings.xml de GTA V: {ex.Message}");
                return null;
            }
        }

        public static List<Consejo> Recomendaciones(Diagnostico equipo, int hilos, AjustesGta? juego)
        {
            var c = new List<Consejo>();
            bool integrada = equipo.Gpu != null && !equipo.GpuDedicada;
            double? vram = equipo.VramGb;

            // ----- Según el hardware (valen aunque FiveM no use settings.xml) -----
            if (integrada || vram is < 2.5)
            {
                c.Add(new(NivelConsejo.Aviso, "Gráfica integrada o con muy poca memoria: texturas en Normal, MSAA apagado (usa FXAA), " +
                                               "hierba en Normal y, si sigue lento, baja la escala de resolución."));
            }
            else if (vram is < 4)
            {
                c.Add(new(NivelConsejo.Aviso, $"Con {vram:0.#} GB de VRAM: texturas en Normal y el «presupuesto de texturas extendido» de FiveM " +
                                               "al mínimo. Los servidores con muchos coches y ropa propios llenan la VRAM rápido."));
            }
            else if (vram is < 6.5)
            {
                c.Add(new(NivelConsejo.Info, $"Con {vram:0.#} GB de VRAM: texturas en Alta como máximo; «Muy alta» llena la memoria en servidores con mods."));
            }

            if (equipo.RamGb is <= 8)
            {
                c.Add(new(NivelConsejo.Aviso, $"Con {equipo.RamGb} GB de RAM: densidad y variedad de población al mínimo y escalado de distancia " +
                                               "extendido a 0. Cierra el navegador antes de jugar."));
            }
            if (hilos <= 4)
            {
                c.Add(new(NivelConsejo.Aviso, $"CPU de {hilos} hilos: FiveM suele quedarse sin CPU antes que sin gráfica. " +
                                               "Hierba y sombras en Normal (también gastan CPU) y población baja."));
            }

            // ----- Según lo que tiene puesto GTA V -----
            if (juego != null)
            {
                if (juego.Msaa is > 0)
                {
                    c.Add(new(NivelConsejo.Aviso, $"MSAA x{juego.Msaa} está activado: es el ajuste que más FPS quita. Apágalo y usa FXAA."));
                }
                if (juego.ReflejosMsaa is > 0)
                {
                    c.Add(new(NivelConsejo.Aviso, $"MSAA de reflejos x{juego.ReflejosMsaa}: apenas se ve y cuesta FPS. Apágalo."));
                }
                if (juego.Hierba is >= 2)
                {
                    c.Add(new(NivelConsejo.Aviso, "Hierba en «Muy alta» o «Ultra»: en zonas rurales hunde los FPS. «Normal» se ve casi igual."));
                }
                if (juego.Texturas is >= 2 && vram is < 6)
                {
                    c.Add(new(NivelConsejo.Aviso, "Texturas en «Muy alta» con menos de 6 GB de VRAM: causa tirones y texturas borrosas al cargar."));
                }
                if (juego.DistanciaExtendida is > 0.01 && (equipo.RamGb is <= 8 || hilos <= 4))
                {
                    c.Add(new(NivelConsejo.Aviso, "El escalado de distancia extendido está activo: pide mucha CPU y RAM. Déjalo en 0."));
                }
                if (juego.Densidad is > 0.6 && equipo.RamGb is <= 8)
                {
                    c.Add(new(NivelConsejo.Aviso, "Densidad de población alta con 8 GB de RAM o menos: bájala a la mitad o menos."));
                }
            }

            if (c.Count == 0)
            {
                c.Add(new(NivelConsejo.Bien, "Tu equipo aguanta FiveM con ajustes medios-altos. Si hay tirones, empieza bajando hierba y sombras."));
            }
            return c;
        }
    }
}
