using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Pruebas.Infra;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Pruebas
{
    // Diagnóstico de gráficos de FiveM (solo lectura) con datos conocidos y un settings.xml de ejemplo
    [Collection("Sistema")]
    public class DiagnosticoGraficosPruebas
    {
        private const string SettingsCaro = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Settings>
              <version value="27" />
              <graphics>
                <TextureQuality value="2" />
                <GrassQuality value="3" />
                <ShadowQuality value="2" />
                <MSAA value="4" />
                <ReflectionMSAA value="2" />
                <CityDensity value="1.000000" />
                <MaxLodScale value="0.500000" />
              </graphics>
            </Settings>
            """;

        [Fact]
        public void LeeElSettingsXmlDeGtaV()
        {
            string carpeta = Ayudas.CarpetaTemporal();
            string ruta = Path.Combine(carpeta, "settings.xml");
            try
            {
                File.WriteAllText(ruta, SettingsCaro);
                AjustesGta a = DiagnosticoGraficos.LeerSettings(ruta)!;

                Assert.Equal(new AjustesGta(2, 3, 2, 4, 2, 1.0, 0.5), a);

                File.WriteAllText(ruta, "<roto");
                Assert.Null(DiagnosticoGraficos.LeerSettings(ruta));   // dañado: no falla
                Assert.Null(DiagnosticoGraficos.LeerSettings(Path.Combine(carpeta, "no-existe.xml")));
            }
            finally
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }

        [Fact]
        public void PcBajoConAjustesCarosRecibeAvisosClaros()
        {
            var equipo = new Diagnostico(8, "NVIDIA GeForce GTX 1050", true, 2, TipoDisco.Hdd, 50, 500);
            List<Consejo> c = DiagnosticoGraficos.Recomendaciones(equipo, 4, new AjustesGta(2, 3, 2, 4, 2, 1.0, 0.5));

            Assert.Contains(c, x => x.Texto.Contains("MSAA x4"));
            Assert.Contains(c, x => x.Texto.Contains("reflejos"));
            Assert.Contains(c, x => x.Texto.Contains("Hierba"));
            Assert.Contains(c, x => x.Texto.Contains("Muy alta") && x.Texto.Contains("VRAM"));
            Assert.Contains(c, x => x.Texto.Contains("distancia extendido"));
            Assert.Contains(c, x => x.Texto.Contains("Densidad de población"));
            Assert.Contains(c, x => x.Texto.Contains("8 GB de RAM"));
            Assert.Contains(c, x => x.Texto.Contains("4 hilos"));
            Assert.All(c, x => Assert.Equal(NivelConsejo.Aviso, x.Nivel));
        }

        [Fact]
        public void GraficaIntegradaSinSettings()
        {
            var equipo = new Diagnostico(16, "Intel(R) UHD Graphics", false, 1, TipoDisco.Ssd, 100, 500);
            List<Consejo> c = DiagnosticoGraficos.Recomendaciones(equipo, 8, null);
            Consejo unico = Assert.Single(c);
            Assert.Contains("integrada", unico.Texto);
        }

        [Fact]
        public void EquipoHolgadoNoInventaProblemas()
        {
            var equipo = new Diagnostico(32, "NVIDIA GeForce RTX 4070", true, 12, TipoDisco.Ssd, 400, 1000);
            List<Consejo> c = DiagnosticoGraficos.Recomendaciones(equipo, 16, new AjustesGta(2, 1, 2, 0, 0, 1.0, 0));
            Consejo unico = Assert.Single(c);
            Assert.Equal(NivelConsejo.Bien, unico.Nivel);
        }

        [Fact]
        public void EsteEquipo_6GbDeVramSoloRecibeConsejoInformativo()
        {
            // RTX 3050 de 6 GB, 16 GB, 12 hilos: sin avisos, solo el límite de texturas
            var equipo = new Diagnostico(16, "NVIDIA GeForce RTX 3050 6GB Laptop GPU", true, 6, TipoDisco.Ssd, 61, 250);
            List<Consejo> c = DiagnosticoGraficos.Recomendaciones(equipo, 12, null);
            Assert.DoesNotContain(c, x => x.Nivel == NivelConsejo.Aviso);
            Assert.Contains(c, x => x.Texto.Contains("texturas en Alta"));
        }
    }
}
