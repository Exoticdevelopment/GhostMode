using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.FiveM;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    // El perfil de energía de NVIDIA contra el driver real (RTX 3050 de este PC).
    // Se usan .exe con nombres de prueba y, para el caso "ya tiene perfil", el de GTA V que trae NVIDIA de fábrica;
    // en los dos casos se comprueba que al revertir todo queda exactamente como estaba.
    [Collection("Sistema")]
    public class PerfilNvidiaPruebas
    {
        [Fact]
        public void SinPerfil_CreaElPropioYAlRevertirLoBorraEntero()
        {
            var nvidia = PerfilNvidia.Sistema;
            Assert.True(nvidia.Disponible, "NVAPI no está disponible: ¿este PC tiene GPU NVIDIA?");
            Assert.False(nvidia.ExistePerfilPropio(), "Ya hay un perfil «Ghost Mode» en el driver: la prueba no lo toca");
            string id = Guid.NewGuid().ToString("N")[..8];
            string[] exes = [$@"C:\Juegos\GhostModePrueba-{id}.exe", $"GhostModePrueba-{id}_GTAProcess.exe"];
            GestorEstado.Limpiar(PerfilNvidia.Modulo);
            try
            {
                Assert.All(exes, e => Assert.Null(nvidia.Leer(e)));

                Assert.True(nvidia.Aplicar(exes));

                Assert.True(nvidia.ExistePerfilPropio());
                Assert.All(exes, e => Assert.Equal((true, PerfilNvidia.PreferirMaximo), nvidia.Leer(e)));
                Assert.True(GestorEstado.TieneCambios(PerfilNvidia.Modulo));
                Assert.False(nvidia.Aplicar(exes));   // otra vez: ya está, no se toca nada

                nvidia.Revertir();

                Assert.False(nvidia.ExistePerfilPropio());
                Assert.All(exes, e => Assert.Null(nvidia.Leer(e)));
                Assert.False(GestorEstado.TieneCambios(PerfilNvidia.Modulo));
            }
            finally
            {
                nvidia.Revertir();
            }
        }

        [Fact]
        public void ConPerfilDeNvidia_SoloCambiaEseAjusteYLoDevuelve()
        {
            var nvidia = PerfilNvidia.Sistema;
            const string gta = "GTA5.exe";   // NVIDIA trae un perfil de fábrica para GTA V
            (bool Propio, uint Valor)? antes = nvidia.Leer(gta);
            Assert.NotNull(antes);
            Assert.False(nvidia.ExistePerfilPropio());
            GestorEstado.Limpiar(PerfilNvidia.Modulo);
            try
            {
                nvidia.Aplicar([gta]);

                Assert.Equal((true, PerfilNvidia.PreferirMaximo), nvidia.Leer(gta));
                Assert.False(nvidia.ExistePerfilPropio());   // se usó el perfil que ya tenía

                nvidia.Revertir();

                Assert.Equal(antes, nvidia.Leer(gta));   // mismo valor y mismo origen (de NVIDIA o del usuario)
                Assert.False(GestorEstado.TieneCambios(PerfilNvidia.Modulo));
            }
            finally
            {
                nvidia.Revertir();
            }
        }

        [Fact]
        public async Task Sesion_FiveMAplicaElPerfilAlAbrirYLoQuitaAlCerrar()
        {
            var nvidia = PerfilNvidia.Sistema;
            string carpeta = Ayudas.CarpetaTemporal();
            (bool, uint)? antesFiveM = nvidia.Leer("FiveM.exe");
            (bool, uint)? antesJuego = nvidia.Leer("FiveM_GTAProcess.exe");
            GestorEstado.Limpiar(PerfilNvidia.Modulo);

            // Solo esta opción: así la prueba no toca nada más de Windows
            var sesion = new Gaming.SesionGaming(Path.Combine(carpeta, "modo_gaming.json"));
            sesion.Config.PrioridadAlta = false;
            sesion.Config.SuspenderApps = false;
            sesion.Config.LiberarRam = false;
            sesion.Config.OptimizacionesInicio = false;
            sesion.Config.PausarGrabacion = false;
            sesion.Config.EnergiaNvidia = true;
            // Un FiveM "de mentira": nunca se ejecuta, solo da la ruta (EjecutablesJuego mira su carpeta)
            var fivem = new Gaming.Juego("FiveM", Path.Combine(carpeta, "FiveM.exe"), carpeta, Gaming.OrigenJuego.FiveM);
            try
            {
                await sesion.AlIniciarJuegoAsync(fivem, []);

                Assert.True(sesion.Activa);
                Assert.Equal((true, PerfilNvidia.PreferirMaximo), nvidia.Leer("FiveM.exe"));
                Assert.Equal((true, PerfilNvidia.PreferirMaximo), nvidia.Leer("FiveM_GTAProcess.exe"));

                await sesion.AlCerrarJuegoAsync(fivem);

                Assert.False(sesion.Activa);
                Assert.Equal(antesFiveM, nvidia.Leer("FiveM.exe"));
                Assert.Equal(antesJuego, nvidia.Leer("FiveM_GTAProcess.exe"));
                Assert.False(nvidia.ExistePerfilPropio());   // el perfil «Ghost Mode» se borró
                Assert.False(GestorEstado.TieneCambios(PerfilNvidia.Modulo));
            }
            finally
            {
                nvidia.Revertir();
                GestorEstado.Limpiar(Gaming.SesionGaming.Modulo);
                Directory.Delete(carpeta, recursive: true);
            }
        }

        [Fact]
        public void SinNvapi_NoFallaYNoHaceNada()
        {
            var falsa = new PerfilNvidia(@"C:\GhostModePrueba\no-existe\nvapi64.dll");
            Assert.False(falsa.Disponible);
            Assert.False(falsa.Aplicar(["FiveM.exe"]));
            Assert.Null(falsa.Leer("FiveM.exe"));

            var otraDll = new PerfilNvidia(Path.Combine(Environment.SystemDirectory, "kernel32.dll"));
            Assert.False(otraDll.Disponible);
        }

        [Fact]
        public void SoloSeCargaLaDelDriverEnSystem32()
        {
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"), PerfilNvidia.RutaOficial);
        }
    }
}
