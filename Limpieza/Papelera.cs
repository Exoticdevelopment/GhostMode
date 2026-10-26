using System.Runtime.InteropServices;   // DllImport: llamar a funciones de Windows escritas en C
using OptimizadorPC.Core;               // Logger

namespace OptimizadorPC.Limpieza
{
    // La papelera de reciclaje de todas las unidades.
    // No se borran sus carpetas a mano ($Recycle.Bin): se le pide a Windows que la vacíe,
    // igual que con clic derecho > "Vaciar papelera".
    public class Papelera : ICategoriaLimpieza
    {
        public string Id => "papelera";
        public string Nombre => "Papelera de reciclaje";
        public string Descripcion => "Archivos eliminados de todas las unidades.";
        public bool MarcadaPorDefecto => true;

        public long Medir()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            // null = todas las unidades. Devuelve 0 si fue bien (un "HRESULT" de Windows)
            int resultado = SHQueryRecycleBin(null, ref info);
            if (resultado != 0)
            {
                throw new InvalidOperationException($"Windows no informó el tamaño de la papelera (código 0x{resultado:X8})");
            }
            return info.i64Size;
        }

        public ResultadoLimpieza Limpiar()
        {
            long antes = Medir();
            if (antes == 0)
            {
                return ResultadoLimpieza.Vacio;
            }

            // Sin preguntar (ya preguntó Ghost Mode), sin barra de progreso y sin sonido
            int resultado = SHEmptyRecycleBin(IntPtr.Zero, null, SinConfirmacion | SinProgreso | SinSonido);
            if (resultado != 0)
            {
                throw new InvalidOperationException($"Windows no pudo vaciar la papelera (código 0x{resultado:X8})");
            }

            // Lo liberado = lo que había menos lo que quede (si algo no se pudo borrar)
            long liberados = antes - Medir();
            Logger.Info($"Papelera vaciada: {liberados / 1048576} MB liberados");
            return new ResultadoLimpieza(liberados, 0);
        }

        // ---------- Funciones de Windows (shell32.dll) ----------

        private const uint SinConfirmacion = 0x1;   // SHERB_NOCONFIRMATION
        private const uint SinProgreso = 0x2;       // SHERB_NOPROGRESSUI
        private const uint SinSonido = 0x4;         // SHERB_NOSOUND

        // La "ficha" que rellena Windows: tamaño en bytes y número de elementos
        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;         // Windows exige que le digamos el tamaño de la ficha
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
    }
}
