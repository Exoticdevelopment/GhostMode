using System.Diagnostics;               // FileVersionInfo (editor del .exe)
using System.IO;
using System.Runtime.InteropServices;   // Marshal (liberar objetos COM)
using Microsoft.Win32;
using OptimizadorPC.Core;               // Logger, GestorEstado

namespace OptimizadorPC.Arranque
{
    // Lee los programas que arrancan con Windows y los habilita o deshabilita
    // igual que el Administrador de tareas: escribiendo en "StartupApproved", sin borrar nada.
    //
    // En StartupApproved cada programa tiene un valor binario de 12 bytes:
    //   02 00 00 00 00 00 00 00 00 00 00 00                → habilitado
    //   03 00 00 00 + 8 bytes con la fecha en que se apagó  → deshabilitado
    // (si el primer byte es impar, está deshabilitado). Si no hay valor, está habilitado.
    public static class GestorArranque
    {
        // Nombre del "cajón" en GestorEstado. Cada programa cambiado es una clave dentro
        public const string Modulo = "inicio-windows";

        private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string Aprobados = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

        // ===== Leer =====

        public static ListaArranque Listar()
        {
            var todos = new List<ElementoArranque>();
            int ocultos = 0;

            // Las 5 fuentes: (dónde está el registro o carpeta, dónde se aprueba, qué origen es)
            LeerRegistro(RegistryHive.CurrentUser, Run, Aprobados + @"\Run", OrigenArranque.RegistroUsuario);
            LeerRegistro(RegistryHive.LocalMachine, Run, Aprobados + @"\Run", OrigenArranque.RegistroEquipo);
            LeerRegistro(RegistryHive.LocalMachine, Run32, Aprobados + @"\Run32", OrigenArranque.RegistroEquipo32);
            LeerCarpeta(RegistryHive.CurrentUser, Environment.SpecialFolder.Startup, OrigenArranque.CarpetaUsuario);
            LeerCarpeta(RegistryHive.LocalMachine, Environment.SpecialFolder.CommonStartup, OrigenArranque.CarpetaEquipo);

            return new ListaArranque(todos.OrderBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList(), ocultos);

            // ---- Funciones locales: viven dentro de Listar y pueden usar "todos" y "ocultos" ----

            void LeerRegistro(RegistryHive hive, string ruta, string rutaAprobacion, OrigenArranque origen)
            {
                try
                {
                    using RegistryKey? clave = Abrir(hive, ruta, escribir: false);
                    if (clave == null)
                    {
                        return;
                    }
                    foreach (string nombre in clave.GetValueNames())
                    {
                        if (clave.GetValue(nombre) is not string comando || comando.Trim().Length == 0)
                        {
                            continue;
                        }
                        Agregar(nombre, comando, ExeDeComando(comando), origen, hive, rutaAprobacion, nombre);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"No se pudo leer {hive}\\{ruta}", ex);
                }
            }

            void LeerCarpeta(RegistryHive hive, Environment.SpecialFolder especial, OrigenArranque origen)
            {
                try
                {
                    string carpeta = Environment.GetFolderPath(especial);
                    if (!Directory.Exists(carpeta))
                    {
                        return;
                    }
                    foreach (string archivo in Directory.GetFiles(carpeta))
                    {
                        string archivoNombre = Path.GetFileName(archivo);
                        if (archivoNombre.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;   // archivo oculto de Windows, no es un programa
                        }
                        string? exe = archivo.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                            ? DestinoAccesoDirecto(archivo)
                            : archivo;
                        // En StartupFolder el valor se llama como el archivo: "Discord.lnk"
                        Agregar(Path.GetFileNameWithoutExtension(archivo), archivo, exe, origen,
                                hive, Aprobados + @"\StartupFolder", archivoNombre);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"No se pudo leer la carpeta Inicio {especial}", ex);
                }
            }

            void Agregar(string nombre, string comando, string? exe, OrigenArranque origen,
                         RegistryHive hive, string rutaAprobacion, string nombreAprobacion)
            {
                string editor = EditorDe(exe);
                if (FiltroCriticos.EsCritico(nombre, exe, editor))
                {
                    ocultos++;
                    return;
                }
                byte[]? aprobacion = LeerAprobacion(hive, rutaAprobacion, nombreAprobacion);
                todos.Add(new ElementoArranque(nombre, editor, origen, comando, EstaHabilitado(aprobacion),
                                               hive, rutaAprobacion, nombreAprobacion));
            }
        }

