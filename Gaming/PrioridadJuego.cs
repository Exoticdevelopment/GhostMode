using System.Diagnostics;
using OptimizadorPC.Core;

namespace OptimizadorPC.Gaming
{
    // Prioridad "Alta" para los procesos del juego mientras corren (como en el Administrador de tareas:
    // clic derecho > Establecer prioridad). Nunca "Tiempo real": puede dejar sin CPU al ratón o al audio.
    //
    // La prioridad muere con el proceso, así que al cerrar el juego no queda nada que deshacer.
    // Aun así la original se anota en GestorEstado: si se desactiva Modo Gaming o Ghost Mode se cierra
    // con el juego abierto, se le devuelve la que tenía.
    public static class PrioridadJuego
    {
        public const string Modulo = "modo-gaming-prioridad";

        public static bool Subir(ProcesoJuego proceso)
        {
            try
            {
                using Process p = Process.GetProcessById(proceso.Pid);
                ProcessPriorityClass original = p.PriorityClass;
                if (original is ProcessPriorityClass.High or ProcessPriorityClass.RealTime)
                {
                    return false;   // ya está alta: nada que hacer
                }

                GestorEstado.GuardarOriginal(Modulo, proceso.Clave, original.ToString());
                p.PriorityClass = ProcessPriorityClass.High;
                Logger.Info($"Modo gaming: prioridad alta para {p.ProcessName} (PID {p.Id}, antes {original})");
                return true;
            }
            catch (Exception ex)
            {
                // Pasa con juegos protegidos por su antitrampas: lo dejamos como está
                GestorEstado.Olvidar(Modulo, proceso.Clave);
                Logger.Aviso($"Modo gaming: no se pudo subir la prioridad del PID {proceso.Pid}: {ex.Message}");
                return false;
            }
        }

        public static void RestaurarTodas()
        {
            foreach (string clave in GestorEstado.ClavesGuardadas(Modulo))
            {
                if (Procesos.SiSigueVivo(clave) is int pid &&
                    Enum.TryParse(GestorEstado.ObtenerOriginal(Modulo, clave), out ProcessPriorityClass original))
                {
                    try
                    {
                        using Process p = Process.GetProcessById(pid);
                        p.PriorityClass = original;
                        Logger.Info($"Modo gaming: prioridad de {p.ProcessName} (PID {pid}) devuelta a {original}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Aviso($"Modo gaming: no se pudo devolver la prioridad del PID {pid}: {ex.Message}");
                    }
                }
                GestorEstado.Olvidar(Modulo, clave);
            }
        }
    }
}
