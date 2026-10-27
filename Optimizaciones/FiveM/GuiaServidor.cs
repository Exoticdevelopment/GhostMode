namespace OptimizadorPC.Optimizaciones.FiveM
{
    // Paso 8 del plan de docs/Investigacion-FiveM.pdf: "¿es el servidor?". Ghost Mode no ejecuta nada:
    // la pantalla fivem explica cómo usar las herramientas oficiales de FiveM (consola F8) y deja copiar los comandos.
    // Fuente de los comandos: "FiveM: comandos de consola del cliente" (docs de Cfx.re)
    public static class GuiaServidor
    {
        // FPS, ping, pérdida de paquetes, uso de CPU y GPU y temperatura de la GPU. No pide modo desarrollador
        public const string Drawperf = "cl_drawperf true";

        // CPU y memoria por recurso (script) del servidor. SÍ pide modo desarrollador
        public const string Resmon = "resmon true";

        // El modo desarrollador: se añade al destino del acceso directo de FiveM
        public const string ModoDesarrollador = "+set moo 31337";
    }
}
