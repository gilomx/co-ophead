# Prueba LAN de entradas

Esta fase comprueba transporte de entradas entre dos equipos. Todavía no sincroniza
escenas, guardado, enemigos ni progreso. El host ejecuta la partida; el cliente puede
permanecer en el título mientras envía el teclado numérico.

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

3. Abre Cuphead y pulsa `F8`. Puede quedarse en el título durante esta prueba.
4. Usa el teclado numérico. Los frames se envían al host por UDP.

## Resultado esperado

Player Two aparece en el host y responde al teclado numérico del cliente. El cliente
todavía no verá la partida del host; esa será la siguiente fase de sincronización.

## Límites actuales

- No hay cifrado, autenticación, lobby ni NAT traversal.
- Debe usarse solo en una LAN de confianza.
- Cada paquete contiene un frame; el estado mantenido del siguiente paquete repara
  liberaciones perdidas, pero aún no hay métricas de pérdida ni jitter buffer.
- El puerto no debe exponerse directamente a Internet.
- Hay ping periódico y timeout de cinco segundos; una desconexión vuelve al estado de
  espera/búsqueda sin reiniciar el juego.
- El sender de desarrollo resume el ping cada cinco segundos para mantener legible la
  secuencia de conexión y escenas.
- En modo LAN, Co-ophead habilita la actualización de Unity en segundo plano para que
  el handshake no expire al cambiar el foco entre Cuphead y herramientas de prueba.

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
