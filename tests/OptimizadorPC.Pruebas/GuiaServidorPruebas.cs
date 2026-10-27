using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class GuiaServidorPruebas
    {
        // Tal cual aparecen en la documentación de Cfx.re (y en docs/Investigacion-FiveM.pdf): un comando mal
        // escrito en la guía haría que el usuario crea que no funciona
        [Fact]
        public void LosComandosSonLosOficiales()
        {
            Assert.Equal("cl_drawperf true", GuiaServidor.Drawperf);
            Assert.Equal("resmon true", GuiaServidor.Resmon);
            Assert.Equal("+set moo 31337", GuiaServidor.ModoDesarrollador);
        }

        [Fact]
        public void LaPantallaFiveMMuestraLosComandosDeLaGuia()
        {
            // El XAML los toma de GuiaServidor (x:Static): si alguien los escribe a mano, la prueba lo detecta
            string xaml = File.ReadAllText(Path.Combine(RaizRepositorio(), "Paginas", "FiveMPagina.xaml"));
            Assert.Contains("{x:Static fivem:GuiaServidor.Drawperf}", xaml);
            Assert.Contains("{x:Static fivem:GuiaServidor.Resmon}", xaml);
            Assert.Contains("{x:Static fivem:GuiaServidor.ModoDesarrollador}", xaml);
            Assert.DoesNotContain("\"cl_drawperf true\"", xaml);
        }

        // La raíz del repositorio a partir de ESTE archivo fuente (tests\OptimizadorPC.Pruebas\ → dos niveles arriba).
        // CallerFilePath la pone el compilador: vale aunque las pruebas se compilen en otra carpeta
        private static string RaizRepositorio([System.Runtime.CompilerServices.CallerFilePath] string archivo = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivo)!, "..", ".."));
    }
}
