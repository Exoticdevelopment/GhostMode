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
        public static readonly string Archivo = Path.Combine(Carpeta, "log.txt");

        // Rotación: al pasar de 5 MB, log.txt pasa a llamarse log.1.txt (se pisa el anterior) y se empieza
        // uno nuevo. Así el log nunca ocupa más de unos 10 MB en total
        public static readonly string ArchivoAnterior = Path.Combine(Carpeta, "log.1.txt");
        public const long TamanoMaximo = 5 * 1024 * 1024;
        private static long? tamano;   // lo que ocupa log.txt: se lee del disco una vez y luego se va sumando

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
            // (y de qué tipo es: "IOException", "UnauthorizedAccessException"... ayuda a saber qué pasó)
            if (ex != null)
            {
                mensaje = $"{mensaje}: {ex.Message} ({ex.GetType().Name})";
            }
            Escribir("ERROR", mensaje);
        }

        // Para errores que nadie esperaba: además del mensaje, DÓNDE pasó (la traza completa), para poder arreglarlo
        public static void Fallo(string mensaje, Exception? ex) =>
            Escribir("ERROR", ex == null ? mensaje : $"{mensaje}: {ex}");

        // private = solo se usa aquí adentro. Es el que realmente escribe en el archivo
        private static void Escribir(string nivel, string mensaje)
        {
            var registro = new LineaLog(DateTime.Now, nivel, mensaje);
            // Arma la línea: [2026-10-25 11:23:00] [INFO] mensaje
            string linea = $"[{registro.Hora:yyyy-MM-dd HH:mm:ss}] [{nivel}] {mensaje}{Environment.NewLine}";

            lock (Candado)
            {
                try
                {
                    Directory.CreateDirectory(Carpeta);   // crea la carpeta si no existe
                    RotarSiHaceFalta();
                    File.AppendAllText(Archivo, linea);   // agrega la línea al final
                    tamano += System.Text.Encoding.UTF8.GetByteCount(linea);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Disco lleno o archivo bloqueado: el log no puede tumbar la app. La línea sigue en
                    // memoria (panel [ LOG ]) y se vuelve a intentar escribir con la siguiente
                    tamano = null;
                }

                recientes.Enqueue(registro);
                if (recientes.Count > MaxRecientes)
                {
                    recientes.Dequeue();   // sale la más vieja
                }
            }

            // Fuera del candado: si quien escucha tarda, no frena a los demás que quieren escribir
            NuevaLinea?.Invoke(registro);
        }

        // Se llama con el candado puesto
        private static void RotarSiHaceFalta()
        {
            tamano ??= File.Exists(Archivo) ? new FileInfo(Archivo).Length : 0;
            if (tamano >= TamanoMaximo)
            {
                tamano = Rotar(Archivo, ArchivoAnterior);
            }
        }

        // archivo → anterior (pisando el que hubiera) y archivo nuevo con una línea que lo explica.
        // Devuelve lo que ocupa el nuevo. Pública para probarla con archivos temporales, sin tocar el log real
        public static long Rotar(string archivo, string anterior)
        {
            File.Move(archivo, anterior, overwrite: true);
            string aviso = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] Log rotado: lo anterior está en {Path.GetFileName(anterior)}{Environment.NewLine}";
            File.AppendAllText(archivo, aviso);
            return System.Text.Encoding.UTF8.GetByteCount(aviso);
        }
    }
}