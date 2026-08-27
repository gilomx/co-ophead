# Co-ophead

Experimento de multijugador cooperativo nativo para **Cuphead en Windows/Steam**.
En lugar de transmitir video como Steam Remote Play, cada equipo ejecuta su propia
copia del juego y el mod intercambia entradas y una cantidad pequeña de estado.

## Objetivo inicial

Conseguir que dos computadoras ejecuten una partida vanilla de dos jugadores:

- el host controla a Player One y la simulación principal;
- el invitado controla a Player Two;
- ambos cargan la misma escena y partida;
- por la red viajan entradas, eventos y correcciones de estado, no audio ni video.

El primer hito no tendrá lobby, interfaz ni matchmaking. Será una prueba local que
inyecte entradas remotas en Player Two. Así podremos validar la parte difícil antes
de elegir el transporte definitivo.

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

Fase 0 en curso: el plugin mínimo vive en `src/Coophead`, registra su versión y cada
escena cargada en `BepInEx/LogOutput.log`.

### Compilar

```powershell
dotnet build .\src\Coophead\Coophead.csproj -c Debug
```

La ruta detectada en esta máquina es `E:\SteamLibrary\steamapps\common\Cuphead`.
En otra instalación se puede definir `CUPHEAD_PATH` como propiedad de MSBuild o copiar
`Directory.Build.props.example` a `Directory.Build.props` y editarla.

Para probar manualmente, copia `src/Coophead/bin/Debug/net35/Coophead.dll` a
`Cuphead/BepInEx/plugins/Coophead/`, inicia el juego y busca `Co-ophead 0.2.0
cargado` en `BepInEx/LogOutput.log`.

### Remote Input Lab

La versión `0.2.0` puede crear y controlar localmente a Player Two sin un segundo
mando. Pulsa `F8` desde el título y después entra normalmente a una partida guardada.
Cuphead creará a ambos jugadores al cargar el mapa:

Player Two no equivale siempre a Mugman: si Player One usa Mugman, el segundo slot
será Cuphead, respetando el comportamiento cooperativo nativo del juego.

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

El laboratorio solo sustituye al jugador Rewired con ID `1`. Al desactivarlo, Cuphead
recupera inmediatamente sus entradas originales.

Las entradas pasan por un transporte loopback con tres frames de latencia simulada.
El teclado produce `InputFrame`; los parches consumen únicamente frames entregados por
el transporte. Así podremos cambiar loopback por LAN o Steam P2P sin reescribir la
integración con el juego.

## Alcance y límites

Este es un proyecto independiente y no está afiliado con Studio MDHR, Microsoft ni
Valve. No incluirá DLL, arte, audio ni otros archivos del juego. Cada jugador deberá
poseer su propia copia.
