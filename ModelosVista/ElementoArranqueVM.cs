using CommunityToolkit.Mvvm.ComponentModel;   // ObservableObject, [ObservableProperty]
using OptimizadorPC.Arranque;
using OptimizadorPC.Componentes;
using Wpf.Ui.Controls;                        // SymbolRegular

namespace OptimizadorPC.ModelosVista
{
    // Una fila de la pantalla Inicio de Windows.
    // El interruptor (Habilitado) se aplica al momento: cuando el usuario lo mueve, avisamos
    // a la página con el evento CambioPedido y ella hace el cambio en Windows.
    public partial class ElementoArranqueVM : ObservableObject
    {
        // El elemento tal como está en Windows. Se reemplaza tras cada cambio (los record no se modifican)
        public ElementoArranque Elemento { get; set; }

        // Mientras es true, mover Habilitado desde el código NO dispara CambioPedido
        private bool cambiandoDesdeCodigo = false;

        public ElementoArranqueVM(ElementoArranque elemento)
        {
            Elemento = elemento;
            cambiandoDesdeCodigo = true;
            Habilitado = elemento.Habilitado;
            cambiandoDesdeCodigo = false;
        }

        public string Nombre => Elemento.Nombre;
        public string Editor => Elemento.Editor;
        public string TextoOrigen => Elemento.TextoOrigen;
        public string Comando => Elemento.Comando;   // se ve al pasar el ratón por el nombre

        // Carpeta Inicio → icono de carpeta; registro → icono de app
        public SymbolRegular Icono =>
            Elemento.Origen is OrigenArranque.CarpetaUsuario or OrigenArranque.CarpetaEquipo
                ? SymbolRegular.FolderOpen20
                : SymbolRegular.Apps20;

        [ObservableProperty]
        public partial bool Habilitado { get; set; }

        [ObservableProperty]
        public partial EstadoInterruptor Estado { get; set; }

        // event Action<...> = aviso que lleva datos: qué fila y qué quiere el usuario (true = habilitar)
        public event Action<ElementoArranqueVM, bool>? CambioPedido;

        // El toolkit llama a este método cada vez que cambia Habilitado
        partial void OnHabilitadoChanged(bool value)
        {
            if (!cambiandoDesdeCodigo)
            {
                CambioPedido?.Invoke(this, value);
            }
        }

        // Pone el interruptor como está en Windows sin que cuente como un clic del usuario
        public void MostrarReal()
        {
            cambiandoDesdeCodigo = true;
            Habilitado = Elemento.Habilitado;
            cambiandoDesdeCodigo = false;
        }
    }
}
