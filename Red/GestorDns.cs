using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using OptimizadorPC.Core;

namespace OptimizadorPC.Red
{
    // Cambia el servidor DNS del adaptador activo y lo devuelve a como estaba.
    //
    // Dónde lo guarda Windows: HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID},
    // valor "NameServer" (Tcpip6 para IPv6). Vacío = automático (los del router); con direcciones = fijo.
    // Ese texto es lo que se guarda en GestorEstado antes de cambiar nada, y lo que se restaura.
    //
    // Se cambia con netsh (la herramienta de Windows para esto) y se cambian IPv4 e IPv6 juntos:
    // si solo se cambiara IPv4, Windows podría seguir preguntando al DNS IPv6 del router.
    public static class GestorDns
    {
        public const string Modulo = "red-dns";

        private const string ClaveV4 = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";
        private const string ClaveV6 = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\";

        // El valor NameServer tal cual: "" = automático, null = el valor no existe (también es automático).
        // La diferencia importa para dejarlo EXACTO: netsh, al volver a automático, crea NameServer="" aunque
        // antes no existiera (pasa con IPv6)
        public static string? LeerNameServer(string idAdaptador, bool ipv6)
        {
            using RegistryKey? clave = Registry.LocalMachine.OpenSubKey((ipv6 ? ClaveV6 : ClaveV4) + idAdaptador);
            return clave?.GetValue("NameServer") as string;
        }

        // ¿Tiene el adaptador configuración de IPv4 / IPv6? (sin IPv6, no hay nada que cambiar ahí)
        public static bool TieneConfiguracion(string idAdaptador, bool ipv6)
        {
            using RegistryKey? clave = Registry.LocalMachine.OpenSubKey((ipv6 ? ClaveV6 : ClaveV4) + idAdaptador);
            return clave != null;
        }

        // Cuál de la lista está puesto ahora (null = uno fijo que no es de la lista)
        public static ServidorDns? Activo(InfoAdaptador adaptador)
        {
            string[] actuales = Direcciones(LeerNameServer(adaptador.Id, ipv6: false) ?? "");   // null o "" = automático
            return actuales.Length == 0
                ? ServidorDns.Automatico
                : ServidorDns.Todos.FirstOrDefault(s => s.Ipv4.Length > 0 && s.Ipv4[0] == actuales[0]);
        }

        // Los DNS que daría "Automático": los que entrega el router por DHCP
        public static string[] DnsDelRouter(InfoAdaptador adaptador)
        {
            using RegistryKey? clave = Registry.LocalMachine.OpenSubKey(ClaveV4 + adaptador.Id);
            string[] router = Direcciones(clave?.GetValue("DhcpNameServer") as string ?? "");
            // Si no hay dato del router pero el DNS está en automático, los que usa ahora son los del router
            return router.Length > 0 || Activo(adaptador) != ServidorDns.Automatico
                ? router
                : adaptador.Dns.Where(d => !d.Contains(':')).ToArray();
        }

        public static void Aplicar(InfoAdaptador adaptador, ServidorDns servidor)
        {
            if (!TieneConfiguracion(adaptador.Id, ipv6: false))
            {
                throw new InvalidOperationException($"No se encontró la configuración de red de {adaptador.Nombre}");
            }
            string id = adaptador.Id;

            // 1. Guardar cómo estaba (solo la primera vez: si se cambia dos veces, el original sigue siendo el primero).
            //    null = el valor no existía
            GestorEstado.GuardarOriginal(Modulo, $"{id}|indice", adaptador.Indice.ToString());
            var familias = new List<bool> { false };
            if (TieneConfiguracion(id, ipv6: true))
            {
                familias.Add(true);
            }
            foreach (bool ipv6 in familias)
            {
                GestorEstado.GuardarOriginal(Modulo, Clave(id, ipv6), LeerNameServer(id, ipv6));
            }

            // 2. Cambiar. "Automático" con un original automático = dejarlo exactamente como estaba
            foreach (bool ipv6 in familias)
            {
                string? original = GestorEstado.ObtenerOriginal(Modulo, Clave(id, ipv6));
                if (servidor.EsAutomatico && string.IsNullOrEmpty(original))
                {
                    DejarComo(adaptador.Indice, id, ipv6, original);
                }
                else
                {
                    Configurar(adaptador.Indice, ipv6, ipv6 ? servidor.Ipv6 : servidor.Ipv4);
                }
            }
            LimpiarCache();
            Logger.Info($"DNS de {adaptador.Nombre}: {servidor.Nombre} ({servidor.TextoDirecciones})");

            // 3. Si con esto quedó igual que al principio, ya no hay nada que deshacer
            if (familias.All(ipv6 => LeerNameServer(id, ipv6) == GestorEstado.ObtenerOriginal(Modulo, Clave(id, ipv6))))
            {
                OlvidarAdaptador(id);
            }
        }

