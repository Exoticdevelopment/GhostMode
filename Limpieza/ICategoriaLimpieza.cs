using OptimizadorPC.Core;   // Logger

namespace OptimizadorPC.Limpieza
{
    // Contrato de una fila de la pantalla Limpieza ("Archivos temporales", "Papelera"...).
    // La pantalla solo conoce esto: agregar una categoría = una clase nueva + una línea en CatalogoLimpieza.
    public interface ICategoriaLimpieza
    {
        string Id { get; }            // Nombre interno, ej: "temp-usuario"
        string Nombre { get; }        // Lo que ve el usuario
        string Descripcion { get; }   // Explicación corta
        bool MarcadaPorDefecto { get; }

        long Medir();                 // Bytes que se liberarían (puede tardar: llamarlo en segundo plano)
        ResultadoLimpieza Limpiar();  // Borra y dice cuánto se liberó
    }

    // Una categoría que es "el contenido de estas carpetas".
    // Las carpetas que no existen en este PC (p. ej. las de NVIDIA en un equipo con AMD) se ignoran solas.
    public class CarpetasLimpieza : ICategoriaLimpieza
    {
        private readonly IReadOnlyList<string> carpetas;
        private readonly TimeSpan antiguedadMinima;

        public string Id { get; }
        public string Nombre { get; }
        public string Descripcion { get; }
        public bool MarcadaPorDefecto { get; }

        // params = se pueden pasar las carpetas separadas por comas, sin armar una lista
        public CarpetasLimpieza(string id, string nombre, string descripcion, bool marcadaPorDefecto,
                                TimeSpan antiguedadMinima, params string[] carpetas)
        {
            Id = id;
            Nombre = nombre;
            Descripcion = descripcion;
            MarcadaPorDefecto = marcadaPorDefecto;
            this.antiguedadMinima = antiguedadMinima;
            this.carpetas = carpetas;
        }

        public long Medir() => carpetas.Sum(c => BorradoSeguro.Medir(c, antiguedadMinima));

        public ResultadoLimpieza Limpiar()
        {
            Logger.Info($"Limpiando: {Nombre}");
            var total = ResultadoLimpieza.Vacio;
            foreach (string carpeta in carpetas)
            {
                total += BorradoSeguro.Borrar(carpeta, antiguedadMinima);
            }
            return total;
        }
    }
}
