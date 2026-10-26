using System.IO;
using System.Text.Json;
using OptimizadorPC.Core;

namespace OptimizadorPC.Gaming
{
    // Un juego agregado a mano con "[ agregar juego ]"
    public record JuegoManual(string Nombre, string Exe);

    // Lo que el usuario elige en la pantalla Modo Gaming. Se guarda en C:\ProgramData\OptimizadorPC\modo_gaming.json.
    // No son "valores originales" de Windows (eso va en GestorEstado): son preferencias de la app
    public sealed class ConfigGaming
    {
        public static readonly string ArchivoPorDefecto = Path.Combine(Logger.Carpeta, "modo_gaming.json");

        // Las opciones de "Al iniciar una partida".
        // Suspender empieza apagada: pausar el navegador corta la música o el vídeo que tengas abierto
        public bool PrioridadAlta { get; set; } = true;
        public bool SuspenderApps { get; set; } = false;
        public bool LiberarRam { get; set; } = true;
        public bool OptimizacionesInicio { get; set; } = true;
        public bool PausarGrabacion { get; set; } = true;   // Game DVR / Xbox Game Bar

        // Nombres de .exe que se pausan durante la partida (editable desde la pantalla)
        public List<string> AppsSuspender { get; set; } = [.. SuspensorApps.PorDefecto];

        public List<JuegoManual> JuegosManuales { get; set; } = [];

        // Interruptor "activación automática" de cada juego: Juego.Id → encendido.
        // Los detectados empiezan apagados (el usuario elige cuáles); los agregados a mano, encendidos
        public Dictionary<string, bool> Automatico { get; set; } = new();

        public bool EsAutomatico(Juego juego) =>
            Automatico.TryGetValue(juego.Id, out bool activo) ? activo : juego.Origen == OrigenJuego.Manual;

        public static ConfigGaming Cargar(string? archivo = null)
        {
            archivo ??= ArchivoPorDefecto;
            try
            {
                if (File.Exists(archivo) && JsonSerializer.Deserialize<ConfigGaming>(File.ReadAllText(archivo)) is ConfigGaming config)
                {
                    return config;
                }
            }
            catch (Exception ex)
            {
                // Archivo dañado: se empieza con los valores por defecto, sin tumbar la app
                Logger.Error("No se pudo leer modo_gaming.json", ex);
            }
            return new ConfigGaming();
        }

        public void Guardar(string? archivo = null)
        {
            archivo ??= ArchivoPorDefecto;
            Directory.CreateDirectory(Path.GetDirectoryName(archivo)!);
            File.WriteAllText(archivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
