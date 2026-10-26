using System.Diagnostics;
using OptimizadorPC.Core;
using OptimizadorPC.Gaming;
using OptimizadorPC.Optimizaciones.Sistema;
using OptimizadorPC.Pruebas.Infra;
using OptimizadorPC.Servicios;

namespace OptimizadorPC.Pruebas
{
    // Modo Gaming contra el Windows real, sin tocar programas del usuario: los "juegos" y las "apps de fondo"
    // son copias de ping.exe con otro nombre (ProcesoFalso). Las optimizaciones se comprueban con una
    // foto del registro y del plan de energía antes y después.
    [Collection("Sistema")]
    public class ModoGamingPruebas
    {
        // ---------- Detección de juegos ----------

        [Fact]
        public void Steam_LeeLasBibliotecasYLosManifiestos()
        {
            string steam = Ayudas.CarpetaTemporal();
            string otroDisco = Ayudas.CarpetaTemporal();
            try
            {
                // libraryfolders.vdf con la ruta de la segunda biblioteca escapada como la escribe Steam
                Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
                File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                    "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t}\n" +
                    "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + otroDisco.Replace("\\", "\\\\") + "\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"730\"\t\t\"123\"\n\t\t}\n\t}\n}\n");

                string juego1 = CrearJuegoSteam(steam, "1001", "Juego Uno", "JuegoUno",
                                                ("JuegoUno.exe", 3000), ("unins000.exe", 9000), ("UnityCrashHandler64.exe", 8000));
                string juego2 = CrearJuegoSteam(otroDisco, "1002", "Juego Dos", "Juego Dos",
                                                ("bin\\x64\\JuegoDos.exe", 5000), ("setup.exe", 7000));
                CrearJuegoSteam(steam, "228980", "Steamworks Common Redistributables", "Steamworks Shared", ("vcredist.exe", 10));
                CrearJuegoSteam(steam, "1493710", "Proton Experimental", "Proton - Experimental", ("proton.exe", 10));
                // Manifiesto de un juego cuya carpeta ya no existe (desinstalado a medias): se ignora
                File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_9.acf"),
                                  "\"AppState\"\n{\n\t\"appid\"\t\t\"9\"\n\t\"name\"\t\t\"Fantasma\"\n\t\"installdir\"\t\t\"NoExiste\"\n}\n");

                List<Juego> juegos = DetectorJuegos.JuegosSteam(steam).OrderBy(j => j.Nombre).ToList();

