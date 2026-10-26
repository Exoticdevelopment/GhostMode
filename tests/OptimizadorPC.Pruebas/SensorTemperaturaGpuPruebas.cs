using System.Diagnostics;
using System.Globalization;
using OptimizadorPC.Pruebas.Infra;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class SensorTemperaturaGpuPruebas
    {
        // ---------- Con la GPU NVIDIA real de este PC ----------

        [Fact]
        public void LeeLaTemperaturaRealIgualQueNvidiaSmi()
        {
            var sensor = new SensorTemperaturaGpu(SensorTemperaturaGpu.RutasOficiales);
            Assert.True(sensor.Disponible, "NVML no está disponible: ¿este PC tiene GPU NVIDIA?");

            double? grados = sensor.Leer();
            int referencia = TemperaturaNvidiaSmi();

            Assert.NotNull(grados);
            Assert.InRange(grados!.Value, 15, 110);
            // Se leen en momentos algo distintos: margen de unos grados
            Assert.InRange(grados.Value, referencia - 5, referencia + 5);
        }

        [Fact]
        public void LeerVariasVecesSigueFuncionando()
        {
            var sensor = new SensorTemperaturaGpu(SensorTemperaturaGpu.RutasOficiales);
            for (int i = 0; i < 5; i++)
            {
                Assert.NotNull(sensor.Leer());
            }
        }

        // ---------- Sin NVML: nunca falla, solo no hay dato ----------

        [Fact]
        public void SinNvmlDllDevuelveNull()
        {
            var sensor = new SensorTemperaturaGpu([@"C:\GhostModePrueba\no-existe\nvml.dll"]);
            Assert.False(sensor.Disponible);
            Assert.Null(sensor.Leer());
        }

        [Fact]
        public void UnaDllQueNoEsNvmlDevuelveNull()
        {
            // kernel32.dll existe y carga, pero no tiene las funciones de NVML
            var sensor = new SensorTemperaturaGpu([Path.Combine(Environment.SystemDirectory, "kernel32.dll")]);
            Assert.False(sensor.Disponible);
            Assert.Null(sensor.Leer());
        }

        [Fact]
        public void UnArchivoDanadoDevuelveNull()
        {
            string carpeta = Ayudas.CarpetaTemporal();
            string falsa = Path.Combine(carpeta, "nvml.dll");
            File.WriteAllText(falsa, "esto no es una DLL");
            try
            {
                var sensor = new SensorTemperaturaGpu([falsa]);
                Assert.False(sensor.Disponible);
                Assert.Null(sensor.Leer());
            }
            finally
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }

        [Fact]
        public void SoloSeBuscaEnLasCarpetasOficialesDelDriver()
        {
            // Nunca "nvml.dll" a secas: Windows la buscaría también junto al .exe de la app
            Assert.All(SensorTemperaturaGpu.RutasOficiales, r => Assert.True(Path.IsPathFullyQualified(r), r));
            Assert.Contains(Path.Combine(Environment.SystemDirectory, "nvml.dll"), SensorTemperaturaGpu.RutasOficiales);
        }

        // ---------- Niveles y colores ----------

        [Theory]
        [InlineData(30, NivelTemperatura.Normal)]
        [InlineData(74.9, NivelTemperatura.Normal)]
        [InlineData(75, NivelTemperatura.Alta)]
        [InlineData(80, NivelTemperatura.Alta)]
        [InlineData(85, NivelTemperatura.Alta)]
        [InlineData(85.1, NivelTemperatura.Critica)]
        [InlineData(95, NivelTemperatura.Critica)]
        public void ClasificaSegunLosLimites(double grados, NivelTemperatura esperado)
        {
            Assert.Equal(esperado, SensorTemperaturaGpu.Clasificar(grados));
        }

        [Theory]
        [InlineData(NivelTemperatura.Normal, "TextoBrush")]
        [InlineData(NivelTemperatura.Alta, "AvisoBrush")]
        [InlineData(NivelTemperatura.Critica, "ErrorBrush")]
        public void ColorDeLaBarraSegunElNivel(NivelTemperatura nivel, string color)
        {
            Assert.Equal(color, Formato.ColorNivel(nivel));
        }

        // nvidia-smi viene con el driver de NVIDIA: la referencia "oficial" para comparar
        private static int TemperaturaNvidiaSmi()
        {
            var configuracion = new ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proceso = Process.Start(configuracion)!;
            string salida = proceso.StandardOutput.ReadToEnd();
            proceso.WaitForExit();
            return int.Parse(salida.Split('\n')[0].Trim(), CultureInfo.InvariantCulture);
        }
    }
}
