using System.Windows.Interop;   // HwndSource: una ventana de Windows "de verdad" sin dibujar nada

namespace OptimizadorPC.Servicios
{
    // Ventana invisible que recibe mensajes de Windows aunque la ventana principal esté oculta en la bandeja:
    // los clics en el icono de la bandeja y la pulsación del atajo global llegan aquí.
    // No es "solo de mensajes" (HWND_MESSAGE) a propósito: esas no reciben "TaskbarCreated", el aviso de que
    // el Explorador se reinició y hay que volver a poner el icono.
    public sealed class VentanaMensajes : IDisposable
    {
        private readonly HwndSource fuente;
        private readonly List<HwndSourceHook> oyentes = new();

        public IntPtr Hwnd => fuente.Handle;

        public VentanaMensajes()
        {
            // Tamaño 0 y sin estilo visible: nunca aparece en pantalla ni en la barra de tareas
            var parametros = new HwndSourceParameters("GhostModeMensajes")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0x80   // WS_EX_TOOLWINDOW: tampoco en Alt+Tab
            };
            fuente = new HwndSource(parametros);
            fuente.AddHook(Despachar);
        }

        // Cada parte (bandeja, atajo) se suscribe a los mensajes que le interesan
        public void Escuchar(HwndSourceHook oyente) => oyentes.Add(oyente);

        private IntPtr Despachar(IntPtr hwnd, int mensaje, IntPtr wParam, IntPtr lParam, ref bool atendido)
        {
            foreach (HwndSourceHook oyente in oyentes)
            {
                IntPtr r = oyente(hwnd, mensaje, wParam, lParam, ref atendido);
                if (atendido)
                {
                    return r;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose() => fuente.Dispose();
    }
}
