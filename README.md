# Co-ophead

Experimento de multijugador cooperativo nativo para **Cuphead en Windows/Steam**.
En lugar de transmitir video como Steam Remote Play, cada equipo ejecuta su propia
copia del juego y el mod intercambia entradas y una cantidad pequeña de estado.

## Descargar la prueba 0.11.2

**[Descargar Co-ophead 0.11.2 para Windows x64](https://github.com/gilomx/co-ophead/raw/refs/heads/main/releases/Coophead-0.11.2-Windows-x64.zip)**

Es un paquete universal: el host y el invitado instalan exactamente el mismo ZIP.
Ya incluye BepInEx 5.4.23.4, Co-ophead y la configuración P2P; no requiere VPN,
abrir puertos ni instalar programas adicionales.

1. Cada jugador extrae el contenido del ZIP en la carpeta que contiene `Cuphead.exe`.
2. Ambos abren Cuphead y pulsan `F6`.
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
- **Modelo:** host autoritativo híbrido; predicción local para el jugador invitado y
  correcciones del host.
- **Transporte previsto:** Steam Networking/P2P para invitaciones y NAT traversal.
- **Compatibilidad inicial:** dos jugadores, misma versión de Cuphead, DLC opcional
  solo después de estabilizar el juego base.

Consulta [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) para las decisiones y fases.
La superficie de integración confirmada está en [docs/GAME_API.md](docs/GAME_API.md).

## Estado

La descarga estable sigue siendo `0.11.2`. La rama principal prepara `0.12.4`: añade
**CO-OPHEAD** al primer menú con la misma tipografía y composición del frontend,
permite copiar el código de sala, muestra el reloj de arena original durante la
conexión y separa correctamente Player One/Player Two en el invitado.

En una sesión, el host elige el único save autoritativo. El invitado no escribe su
progreso local mientras está conectado. Cuphead asigna automáticamente al segundo
jugador el personaje opuesto al primero, evitando personajes duplicados.
En este MVP todavía no se copia el contenido completo del save: ambos equipos deben
tener progreso compatible en el mismo número de slot para recorrer los mismos mapas.

### Compilar

```powershell
dotnet build .\src\Coophead\Coophead.csproj -c Debug
```

En esta máquina Cuphead está en
`C:\Program Files (x86)\Steam\steamapps\common\Cuphead`. Se puede definir
`CUPHEAD_PATH` como propiedad de MSBuild o copiar `Directory.Build.props.example` a
`Directory.Build.props` y editarla.

Para probar manualmente, copia `src/Coophead/bin/Debug/net35/Coophead.dll` a
`Cuphead/BepInEx/plugins/Coophead/`, inicia el juego y busca `Co-ophead 0.12.4
cargado` en `BepInEx/LogOutput.log`.

### Remote Input Lab

La versión de desarrollo `0.12.4` puede crear y controlar localmente a Player Two sin un segundo
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

En Internet o LAN, el invitado usa los controles que tenga configurados para Player
One, envía ese frame y lo aplica como predicción a Player Two. Como respaldo para
el movimiento puede usar las flechas, `WASD`, `Numpad 4/6` (izquierda/derecha) y
`Numpad 2/8` (abajo/arriba). Se leen ambos perfiles locales de Rewired para tolerar
la asignación de dispositivos de una VM. Su Player One local queda reservado para representar al host. Al
desactivar el laboratorio, Cuphead recupera sus entradas.

Las entradas pasan por un transporte loopback con tres frames de latencia simulada.
El teclado produce `InputFrame`; los parches consumen únicamente frames entregados por
el transporte. Así podremos cambiar loopback por LAN o Steam P2P sin reescribir la
integración con el juego.

También está disponible el primer transporte UDP para dos equipos en una red local.
Consulta [docs/LAN_TEST.md](docs/LAN_TEST.md). La posición de ambos jugadores ya se
corrige con snapshots básicos tanto en el mapa como dentro de niveles; enemigos,
animaciones y combate todavía requieren una
sincronización más completa.

## Alcance y límites

Este es un proyecto independiente y no está afiliado con Studio MDHR, Microsoft ni
Valve. No incluirá DLL, arte, audio ni otros archivos del juego. Cada jugador deberá
poseer su propia copia.
