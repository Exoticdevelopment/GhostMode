using System.Collections.ObjectModel;   // ObservableCollection: lista que avisa a la pantalla al cambiar
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                // TakeLast = las últimas N de la lista (de las que se muestran: el filtro va antes)
                foreach (LineaLog linea in Logger.Recientes.Where(SeMuestra).TakeLast(MaxLineas))
                {
                    Agregar(linea);
                }
                ActualizarVacio();
                Logger.NuevaLinea += AlNuevaLinea;
                Parpadeo.Iniciar(CursorLog);
            };
            Unloaded += (s, e) =>
            {
                Logger.NuevaLinea -= AlNuevaLinea;
                Parpadeo.Detener(CursorLog);
            };
        }

        // El Logger puede avisar desde un hilo en segundo plano, y la pantalla solo se puede tocar
        // desde el hilo de la interfaz: BeginInvoke = "haz esto en el hilo de la interfaz cuando puedas"
        private void AlNuevaLinea(LineaLog linea)
        {
            if (!SeMuestra(linea))
            {
                return;
            }
            Dispatcher.BeginInvoke(() =>
            {
                Agregar(linea);
                ActualizarVacio();
            });
        }

        // "Estado original guardado: ..." sale varias veces por cada optimización y taparía las de
        // "Aplicando/Aplicada". Aquí no se muestra; en log.txt sigue estando
        private static bool SeMuestra(LineaLog linea) =>
            !linea.Mensaje.StartsWith(GestorEstado.PrefijoLogOriginal, StringComparison.Ordinal);

        private void Agregar(LineaLog linea)
        {
            // Cada nivel con su color: INFO gris, OK verde, AVISO ámbar, ERROR rojo
            string color = linea.Nivel switch
            {
                "ERROR" => "ErrorBrush",
                "AVISO" => "AvisoBrush",
                "OK" => "ExitoBrush",
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
    }
}
