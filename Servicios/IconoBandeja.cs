using System.Runtime.InteropServices;   // DllImport
using System.Windows;
using System.Windows.Controls;
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // El icono de Ghost Mode junto al reloj (área de notificación), hecho con Shell_NotifyIcon de Windows:
    // sin cargar Windows Forms, para que la app siga siendo liviana.
    //   clic izquierdo  → abrir la ventana
    //   clic derecho    → menú (Abrir, Activar/Desactivar Ghost Mode, Deshacer todo, Salir)
    // También muestra notificaciones de Windows cuando la ventana no está a la vista.
    public sealed class IconoBandeja : IDisposable
    {
        // La de la app (null hasta que MainWindow la crea). Avisos la usa para notificar
        public static IconoBandeja? Actual { get; private set; }

        public event Action? Abrir;
        public event Action? AlternarGhost;
        public event Action? DeshacerTodo;
        public event Action? Salir;

        // El texto del menú depende del estado: "Activar" o "Desactivar Ghost Mode"
        public Func<bool>? GhostActivo { get; set; }

        private readonly VentanaMensajes ventana;
        private readonly IntPtr icono;
        private readonly uint mensajeTaskbarCreated;
        private bool visible;

        public IconoBandeja(VentanaMensajes ventana)
        {
            this.ventana = ventana;
            icono = CargarIcono();
            mensajeTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
            ventana.Escuchar(AlRecibirMensaje);
            Mostrar();
            Actual = this;
        }

        private void Mostrar()
        {
            NOTIFYICONDATAW datos = Datos(NIF_MESSAGE | NIF_ICON | NIF_TIP);
            datos.szTip = "Ghost Mode";
            visible = Shell_NotifyIcon(NIM_ADD, ref datos);
            if (!visible)
            {
                Logger.Aviso("No se pudo poner el icono en la bandeja del sistema");
            }
        }

        // Notificación de Windows (globo / toast) con el icono de Ghost Mode
        public void Notificar(string titulo, string mensaje)
        {
            if (!visible)
            {
                return;
            }
            NOTIFYICONDATAW datos = Datos(NIF_INFO);
            datos.szInfoTitle = Recortar(titulo, 63);
            datos.szInfo = Recortar(mensaje, 255);
            datos.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
            datos.hBalloonIcon = icono;
            Shell_NotifyIcon(NIM_MODIFY, ref datos);
        }

        private IntPtr AlRecibirMensaje(IntPtr hwnd, int mensaje, IntPtr wParam, IntPtr lParam, ref bool atendido)
        {
            // El Explorador se reinició (o se colgó): la bandeja se vació, hay que volver a poner el icono
            if ((uint)mensaje == mensajeTaskbarCreated)
            {
                Mostrar();
                return IntPtr.Zero;
            }
            if (mensaje != WM_ICONO)
            {
                return IntPtr.Zero;
            }

            atendido = true;
            switch ((int)lParam)
            {
                case WM_LBUTTONUP:
                    Abrir?.Invoke();
                    break;
                case WM_RBUTTONUP:
                    MostrarMenu();
                    break;
            }
            return IntPtr.Zero;
        }

        private void MostrarMenu()
        {
            bool activo = SinFallar(() => GhostActivo?.Invoke() ?? false);
            var menu = new ContextMenu();
            menu.Items.Add(Opcion("Abrir Ghost Mode", () => Abrir?.Invoke()));
            menu.Items.Add(Opcion(activo ? "Desactivar Ghost Mode" : "Activar Ghost Mode", () => AlternarGhost?.Invoke()));
            menu.Items.Add(Opcion("Deshacer todo", () => DeshacerTodo?.Invoke()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Opcion("Salir", () => Salir?.Invoke()));

            // Sin esto, el menú no se cierra al hacer clic fuera de él (comportamiento documentado de Windows)
            SetForegroundWindow(ventana.Hwnd);
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private static MenuItem Opcion(string texto, Action accion)
        {
            var item = new MenuItem { Header = texto };
            item.Click += (s, e) => accion();
            return item;
        }

        private static bool SinFallar(Func<bool> leer)
        {
            try
            {
                return leer();
            }
            catch (Exception ex)
            {
                Logger.Error("No se pudo leer el estado de Ghost Mode para el menú de la bandeja", ex);
                return false;
            }
        }

        private NOTIFYICONDATAW Datos(uint banderas) => new()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = ventana.Hwnd,
            uID = 1,
            uFlags = banderas,
            uCallbackMessage = WM_ICONO,
            hIcon = icono,
            szTip = "",
            szInfo = "",
            szInfoTitle = ""
        };

        private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";

        // El icono del propio .exe (Assets\ghostmode.ico, que el .csproj incrusta como icono de la aplicación),
        // al tamaño que pide Windows para la bandeja según la escala de la pantalla
        private static IntPtr CargarIcono()
        {
            string? exe = Environment.ProcessPath;
            if (exe != null && ExtractIconEx(exe, 0, out IntPtr grande, out IntPtr pequeno, 1) > 0 && pequeno != IntPtr.Zero)
            {
                DestroyIcon(grande);   // solo hace falta el pequeño
                return pequeno;
            }
            return LoadIcon(IntPtr.Zero, (IntPtr)32512);   // el icono genérico de aplicación de Windows
        }

        public void Dispose()
        {
            if (visible)
            {
                NOTIFYICONDATAW datos = Datos(0);
                Shell_NotifyIcon(NIM_DELETE, ref datos);
                visible = false;
            }
            if (Actual == this)
            {
                Actual = null;
            }
        }

        // ===== Funciones de Windows =====

        private const int WM_ICONO = 0x8000 + 1;   // WM_APP + 1: el mensaje que nos manda la bandeja
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
        private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
        private const uint NIIF_USER = 0x4, NIIF_LARGE_ICON = 0x20;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint mensaje, ref NOTIFYICONDATAW datos);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string archivo, int indice, out IntPtr grande, out IntPtr pequeno, uint cuantos);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icono);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr instancia, IntPtr nombre);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string nombre);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}
