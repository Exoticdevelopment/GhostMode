using Microsoft.Win32;
using OptimizadorPC.Core;

namespace OptimizadorPC.Optimizaciones.Sistema
{
    // "Solución de problemas" de Ajustes: desactivar MPO (Multiplane Overlay).
    // NO da FPS: es el arreglo que publicó NVIDIA para parpadeos y tirones del escritorio o de apps en ventana
    // con algunos drivers (valor OverlayTestMode = 5). Afecta a todo el sistema y pide REINICIAR.
    // Hay informes de que Windows 11 reciente ya no lo respeta (ver docs/investigacion-fivem.md).
    // Apagado por defecto. Reversible: se guarda si el valor existía y cuál era (GestorEstado, módulo "mpo").
    public static class SolucionMpo
    {
        public const string Modulo = "mpo";
        public const string Ruta = @"SOFTWARE\Microsoft\Windows\Dwm";
        public const string Nombre = "OverlayTestMode";
        private const int Desactivado = 5;

        public static bool EstaDesactivado()
        {
            using RegistryKey? clave = Registry.LocalMachine.OpenSubKey(Ruta);
            return clave?.GetValue(Nombre) is int valor && valor == Desactivado;
        }

        public static void Desactivar()
        {
            if (EstaDesactivado())
            {
                return;
            }
            using RegistryKey clave = Registry.LocalMachine.CreateSubKey(Ruta);
            // null = no existía (lo normal): al revertir se borra
            GestorEstado.GuardarOriginal(Modulo, Nombre, clave.GetValue(Nombre) is int previo ? previo.ToString() : null);
            clave.SetValue(Nombre, Desactivado, RegistryValueKind.DWord);
            Logger.Info("MPO desactivado (OverlayTestMode = 5). Se aplica al reiniciar Windows");
        }

        public static void Revertir()
        {
            if (!GestorEstado.TieneOriginal(Modulo, Nombre))
            {
                return;
            }
            string? original = GestorEstado.ObtenerOriginal(Modulo, Nombre);
            using (RegistryKey clave = Registry.LocalMachine.CreateSubKey(Ruta))
            {
                if (original == null)
                {
                    clave.DeleteValue(Nombre, throwOnMissingValue: false);
                }
                else
                {
                    clave.SetValue(Nombre, int.Parse(original), RegistryValueKind.DWord);
                }
            }
            GestorEstado.Limpiar(Modulo);
            Logger.Info("MPO restaurado a como estaba. Se aplica al reiniciar Windows");
        }
    }
}
