using CommunityToolkit.Mvvm.ComponentModel;   // ObservableObject, [ObservableProperty]
using OptimizadorPC.Componentes;
using OptimizadorPC.Core;

namespace OptimizadorPC.ModelosVista
{
    // "Modelo de vista" (VM) de UNA optimización: lo que su tarjeta necesita mostrar.
    // La optimización sabe aplicar y revertir; este VM recuerda además lo que el usuario
    // eligió en el interruptor, si se está aplicando y si falló.
    //
    // ObservableObject + [ObservableProperty] = cuando la propiedad cambia, la pantalla
    // se entera sola y se redibuja (CommunityToolkit escribe ese código por nosotros).
    // "partial" = una parte de la clase la escribimos nosotros y otra la genera el toolkit.
    public partial class OptimizacionVM : ObservableObject
    {
        public IOptimizacion Optimizacion { get; }

        public OptimizacionVM(IOptimizacion optimizacion)
        {
            Optimizacion = optimizacion;
        }

        // Atajos para que el XAML escriba {Binding Nombre} en vez de {Binding Optimizacion.Nombre}
        public string Nombre => Optimizacion.Nombre;
        public string Descripcion => Optimizacion.Descripcion;
        public Categoria Categoria => Optimizacion.Categoria;
        public Riesgo Riesgo => Optimizacion.Riesgo;

        // Textos y colores de las pastillas
        public string TextoImpacto => $"Impacto {Optimizacion.Impacto}";   // "Impacto Alto"
        public bool ImpactoAlto => Optimizacion.Impacto == Impacto.Alto;   // pastilla verde
        public string TextoRiesgo => Optimizacion.Riesgo.ToString();       // "Seguro" o "Moderado"
        public bool EsModerado => Optimizacion.Riesgo == Riesgo.Moderado;  // pastilla naranja

        // Lo que el usuario quiere (posición del interruptor).
        // NotifyPropertyChangedFor = al cambiar, avisar también que cambió TieneCambioPendiente
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TieneCambioPendiente))]
        public partial bool Deseada { get; set; }

        // Lo que Windows tiene de verdad ahora mismo (leído con EstaActiva)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TieneCambioPendiente))]
        public partial bool Aplicada { get; private set; }

        // Normal, Aplicando o Error: lo dibuja el interruptor
        [ObservableProperty]
        public partial EstadoInterruptor Estado { get; set; }

        // ¿El interruptor está distinto de lo que hay en Windows? → cuenta para "Aplicar N cambios"
        public bool TieneCambioPendiente => Deseada != Aplicada;

        // El toolkit llama a este método cada vez que cambia Deseada.
        // Si el usuario vuelve a mover el interruptor tras un error, quitamos la X roja.
        partial void OnDeseadaChanged(bool value)
        {
            if (Estado == EstadoInterruptor.Error)
            {
                Estado = EstadoInterruptor.Normal;
            }
        }

        // Pregunta a Windows si la optimización está aplicada y pone el interruptor igual
        public async Task LeerEstadoRealAsync()
        {
            bool activa;
            try
            {
                // EstaActiva puede tardar (powercfg, registro): en segundo plano
                activa = await Task.Run(Optimizacion.EstaActiva);
            }
            catch (Exception ex)
            {
                Logger.Error($"No se pudo leer el estado de {Optimizacion.Id}", ex);
                activa = false;
            }

            Aplicada = activa;
            Deseada = activa;
        }
    }
}
