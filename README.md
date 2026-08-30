# Co-ophead

Experimento de multijugador cooperativo nativo para **Cuphead en Windows/Steam**.
En lugar de transmitir video como Steam Remote Play, cada equipo ejecuta su propia
copia del juego y el mod intercambia entradas y una cantidad pequeña de estado.

## Descargar la prueba 0.12.10

**[Descargar Co-ophead 0.12.10 para Windows x64](https://github.com/gilomx/co-ophead/raw/refs/heads/main/releases/Coophead-0.12.10-Windows-x64-PRUEBA-NORMAL.zip)**

Es un paquete universal: el host y el invitado instalan exactamente el mismo ZIP.
Ya incluye BepInEx 5.4.23.4, Co-ophead y la configuración P2P; no requiere VPN,
abrir puertos ni instalar programas adicionales.

1. Cada jugador extrae el contenido del ZIP en la carpeta que contiene `Cuphead.exe`.
2. Ambos abren Cuphead y seleccionan **CO-OPHEAD** en el primer menú.
3. Uno selecciona **Crear partida** y comparte el código de seis caracteres.
4. El otro escribe el código y selecciona **Unirse**.

Esta es una compilación preliminar. La conexión es directa entre ambos equipos y
puede fallar cuando alguna red usa NAT simétrico o CGNAT; todavía no hay relay de
respaldo porque el objetivo actual es mantener el servicio sin costo.

## Objetivo inicial

Conseguir que dos computadoras ejecuten una partida vanilla de dos jugadores:

- el host controla a Player One y la simulación principal;
- el invitado controla a Player Two;
- ambos cargan la misma escena y partida;
- por la red viajan entradas, eventos y correcciones de estado, no audio ni video.

El primer hito usa un lobby pequeño dentro del frontend e inyecta las entradas del
invitado en Player Two. Así se puede validar la parte difícil antes de ampliar la
sincronización de combate.

## Enfoque técnico

- **Carga del mod:** BepInEx 5 para Cuphead (Unity/Mono).
- **Intercepción:** Harmony, manteniendo intactos los archivos originales del juego.
- **Modelo:** host autoritativo híbrido; el mapa se representa desde snapshots del
  host y Player Two conserva predicción local durante los combates.
- **Transporte previsto:** Steam Networking/P2P para invitaciones y NAT traversal.
- **Compatibilidad inicial:** dos jugadores, misma versión de Cuphead, DLC opcional
  solo después de estabilizar el juego base.

Consulta [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) para las decisiones y fases.
La superficie de integración confirmada está en [docs/GAME_API.md](docs/GAME_API.md).

## Pruebas pendientes

La separación de controles, el nuevo buffer de movimiento y la reanimación nativa
están pendientes de validación manual en dos equipos. El
procedimiento y los resultados esperados están en
[docs/PENDING_TESTS.md](docs/PENDING_TESTS.md).

## Estado

La compilación de desarrollo y el ZIP universal de prueba actuales son `0.12.10`.
Esta versión añade **CO-OPHEAD** al primer menú con la misma tipografía y composición del frontend,
permite copiar el código de sala, muestra el reloj de arena original durante la
conexión y separa correctamente Player One/Player Two en el invitado. También muestra
ping y pérdida estimada, evita que los snapshots retrasados frenen a Player Two y
pausa ambos juegos si uno deja de enviar frames, con reanudación coordinada en tres
segundos. La prueba vuelve al comportamiento normal de Cuphead: al perder el foco el
juego deja de ejecutarse en segundo plano y no se aplica ningún filtro experimental.

El invitado anuncia el equipamiento de su Player One local y el host lo usa como
loadout temporal de Player Two: arma primaria/secundaria, súper, amuleto y vida
máxima coinciden en ambas PCs. El host también transmite su propio loadout al
invitado. Estos overlays no modifican los objetos del save y desaparecen al cerrar
la sesión. El lobby no habilita **EMPEZAR** hasta recibir el equipamiento del
invitado, y cada carga coordinada espera el contexto de la misma transición.

La entrada a niveles y el movimiento del mapa pertenecen ahora al host. El invitado
envía acciones, pero representa a ambos personajes desde un buffer corto de snapshots;
sus colisiones y su progreso local ya no pueden regresarlo varios segundos después.
En combate, Player One remoto usa el mismo buffer para evitar teletransportes y Player
Two conserva predicción local. Las solicitudes EX/Super se conservan hasta que el host
pueda procesarlas.

La entrada física del invitado se lee sólo desde su perfil local de Player One y se
convierte a acciones semánticas antes de enviarse. Ya no se suman Player One, Player
Two y teclas fijas. Los globos de interacción muestran el binding real del invitado.
La revive de Goopy sigue el `PlayerDeathEffect` nativo una sola vez, por lo que el
fantasma puede terminar su animación y destruirse correctamente.

Una salida voluntaria envía una despedida explícita: el host quita a Player Two,
reanuda el juego inmediatamente y muestra un aviso. **Remover Player Two** cierra la
conexión en ambos extremos y vuelve al inicio, sin reincorporación automática. Los
errores de código inexistente, sala llena y versiones diferentes se muestran con un
mensaje legible. El menú de espera selecciona **Seguir esperando** de forma
predeterminada y acepta teclado o mando.

Al entrar a un nivel, ambos equipos conservan el iris y el reloj de arena hasta que
el invitado confirma que `Level.Start` terminó. Si la espera supera dos segundos, el
host muestra `TU INVITADO TALENTO...`; al quedar ambos listos, el iris se abre con la
transición original y sin una cuenta regresiva visible.

> **Nota de prueba:** `RunInBackground = false` y
> `BlockLocalInputWhenUnfocused = false`. Cuphead usa su comportamiento normal al
> cambiar de ventana; el código del filtro se conserva sólo como opción diagnóstica.

En una sesión, el host elige el único save autoritativo. El invitado no escribe su
progreso local mientras está conectado: Co-ophead bloquea las rutas nativas de guardado
y restaura al salir una copia en memoria de cada slot prestado. Cuphead asigna
automáticamente al segundo jugador el personaje opuesto al primero, evitando personajes
duplicados.
Como todavía no existe un selector de save para el invitado, `0.12.10` toma el
loadout de Player One del slot local que Cuphead tenga activo al pulsar **UNIRSE**
(normalmente el primer slot) y lo presta temporalmente a Player Two. El catálogo,
las victorias y el mapa continúan siendo los del save autoritativo del host.
En este MVP todavía no se copia el contenido completo del save. El host decide
movimiento, colisiones y entrada a niveles, pero el invitado aún puede ver elementos
del mapa distintos si su progreso local no coincide. La copia temporal del progreso
del host sigue siendo una fase posterior.

### Compilar

```powershell
dotnet build .\src\Coophead\Coophead.csproj -c Debug
```

En esta máquina Cuphead está en
`C:\Program Files (x86)\Steam\steamapps\common\Cuphead`. Se puede definir
`CUPHEAD_PATH` como propiedad de MSBuild o copiar `Directory.Build.props.example` a
`Directory.Build.props` y editarla.

Para probar manualmente, copia `src/Coophead/bin/Debug/net35/Coophead.dll` a
`Cuphead/BepInEx/plugins/Coophead/`, inicia el juego y busca `Co-ophead 0.12.10
cargado` en `BepInEx/LogOutput.log`.

### Remote Input Lab

La versión de desarrollo `0.12.10` puede crear y controlar localmente a Player Two sin un segundo
mando. Pulsa `F8` desde el título y después entra normalmente a una partida guardada.
Cuphead creará a ambos jugadores al cargar el mapa:

Player Two no equivale siempre a Mugman: si Player One usa Mugman, el segundo slot
será Cuphead, respetando el comportamiento cooperativo nativo del juego.

En el primer menú selecciona **CO-OPHEAD**. El anfitrión crea la sala, copia el código
y espera al invitado; cuando se conecte verá **EMPEZAR** y elegirá el save autoritativo.
El invitado escribe o pega el código de seis caracteres en la fila con cursor y queda
esperando al anfitrión, sin abrir un save local. `F6` conserva el panel de diagnóstico
como respaldo y `F8` sigue activando el laboratorio loopback.

- `Numpad 4/6`: izquierda/derecha;
- `Numpad 2/8`: abajo/arriba;
- `Numpad 0`: salto/parry;
- `Numpad 1`: disparo;
- `Numpad 3`: dash;
- `Numpad 5`: fijar dirección;
- `Numpad 7`: cambiar arma;
- `Numpad 9`: super;
- `Numpad Enter`: pausa;
- `Numpad .`: swap;
- `F8`: activar el laboratorio; repetirlo no lo desactiva;
- `F7`: desactivar el laboratorio.

En Internet o LAN, el invitado usa exclusivamente los controles que tenga configurados
para Player One. Co-ophead traduce ese perfil local a acciones (`salto`, `disparo`,
`dash`, ejes, etc.) y el host las aplica a Player Two; no copia identificadores de
teclado o mando entre equipos. Su Player One local queda reservado para representar
al host. Las teclas de Numpad anteriores son sólo para el laboratorio loopback.

Las entradas pasan por un transporte loopback con tres frames de latencia simulada.
El teclado produce `InputFrame`; los parches consumen únicamente frames entregados por
el transporte. Así podremos cambiar loopback por LAN o Steam P2P sin reescribir la
integración con el juego.

También está disponible el primer transporte UDP para dos equipos en una red local.
Consulta [docs/LAN_TEST.md](docs/LAN_TEST.md). La posición se representa desde
snapshots autoritativos en el mapa. Dentro de niveles, Player One se suaviza desde el
host y Player Two conserva predicción local. Si cualquiera de los juegos deja de producir
frames, la sesión se oscurece y queda pausada hasta completar una cuenta regresiva
coordinada. Enemigos, animaciones y combate todavía requieren una sincronización más
completa.

## Alcance y límites

Este es un proyecto independiente y no está afiliado con Studio MDHR, Microsoft ni
Valve. No incluirá DLL, arte, audio ni otros archivos del juego. Cada jugador deberá
poseer su propia copia.
