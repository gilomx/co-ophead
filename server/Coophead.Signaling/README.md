# Co-ophead Signaling

Servicio efímero de códigos y endpoints para establecer conexiones UDP directas. No
transporta frames de juego. Usa Cloudflare Workers + Durable Objects en el plan Free.

```powershell
npm install
npm run check
npm run dev
```

Rutas:

- `POST /rooms`: crea sala con `{ address, port, version }`.
- `POST /rooms/CODE`: une invitado y devuelve el endpoint del host.
- `GET /rooms/CODE?role=host`: el host consulta hasta recibir el endpoint invitado.
- `GET /health`: comprobación sin estado.
