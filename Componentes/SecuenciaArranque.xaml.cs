using System.Diagnostics;   // Stopwatch
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Componentes
{
    // Escribe las líneas de ComprobacionesArranque como una terminal: letra a letra, en orden,
    // y cada una SOLO cuando su comprobación terminó de verdad (si aún no, el cursor espera).
    // Al acabar (o al saltarla) avisa con Terminada y quien la puso la quita.
    public partial class SecuenciaArranque : UserControl
    {
        // Por tiempo y no por pasos del reloj: si la interfaz va cargada en un PC lento, se pone al día en vez de alargarse.
        // ~110 letras en total → ~0,6 s escribiendo + la pausa final ≈ 1 segundo
        private const double LetrasPorSegundo = 190;
        private static readonly TimeSpan Pausa = TimeSpan.FromMilliseconds(300);   // la última línea se lee antes de cerrar
        private static readonly TimeSpan Maximo = TimeSpan.FromSeconds(3);         // si algo tarda más, no se hace esperar a nadie

        public event Action? Terminada;

        private readonly DispatcherTimer reloj = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly Stopwatch cronometro = new();
        private IReadOnlyList<Task<LineaArranque>> comprobaciones = [];
        private int indice = 0;              // la línea que toca
        private Run? runMarca, runTexto;     // la línea que se está escribiendo (null = ninguna)
        private string marca = "", texto = "";
        private int escritas = 0;
        private TimeSpan inicioLinea;
        private TimeSpan? finEscritura;
        private bool terminada = false;

        public SecuenciaArranque()
        {
            InitializeComponent();
            TxtOrden.Text = $"{PromptTerminal.Usuario}@ghost:~$ ghost --boot";
            MouseDown += (s, e) => Terminar();
            reloj.Tick += (s, e) => Paso();
        }

        public void Empezar()
        {
            comprobaciones = ComprobacionesArranque.Iniciar();
            Parpadeo.Iniciar(CursorArranque);
            cronometro.Start();
            reloj.Start();
        }

        // Para la ventana: cualquier tecla la salta (y no llega a la página de debajo)
        public void Saltar(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            Terminar();
        }

        private void Paso()
        {
            if (cronometro.Elapsed > Maximo)
            {
                Terminar();
                return;
            }

            if (runMarca == null && !EmpezarSiguienteLinea())
            {
                return;
            }

            // (No se mira la opción de animaciones de Windows: la optimización "Efectos visuales" de Ghost Mode
            // la apaga, y la escritura no se vería nunca. Para no verla está el interruptor de Ajustes)
            escritas = (int)((cronometro.Elapsed - inicioLinea).TotalSeconds * LetrasPorSegundo) + 1;
            int enMarca = Math.Min(escritas, marca.Length);
            runMarca!.Text = marca[..enMarca];
            runTexto!.Text = texto[..Math.Clamp(escritas - marca.Length, 0, texto.Length)];
            if (escritas >= marca.Length + texto.Length)
            {
                runMarca = runTexto = null;   // línea completa: la siguiente en el próximo paso
                indice++;
            }
        }

        // Devuelve false si no hay línea que escribir ahora (esperando un resultado, o ya no quedan)
        private bool EmpezarSiguienteLinea()
        {
            if (indice >= comprobaciones.Count)
            {
                finEscritura ??= cronometro.Elapsed;
                if (cronometro.Elapsed - finEscritura >= Pausa)
                {
                    Terminar();
                }
                return false;
            }
            Task<LineaArranque> tarea = comprobaciones[indice];
            if (!tarea.IsCompleted)
            {
                return false;   // todavía cargando de verdad: no se escribe nada inventado
            }

            LineaArranque linea = tarea.Result;   // Comprobar() nunca lanza: siempre hay resultado
            marca = linea.Marca;
            texto = linea.Texto;
            escritas = 0;
            inicioLinea = cronometro.Elapsed;
            runMarca = new Run { Foreground = ColorMarca(linea) };
            runTexto = new Run { Foreground = ColorTexto(linea) };
            var bloque = new TextBlock { FontFamily = (FontFamily)FindResource("FuenteMono"), FontSize = (double)FindResource("TamanoTexto"), Margin = new Thickness(0, 1, 0, 1) };
            bloque.Inlines.Add(runMarca);
            bloque.Inlines.Add(runTexto);
            Lineas.Children.Add(bloque);
            return true;
        }

        private Brush ColorMarca(LineaArranque linea) => (Brush)FindResource(linea.Resultado switch
        {
            ResultadoComprobacion.Ok => "ExitoBrush",
            ResultadoComprobacion.Fallo => "ErrorBrush",
            _ => "TextoSecundarioBrush"
        });

        // La línea final (estado de Ghost Mode) en morado si está encendido: el color de Ghost Mode
        private Brush ColorTexto(LineaArranque linea) => (Brush)FindResource(
            linea.Resultado == ResultadoComprobacion.Estado && linea.Texto.EndsWith(": on") ? "AcentoBrilloBrush" : "TextoBrush");

        private void Terminar()
        {
            if (terminada)
            {
                return;
            }
            terminada = true;
            reloj.Stop();
            Parpadeo.Detener(CursorArranque);
            Terminada?.Invoke();
        }
    }
}