        // ===== Cambiar =====

        // Habilita o deshabilita un programa. Reversible:
        // - la primera vez se guarda en GestorEstado cómo estaba (o null si no había valor)
        // - si se deja otra vez como estaba al principio, se restaura ese original exacto y se olvida
        public static void CambiarEstado(ElementoArranque elemento, bool habilitar)
        {
            string clave = elemento.Clave;
            byte[]? actual = LeerAprobacion(elemento.Hive, elemento.RutaAprobacion, elemento.NombreAprobacion);

            if (GestorEstado.TieneOriginal(Modulo, clave))
            {
                string? original = GestorEstado.ObtenerOriginal(Modulo, clave);
                bool originalHabilitado = EstaHabilitado(original == null ? null : Convert.FromHexString(original));
                if (originalHabilitado == habilitar)
                {
                    Restaurar(clave);
                    GestorEstado.Olvidar(Modulo, clave);
                    Logger.Info($"Inicio de Windows: {elemento.Nombre} vuelve a como estaba ({(habilitar ? "habilitado" : "deshabilitado")})");
                    return;
                }
            }
            else
            {
                // Foto del "antes": el valor en hexadecimal, o null si no existía
                GestorEstado.GuardarOriginal(Modulo, clave, actual == null ? null : Convert.ToHexString(actual));
            }

            EscribirAprobacion(elemento.Hive, elemento.RutaAprobacion, elemento.NombreAprobacion, ValorPara(habilitar));
            Logger.Info($"Inicio de Windows: {elemento.Nombre} {(habilitar ? "habilitado" : "deshabilitado")}");
        }

        // "Deshacer todo": deja cada programa que tocamos como estaba antes de usar Ghost Mode
        public static void RestaurarTodo()
        {
            foreach (string clave in GestorEstado.ClavesGuardadas(Modulo))
            {
                Restaurar(clave);
            }
            GestorEstado.Limpiar(Modulo);
            Logger.Info("Inicio de Windows: todos los programas restaurados a como estaban");
        }

        private static void Restaurar(string clave)
        {
            var (hive, ruta, nombre) = SepararClave(clave);
            string? original = GestorEstado.ObtenerOriginal(Modulo, clave);
            if (original == null)
            {
                // No había valor: lo borramos para dejarlo igual (= habilitado, como estaba)
                using RegistryKey? k = Abrir(hive, ruta, escribir: true);
                k?.DeleteValue(nombre, throwOnMissingValue: false);
            }
            else
            {
                EscribirAprobacion(hive, ruta, nombre, Convert.FromHexString(original));
            }
        }

        // ===== Clave en GestorEstado: "HKCU|ruta|nombre" =====

        public static string ArmarClave(RegistryHive hive, string ruta, string nombre) =>
            $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}|{ruta}|{nombre}";

        private static (RegistryHive, string, string) SepararClave(string clave)
        {
            // Split con 3 = como mucho 3 trozos: si el nombre tuviera "|", se queda entero en el último
            string[] partes = clave.Split('|', 3);
            return (partes[0] == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, partes[1], partes[2]);
        }

        // ===== StartupApproved =====

        private static bool EstaHabilitado(byte[]? valor) =>
            valor == null || valor.Length == 0 || (valor[0] & 1) == 0;   // & 1 = "¿es impar?"

