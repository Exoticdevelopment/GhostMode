using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OptimizadorPC.Paginas
{
    // Pantalla "Próximamente" que comparten todas las secciones aún vacías.
    // Se arma por código (sin XAML) porque es muy simple: un título y dos textos.
    public class PaginaProximamente : Page
    {
        public PaginaProximamente(string titulo, string cuando)
        {
            Title = titulo;

            var panel = new StackPanel { Margin = new Thickness(44, 36, 44, 36) };

            panel.Children.Add(new TextBlock
            {
                Text = titulo,
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                // TryFindResource busca un color de Colores.xaml por su nombre
                Foreground = (Brush)TryFindResource("TextoBrush")
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Próximamente · {cuando}",
                FontSize = 14,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)TryFindResource("TextoSecundarioBrush")
            });

            Content = panel;
        }
    }

    // Una clase por sección: la barra lateral distingue las páginas por su TIPO.
    // ": base(...)" = llamar al constructor de PaginaProximamente con estos textos.
    public class ModoGamingPagina : PaginaProximamente
    {
        public ModoGamingPagina() : base("Modo Gaming", "en construcción") { }
    }

    public class RedPagina : PaginaProximamente
    {
        public RedPagina() : base("Red", "en construcción") { }
    }

    public class MonitorPagina : PaginaProximamente
    {
        public MonitorPagina() : base("Monitor", "Fase 4") { }
    }

    public class AjustesPagina : PaginaProximamente
    {
        public AjustesPagina() : base("Ajustes", "en construcción") { }
    }
}
