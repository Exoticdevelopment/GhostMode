using System.IO;
using System.Text.Json;
// System.Text.Json = herramienta de .NET para leer y escribir archivos JSON

namespace OptimizadorPC.Core
{
    public static class GestorEstado
    {
        // Archivo: C:\ProgramData\OptimizadorPC\estado.json
        // Reutilizamos la carpeta del Logger para no repetir la ruta
        private static readonly string Archivo = Path.Combine(Logger.Carpeta, "estado.json");

        // Los datos en memoria. Es un "diccionario de diccionarios":
        //   clave externa = Id del módulo ("plan-energia")
        //   clave interna = qué guardamos ("guid") → valor original
        private static Dictionary<string, Dictionary<string, string?>> datos = Cargar();

        // Guarda el valor original SOLO si no se había guardado antes.
        // ¿Por qué? Si aplicas el modo gaming dos veces seguidas, la segunda vez
        // el "original" ya sería el valor optimizado, y perderíamos el verdadero.
        public static void GuardarOriginal(string modulo, string clave, string? valor)
        {
            // Si el módulo no tiene su "cajón" todavía, se lo creamos
            if (!datos.ContainsKey(modulo))
            {
                datos[modulo] = new Dictionary<string, string?>();
            }

            // Solo guardamos si esa clave no existía
            if (!datos[modulo].ContainsKey(clave))
            {
                datos[modulo][clave] = valor;
                Persistir();
                Logger.Info($"Estado original guardado: {modulo}.{clave} = {valor ?? "(no existía)"}");
            }
        }

        // Devuelve el valor original guardado, o null si no hay nada
        public static string? ObtenerOriginal(string modulo, string clave)
        {
            if (datos.ContainsKey(modulo) && datos[modulo].ContainsKey(clave))
            {
                return datos[modulo][clave];
            }
            return null;
        }

        // Olvida UNA clave (no todo el módulo). Si el módulo se queda vacío, se quita entero.
        // Sirve cuando el usuario deja algo otra vez como estaba al principio
        public static void Olvidar(string modulo, string clave)
        {
            if (datos.TryGetValue(modulo, out var claves) && claves.Remove(clave))
            {
                if (claves.Count == 0)
                {
                    datos.Remove(modulo);
                }
                Persistir();
            }
        }

        // ¿Hay algo guardado para esta clave?
        public static bool TieneOriginal(string modulo, string clave)
        {
            return datos.ContainsKey(modulo) && datos[modulo].ContainsKey(clave);
        }

        // Se llama después de revertir un módulo: ya no necesitamos su "foto"
        public static void Limpiar(string modulo)
        {
            if (datos.Remove(modulo))
            {
                Persistir();
            }
        }

        // ¿Quedó algún cambio sin revertir? Sirve para el botón "Deshacer todo"
        public static bool HayCambiosPendientes => datos.Count > 0;

        // ¿Este módulo en concreto tiene algo que revertir? Sirve para su botón "Revertir"
        public static bool TieneCambios(string modulo) => datos.ContainsKey(modulo);

        // Qué claves tiene guardadas un módulo. Sirve a los módulos cuya lista de cambios
        // no es fija (p. ej. un ejecutable de FiveM que ya no existe pero que la app cambió)
        public static IReadOnlyList<string> ClavesGuardadas(string modulo) =>
            datos.TryGetValue(modulo, out var claves) ? claves.Keys.ToList() : [];

        // Lee el archivo JSON al iniciar la app
        private static Dictionary<string, Dictionary<string, string?>> Cargar()
        {
            try
            {
                if (File.Exists(Archivo))
                {
                    string json = File.ReadAllText(Archivo);
                    var resultado = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(json);
                    if (resultado != null)
                    {
                        return resultado;
                    }
                }
            }
            catch (Exception ex)
            {
                // Si el archivo está dañado, no tumbamos la app: lo anotamos y empezamos vacío
                Logger.Error("No se pudo leer estado.json", ex);
            }
            return new Dictionary<string, Dictionary<string, string?>>();
        }

        // Escribe los datos en el archivo JSON
        private static void Persistir()
        {
            Directory.CreateDirectory(Logger.Carpeta);
            var opciones = new JsonSerializerOptions { WriteIndented = true };  // JSON bonito, con saltos de línea
            File.WriteAllText(Archivo, JsonSerializer.Serialize(datos, opciones));
        }
    }
}