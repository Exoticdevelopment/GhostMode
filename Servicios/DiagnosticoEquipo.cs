using System.IO;
using System.Management;   // WMI
using Microsoft.Win32;
using OptimizadorPC.Core;

namespace OptimizadorPC.Servicios
{
    public enum TipoDisco { Desconocido, Hdd, Ssd }

    // Lo que mira el diagnóstico rápido de la bienvenida. null = no se pudo leer (no se inventa)
    public record Diagnostico(
        int? RamGb,
        string? Gpu,
        bool GpuDedicada,
        double? VramGb,
        TipoDisco DiscoSistema,
        double? LibreGb,
        double? TotalGb);

    // Una recomendación: Bien (verde), Aviso (ámbar) o Info (gris)
    public enum NivelConsejo { Bien, Aviso, Info }
    public record Consejo(NivelConsejo Nivel, string Texto);

    public static class DiagnosticoEquipo
    {
        public static async Task<Diagnostico> LeerAsync()
        {
            DatosEquipo equipo = await InfoSistema.ObtenerAsync();
            return await Task.Run(() =>
            {
                (string? gpu, double? vram) = GpuConMasVram();
                gpu ??= equipo.Gpu;
                (double? libre, double? total) = EspacioSistema();
                return new Diagnostico(equipo.RamGb, gpu, gpu != null && InfoSistema.EsDedicada(gpu), vram,
                                       TipoDiscoSistema(), libre, total);
            });
        }

        // Recomendaciones honestas para jugar (FiveM sobre todo). Con datos conocidos se prueba en las pruebas
        public static List<Consejo> Recomendaciones(Diagnostico d)
        {
            var consejos = new List<Consejo>();

            switch (d.RamGb)
            {
                case < 8:
                    consejos.Add(new(NivelConsejo.Aviso, $"Con {d.RamGb} GB de RAM, FiveM va muy justo: usa gráficos bajos y cierra todo lo demás antes de jugar."));
                    break;
                case < 12:
                    consejos.Add(new(NivelConsejo.Aviso, $"Con {d.RamGb} GB de RAM, FiveM puede tener tirones en servidores grandes: cierra el navegador antes de jugar (Modo Gaming puede pausarlo solo)."));
                    break;
                case not null:
                    consejos.Add(new(NivelConsejo.Bien, $"{d.RamGb} GB de RAM: suficiente para FiveM."));
                    break;
            }

            if (d.Gpu != null && !d.GpuDedicada)
            {
                consejos.Add(new(NivelConsejo.Aviso, "Solo hay gráfica integrada: usa el preset bajo y, si hace falta, baja la resolución."));
            }
            else if (d.VramGb is double vram && vram < 4)
            {
                consejos.Add(new(NivelConsejo.Aviso, $"La gráfica tiene {vram:0.#} GB de memoria: deja las texturas en normal para evitar tirones."));
            }
            else if (d.Gpu != null)
            {
                consejos.Add(new(NivelConsejo.Bien, d.VramGb is double v ? $"Gráfica dedicada con {v:0.#} GB." : "Gráfica dedicada."));
            }

            if (d.DiscoSistema == TipoDisco.Hdd)
            {
                consejos.Add(new(NivelConsejo.Aviso, "Windows está en un disco duro (HDD): las cargas serán lentas y habrá tirones al cargar zonas. Un SSD es la mejora que más se nota."));
            }
            else if (d.DiscoSistema == TipoDisco.Ssd)
            {
                consejos.Add(new(NivelConsejo.Bien, "Windows está en un SSD."));
            }

            if (d.LibreGb is double libre && d.TotalGb is double total && (libre < 15 || libre / total < 0.10))
            {
                consejos.Add(new(NivelConsejo.Aviso, $"Quedan {libre:0} GB libres en el disco de Windows: con poco espacio todo va más lento. Usa la sección Limpieza."));
            }

            if (consejos.All(c => c.Nivel == NivelConsejo.Bien))
            {
                consejos.Add(new(NivelConsejo.Info, "Tu equipo no tiene cuellos de botella evidentes. Ghost Mode ayudará sobre todo con los programas de fondo."));
            }
            return consejos;
        }

