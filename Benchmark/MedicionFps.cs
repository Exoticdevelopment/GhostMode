namespace OptimizadorPC.Benchmark
{
    // Una medición de 60 segundos sobre un juego. Todo sale del CSV de PresentMon, nada estimado.
    public record MedicionFps(
        double FpsPromedio,             // frames / segundos
        double Fps1Low,                 // FPS del 1 % de frames más lentos (percentil 99 del tiempo de frame)
        int Frames,                     // cuántos frames se capturaron
        double Segundos,                // cuánto duró de verdad la captura
        List<double> FpsPorSegundo);    // los FPS de cada segundo, para la gráfica

    // Un benchmark completo: antes y después de aplicar Ghost Mode
    public record ResultadoBenchmark(
        DateTime Fecha,
        string Juego,                   // nombre del proceso, ej: "FiveM_b3095_GTAProcess"
        MedicionFps Antes,
        MedicionFps Despues,
        List<string> Optimizaciones)    // nombres de las optimizaciones que se aplicaron entre medio
    {
        // Cuánto cambió, en %. Puede ser negativo: si empeoró, se muestra tal cual
        public double MejoraPromedio => Cambio(Antes.FpsPromedio, Despues.FpsPromedio);
        public double Mejora1Low => Cambio(Antes.Fps1Low, Despues.Fps1Low);

        private static double Cambio(double antes, double despues) =>
            antes > 0 ? (despues - antes) / antes * 100 : 0;
    }
}
