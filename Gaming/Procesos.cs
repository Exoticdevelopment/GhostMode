using System.Runtime.InteropServices;   // DllImport: llamar a funciones de Windows escritas en C
using System.Text;

namespace OptimizadorPC.Gaming
{
    // Un programa abierto, tal como lo da la "foto" de procesos de Windows
    public record ProcesoVivo(int Pid, int PadrePid, string NombreExe);

    // Un proceso de un juego: su PID, cuándo arrancó y la ruta de su .exe
    public record ProcesoJuego(int Pid, long Inicio, string Ruta)
    {
        public string Clave => Procesos.Clave(Pid, Inicio);
    }

    // Lo mínimo que hace falta saber de los procesos, con llamadas directas a Windows (rápidas y sin
    // crear un objeto Process por cada uno: Modo Gaming mira la lista cada 2 s).
    //
    // Clave de un proceso = "PID:hora de inicio". Windows reutiliza los PID: si Ghost Mode se cierra y al
    // volver a abrir encuentra el mismo PID, la hora de inicio dice si es el MISMO proceso o uno nuevo.
    public static class Procesos
    {
        // ===== Leer =====

        // Todos los procesos abiertos: PID, quién lo abrió y el nombre del .exe (Toolhelp32, API documentada)
        public static List<ProcesoVivo> Listar()
        {
            var lista = new List<ProcesoVivo>();
            IntPtr foto = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (foto == INVALID_HANDLE_VALUE)
            {
                return lista;
            }
            try
            {
                var entrada = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
                for (bool hay = Process32FirstW(foto, ref entrada); hay; hay = Process32NextW(foto, ref entrada))
                {
                    lista.Add(new ProcesoVivo((int)entrada.th32ProcessID, (int)entrada.th32ParentProcessID, entrada.szExeFile));
                }
            }
            finally
            {
                CloseHandle(foto);
            }
            return lista;
        }

        // Hora de inicio (formato FILETIME de Windows) o null si ya terminó o no se puede abrir.
        // OJO: un proceso que terminó se puede seguir "abriendo" mientras otro programa lo tenga agarrado
        // (Steam, por ejemplo, con sus juegos). Por eso además se mira que siga corriendo
        public static long? Inicio(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                bool corriendo = GetExitCodeProcess(h, out uint codigo) && codigo == STILL_ACTIVE;
                return corriendo && GetProcessTimes(h, out long creacion, out _, out _, out _) ? creacion : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        // Ruta completa del .exe: C:\Juegos\Juego\Juego.exe
        public static string? Ruta(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                var texto = new StringBuilder(1024);
                int largo = texto.Capacity;
                return QueryFullProcessImageNameW(h, 0, texto, ref largo) ? texto.ToString() : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        // Sesión de Windows: 0 = servicios del sistema; 1, 2... = usuarios con sesión iniciada
        public static int? Sesion(int pid) => ProcessIdToSessionId((uint)pid, out uint sesion) ? (int)sesion : null;

        public static string Clave(int pid, long inicio) => $"{pid}:{inicio}";

        // ¿Sigue abierto el MISMO proceso de esa clave? Devuelve su PID si es así
        public static int? SiSigueVivo(string clave)
        {
            string[] partes = clave.Split(':');
            if (partes.Length != 2 || !int.TryParse(partes[0], out int pid) || !long.TryParse(partes[1], out long inicio))
            {
                return null;
            }
            return Inicio(pid) == inicio ? pid : null;
        }

        // ===== Pausar y reanudar =====
        // NtSuspendProcess / NtResumeProcess (ntdll) congelan y descongelan TODOS los hilos del proceso a la vez.
        // Es lo mismo que hacen el Monitor de recursos de Windows y Process Explorer (Sysinternals).
        // Cada pausa suma 1 y cada reanudación resta 1: por eso se reanuda exactamente una vez por pausa.

        public static bool Suspender(int pid) => ConProceso(pid, PROCESS_SUSPEND_RESUME, h => NtSuspendProcess(h) == 0);

        public static bool Reanudar(int pid) => ConProceso(pid, PROCESS_SUSPEND_RESUME, h => NtResumeProcess(h) == 0);

        private static bool ConProceso(int pid, uint permisos, Func<IntPtr, bool> accion)
        {
            IntPtr h = OpenProcess(permisos, false, pid);
            if (h == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                return accion(h);
            }
            finally
            {
                CloseHandle(h);
            }
        }

        // ===== Funciones de Windows =====

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public UIntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr foto, ref PROCESSENTRY32W entrada);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr foto, ref PROCESSENTRY32W entrada);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint permisos, bool heredar, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(IntPtr h, out long creacion, out long salida, out long kernel, out long usuario);

        // Código de salida de un proceso; 259 (STILL_ACTIVE) = aún no terminó
        private const uint STILL_ACTIVE = 259;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr h, out uint codigo);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr h, int formato, StringBuilder texto, ref int largo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ProcessIdToSessionId(uint pid, out uint sesion);

        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr h);

        [DllImport("ntdll.dll")]
        private static extern int NtResumeProcess(IntPtr h);
    }
}