        private static byte[] ValorPara(bool habilitar)
        {
            var valor = new byte[12];   // 12 bytes a cero
            if (habilitar)
            {
                valor[0] = 0x02;
            }
            else
            {
                valor[0] = 0x03;
                // Bytes 4 a 11: la fecha en que se deshabilitó (el Administrador de tareas también la pone)
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(valor, 4);
            }
            return valor;
        }

        private static byte[]? LeerAprobacion(RegistryHive hive, string ruta, string nombre)
        {
            using RegistryKey? clave = Abrir(hive, ruta, escribir: false);
            return clave?.GetValue(nombre) as byte[];
        }

        private static void EscribirAprobacion(RegistryHive hive, string ruta, string nombre, byte[] valor)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey clave = baseKey.CreateSubKey(ruta);   // la crea si no existe
            clave.SetValue(nombre, valor, RegistryValueKind.Binary);
        }

        // Registry64 = ver el registro "de verdad", sin la redirección que Windows hace a los programas de 32 bits
        private static RegistryKey? Abrir(RegistryHive hive, string ruta, bool escribir)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            return baseKey.OpenSubKey(ruta, escribir);
        }

        // ===== Ayudantes: de un comando a un .exe y su editor =====

        // "C:\Program Files\Steam\steam.exe" -silent       → C:\Program Files\Steam\steam.exe
        // C:\Users\Ana\AppData\Local\Discord\Update.exe --processStart Discord.exe → ...\Update.exe
        // rundll32.exe algo.dll,Funcion                     → C:\Windows\System32\rundll32.exe
        public static string? ExeDeComando(string comando)
        {
            comando = Environment.ExpandEnvironmentVariables(comando.Trim());
            string ruta;

            if (comando.StartsWith('"'))
            {
                int cierre = comando.IndexOf('"', 1);
                ruta = cierre > 1 ? comando[1..cierre] : comando.Trim('"');
            }
            else
            {
                // Sin comillas: hasta el ".exe" (las rutas pueden tener espacios), o hasta el primer espacio
                int exe = comando.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                int espacio = comando.IndexOf(' ');
                ruta = exe >= 0 ? comando[..(exe + 4)] : espacio > 0 ? comando[..espacio] : comando;
            }

            if (Path.IsPathRooted(ruta))
            {
                return ruta;
            }
            // Solo el nombre ("rundll32.exe"): Windows lo busca en System32
            string enSistema = Path.Combine(Environment.SystemDirectory, ruta);
            return File.Exists(enSistema) ? enSistema : null;
        }

        // "Editor" = la empresa que firma el .exe (lo que el Explorador muestra en Propiedades > Detalles)
        private static string EditorDe(string? exe)
        {
            try
            {
                if (exe != null && File.Exists(exe))
                {
                    string? empresa = FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim();
                    if (!string.IsNullOrEmpty(empresa))
                    {
                        return empresa;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Aviso($"No se pudo leer el editor de {exe}: {ex.Message}");
            }
            return "Desconocido";
        }

        // A dónde apunta un acceso directo (.lnk). Usamos el objeto "WScript.Shell" que trae Windows.
        // dynamic = el compilador no revisa los métodos; se buscan al ejecutar (así funcionan los objetos COM)
        private static string? DestinoAccesoDirecto(string lnk)
        {
            object? shell = null;
            object? acceso = null;
            try
            {
                Type? tipo = Type.GetTypeFromProgID("WScript.Shell");
                if (tipo == null)
                {
                    return null;
                }
                shell = Activator.CreateInstance(tipo);
                acceso = ((dynamic)shell!).CreateShortcut(lnk);
                string destino = ((dynamic)acceso!).TargetPath;
                return string.IsNullOrEmpty(destino) ? null : destino;
            }
            catch (Exception ex)
            {
                Logger.Aviso($"No se pudo leer el acceso directo {lnk}: {ex.Message}");
                return null;
            }
            finally
            {
                // Los objetos COM no los limpia .NET solo: hay que soltarlos
                if (acceso != null)
                {
                    Marshal.FinalReleaseComObject(acceso);
                }
                if (shell != null)
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }
    }
}
