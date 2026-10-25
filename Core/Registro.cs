using Microsoft.Win32;
// Microsoft.Win32 = herramientas de .NET para leer y escribir el registro de Windows

namespace OptimizadorPC.Core
{
    // Un cambio en el registro: "en esta Ruta, pon el valor Nombre = Valor".
    // record = clase pequeña que solo guarda datos. Se crea así:
    //   new AjusteRegistro(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", "1")
    // Si no se indica Tipo, se asume DWord (número entero, el más común).
    public record AjusteRegistro(
        string Ruta,
        string Nombre,
        string Valor,
        RegistryValueKind Tipo = RegistryValueKind.DWord);

    // Ayudante para los módulos que tocan el registro (GameMode, EfectosVisuales...).
    // Trabaja siempre en HKEY_CURRENT_USER: la configuración del usuario actual.
    public static class Registro
    {
        // ---------- Lo que usan los módulos ----------

        // ¿Están TODOS los ajustes ya puestos? (para EstaActiva)
        public static bool EstanAplicados(IEnumerable<AjusteRegistro> ajustes)
        {
            // All = "¿todos cumplen la condición?"
            return ajustes.All(a => Leer(a.Ruta, a.Nombre) == a.Valor);
        }

        public static void Aplicar(string modulo, IEnumerable<AjusteRegistro> ajustes)
        {
            foreach (var a in ajustes)
            {
                // 1. Foto del "antes". Si el valor no existía, se guarda null
                GestorEstado.GuardarOriginal(modulo, Clave(a), Leer(a.Ruta, a.Nombre));

                // 2. Escribir el valor nuevo
                Escribir(a.Ruta, a.Nombre, a.Valor, a.Tipo);
                Logger.Info($"Registro: {Clave(a)} = {a.Valor}");
            }
        }

        public static void Revertir(string modulo, IEnumerable<AjusteRegistro> ajustes)
        {
            foreach (var a in ajustes)
            {
                // Sin foto no sabemos cómo estaba: mejor no tocar nada
                if (!GestorEstado.TieneOriginal(modulo, Clave(a)))
                {
                    Logger.Aviso($"Sin valor original para {Clave(a)}, no se revierte");
                    continue;   // salta al siguiente ajuste
                }

                string? original = GestorEstado.ObtenerOriginal(modulo, Clave(a));

                if (original == null)
                {
                    // El valor no existía antes de aplicar: lo borramos para dejarlo igual
                    Borrar(a.Ruta, a.Nombre);
                    Logger.Info($"Registro: {Clave(a)} borrado (no existía)");
                }
                else
                {
                    Escribir(a.Ruta, a.Nombre, original, a.Tipo);
                    Logger.Info($"Registro: {Clave(a)} restaurado a {original}");
                }
            }

            GestorEstado.Limpiar(modulo);   // ya no necesitamos las fotos
        }

        // ---------- Lectura y escritura "en crudo" ----------

        // Devuelve el valor como texto, o null si no existe.
        // Guardamos todo como texto porque GestorEstado solo guarda strings.
        public static string? Leer(string ruta, string nombre)
        {
            // using = cierra la clave automáticamente al terminar
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(ruta);
            object? valor = clave?.GetValue(nombre);   // ?. = si clave es null, no sigue y da null

            // Los valores binarios (byte[]) los pasamos a texto hexadecimal: "9012038010"
            if (valor is byte[] bytes)
            {
                return Convert.ToHexString(bytes);
            }
            return valor?.ToString();
        }

        public static void Escribir(string ruta, string nombre, string valor, RegistryValueKind tipo)
        {
            // CreateSubKey abre la clave, o la crea si no existe
            using RegistryKey clave = Registry.CurrentUser.CreateSubKey(ruta);

            // Convertimos el texto al tipo que espera el registro
            object dato = tipo switch
            {
                RegistryValueKind.DWord => int.Parse(valor),
                RegistryValueKind.Binary => Convert.FromHexString(valor),
                _ => valor   // _ = "cualquier otro caso" (texto normal)
            };
            clave.SetValue(nombre, dato, tipo);
        }

        public static void Borrar(string ruta, string nombre)
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(ruta, writable: true);
            clave?.DeleteValue(nombre, throwOnMissingValue: false);   // si ya no está, no pasa nada
        }

        // Nombre único de cada ajuste dentro de GestorEstado: "Ruta\Nombre"
        private static string Clave(AjusteRegistro a) => $@"{a.Ruta}\{a.Nombre}";
    }
}
