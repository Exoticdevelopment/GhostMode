using OptimizadorPC.Core;
using OptimizadorPC.Optimizaciones.Sistema;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    [Collection("Sistema")]
    public class OptimizacionesSistemaPruebas
    {
        private const string AltoRendimiento = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        [Fact]
        public void PlanEnergia_AplicaAltoRendimientoYRevierteAlPlanOriginal()
        {
            var plan = new PlanEnergia();
            string original = Ayudas.PlanActivo();
            List<string> planesAntes = Ayudas.Planes();
            Assert.NotEqual(AltoRendimiento, original);   // si ya estaba, la prueba no demostraría nada
            GestorEstado.Limpiar(plan.Id);

            try
            {
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
                if (Ayudas.PlanActivo() != original)
                {
                    Ayudas.Powercfg($"/setactive {original}");
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
            Dictionary<string, ValorRegistro> antes = Ayudas.Foto(ajustes);
            GestorEstado.Limpiar(modulo.Id);

            try
            {
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
                Ayudas.RestaurarFoto(ajustes, antes);
                GestorEstado.Limpiar(modulo.Id);
            }
        }
    }
}
