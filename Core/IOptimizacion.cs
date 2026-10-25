namespace OptimizadorPC.Core
{
    // Contrato que cumple TODA optimización (plan de energía, caché de FiveM, etc.)
    // La interfaz gráfica solo conoce esto, así que agregar un módulo nuevo
    // no obliga a cambiar nada más en la app.
    public interface IOptimizacion
    {
        // Propiedades de solo lectura
        string Id { get; }            // Nombre interno único, ej: "plan-energia"
        string Nombre { get; }        // Lo que ve el usuario
        string Descripcion { get; }   // Explicación corta

        // Métodos
        bool EstaActiva();   // ¿Ya está aplicada? Devuelve true o false
        void Aplicar();      // Aplica el cambio (void = no devuelve nada)
        void Revertir();     // Deshace el cambio
    }
}
