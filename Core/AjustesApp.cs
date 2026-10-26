using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OptimizadorPC.Core
{
    // Preferencias generales de la app: C:\ProgramData\OptimizadorPC\ajustes.json.
    // (Las de Modo Gaming van aparte, en modo_gaming.json; los valores originales de Windows, en estado.json)
    public sealed class AjustesApp
    {
        public static readonly string ArchivoPorDefecto = Path.Combine(Logger.Carpeta, "ajustes.json");

        // La app la usa así; las pruebas cargan la suya con otro archivo
        public static AjustesApp Actual { get; } = Cargar();

        // La bienvenida de 3 pasos ya se mostró (sale solo la primera vez)
        public bool BienvenidaVista { get; set; }

        // Atajo global para activar/desactivar Ghost Mode. null = sin atajo
        public string? Atajo { get; set; } = "Ctrl+Alt+G";

        // El aviso "Ghost Mode sigue en la bandeja" sale una sola vez
        public bool AvisoBandejaMostrado { get; set; }

        public static AjustesApp Cargar(string? archivo = null)
        {
            archivo ??= ArchivoPorDefecto;
            try
            {
                if (File.Exists(archivo) && JsonSerializer.Deserialize<AjustesApp>(File.ReadAllText(archivo)) is AjustesApp ajustes)
                {
                    return ajustes;
                }
            }
            catch (Exception ex)
            {
                // Archivo dañado: valores por defecto, sin tumbar la app
                Logger.Error("No se pudo leer ajustes.json", ex);
            }
            return new AjustesApp();
        }

        // Devuelve false si no se pudo escribir (queda en el log); la app sigue con lo que tiene en memoria
        public bool Guardar(string? archivo = null)
        {
            archivo ??= ArchivoPorDefecto;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(archivo)!);
                File.WriteAllText(archivo, JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // "Ctrl+Alt+G" legible (sin escapar el +)
                }));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo guardar ajustes.json", ex);
                return false;
            }
        }
    }
}