        // «Deshacer todo»: cada adaptador cambiado vuelve a su NameServer original exacto
        public static void Revertir()
        {
            foreach (string id in GestorEstado.ClavesGuardadas(Modulo).Select(c => c.Split('|')[0]).Distinct().ToList())
            {
                // El número de interfaz puede cambiar si se reinstala el driver: se busca por GUID
                int indice = IndiceActual(id)
                             ?? int.Parse(GestorEstado.ObtenerOriginal(Modulo, $"{id}|indice") ?? "0");

                foreach (bool ipv6 in new[] { false, true })
                {
                    if (GestorEstado.TieneOriginal(Modulo, Clave(id, ipv6)) && TieneConfiguracion(id, ipv6))
                    {
                        DejarComo(indice, id, ipv6, GestorEstado.ObtenerOriginal(Modulo, Clave(id, ipv6)));
                    }
                }
                string? v4 = GestorEstado.ObtenerOriginal(Modulo, Clave(id, false));
                OlvidarAdaptador(id);
                Logger.Info($"DNS restaurado: {(string.IsNullOrEmpty(v4) ? "automático" : v4)}");
            }
            LimpiarCache();
        }

        private static string Clave(string id, bool ipv6) => $"{id}|{(ipv6 ? "ipv6" : "ipv4")}";

        // Deja NameServer exactamente con ese texto (null = que no exista)
        private static void DejarComo(int indice, string id, bool ipv6, string? valor)
        {
            Configurar(indice, ipv6, Direcciones(valor ?? ""));
            if (valor == null)
            {
                // netsh dejó NameServer="" donde antes no había nada: se quita (sigue en automático igual)
                using RegistryKey? clave = Registry.LocalMachine.OpenSubKey((ipv6 ? ClaveV6 : ClaveV4) + id, writable: true);
                clave?.DeleteValue("NameServer", throwOnMissingValue: false);
            }
        }

        // ipconfig /flushdns: borra las direcciones que Windows recuerda (útil si una web o servidor cambió de IP)
        public static void LimpiarCache()
        {
            Ejecutar("ipconfig", "/flushdns");
            Logger.Info("Caché DNS de Windows vaciada");
        }

        // NameServer separa con comas (a veces espacios): "1.1.1.1,1.0.0.1"
        public static string[] Direcciones(string texto) =>
            texto.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Sin direcciones = automático (DHCP); con direcciones = la primera reemplaza todo, las demás se suman en orden
        private static void Configurar(int indice, bool ipv6, string[] direcciones)
        {
            string familia = ipv6 ? "ipv6" : "ipv4";
            if (direcciones.Length == 0)
            {
                Ejecutar("netsh", $"interface {familia} set dnsservers name={indice} source=dhcp");
                return;
            }
            Ejecutar("netsh", $"interface {familia} set dnsservers name={indice} source=static address={direcciones[0]} validate=no");
            for (int i = 1; i < direcciones.Length; i++)
            {
                Ejecutar("netsh", $"interface {familia} add dnsservers name={indice} address={direcciones[i]} index={i + 1} validate=no");
            }
        }

        private static int? IndiceActual(string id)
        {
            NetworkInterface? tarjeta = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == id);
            try
            {
                return tarjeta?.GetIPProperties().GetIPv4Properties()?.Index;
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        private static void OlvidarAdaptador(string id)
        {
            foreach (string clave in GestorEstado.ClavesGuardadas(Modulo).Where(c => c.StartsWith(id + "|")).ToList())
            {
                GestorEstado.Olvidar(Modulo, clave);
            }
        }

        private static void Ejecutar(string programa, string argumentos)
        {
            var configuracion = new ProcessStartInfo(programa, argumentos)
            {
                RedirectStandardOutput = true,   // capturar lo que imprime
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true            // sin ventana negra parpadeando
            };
            using var proceso = Process.Start(configuracion)!;
            // Leemos las dos salidas a la vez para que no se quede trabado esperando
            Task<string> errores = proceso.StandardError.ReadToEndAsync();
            string salida = proceso.StandardOutput.ReadToEnd();
            proceso.WaitForExit();

            if (proceso.ExitCode != 0)
            {
                string mensaje = errores.Result.Trim();
                throw new InvalidOperationException(
                    $"{programa} {argumentos} falló (código {proceso.ExitCode}): {(mensaje.Length > 0 ? mensaje : salida.Trim())}");
            }
        }
    }
}
