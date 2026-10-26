using System.Globalization;   // CultureInfo.InvariantCulture: el CSV usa punto decimal
using System.IO;

namespace OptimizadorPC.Benchmark
{
    // Convierte el CSV de PresentMon en una MedicionFps.
    // PresentMon escribe una fila por frame; la columna "MsBetweenPresents" es el tiempo (en ms)
    // entre ese frame y el anterior. Con esa lista sale todo lo demás.
    public static class AnalizadorFrames
    {
        // Con menos frames no hay datos suficientes para un resultado honesto
        private const int FramesMinimos = 30;

        public static MedicionFps Analizar(string archivoCsv)
        {
            string[] lineas = File.ReadAllLines(archivoCsv);
            if (lineas.Length < 2)
            {
                throw new InvalidOperationException(
                    "PresentMon no capturó ningún frame. ¿El juego estaba abierto, en primer plano y dibujando?");
            }

            // La primera línea son los nombres de las columnas: buscamos las que necesitamos
            string[] cabecera = lineas[0].Split(',');
            int colTiempo = Array.IndexOf(cabecera, "MsBetweenPresents");
            int colSwapChain = Array.IndexOf(cabecera, "SwapChainAddress");
            if (colTiempo < 0)
            {
                throw new InvalidOperationException("El CSV de PresentMon no tiene la columna MsBetweenPresents");
            }

            // Un juego puede dibujar en varias "swap chains" (p. ej. el juego y un overlay).
            // Agrupamos los frames por swap chain y nos quedamos con la que más tiene: la del juego
            var porSwapChain = new Dictionary<string, List<double>>();
            foreach (string linea in lineas.Skip(1))
            {
                string[] campos = linea.Split(',');
                if (campos.Length <= colTiempo)
                {
                    continue;
                }
                // TryParse: si el valor es "NA" u otra cosa, se salta la fila sin fallar
                if (!double.TryParse(campos[colTiempo], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) || ms <= 0)
                {
                    continue;
                }
                string swapChain = colSwapChain >= 0 && campos.Length > colSwapChain ? campos[colSwapChain] : "";
                if (!porSwapChain.TryGetValue(swapChain, out var lista))
                {
                    lista = new List<double>();
                    porSwapChain[swapChain] = lista;
                }
                lista.Add(ms);
            }

            List<double> tiempos = porSwapChain.Values.OrderByDescending(l => l.Count).FirstOrDefault() ?? [];
            if (tiempos.Count < FramesMinimos)
            {
                throw new InvalidOperationException(
                    $"Solo se capturaron {tiempos.Count} frames. ¿El juego estaba en primer plano y no en pausa?");
            }

            return Calcular(tiempos);
        }

        private static MedicionFps Calcular(List<double> tiempos)
        {
            double totalMs = tiempos.Sum();
            double segundos = totalMs / 1000;
            double promedio = tiempos.Count / segundos;

            // 1% low: ordenamos los tiempos de menor a mayor y tomamos el del percentil 99.
            // Ese frame es más lento que el 99 % de los demás: sus FPS son el "1% low"
            List<double> ordenados = tiempos.Order().ToList();
            int indice = (int)Math.Ceiling(ordenados.Count * 0.99) - 1;
            double unoPorCientoLow = 1000 / ordenados[Math.Clamp(indice, 0, ordenados.Count - 1)];

            // FPS de cada segundo: vamos sumando tiempos y contamos cuántos frames caen en cada segundo
            var porSegundo = new List<double>();
            double acumulado = 0;
            int framesEnEsteSegundo = 0;
            foreach (double ms in tiempos)
            {
                acumulado += ms;
                framesEnEsteSegundo++;
                while (acumulado >= 1000)
                {
                    porSegundo.Add(framesEnEsteSegundo);
                    framesEnEsteSegundo = 0;
                    acumulado -= 1000;
                }
            }

            return new MedicionFps(promedio, unoPorCientoLow, tiempos.Count, segundos, porSegundo);
        }
    }
}
