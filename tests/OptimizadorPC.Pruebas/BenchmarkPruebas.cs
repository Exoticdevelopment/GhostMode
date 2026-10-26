using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptimizadorPC.Benchmark;
using OptimizadorPC.Core;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class BenchmarkPruebas
    {
        private static readonly string ExePresentMon = Path.Combine(Logger.Carpeta, "herramientas", "PresentMon.exe");

        // ---------- AnalizadorFrames ----------

        [Fact]
        public void AnalizadorFrames_PromedioY1LowConDatosConocidos()
        {
            // 95 frames de 10 ms (100 FPS) y 5 de 40 ms (25 FPS). Total: 950 + 200 = 1150 ms
            var tiempos = Enumerable.Repeat(10.0, 95).Concat(Enumerable.Repeat(40.0, 5)).ToList();
            MedicionFps m = Analizar(Csv(tiempos.Select(t => ("0x1", t))));

            Assert.Equal(100, m.Frames);
            Assert.Equal(1.15, m.Segundos, precision: 6);
            Assert.Equal(100 / 1.15, m.FpsPromedio, precision: 6);   // 86,96 FPS
            // Percentil 99 de 100 tiempos = el 99.º más rápido = 40 ms → 25 FPS
            Assert.Equal(25, m.Fps1Low, precision: 6);
            // Primer segundo: 95 frames de 10 ms (950 ms) + 2 de 40 ms = 1030 ms → 97 frames
            Assert.Equal([97.0], m.FpsPorSegundo);
        }

        [Fact]
        public void AnalizadorFrames_RitmoConstante()
        {
            // 120 frames de 16,666 ms = 2 s exactos a 60 FPS
            var tiempos = Enumerable.Repeat(1000.0 / 60, 120);
            MedicionFps m = Analizar(Csv(tiempos.Select(t => ("0x1", t))));

            Assert.Equal(60, m.FpsPromedio, precision: 6);
            Assert.Equal(60, m.Fps1Low, precision: 6);
            Assert.Equal(2, m.Segundos, precision: 6);
        }

        [Fact]
        public void AnalizadorFrames_UsaLaSwapChainConMasFramesYSaltaFilasInvalidas()
        {
            var filas = Enumerable.Repeat(("0xJUEGO", 20.0), 50)        // el juego: 50 FPS
                .Concat(Enumerable.Repeat(("0xOVERLAY", 5.0), 10))       // un overlay: menos frames
                .ToList();
            string csv = Csv(filas) + "juego.exe,1,0xJUEGO,NA\njuego.exe,1,0xJUEGO,0\n";
            MedicionFps m = Analizar(csv);

            Assert.Equal(50, m.Frames);
            Assert.Equal(50, m.FpsPromedio, precision: 6);
        }

        [Fact]
        public void AnalizadorFrames_RechazaCapturasSinDatosSuficientes()
        {
            Assert.Throws<InvalidOperationException>(() => Analizar(Csv(Enumerable.Repeat(("0x1", 10.0), 29))));
            Assert.Throws<InvalidOperationException>(() => Analizar("Application,ProcessID,SwapChainAddress,MsBetweenPresents\n"));
            Assert.Throws<InvalidOperationException>(() => Analizar("Application,Otra\njuego.exe,1\n"));
        }

        // CSV con las mismas columnas que escribe PresentMon. Tiempos con punto decimal (cultura invariante)
        private static string Csv(IEnumerable<(string SwapChain, double Ms)> frames)
        {
            var texto = new StringBuilder("Application,ProcessID,SwapChainAddress,MsBetweenPresents\n");
            foreach (var (swapChain, ms) in frames)
            {
                texto.Append($"juego.exe,1234,{swapChain},{ms.ToString("R", CultureInfo.InvariantCulture)}\n");
            }
            return texto.ToString();
        }

        private static MedicionFps Analizar(string csv)
        {
            string archivo = Path.Combine(Path.GetTempPath(), $"GhostModePrueba-{Guid.NewGuid():N}.csv");
            File.WriteAllText(archivo, csv);
            try
            {
                return AnalizadorFrames.Analizar(archivo);
            }
            finally
            {
                File.Delete(archivo);
            }
        }

        // ---------- PresentMon (necesita internet) ----------

        [Fact]
        public async Task PresentMon_SeDescargaVerificaLaHuellaYResponde()
        {
            if (!PresentMon.EstaDescargado)
            {
                await PresentMon.DescargarAsync(CancellationToken.None);
            }
            Assert.True(PresentMon.EstaDescargado);
            Assert.False(File.Exists(ExePresentMon + ".descarga"), "Quedó el archivo temporal de la descarga");

            // La huella del archivo en disco es la que publica GitHub para esa versión
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostMode-Pruebas");
            using JsonDocument release = JsonDocument.Parse(
                await http.GetStringAsync("https://api.github.com/repos/GameTechDev/PresentMon/releases/latest"));
            JsonElement asset = release.RootElement.GetProperty("assets").EnumerateArray()
                .First(a => Regex.IsMatch(a.GetProperty("name").GetString()!, @"^PresentMon-[\d.]+-x64\.exe$", RegexOptions.IgnoreCase));
            string esperada = asset.GetProperty("digest").GetString()!.Replace("sha256:", "");
            string real = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(ExePresentMon)));
            Assert.Equal(esperada, real, ignoreCase: true);

            // Y es un PresentMon de verdad: --help explica las opciones que usa la app
            var configuracion = new ProcessStartInfo(ExePresentMon, "--help")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proceso = Process.Start(configuracion)!;
            Task<string> salida = proceso.StandardOutput.ReadToEndAsync();
            Task<string> errores = proceso.StandardError.ReadToEndAsync();
            Assert.True(proceso.WaitForExit(15000), "PresentMon --help no terminó");
            string ayuda = await salida + await errores;

            foreach (string opcion in new[] { "--process_id", "--output_file", "--timed", "--terminate_after_timed",
                                              "--terminate_on_proc_exit", "--session_name", "--stop_existing_session",
                                              "--no_console_stats" })
            {
                Assert.Contains(opcion, ayuda);
            }
        }
    }
}
