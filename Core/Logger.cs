using System.IO;
// Trae las herramientas para manejar archivos y carpetas

namespace OptimizadorPC.Core
{
    // Una línea del log ya escrita: hora, nivel ("INFO", "AVISO", "ERROR") y mensaje
    public record LineaLog(DateTime Hora, string Nivel, string Mensaje);

    // static = no hace falta crearla con "new", se usa directo: Logger.Info("...")
    public static class Logger
    {
        // Carpeta donde se guarda todo: C:\ProgramData\OptimizadorPC
        public static readonly string Carpeta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OptimizadorPC");

        // Archivo del log: C:\ProgramData\OptimizadorPC\log.txt
        private static readonly string Archivo = Path.Combine(Carpeta, "log.txt");

        // Evita que dos partes de la app escriban al mismo tiempo y se mezclen las líneas
        private static readonly object Candado = new();

        // Las últimas líneas en memoria, para que el panel [ LOG ] tenga historial al abrirse
        private const int MaxRecientes = 50;
        private static readonly Queue<LineaLog> recientes = new();

        // Aviso de "se escribió una línea nueva". OJO: puede llegar desde cualquier hilo
        // (el log se usa también en segundo plano); quien lo escuche debe pasarlo al hilo de la interfaz
        public static event Action<LineaLog>? NuevaLinea;

        // Copia de las últimas líneas, de la más vieja a la más nueva
        public static IReadOnlyList<LineaLog> Recientes
        {
            get
            {
                lock (Candado)
                {
                    return recientes.ToList();
                }
            }
        }

        // Los tres tipos de mensaje. Todos usan el mismo método Escribir()
        public static void Info(string mensaje) => Escribir("INFO", mensaje);

        public static void Aviso(string mensaje) => Escribir("AVISO", mensaje);

        public static void Error(string mensaje, Exception? ex = null)
        {
            // Si nos pasaron un error (ex), agregamos su explicación al mensaje
            if (ex != null)
            {
                mensaje = $"{mensaje}: {ex.Message}";
            }
            Escribir("ERROR", mensaje);
        }

        // private = solo se usa aquí adentro. Es el que realmente escribe en el archivo
        private static void Escribir(string nivel, string mensaje)
        {
            var registro = new LineaLog(DateTime.Now, nivel, mensaje);
            // Arma la línea: [2026-10-25 11:23:00] [INFO] mensaje
            string linea = $"[{registro.Hora:yyyy-MM-dd HH:mm:ss}] [{nivel}] {mensaje}";

            lock (Candado)
            {
                Directory.CreateDirectory(Carpeta);   // crea la carpeta si no existe
                File.AppendAllText(Archivo, linea + Environment.NewLine);   // agrega la línea al final

                recientes.Enqueue(registro);
                if (recientes.Count > MaxRecientes)
                {
                    recientes.Dequeue();   // sale la más vieja
                }
            }

            // Fuera del candado: si quien escucha tarda, no frena a los demás que quieren escribir
            NuevaLinea?.Invoke(registro);
        }
    }
}