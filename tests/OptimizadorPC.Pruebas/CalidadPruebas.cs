using System.IO.Compression;
using OptimizadorPC.Core;
using OptimizadorPC.Pruebas.Infra;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Pruebas
{
    // Calidad: rotación del log, exportar diagnóstico sin datos personales y errores con contexto
    [Collection("Sistema")]
    public class CalidadPruebas
    {
        // ---------- Rotación del log ----------

        [Fact]
        public void Log_RotaPisandoElAnteriorYDejaUnoNuevo()
        {
            string carpeta = Ayudas.CarpetaTemporal();
            string log = Path.Combine(carpeta, "log.txt");
            string anterior = Path.Combine(carpeta, "log.1.txt");
            try
            {
                File.WriteAllText(anterior, "muy viejo");
                File.WriteAllText(log, new string('x', 1000));

                long nuevo = Logger.Rotar(log, anterior);

                Assert.Equal(new string('x', 1000), File.ReadAllText(anterior));   // el viejo se pisó
                Assert.Contains("Log rotado", File.ReadAllText(log));
                Assert.Equal(new FileInfo(log).Length, nuevo);
            }
            finally
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }

        [Fact]
        public void Log_ElLimiteEsDe5Mb()
        {
            Assert.Equal(5 * 1024 * 1024, Logger.TamanoMaximo);
            Assert.Equal(Path.Combine(Logger.Carpeta, "log.1.txt"), Logger.ArchivoAnterior);
        }

        // ---------- Exportar diagnóstico ----------

        [Fact]
        public async Task Diagnostico_ZipConTodoYSinDatosPersonales()
        {
            string datos = Ayudas.CarpetaTemporal();
            string destino = Path.Combine(Ayudas.CarpetaTemporal(), "diagnostico.zip");
            string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            try
            {
                // Archivos falsos con lo que no debe salir: ruta del perfil (también escapada como en JSON),
                // nombre de usuario, nombre del equipo e IP de la red de casa
                File.WriteAllText(Path.Combine(datos, "log.txt"),
                    $"[INFO] juego en {perfil}\\Juegos\\x.exe\n[INFO] equipo {Environment.MachineName}, ip 192.168.1.13 y 10.0.0.7\n" +
                    $"[INFO] usuario {Environment.UserName} · autor Exoticdevelopment · dns 1.1.1.1");
                File.WriteAllText(Path.Combine(datos, "modo_gaming.json"), $"{{\"Exe\":\"{perfil.Replace("\\", "\\\\")}\\\\a.exe\"}}");
                File.WriteAllText(Path.Combine(datos, "estado.json"), "{}");

                await ExportadorDiagnostico.CrearAsync(destino, datos);

                using ZipArchive zip = ZipFile.OpenRead(destino);
                Assert.Equal(["equipo.txt", "estado.json", "log.txt", "modo_gaming.json"], zip.Entries.Select(e => e.Name).Order());
                foreach (ZipArchiveEntry entrada in zip.Entries)
                {
                    using var lector = new StreamReader(entrada.Open());
                    string texto = lector.ReadToEnd();
                    Assert.DoesNotContain(Environment.MachineName, texto, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(perfil, texto, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("192.168.1.13", texto);
                    Assert.DoesNotContain("10.0.0.7", texto);
                    Assert.DoesNotMatch($@"(?i)(?<![\p{{L}}]){Environment.UserName}(?![\p{{L}}])", texto);
                    if (entrada.Name == "log.txt")
                    {
                        Assert.Contains(@"C:\Users\<usuario>\Juegos\x.exe", texto);
                        Assert.Contains("Exoticdevelopment", texto);   // no se rompen palabras que lo contienen
                        Assert.Contains("1.1.1.1", texto);             // las IP públicas sí sirven para diagnosticar
                    }
                    if (entrada.Name == "equipo.txt")
                    {
                        Assert.Contains("RAM: 16 GB", texto);
                        Assert.Contains("GPU:", texto);
                    }
                }
                Assert.False(File.Exists(destino + ".tmp"));
            }
            finally
            {
                Directory.Delete(datos, recursive: true);
                Directory.Delete(Path.GetDirectoryName(destino)!, recursive: true);
            }
        }

        // ---------- Errores con contexto ----------

        [Fact]
        public void Errores_LlevanElTipoYLosInesperadosLaTraza()
        {
            Logger.Error("Prueba de calidad: error esperado", new IOException("disco lleno"));
            Logger.Fallo("Prueba de calidad: error inesperado", Lanzar());

            List<LineaLog> ultimas = Logger.Recientes.TakeLast(2).ToList();
            Assert.EndsWith("disco lleno (IOException)", ultimas[0].Mensaje);
            Assert.Contains("InvalidOperationException", ultimas[1].Mensaje);
            Assert.Contains(nameof(Lanzar), ultimas[1].Mensaje);   // la traza dice dónde pasó
        }

        private static Exception Lanzar()
        {
            try
            {
                throw new InvalidOperationException("algo raro");
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
