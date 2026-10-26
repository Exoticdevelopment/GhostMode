using System.IO;
using System.Runtime.InteropServices;   // NativeLibrary, Marshal
using OptimizadorPC.Core;

namespace OptimizadorPC.Optimizaciones.FiveM
{
    // "Modo de control de energía: preferir máximo rendimiento" para los .exe de FiveM, solo durante la partida.
    // Es el mismo ajuste del Panel de control de NVIDIA, escrito con NVAPI (DRS = Driver Settings), la API oficial
    // de NVIDIA (SDK con licencia MIT: github.com/NVIDIA/nvapi). La implementación la trae el driver: nvapi64.dll.
    //
    // Cómo se aplica sin pisar nada del usuario:
    //   - si el .exe ya pertenece a un perfil (de NVIDIA o del usuario): se cambia SOLO ese ajuste en ese perfil,
    //     guardando antes su valor (o que no tenía valor propio, para volver al de NVIDIA)
    //   - si no: se crea un perfil propio "Ghost Mode" con esos .exe, que al revertir se borra entero
    // Todo queda en GestorEstado (módulo "nvidia-energia") antes de cambiarlo.
    //
    // El driver lee el perfil al ARRANCAR cada proceso: por eso se aplica al abrir FiveM.exe (el lanzador),
    // antes de que arranque el proceso del juego (FiveM_..._GTAProcess.exe).
    public sealed class PerfilNvidia
    {
        public const string Modulo = "nvidia-energia";
        public const string NombrePerfil = "Ghost Mode";

        // Del SDK (NvApiDriverSettings.h): PREFERRED_PSTATE = "Power management mode"
        public const uint AjusteEnergia = 0x1057EB71;
        public const uint PreferirMaximo = 0x1;

        // Solo la copia que instala el driver en System32, con la ruta completa (como nvml.dll)
        public static readonly string RutaOficial = Path.Combine(Environment.SystemDirectory, "nvapi64.dll");

        private static readonly Lazy<PerfilNvidia> sistema = new(() => new PerfilNvidia(RutaOficial));
        public static PerfilNvidia Sistema => sistema.Value;

        private readonly object candado = new();
        private readonly Funciones? f;

        public bool Disponible => f != null;

        public PerfilNvidia(string ruta)
        {
            f = Cargar(ruta);
        }

        // ===== Aplicar y revertir =====

        // Devuelve true si cambió algo. Lanza InvalidOperationException si NVAPI devuelve un error
        public bool Aplicar(IEnumerable<string> exes)
        {
            if (f == null)
            {
                return false;
            }
            List<string> nombres = exes.Select(e => Path.GetFileName(e).ToLowerInvariant()).Distinct().ToList();
            bool cambio = false;

            lock (candado)
            {
                ConSesion(sesion =>
                {
                    IntPtr propio = IntPtr.Zero;
                    foreach (string exe in nombres)
                    {
                        IntPtr perfil;
                        int estado = BuscarApp(sesion, exe, out perfil);
                        if (estado == EXECUTABLE_NOT_FOUND)
                        {
                            // Sin perfil: al nuestro (se crea la primera vez)
                            propio = propio != IntPtr.Zero ? propio : PerfilPropio(sesion);
                            Comprobar(CrearApp(sesion, propio, exe), $"agregar {exe} al perfil");
                            GestorEstado.GuardarOriginal(Modulo, "app:" + exe, NombrePerfil);
                            perfil = propio;
                        }
                        else
                        {
                            Comprobar(estado, $"buscar {exe}");
                        }

                        string nombrePerfil = NombreDe(sesion, perfil);
                        (bool propioDelUsuario, uint valor) = LeerAjuste(sesion, perfil);
                        if (propioDelUsuario && valor == PreferirMaximo)
                        {
                            continue;   // ya estaba así: nada que guardar ni cambiar
                        }
                        // "predefinido" = sin valor propio: al revertir se borra y vuelve el de NVIDIA
                        GestorEstado.GuardarOriginal(Modulo, "perfil:" + nombrePerfil, propioDelUsuario ? valor.ToString() : "predefinido");
                        EscribirAjuste(sesion, perfil, PreferirMaximo);
                        cambio = true;
                        Logger.Info($"NVIDIA: «preferir máximo rendimiento» para {exe} (perfil «{nombrePerfil}»)");
                    }
                    if (cambio)
                    {
                        Comprobar(f.SaveSettings(sesion), "guardar los ajustes del driver");
                    }
                });
            }
            return cambio;
        }

