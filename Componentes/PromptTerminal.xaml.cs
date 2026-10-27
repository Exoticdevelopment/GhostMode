using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OptimizadorPC.Componentes
{
    // Título de página tipo terminal: "exotic@ghost:~$ status_".
    // Uso: <componentes:PromptTerminal Comando="status"/>
    public partial class PromptTerminal : UserControl
    {
        // El usuario de Windows en minúsculas, como en una consola. Se lee una vez para toda la app
        public static readonly string Usuario = Environment.UserName.ToLowerInvariant();

        public static readonly DependencyProperty ComandoProperty = DependencyProperty.Register(
            nameof(Comando), typeof(string), typeof(PromptTerminal),
            new PropertyMetadata("", (d, e) => ((PromptTerminal)d).RunComando.Text = (string)e.NewValue));

        public string Comando
        {
            get => (string)GetValue(ComandoProperty);
            set => SetValue(ComandoProperty, value);
        }

        public PromptTerminal()
        {
            InitializeComponent();
            RunUsuario.Text = $"{Usuario}@ghost";

            // Pincel propio para el cursor: es el que parpadea (si fuera el morado compartido, parpadearía toda la app)
            var pincelCursor = new SolidColorBrush(((SolidColorBrush)FindResource("AcentoBrush")).Color);
            RunCursor.Foreground = pincelCursor;

            // Solo parpadea mientras la página está a la vista
            Loaded += (s, e) => Parpadeo.Iniciar(pincelCursor);
            Unloaded += (s, e) => Parpadeo.Detener(pincelCursor);
        }
    }
}
