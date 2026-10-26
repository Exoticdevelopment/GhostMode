using OptimizadorPC.Limpieza;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    // NUNCA se llama a Limpiar() de las categorías reales ni de la Papelera:
    // solo se mide. El borrado se prueba en una carpeta temporal creada aquí.
    [Collection("Sistema")]
    public class LimpiezaPruebas
    {
        [Fact]
        public void Analizar_MideTodasLasCategoriasSinBorrarNada()
        {
            // Un archivo "viejo" nuestro en %TEMP%: es justo lo que la limpieza borraría
            string carpeta = Ayudas.CarpetaTemporal();
            string testigo = Path.Combine(carpeta, "testigo.bin");
            File.WriteAllBytes(testigo, new byte[4096]);
            File.SetLastWriteTimeUtc(testigo, DateTime.UtcNow.AddDays(-3));

            // Y los archivos viejos que ya había en %TEMP% (los que no se usan desde hace días)
            List<string> viejosAntes = ArchivosViejos(Path.GetTempPath());

            try
            {
                foreach (ICategoriaLimpieza categoria in CatalogoLimpieza.Todas)
                {
                    long bytes = categoria.Medir();
                    Assert.True(bytes >= 0, $"{categoria.Id} midió {bytes}");
                    if (categoria.Id == "temp-usuario")
                    {
                        Assert.True(bytes >= 4096, "La medición de temporales no contó el archivo testigo");
                    }
                }

                Assert.True(File.Exists(testigo), "Medir borró un archivo");
                Assert.DoesNotContain(viejosAntes, a => !File.Exists(a));
            }
            finally
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }

        [Fact]
        public void Papelera_MedirNoLaVacia()
        {
            var papelera = new Papelera();
            long antes = papelera.Medir();
            long despues = papelera.Medir();
            Assert.True(antes >= 0);
            Assert.Equal(antes, despues);
        }

        [Fact]
        public void BorradoSeguro_BorraSoloLoViejoYNoSigueEnlaces()
        {
            string raiz = Ayudas.CarpetaTemporal();
            string carpeta = Path.Combine(raiz, "a-limpiar");
            string fuera = Path.Combine(raiz, "fuera");   // no se limpia: solo se llega por el enlace
            Directory.CreateDirectory(Path.Combine(carpeta, "sub", "profunda"));
            Directory.CreateDirectory(fuera);
            DateTime viejo = DateTime.UtcNow.AddDays(-2);

            string Crear(string ruta, int tamano, bool esViejo)
            {
                File.WriteAllBytes(ruta, new byte[tamano]);
                if (esViejo)
                {
                    File.SetLastWriteTimeUtc(ruta, viejo);
                }
                return ruta;
            }

            string viejo1 = Crear(Path.Combine(carpeta, "viejo.tmp"), 1000, true);
            string viejo2 = Crear(Path.Combine(carpeta, "sub", "profunda", "viejo.log"), 2000, true);
            string soloLectura = Crear(Path.Combine(carpeta, "sub", "solo-lectura.tmp"), 300, true);
            File.SetAttributes(soloLectura, FileAttributes.ReadOnly);
            string nuevo = Crear(Path.Combine(carpeta, "nuevo.tmp"), 500, false);
            string enUso = Crear(Path.Combine(carpeta, "en-uso.tmp"), 700, true);
            string ajeno = Crear(Path.Combine(fuera, "no-tocar.txt"), 50, true);
            string enlace = Path.Combine(carpeta, "enlace");
            Directory.CreateSymbolicLink(enlace, fuera);

            try
            {
                // Medir cuenta lo viejo (incluido el que estará en uso), ni lo nuevo ni lo del enlace
                Assert.Equal(1000 + 2000 + 300 + 700, BorradoSeguro.Medir(carpeta, TimeSpan.FromDays(1)));

                ResultadoLimpieza resultado;
                using (new FileStream(enUso, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    resultado = BorradoSeguro.Borrar(carpeta, TimeSpan.FromDays(1));
                }

                Assert.Equal(1000 + 2000 + 300, resultado.BytesLiberados);
                Assert.Equal(1, resultado.ArchivosEnUso);
                Assert.False(File.Exists(viejo1));
                Assert.False(File.Exists(viejo2));
                Assert.False(File.Exists(soloLectura));
                Assert.True(File.Exists(nuevo));
                Assert.True(File.Exists(enUso));
                Assert.True(File.Exists(ajeno), "Siguió el enlace y borró fuera de la carpeta");
                Assert.True(Directory.Exists(carpeta), "Borró la carpeta en sí");
                Assert.False(Directory.Exists(Path.Combine(carpeta, "sub")), "No quitó las subcarpetas vacías");
            }
            finally
            {
                if (Directory.Exists(enlace))
                {
                    Directory.Delete(enlace);   // solo el enlace, no a donde apunta
                }
                foreach (string archivo in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(archivo, FileAttributes.Normal);
                }
                Directory.Delete(raiz, recursive: true);
            }
        }

        [Fact]
        public void BorradoSeguro_CarpetaQueNoExisteNoFalla()
        {
            string noExiste = Path.Combine(Path.GetTempPath(), "GhostModePrueba-" + Guid.NewGuid().ToString("N"));
            Assert.Equal(0, BorradoSeguro.Medir(noExiste, TimeSpan.Zero));
            Assert.Equal(ResultadoLimpieza.Vacio, BorradoSeguro.Borrar(noExiste, TimeSpan.Zero));
        }

        private static List<string> ArchivosViejos(string carpeta)
        {
            var opciones = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            DateTime limite = DateTime.UtcNow.AddDays(-2);
            return new DirectoryInfo(carpeta).EnumerateFiles("*", opciones)
                .Where(f => f.LastWriteTimeUtc < limite)
                .Select(f => f.FullName)
                .ToList();
        }
    }
}
