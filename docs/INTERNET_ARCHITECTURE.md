# Arquitectura de Internet sin configuración

El usuario instala únicamente Co-ophead. No abre puertos ni instala una VPN.

1. El host abre una conexión TCP saliente al relay y recibe un código de seis caracteres.
2. El invitado abre otra conexión saliente y presenta ese código.
3. El relay une ambas conexiones y reenvía frames binarios opacos.
4. Una versión posterior intentará UDP directo; TCP queda como respaldo universal.

El relay nunca debe aceptar paquetes de juego antes de unir una sala. Los códigos son
efímeros y las versiones públicas deberán añadir cifrado de extremo a extremo,
límites de tráfico, expiración de salas y autenticación del protocolo.

El servidor inicial está en `server/Coophead.Relay`. Se prueba con:

```powershell
dotnet run --project .\server\Coophead.Relay -- --self-test
```

## Integración actual del mod

`InputLab.Transport` acepta `InternetHost` e `InternetClient`. Ambos usan
`Internet.RelayAddress` y `Internet.RelayPort`; el invitado añade el código en
`Internet.RoomCode`. El host muestra el código generado en `BepInEx/LogOutput.log`.

Esta configuración sirve para desarrollo local. La experiencia final expondrá crear
y unirse dentro del juego y utilizará una dirección de relay incluida en el mod.
