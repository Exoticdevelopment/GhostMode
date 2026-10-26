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
                // 1. Foto del "antes". Si el valor no existía, se guarda null.
                //    También su tipo: puede no ser el que usamos nosotros (p. ej. un texto donde ponemos un número)
                //    y al revertir hay que dejarlo con el tipo de antes
                if (!GestorEstado.TieneOriginal(modulo, Clave(a)))
                {
                    RegistryValueKind? tipo = LeerTipo(a.Ruta, a.Nombre);
                    if (tipo != null)
                    {
                        GestorEstado.GuardarOriginal(modulo, ClaveTipo(a), tipo.ToString());
                    }
                    GestorEstado.GuardarOriginal(modulo, Clave(a), Leer(a.Ruta, a.Nombre));
                }

                // 2. Si la clave (la "carpeta" del registro) no existe, Escribir la creará:
                //    anotamos cuál fue la primera que faltaba para quitarla al revertir
                string? creada = PrimeraClaveQueFalta(a.Ruta);
                if (creada != null)
                {
                    GestorEstado.GuardarOriginal(modulo, PrefijoCreada + creada, null);
                }

                // 3. Escribir el valor nuevo
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
                    // Con el tipo que tenía antes. Las fotos de versiones anteriores no lo guardaban:
                    // en ese caso se usa el de la app, como antes
                    RegistryValueKind tipo = Enum.TryParse(GestorEstado.ObtenerOriginal(modulo, ClaveTipo(a)), out RegistryValueKind t)
                        ? t
                        : a.Tipo;
                    Escribir(a.Ruta, a.Nombre, original, tipo);
                    Logger.Info($"Registro: {Clave(a)} restaurado a {original}");
                }
            }

            // Las claves que creamos al aplicar se quitan, pero solo si quedaron vacías del todo
            // (si otro programa guardó algo ahí después, se dejan)
            foreach (string clave in GestorEstado.ClavesGuardadas(modulo).Where(c => c.StartsWith(PrefijoCreada)))
            {
                string ruta = clave[PrefijoCreada.Length..];
                if (ArbolVacio(ruta))
                {
                    Registry.CurrentUser.DeleteSubKeyTree(ruta, throwOnMissingSubKey: false);
                    Logger.Info($"Registro: clave {ruta} quitada (no existía)");
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
            // ?. = si clave es null, no sigue y da null.
            // DoNotExpand: un texto como "%USERPROFILE%\x" se guarda tal cual, no ya traducido a C:\Users\...
            object? valor = clave?.GetValue(nombre, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

            return valor switch
            {
                // Los valores binarios (byte[]) los pasamos a texto hexadecimal: "9012038010"
                byte[] bytes => Convert.ToHexString(bytes),
                // Varias líneas de texto (REG_MULTI_SZ): las unimos con el carácter nulo, que no puede ir dentro
                string[] lineas => string.Join('\0', lineas),
                _ => valor?.ToString()
            };
        }

        // El tipo del valor (DWord, String, Binary...), o null si no existe
        public static RegistryValueKind? LeerTipo(string ruta, string nombre)
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(ruta);
            if (clave?.GetValue(nombre) == null)
            {
                return null;
            }
            return clave.GetValueKind(nombre);
        }

        public static void Escribir(string ruta, string nombre, string valor, RegistryValueKind tipo)
        {
            // CreateSubKey abre la clave, o la crea si no existe
            using RegistryKey clave = Registry.CurrentUser.CreateSubKey(ruta);

            // Convertimos el texto al tipo que espera el registro
            object dato = tipo switch
            {
                RegistryValueKind.DWord => int.Parse(valor),
                RegistryValueKind.QWord => long.Parse(valor),
                RegistryValueKind.Binary => Convert.FromHexString(valor),
                RegistryValueKind.MultiString => valor.Length == 0 ? Array.Empty<string>() : valor.Split('\0'),
                _ => valor   // _ = "cualquier otro caso" (texto normal)
            };
            clave.SetValue(nombre, dato, tipo);
        }

        public static void Borrar(string ruta, string nombre)
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(ruta, writable: true);
            clave?.DeleteValue(nombre, throwOnMissingValue: false);   // si ya no está, no pasa nada
        }

        // ---------- Claves que la app crea ----------

        // La primera "carpeta" de la ruta que no existe, o null si existe entera.
        // Ej.: si existe Software\Microsoft pero no Software\Microsoft\DirectX, devuelve Software\Microsoft\DirectX
        private static string? PrimeraClaveQueFalta(string ruta)
        {
            string[] partes = ruta.Split('\\');
            for (int i = 1; i <= partes.Length; i++)
            {
                string parcial = string.Join('\\', partes[..i]);
                using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(parcial);
                if (clave == null)
                {
                    return parcial;
                }
            }
            return null;
        }

        // ¿La clave y todas sus subclaves están sin ningún valor? (si ya no existe, cuenta como vacía)
        private static bool ArbolVacio(string ruta)
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(ruta);
            return clave == null ||
                   (clave.ValueCount == 0 && clave.GetSubKeyNames().All(sub => ArbolVacio($@"{ruta}\{sub}")));
        }

        // ---------- Nombres dentro de GestorEstado ----------

        // Nombre único de cada ajuste dentro de GestorEstado: "Ruta\Nombre"
        private static string Clave(AjusteRegistro a) => $@"{a.Ruta}\{a.Nombre}";

        // Tipo original del ajuste: "tipo:Ruta\Nombre" → "DWord", "String"...
        private static string ClaveTipo(AjusteRegistro a) => "tipo:" + Clave(a);

        // Clave del registro que no existía y creamos: "creada:Ruta"
        private const string PrefijoCreada = "creada:";
    }
}
