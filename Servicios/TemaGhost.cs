using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OptimizadorPC.Servicios
{
    // El aspecto de "Ghost Mode encendido": el banner y los bordes de todos los paneles pasan a morado
    // y la barra de título dice "ghost_mode: ACTIVE". Apagado, todo vuelve al gris.
    //
    // Cómo: dos pinceles COMPARTIDOS (MarcoBrush, BannerBrush) que los estilos usan con {DynamicResource}.
    // Se cambia el color de ese único pincel y todo lo que lo usa se repinta: no se recorre ni se reconstruye nada.
    // El cambio es una transición de color corta: sin desenfoques ni efectos (PCs de pocos recursos)
    public static class TemaGhost
    {
        private static readonly TimeSpan Duracion = TimeSpan.FromMilliseconds(350);

        private static readonly SolidColorBrush marco = new();
        private static readonly SolidColorBrush banner = new();
        private static Color acento, bordeApagado, bannerApagado;

        public static bool Activo { get; private set; }

        // Para lo que no es un color (el texto de la barra de título)
        public static event Action? Cambiado;

        // Al iniciar la app: pone los pinceles en sus recursos. Al nivel de la app ganan a los de Colores.xaml
        // (que quedan como valor por defecto si algo se dibuja sin la app, p. ej. en las pruebas)
        public static void Registrar(Application app)
        {
            acento = (Color)app.FindResource("AcentoColor");
            bordeApagado = (Color)app.FindResource("BordeColor");
            bannerApagado = (Color)app.FindResource("BannerApagadoColor");
            marco.Color = bordeApagado;
            banner.Color = bannerApagado;
            // OJO: WPF congela (Freeze) los pinceles que entran en los recursos de la app, y uno congelado ya no
            // se puede animar. Uno con una animación puesta no se puede congelar: por eso se anima (al mismo color)
            // ANTES de guardarlo
            Animar(marco, bordeApagado);
            Animar(banner, bannerApagado);
            app.Resources["MarcoBrush"] = marco;
            app.Resources["BannerBrush"] = banner;
        }

        public static void Aplicar(bool activo)
        {
            if (activo == Activo)
            {
                return;
            }
            Activo = activo;
            Animar(marco, activo ? acento : bordeApagado);
            Animar(banner, activo ? acento : bannerApagado);
            Cambiado?.Invoke();
        }

        // DesiredFrameRate = 30: para un fundido de color de 0,35 s sobra, y la mitad de redibujos que a 60
        private static void Animar(SolidColorBrush pincel, Color hacia)
        {
            var transicion = new ColorAnimation(hacia, Duracion);
            Timeline.SetDesiredFrameRate(transicion, 30);
            pincel.BeginAnimation(SolidColorBrush.ColorProperty, transicion);
        }
    }
}
