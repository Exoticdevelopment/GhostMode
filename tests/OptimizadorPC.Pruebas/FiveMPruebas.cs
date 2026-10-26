using Microsoft.Win32;
using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class FiveMPruebas
    {
        private const string RutaGpu = @"Software\Microsoft\DirectX\UserGpuPreferences";

        private static readonly string CarpetaFiveM =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM");

        [Fact]
        public void UbicacionFiveM_NoFallaSiFiveMNoEstaInstalado()
        {
            string? exe = UbicacionFiveM.BuscarExe();
            Assert.True(exe == null, $"Esta prueba es para un PC sin FiveM, pero se encontró {exe}");

            // Nada de esto debe lanzar excepciones
            _ = UbicacionFiveM.EstaEnEjecucion();
            var gpu = new GpuDedicadaFiveM();
            Assert.False(gpu.EstaActiva());

            // Aplicar sin FiveM avisa con un mensaje claro (la interfaz lo muestra) y no toca el registro
            var antes = Ayudas.LeerCrudo(RutaGpu, Path.Combine(CarpetaFiveM, "FiveM.exe"));
            var error = Assert.Throws<InvalidOperationException>(gpu.Aplicar);
            Assert.Contains("FiveM no está instalado", error.Message);
            Assert.Equal(antes, Ayudas.LeerCrudo(RutaGpu, Path.Combine(CarpetaFiveM, "FiveM.exe")));
            Assert.False(GestorEstado.TieneCambios(gpu.Id));
        }

        // FiveM no está instalado en este PC: armamos una instalación falsa (archivos vacíos que nunca
        // se ejecutan) en la ruta por defecto, solo si ahí no hay nada, y la quitamos al terminar
        [Fact]
        public void GpuDedicadaFiveM_AplicaYRevierteElRegistroExacto()
        {
            string exe = Path.Combine(CarpetaFiveM, "FiveM.exe");
            string app = Path.Combine(CarpetaFiveM, "FiveM.app");
            Assert.False(File.Exists(exe) || Directory.Exists(app), "Hay un FiveM real: esta prueba no lo toca");

            bool habiaCarpeta = Directory.Exists(CarpetaFiveM);
            bool habiaClaveGpu;
            using (RegistryKey? clave = Registry.CurrentUser.OpenSubKey(RutaGpu))
            {
                habiaClaveGpu = clave != null;
            }
            string juego = Path.Combine(app, "data", "cache", "subprocess", "FiveM_b3095_GTAProcess.exe");
            var gpu = new GpuDedicadaFiveM();
            GestorEstado.Limpiar(gpu.Id);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(juego)!);
                File.WriteAllBytes(exe, []);
                File.WriteAllBytes(juego, []);
                Assert.Equal(exe, UbicacionFiveM.BuscarExe());

                // El proceso del juego ya tenía otros ajustes de Windows: deben conservarse
                Registro.Escribir(RutaGpu, juego, "SwapEffectUpgradeEnable=1;GpuPreference=0;", RegistryValueKind.String);
                var antesExe = Ayudas.LeerCrudo(RutaGpu, exe);
                var antesJuego = Ayudas.LeerCrudo(RutaGpu, juego);
                Assert.False(gpu.EstaActiva());

                gpu.Aplicar();

                Assert.Equal(new ValorRegistro(true, RegistryValueKind.String, "GpuPreference=2;"), Ayudas.LeerCrudo(RutaGpu, exe));
                Assert.Equal(new ValorRegistro(true, RegistryValueKind.String, "SwapEffectUpgradeEnable=1;GpuPreference=2;"),
                             Ayudas.LeerCrudo(RutaGpu, juego));
                Assert.True(gpu.EstaActiva());

                gpu.Revertir();

                Assert.Equal(antesExe, Ayudas.LeerCrudo(RutaGpu, exe));
                Assert.Equal(antesJuego, Ayudas.LeerCrudo(RutaGpu, juego));
                Assert.False(gpu.EstaActiva());
                Assert.False(GestorEstado.TieneCambios(gpu.Id));
            }
            finally
            {
                GestorEstado.Limpiar(gpu.Id);
                using (RegistryKey? clave = Registry.CurrentUser.OpenSubKey(RutaGpu, writable: true))
                {
                    clave?.DeleteValue(exe, throwOnMissingValue: false);
                    clave?.DeleteValue(juego, throwOnMissingValue: false);
                }
                if (!habiaClaveGpu)
                {
                    Registry.CurrentUser.DeleteSubKey(RutaGpu, throwOnMissingSubKey: false);
                }
                File.Delete(exe);
                if (Directory.Exists(app))
                {
                    Directory.Delete(app, recursive: true);
                }
                if (!habiaCarpeta)
                {
                    Directory.Delete(CarpetaFiveM);
                }
            }
        }
    }
}
