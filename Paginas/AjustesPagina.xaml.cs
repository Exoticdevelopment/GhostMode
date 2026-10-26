using System.Reflection;   // Leer los datos del propio programa (nombre, versión)
using System.Windows;
using System.Windows.Controls;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Paginas
{
    public partial class AjustesPagina : Page
    {
        public AjustesPagina()
        {
            InitializeComponent();

            // El "ensamblado" = el propio programa. Sus datos salen del .csproj al compilar,
            // así la versión que se ve aquí es siempre la del .exe, sin escribirla a mano
            Assembly app = Assembly.GetExecutingAssembly();

            TxtNombre.Text = app.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Ghost Mode";

            // InformationalVersion puede venir como "1.0.0+a1b2c3d" (el código del commit): nos quedamos con "1.0.0"
            string? version = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            TxtVersion.Text = version?.Split('+')[0] ?? app.GetName().Version?.ToString() ?? "desconocida";
        }

        // Los enlaces llevan la URL en su Tag
        private void Enlace_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url })
            {
                Enlaces.Abrir(url);
            }
        }
    }
}
