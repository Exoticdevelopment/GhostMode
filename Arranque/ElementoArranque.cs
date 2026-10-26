using Microsoft.Win32;   // RegistryHive

namespace OptimizadorPC.Arranque
{
    // De dónde arranca el programa. Se muestra en la columna "Origen"
    public enum OrigenArranque
    {
        RegistroUsuario,     // HKCU\...\Run: solo para tu usuario
        RegistroEquipo,      // HKLM\...\Run: para todos los usuarios
        RegistroEquipo32,    // HKLM\WOW6432Node\...\Run: programas de 32 bits, para todos
        CarpetaUsuario,      // Carpeta Inicio de tu usuario (accesos directos)
        CarpetaEquipo        // Carpeta Inicio común a todos los usuarios
    }

    // Un programa que arranca con Windows.
    // record = clase pequeña que solo guarda datos.
    //
    // Hive + RutaAprobacion + NombreAprobacion = el sitio de "StartupApproved" donde Windows guarda
    // si está habilitado o no. Ahí escribimos para desactivarlo (como el Administrador de tareas):
    // la entrada original (en Run o en la carpeta) NUNCA se borra.
    public record ElementoArranque(
        string Nombre,
        string Editor,
        OrigenArranque Origen,
        string Comando,
        bool Habilitado,
        RegistryHive Hive,
        string RutaAprobacion,
        string NombreAprobacion)
    {
        // Nombre único del elemento, que también es su clave en GestorEstado: "HKCU|ruta|nombre"
        public string Clave => GestorArranque.ArmarClave(Hive, RutaAprobacion, NombreAprobacion);

        public string TextoOrigen => Origen switch
        {
            OrigenArranque.RegistroUsuario => "Registro (usuario)",
            OrigenArranque.RegistroEquipo => "Registro (equipo)",
            OrigenArranque.RegistroEquipo32 => "Registro (equipo, 32 bits)",
            OrigenArranque.CarpetaUsuario => "Carpeta Inicio (usuario)",
            _ => "Carpeta Inicio (equipo)"
        };
    }

    // Lo que devuelve la lectura: los que se pueden mostrar y cuántos se ocultaron por ser críticos
    public record ListaArranque(IReadOnlyList<ElementoArranque> Elementos, int Ocultos);
}
