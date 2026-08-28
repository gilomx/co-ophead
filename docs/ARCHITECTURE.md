# Arquitectura inicial

## La decisión central

Cuphead no fue diseñado como una simulación de red determinista. Un lockstep puro
parece atractivo por su bajo ancho de banda, pero cualquier diferencia de RNG,
orden de actualización, física o tiempo produce una desincronización acumulativa.
Implementar rollback completo exigiría capturar y restaurar prácticamente todo el
estado del juego, algo demasiado grande para el primer hito.

Usaremos un modelo **host autoritativo híbrido**:

1. Cada cliente ejecuta Cuphead localmente.
2. El invitado envía un `InputFrame` compacto al host.
3. El host aplica esa entrada al Player Two vanilla y es autoridad sobre enemigos,
   jefe, RNG, cambios de escena, reintentos y progreso.
4. El invitado predice su propio movimiento para reducir la sensación de latencia.
5. El host envía snapshots y eventos; el invitado interpola o corrige diferencias.

Este modelo consume kilobytes por segundo, no los megabits de un stream de video.

## Capas

### 1. Integración con el juego

- arranque mediante BepInEx;
- parches Harmony pequeños y auditables;
- adaptación de Rewired/entrada para alimentar Player Two;
- observación de escenas, jugadores, daño, RNG y jefe.

No se modificará `Assembly-CSharp.dll` en disco.

### 2. Simulación de sesión

La lógica de juego no conocerá Steam directamente. Consumirá una interfaz de sesión
y mensajes versionados, para poder probarla primero con un loopback dentro del mismo
proceso y después con dos procesos.

Mensajes mínimos previstos:

- `Hello`: versión del mod, protocolo, juego y DLC;
- `InputFrame`: tick, ejes y bits de botones;
- `SceneCommand`: escena, dificultad y punto de entrada;
- `PlayerSnapshot`: tick, posición, velocidad, vida y estado;
- `WorldSnapshot`: RNG, jefe y entidades críticas;
- `GameplayEvent`: daño, parry, muerte, revive, retry y salida;
- `Ping/Pong`: latencia y reloj.

Los inputs pueden viajar sin garantía de entrega e incluir redundancia de varios
ticks. Las transiciones, eventos y negociación deben ser fiables y ordenadas.

### 3. Transporte

El transporte final será P2P sobre Steam para aprovechar identidad, invitaciones,
relay y NAT traversal. Durante el desarrollo, un transporte loopback permitirá
depurar sin dos cuentas de Steam.

## Fases verificables

### Fase 0 — Plugin mínimo

- cargar una DLL con BepInEx;
- escribir versión y escena actual en el log;
- confirmar que Harmony puede parchear un método estable.

### Fase 1 — Remote Input Lab

- crear Player Two usando el flujo cooperativo nativo;
- representar entradas con `InputFrame` independiente de Rewired;
- controlar Player Two mediante loopback;
- probar mapa, salto, dash, disparo, parry, lock y equipamiento.

Esta fase separa errores de integración de errores de red.

### Fase 2 — Dos procesos en LAN

- handshake estricto de versiones;
- UDP o transporte intercambiable de laboratorio;
- sincronizar entrada, carga de escena y reintento;
- retener el iris de los niveles hasta recibir la confirmación `LevelReady`;
- medir pérdida y RTT; añadir después jitter y métricas de desync.

### Fase 3 — Steam P2P

- lobby privado e invitación;
- relay/NAT traversal;
- desconexión y mensajes de error claros.

### Fase 4 — Sincronización de combate

- autoridad de jefe, enemigos y daño;
- snapshots periódicos y detector de desync;
- corrección suave del jugador y corrección fuerte solo cuando sea necesaria;
- matriz de pruebas jefe por jefe.

### Fase 5 — Producto utilizable

- menú, configuración e instalador;
- diagnóstico exportable;
- compatibilidad con DLC;
- empaquetado sin redistribuir archivos del juego.

## Reglas del protocolo

- Toda conexión declara una versión de protocolo independiente de la versión visual.
- Nunca se deserializan tipos arbitrarios; cada paquete tiene tamaño máximo y campos
  validados.
- Ningún paquete del invitado puede ordenar progreso, otorgar objetos o elegir la
  vida del jefe.
- La simulación de red nunca bloquea el hilo principal de Unity.
- Los logs no contienen Steam IDs completos ni secretos.

## Riesgos conocidos

- Cuphead está profundamente acoplado a `PlayerOne` y `PlayerTwo`.
- Los scripts específicos de cada jefe pueden requerir sincronización particular.
- Rewired puede reasignar dispositivos y romper el enrutamiento remoto.
- Los métodos parcheados pueden cambiar con una actualización del juego.
- Predicción y corrección deben evitar falsos golpes o revives inconsistentes.

Por eso el primer entregable es Remote Input Lab, no un menú multijugador completo.
