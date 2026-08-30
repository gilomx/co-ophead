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
4. Usa el teclado o mando configurado para Player One en ese Cuphead. Co-ophead lee
   sólo ese perfil y envía acciones semánticas al host; no mezcla el perfil de Player
   Two ni teclas fijas. En una VM, su ventana debe tener el foco y el controlador debe
   estar conectado o capturado por el sistema invitado.

## Resultado esperado

Player Two aparece en el host y responde a los controles de Player One configurados
en el cliente. El host no necesita compartir los mismos bindings: recibe `salto`,
`disparo`, ejes y demás acciones ya traducidas. Al cambiar de mapa o entrar a un
nivel, el cliente carga la misma escena localmente.

Co-ophead también envía un contexto fiable con el slot seleccionado, personaje
principal, dificultad, mapa, nivel y los loadouts aceptados de ambos jugadores. El
invitado anuncia primero su Player One local para usarlo como Player Two; el host no
habilita el inicio hasta recibir arma primaria/secundaria, súper y amuleto. El sender
lo imprime como `contexto #N`. Un cliente Cuphead aplica slot, personaje, dificultad
y overlays temporales de equipamiento. Usa el identificador de nivel para cargar
mediante `SceneLoader`, pero no guarda esos overlays ni escribe progreso local. Antes
de usar un slot conserva su JSON completo en memoria, bloquea las tres rutas nativas de
guardado y restaura ese JSON al cerrar la sesión.
El host refresca el contexto cada cinco segundos para que un cliente reconectado lo
reciba; el sender oculta refrescos idénticos y solo imprime cambios reales.
Además, el host envía snapshots de posición, vida actual/máxima, muerte y revive de ambos jugadores
en cada actualización de red. En el mapa, el invitado congela su física local y representa
a los dos jugadores desde un buffer corto de estado autoritativo del host. En combate,
Player One usa ese buffer para suavizar el movimiento remoto y Player Two conserva
predicción local mientras recibe controles. La revive remota reutiliza una sola vez la
transición nativa del fantasma.

Ambos lados muestran RTT y pérdida estimada en la esquina superior derecha. Si dejan
de llegar frames durante tres segundos, la partida queda pausada bajo un overlay. La
primera opción queda seleccionada y el menú acepta teclado o mando. El invitado avisa
al host mediante el siguiente frame disponible y ambos reanudan después de una cuenta
regresiva de tres segundos. Las entradas mantenidas durante la espera se neutralizan
para que no se ejecuten acciones atrasadas al volver. Una salida voluntaria usa una
despedida explícita y no espera este timeout ni intenta reconectar automáticamente.

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
- Hay ping periódico y timeout de quince segundos para tolerar cargas de escena. Un
  microcorte entra en espera; una desconexión voluntaria se distingue y se resuelve de
  inmediato.
- El sender de desarrollo resume el ping cada cinco segundos para mantener legible la
  secuencia de conexión y escenas.
- La prueba usa el comportamiento normal de Cuphead:
  `[Testing] RunInBackground = false`. Al perder el foco, el otro equipo puede entrar
  en la espera coordinada hasta que Cuphead vuelva a estar activo.

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
