# Prueba 0.12.10: loadout, controles, desconexión, mapa y revive

**Estado:** pendiente de prueba manual en dos equipos.

Usar en ambos extremos exactamente:

- `releases/Coophead-0.12.10-Windows-x64-PRUEBA-NORMAL.zip`
- `RunInBackground = false`
- `BlockLocalInputWhenUnfocused = false`

No mezclar esta versión con una anterior: el protocolo 13 rechazará la conexión para
evitar una sala que parezca unida pero no intercambie input.

## Prueba específica del caso 5/3

1. Antes de pulsar **UNIRSE**, equipar en Player One del invitado **Corazón doble**
   y dejar al Player Two del save del host sin ese amuleto.
2. Crear la sala. En el host no debe aparecer **EMPEZAR** hasta que el loadout del
   invitado haya sido aceptado; normalmente tarda menos de un segundo.
3. Entrar a Goopy. Player Two debe iniciar con cinco puntos de vida en ambas PCs,
   y Player One debe mostrar en el invitado la misma vida/amuleto que tiene el host.
4. Repetir con arma primaria, secundaria y súper distintos. Cambiar de arma y usar
   EX debe producir la misma arma/animación en ambas PCs.
5. Desconectar y abrir una partida local. Cada equipo debe recuperar su equipamiento
   anterior; el loadout prestado no debe quedar escrito en ningún save. Comprobar
   también que el contador de muertes, monedas y posición del invitado no cambiaron.
6. Guardar los logs y comprobar en el invitado una línea
   `[LoadoutSync] Vida máxima verificada en ambas PCs`. Si aparece
   `La vida máxima local no coincide`, adjuntar los dos logs.

Esta ronda usa el Player One del slot local activo del invitado como Player Two. El
selector explícito de save del invitado sigue pendiente. Reliquia maldita/divina,
Djimmi y el cambio de bomba en niveles de avión requieren pruebas separadas porque
también dependen de progreso o inventario, no sólo del ID equipado.

## Qué se debe probar

1. Abrir el host y el invitado en equipos distintos. Al cambiar de ventana, Cuphead
   debe comportarse normalmente: no continúa ejecutando input en segundo plano y no
   aparece ningún bloqueo o rearme del filtro experimental.
2. Usar en el invitado una configuración de teclado o mando distinta a la del host.
   Cada tecla debe ejecutar una sola acción; no debe aparecer caminar agachado por
   combinar Player One, Player Two o teclas fijas.
3. Acercar al invitado a una interacción del mapa. El globo debe incluir la tecla o
   botón configurado localmente, no quedar vacío.
4. Separar a ambos jugadores en el mapa y caminar al mismo tiempo. En el invitado no
   debe haber regresos a posiciones anteriores ni rutas transitables sólo por unos
   segundos. Ambos actores deben moverse de forma continua desde el estado del host.
5. Con Player Two, confirmar Goopy. Sólo el host debe abrir y resolver el menú de
   dificultad; el invitado espera la orden autoritativa y ambos cargan el mismo nivel.
   El menú ya no debe alternarse entre una ventana y la otra.
6. Perder la partida y seleccionar **RETRY**. El invitado debe cargar una generación
   nueva de la escena; el menú de derrota anterior no puede permanecer sobre el juego.
7. Durante una carga lenta, volver a reintentar si es posible. El loader anterior debe
   terminar antes de iniciar el nuevo y ninguna pantalla puede quedar retenida.
8. En el invitado, probar caminar, salto, disparo, dash, fijar dirección y EX usando
   sus bindings reales, no teclas predeterminadas del mod.
9. Hacer varios EX desde Player Two, incluyendo uno cerca de una transición o pausa.
   Deben verse tanto en el invitado como en el host y descontar una sola carta.
10. Hacer EX/Super desde Player One. En el invitado deben verse la animación, sonido y
   proyectil completos, no sólo el retroceso del personaje.
11. Observar las tres fases de Goopy. El movimiento remoto puede llevar el retraso del
   ping, pero no debe frenarse a intervalos regulares ni teletransportarse en distancias
   pequeñas. Las transiciones de actor/fase sí pueden hacer una corrección inmediata.
12. Matar a Player One y revivirlo desde Player Two. El fantasma debe reproducir una
    sola salida, desaparecer y reactivar al jugador una vez, sin caídas o reapariciones.
13. Hacer un dash de Player Two contra Goopy desde ambos lados. Después del golpe deben
    coincidir vida, posición y dirección del knockback, sin doble daño ni control trabado.
14. Quitar el foco hasta mostrar la espera. **Seguir esperando** debe iniciar seleccionado
    y se debe poder cambiar/aceptar con teclado o mando, sin mouse.
15. Desde el invitado seleccionar **Desconectar**. El host debe recibir el aviso de
    salida y continuar solo inmediatamente, sin esperar el timeout.
16. Volver a conectar y seleccionar **Remover Player Two**. Ambos juegos deben cerrar
    la sesión y volver al inicio; Player Two no debe reaparecer.
17. Probar un código inexistente. Debe decir que la sala no existe o expiró, sin
    `Generic/unknown HTTP error`.
18. Completar el combate, KO, resultados y regreso al mapa en ambos equipos.

## Resultado aceptable y diagnóstico

Con 90–100 ms de ping es aceptable una diferencia visual breve en el instante del
contacto. Se considera fallo si:

- el mapa o la selección de nivel divergen;
- queda un menú de derrota sobre una partida reiniciada;
- EX/Super no aparece, se ejecuta dos veces o descuenta dos cartas;
- Goopy avanza a cortes regulares o queda en una fase distinta;
- un jugador conserva una posición, golpe o bloqueo diferente;
- la sesión muestra conectada una versión incompatible.

Si ocurre un fallo, guardar `BepInEx/LogOutput.log` de ambos equipos antes de volver
a abrir Cuphead. Anotar la primera acción diferente, el ping mostrado y si ocurrió
durante mapa, carga, pausa, EX o cambio de fase.
