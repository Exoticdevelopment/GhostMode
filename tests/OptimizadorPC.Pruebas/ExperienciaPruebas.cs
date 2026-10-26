using System.Windows.Input;
using OptimizadorPC.Core;
using OptimizadorPC.Pruebas.Infra;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Pruebas
{
    // Parte de experiencia de producto: diagnóstico de la bienvenida, atajo global, inicio con Windows y ajustes
    [Collection("Sistema")]
    public class ExperienciaPruebas
    {
        // ---------- Diagnóstico ----------

        [Fact]
        public void Diagnostico_EquipoModestoRecibeAvisosHonestos()
        {
            var d = new Diagnostico(8, "Intel(R) UHD Graphics 620", false, 1, TipoDisco.Hdd, 9, 230);
            List<Consejo> c = DiagnosticoEquipo.Recomendaciones(d);

            Assert.Contains(c, x => x.Nivel == NivelConsejo.Aviso && x.Texto.Contains("8 GB de RAM") && x.Texto.Contains("tirones"));
            Assert.Contains(c, x => x.Nivel == NivelConsejo.Aviso && x.Texto.Contains("integrada"));
            Assert.Contains(c, x => x.Nivel == NivelConsejo.Aviso && x.Texto.Contains("HDD"));
            Assert.Contains(c, x => x.Nivel == NivelConsejo.Aviso && x.Texto.Contains("9 GB libres"));
            Assert.DoesNotContain(c, x => x.Nivel == NivelConsejo.Bien);
        }

        [Fact]
        public void Diagnostico_EquipoSobradoNoInventaProblemas()
        {
            var d = new Diagnostico(16, "NVIDIA GeForce RTX 3050 6GB Laptop GPU", true, 6, TipoDisco.Ssd, 300, 476);
            List<Consejo> c = DiagnosticoEquipo.Recomendaciones(d);

            Assert.DoesNotContain(c, x => x.Nivel == NivelConsejo.Aviso);
            Assert.Contains(c, x => x.Nivel == NivelConsejo.Info);
        }

        [Theory]
        [InlineData(4, "muy justo")]
        [InlineData(8, "puede tener tirones")]
        [InlineData(12, "suficiente")]
        public void Diagnostico_RamSegunLaCantidad(int gb, string texto)
        {
            var d = new Diagnostico(gb, null, false, null, TipoDisco.Desconocido, null, null);
            Assert.Contains(DiagnosticoEquipo.Recomendaciones(d), x => x.Texto.Contains(texto));
        }

        [Fact]
        public void Diagnostico_PocaVramEnGraficaDedicada()
        {
            var d = new Diagnostico(16, "NVIDIA GeForce GTX 1050", true, 2, TipoDisco.Ssd, 100, 500);
            Assert.Contains(DiagnosticoEquipo.Recomendaciones(d), x => x.Nivel == NivelConsejo.Aviso && x.Texto.Contains("texturas"));
        }

        [Fact]
        public async Task Diagnostico_LeeEsteEquipoDeVerdad()
        {
            Diagnostico d = await DiagnosticoEquipo.LeerAsync();

            Assert.Equal(16, d.RamGb);
            Assert.Contains("NVIDIA", d.Gpu);
            Assert.True(d.GpuDedicada);
            Assert.InRange(d.VramGb!.Value, 5.5, 6.5);   // RTX 3050 de 6 GB: el dato real, no el de WMI (máx. 4 GB)
            Assert.NotEqual(TipoDisco.Desconocido, d.DiscoSistema);
            Assert.True(d.LibreGb > 0 && d.LibreGb < d.TotalGb);
        }

        // ---------- Atajo ----------

        [Theory]
        [InlineData("Ctrl+Alt+G", ModifierKeys.Control | ModifierKeys.Alt, Key.G)]
        [InlineData("alt + shift + f9", ModifierKeys.Alt | ModifierKeys.Shift, Key.F9)]
        [InlineData("Win+5", ModifierKeys.Windows, Key.D5)]
        [InlineData("Ctrl+Num0", ModifierKeys.Control, Key.NumPad0)]
        public void Atajo_SeLeeYSeEscribeIgual(string texto, ModifierKeys mods, Key tecla)
        {
            Combinacion c = Combinacion.Leer(texto)!;
            Assert.Equal(mods, c.Modificadores);
            Assert.Equal(tecla, c.Tecla);
            Assert.Equal(c, Combinacion.Leer(c.ToString()));   // ida y vuelta
        }

        [Theory]
        [InlineData("G")]               // sin modificador
        [InlineData("Shift+G")]         // solo Shift: saltaría al escribir mayúsculas
        [InlineData("Ctrl+Alt")]        // sin tecla
        [InlineData("Ctrl+G+H")]        // dos teclas
        [InlineData("Ctrl+NoExiste")]
        [InlineData("")]
        [InlineData(null)]
        public void Atajo_RechazaCombinacionesInvalidas(string? texto)
        {
            Assert.Null(Combinacion.Leer(texto));
        }

        [Fact]
        public void Atajo_ElOrdenSiempreEsCtrlAltShiftWin()
        {
            Assert.Equal("Ctrl+Alt+Shift+Win+F1",
                         new Combinacion(ModifierKeys.Windows | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Control, Key.F1).ToString());
        }

        // ---------- Inicio con Windows ----------

        [Fact]
        public void InicioWindows_CreaLaTareaElevadaYLaBorraSinDejarRastro()
        {
            // Nombre propio de la prueba: la tarea real del usuario (si la tiene) no se toca
            string nombre = "GhostModePrueba-" + Guid.NewGuid().ToString("N")[..8];
            string exe = Path.Combine(Ayudas.CarpetaTemporal(), "Ghost Mode Prueba.exe");
            try
            {
                Assert.False(InicioAutomatico.Existe(nombre));

                InicioAutomatico.Activar(exe, nombre);

                Assert.True(InicioAutomatico.Existe(nombre));
                Assert.Equal(exe, InicioAutomatico.RutaConfigurada(nombre));
                string xml = ConsultarXml(nombre);
                Assert.Contains("<RunLevel>HighestAvailable</RunLevel>", xml);
                Assert.Contains("<LogonTrigger>", xml);
                Assert.Contains($"<Arguments>{InicioAutomatico.ArgumentoMinimizado}</Arguments>", xml);
                Assert.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", xml);

                // Si el .exe cambia de sitio, la tarea se corrige sola
                string otro = Path.Combine(Path.GetDirectoryName(exe)!, "Movido.exe");
                InicioAutomatico.CorregirRuta(otro, nombre);
                Assert.Equal(otro, InicioAutomatico.RutaConfigurada(nombre));

                InicioAutomatico.Desactivar(nombre);

                Assert.False(InicioAutomatico.Existe(nombre));
                Assert.Null(InicioAutomatico.RutaConfigurada(nombre));
                InicioAutomatico.Desactivar(nombre);   // otra vez: no falla
            }
            finally
            {
                if (InicioAutomatico.Existe(nombre))
                {
                    InicioAutomatico.Desactivar(nombre);
                }
                Directory.Delete(Path.GetDirectoryName(exe)!, recursive: true);
            }
        }

        // ---------- Ajustes ----------

        [Fact]
        public void Ajustes_GuardaYCargaConValoresPorDefecto()
        {
            string carpeta = Ayudas.CarpetaTemporal();
            string archivo = Path.Combine(carpeta, "ajustes.json");
            try
            {
                AjustesApp nuevo = AjustesApp.Cargar(archivo);
                Assert.False(nuevo.BienvenidaVista);
                Assert.Equal("Ctrl+Alt+G", nuevo.Atajo);

                nuevo.BienvenidaVista = true;
                nuevo.Atajo = null;
                Assert.True(nuevo.Guardar(archivo));

                AjustesApp leido = AjustesApp.Cargar(archivo);
                Assert.True(leido.BienvenidaVista);
                Assert.Null(leido.Atajo);

                File.WriteAllText(archivo, "{ roto");
                Assert.False(AjustesApp.Cargar(archivo).BienvenidaVista);
            }
            finally
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }

        private static string ConsultarXml(string nombre)
        {
            var info = new System.Diagnostics.ProcessStartInfo("schtasks", $"/Query /TN \"{nombre}\" /XML")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(info)!;
            string xml = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return xml;
        }
    }
}
