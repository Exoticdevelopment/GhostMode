using OptimizadorPC.Servicios;

namespace OptimizadorPC.Pruebas
{
    public class InfoSistemaPruebas
    {
        [Theory]
        // CPU
        [InlineData("13th Gen Intel(R) Core(TM) i5-13420H", "i5-13420H")]
        [InlineData("Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz", "i5-8250U")]
        [InlineData("Intel(R) Core(TM) i7-10700K CPU @ 3.80GHz", "i7-10700K")]
        [InlineData("Intel(R) Core(TM) Ultra 7 155H", "Ultra 7 155H")]
        [InlineData("AMD Ryzen 5 5600X 6-Core Processor", "Ryzen 5 5600X")]
        [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", "Ryzen 7 7840HS")]
        [InlineData("Intel(R) Pentium(R) Silver N5000 CPU @ 1.10GHz", "Intel Pentium Silver N5000")]   // sin patrón: nombre limpio
        public void Resumen_CpuCorta(string cpu, string esperado)
        {
            Assert.Equal(esperado, InfoSistema.Resumen(new DatosEquipo(cpu, null, null, null, null, "Windows 11")));
        }

        [Theory]
        [InlineData("NVIDIA GeForce RTX 3050 6GB Laptop GPU", "RTX 3050")]
        [InlineData("NVIDIA GeForce GTX 1650 Ti", "GTX 1650")]
        [InlineData("NVIDIA GeForce MX450", "MX450")]
        [InlineData("AMD Radeon RX 6600", "RX 6600")]
        [InlineData("Intel(R) Arc(TM) A770 Graphics", "Arc A770")]
        [InlineData("Intel(R) UHD Graphics 620", "Intel UHD Graphics 620")]
        public void Resumen_GpuCorta(string gpu, string esperado)
        {
            Assert.Equal(esperado, InfoSistema.Resumen(new DatosEquipo(null, null, null, null, gpu, "Windows 11")));
        }

        [Fact]
        public void Resumen_LineaCompletaYSinObjetosInternos()
        {
            var equipo = new DatosEquipo("13th Gen Intel(R) Core(TM) i5-13420H", 8, 2.1, 16,
                                         "NVIDIA GeForce RTX 3050 6GB Laptop GPU", "Windows 11");
            Assert.Equal("i5-13420H · 8 núcleos · 16 GB · RTX 3050", InfoSistema.Resumen(equipo));
        }

        [Fact]
        public void Resumen_SinDatosNoInventaNada()
        {
            Assert.Equal("sin datos de hardware",
                         InfoSistema.Resumen(new DatosEquipo(null, null, null, null, null, "Windows 11")));
        }
    }
}