        // ===== Lecturas =====

        // La memoria de vídeo real está en el registro del driver (qwMemorySize). La de WMI (AdapterRAM)
        // se queda corta: no pasa de 4 GB
        public static (string? Nombre, double? VramGb) GpuConMasVram()
        {
            const string clase = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            try
            {
                using RegistryKey? raiz = Registry.LocalMachine.OpenSubKey(clase);
                if (raiz == null)
                {
                    return (null, null);
                }
                (string? Nombre, double? Gb) mejor = (null, null);
                foreach (string sub in raiz.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
                {
                    using RegistryKey? k = raiz.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is not string nombre ||
                        k.GetValue("HardwareInformation.qwMemorySize") is not long bytes || bytes <= 0)
                    {
                        continue;
                    }
                    double gb = bytes / (1024.0 * 1024 * 1024);
                    if (mejor.Gb == null || gb > mejor.Gb)
                    {
                        mejor = (nombre, gb);
                    }
                }
                return mejor;
            }
            catch (Exception ex)
            {
                Logger.Error("Diagnóstico: no se pudo leer la memoria de la gráfica", ex);
                return (null, null);
            }
        }

        // Disco donde está Windows
        public static TipoDisco TipoDiscoSistema() => TipoDiscoDe(Path.GetPathRoot(Environment.SystemDirectory)![0]);

        // Disco de una unidad: partición con esa letra → número de disco → tipo de medio.
        // Lo usan también FiveM y GTA V (Optimizaciones/FiveM/DiscoJuego.cs). Si algo no se puede leer, Desconocido
        public static TipoDisco TipoDiscoDe(char letra)
        {
            if (!char.IsAsciiLetter(letra))
            {
                return TipoDisco.Desconocido;   // (además, la letra va dentro de la consulta: solo A-Z)
            }
            try
            {
                var ambito = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                using var particiones = new ManagementObjectSearcher(ambito,
                    new ObjectQuery($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{char.ToUpperInvariant(letra)}'"));
                uint? disco = particiones.Get().Cast<ManagementObject>().Select(p => (uint?)Convert.ToUInt32(p["DiskNumber"])).FirstOrDefault();
                if (disco == null)
                {
                    return TipoDisco.Desconocido;
                }

                using var discos = new ManagementObjectSearcher(ambito,
                    new ObjectQuery($"SELECT MediaType, BusType FROM MSFT_PhysicalDisk WHERE DeviceId = '{disco}'"));
                ManagementObject? fisico = discos.Get().Cast<ManagementObject>().FirstOrDefault();
                return fisico == null
                    ? TipoDisco.Desconocido   // p. ej. un volumen de Espacios de almacenamiento
                    : ClasificarDisco(Convert.ToInt32(fisico["MediaType"]), Convert.ToInt32(fisico["BusType"]));
            }
            catch (Exception ex)
            {
                Logger.Error($"Diagnóstico: no se pudo leer el tipo de disco de {letra}:", ex);
                return TipoDisco.Desconocido;
            }
        }

        // MSFT_PhysicalDisk.MediaType: 3 = HDD, 4 = SSD, 5 = SCM (memoria persistente, más rápida que un SSD).
        // BusType 17 = NVMe: siempre es un SSD, aunque Windows no rellene MediaType
        public static TipoDisco ClasificarDisco(int mediaType, int busType) => mediaType switch
        {
            3 => TipoDisco.Hdd,
            4 or 5 => TipoDisco.Ssd,
            _ => busType == 17 ? TipoDisco.Ssd : TipoDisco.Desconocido
        };

        public static (double? LibreGb, double? TotalGb) EspacioSistema()
        {
            try
            {
                var unidad = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
                const double gb = 1024.0 * 1024 * 1024;
                return (unidad.AvailableFreeSpace / gb, unidad.TotalSize / gb);
            }
            catch (Exception ex)
            {
                Logger.Error("Diagnóstico: no se pudo leer el espacio libre", ex);
                return (null, null);
            }
        }
    }
}
