using System.Runtime.InteropServices;   // DllImport
using System.Windows.Input;              // Key, ModifierKeys, KeyInterop
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    // Una combinación de teclas: Ctrl+Alt+G
    public record Combinacion(ModifierKeys Modificadores, Key Tecla)
    {
        // Los modificadores en el orden en que se escriben, con su valor para RegisterHotKey
        public static readonly (ModifierKeys Tecla, string Nombre, uint Windows)[] ModificadoresConocidos =
        [
            (ModifierKeys.Control, "Ctrl", 0x2),
            (ModifierKeys.Alt, "Alt", 0x1),
            (ModifierKeys.Shift, "Shift", 0x4),
            (ModifierKeys.Windows, "Win", 0x8)
        ];

        // "Ctrl+Alt+G", en el orden de siempre
        public override string ToString()
        {
            IEnumerable<string> mods = ModificadoresConocidos.Where(m => Modificadores.HasFlag(m.Tecla)).Select(m => m.Nombre);
            return string.Join("+", mods.Append(NombreTecla(Tecla)));
        }

        // D5 → "5", NumPad5 → "Num5", el resto como se llama (F9, G, Insert...)
        private static string NombreTecla(Key k) => k switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(k - Key.NumPad0),
            _ => k.ToString()
        };

        // Texto → combinación. null si no es válida. Reglas: al menos Ctrl, Alt o Win (con solo Shift se
        // dispararía al escribir) y una tecla normal que no sea un modificador
        public static Combinacion? Leer(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            ModifierKeys mods = ModifierKeys.None;
            Key? tecla = null;
            foreach (string parte in texto.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                switch (parte.ToLowerInvariant())
                {
                    case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                    case "alt": mods |= ModifierKeys.Alt; break;
                    case "shift" or "mayús": mods |= ModifierKeys.Shift; break;
                    case "win": mods |= ModifierKeys.Windows; break;
                    default:
                        if (tecla != null || DeTexto(parte) is not Key k)
                        {
                            return null;
                        }
                        tecla = k;
                        break;
                }
            }
            return Crear(mods, tecla ?? Key.None);
        }

        // Lo que se pulsa en el cuadro de Ajustes → combinación (null si aún no es válida)
        public static Combinacion? Crear(ModifierKeys mods, Key tecla)
        {
            bool esModificador = tecla is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                                 or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
            bool conModificadorFuerte = (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
            return esModificador || !conModificadorFuerte ? null : new Combinacion(mods, tecla);
        }

        private static Key? DeTexto(string parte)
        {
            if (parte.Length == 1 && char.IsDigit(parte[0]))
            {
                return Key.D0 + (parte[0] - '0');
            }
            if (parte.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && parte.Length == 4 && char.IsDigit(parte[3]))
            {
                return Key.NumPad0 + (parte[3] - '0');
            }
            return Enum.TryParse(parte, ignoreCase: true, out Key k) && !int.TryParse(parte, out _) ? k : null;
        }
    }

    // El atajo de teclado global (funciona aunque Ghost Mode esté en la bandeja o se esté jugando),
    // con RegisterHotKey de Windows: no lee el teclado ni registra lo que se escribe, solo recibe ESA combinación
    public sealed class AtajoGlobal : IDisposable
    {
        private const int Id = 0x4748;   // "GH": cualquier número fijo de esta app
        private const int WM_HOTKEY = 0x0312;

        private readonly VentanaMensajes ventana;
        private bool registrado;

        public event Action? Pulsado;
        public Combinacion? Actual { get; private set; }

        public AtajoGlobal(VentanaMensajes ventana)
        {
            this.ventana = ventana;
            ventana.Escuchar(AlRecibirMensaje);
        }

        // Cambia el atajo. Devuelve false si Windows no lo deja (otra app ya usa esa combinación)
        public bool Registrar(Combinacion? combinacion)
        {
            Quitar();
            if (combinacion == null)
            {
                return true;
            }

            // MOD_NOREPEAT: mantener pulsado no lo dispara en bucle
            uint mods = Combinacion.ModificadoresConocidos.Where(m => combinacion.Modificadores.HasFlag(m.Tecla))
                                                  .Aggregate(MOD_NOREPEAT, (total, m) => total | m.Windows);

            registrado = RegisterHotKey(ventana.Hwnd, Id, mods, (uint)KeyInterop.VirtualKeyFromKey(combinacion.Tecla));
            if (registrado)
            {
                Actual = combinacion;
                Logger.Info($"Atajo global: {combinacion}");
            }
            else
            {
                Logger.Aviso($"No se pudo registrar el atajo {combinacion}: otra app ya lo usa");
            }
            return registrado;
        }

        private void Quitar()
        {
            if (registrado)
            {
                UnregisterHotKey(ventana.Hwnd, Id);
                registrado = false;
            }
            Actual = null;
        }

        private IntPtr AlRecibirMensaje(IntPtr hwnd, int mensaje, IntPtr wParam, IntPtr lParam, ref bool atendido)
        {
            if (mensaje == WM_HOTKEY && (int)wParam == Id)
            {
                atendido = true;
                Pulsado?.Invoke();
            }
            return IntPtr.Zero;
        }

        public void Dispose() => Quitar();

        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint tecla);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
