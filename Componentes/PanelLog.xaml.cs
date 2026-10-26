using System.Collections.ObjectModel;   // ObservableCollection: lista que avisa a la pantalla al cambiar
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OptimizadorPC.Core;               // Logger, LineaLog

namespace OptimizadorPC.Componentes
{
    // Una línea tal como se dibuja: "[21:14:03] " "INFO  " "mensaje"
    public record LineaLogVM(string Hora, string Nivel, string Mensaje, Brush ColorNivel);

    // Panel "[ LOG ]": las últimas 8 líneas del Logger, en vivo, con un cursor que parpadea.
    // Solo escucha al Logger mientras está a la vista.
    public partial class PanelLog : UserControl
    {
        private const int MaxLineas = 8;
        private readonly ObservableCollection<LineaLogVM> lineas = new();

        public PanelLog()
        {
            InitializeComponent();
            ListaLineas.ItemsSource = lineas;

            Loaded += (s, e) =>
            {
                lineas.Clear();
                // TakeLast = las últimas N de la lista
                foreach (LineaLog linea in Logger.Recientes.TakeLast(MaxLineas))
                {
                    Agregar(linea);
                }
                ActualizarVacio();
                Logger.NuevaLinea += AlNuevaLinea;
                IniciarParpadeo();
            };
            Unloaded += (s, e) =>
            {
                Logger.NuevaLinea -= AlNuevaLinea;
                CursorLog.BeginAnimation(OpacityProperty, null);   // null = quitar la animación: cero consumo
            };
        }

        // El Logger puede avisar desde un hilo en segundo plano, y la pantalla solo se puede tocar
        // desde el hilo de la interfaz: BeginInvoke = "haz esto en el hilo de la interfaz cuando puedas"
        private void AlNuevaLinea(LineaLog linea)
        {
            Dispatcher.BeginInvoke(() =>
            {
                Agregar(linea);
                ActualizarVacio();
            });
        }

        private void Agregar(LineaLog linea)
        {
            // Cada nivel con su color: INFO gris, AVISO ámbar, ERROR rojo
            string color = linea.Nivel switch
            {
                "ERROR" => "ErrorBrush",
                "AVISO" => "AvisoBrush",
                _ => "TextoSecundarioBrush"
            };
            // PadRight(6) = rellenar con espacios hasta 6 letras: los mensajes quedan alineados
            lineas.Add(new LineaLogVM($"[{linea.Hora:HH:mm:ss}] ", linea.Nivel.PadRight(6),
                                      linea.Mensaje, (Brush)FindResource(color)));
            while (lineas.Count > MaxLineas)
            {
                lineas.RemoveAt(0);   // sale la más vieja
            }
        }

        private void ActualizarVacio() =>
            TxtVacio.Visibility = lineas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Cursor "_": visible medio segundo, invisible medio segundo, para siempre.
        // Fotogramas "Discrete" = salta de un valor a otro sin fundido, y DesiredFrameRate = 2
        // le dice a WPF que basta con redibujar 2 veces por segundo
        private void IniciarParpadeo()
        {
            var parpadeo = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(1),
                RepeatBehavior = RepeatBehavior.Forever
            };
            parpadeo.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            parpadeo.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
            Timeline.SetDesiredFrameRate(parpadeo, 2);
            CursorLog.BeginAnimation(OpacityProperty, parpadeo);
        }
    }
}
