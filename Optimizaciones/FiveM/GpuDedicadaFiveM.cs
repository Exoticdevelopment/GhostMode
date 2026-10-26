using Microsoft.Win32;      // RegistryValueKind
using OptimizadorPC.Core;   // IOptimizacion, Registro, AjusteRegistro, GestorEstado, Logger

namespace OptimizadorPC.Optimizaciones.FiveM
{
    // Le dice a Windows que FiveM use la GPU de "alto rendimiento" (la dedicada, p. ej. una RTX)
    // en vez de la integrada del procesador. En portátiles con dos GPU es lo que más se nota.
    // Es lo mismo que hacer a mano: Configuración > Pantalla > Gráficos > FiveM > Alto rendimiento.
    public class GpuDedicadaFiveM : IOptimizacion
    {
        public string Id => "fivem-gpu";
        public string Nombre => "GPU dedicada para FiveM";
        public string Descripcion => "Obliga a FiveM a usar la tarjeta gráfica de alto rendimiento en vez de la integrada. " +
                                     "Se nota sobre todo en portátiles. Se aplica al volver a abrir FiveM.";
        public Categoria Categoria => Categoria.FiveM;
        public Impacto Impacto => Impacto.Alto;
        public Riesgo Riesgo => Riesgo.Seguro;

        // Aquí guarda Windows la preferencia de GPU de cada programa.
        // Nombre del valor = ruta completa del .exe; dato = texto tipo "GpuPreference=2;"
        // (0 = que decida Windows, 1 = ahorro de energía, 2 = alto rendimiento)
        private const string Ruta = @"Software\Microsoft\DirectX\UserGpuPreferences";
        private const string Preferencia = "GpuPreference=2";

        public bool EstaActiva()
        {
            string? exe = UbicacionFiveM.BuscarExe();
            if (exe == null)
            {
                return false;   // sin FiveM no hay nada aplicado
            }
            return UbicacionFiveM.EjecutablesJuego(exe).All(TieneAltoRendimiento);
        }

        public void Aplicar()
        {
            string exe = UbicacionFiveM.BuscarExe()
                ?? throw new InvalidOperationException("FiveM no está instalado");

            // Solo los que aún no lo tienen. Si FiveM descargó una versión nueva del juego,
            // al volver a aplicar se añade solo el .exe nuevo
            var ajustes = UbicacionFiveM.EjecutablesJuego(exe)
                .Where(e => !TieneAltoRendimiento(e))
                .Select(e => new AjusteRegistro(Ruta, e, ConAltoRendimiento(Registro.Leer(Ruta, e)), RegistryValueKind.String))
                .ToList();

            Registro.Aplicar(Id, ajustes);
            Logger.Info($"GPU de alto rendimiento para {ajustes.Count} ejecutables de FiveM");
        }

        public void Revertir()
        {
            // Revertimos TODO lo que guardamos, aunque ese .exe ya no exista (la caché se pudo limpiar).
            // GestorEstado guarda cada cambio como "Ruta\Nombre": quitando "Ruta\" queda la ruta del .exe.
            // Al revertir, Registro solo usa Ruta, Nombre y Tipo: el Valor da igual, por eso va vacío.
            var ajustes = GestorEstado.ClavesGuardadas(Id)
                .Where(c => c.StartsWith(Ruta + @"\"))
                .Select(c => new AjusteRegistro(Ruta, c[(Ruta.Length + 1)..], "", RegistryValueKind.String))
                .ToList();

            Registro.Revertir(Id, ajustes);
            Logger.Info("Preferencia de GPU de FiveM restaurada a como estaba");
        }

        // ¿El valor de este .exe ya pide alto rendimiento?
        private static bool TieneAltoRendimiento(string exe) =>
            Partes(Registro.Leer(Ruta, exe)).Contains(Preferencia);

        // Windows guarda varios ajustes en el mismo texto: "SwapEffectUpgradeEnable=1;GpuPreference=0;".
        // Cambiamos solo GpuPreference y dejamos el resto como estaba.
        private static string ConAltoRendimiento(string? actual)
        {
            var partes = Partes(actual).Where(p => !p.StartsWith("GpuPreference=")).ToList();
            partes.Add(Preferencia);
            return string.Join(";", partes) + ";";
        }

        // "A=1;B=2;" → ["A=1", "B=2"]
        private static List<string> Partes(string? valor) =>
            (valor ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
