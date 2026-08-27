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
`Cuphead/BepInEx/plugins/Coophead/`, inicia el juego y busca `Co-ophead 0.1.0-dev
cargado` en `BepInEx/LogOutput.log`.

## Alcance y límites

Este es un proyecto independiente y no está afiliado con Studio MDHR, Microsoft ni
Valve. No incluirá DLL, arte, audio ni otros archivos del juego. Cada jugador deberá
poseer su propia copia.
