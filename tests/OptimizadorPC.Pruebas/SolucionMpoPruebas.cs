using Microsoft.Win32;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.Sistema;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    // MPO (Solución de problemas): se escribe y se revierte de verdad en HKLM, comprobando la clave Dwm entera.
    // Sin reiniciar, Windows no aplica el cambio: la prueba no afecta a la pantalla
    [Collection("Sistema")]
    public class SolucionMpoPruebas
    {
        [Fact]
        public void DesactivarYRevertirDejaLaClaveDwmExacta()
        {
            Dictionary<string, string> antes = FotoDwm();
            Assert.False(SolucionMpo.EstaDesactivado(), "MPO ya estaba desactivado en este PC: la prueba no demostraría nada");
            GestorEstado.Limpiar(SolucionMpo.Modulo);
            try
            {
                SolucionMpo.Desactivar();

                Assert.True(SolucionMpo.EstaDesactivado());
                Assert.Equal("DWord:5", FotoDwm()[SolucionMpo.Nombre]);
                Assert.True(GestorEstado.TieneOriginal(SolucionMpo.Modulo, SolucionMpo.Nombre));
                Assert.Null(GestorEstado.ObtenerOriginal(SolucionMpo.Modulo, SolucionMpo.Nombre));   // no existía

                SolucionMpo.Desactivar();   // otra vez: no pisa el original

                SolucionMpo.Revertir();

                Assert.False(SolucionMpo.EstaDesactivado());
                Assert.Equal(antes, FotoDwm());   // también: el valor creado se borró
                Assert.False(GestorEstado.TieneCambios(SolucionMpo.Modulo));
            }
            finally
            {
                if (!antes.ContainsKey(SolucionMpo.Nombre))
                {
                    using RegistryKey? k = Registry.LocalMachine.OpenSubKey(SolucionMpo.Ruta, writable: true);
                    k?.DeleteValue(SolucionMpo.Nombre, throwOnMissingValue: false);
                }
                GestorEstado.Limpiar(SolucionMpo.Modulo);
            }
        }

        [Fact]
        public void SiYaTeniaOtroValorLoDevuelve()
        {
            Dictionary<string, string> antes = FotoDwm();
            Assert.False(antes.ContainsKey(SolucionMpo.Nombre));
            GestorEstado.Limpiar(SolucionMpo.Modulo);
            using RegistryKey clave = Registry.LocalMachine.CreateSubKey(SolucionMpo.Ruta);
            try
            {
                clave.SetValue(SolucionMpo.Nombre, 1, RegistryValueKind.DWord);   // un valor previo cualquiera

                SolucionMpo.Desactivar();
                SolucionMpo.Revertir();

                Assert.Equal("DWord:1", FotoDwm()[SolucionMpo.Nombre]);
            }
            finally
            {
                clave.DeleteValue(SolucionMpo.Nombre, throwOnMissingValue: false);
                GestorEstado.Limpiar(SolucionMpo.Modulo);
                Assert.Equal(antes, FotoDwm());
            }
        }

        private static Dictionary<string, string> FotoDwm()
        {
            using RegistryKey? clave = Registry.LocalMachine.OpenSubKey(SolucionMpo.Ruta);
            return clave == null ? []
                : clave.GetValueNames().ToDictionary(n => n, n => $"{clave.GetValueKind(n)}:{Texto(clave.GetValue(n))}");
        }

        private static string Texto(object? v) => v switch
        {
            byte[] b => Convert.ToHexString(b),
            string[] l => string.Join("|", l),
            _ => v?.ToString() ?? ""
        };
    }
}
