# Pruebas pendientes

## Goopy: EX/Super e impacto después de dash

**Estado:** pendiente de prueba manual en dos equipos.

Esta prueba valida las correcciones añadidas después de observar que el EX del
host no aparecía en el invitado y que un dash de Player Two contra Goopy podía
dejar posiciones distintas de forma permanente.

Paquetes que deben usarse en ambos extremos:

- `releases/Coophead-AirGPU-Windows-x64-PRUEBA-GOOPY-EX-IMPACTO.zip`
  en AirGPU, con `RunInBackground = false`.
- `releases/Coophead-Local-Windows-x64-PRUEBA-GOOPY-EX-IMPACTO.zip`
  en la PC local, con `RunInBackground = true` sólo durante el desarrollo.

No mezclar estos paquetes con compilaciones anteriores: ambos equipos deben usar
la misma compilación y entrar a Goopy Le Grande en dificultad Normal/Regular.

### Qué se debe probar

1. Entrar a Goopy desde el host y confirmar que la carga coordinada termina en
   ambos equipos y nadie puede moverse antes de `WALLOP`.
2. En el invitado, probar caminar, salto, disparo, dash, fijar dirección y EX.
   En teclado: `C` fija, `V` usa EX/Super y `Shift` hace dash. En mando Xbox:
   `RB` fija y `B` usa EX/Super.
3. Hacer uno o varios EX desde el host. En el invitado deben verse la animación,
   el sonido y el proyectil; no debe verse únicamente el pequeño retroceso del
   personaje. Repetir también con Super si hay medidor suficiente.
4. Hacer EX/Super desde el invitado y comprobar que la acción aparece tanto en
   su propia ventana como en la del host.
5. Hacer un dash de Player Two sin tocar a Goopy. Debe sentirse inmediato y no
   debe existir corrección, tirón ni rebobinado al terminar.
6. Hacer un dash de Player Two contra Goopy desde la izquierda y desde la derecha.
   El contacto puede verse en instantes ligeramente distintos por el ping, pero
   después del golpe deben coincidir la vida, la posición y la dirección del
   knockback en ambas pantallas.
7. Al terminar el golpe, Player Two debe recuperar el control inmediatamente y
   continuar caminando y disparando desde la misma zona en ambos equipos. No debe
   conservarse un desplazamiento permanente.
8. Repetir un golpe normal contra cada jugador y comprobar que no se descuenta
   vida dos veces. Si es posible, probar muerte y reanimación.
9. Completar las tres fases de Goopy y verificar transformación, lápida, KO,
   resultado y regreso al mapa en ambos equipos.

### Resultado aceptable y fallos

Con 90–100 ms de ping es aceptable una diferencia visual breve en el instante del
contacto. Se considera fallo cualquiera de estos casos:

- el EX/Super del host no aparece completo en el invitado;
- fijar o EX no responde en el invitado;
- un dash sin golpe se siente corregido o pesado;
- después de un golpe los jugadores quedan en posiciones distintas;
- se descuenta vida dos veces, queda bloqueado el estado de golpe o no vuelve el
  control;
- las fases, vida o KO de Goopy divergen.

Si ocurre un fallo, anotar cuál fue la primera acción diferente y guardar
`BepInEx/LogOutput.log` de ambos equipos antes de volver a abrir Cuphead. También
conviene registrar el ping mostrado y desde qué lado llegó el golpe.
