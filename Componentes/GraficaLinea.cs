using System.Windows;
using System.Windows.Media;

namespace OptimizadorPC.Componentes
{
    // Gráfica de línea muy liviana: se dibuja a sí misma con OnRender (sin controles por cada punto,
    // sin animaciones). Solo se redibuja cuando llega un dato nuevo.
    //
    // Se usa así:
    //   <componentes:GraficaLinea x:Name="GrafCpu" Height="90" Maximo="100"/>
    //   GrafCpu.Agregar(24);                       // monitor en vivo: un punto más, el más viejo sale
    //   GrafFps.MostrarSeries(despues, antes);     // benchmark: dos series completas
    public class GraficaLinea : FrameworkElement
    {
        // Lo que se dibuja. double.NaN = "no hay dato en ese punto" (se deja un hueco, no se inventa)
        private readonly List<double> serie = new();
        private List<double>? serieSecundaria;

        // Cuántos puntos caben a lo ancho (60 = los últimos 60 segundos)
        public int Capacidad { get; set; } = 60;

        // El valor que toca el techo de la gráfica. 0 = se calcula solo con el mayor valor
        public double Maximo { get; set; } = 100;

        // Colores: verde para la serie principal, gris para la secundaria ("antes" en el benchmark)
        public Brush Trazo { get; set; } = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        public Brush TrazoSecundario { get; set; } = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
        public Brush Lineas { get; set; } = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x37));

        // Monitor en vivo: agrega un punto al final y, si no caben, quita el más viejo
        public void Agregar(double? valor)
        {
            serie.Add(valor ?? double.NaN);
            if (serie.Count > Capacidad)
            {
                serie.RemoveAt(0);
            }
            InvalidateVisual();   // "vuelve a dibujarte" (WPF llama a OnRender)
        }

        public void Vaciar()
        {
            serie.Clear();
            serieSecundaria = null;
            InvalidateVisual();
        }

        // Benchmark: dos series completas a la vez. La gráfica se estira para que quepan todas
        public void MostrarSeries(IReadOnlyList<double> principal, IReadOnlyList<double>? secundaria)
        {
            serie.Clear();
            serie.AddRange(principal);
            serieSecundaria = secundaria?.ToList();
            Capacidad = Math.Max(2, Math.Max(principal.Count, secundaria?.Count ?? 0));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double ancho = ActualWidth;
            double alto = ActualHeight;
            if (ancho <= 0 || alto <= 0)
            {
                return;
            }

            // Un rectángulo transparente para que la gráfica entera reciba el ratón (ToolTip)
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ancho, alto));

            // Líneas guía horizontales: arriba, en medio y abajo
            var guia = new Pen(Lineas, 1);
            guia.Freeze();   // Freeze = "ya no cambiará": WPF lo dibuja más rápido
            foreach (double y in new[] { 0.5, alto / 2, alto - 0.5 })
            {
                dc.DrawLine(guia, new Point(0, y), new Point(ancho, y));
            }

            double maximo = Maximo > 0 ? Maximo : MayorValor();
            if (maximo <= 0)
            {
                return;
            }

            if (serieSecundaria != null)
            {
                DibujarSerie(dc, serieSecundaria, TrazoSecundario, ancho, alto, maximo, conRelleno: false);
            }
            DibujarSerie(dc, serie, Trazo, ancho, alto, maximo, conRelleno: true);
        }

        private double MayorValor()
        {
            IEnumerable<double> todos = serieSecundaria != null ? serie.Concat(serieSecundaria) : serie;
            double mayor = todos.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
            return mayor * 1.1;   // 10 % de aire arriba para que la línea no toque el borde
        }

        private void DibujarSerie(DrawingContext dc, List<double> valores, Brush color,
                                  double ancho, double alto, double maximo, bool conRelleno)
        {
            if (valores.Count == 0)
            {
                return;
            }

            // Los puntos van pegados a la derecha: el último dato siempre es "Ahora"
            double paso = ancho / (Capacidad - 1);
            double inicio = ancho - paso * (valores.Count - 1);

            // Cada tramo sin huecos es una figura aparte
            var tramos = new List<List<Point>>();
            List<Point>? actual = null;
            for (int i = 0; i < valores.Count; i++)
            {
                if (double.IsNaN(valores[i]))
                {
                    actual = null;   // hueco: el siguiente punto empieza otro tramo
                    continue;
                }
                double y = alto - Math.Clamp(valores[i] / maximo, 0, 1) * alto;
                if (actual == null)
                {
                    actual = new List<Point>();
                    tramos.Add(actual);
                }
                actual.Add(new Point(inicio + paso * i, y));
            }

            var lapiz = new Pen(color, 1.5) { LineJoin = PenLineJoin.Round };
            lapiz.Freeze();

            Brush? relleno = null;
            if (conRelleno && color is SolidColorBrush solido)
            {
                // El mismo color, casi transparente (alfa 0x22 de 0xFF)
                relleno = new SolidColorBrush(Color.FromArgb(0x22, solido.Color.R, solido.Color.G, solido.Color.B));
                relleno.Freeze();
            }

            foreach (List<Point> puntos in tramos)
            {
                // StreamGeometry = la forma más barata de describir una figura en WPF
                if (relleno != null && puntos.Count > 1)
                {
                    var area = new StreamGeometry();
                    using (StreamGeometryContext ctx = area.Open())
                    {
                        ctx.BeginFigure(new Point(puntos[0].X, alto), isFilled: true, isClosed: true);
                        ctx.PolyLineTo(puntos, isStroked: false, isSmoothJoin: false);
                        ctx.LineTo(new Point(puntos[^1].X, alto), isStroked: false, isSmoothJoin: false);   // ^1 = el último
                    }
                    area.Freeze();
                    dc.DrawGeometry(relleno, null, area);
                }

                var linea = new StreamGeometry();
                using (StreamGeometryContext ctx = linea.Open())
                {
                    ctx.BeginFigure(puntos[0], isFilled: false, isClosed: false);
                    ctx.PolyLineTo(puntos.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
                }
                linea.Freeze();
                dc.DrawGeometry(null, lapiz, linea);
            }
        }
    }
}
