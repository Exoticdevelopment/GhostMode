using System.IO;
using System.Text.Json;
using OptimizadorPC.Core;   // Logger

namespace OptimizadorPC.Benchmark
{
    // Los benchmarks hechos, guardados en C:\ProgramData\OptimizadorPC\benchmarks.json
    public static class HistorialBenchmarks
    {
        private static readonly string Archivo = Path.Combine(Logger.Carpeta, "benchmarks.json");

        // Se lee una vez al primer uso. Los más nuevos al final
        private static readonly List<ResultadoBenchmark> resultados = Cargar();

        // Avisa cuando se guarda uno nuevo (el Inicio actualiza su tarjeta de puntuación)
        public static event Action? Cambiado;

        public static IReadOnlyList<ResultadoBenchmark> Todos => resultados;

        // null = todavía no hay ninguno
        public static ResultadoBenchmark? Ultimo => resultados.LastOrDefault();

        public static void Agregar(ResultadoBenchmark resultado)
        {
            resultados.Add(resultado);
            try
            {
                Directory.CreateDirectory(Logger.Carpeta);
                var opciones = new JsonSerializerOptions { WriteIndented = true };   // JSON bonito, con saltos de línea
                File.WriteAllText(Archivo, JsonSerializer.Serialize(resultados, opciones));
                Logger.Info($"Benchmark guardado: {resultado.Juego}, {resultado.Antes.FpsPromedio:F1} → " +
                            $"{resultado.Despues.FpsPromedio:F1} FPS");
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo guardar benchmarks.json", ex);
            }
            Cambiado?.Invoke();
        }

        private static List<ResultadoBenchmark> Cargar()
        {
            try
            {
                if (File.Exists(Archivo))
                {
                    return JsonSerializer.Deserialize<List<ResultadoBenchmark>>(File.ReadAllText(Archivo)) ?? [];
                }
            }
            catch (Exception ex)
            {
                // Si el archivo está dañado, no tumbamos la app: lo anotamos y empezamos vacío
                Logger.Error("No se pudo leer benchmarks.json", ex);
            }
            return [];
        }
    }
}
