using OptimizadorPC.Core;
using Wpf.Ui.Controls;   // SymbolRegular

namespace OptimizadorPC.ModelosVista
{
    // Una columna de la pantalla Optimizaciones: "Energía" con su icono y sus tarjetas.
    // record = clase pequeña que solo guarda datos (se crea con new GrupoCategoria(...))
    public record GrupoCategoria(string Nombre, SymbolRegular Icono, IReadOnlyList<OptimizacionVM> Optimizaciones)
    {
        // El texto bonito de cada categoría (el enum va sin tildes)
        public static string NombreDe(Categoria categoria) => categoria switch
        {
            Categoria.Energia => "Energía",
            Categoria.Graficos => "Gráficos",
            Categoria.Sistema => "Sistema",
            Categoria.Entrada => "Entrada (mouse)",
            _ => categoria.ToString()   // _ = "cualquier otro caso" (por si se agrega una nueva)
        };

        public static SymbolRegular IconoDe(Categoria categoria) => categoria switch
        {
            Categoria.Energia => SymbolRegular.Flash20,
            Categoria.Graficos => SymbolRegular.Desktop20,
            Categoria.Sistema => SymbolRegular.DeveloperBoard20,
            Categoria.Entrada => SymbolRegular.CursorClick20,
            _ => SymbolRegular.Options20
        };
    }
}
