using System.Diagnostics;   // Para ejecutar PowerShell
using System.Text;          // Encoding (para leer bien las tildes de los mensajes)

namespace OptimizadorPC.Core
{
    // enum = lista cerrada de valores posibles. Más claro que un true/false
    // porque aquí hay tres resultados distintos.
    public enum ResultadoPunto
    {
        Creado,               // Todo bien
        YaHabiaUnoReciente,   // Windows solo permite uno cada 24 h: hay uno de hace poco
        Fallo                 // Normalmente: la "Protección del sistema" está desactivada
    }

    public static class PuntoRestauracion
    {
        // async Task<...> = método que tarda y no bloquea la ventana mientras espera.
        // Se usa así:  ResultadoPunto r = await PuntoRestauracion.CrearAsync("...");
        public static async Task<ResultadoPunto> CrearAsync(string descripcion)
        {
            Logger.Info($"Creando punto de restauración: {descripcion}");

            // En PowerShell, una comilla simple dentro de '...' se escribe doble ('')
            string texto = descripcion.Replace("'", "''");

            // El script devuelve un "código de salida" según lo que pasó:
            //   0 = creado, 2 = Windows avisó que ya hay uno reciente, 1 = error
            string script = $@"
                [Console]::OutputEncoding = [Text.Encoding]::UTF8
                try {{
                    Checkpoint-Computer -Description '{texto}' -RestorePointType MODIFY_SETTINGS `
                        -ErrorAction Stop -WarningAction SilentlyContinue -WarningVariable avisos
                    if ($avisos) {{ exit 2 }} else {{ exit 0 }}
                }} catch {{
                    [Console]::Error.WriteLine($_.Exception.Message)
                    exit 1
                }}";
            // Nota: en un $@"..." las llaves de PowerShell se escriben dobles {{ }}
            // para que C# no las confunda con sus propias {variables}

            try
            {
                var (codigo, error) = await EjecutarPowerShellAsync(script);

                // switch = elegir según el valor de "codigo"
                switch (codigo)
                {
                    case 0:
                        Logger.Info("Punto de restauración creado");
                        return ResultadoPunto.Creado;
                    case 2:
                        Logger.Aviso("Windows no creó el punto: ya existe uno de las últimas 24 horas");
                        return ResultadoPunto.YaHabiaUnoReciente;
                    default:
                        Logger.Error($"No se pudo crear el punto de restauración: {error}");
                        return ResultadoPunto.Fallo;
                }
            }
            catch (Exception ex)
            {
                // Por ejemplo, si powershell.exe no se pudo abrir
                Logger.Error("Error al crear el punto de restauración", ex);
                return ResultadoPunto.Fallo;
            }
        }

        // Ejecuta un script y devuelve DOS cosas a la vez (una "tupla"):
        // el código de salida y el mensaje de error, si lo hubo
        private static async Task<(int Codigo, string Error)> EjecutarPowerShellAsync(string script)
        {
            var configuracion = new ProcessStartInfo
            {
                // powershell.exe (la versión 5.1 que trae Windows) tiene Checkpoint-Computer;
                // la versión 7 (pwsh) no lo incluye
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,       // capturar los mensajes de error
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // ArgumentList: .NET pone las comillas por nosotros, aunque el script tenga espacios
            configuracion.ArgumentList.Add("-NoProfile");       // no cargar la configuración del usuario
            configuracion.ArgumentList.Add("-NonInteractive");  // nunca quedarse esperando una respuesta
            configuracion.ArgumentList.Add("-Command");
            configuracion.ArgumentList.Add(script);

            using var proceso = Process.Start(configuracion)!;

            // Leemos la salida y los errores a la vez. Si leyéramos uno y luego el otro,
            // PowerShell podría quedarse trabado esperando a que vaciemos el que no leemos.
            Task<string> salida = proceso.StandardOutput.ReadToEndAsync();
            Task<string> error = proceso.StandardError.ReadToEndAsync();

            await proceso.WaitForExitAsync();   // esperar sin congelar la ventana
            await salida;

            // Trim quita espacios y saltos de línea sobrantes ('﻿' = marca invisible de UTF-8)
            return (proceso.ExitCode, (await error).Trim().Trim('﻿'));
        }
    }
}
