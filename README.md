# Ghost Mode

Optimizador de Windows para jugar en PCs de bajos recursos. Aplica ajustes reversibles (plan de energía,
Modo Juego, efectos visuales, GPU dedicada para FiveM), limpia archivos temporales y cachés, controla los
programas que arrancan con Windows y mide los FPS antes y después con PresentMon.

Modo Gaming detecta los juegos de Steam, Epic Games y FiveM (o los que agregues a mano) y, al abrir uno,
aplica solo lo que elijas: prioridad alta, pausar apps de fondo, liberar memoria en espera si queda poca y
las optimizaciones de Inicio. Al cerrar el juego lo deshace todo, y si Ghost Mode se cierra a mitad de
partida, al volver a abrirse reanuda lo que quedó pausado.

Red mide la latencia real de los DNS (Automático, Cloudflare, Google, Quad9) con consultas DNS, recomienda el
más rápido y lo aplica al adaptador activo guardando el original; muestra el ping en vivo de los últimos
60 s (también a la IP de un servidor de FiveM) y los datos del adaptador. Sin "trucos" de red sin respaldo.

Vive en la bandeja del sistema mientras Modo Gaming vigila tus juegos (menú con Abrir, Activar/Desactivar
Ghost Mode, Deshacer todo y Salir), puede iniciarse con Windows minimizado (tarea programada con privilegios
elevados, sin aviso de UAC en cada arranque) y se activa o desactiva con un atajo global (`Ctrl+Alt+G` por
defecto, configurable en Ajustes). La primera vez muestra una bienvenida con un diagnóstico rápido del equipo.

Todos los cambios se pueden deshacer: Ghost Mode guarda el valor original antes de modificar nada y crea un
punto de restauración antes de aplicar optimizaciones. Nunca desactiva Windows Defender ni Windows Update.

Hecho en C# con WPF y .NET 10.

## Requisitos

- Windows 10 u 11 (64 bits). La app pide permisos de administrador.
- Para compilar: [.NET 10 SDK](https://dotnet.microsoft.com/download).

## Compilar

```
dotnet build
```

Para generar un único `.exe` que no necesita instalar .NET:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

El resultado queda en `bin/Release/net10.0-windows/win-x64/publish/OptimizadorPC.exe`.

## Pruebas

Las pruebas de `tests/OptimizadorPC.Pruebas` (xUnit) ejecutan la lógica real contra Windows, sin la interfaz:
aplican y revierten cada optimización y comprueban que todo vuelve exactamente a como estaba. Necesitan una
terminal de administrador e internet (descargan PresentMon). Cierra Ghost Mode antes de ejecutarlas.

```
dotnet test tests/OptimizadorPC.Pruebas
```

## Autor

Miguel Ángel González Zuluaga — [@Exoticdevelopment](https://github.com/Exoticdevelopment)

## Licencia

MIT. Consulta el archivo [LICENSE](LICENSE).
