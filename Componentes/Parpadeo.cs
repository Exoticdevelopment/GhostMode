using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OptimizadorPC.Componentes
{
    // El cursor "_" de terminal: visible medio segundo, invisible medio segundo, para siempre.
    // Lo usan el prompt de cada página y el panel de log.
    public static class Parpadeo
    {
        // Un elemento suelto (TextBlock): se anima su Opacity
        public static void Iniciar(UIElement cursor) => Iniciar(cursor, UIElement.OpacityProperty);

        // Un trozo de texto dentro de una línea (Run): no tiene Opacity propia, se anima la de su pincel.
        // El pincel tiene que ser suyo (new SolidColorBrush), no uno compartido de Colores.xaml
        public static void Iniciar(Brush pincel) => Iniciar(pincel, Brush.OpacityProperty);

        // null = quitar la animación: cero consumo mientras no se ve
        public static void Detener(UIElement cursor) => cursor.BeginAnimation(UIElement.OpacityProperty, null);
        public static void Detener(Brush pincel) => pincel.BeginAnimation(Brush.OpacityProperty, null);

        // Fotogramas "Discrete" = salta de un valor a otro sin fundido, y DesiredFrameRate = 2
        // le dice a WPF que basta con redibujar 2 veces por segundo
        private static void Iniciar(IAnimatable objetivo, DependencyProperty propiedad)
        {
            var parpadeo = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(1),
                RepeatBehavior = RepeatBehavior.Forever
            };
            parpadeo.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            parpadeo.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
            Timeline.SetDesiredFrameRate(parpadeo, 2);
            objetivo.BeginAnimation(propiedad, parpadeo);
        }
    }
}