        // Deja cada perfil como estaba y borra el propio si se creó
        public void Revertir()
        {
            if (f == null || !GestorEstado.TieneCambios(Modulo))
            {
                return;
            }
            lock (candado)
            {
                ConSesion(sesion =>
                {
                    foreach (string clave in GestorEstado.ClavesGuardadas(Modulo).Where(c => c.StartsWith("perfil:")))
                    {
                        string nombre = clave["perfil:".Length..];
                        string? original = GestorEstado.ObtenerOriginal(Modulo, clave);
                        if (nombre == NombrePerfil || BuscarPerfil(sesion, nombre, out IntPtr perfil) != OK)
                        {
                            continue;   // el propio se borra entero más abajo; uno que ya no existe no se toca
                        }
                        if (original == "predefinido")
                        {
                            int r = f.DeleteProfileSetting(sesion, perfil, AjusteEnergia);
                            if (r != SETTING_NOT_FOUND)
                            {
                                Comprobar(r, $"devolver «{nombre}» al valor de NVIDIA");
                            }
                        }
                        else if (uint.TryParse(original, out uint valor))
                        {
                            EscribirAjuste(sesion, perfil, valor);
                        }
                        Logger.Info($"NVIDIA: modo de energía del perfil «{nombre}» restaurado");
                    }

                    if (GestorEstado.TieneOriginal(Modulo, "creado:" + NombrePerfil) &&
                        BuscarPerfil(sesion, NombrePerfil, out IntPtr propio) == OK)
                    {
                        Comprobar(f.DeleteProfile(sesion, propio), "borrar el perfil «Ghost Mode»");
                        Logger.Info("NVIDIA: perfil «Ghost Mode» borrado");
                    }
                    Comprobar(f.SaveSettings(sesion), "guardar los ajustes del driver");
                });
            }
            GestorEstado.Limpiar(Modulo);
        }

        // Para las pruebas y el diagnóstico: el modo de energía con el que arrancaría ese .exe
        // (null = el .exe no está en ningún perfil). propio = false si es el valor de fábrica de NVIDIA
        public (bool Propio, uint Valor)? Leer(string exe)
        {
            if (f == null)
            {
                return null;
            }
            (bool, uint)? resultado = null;
            lock (candado)
            {
                ConSesion(sesion =>
                {
                    if (BuscarApp(sesion, Path.GetFileName(exe).ToLowerInvariant(), out IntPtr perfil) == OK)
                    {
                        resultado = LeerAjuste(sesion, perfil);
                    }
                });
            }
            return resultado;
        }

        public bool ExistePerfilPropio()
        {
            bool existe = false;
            if (f != null)
            {
                lock (candado)
                {
                    ConSesion(sesion => existe = BuscarPerfil(sesion, NombrePerfil, out _) == OK);
                }
            }
            return existe;
        }

        // ===== Pasos con NVAPI =====

        private void ConSesion(Action<IntPtr> trabajo)
        {
            Comprobar(f!.CreateSession(out IntPtr sesion), "abrir una sesión de ajustes del driver");
            try
            {
                Comprobar(f.LoadSettings(sesion), "leer los ajustes del driver");
                trabajo(sesion);
            }
            finally
            {
                f.DestroySession(sesion);
            }
        }

        private IntPtr PerfilPropio(IntPtr sesion)
        {
            if (BuscarPerfil(sesion, NombrePerfil, out IntPtr perfil) == OK)
            {
                return perfil;
            }
            using var datos = new Bloque(TamPerfil);
            datos.Escribir(0, Version(TamPerfil, 1));
            datos.EscribirTexto(4, NombrePerfil);
            Comprobar(f!.CreateProfile(sesion, datos.Puntero, out perfil), "crear el perfil «Ghost Mode»");
            GestorEstado.GuardarOriginal(Modulo, "creado:" + NombrePerfil, "1");
            Logger.Info("NVIDIA: perfil «Ghost Mode» creado");
            return perfil;
        }

        private int BuscarApp(IntPtr sesion, string exe, out IntPtr perfil)
        {
            using var nombre = Bloque.Texto(exe);
            using var app = new Bloque(TamApp);
            app.Escribir(0, Version(TamApp, 1));
            return f!.FindApplicationByName(sesion, nombre.Puntero, out perfil, app.Puntero);
        }

