using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Pruebas.Infra;
using OptimizadorPC.Servicios;   // DiagnosticoEquipo, TipoDisco

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class DiscoJuegoPruebas
    {
        [Theory]
        [InlineData(3, 11, TipoDisco.Hdd)]           // HDD por SATA
        [InlineData(4, 11, TipoDisco.Ssd)]           // SSD por SATA
        [InlineData(4, 17, TipoDisco.Ssd)]           // SSD NVMe
        [InlineData(5, 17, TipoDisco.Ssd)]           // SCM: memoria persistente
        [InlineData(0, 17, TipoDisco.Ssd)]           // NVMe sin MediaType: un NVMe siempre es SSD
        [InlineData(0, 11, TipoDisco.Desconocido)]   // SATA sin MediaType: no se adivina
        [InlineData(0, 7, TipoDisco.Desconocido)]    // USB sin MediaType
        public void Clasificar_SoloAfirmaLoQueWindowsDice(int mediaType, int busType, TipoDisco esperado)
        {
            Assert.Equal(esperado, DiagnosticoEquipo.ClasificarDisco(mediaType, busType));
        }

        [Theory]
        [InlineData(@"D:\Juegos\Grand Theft Auto V", 'D')]
        [InlineData(@"c:\Users\Ana\AppData\Local\FiveM", 'C')]
        [InlineData(@"\\servidor\juegos\GTA V", null)]   // de red: no es un disco de este PC
        [InlineData(@"Juegos\GTA V", null)]              // relativa
        public void UnidadDe(string ruta, char? esperada)
        {
            Assert.Equal(esperada, DiscoJuego.UnidadDe(ruta));
        }

        [Fact]
        public void TipoDeUnidad_ElDiscoDelSistemaSeLeeSinFallar()
        {
            char sistema = Environment.SystemDirectory[0];
            TipoDisco tipo = DiagnosticoEquipo.TipoDiscoDe(sistema);
            Assert.True(Enum.IsDefined(tipo));
            // Una letra sin ninguna partición: Desconocido, nunca un error ni un HDD inventado
            char libre = "ZYXWVUTSRQPONMLKJIHGFED".First(l => !Directory.Exists($"{l}:\\"));
            Assert.Equal(TipoDisco.Desconocido, DiagnosticoEquipo.TipoDiscoDe(libre));
        }

        [Fact]
        public void LeerIvPath_LaCarpetaDeGtaQueUsaFiveM()
        {
            string carpeta = Ayudas.CarpetaTemporal();
            try
            {
                string ini = Path.Combine(carpeta, "CitizenFX.ini");
                File.WriteAllText(ini, "[Game]\r\nIVPath=E:\\Rockstar Games\\Grand Theft Auto V\r\nSavedBuildNumber=3095\r\n");
                Assert.Equal(@"E:\Rockstar Games\Grand Theft Auto V", DiscoJuego.LeerIvPath(ini));

                // IVPath fuera de [Game] no cuenta
                File.WriteAllText(ini, "[Otro]\r\nIVPath=E:\\nada\r\n[Game]\r\nSavedBuildNumber=3095\r\n");
                Assert.Null(DiscoJuego.LeerIvPath(ini));

                File.WriteAllText(ini, "[Game]\r\nIVPath=\r\n");
                Assert.Null(DiscoJuego.LeerIvPath(ini));

                Assert.Null(DiscoJuego.LeerIvPath(Path.Combine(carpeta, "no-existe.ini")));
            }
            finally
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }

        [Fact]
        public void TextoAviso_SoloConHdd()
        {
            var fivemSsd = new UbicacionEnDisco("FiveM", @"C:\FiveM", 'C', TipoDisco.Ssd);
            var gtaHdd = new UbicacionEnDisco("GTA V", @"D:\GTA V", 'D', TipoDisco.Hdd);
            var gtaDesconocido = gtaHdd with { Tipo = TipoDisco.Desconocido };

            Assert.Null(DiscoJuego.TextoAviso([]));
            Assert.Null(DiscoJuego.TextoAviso([fivemSsd]));
            Assert.Null(DiscoJuego.TextoAviso([fivemSsd, gtaDesconocido]));   // lo que no se sabe no se avisa

            string? aviso = DiscoJuego.TextoAviso([fivemSsd, gtaHdd]);
            Assert.NotNull(aviso);
            Assert.StartsWith("GTA V está en un disco duro (HDD, unidad D:).", aviso);
            Assert.Contains(DiscoJuego.Aviso, aviso);
            Assert.DoesNotContain("FiveM", aviso);

            string? ambos = DiscoJuego.TextoAviso([fivemSsd with { Tipo = TipoDisco.Hdd, Unidad = 'D' }, gtaHdd]);
            Assert.StartsWith("FiveM y GTA V están en un disco duro (HDD, unidad D:).", ambos);
        }

        [Fact]
        public void Revisar_SoloDevuelveLoQueExiste()
        {
            // En este PC puede haber FiveM, GTA V, los dos o ninguno: lo que salga tiene que existir de verdad
            foreach (UbicacionEnDisco u in DiscoJuego.Revisar())
            {
                Assert.True(Directory.Exists(u.Carpeta), u.Carpeta);
                Assert.Equal(DiscoJuego.UnidadDe(u.Carpeta), u.Unidad);
            }
        }
    }
}
