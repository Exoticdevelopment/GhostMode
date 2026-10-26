using System.Runtime.InteropServices;   // DllImport
using System.Windows;
using System.Windows.Interop;            // Imaging: convertir un ícono de Windows en imagen de WPF
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // El ícono de un archivo tal como lo muestra el Explorador (SHGetFileInfo, de shell32)
    public static class IconoArchivo
    {
        // null si el archivo no existe o no tiene ícono: la pantalla pone uno genérico
        public static ImageSource? De(string ruta)
        {
            var info = new SHFILEINFO();
            IntPtr resultado = SHGetFileInfo(ruta, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (resultado == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                BitmapSource imagen = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                imagen.Freeze();   // Freeze = ya no cambia: se puede usar desde cualquier hilo
                return imagen;
            }
            catch (Exception ex)
            {
                Logger.Aviso($"No se pudo leer el ícono de {ruta}: {ex.Message}");
                return null;
            }
            finally
            {
                DestroyIcon(info.hIcon);   // el ícono de Windows ya se copió: hay que soltarlo
            }
        }

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string ruta, uint atributos, ref SHFILEINFO info, uint tamano, uint opciones);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr icono);
    }
}
