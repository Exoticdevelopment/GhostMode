# Investigación: optimizaciones de rendimiento para FiveM

Informe previo a programar nada. Para cada ajuste: qué hace, impacto real (con fuentes), riesgos,
si es reversible y la decisión propuesta. Al final, un orden por impacto frente a riesgo.

**Cómo se hizo.** Se revisaron [hellzerg/optimizer](https://github.com/hellzerg/optimizer) (GPL-3.0: solo
ideas, nada de código) y [ChrisTitusTech/winutil](https://github.com/ChrisTitusTech/winutil), únicamente
en lo que toca al rendimiento en juegos, y se contrastó cada ajuste con documentación oficial (Microsoft,
NVIDIA, Intel) o, si no la hay, se dice. **FiveM no está instalado en este PC**, así que nada de lo
específico de FiveM se ha podido medir aquí: se marca como "por verificar".

Equipo de referencia (el de pruebas): Intel i5-13420H (núcleos P + E), 16 GB, RTX 3050 Laptop (Optimus), Wi-Fi.

---

## Qué hacen las dos herramientas (resumen)

**hellzerg/optimizer** — en su código (`OptimizeHelper.cs`):
- *EnablePerformanceTweaks*: `SystemResponsiveness = 1`, perfil MMCSS "Games" (GPU Priority 8, Priority 6,
  Scheduling Category High), además de ajustes de Explorer y desactivar servicios de telemetría.
- *DisableNetworkThrottling*: `NetworkThrottlingIndex = 0xFFFFFFFF`, `NonBestEffortLimit = 0`.
- *EnableGamingMode*: Game Mode automático, programación de GPU acelerada por hardware (`HwSchMode = 2`) y
  `GameDVR_FSEBehaviorMode = 2`.
- *DisableGameBar*: apaga la captura de Game DVR.
- Opción de línea de comandos `/disablehpet`.

**winutil** — en `config/tweaks.json`: desactivar MPO (`OverlayTestMode = 5` y, en modo "completo",
`DisableOverlays = 1`), Game Mode, plan "Máximo rendimiento" (marcado "NOT FOR LAPTOPS") y Teredo.
Ninguna de las dos toca el temporizador a 0,5 ms, NVAPI, la afinidad ni el `settings.xml` de GTA V.

Ghost Mode **ya cubre** el plan de energía, Game Mode, efectos visuales y la GPU dedicada para FiveM.

---

## 1. Preset gráfico de GTA V (`settings.xml`) con respaldo

**Qué hace.** GTA V guarda la configuración gráfica en `Documentos\Rockstar Games\GTA V\settings.xml`.
Un "preset" escribiría valores más bajos (sombras, hierba, reflejos, MSAA, distancia de detalle…) tras
guardar una copia del archivo original.

**Impacto real.** Es, con diferencia, lo que más FPS da: la propia guía de la comunidad de Cfx.re para
diagnosticar FPS bajos empieza por bajar todo a normal/bajo y subir ajuste por ajuste
([forum.cfx.re](https://forum.cfx.re/t/fivem-low-fps-lags-stuttering/180572)). En equipos con poca VRAM
(la RTX 3050 Laptop tiene 6 GB) la textura y la hierba son las que más pesan.

**Riesgos.**
- **No está confirmado que FiveM siga leyendo ese archivo.** Varios usuarios del foro oficial informan de
  que en builds recientes cambiar `settings.xml` ya no tiene efecto y de que el menú de FiveM difiere del de
  GTA V ([1](https://forum.cfx.re/t/help-where-is-the-new-settings-located/4763451),
  [2](https://forum.cfx.re/t/fivem-stuttering-settings-completely-different-from-gta/1366118)). No hay
  documentación oficial que diga dónde se guarda ahora.
- Si el juego está abierto, sobrescribe el archivo al salir (hay que aplicarlo con el juego cerrado).
- Un preset "para todos" puede empeorar la imagen sin ganar nada si el cuello de botella es la CPU.

**Reversible.** Sí, restaurando la copia (byte a byte).

**Decisión.** Prometedor pero **bloqueado hasta verificar con FiveM instalado** dónde lee hoy los ajustes.
Propuesta: primero un modo "solo lectura" que muestre la configuración actual y avise de las opciones caras.

---

## 2. Temporizador del sistema a 0,5 ms durante el juego

**Qué hace.** Pide a Windows que el reloj de interrupciones corra a 0,5 ms en vez de 15,6 ms.

**Impacto real.** Pequeño o nulo hoy, y discutido:
- Desde Windows 10 2004, `timeBeginPeriod` es **por proceso**: lo que pida Ghost Mode no afecta al juego, y
  los juegos que lo necesitan ya lo piden ellos mismos. En Windows 11, además, deja de garantizarse si la
  ventana del proceso está minimizada u oculta
  ([Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod)).
- Para que sea "global" hace falta el valor `GlobalTimerResolutionRequests` (Windows 11), sin documentación
  oficial y que su propia herramienta de referencia recomienda **solo para depuración**
  ([valleyofdoom/TimerResolution](https://github.com/valleyofdoom/TimerResolution)).
- 0,5 ms solo se consigue con `NtSetTimerResolution` (no documentada). Los resultados en foros son mixtos e
  incluso hay quien mide más latencia ([Guru3D](https://forums.guru3d.com/goto/post?id=6122297),
  [Blur Busters](https://forums.blurbusters.com/viewtopic.php?p=110633)).

**Riesgos.** Más consumo y temperatura (en un portátil, menos batería); API no documentada.

**Reversible.** Sí (se suelta al terminar), pero el valor de registro global requiere cuidado.

**Decisión.** **Descartar** como placebo para la mayoría. No compensa en un optimizador "honesto".

---

## 3. Perfil de NVIDIA con NVAPI (energía máxima, baja latencia)

**Qué hace.** NVAPI tiene la API DRS (Driver Settings) para leer y escribir el perfil del driver de una
aplicación, lo mismo que el Panel de control de NVIDIA ([DRS API](https://docs.nvidia.com/nvapi/group__drsapi.html)).
El SDK público está en [NVIDIA/nvapi](https://github.com/NVIDIA/nvapi) con **licencia MIT**; la
implementación la trae el driver instalado.
- *Modo de control de energía = preferir máximo rendimiento*: evita que la GPU baje y suba de reloj.
- *Modo de baja latencia (On/Ultra)*: limita la cola de fotogramas que prepara la CPU.

**Impacto real.**
- Energía máxima: puede quitar tirones por subidas de reloj en equipos que bajan mucho la frecuencia; el
  efecto en FPS medios suele ser pequeño. En portátil sube consumo y temperatura.
- Baja latencia: **solo ayuda cuando la GPU es el límite**; si el límite es la CPU (habitual en FiveM con
  muchos jugadores y scripts), "Ultra" puede dar más tirones
  ([Blur Busters](https://forums.blurbusters.com/viewtopic.php?p=44683),
  [How-To Geek](https://www.howtogeek.com/437761/how-to-enable-ultra-low-latency-mode-for-nvidia-graphics)).
  GTA V no tiene NVIDIA Reflex, que sería la opción buena.

**Riesgos.** Solo NVIDIA; tocar el perfil equivocado afecta a otros juegos (hay que usar un perfil propio
para los .exe de FiveM, no el global). Integración nativa nueva (como NVML, cargando la DLL del driver).

**Reversible.** Sí: DRS permite leer el valor anterior de cada ajuste y restaurarlo, o borrar el perfil
creado. Se guardaría en GestorEstado como el resto.

**Decisión.** **Implementar solo "energía máxima"**, en un perfil propio de FiveM y únicamente durante
la sesión de Modo Gaming. Baja latencia: **no por defecto**, como mucho una opción avanzada con su aviso.

---

## 4. Desactivar MPO (Multiplane Overlay)

**Qué hace.** MPO deja que el compositor de Windows mezcle capas en hardware. El ajuste conocido es
`HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode = 5` (winutil añade `DisableOverlays = 1`).

**Impacto real.** **No es una mejora de rendimiento**: es la solución de NVIDIA a parpadeos y tirones en el
escritorio y en apps con ventana con ciertos drivers (artículo de soporte de NVIDIA posterior al driver
461.09, citado en [Reddit](https://lr.vern.cc/r/Amd/comments/yvyqc7/disabling_multiplane_overlay_mpo_fixed_all/) y
[Adobe](https://community.adobe.com/t5/premiere-pro-discussions/nvidia-g-sync-causing-intermittent-stuttering-in-premiere-and-after-effects/m-p/13046264)).
En Windows 11 reciente hay informes de que el valor ya no surte efecto
([Guru3D](https://forums.guru3d.com/goto/post?id=6305178)).

**Riesgos.** Afecta a todo el sistema, necesita reiniciar, y algunos usuarios dicen que rompe VRR/G-Sync o
que hay juegos que necesitan MPO.

**Reversible.** Sí (borrar el valor y reiniciar).

**Decisión.** **No va en Ghost Mode ni en Modo Gaming.** Como mucho, una opción de "solución de problemas"
para quien tenga parpadeos, con su aviso y el reinicio explicado.

---

## 5. Afinidad de CPU

**Qué hace.** Obliga al juego a usar solo ciertos núcleos (por ejemplo, solo los P-cores del i5-13420H).

**Impacto real.** Intel desaconseja fijar la afinidad: "estarás peleando contra el sistema operativo y el
hardware"; recomienda dar contexto al planificador en vez de atar hilos
([Intel](https://www.intel.com/content/www/us/en/developer/articles/technical/optimizing-threading-for-gaming-performance.html)).
Intel cifra en ~1 % la diferencia entre usar o no los E-cores con el planificador actual
([PCGH](https://www.pcgameshardware.de/CPU-CPU-154106/News/Intel-E-Core-Kritik-Scheduler-Probleme-Gaming-1526293/)).
Los informes de usuarios son contradictorios. Además, el diseño (núcleo reservado para Windows) quitaría un
núcleo a un juego que suele estar limitado por CPU.

**Riesgos.** Puede bajar FPS y dar tirones; el antitrampas puede no permitirlo.

**Reversible.** Sí (muere con el proceso).

**Decisión.** **Descartar.** Ya damos prioridad alta, que es la forma suave de "dar contexto".

---

## 6. Ajustes oficiales del cliente de FiveM

**Qué hay.** FiveM añade opciones propias a los gráficos (por ejemplo "Extended Texture Budget") y tiene
su consola (`cl_drawperf true` muestra FPS y pérdida). **No se encontró documentación oficial de Cfx.re**
sobre estas opciones ni sobre dónde se guardan; lo que circula son guías comerciales con cifras que se
contradicen entre sí ([FiveMX](https://fivemx.com/blog/fivem-how-to-fix-texture-loss/)) o blogs
([Hone](https://hone.gg/blog/fivem-stuttering-low-fps)).

**Impacto real.** El presupuesto de texturas evita texturas borrosas, pero si se sube por encima de la VRAM
causa tirones: depende de cada GPU.

**Decisión.** **No tocar automáticamente** sin documentación oficial. Sí podemos **informar**: mostrar la
VRAM de la GPU y una recomendación honesta ("con 4 GB de VRAM, no subas el presupuesto de texturas").

---

## 7. Otros de hellzerg/winutil, ya evaluados

| Ajuste | Veredicto | Motivo |
|---|---|---|
| `SystemResponsiveness`, perfil MMCSS "Games" | Placebo | Solo afecta a hilos registrados en MMCSS; GTA V/FiveM no consta que lo use. |
| `NetworkThrottlingIndex` | Placebo | Limita paquetes de multimedia, no el tráfico del juego; un commit de winutil lo retiró de sus ajustes ([espejo del commit](https://git.binarymagic.net/AIR/winutil/commit/ff694a5d6b7d54423eebfc98b09a2f5d2b7832ed)). |
| Desactivar HPET (`/disablehpet`) | Descartar | Cambio de arranque (bcdedit), efecto discutido, no es por partida. |
| HAGS (`HwSchMode`) | No tocar | Requiere reiniciar y el efecto depende del driver; mejor que lo decida el usuario en Windows. |
| Grabación en segundo plano de Game DVR | **Útil y barato** | Si está activada, grabar continuamente cuesta rendimiento. Se puede apagar durante la partida y restaurar. |
| Teredo | Descartar | Cambio de red global; puede romper el chat o el multijugador de algunos juegos. |

---

## Orden propuesto (impacto frente a riesgo)

1. **Apagar la grabación en segundo plano de Game DVR durante la partida** — impacto medio cuando está
   activa, riesgo bajo, reversible al cerrar el juego. Encaja en Modo Gaming.
2. **Perfil NVIDIA "preferir máximo rendimiento" solo para FiveM y solo durante la partida** — impacto
   bajo/medio (menos tirones por reloj), riesgo bajo, reversible con DRS. Solo NVIDIA.
3. **Diagnóstico de gráficos de FiveM (solo lectura)** — mostrar VRAM y ajustes caros con recomendaciones.
   Paso previo al preset.
4. **Preset gráfico con respaldo** — el de mayor impacto, pero **solo después de verificar con FiveM
   instalado** dónde lee los ajustes hoy.
5. **MPO** — solo como "solución de problemas" para parpadeos, fuera de Ghost Mode.

**Descartados por placebo o riesgo:** temporizador a 0,5 ms, afinidad de CPU, `SystemResponsiveness`,
`NetworkThrottlingIndex`, HPET, Teredo y el modo de baja latencia "Ultra" por defecto.

Pendiente de tu aprobación antes de implementar.
