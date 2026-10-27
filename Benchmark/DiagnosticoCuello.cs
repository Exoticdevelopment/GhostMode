namespace OptimizadorPC.Benchmark
{
    // Qué limita los FPS (docs/Investigacion-FiveM.pdf, "El modelo" y paso 1 del plan)
    public enum CuelloBotella
    {
        CpuOServidor,   // al bajar la resolución los FPS casi no cambian
        Gpu             // al bajar la resolución los FPS suben
    }

    // Una recomendación del resultado. Sirve = false: lo que en este caso NO da FPS (también se dice)
    public record RecomendacionCuello(string Texto, bool Sirve, bool Pendiente = false);

    // Las dos mediciones del diagnóstico y lo que se deduce de ellas. Todos los números salen de PresentMon
    public record ResultadoCuello(MedicionFps Normal, MedicionFps ResolucionMinima)
    {
        // Cuánto cambiaron los FPS promedio al bajar la resolución, en %. Puede ser negativo (ruido)
        public double CambioPorcentaje => (ResolucionMinima.FpsPromedio - Normal.FpsPromedio) / Normal.FpsPromedio * 100;

        public CuelloBotella Cuello =>
            CambioPorcentaje < DiagnosticoCuello.UmbralPorcentaje ? CuelloBotella.CpuOServidor : CuelloBotella.Gpu;
    }

    // Diagnóstico de cuello de botella: 30 s con el juego tal como está y 30 s con la resolución al mínimo.
    // La resolución casi solo cambia el trabajo de la GPU (píxeles que dibujar), así que:
    //   - si los FPS casi no cambian (menos del 10 %), la GPU no era la que frenaba: es la CPU o el servidor
    //   - si suben, la GPU era el límite
    public static class DiagnosticoCuello
    {
        public const int SegundosPorMedicion = 30;
        public const double UmbralPorcentaje = 10;

        // Lanza ArgumentException si la primera medición no tiene FPS (no se puede comparar contra cero)
        public static ResultadoCuello Evaluar(MedicionFps normal, MedicionFps resolucionMinima)
        {
            if (normal.FpsPromedio <= 0 || resolucionMinima.FpsPromedio <= 0)
            {
                throw new ArgumentException("Una de las mediciones no tiene FPS: no hay nada que comparar");
            }
            return new ResultadoCuello(normal, resolucionMinima);
        }

        // Mide el proceso con PresentMon (descargándolo antes si hace falta)
        public static async Task<MedicionFps> MedirAsync(int procesoId, Action? descargando, CancellationToken cancelar)
        {
            if (!PresentMon.EstaDescargado)
            {
                descargando?.Invoke();
                await PresentMon.DescargarAsync(cancelar);
            }
            return await PresentMon.MedirAsync(procesoId, SegundosPorMedicion, cancelar);
        }

        // Por qué sale lo que sale, con las palabras de la investigación. cambio = "+4 %", ya formateado
        public static string Explicacion(CuelloBotella cuello, string cambio) => cuello switch
        {
            CuelloBotella.CpuOServidor =>
                $"Con la resolución al mínimo la GPU dibuja muchos menos píxeles, y aun así los FPS casi no cambiaron ({cambio}). " +
                "La GPU no es la que frena: el límite es la CPU (lógica del juego, scripts del servidor, jugadores, llamadas de dibujo) " +
                "o el propio servidor. Optimizar lo que no es el cuello de botella da cero FPS: bajar gráficos no te subirá nada.",
            _ =>
                $"Con la resolución al mínimo los FPS subieron {cambio}: la GPU es la que frena. " +
                "Los FPS los decide el componente más lento por frame, así que los cambios grandes salen de recortar el trabajo de la GPU."
        };

        // Qué hace Ghost Mode (y qué no) en cada caso
        public static IReadOnlyList<RecomendacionCuello> Recomendaciones(CuelloBotella cuello) => cuello switch
        {
            CuelloBotella.CpuOServidor =>
            [
                new("Modo Gaming: al abrir el juego pausa apps en segundo plano, libera RAM y sube su prioridad. Recupera la CPU y la RAM que otros programas le roban.", true),
                new("Plan de alto rendimiento (Ghost Mode, en Inicio): Windows no recorta la CPU para ahorrar energía mientras juegas.", true),
                new("¿Es el servidor? Cada script del servidor suma tiempo de CPU a cada frame. Compara cl_drawperf en tu servidor y en uno de prueba: la guía está en la pantalla fivem.", true),
                new("En los ajustes del juego, baja la distancia de dibujado y la población: menos objetos en pantalla son menos llamadas de dibujo, que paga la CPU. (Es una inferencia de la investigación: compruébalo con el benchmark.)", true),
                new("game_setDefaultEntityCapLimitToLow, el comando oficial de FiveM para hardware modesto: pendiente de probar con FiveM instalado.", true, Pendiente: true),
                new("Bajar Post FX, MSAA, sombras o la resolución: es trabajo de la GPU, y la GPU no es la que limita.", false),
            ],
            _ =>
            [
                new("GPU dedicada para FiveM (pantalla fivem): en un portátil con dos gráficas, que el juego use la integrada puede costar más que todos los ajustes juntos.", true),
                new("Perfil NVIDIA a máximo rendimiento: Modo Gaming lo pone al abrir FiveM (solo con GPU NVIDIA).", true),
                new("En los ajustes del juego, los que más cuestan en GTA V (GamersNexus): Post FX, MSAA (desactívalo y usa FXAA), calidad de hierba, shaders y sombras. Ponlos en Normal.", true),
                new("Preset gráfico automático de Ghost Mode: pendiente de probar con FiveM instalado.", true, Pendiente: true),
                new("Bajar el filtro anisotrópico o unas texturas que caben en la VRAM: empeora la imagen sin dar FPS.", false),
            ]
        };
    }
}
