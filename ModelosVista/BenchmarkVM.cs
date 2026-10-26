using OptimizadorPC.Benchmark;
using OptimizadorPC.Servicios;   // InfoSistema.Es

namespace OptimizadorPC.ModelosVista
{
    // Un benchmark guardado, convertido en los textos que muestra la pantalla.
    // No usa [ObservableProperty]: un resultado guardado ya no cambia.
    public class BenchmarkVM
    {
        public ResultadoBenchmark Resultado { get; }

        public BenchmarkVM(ResultadoBenchmark resultado)
        {
            Resultado = resultado;
        }

        public string Juego => Resultado.Juego;
        public string Fecha => Resultado.Fecha.ToString("dd/MM/yyyy HH:mm");

        // "58 → 64 FPS"
        public string TextoPromedio => $"{Fps(Resultado.Antes.FpsPromedio)} → {Fps(Resultado.Despues.FpsPromedio)} FPS";
        public string Texto1Low => $"{Fps(Resultado.Antes.Fps1Low)} → {Fps(Resultado.Despues.Fps1Low)} FPS";

        // "+10,3 %" o "−2,1 %": si empeoró se muestra igual, sin maquillar
        public string TextoMejora => Porcentaje(Resultado.MejoraPromedio);
        public string TextoMejora1Low => Porcentaje(Resultado.Mejora1Low);
        public bool Mejoro => Resultado.MejoraPromedio >= 0;

        public string TextoOptimizaciones => string.Join(", ", Resultado.Optimizaciones);

        public static string Fps(double valor) => valor.ToString("F0", InfoSistema.Es);

        private static string Porcentaje(double valor) =>
            (valor >= 0 ? "+" : "−") + Math.Abs(valor).ToString("F1", InfoSistema.Es) + " %";
    }
}
