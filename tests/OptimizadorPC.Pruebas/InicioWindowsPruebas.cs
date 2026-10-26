using Microsoft.Win32;
using OptimizadorPC.Arranque;
using OptimizadorPC.Core;
using OptimizadorPC.Pruebas.Infra;

namespace OptimizadorPC.Pruebas
{
    // Solo se habilitan/deshabilitan entradas creadas por la prueba. Los programas reales solo se leen.
    [Collection("Sistema")]
    public class InicioWindowsPruebas
    {
        private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AprobadosRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        [Fact]
        public void Listar_DevuelveElementosYOcultaLosCriticos()
        {
            ListaArranque lista = GestorArranque.Listar();

            // Windows 11 siempre trae al menos SecurityHealth (Seguridad de Windows), que debe ocultarse
            Assert.True(lista.Ocultos > 0, "No se ocultó ningún elemento crítico");
            Assert.DoesNotContain(lista.Elementos, e => e.Nombre.Equals("SecurityHealth", StringComparison.OrdinalIgnoreCase));

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
            foreach (ElementoArranque e in lista.Elementos)
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Nombre));
                string? exe = GestorArranque.ExeDeComando(e.Comando);
                Assert.False(exe != null && exe.StartsWith(windows, StringComparison.OrdinalIgnoreCase),
                             $"{e.Nombre} arranca desde Windows y no se ocultó");
                Assert.False(FiltroCriticos.EsCritico(e.Nombre, exe, e.Editor), $"{e.Nombre} es crítico y no se ocultó");
            }
        }

        [Theory]
        [InlineData("SecurityHealth", null, "Desconocido", true)]                                    // nombre de Windows
        [InlineData("Algo", @"C:\Windows\System32\algo.exe", "Desconocido", true)]                 // dentro de C:\Windows
        [InlineData("Algo", @"C:\Program Files\Windows Defender\MSASCuiL.exe", "Microsoft", true)]
        [InlineData("AvastUI", @"C:\Program Files\Avast Software\Avast\AvastUI.exe", "AVAST Software", true)]
        [InlineData("egui", @"C:\Program Files\ESET\ESET Security\egui.exe", "ESET, spol. s r.o.", true)]
        [InlineData("Steam", @"C:\Program Files (x86)\Steam\steam.exe", "Valve Corporation", false)]
        [InlineData("Reset", @"C:\Programas\preset\reset.exe", "Reset Tools", false)]               // "eset" dentro de otra palabra
        public void FiltroCriticos_ReconoceWindowsYAntivirus(string nombre, string? exe, string editor, bool critico)
        {
            Assert.Equal(critico, FiltroCriticos.EsCritico(nombre, exe, editor));
        }

        [Fact]
        public void Listar_OcultaUnaEntradaPropiaQueArrancaDesdeWindows()
        {
            string nombre = "GhostModePruebaCritica-" + Guid.NewGuid().ToString("N")[..8];
            int ocultosAntes = GestorArranque.Listar().Ocultos;
            EscribirRun(nombre, $@"""{Environment.SystemDirectory}\GhostModePrueba-no-existe.exe""");
            try
            {
                ListaArranque lista = GestorArranque.Listar();
                Assert.DoesNotContain(lista.Elementos, e => e.Nombre == nombre);
                Assert.Equal(ocultosAntes + 1, lista.Ocultos);
            }
            finally
            {
                BorrarEntrada(nombre);
            }
        }

        [Fact]
        public void CambiarEstado_DesactivaYReactivaSoloLaEntradaDePrueba()
        {
            string nombre = "GhostModePrueba-" + Guid.NewGuid().ToString("N")[..8];
            string comando = @"""C:\GhostModePrueba\no-existe.exe"" --prueba";
            byte[]? aprobadosAntes = null;
            Dictionary<string, object?> otrosAntes = FotoAprobados();
            GestorEstado.Limpiar(GestorArranque.Modulo);
            EscribirRun(nombre, comando);

            try
            {
                ElementoArranque elemento = Buscar(nombre);
                Assert.True(elemento.Habilitado);
                Assert.Equal(OrigenArranque.RegistroUsuario, elemento.Origen);
                Assert.Equal(aprobadosAntes, LeerAprobado(nombre));   // no había valor en StartupApproved

                // Desactivar: escribe 03 + fecha en StartupApproved; la entrada de Run sigue intacta
                GestorArranque.CambiarEstado(elemento, habilitar: false);
                byte[] desactivado = LeerAprobado(nombre)!;
                Assert.Equal(12, desactivado.Length);
                Assert.Equal(0x03, desactivado[0]);
                Assert.Equal(comando, LeerRun(nombre));
                Assert.False(Buscar(nombre).Habilitado);
                Assert.True(GestorEstado.TieneOriginal(GestorArranque.Modulo, elemento.Clave));

                // Reactivar = volver a como estaba: el valor se borra (no existía) y se olvida el original
                GestorArranque.CambiarEstado(Buscar(nombre), habilitar: true);
                Assert.Null(LeerAprobado(nombre));
                Assert.True(Buscar(nombre).Habilitado);
                Assert.False(GestorEstado.TieneCambios(GestorArranque.Modulo));

                // "Deshacer todo" también lo deja como estaba
                GestorArranque.CambiarEstado(Buscar(nombre), habilitar: false);
                GestorArranque.RestaurarTodo();
                Assert.Null(LeerAprobado(nombre));
                Assert.False(GestorEstado.TieneCambios(GestorArranque.Modulo));

                // Ningún otro programa de inicio cambió
                Dictionary<string, object?> otrosDespues = FotoAprobados();
                otrosDespues.Remove(nombre);
                Assert.Equal(otrosAntes, otrosDespues);
            }
            finally
            {
                BorrarEntrada(nombre);
                GestorEstado.Limpiar(GestorArranque.Modulo);
            }
        }

        // ---------- Ayudantes ----------

        private static ElementoArranque Buscar(string nombre) =>
            Assert.Single(GestorArranque.Listar().Elementos, e => e.Nombre == nombre);

        private static void EscribirRun(string nombre, string comando)
        {
            using RegistryKey clave = Registry.CurrentUser.CreateSubKey(Run);
            clave.SetValue(nombre, comando, RegistryValueKind.String);
        }

        private static string? LeerRun(string nombre)
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(Run);
            return clave?.GetValue(nombre) as string;
        }

        private static byte[]? LeerAprobado(string nombre)
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(AprobadosRun);
            return clave?.GetValue(nombre) as byte[];
        }

        // Todos los valores de StartupApproved\Run (en hexadecimal) para comparar antes y después
        private static Dictionary<string, object?> FotoAprobados()
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(AprobadosRun);
            if (clave == null)
            {
                return [];
            }
            return clave.GetValueNames().ToDictionary(
                n => n, n => (object?)(clave.GetValue(n) is byte[] b ? Convert.ToHexString(b) : clave.GetValue(n)));
        }

        private static void BorrarEntrada(string nombre)
        {
            using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(Run, writable: true))
            {
                run?.DeleteValue(nombre, throwOnMissingValue: false);
            }
            using RegistryKey? aprobados = Registry.CurrentUser.OpenSubKey(AprobadosRun, writable: true);
            aprobados?.DeleteValue(nombre, throwOnMissingValue: false);
        }
    }
}
