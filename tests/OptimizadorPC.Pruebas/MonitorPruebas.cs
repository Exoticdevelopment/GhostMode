using System.Management;
using System.Reflection;
using OptimizadorPC.Pruebas.Infra;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class MonitorPruebas
    {
        [Fact]
        public void Monitor_DevuelveValoresRealesDeCpuRamGpuYDisco()
        {
            var monitor = new MonitorRecursos(detallado: true);
            // Medir() es lo que el reloj ejecuta en segundo plano; lo llamamos directo, sin interfaz
            MethodInfo medir = typeof(MonitorRecursos).GetMethod("Medir", BindingFlags.NonPublic | BindingFlags.Instance)!;

            // La CPU y los contadores de disco necesitan dos lecturas: la primera solo deja la referencia
            var primera = (MuestraRecursos)medir.Invoke(monitor, null)!;
            Assert.Null(primera.CpuPorcentaje);

            // Un poco de trabajo real entre las dos lecturas: CPU y escritura en disco
            string archivo = Path.Combine(Path.GetTempPath(), $"GhostModePrueba-{Guid.NewGuid():N}.bin");
            try
            {
                var fin = DateTime.UtcNow.AddSeconds(1.5);
                using (var flujo = new FileStream(archivo, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                  4096, FileOptions.WriteThrough))
                {
                    var bloque = new byte[1024 * 1024];
                    Random.Shared.NextBytes(bloque);
                    while (DateTime.UtcNow < fin)
                    {
                        flujo.Write(bloque);
                        flujo.Flush(flushToDisk: true);
                    }
                }

                var m = (MuestraRecursos)medir.Invoke(monitor, null)!;

                Assert.NotNull(m.CpuPorcentaje);
                Assert.InRange(m.CpuPorcentaje!.Value, 0.01, 100);

                Assert.NotNull(m.RamUsadaGb);
                Assert.True(m.RamUsadaGb > 0.5, $"RAM usada: {m.RamUsadaGb} GB");
                Assert.True(m.RamLibreGb > 0, $"RAM libre: {m.RamLibreGb} GB");
                Assert.InRange(m.RamPorcentaje!.Value, 1, 100);

                // GPU: puede estar al 0 % de verdad si nada dibuja; lo importante es que el contador exista y lea
                Assert.NotNull(m.GpuPorcentaje);
                Assert.InRange(m.GpuPorcentaje!.Value, 0, 100);
                Assert.True(MotoresGpu3D() > 0, "Windows no expone motores 3D de la GPU");

                // Temperatura de la GPU NVIDIA (NVML)
                Assert.NotNull(m.GpuTemperatura);
                Assert.InRange(m.GpuTemperatura!.Value, 15, 110);

                Assert.NotNull(m.DiscoPorcentaje);
                Assert.InRange(m.DiscoPorcentaje!.Value, 0.01, 100);
                Assert.True(m.DiscoEscrituraMBs > 0, $"Escritura de disco: {m.DiscoEscrituraMBs} MB/s");
                Assert.NotNull(m.DiscoLecturaMBs);
                Assert.True(m.Procesos > 10, $"Procesos: {m.Procesos}");
                Assert.True(m.AppRamMb > 0);
                Assert.NotNull(m.AppCpuPorcentaje);
            }
            finally
            {
                monitor.Detener();
                File.Delete(archivo);
            }
        }

        private static int MotoresGpu3D()
        {
            using var buscador = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE '%engtype_3D'");
            return buscador.Get().Count;
        }
    }
}
