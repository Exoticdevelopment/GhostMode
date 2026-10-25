using Microsoft.Win32;      // RegistryValueKind (el tipo de cada valor del registro)
using OptimizadorPC.Core;   // Logger, IOptimizacion, Registro, AjusteRegistro

namespace OptimizadorPC.Optimizaciones.Sistema
{
    public class EfectosVisuales : IOptimizacion
    {
        public string Id => "efectos-visuales";
        public string Nombre => "Menos animaciones y transparencias";
        public string Descripcion => "Quita animaciones, sombras y transparencias de Windows. Algunos cambios se notan al cerrar sesión.";
        public Categoria Categoria => Categoria.Graficos;
        public Impacto Impacto => Impacto.Medio;
        public Riesgo Riesgo => Riesgo.Seguro;

        private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        private static readonly AjusteRegistro[] Ajustes =
        {
            // 3 = "Personalizado" en la ventana de Efectos visuales de Windows
            // (no "Mejor rendimiento", porque dejamos las fuentes suaves y las miniaturas)
            new(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", "3"),

            // Transparencia de la barra de tareas y el menú Inicio
            new(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", "0"),

            // Animaciones de la barra de tareas y sombras de los iconos del escritorio
            new(Advanced, "TaskbarAnimations", "0"),
            new(Advanced, "ListviewAlphaSelect", "0"),
            new(Advanced, "ListviewShadow", "0"),

            // Animación al minimizar/maximizar. Ojo: Windows lo guarda como TEXTO, no número
            new(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", RegistryValueKind.String),

            // Interruptores empaquetados en bytes (menús con fundido, desplazamiento suave...).
            // Es el valor que pone Windows con "Ajustar para obtener el mejor rendimiento"
            new(@"Control Panel\Desktop", "UserPreferencesMask", "9012038010000000", RegistryValueKind.Binary),
        };

        public bool EstaActiva() => Registro.EstanAplicados(Ajustes);

        public void Aplicar()
        {
            Registro.Aplicar(Id, Ajustes);
            Logger.Info("Efectos visuales reducidos");
        }

        public void Revertir()
        {
            Registro.Revertir(Id, Ajustes);
            Logger.Info("Efectos visuales restaurados a como estaban");
        }
    }
}
