using OptimizadorPC.Core;   // Logger, IOptimizacion, Registro, AjusteRegistro

namespace OptimizadorPC.Optimizaciones.Sistema
{
    public class GameMode : IOptimizacion
    {
        public string Id => "game-mode";
        public string Nombre => "Modo Juego sin grabación de fondo";
        public string Descripcion => "Activa el Modo Juego de Windows y apaga Game DVR, que graba clips en segundo plano y gasta CPU y GPU.";
        // Gráficos: en el diseño, "capturas en segundo plano" va en esa columna
        public Categoria Categoria => Categoria.Graficos;
        public Impacto Impacto => Impacto.Alto;
        public Riesgo Riesgo => Riesgo.Seguro;

        // La lista de cambios. Todo lo difícil (guardar el original, revertir)
        // lo hace la clase Registro: aquí solo decimos QUÉ cambiar.
        // @"..." = texto "literal": las \ se toman tal cual, sin escribir \\
        private static readonly AjusteRegistro[] Ajustes =
        {
            // Modo Juego: Windows da prioridad al juego que está en primer plano
            new(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", "1"),
            new(@"Software\Microsoft\GameBar", "AllowAutoGameMode", "1"),

            // Game DVR: grabación automática de partidas (0 = apagado)
            new(@"System\GameConfigStore", "GameDVR_Enabled", "0"),
            new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", "0"),
        };

        public bool EstaActiva() => Registro.EstanAplicados(Ajustes);

        public void Aplicar()
        {
            Registro.Aplicar(Id, Ajustes);
            Logger.Info("Modo Juego activado y Game DVR desactivado");
        }

        public void Revertir()
        {
            Registro.Revertir(Id, Ajustes);
            Logger.Info("Modo Juego y Game DVR restaurados a como estaban");
        }
    }
}
