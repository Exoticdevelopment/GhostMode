using OptimizadorPC.Core;

namespace OptimizadorPC.Gaming
{
    // Durante la partida: apaga la grabación de Game DVR (Xbox Game Bar), incluida la grabación en segundo plano
    // ("grabar lo que acaba de pasar"), que mientras está activa codifica vídeo todo el rato y gasta GPU.
    // Al cerrar el juego vuelve a dejarla como estaba (Registro guarda el valor original de cada clave).
    //
    // Se solapa a propósito con la optimización "Modo Juego sin grabación de fondo": si esa ya está puesta,
    // aquí no hay nada que hacer. Si las dos se aplican en la misma sesión, se deshacen en orden inverso
    // (primero esta) para que cada una devuelva exactamente el valor que encontró.
    public static class GrabacionFondo
    {
        public const string Modulo = "modo-gaming-grabacion";

        private static readonly AjusteRegistro[] Ajustes =
        [
            new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", "0"),
            new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", "0"),
            new(@"System\GameConfigStore", "GameDVR_Enabled", "0"),
        ];

        public static bool EstaApagada() => Registro.EstanAplicados(Ajustes);

        // true si cambió algo
        public static bool Apagar()
        {
            if (EstaApagada())
            {
                return false;
            }
            Registro.Aplicar(Modulo, Ajustes);
            Logger.Info("Modo gaming: grabación de Game DVR en pausa durante la partida");
            return true;
        }

        public static void Restaurar()
        {
            if (!GestorEstado.TieneCambios(Modulo))
            {
                return;
            }
            Registro.Revertir(Modulo, Ajustes);
            Logger.Info("Modo gaming: grabación de Game DVR restaurada a como estaba");
        }
    }
}
