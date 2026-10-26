using System.Diagnostics;               // Para ejecutar programas externos (powercfg)
using System.Text.RegularExpressions;   // Para buscar patrones en texto
using OptimizadorPC.Core;               // Nuestro "barrio": Logger, GestorEstado, IOptimizacion

namespace OptimizadorPC.Optimizaciones.Sistema
{
    // ": IOptimizacion" = esta clase promete cumplir el contrato
    public class PlanEnergia : IOptimizacion
    {
        // GUID = identificador único que Windows le da a cada plan de energía.
        // Este es el de "Alto rendimiento", es igual en todos los Windows.
        private const string AltoRendimiento = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        // Lo que exige el contrato
        public string Id => "plan-energia";
        public string Nombre => "Plan de alto rendimiento";
        public string Descripcion => "Evita que el procesador baje su velocidad para ahorrar energía.";
        public Categoria Categoria => Categoria.Energia;
        public Impacto Impacto => Impacto.Alto;
        public Riesgo Riesgo => Riesgo.Seguro;

        public bool EstaActiva()
        {
            return ObtenerPlanActual() == AltoRendimiento;
        }

        public void Aplicar()
        {
            // 1. Foto del "antes" (GestorEstado solo la guarda la primera vez)
            GestorEstado.GuardarOriginal(Id, "guid", ObtenerPlanActual());

            // 2. Aplicar el cambio
            EjecutarPowercfg($"/setactive {AltoRendimiento}");
            Logger.Info("Plan de energía cambiado a Alto rendimiento");
        }

        public void Revertir()
        {
            string? original = GestorEstado.ObtenerOriginal(Id, "guid");

            if (original == null)
            {
                Logger.Aviso("No hay plan original guardado, no se revierte nada");
                return;   // salimos del método
            }

            // Si powercfg falla, lanza un error ANTES de Limpiar: la foto se conserva para reintentar
            EjecutarPowercfg($"/setactive {original}");
            GestorEstado.Limpiar(Id);   // ya no necesitamos la foto
            Logger.Info($"Plan de energía restaurado a {original}");
        }

        // ---------- Métodos de ayuda (privados) ----------

        private string ObtenerPlanActual()
        {
            // powercfg /getactivescheme devuelve algo como:
            // "GUID del plan de energía: 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado)"
            string salida = EjecutarPowercfg("/getactivescheme");

            // Buscamos el patrón de un GUID (8-4-4-4-12 caracteres hexadecimales).
            // Así funciona aunque Windows esté en español, inglés o cualquier idioma.
            Match coincidencia = Regex.Match(salida, @"[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");

            // Sin GUID no sabríamos a qué plan volver: mejor no cambiar nada
            if (!coincidencia.Success)
            {
                throw new InvalidOperationException($"powercfg no indicó el plan de energía activo: {salida.Trim()}");
            }
            return coincidencia.Value.ToLower();
        }

        // Ejecuta powercfg y devuelve lo que imprime. Si Windows rechaza la orden (p. ej. un plan que
        // no existe en este PC), lanza un error con su mensaje: así nunca se anota un cambio que no se hizo
        private static string EjecutarPowercfg(string argumentos)
        {
            var configuracion = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = argumentos,
                RedirectStandardOutput = true,   // capturar lo que imprime
                RedirectStandardError = true,    // y los mensajes de error
                UseShellExecute = false,
                CreateNoWindow = true            // sin ventana negra parpadeando
            };

            using var proceso = Process.Start(configuracion)!;
            // Leemos las dos salidas a la vez para que powercfg no se quede trabado esperando
            Task<string> errores = proceso.StandardError.ReadToEndAsync();
            string salida = proceso.StandardOutput.ReadToEnd();
            proceso.WaitForExit();

            if (proceso.ExitCode != 0)
            {
                // powercfg a veces escribe el error en la salida normal: usamos la que tenga texto
                string mensaje = errores.Result.Trim();
                throw new InvalidOperationException(
                    $"powercfg {argumentos} falló (código {proceso.ExitCode}): {(mensaje.Length > 0 ? mensaje : salida.Trim())}");
            }
            return salida;
        }
    }
}