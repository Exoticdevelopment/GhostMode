using System.Text.Json;
using OptimizadorPC.Core;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class GestorEstadoPruebas
    {
        private const string Modulo = "prueba-gestor-estado";

        [Fact]
        public void GuardaElOriginalSoloLaPrimeraVez()
        {
            GestorEstado.Limpiar(Modulo);
            try
            {
                GestorEstado.GuardarOriginal(Modulo, "clave", "original");
                GestorEstado.GuardarOriginal(Modulo, "clave", "ya-optimizado");

                Assert.Equal("original", GestorEstado.ObtenerOriginal(Modulo, "clave"));
                Assert.True(GestorEstado.TieneCambios(Modulo));
            }
            finally
            {
                GestorEstado.Limpiar(Modulo);
            }
        }

        [Fact]
        public void SobreviveARecargarElArchivo()
        {
            GestorEstado.Limpiar(Modulo);
            try
            {
                GestorEstado.GuardarOriginal(Modulo, "valor", "123");
                GestorEstado.GuardarOriginal(Modulo, "no-existia", null);   // null = "el valor no existía"

                // Está escrito en disco...
                var enDisco = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(
                    File.ReadAllText(EstadoSistemaFixture.ArchivoEstado))!;
                Assert.Equal("123", enDisco[Modulo]["valor"]);

                // ...y al "abrir la app de nuevo" se recupera igual, incluido el null
                Ayudas.RecargarGestorEstado();
                Assert.Equal("123", GestorEstado.ObtenerOriginal(Modulo, "valor"));
                Assert.True(GestorEstado.TieneOriginal(Modulo, "no-existia"));
                Assert.Null(GestorEstado.ObtenerOriginal(Modulo, "no-existia"));

                // Tras recargar, sigue sin pisar el original
                GestorEstado.GuardarOriginal(Modulo, "valor", "999");
                Assert.Equal("123", GestorEstado.ObtenerOriginal(Modulo, "valor"));
            }
            finally
            {
                GestorEstado.Limpiar(Modulo);
            }
        }

        [Fact]
        public void LimpiarYOlvidarQuitanLoGuardado()
        {
            GestorEstado.Limpiar(Modulo);
            GestorEstado.GuardarOriginal(Modulo, "a", "1");
            GestorEstado.GuardarOriginal(Modulo, "b", "2");

            GestorEstado.Olvidar(Modulo, "a");
            Assert.False(GestorEstado.TieneOriginal(Modulo, "a"));
            Assert.Equal(["b"], GestorEstado.ClavesGuardadas(Modulo));

            // Olvidar la última clave quita el módulo entero
            GestorEstado.Olvidar(Modulo, "b");
            Assert.False(GestorEstado.TieneCambios(Modulo));

            GestorEstado.GuardarOriginal(Modulo, "c", "3");
            GestorEstado.Limpiar(Modulo);
            Assert.False(GestorEstado.TieneCambios(Modulo));

            // Y la limpieza también quedó en disco
            Ayudas.RecargarGestorEstado();
            Assert.False(GestorEstado.TieneCambios(Modulo));
        }
    }
}
