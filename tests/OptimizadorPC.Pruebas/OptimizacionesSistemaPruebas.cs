using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.Sistema;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class OptimizacionesSistemaPruebas
    {
        private const string AltoRendimiento = Ayudas.AltoRendimiento;

        [Fact]
        public void PlanEnergia_AplicaAltoRendimientoYRevierteAlPlanOriginal()
        {
            var plan = new PlanEnergia();
            string planReal = Ayudas.PlanActivo();   // el del usuario: vuelve sí o sí en el finally
            GestorEstado.Limpiar(plan.Id);

            try
            {
                // Si el usuario ya tiene Alto rendimiento (p. ej. Ghost Mode activado), se parte de otro plan:
                // si no, aplicar no cambiaría nada y la prueba no demostraría nada
                string original = Ayudas.PartirSinAltoRendimiento();
                Assert.NotEqual(AltoRendimiento, original);
                // La lista, desde el punto de partida: en algunos equipos Alto rendimiento es un plan oculto
                // que powercfg /list solo enseña mientras está activo
                List<string> planesAntes = Ayudas.Planes();

                Assert.False(plan.EstaActiva());

                plan.Aplicar();
                Assert.Equal(AltoRendimiento, Ayudas.PlanActivo());
                Assert.True(plan.EstaActiva());
                Assert.Equal(original, GestorEstado.ObtenerOriginal(plan.Id, "guid"));

                // Aplicar otra vez no debe pisar el original con el plan ya optimizado
                plan.Aplicar();
                Assert.Equal(original, GestorEstado.ObtenerOriginal(plan.Id, "guid"));

                plan.Revertir();
                Assert.Equal(original, Ayudas.PlanActivo());
                Assert.False(plan.EstaActiva());
                Assert.False(GestorEstado.TieneCambios(plan.Id));
                Assert.Equal(planesAntes, Ayudas.Planes());   // no se creó ni se borró ningún plan
            }
            finally
            {
                if (Ayudas.PlanActivo() != planReal)
                {
                    Ayudas.Powercfg($"/setactive {planReal}");
                }
                GestorEstado.Limpiar(plan.Id);
            }
        }

        [Fact]
        public void GameMode_AplicaYRevierteElRegistroExacto() => ProbarRegistro(new GameMode(), Ayudas.AjustesDe<GameMode>());

        [Fact]
        public void EfectosVisuales_AplicaYRevierteElRegistroExacto() =>
            ProbarRegistro(new EfectosVisuales(), Ayudas.AjustesDe<EfectosVisuales>());

        // Foto del registro → Aplicar → cada valor es el de la app → Revertir → todo igual que la foto
        private static void ProbarRegistro(IOptimizacion modulo, AjusteRegistro[] ajustes)
        {
            Dictionary<string, ValorRegistro> real = Ayudas.Foto(ajustes);   // la del usuario: vuelve en el finally
            GestorEstado.Limpiar(modulo.Id);

            try
            {
                // Con la optimización ya puesta (Ghost Mode activado), se parte de "sin aplicar"
                Ayudas.PartirSinAplicar(modulo, ajustes);
                Dictionary<string, ValorRegistro> antes = Ayudas.Foto(ajustes);

                modulo.Aplicar();

                foreach (var a in ajustes)
                {
                    ValorRegistro ahora = Ayudas.LeerCrudo(a.Ruta, a.Nombre);
                    Assert.True(ahora.Existe, $@"{a.Ruta}\{a.Nombre} no se escribió");
                    Assert.Equal(a.Tipo, ahora.Tipo);
                    Assert.Equal(a.Valor, ahora.Texto);
                }
                Assert.True(modulo.EstaActiva());
                Assert.True(GestorEstado.TieneCambios(modulo.Id));

                modulo.Revertir();

                Assert.Equal(antes, Ayudas.Foto(ajustes));
                Assert.False(GestorEstado.TieneCambios(modulo.Id));
            }
            finally
            {
                Ayudas.RestaurarFoto(ajustes, real);
                GestorEstado.Limpiar(modulo.Id);
            }
        }
    }
}
