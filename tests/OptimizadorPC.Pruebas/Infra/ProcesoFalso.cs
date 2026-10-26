using System.Diagnostics;

namespace OptimizadorPC.Pruebas.Infra
{
    // Un "juego" o una "app de fondo" de mentira: una copia de ping.exe de Windows con otro nombre,
    // en una carpeta temporal propia, haciendo ping a 127.0.0.1 durante unos minutos (sin ventana).
    // Así las pruebas nunca tocan programas reales del usuario. Dispose lo cierra y borra la carpeta.
    public sealed class ProcesoFalso : IDisposable
    {
        public string Carpeta { get; }
        public string Exe { get; }
        public Process Proceso { get; }

        public string NombreExe => Path.GetFileName(Exe);

        public ProcesoFalso(string nombreExe, string? carpeta = null, string? copiarDe = null, string argumentos = "-n 300 127.0.0.1")
        {
            Carpeta = carpeta ?? Ayudas.CarpetaTemporal();
            Directory.CreateDirectory(Carpeta);
            Exe = Path.Combine(Carpeta, nombreExe);
            File.Copy(copiarDe ?? Path.Combine(Environment.SystemDirectory, "PING.EXE"), Exe);
            Proceso = Process.Start(new ProcessStartInfo(Exe, argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true   // que su salida no aparezca en la consola de las pruebas
            })!;
        }

        // ¿Todos sus hilos están congelados? (así se ve un proceso pausado en el Administrador de tareas)
        public static bool EstaPausado(int pid)
        {
            using Process p = Process.GetProcessById(pid);
            p.Refresh();
            return p.Threads.Cast<ProcessThread>().All(h =>
                h.ThreadState == System.Diagnostics.ThreadState.Wait && h.WaitReason == ThreadWaitReason.Suspended);
        }

        public void Cerrar()
        {
            try
            {
                if (!Proceso.HasExited)
                {
                    Proceso.Kill(entireProcessTree: true);
                    Proceso.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
                // ya estaba cerrado
            }
        }

        public void Dispose()
        {
            Cerrar();
            Proceso.Dispose();
            try
            {
                Directory.Delete(Carpeta, recursive: true);
            }
            catch (IOException)
            {
                // Windows tarda un instante en soltar el .exe; reintentar una vez
                Thread.Sleep(500);
                Directory.Delete(Carpeta, recursive: true);
            }
        }
    }
}
