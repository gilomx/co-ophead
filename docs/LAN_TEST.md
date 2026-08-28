# Prueba LAN de entradas

Esta fase comprueba transporte de entradas y carga de escenas entre dos equipos.
Todavía no sincroniza enemigos, física ni progreso. El host ejecuta la partida y el
cliente sigue sus cambios de mapa y nivel mediante el cargador interno de Cuphead.

Ambos equipos necesitan la misma versión de Cuphead, BepInEx 5 y `Coophead.dll`.
Antes de aceptar entradas, intercambian versión de mod/protocolo. Una combinación
incompatible se rechaza y queda registrada claramente en ambos logs.

## Host

1. Ejecuta Cuphead una vez para generar `BepInEx/config/mx.gilomx.coophead.cfg`.
2. Cierra el juego.
3. Configura:

```ini
[InputLab]
Transport = LanHost
LanHostAddress = 127.0.0.1
LanPort = 27182
```

4. Permite UDP entrante al puerto `27182` en la red privada si Windows pregunta.
5. Abre Cuphead, pulsa `F8` en el título y entra a una partida.

## Cliente

1. Obtén la IPv4 privada del host, por ejemplo `192.168.1.50`.
2. Configura:

```ini
[InputLab]
Transport = LanClient
LanHostAddress = 192.168.1.50
LanPort = 27182
```

3. Abre Cuphead y pulsa `F8`. El host dirigirá sus cambios de escena.
4. Usa los controles que tengas configurados en Cuphead. Como respaldo, las flechas,
   `WASD` o `Numpad 4/6` y `Numpad 2/8` controlan el movimiento. Los frames se
   envían al host por UDP. En una VM, su ventana debe tener el foco y el controlador
   debe estar conectado o capturado por el sistema invitado.

## Resultado esperado

Player Two aparece en el host y responde a los controles configurados del cliente (o
al teclado numérico de respaldo). Al cambiar de mapa o entrar a un nivel, el cliente
carga la misma escena localmente.

Co-ophead también envía un contexto fiable con el slot seleccionado, personaje
principal, dificultad, mapa y nivel. El sender lo imprime como `contexto #N`. Un
cliente Cuphead aplica slot, personaje y dificultad. Usa el identificador de nivel
para cargar mediante `SceneLoader`, pero no escribe mapa, victorias ni progreso en el
guardado local.
El host refresca el contexto cada cinco segundos para que un cliente reconectado lo
reciba; el sender oculta refrescos idénticos y solo imprime cambios reales.
Además, el host envía snapshots de posición, vida y muerte de ambos jugadores veinte
veces por segundo. El invitado aplica Player One como estado autoritativo. Player Two
usa predicción local mientras recibe controles y converge al snapshot cuando queda
neutral; una diferencia grande todavía produce una corrección inmediata.

Ambos lados muestran RTT y pérdida estimada en la esquina superior derecha. Si dejan
de llegar frames durante 1.25 segundos, la partida queda pausada bajo un overlay. El
invitado avisa al host mediante el siguiente frame disponible y ambos reanudan después
de una cuenta regresiva de tres segundos. Las entradas mantenidas durante la espera se
neutralizan para que no se ejecuten acciones atrasadas al volver.

Los niveles usan además una compuerta de carga. La coroutine original de `SceneLoader`
se retiene después de `UnloadUnusedAssets` y antes de ocultar el reloj de arena. El
invitado anuncia `LevelReady` después de `Level.Start`; el host publica la liberación
en el contexto fiable y ambos permiten que continúe la apertura normal del iris. No
hay una cuenta regresiva visible.

## Límites actuales

- No hay cifrado, autenticación, lobby ni NAT traversal.
- Debe usarse solo en una LAN de confianza.
- Cada paquete contiene un frame; el estado mantenido del siguiente paquete repara
  liberaciones perdidas. Hay una estimación de pérdida basada en saltos de tick, pero
  todavía no existe un jitter buffer adaptativo.
- El puerto no debe exponerse directamente a Internet.
- Hay ping periódico y timeout de quince segundos para tolerar cargas de escena; una desconexión vuelve al estado de
  espera/búsqueda sin reiniciar el juego.
- El sender de desarrollo resume el ping cada cinco segundos para mantener legible la
  secuencia de conexión y escenas.
- En modo LAN, Co-ophead habilita la actualización de Unity en segundo plano para que
  el handshake no expire al cambiar el foco entre Cuphead y herramientas de prueba.
  Esto está marcado explícitamente como temporal mediante
  `[Testing] RunInBackground = true`. El comportamiento previsto para una versión
  final es `false`; con ese valor se prueba el overlay de espera al abandonar Cuphead.

## Prueba con una sola PC

`tools/CoopheadLanSender` simula el cliente sin abrir una segunda copia de Cuphead.
Configura Cuphead como `LanHost`, inicia el sender y usa el teclado numérico mientras
el juego conserva el foco:

```powershell
dotnet run --project .\tools\CoopheadLanSender\CoopheadLanSender.csproj
```

El sender se conecta a `127.0.0.1:27182` por defecto. `F7` lo cierra. También acepta
dirección y puerto como argumentos para futuras pruebas:

```powershell
dotnet run --project .\tools\CoopheadLanSender\CoopheadLanSender.csproj -- 192.168.1.84 27182
```
