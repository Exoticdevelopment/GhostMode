using System.Windows;
using System.Windows.Controls.Primitives;   // ToggleButton

namespace OptimizadorPC.Componentes
{
    // Lo que el interruptor muestra además de encendido/apagado
    public enum EstadoInterruptor
    {
        Normal,      // Activo o Inactivo, según IsChecked
        Aplicando,   // ruedita naranja: Windows está haciendo el cambio
        Error        // X roja: el último cambio falló
    }

    // Interruptor de cuatro estados: Activo, Inactivo, Aplicando y Error.
    // Hereda de ToggleButton (un botón que se queda pulsado o no), así ya trae gratis
    // IsChecked, el clic, la tecla Espacio y la accesibilidad. Solo le sumamos "Estado".
    // Su dibujo está en Estilos/Controles.xaml.
    public class InterruptorEstado : ToggleButton
    {
        // DependencyProperty = propiedad "especial" de WPF: se puede enlazar con {Binding}
        // y los Trigger del estilo reaccionan cuando cambia.
        public static readonly DependencyProperty EstadoProperty = DependencyProperty.Register(
            nameof(Estado),                     // nombre de la propiedad
            typeof(EstadoInterruptor),          // su tipo
            typeof(InterruptorEstado),          // a qué clase pertenece
            new PropertyMetadata(EstadoInterruptor.Normal));   // valor inicial

        // La propiedad "normal" que usa el código: solo lee y escribe la de arriba
        public EstadoInterruptor Estado
        {
            get => (EstadoInterruptor)GetValue(EstadoProperty);
            set => SetValue(EstadoProperty, value);
        }

        // Se llama en cada clic. Mientras se aplica un cambio, el interruptor no se mueve
        protected override void OnToggle()
        {
            if (Estado == EstadoInterruptor.Aplicando)
            {
                return;
            }
            base.OnToggle();
        }
    }
}
