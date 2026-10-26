# Ghost Mode

Optimizador de Windows para jugar en PCs de bajos recursos. Aplica ajustes reversibles (plan de energía,
Modo Juego, efectos visuales, GPU dedicada para FiveM), limpia archivos temporales y cachés, controla los
programas que arrancan con Windows y mide los FPS antes y después con PresentMon.

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

## Autor

Miguel Ángel González Zuluaga — [@Exoticdevelopment](https://github.com/Exoticdevelopment)

## Licencia

MIT. Consulta el archivo [LICENSE](LICENSE).
