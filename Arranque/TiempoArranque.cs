using System.Diagnostics.Eventing.Reader;   // Leer el Visor de eventos de Windows
using OptimizadorPC.Core;                   // Logger

namespace OptimizadorPC.Arranque
{
    // El "último tiempo de arranque" real. Windows lo mide en cada inicio y lo anota en el
    // Visor de eventos: Microsoft-Windows-Diagnostics-Performance/Operational, evento 100,
    // dato "BootTime" en milisegundos. Si no está (registro desactivado), devolvemos null.
    public static class TiempoArranque
    {
        private const string Registro = "Microsoft-Windows-Diagnostics-Performance/Operational";

        public static TimeSpan? Leer()
        {
            try
            {
                // XPath = "filtro" del Visor de eventos: solo los eventos con Id 100.
                // ReverseDirection = empezar por el más nuevo
                var consulta = new EventLogQuery(Registro, PathType.LogName, "*[System[EventID=100]]")
                {
                    ReverseDirection = true
                };
                using var lector = new EventLogReader(consulta);
                using EventRecord? evento = lector.ReadEvent();
                if (evento is not EventLogRecord registro)
                {
                    return null;
                }

                // Pedimos solo el dato "BootTime" del evento
                var selector = new EventLogPropertySelector(new[] { "Event/EventData/Data[@Name='BootTime']" });
                object? valor = registro.GetPropertyValues(selector).FirstOrDefault();
                return valor != null ? TimeSpan.FromMilliseconds(Convert.ToDouble(valor)) : null;
            }
            catch (Exception ex)
            {
                Logger.Aviso($"No se pudo leer el tiempo de arranque: {ex.Message}");
                return null;
            }
        }
    }
}
