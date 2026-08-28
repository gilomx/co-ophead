# Superficie de integración de Cuphead

Inventario obtenido de la instalación local actualmente probada. Este documento
contiene únicamente nombres, firmas y constantes; no incluye código ni binarios del
juego.

## Versión observada

- Ejecutable Unity: `2017.4.9.7177439`
- BepInEx: `5.4.23.4`
- Harmony: `2.9.0.0`
- Ensamblado principal: `Assembly-CSharp 0.0.0.0`

## Ruta de entrada

`PlayerInput` es la frontera útil entre la lógica de Cuphead y Rewired:

- `Init(PlayerId)` obtiene `Rewired.Player` mediante
  `PlayerManager.GetPlayerInput(PlayerId)`.
- `GetAxis(PlayerInput.Axis)` delega a `Rewired.Player.GetAxis(int)`.
- `GetButton(CupheadButton)` delega a `Rewired.Player.GetButton(int)`.
- `PlayerManager` conserva diccionarios de entradas y controladores por jugador.

Esto permite sustituir lecturas de Player Two sin alterar los motores de movimiento.
Para cubrir transiciones de botones usadas directamente por el juego, el laboratorio
también deberá interceptar `Rewired.Player.GetButtonDown(int)` y `GetButtonUp(int)`.

## Identificadores confirmados

### Jugadores

- `PlayerOne = 0`
- `PlayerTwo = 1`

### Ejes

- `MoveHorizontal = 0`
- `MoveVertical = 1`

### Botones de juego

- `Jump = 2`
- `Shoot = 3`
- `Super = 4`
- `SwitchWeapon = 5`
- `Lock = 6`
- `Dash = 7`
- `Pause = 8`
- `Accept = 13`
- `Cancel = 14`
- `EquipMenu = 15`
- `Swap = 26`

## Motores observados

- `MapPlayerMotor.Update()` para el mapa.
- `LevelPlayerMotor.FixedUpdate()` para niveles terrestres.
- `PlanePlayerMotor.FixedUpdate()` para niveles de avión.
- `ArcadePlayerMotor.FixedUpdate()` para escenas especiales.

No los parchearemos en el primer prototipo. Si todos consumen la misma entrada de
Rewired, una sola capa remota podrá servir para los distintos tipos de nivel.

## Decisión para Remote Input Lab

El laboratorio mantendrá un `InputFrame` con:

- tick local;
- dos ejes cuantizados;
- máscara de botones mantenidos;
- máscaras de botones pulsados y liberados.

Mientras esté activado, solo reemplazará lecturas cuyo `Rewired.Player.id` sea el de
Player Two. Cuando esté desactivado, los parches devolverán el control inmediatamente
al juego sin modificar el resultado original.

El slot y el personaje son conceptos distintos. Player Two será Mugman cuando Player
One sea Cuphead, o Cuphead cuando Player One sea Mugman.

## Contexto de sesión confirmado

- `PlayerData.CurrentSaveFileIndex`: slot activo, de 0 a 2.
- `PlayerManager.player1IsMugman`: personaje principal.
- `Level.CurrentMode`: dificultad (`Easy=0`, `Normal=1`, `Hard=2`).
- `PlayerData.Data.CurrentMap`: mapa del guardado activo.
- `Level.Current.CurrentLevel`: nivel en ejecución cuando existe una instancia.

## Frontend confirmado

La primera lista de la captura vive en `scene_slot_select` y la controla
`SlotSelectScreen`:

- `mainMenuItems` y `_availableMainMenuItems` son arreglos paralelos;
- `UpdateMainMenu()` navega por la longitud de esos arreglos y no ejecuta nada para
  valores de enum desconocidos;
- `UITextAnimator.SetString()` conserva la animación tipográfica al cambiar etiquetas;
- `SceneLoader.icon` contiene el reloj de arena animado de las pantallas de carga;
- `GUIUtility.systemCopyBuffer` permite copiar el código de sala.

La integración `0.12.1` inserta un valor centinela después de **EMPEZAR**, intercepta
su confirmación con Harmony y clona el bloque tipográfico del menú antes de modificar
su layout. Para los estados de espera clona únicamente `SceneLoader.icon` y dispara
su animación `Hourglass`; no instancia otro `SceneLoader` ni extrae o redistribuye
recursos del juego.

Mientras una sesión online permanece activa en `scene_slot_select`, las lecturas de
`AnyPlayerInput` del frontend se limitan transitoriamente a Player One. Así el host
conserva el control exclusivo del menú, del save y del personaje; el arreglo original
de jugadores se restaura después de cada lectura y el gameplay no se modifica.

## Política provisional de sesión

Cuphead mantiene un solo `PlayerData.CurrentSaveFileIndex` para la partida y loadouts
separados por `PlayerId`. Por eso el host elige el save autoritativo y el cliente
bloquea `PlayerData.SaveCurrentFile()` durante la conexión. `player1IsMugman` basta
para garantizar personajes distintos: Player Two usa automáticamente el opuesto.
Por ahora el protocolo transmite el índice y contexto del save, no todos sus datos;
el invitado necesita un slot compatible y cualquier cambio local permanece sin
guardarse durante la sesión.

## Repetir la inspección

```powershell
dotnet run --project .\tools\AssemblyInspector\AssemblyInspector.csproj -- `
  'RUTA_A_CUPHEAD\Cuphead_Data\Managed\Assembly-CSharp.dll' `
  --full --il '=PlayerInput' '=PlayerId' '=CupheadButton'
```

La herramienta lee metadatos con Mono.Cecil incluido en BepInEx. No escribe sobre el
ensamblado inspeccionado.