                Assert.Equal(["Juego Dos", "Juego Uno"], juegos.Select(j => j.Nombre));
                Assert.All(juegos, j => Assert.Equal(OrigenJuego.Steam, j.Origen));
                // El .exe principal es el más grande que no sea instalador ni reporte de errores
                Assert.Equal(Path.Combine(juego2, "bin", "x64", "JuegoDos.exe"), juegos[0].Exe);
                Assert.Equal(Path.Combine(juego1, "JuegoUno.exe"), juegos[1].Exe);
                Assert.Equal(juego1, juegos[1].Carpeta);
            }
            finally
            {
                Directory.Delete(steam, recursive: true);
                Directory.Delete(otroDisco, recursive: true);
            }
        }

        [Fact]
        public void Vdf_DesescapaLasRutasYDevuelveTodosLosValores()
        {
            string vdf = "\"a\" { \"path\" \"D:\\\\Juegos\\\\Steam\" \"x\" { \"path\" \"E:\\\\Steam\" } \"name\" \"Con \\\"comillas\\\"\" }";
            Assert.Equal([@"D:\Juegos\Steam", @"E:\Steam"], DetectorJuegos.ValoresVdf(vdf, "path"));
            Assert.Equal(["Con \"comillas\""], DetectorJuegos.ValoresVdf(vdf, "name"));
            Assert.Empty(DetectorJuegos.ValoresVdf(vdf, "installdir"));
        }

        [Fact]
        public void Epic_LeeLosManifiestosYSoloCuentaJuegosCompletos()
        {
            string manifiestos = Ayudas.CarpetaTemporal();
            string instalacion = Ayudas.CarpetaTemporal();
            try
            {
                Directory.CreateDirectory(Path.Combine(instalacion, "Binaries"));
                File.WriteAllBytes(Path.Combine(instalacion, "Binaries", "Fortnite.exe"), new byte[100]);
                string Item(string nombre, string exe, bool incompleto, string categorias) =>
                    $$"""{"DisplayName":"{{nombre}}","InstallLocation":"{{instalacion.Replace("\\", "\\\\")}}","LaunchExecutable":"{{exe}}","bIsIncompleteInstall":{{(incompleto ? "true" : "false")}},"AppCategories":[{{categorias}}]}""";

                File.WriteAllText(Path.Combine(manifiestos, "a.item"), Item("Fortnite", "Binaries/Fortnite.exe", false, "\"public\",\"games\""));
                File.WriteAllText(Path.Combine(manifiestos, "b.item"), Item("A medio bajar", "Binaries/Fortnite.exe", true, "\"games\""));
                File.WriteAllText(Path.Combine(manifiestos, "c.item"), Item("Unreal Engine", "Binaries/Fortnite.exe", false, "\"engines\""));
                File.WriteAllText(Path.Combine(manifiestos, "d.item"), Item("Sin exe", "Binaries/NoExiste.exe", false, "\"games\""));

                Juego juego = Assert.Single(DetectorJuegos.JuegosEpic(manifiestos));
                Assert.Equal("Fortnite", juego.Nombre);
                Assert.Equal(Path.Combine(instalacion, "Binaries", "Fortnite.exe"), juego.Exe);
                Assert.Equal(OrigenJuego.Epic, juego.Origen);
            }
            finally
            {
                Directory.Delete(manifiestos, recursive: true);
                Directory.Delete(instalacion, recursive: true);
            }
        }

        [Fact]
        public void Detectar_EnEstePcNoFallaYCadaJuegoExiste()
        {
            List<Juego> juegos = DetectorJuegos.Detectar();
            Assert.All(juegos, j => Assert.True(File.Exists(j.Exe), j.Exe));
            Assert.Equal(juegos.Count, juegos.DistinctBy(j => j.Id).Count());
            Assert.Empty(DetectorJuegos.JuegosEpic(@"C:\GhostModePrueba\no-existe"));
        }

        [Fact]
        public void Juego_PorCarpetaOPorExeExacto()
        {
            var porCarpeta = new Juego("X", @"C:\Juegos\X\x.exe", @"C:\Juegos\X", OrigenJuego.Steam);
            Assert.True(porCarpeta.EsSuyo(@"C:\Juegos\X\bin\otro.exe"));
            Assert.True(porCarpeta.EsSuyo(@"c:\juegos\x\X.EXE"));
            Assert.False(porCarpeta.EsSuyo(@"C:\Juegos\XY\x.exe"));   // carpeta con nombre parecido

            var manual = new Juego("Y", @"C:\Y\y.exe", null, OrigenJuego.Manual);
            Assert.True(manual.EsSuyo(@"C:\Y\Y.exe"));
            Assert.False(manual.EsSuyo(@"C:\Y\otro.exe"));
        }

        // ---------- Configuración ----------

        [Fact]
        public void Config_GuardaYCargaYUsaValoresSegurosPorDefecto()
        {
            string carpeta = Ayudas.CarpetaTemporal();
            string archivo = Path.Combine(carpeta, "modo_gaming.json");
            try
            {
                var nueva = ConfigGaming.Cargar(archivo);
                Assert.False(nueva.SuspenderApps);   // pausar apps hay que pedirlo
                Assert.Equal(SuspensorApps.PorDefecto, nueva.AppsSuspender);
                Assert.True(nueva.EsAutomatico(new Juego("M", @"C:\m.exe", null, OrigenJuego.Manual)));
                Assert.False(nueva.EsAutomatico(new Juego("S", @"C:\s\s.exe", @"C:\s", OrigenJuego.Steam)));

                nueva.SuspenderApps = true;
                nueva.AppsSuspender = ["uno.exe"];
                nueva.Automatico[@"c:\s"] = true;
                nueva.Guardar(archivo);

                var leida = ConfigGaming.Cargar(archivo);
                Assert.True(leida.SuspenderApps);
                Assert.Equal(["uno.exe"], leida.AppsSuspender);
                Assert.True(leida.EsAutomatico(new Juego("S", @"C:\s\s.exe", @"C:\s", OrigenJuego.Steam)));

                File.WriteAllText(archivo, "{ esto no es json");
                Assert.False(ConfigGaming.Cargar(archivo).SuspenderApps);   // dañado → valores por defecto
            }
            finally
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }

        // ---------- Vigilante ----------

        [Fact]
        public void Vigilante_AvisaAlAbrirYAlCerrarUnJuego()
        {
            using var falso = new ProcesoFalso("GhostModePruebaJuego.exe");
            falso.Cerrar();   // primero cerrado: se abre de nuevo más abajo
            var juego = new Juego("Juego de prueba", falso.Exe, null, OrigenJuego.Manual);
            var vigilante = new VigilanteJuegos();
            var iniciados = new List<IReadOnlyList<ProcesoJuego>>();
            var cerrados = 0;
            vigilante.JuegoIniciado += (j, procesos) => iniciados.Add(procesos);
            vigilante.JuegoCerrado += j => cerrados++;
            vigilante.Vigilar([juego]);

            vigilante.Revisar();
            Assert.Empty(iniciados);

            using Process abierto = Process.Start(new ProcessStartInfo(falso.Exe, "-n 300 127.0.0.1")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            })!;
            try
            {
                vigilante.Revisar();
                ProcesoJuego proceso = Assert.Single(Assert.Single(iniciados));
                Assert.Equal(abierto.Id, proceso.Pid);
                Assert.Equal(falso.Exe, proceso.Ruta, ignoreCase: true);
                Assert.Single(vigilante.JuegosAbiertos);

                vigilante.Revisar();   // sigue abierto: no avisa otra vez
                Assert.Single(iniciados);
                Assert.Equal(0, cerrados);
            }
            finally
            {
                abierto.Kill();
                abierto.WaitForExit();
            }

            vigilante.Revisar();
            Assert.Equal(1, cerrados);
            Assert.Empty(vigilante.JuegosAbiertos);
        }

        [Fact]
        public void Vigilante_PorCarpetaCuentaOtrosExeDelJuego()
        {
            using var lanzador = new ProcesoFalso("GhostModePruebaLanzador.exe");
            var juego = new Juego("Juego por carpeta", Path.Combine(lanzador.Carpeta, "Principal.exe"), lanzador.Carpeta, OrigenJuego.Steam);
            var vigilante = new VigilanteJuegos();
            var procesos = new List<ProcesoJuego>();
            vigilante.JuegoIniciado += (j, p) => procesos.AddRange(p);
            vigilante.Vigilar([juego]);

            vigilante.Revisar();

            Assert.Equal(lanzador.Proceso.Id, Assert.Single(procesos).Pid);
        }

        [Fact]
        public void Vigilante_UnaVueltaEsLigera()
        {
            var vigilante = new VigilanteJuegos();
            vigilante.Vigilar([new Juego("Nada", @"C:\GhostModePrueba\nada.exe", null, OrigenJuego.Manual)]);
            vigilante.Revisar();   // la primera lee la ruta de todos los procesos

            var reloj = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                vigilante.Revisar();
            }
            reloj.Stop();
            // Cada 2 s se hace UNA vuelta: debe tardar muy poco
            Assert.True(reloj.ElapsedMilliseconds / 10.0 < 50, $"Una vuelta tardó {reloj.ElapsedMilliseconds / 10.0} ms");
        }

        // ---------- Prioridad ----------

        [Fact]
        public void Prioridad_SubeAAltaYSeDevuelveLaOriginal()
        {
            using var falso = new ProcesoFalso("GhostModePruebaJuego.exe");
            long inicio = Procesos.Inicio(falso.Proceso.Id)!.Value;
            var proceso = new ProcesoJuego(falso.Proceso.Id, inicio, falso.Exe);
            Assert.Equal(ProcessPriorityClass.Normal, Prioridad(falso));

            try
            {
                Assert.True(PrioridadJuego.Subir(proceso));
                Assert.Equal(ProcessPriorityClass.High, Prioridad(falso));
                Assert.Equal("Normal", GestorEstado.ObtenerOriginal(PrioridadJuego.Modulo, proceso.Clave));
                Assert.False(PrioridadJuego.Subir(proceso));   // ya está alta: no se vuelve a anotar

                PrioridadJuego.RestaurarTodas();

                Assert.Equal(ProcessPriorityClass.Normal, Prioridad(falso));
                Assert.False(GestorEstado.TieneCambios(PrioridadJuego.Modulo));
            }
            finally
            {
                GestorEstado.Limpiar(PrioridadJuego.Modulo);
            }
        }

        [Fact]
        public void Procesos_UnoCerradoNoSigueVivoAunqueAlguienLoTengaAbierto()
        {
            using var falso = new ProcesoFalso("GhostModePruebaJuego.exe");
            string clave = Procesos.Clave(falso.Proceso.Id, Procesos.Inicio(falso.Proceso.Id)!.Value);
            Assert.Equal(falso.Proceso.Id, Procesos.SiSigueVivo(clave));

            // falso.Proceso sigue agarrando el proceso después de cerrarlo (como hace Steam con sus juegos)
            falso.Cerrar();

            Assert.Null(Procesos.SiSigueVivo(clave));
            Assert.Null(Procesos.Inicio(falso.Proceso.Id));
        }

        // ---------- Pausar apps ----------

        [Fact]
        public void Suspender_PausaLaAppYReanudarLaDevuelve()
        {
            using var fondo = new ProcesoFalso("GhostModePruebaFondo.exe");
            try
            {
                List<string> pausadas = SuspensorApps.Suspender([fondo.NombreExe], new HashSet<int>());

                Assert.Equal([fondo.NombreExe], pausadas);
                Assert.True(ProcesoFalso.EstaPausado(fondo.Proceso.Id));
                Assert.Single(GestorEstado.ClavesGuardadas(SuspensorApps.Modulo));

                Assert.Equal(1, SuspensorApps.ReanudarTodas());

                Assert.False(ProcesoFalso.EstaPausado(fondo.Proceso.Id));
                Assert.False(GestorEstado.TieneCambios(SuspensorApps.Modulo));
            }
            finally
            {
                SuspensorApps.ReanudarTodas();
            }
        }

        [Fact]
        public void Suspender_NuncaTocaElJuegoNiQuienLoAbrio()
        {
            // "Launcher" (copia de cmd.exe) abre el "juego" (copia de ping.exe): los dos en la lista,
            // pero el juego se excluye y el launcher es su padre → ninguno se pausa
            string carpeta = Ayudas.CarpetaTemporal();
            string juegoExe = Path.Combine(carpeta, "GhostModePruebaJuego.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), juegoExe);
            using var launcher = new ProcesoFalso("GhostModePruebaLauncher.exe", carpeta,
                                                  Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                                                  $"/c \"\"{juegoExe}\" -n 300 127.0.0.1\"");
            try
            {
                int juegoPid = EsperarHijo(launcher.Proceso.Id);

                List<string> pausadas = SuspensorApps.Suspender(
                    [launcher.NombreExe, "GhostModePruebaJuego.exe"], new HashSet<int> { juegoPid });

                Assert.Empty(pausadas);
                Assert.False(ProcesoFalso.EstaPausado(launcher.Proceso.Id));
                Assert.False(ProcesoFalso.EstaPausado(juegoPid));
            }
            finally
            {
                SuspensorApps.ReanudarTodas();
            }
        }

        [Theory]
        [InlineData("explorer.exe")]
        [InlineData("audiodg.exe")]
        [InlineData("Discord.exe")]
        [InlineData("MsMpEng.exe")]
        [InlineData("svchost.exe")]
        [InlineData("steam.exe")]
        [InlineData("chrome")]          // sin .exe
        [InlineData("..\\malo.exe")]    // con barras
        public void Suspender_RechazaProtegidosYNombresInvalidos(string nombre)
        {
            Assert.NotNull(SuspensorApps.MotivoRechazo(nombre));
            Assert.Empty(SuspensorApps.Suspender([nombre], new HashSet<int>()));
        }

        [Fact]
        public void Suspender_LaListaPorDefectoNoTieneNadaProtegido()
        {
            Assert.All(SuspensorApps.PorDefecto, n => Assert.Null(SuspensorApps.MotivoRechazo(n)));
            Assert.DoesNotContain(SuspensorApps.PorDefecto, n => n.Contains("discord", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Recuperar_TrasUnCierreInesperadoReanudaLoPausado()
        {
            using var fondo = new ProcesoFalso("GhostModePruebaFondo.exe");
            string carpeta = Ayudas.CarpetaTemporal();
            try
            {
                SuspensorApps.Suspender([fondo.NombreExe], new HashSet<int>());
                Assert.True(ProcesoFalso.EstaPausado(fondo.Proceso.Id));

                // "Ghost Mode se cerró de golpe": al abrirse otra vez lee estado.json de cero
                Ayudas.RecargarGestorEstado();
                Assert.True(SesionGaming.QuedoAlgoPendiente());
                var sesion = new SesionGaming(Path.Combine(carpeta, "modo_gaming.json"));
                await sesion.RecuperarAsync();

                Assert.False(ProcesoFalso.EstaPausado(fondo.Proceso.Id));
                Assert.False(SesionGaming.QuedoAlgoPendiente());
            }
            finally
            {
                SuspensorApps.ReanudarTodas();
                Directory.Delete(carpeta, recursive: true);
            }
        }

        // ---------- Memoria en espera ----------

        [Theory]
        [InlineData(500, 4000, true)]     // poca libre y mucha en espera → liberar
        [InlineData(500, 800, false)]     // poca libre pero casi nada que recuperar
        [InlineData(3000, 8000, false)]   // hay libre de sobra
        [InlineData(1024, 4000, false)]   // justo 1 GB libre: no hace falta
        public void Memoria_SoloSeLiberaSiLaLibreEsBaja(long libreMb, long esperaMb, bool esperado)
        {
            var m = new MemoriaStandby.EstadoMemoria(libreMb * MemoriaStandby.Mb, esperaMb * MemoriaStandby.Mb);
            Assert.Equal(esperado, MemoriaStandby.HaceFalta(m));
        }

        [Fact]
        public void Memoria_LeeLosContadoresYWindowsPermiteLiberar()
        {
            MemoriaStandby.EstadoMemoria? m = MemoriaStandby.Leer();
            Assert.NotNull(m);
            Assert.True(m!.LibreBytes > 0);
            Assert.True(m.EsperaBytes >= 0);
            // Solo descarta caché de archivos (Windows la vuelve a leer del disco si hace falta)
            Assert.True(MemoriaStandby.Liberar(), "Windows no dio permiso para vaciar la memoria en espera");
        }

        // ---------- La sesión completa ----------

        [Fact]
        public async Task Sesion_AlAbrirAplicaTodoYAlCerrarLoDejaExactamenteIgual()
        {
            string carpeta = Ayudas.CarpetaTemporal();
            using var fondo = new ProcesoFalso("GhostModePruebaFondo.exe");
            AjusteRegistro[] ajustes = [.. Ayudas.AjustesDe<GameMode>(), .. Ayudas.AjustesDe<EfectosVisuales>()];
            Dictionary<string, ValorRegistro> registroAntes = Ayudas.Foto(ajustes);
            string planAntes = Ayudas.PlanActivo();
            Assert.False(AplicadorOptimizaciones.GhostMode.All(o => o.EstaActiva()), "Ghost Mode ya estaba activo: la prueba no demostraría nada");

            var sesion = new SesionGaming(Path.Combine(carpeta, "modo_gaming.json"));
            sesion.Config.PrioridadAlta = true;
            sesion.Config.SuspenderApps = true;
            sesion.Config.AppsSuspender = [fondo.NombreExe];   // SOLO la app falsa: nunca el navegador real
            sesion.Config.LiberarRam = true;
            sesion.Config.OptimizacionesInicio = true;

            ProcesoFalso? juego = null;
            try
            {
                string juegoExe = Path.Combine(Ayudas.CarpetaTemporal(), "GhostModePruebaJuego.exe");
                File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), juegoExe);
                sesion.AgregarManual(juegoExe);   // a mano = activación automática encendida
                Directory.Delete(Path.GetDirectoryName(juegoExe)!, recursive: true);

                sesion.RevisarAhora();
                Assert.False(sesion.Activa);

                // Se abre el juego
                juego = new ProcesoFalso("GhostModePruebaJuego.exe", Path.GetDirectoryName(juegoExe));
                sesion.RevisarAhora();
                await sesion.TrabajoEnCurso;

                Assert.True(sesion.Activa);
                Assert.Equal(ProcessPriorityClass.High, Prioridad(juego));
                Assert.True(ProcesoFalso.EstaPausado(fondo.Proceso.Id));
                Assert.All(AplicadorOptimizaciones.GhostMode, o => Assert.True(o.EstaActiva(), o.Id));
                Assert.True(SesionGaming.QuedoAlgoPendiente());

                // Se cierra el juego
                juego.Cerrar();
                sesion.RevisarAhora();
                await sesion.TrabajoEnCurso;

                Assert.False(sesion.Activa);
                Assert.False(ProcesoFalso.EstaPausado(fondo.Proceso.Id));
                Assert.Equal(planAntes, Ayudas.PlanActivo());
                Assert.Equal(registroAntes, Ayudas.Foto(ajustes));
                Assert.False(SesionGaming.QuedoAlgoPendiente());
                Assert.All(AplicadorOptimizaciones.GhostMode, o => Assert.False(GestorEstado.TieneCambios(o.Id), o.Id));
            }
            finally
            {
                // Red de seguridad si algo falló a mitad
                juego?.Dispose();
                SuspensorApps.ReanudarTodas();
                PrioridadJuego.RestaurarTodas();
                GestorEstado.Limpiar(SesionGaming.Modulo);
                if (Ayudas.PlanActivo() != planAntes)
                {
                    Ayudas.Powercfg($"/setactive {planAntes}");
                }
                Ayudas.RestaurarFoto(ajustes, registroAntes);
                foreach (IOptimizacion o in AplicadorOptimizaciones.GhostMode)
                {
                    GestorEstado.Limpiar(o.Id);
                }
                Directory.Delete(carpeta, recursive: true);
            }
        }

        // ---------- Ayudas ----------

        // Crea steamapps\appmanifest_ID.acf y la carpeta del juego con .exe de esos tamaños. Devuelve la carpeta
        private static string CrearJuegoSteam(string biblioteca, string appId, string nombre, string carpeta,
                                              params (string Exe, int Bytes)[] exes)
        {
            string steamapps = Path.Combine(biblioteca, "steamapps");
            string instalacion = Path.Combine(steamapps, "common", carpeta);
            Directory.CreateDirectory(instalacion);
            File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{appId}.acf"),
                $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"name\"\t\t\"{nombre}\"\n\t\"installdir\"\t\t\"{carpeta}\"\n" +
                "\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\t\"spanish\"\n\t}\n}\n");
            foreach (var (exe, bytes) in exes)
            {
                string ruta = Path.Combine(instalacion, exe);
                Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
                File.WriteAllBytes(ruta, new byte[bytes]);
            }
            return instalacion;
        }

        private static ProcessPriorityClass Prioridad(ProcesoFalso falso)
        {
            falso.Proceso.Refresh();
            return falso.Proceso.PriorityClass;
        }

        // El PID del proceso que abrió "padre" (espera hasta 5 s a que aparezca)
        private static int EsperarHijo(int padre)
        {
            for (int i = 0; i < 50; i++)
            {
                if (Procesos.Listar().FirstOrDefault(p => p.PadrePid == padre && p.NombreExe != "conhost.exe") is ProcesoVivo hijo)
                {
                    return hijo.Pid;
                }
                Thread.Sleep(100);
            }
            throw new TimeoutException("El launcher de prueba no abrió el juego");
        }
    }
}
