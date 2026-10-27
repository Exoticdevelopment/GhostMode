using OptimizadorPC.Benchmark;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class DiagnosticoCuelloPruebas
    {
        // Una medición con ese FPS promedio (el resto da igual para clasificar)
        private static MedicionFps Medicion(double fps, double fps1Low = 0) =>
            new(fps, fps1Low, Frames: (int)(fps * 30), Segundos: 30, FpsPorSegundo: []);

        [Theory]
        // El caso del foro de Cfx.re: 47 FPS y ningún cambio al bajar a 800×600 → CPU o servidor
        [InlineData(47, 47, CuelloBotella.CpuOServidor)]
        [InlineData(50, 54.95, CuelloBotella.CpuOServidor)]   // +9,9 %: por debajo del umbral
        [InlineData(50, 55, CuelloBotella.Gpu)]               // +10 % justo: ya cuenta como que subió
        [InlineData(40, 70, CuelloBotella.Gpu)]               // +75 %
        [InlineData(60, 57, CuelloBotella.CpuOServidor)]      // bajó un poco: es ruido, no GPU
        public void Clasifica_ConElUmbralDel10PorCiento(double normal, double minima, CuelloBotella esperado)
        {
            ResultadoCuello r = DiagnosticoCuello.Evaluar(Medicion(normal), Medicion(minima));
            Assert.Equal(esperado, r.Cuello);
        }

        [Fact]
        public void CambioPorcentaje_SaleDeLosFpsMedidos()
        {
            ResultadoCuello r = DiagnosticoCuello.Evaluar(Medicion(40), Medicion(50));
            Assert.Equal(25, r.CambioPorcentaje, precision: 9);
        }

        [Theory]
        [InlineData(0, 60)]
        [InlineData(60, 0)]
        public void SinFps_NoSeInventaUnResultado(double normal, double minima)
        {
            Assert.Throws<ArgumentException>(() => DiagnosticoCuello.Evaluar(Medicion(normal), Medicion(minima)));
        }

        [Fact]
        public void LasMedicionesDuran30Segundos()
        {
            Assert.Equal(30, DiagnosticoCuello.SegundosPorMedicion);
        }

        [Fact]
        public void Cpu_NoRecomiendaBajarGraficosYMandaAComprobarElServidor()
        {
            var recomendaciones = DiagnosticoCuello.Recomendaciones(CuelloBotella.CpuOServidor);

            // Bajar gráficos aparece, pero como lo que NO sirve
            Assert.Contains(recomendaciones, r => !r.Sirve && r.Texto.Contains("Post FX"));
            Assert.DoesNotContain(recomendaciones, r => r.Sirve && r.Texto.Contains("Post FX"));
            Assert.Contains(recomendaciones, r => r.Sirve && r.Texto.Contains("cl_drawperf"));
            Assert.Contains(recomendaciones, r => r.Sirve && r.Texto.Contains("Modo Gaming"));
            // Lo bloqueado hasta tener FiveM instalado va marcado como pendiente
            Assert.Contains(recomendaciones, r => r.Pendiente && r.Texto.Contains("game_setDefaultEntityCapLimitToLow"));
        }

        [Fact]
        public void Gpu_RecomiendaLosAjustesCarosYNoLosGratis()
        {
            var recomendaciones = DiagnosticoCuello.Recomendaciones(CuelloBotella.Gpu);

            Assert.Contains(recomendaciones, r => r.Sirve && r.Texto.Contains("GPU dedicada"));
            Assert.Contains(recomendaciones, r => r.Sirve && r.Texto.Contains("MSAA"));
            Assert.Contains(recomendaciones, r => !r.Sirve && r.Texto.Contains("anisotrópico"));
            Assert.Contains(recomendaciones, r => r.Pendiente);
        }

        [Fact]
        public void Explicacion_LlevaElCambioMedido()
        {
            Assert.Contains("(+4 %)", DiagnosticoCuello.Explicacion(CuelloBotella.CpuOServidor, "+4 %"));
            Assert.Contains("subieron +31 %", DiagnosticoCuello.Explicacion(CuelloBotella.Gpu, "+31 %"));
        }
    }
}
