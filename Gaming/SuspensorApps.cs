using System.IO;
using OptimizadorPC.Core;

namespace OptimizadorPC.Gaming
{
    // Pausa apps en segundo plano mientras se juega y las reanuda al terminar.
    // Pausar = congelar sus hilos: no usan CPU (la memoria sigue ocupada). Al reanudar siguen donde estaban.
    //
    // Seguridad ante cierres inesperados: ANTES de pausar cada proceso se anota en GestorEstado
    // (módulo "modo-gaming-suspendidas"). Si Ghost Mode se cierra o falla con apps pausadas,
    // al volver a abrirse ReanudarTodas() las encuentra en estado.json y las reanuda.
    public static class SuspensorApps
    {
        public const string Modulo = "modo-gaming-suspendidas";

        // Lista inicial: navegadores y launchers que se quedan abiertos de fondo.
        // Steam no está: su overlay y sus logros dependen de que siga funcionando
        public static readonly IReadOnlyList<string> PorDefecto =
        [
            "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe", "vivaldi.exe",
            "EpicGamesLauncher.exe", "EpicWebHelper.exe", "EADesktop.exe", "Battle.net.exe", "upc.exe"
        ];

        // NUNCA se pausan, aunque el usuario los escriba en la lista:
        // Windows, antivirus, audio, chat de voz y overlays/grabación
        private static readonly HashSet<string> Protegidos = new(StringComparer.OrdinalIgnoreCase)
        {
            // Windows
            "system", "registry", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe",
            "svchost.exe", "dwm.exe", "explorer.exe", "fontdrvhost.exe", "sihost.exe", "ctfmon.exe", "taskhostw.exe",
            "runtimebroker.exe", "searchhost.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
            "textinputhost.exe", "conhost.exe", "taskmgr.exe", "dllhost.exe", "wudfhost.exe", "spoolsv.exe",
            // Antivirus
            "msmpeng.exe", "nissrv.exe", "mpdefendercoreservice.exe", "securityhealthservice.exe",
            "securityhealthsystray.exe", "smartscreen.exe", "avp.exe", "avpui.exe", "avastui.exe", "avastsvc.exe",
            "avgui.exe", "avgsvc.exe", "bdagent.exe", "bdservicehost.exe", "vsserv.exe", "mcshield.exe", "mfemms.exe",
            "ekrn.exe", "egui.exe", "nortonsecurity.exe", "ccsvchst.exe", "mbam.exe", "mbamservice.exe", "mbamtray.exe",
            "wrsa.exe", "sophoshealth.exe", "savservice.exe",
            // Audio
            "audiodg.exe", "rtkauduservice64.exe", "nahimicsvc64.exe", "nahimicsvc32.exe", "voicemeeter.exe",
            "voicemeeterpro.exe", "voicemeeter8.exe", "voicemeeter8x64.exe", "soundswitch.exe", "ravbg64.exe",
            // Voz: la gente habla mientras juega
            "discord.exe", "discordptb.exe", "discordcanary.exe", "teamspeak.exe", "ts3client_win64.exe", "mumble.exe",
            // Overlays, grabación y la tienda que lanza el juego
            "steam.exe", "steamwebhelper.exe", "obs64.exe", "nvcontainer.exe", "nvidia overlay.exe", "nvidia share.exe",
            "radeonsoftware.exe", "amdrsserv.exe", "gamebar.exe"
        };

        // Por qué no se puede agregar un nombre a la lista (null = sí se puede)
        public static string? MotivoRechazo(string nombreExe)
        {
            if (!nombreExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || nombreExe.Length <= 4 ||
                nombreExe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "Escribe el nombre del programa terminado en .exe (por ejemplo, chrome.exe).";
            }
            if (Protegidos.Contains(nombreExe))
            {
                return $"{nombreExe} es de Windows, del antivirus, de audio o de chat de voz: nunca se pausa.";
            }
            return null;
        }

