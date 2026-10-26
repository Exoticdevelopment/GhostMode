using Microsoft.Win32;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.Sistema;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    // Todo se hace en una clave propia de la prueba: HKCU\Software\GhostModePrueba-xxxx
    [Collection("Sistema")]
    public class RegistroPruebas : IDisposable
    {
        private const string Modulo = "prueba-registro";
        private readonly string raiz = @"Software\GhostModePrueba-" + Guid.NewGuid().ToString("N")[..8];

        public RegistroPruebas() => GestorEstado.Limpiar(Modulo);

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(raiz, throwOnMissingSubKey: false);
            GestorEstado.Limpiar(Modulo);
        }

        public static TheoryData<RegistryValueKind, object, RegistryValueKind, string> TiposDistintos => new()
        {
            // tipo original, valor original, tipo que pone la app, valor que pone la app
            { RegistryValueKind.String, "1", RegistryValueKind.DWord, "0" },
            { RegistryValueKind.DWord, 1, RegistryValueKind.String, "0" },
            { RegistryValueKind.ExpandString, @"%USERPROFILE%\x", RegistryValueKind.String, "fijo" },
            { RegistryValueKind.QWord, 5_000_000_000L, RegistryValueKind.DWord, "0" },
            { RegistryValueKind.MultiString, new[] { "a", "b" }, RegistryValueKind.String, "x" },
            { RegistryValueKind.MultiString, Array.Empty<string>(), RegistryValueKind.String, "x" },
            { RegistryValueKind.Binary, new byte[] { 1, 2, 3 }, RegistryValueKind.DWord, "0" },
        };

        [Theory]
        [MemberData(nameof(TiposDistintos))]
        public void Revertir_RestauraElValorConSuTipoOriginal(RegistryValueKind tipoOriginal, object original,
                                                              RegistryValueKind tipoApp, string valorApp)
        {
            using (RegistryKey clave = Registry.CurrentUser.CreateSubKey(raiz))
            {
                clave.SetValue("Valor", original, tipoOriginal);
            }
            var ajuste = new AjusteRegistro(raiz, "Valor", valorApp, tipoApp);
            ValorRegistro antes = Ayudas.LeerCrudo(raiz, "Valor");

            Registro.Aplicar(Modulo, [ajuste]);
            Assert.Equal(tipoApp, Ayudas.LeerCrudo(raiz, "Valor").Tipo);

            Registro.Revertir(Modulo, [ajuste]);
            Assert.Equal(antes, Ayudas.LeerCrudo(raiz, "Valor"));
            Assert.False(GestorEstado.TieneCambios(Modulo));
        }

        [Fact]
        public void Revertir_FotoDeVersionAnteriorSinTipoUsaElTipoDeLaApp()
        {
            // Así guardaban la foto las versiones anteriores: solo el valor, sin "tipo:"
            var ajuste = new AjusteRegistro(raiz, "Valor", "9");
            Registro.Escribir(raiz, "Valor", "9", RegistryValueKind.DWord);
            GestorEstado.GuardarOriginal(Modulo, $@"{raiz}\Valor", "7");

            Registro.Revertir(Modulo, [ajuste]);

            Assert.Equal(new ValorRegistro(true, RegistryValueKind.DWord, "7"), Ayudas.LeerCrudo(raiz, "Valor"));
        }

        [Fact]
        public void Revertir_QuitaLasClavesQueCreoAlAplicar()
        {
            string ruta = $@"{raiz}\Sub\Hoja";
            var ajustes = new[] { new AjusteRegistro(ruta, "A", "1"), new AjusteRegistro($@"{raiz}\Sub", "B", "2") };

            Registro.Aplicar(Modulo, ajustes);
            Assert.Equal("1", Registro.Leer(ruta, "A"));

            Registro.Revertir(Modulo, ajustes);

            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(raiz);
            Assert.Null(clave);   // ni la hoja ni las de arriba que no existían
            Assert.False(GestorEstado.TieneCambios(Modulo));
        }

        [Fact]
        public void Revertir_NoQuitaUnaClaveCreadaSiOtroProgramaEscribioEnElla()
        {
            var ajuste = new AjusteRegistro(raiz, "Nuestro", "1");
            Registro.Aplicar(Modulo, [ajuste]);
            Registro.Escribir($@"{raiz}\DeOtro", "Ajeno", "5", RegistryValueKind.DWord);

            Registro.Revertir(Modulo, [ajuste]);

            Assert.Null(Registro.Leer(raiz, "Nuestro"));
            Assert.Equal("5", Registro.Leer($@"{raiz}\DeOtro", "Ajeno"));
        }

        [Fact]
        public void Revertir_NoQuitaClavesQueYaExistian()
        {
            using (Registry.CurrentUser.CreateSubKey(raiz)) { }
            var ajuste = new AjusteRegistro(raiz, "Valor", "1");

            Registro.Aplicar(Modulo, [ajuste]);
            Registro.Revertir(Modulo, [ajuste]);

            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(raiz);
            Assert.NotNull(clave);
            Assert.Equal(0, clave.ValueCount);
        }

        // ---------- PlanEnergia: si powercfg falla, no se finge un cambio ----------

        [Fact]
        public void PlanEnergia_SiPowercfgFallaAvisaYConservaLaFoto()
        {
            var plan = new PlanEnergia();
            string activo = Ayudas.PlanActivo();
            GestorEstado.Limpiar(plan.Id);
            // Una foto con un plan que no existe en este PC: powercfg /setactive lo rechaza
            GestorEstado.GuardarOriginal(plan.Id, "guid", "00000000-0000-0000-0000-0000000000aa");

            try
            {
                var error = Assert.Throws<InvalidOperationException>(plan.Revertir);
                Assert.Contains("powercfg", error.Message);
                Assert.Equal(activo, Ayudas.PlanActivo());     // nada cambió
                Assert.True(GestorEstado.TieneCambios(plan.Id));   // la foto sigue para reintentar
            }
            finally
            {
                GestorEstado.Limpiar(plan.Id);
                if (Ayudas.PlanActivo() != activo)
                {
                    Ayudas.Powercfg($"/setactive {activo}");
                }
            }
        }
    }
}
