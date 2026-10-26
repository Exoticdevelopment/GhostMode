using System.IO;

namespace OptimizadorPC.Gaming
{
    // De dónde salió el juego de la lista
    public enum OrigenJuego { Steam, Epic, FiveM, Manual }

    // Un juego de la lista de Modo Gaming.
    //   Exe     = el ejecutable que se muestra (y del que se saca el ícono)
    //   Carpeta = si no es null, cuenta como "abierto" CUALQUIER programa que corra desde esa carpeta.
    //             Los juegos de Steam/Epic/FiveM a veces arrancan otro .exe distinto del principal
    //             (FiveM, por ejemplo, abre "FiveM_b3095_GTAProcess.exe" dentro de su carpeta).
    //             Los agregados a mano no tienen carpeta: solo cuenta ese .exe exacto.
    public record Juego(string Nombre, string Exe, string? Carpeta, OrigenJuego Origen)
    {
        // Clave estable para guardar su interruptor: la carpeta si la hay (el .exe "principal" de Steam
        // se adivina y podría cambiar con una actualización), si no el .exe
        public string Id => (Carpeta ?? Exe).ToLowerInvariant();

        // ¿Este programa (ruta completa de su .exe) es parte del juego?
        public bool EsSuyo(string rutaProceso) => Carpeta != null
            ? rutaProceso.StartsWith(Path.TrimEndingDirectorySeparator(Carpeta) + "\\", StringComparison.OrdinalIgnoreCase)
            : string.Equals(rutaProceso, Exe, StringComparison.OrdinalIgnoreCase);
    }
}