        // Pausa los procesos de la lista. No toca:
        //   - los protegidos, los de otra sesión (servicios) ni los que corren desde la carpeta de Windows
        //   - el propio Ghost Mode
        //   - los del juego (excluir) ni los que abrieron el juego (su launcher: si se pausa, el juego puede fallar)
        // Devuelve los nombres de lo que se pausó
        public static List<string> Suspender(IEnumerable<string> nombres, IReadOnlySet<int> excluir)
        {
            var lista = new HashSet<string>(nombres.Where(n => MotivoRechazo(n) == null), StringComparer.OrdinalIgnoreCase);
            List<ProcesoVivo> procesos = Procesos.Listar();
            HashSet<int> intocables = ConSusPadres(excluir, procesos);
            intocables.Add(Environment.ProcessId);

            int miSesion = Procesos.Sesion(Environment.ProcessId) ?? -1;
            string carpetaWindows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
            var pausados = new List<string>();

            foreach (ProcesoVivo p in procesos.Where(p => lista.Contains(p.NombreExe) && !intocables.Contains(p.Pid)))
            {
                if (Procesos.Sesion(p.Pid) != miSesion ||
                    Procesos.Ruta(p.Pid) is not string ruta || ruta.StartsWith(carpetaWindows, StringComparison.OrdinalIgnoreCase) ||
                    Procesos.Inicio(p.Pid) is not long inicio)
                {
                    continue;
                }

                // Primero se anota, después se pausa: si la app muere entre medio, al volver se intenta reanudar
                // (reanudar algo que no estaba pausado no hace nada)
                string clave = Procesos.Clave(p.Pid, inicio);
                GestorEstado.GuardarOriginal(Modulo, clave, p.NombreExe);
                if (Procesos.Suspender(p.Pid))
                {
                    pausados.Add(p.NombreExe);
                }
                else
                {
                    GestorEstado.Olvidar(Modulo, clave);
                    Logger.Aviso($"Modo gaming: Windows no dejó pausar {p.NombreExe} (PID {p.Pid})");
                }
            }

            if (pausados.Count > 0)
            {
                Logger.Info($"Modo gaming: {pausados.Count} procesos pausados ({string.Join(", ", pausados.Distinct())})");
            }
            return pausados;
        }

        // Reanuda TODO lo anotado en estado.json (de esta sesión o de una anterior que terminó mal)
        public static int ReanudarTodas()
        {
            int reanudados = 0;
            foreach (string clave in GestorEstado.ClavesGuardadas(Modulo))
            {
                string nombre = GestorEstado.ObtenerOriginal(Modulo, clave) ?? "?";
                if (Procesos.SiSigueVivo(clave) is int pid)
                {
                    if (Procesos.Reanudar(pid))
                    {
                        reanudados++;
                    }
                    else
                    {
                        Logger.Error($"Modo gaming: no se pudo reanudar {nombre} (PID {pid})");
                        continue;   // se queda anotado: se reintenta la próxima vez
                    }
                }
                // Si ya no existe, no hay nada que reanudar: se olvida
                GestorEstado.Olvidar(Modulo, clave);
            }
            if (reanudados > 0)
            {
                Logger.Info($"Modo gaming: {reanudados} procesos reanudados");
            }
            return reanudados;
        }

        // Los PID dados más todos sus "antepasados" (quien los abrió, quien abrió a ese...)
        private static HashSet<int> ConSusPadres(IReadOnlySet<int> pids, List<ProcesoVivo> procesos)
        {
            Dictionary<int, int> padreDe = procesos.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First().PadrePid);
            var resultado = new HashSet<int>(pids);
            foreach (int pid in pids)
            {
                int actual = pid;
                // Máximo 10 saltos: Windows reutiliza PID y un padre "huérfano" podría formar un ciclo
                for (int i = 0; i < 10 && padreDe.TryGetValue(actual, out int padre) && padre > 4; i++)
                {
                    resultado.Add(padre);
                    actual = padre;
                }
            }
            return resultado;
        }
    }
}
