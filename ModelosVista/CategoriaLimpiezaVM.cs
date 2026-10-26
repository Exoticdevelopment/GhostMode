using CommunityToolkit.Mvvm.ComponentModel;   // ObservableObject, [ObservableProperty]
using OptimizadorPC.Limpieza;
using OptimizadorPC.Servicios;
using Wpf.Ui.Controls;                        // SymbolRegular

namespace OptimizadorPC.ModelosVista
{
    // Una fila de la pantalla Limpieza: la casilla, el icono, el nombre y los MB medidos.
    // Igual que OptimizacionVM: [ObservableProperty] hace que la pantalla se redibuje sola al cambiar.
    public partial class CategoriaLimpiezaVM : ObservableObject
    {
        public ICategoriaLimpieza Categoria { get; }

        public CategoriaLimpiezaVM(ICategoriaLimpieza categoria)
        {
            Categoria = categoria;
            Seleccionada = categoria.MarcadaPorDefecto;
        }

        public string Nombre => Categoria.Nombre;
        public string Descripcion => Categoria.Descripcion;

        // El icono de cada fila (el diseño usa uno distinto por categoría)
        public SymbolRegular Icono => Categoria.Id switch
        {
            "temp-usuario" => SymbolRegular.DocumentCopy20,
            "temp-windows" => SymbolRegular.Folder20,
            "windows-update" => SymbolRegular.ArrowDownload20,
            "shaders" => SymbolRegular.Layer20,
            "papelera" => SymbolRegular.Delete20,
            _ => SymbolRegular.Broom20
        };

        // ¿La casilla está marcada?
        [ObservableProperty]
        public partial bool Seleccionada { get; set; }

        // Bytes medidos en el último análisis. null = aún no se analizó o no se pudo medir
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextoTamano))]
        public partial long? Bytes { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextoTamano))]
        public partial bool Midiendo { get; set; }

        // Lo que se ve a la derecha de la fila. Nunca un número inventado: si no se sabe, "—"
        public string TextoTamano => Midiendo ? "Midiendo…" : Bytes != null ? Formato.Tamano(Bytes.Value) : "—";
    }
}