        private int CrearApp(IntPtr sesion, IntPtr perfil, string exe)
        {
            using var app = new Bloque(TamApp);
            app.Escribir(0, Version(TamApp, 1));
            app.EscribirTexto(8, exe);   // appName va después de version e isPredefined
            return f!.CreateApplication(sesion, perfil, app.Puntero);
        }

        private int BuscarPerfil(IntPtr sesion, string nombre, out IntPtr perfil)
        {
            using var texto = Bloque.Texto(nombre);
            return f!.FindProfileByName(sesion, texto.Puntero, out perfil);
        }

        private string NombreDe(IntPtr sesion, IntPtr perfil)
        {
            using var datos = new Bloque(TamPerfil);
            datos.Escribir(0, Version(TamPerfil, 1));
            Comprobar(f!.GetProfileInfo(sesion, perfil, datos.Puntero), "leer el nombre del perfil");
            return datos.LeerTexto(4);
        }

        // (¿valor propio del usuario?, valor). Sin ningún valor (ni de NVIDIA) cuenta como "adaptable" (0)
        private (bool Propio, uint Valor) LeerAjuste(IntPtr sesion, IntPtr perfil)
        {
            using var ajuste = new Bloque(TamAjuste);
            ajuste.Escribir(0, Version(TamAjuste, 1));
            int r = f!.GetSetting(sesion, perfil, AjusteEnergia, ajuste.Puntero);
            if (r == SETTING_NOT_FOUND)
            {
                return (false, 0);
            }
            Comprobar(r, "leer el modo de energía");
            bool predefinido = ajuste.Leer(OffsetEsPredefinido) != 0;
            return (!predefinido, ajuste.Leer(OffsetValorActual));
        }

        private void EscribirAjuste(IntPtr sesion, IntPtr perfil, uint valor)
        {
            using var ajuste = new Bloque(TamAjuste);
            ajuste.Escribir(0, Version(TamAjuste, 1));
            ajuste.Escribir(OffsetId, AjusteEnergia);
            ajuste.Escribir(OffsetTipo, 0);   // NVDRS_DWORD_TYPE
            ajuste.Escribir(OffsetValorActual, valor);
            Comprobar(f!.SetSetting(sesion, perfil, ajuste.Puntero), "cambiar el modo de energía");
        }

        private static void Comprobar(int estado, string que)
        {
            if (estado != OK)
            {
                throw new InvalidOperationException($"NVAPI no pudo {que} (código {estado})");
            }
        }

        // ===== Estructuras del SDK (nvapi.h), en bytes =====
        // Se arman a mano en memoria nativa para no depender de cómo .NET ordena un struct tan grande.
        // NvAPI_UnicodeString = 2048 caracteres UTF-16 = 4096 bytes.
        private const int LargoTexto = 4096;
        private const int TamPerfil = 4 + LargoTexto + 4 + 4 + 4 + 4;   // NVDRS_PROFILE_V1
        private const int TamApp = 4 + 4 + LargoTexto * 3;              // NVDRS_APPLICATION_V1
        // NVDRS_SETTING_V1 (#pragma pack 4): version, settingName, settingId, settingType, settingLocation,
        // isCurrentPredefined, isPredefinedValid, union predefinido (4100 bytes), union actual (4100 bytes)
        private const int OffsetId = 4 + LargoTexto;
        private const int OffsetTipo = OffsetId + 4;
        private const int OffsetEsPredefinido = OffsetTipo + 8;
        private const int OffsetValorPredefinido = OffsetEsPredefinido + 8;
        private const int OffsetValorActual = OffsetValorPredefinido + 4100;
        private const int TamAjuste = OffsetValorActual + 4100;

        private static uint Version(int tamano, int version) => (uint)tamano | ((uint)version << 16);

        private const int OK = 0;
        private const int SETTING_NOT_FOUND = -160;
        private const int EXECUTABLE_NOT_FOUND = -166;

        // Memoria nativa a cero que se libera sola al terminar (using)
        private sealed class Bloque : IDisposable
        {
            public IntPtr Puntero { get; }
            private readonly int tamano;

            public Bloque(int tamano)
            {
                this.tamano = tamano;
                Puntero = Marshal.AllocHGlobal(tamano);
                Marshal.Copy(new byte[tamano], 0, Puntero, tamano);
            }

            public static Bloque Texto(string texto)
            {
                var b = new Bloque(LargoTexto);
                b.EscribirTexto(0, texto);
                return b;
            }

