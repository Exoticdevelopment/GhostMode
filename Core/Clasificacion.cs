namespace OptimizadorPC.Core
{
    // Las etiquetas que muestra cada tarjeta de optimización.
    // Son enum (lista cerrada) en vez de texto para que un error de escritura
    // como Impacto.Altto lo detecte el compilador, no el usuario.
    // Sin tildes: el texto bonito ("Gráficos", "Entrada (mouse)") lo pone la interfaz.

    // Columna de la pantalla Optimizaciones donde aparece
    public enum Categoria
    {
        Energia,
        Graficos,
        Sistema,
        Entrada,
        FiveM      // Solo se ven en la página FiveM (no en Optimizaciones ni en el botón Ghost Mode)
    }

    // Cuánto se nota la mejora
    public enum Impacto
    {
        Alto,
        Medio,
        Bajo
    }

    // Seguro = no cambia cómo funcionan otras apps.
    // Moderado = puede cambiar el comportamiento de alguna app (siempre reversible).
    public enum Riesgo
    {
        Seguro,
        Moderado
    }
}