            public void Escribir(int offset, uint valor) => Marshal.WriteInt32(Puntero, offset, unchecked((int)valor));
            public uint Leer(int offset) => unchecked((uint)Marshal.ReadInt32(Puntero, offset));

            public void EscribirTexto(int offset, string texto)
            {
                char[] letras = texto.Length >= 2047 ? texto[..2047].ToCharArray() : texto.ToCharArray();
                Marshal.Copy(letras, 0, Puntero + offset, letras.Length);   // el resto ya es cero: termina en \0
            }

            public string LeerTexto(int offset) => Marshal.PtrToStringUni(Puntero + offset) ?? "";

            public void Dispose() => Marshal.FreeHGlobal(Puntero);
        }

        // ===== Carga de nvapi64.dll =====

        private sealed record Funciones(
            CrearSesionFn CreateSession, SesionFn DestroySession, SesionFn LoadSettings, SesionFn SaveSettings,
            BuscarAppFn FindApplicationByName, CrearPerfilFn CreateProfile, BuscarPerfilFn FindProfileByName,
            CrearAppFn CreateApplication, AjusteFn SetSetting, LeerAjusteFn GetSetting, BorrarAjusteFn DeleteProfileSetting,
            PerfilFn DeleteProfile, InfoPerfilFn GetProfileInfo);

        // Si nvapi64.dll no está (sin GPU NVIDIA) o algo falla, la app sigue sin este ajuste
        private static Funciones? Cargar(string ruta)
        {
            if (!File.Exists(ruta) || !NativeLibrary.TryLoad(ruta, out IntPtr dll))
            {
                Logger.Info("NVIDIA: nvapi64.dll no está (sin GPU NVIDIA): el ajuste de energía no se usa");
                return null;
            }
            try
            {
                if (!NativeLibrary.TryGetExport(dll, "nvapi_QueryInterface", out IntPtr consultar))
                {
                    throw new InvalidOperationException("no es una NVAPI válida");
                }
                var query = Marshal.GetDelegateForFunctionPointer<QueryFn>(consultar);
                T Funcion<T>(uint id) where T : Delegate
                {
                    IntPtr p = query(id);
                    return p != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(p)
                        : throw new InvalidOperationException($"falta la función 0x{id:X8}");
                }

                // Identificadores de nvapi_interface.h del SDK oficial
                Comprobar(Funcion<InicioFn>(0x0150E828)(), "iniciarse");
                var funciones = new Funciones(
                    Funcion<CrearSesionFn>(0x0694D52E), Funcion<SesionFn>(0xDAD9CFF8),
                    Funcion<SesionFn>(0x375DBD6B), Funcion<SesionFn>(0xFCBC7E14),
                    Funcion<BuscarAppFn>(0xEEE566B2), Funcion<CrearPerfilFn>(0xCC176068),
                    Funcion<BuscarPerfilFn>(0x7E4A9A0B), Funcion<CrearAppFn>(0x4347A9DE),
                    Funcion<AjusteFn>(0x577DD202), Funcion<LeerAjusteFn>(0x73BF8338),
                    Funcion<BorrarAjusteFn>(0xE4A26362), Funcion<PerfilFn>(0x17093206),
                    Funcion<InfoPerfilFn>(0x61CD6FD6));
                Logger.Info("NVIDIA: NVAPI lista para el perfil de energía");
                return funciones;
            }
            catch (Exception ex)
            {
                Logger.Aviso($"NVIDIA: no se pudo usar {ruta}: {ex.Message}");
                NativeLibrary.Free(dll);
                return null;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr QueryFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InicioFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CrearSesionFn(out IntPtr sesion);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SesionFn(IntPtr sesion);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BuscarAppFn(IntPtr sesion, IntPtr nombre, out IntPtr perfil, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CrearPerfilFn(IntPtr sesion, IntPtr datos, out IntPtr perfil);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BuscarPerfilFn(IntPtr sesion, IntPtr nombre, out IntPtr perfil);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CrearAppFn(IntPtr sesion, IntPtr perfil, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AjusteFn(IntPtr sesion, IntPtr perfil, IntPtr ajuste);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LeerAjusteFn(IntPtr sesion, IntPtr perfil, uint id, IntPtr ajuste);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BorrarAjusteFn(IntPtr sesion, IntPtr perfil, uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PerfilFn(IntPtr sesion, IntPtr perfil);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InfoPerfilFn(IntPtr sesion, IntPtr perfil, IntPtr datos);
    }
}
