import { DurableObject } from "cloudflare:workers";

interface Env { ROOMS: DurableObjectNamespace<Room>; }
interface Endpoint { address: string; port: number; version: number; }
interface RoomState { host?: Endpoint; guest?: Endpoint; }

const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
const roomLifetimeMs = 10 * 60 * 1000;

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/health")
      return json({ ok: true });

    if (request.method === "POST" && url.pathname === "/rooms") {
      const endpoint = await readEndpoint(request);
      if (!endpoint) return json({ error: "endpoint inválido" }, 400);
      const code = createCode();
      const room = env.ROOMS.getByName(code);
      const response = await room.fetch(new Request("https://room/create", {
        method: "POST", body: JSON.stringify(endpoint)
      }));
      if (!response.ok) return response;
      return json({ code, expiresInSeconds: roomLifetimeMs / 1000 }, 201);
    }

    const match = /^\/ROOMS\/([A-Z2-9]{6})$/.exec(url.pathname.toUpperCase());
    if (!match) return json({ error: "ruta inexistente" }, 404);
    const room = env.ROOMS.getByName(match[1]);

    if (request.method === "POST") {
      const endpoint = await readEndpoint(request);
      if (!endpoint) return json({ error: "endpoint inválido" }, 400);
      return room.fetch(new Request("https://room/join", {
        method: "POST", body: JSON.stringify(endpoint)
      }));
    }
    if (request.method === "GET") {
      const role = url.searchParams.get("role");
      if (role !== "host" && role !== "guest") return json({ error: "rol inválido" }, 400);
      return room.fetch("https://room/status?role=" + role);
    }
    return json({ error: "método inválido" }, 405);
  }
};

export class Room extends DurableObject<Env> {
  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const state = (await this.ctx.storage.get<RoomState>("state")) ?? {};
    if (request.method === "POST" && url.pathname === "/create") {
      if (state.host) return json({ error: "colisión de sala" }, 409);
      state.host = await request.json<Endpoint>();
      await this.save(state);
      return json({ waiting: true });
    }
    if (request.method === "POST" && url.pathname === "/join") {
      if (!state.host) return json({ error: "sala inexistente o expirada" }, 404);
      if (state.guest) return json({ error: "sala llena" }, 409);
      const guest = await request.json<Endpoint>();
      if (guest.version !== state.host.version) return json({ error: "versión incompatible" }, 409);
      state.guest = guest;
      await this.save(state);
      return json({ peer: state.host });
    }
    if (request.method === "GET" && url.pathname === "/status") {
      if (!state.host) return json({ error: "sala inexistente o expirada" }, 404);
      const role = url.searchParams.get("role");
      const peer = role === "host" ? state.guest : state.host;
      return json(peer ? { peer } : { waiting: true });
    }
    return json({ error: "operación inválida" }, 400);
  }

  async alarm(): Promise<void> { await this.ctx.storage.deleteAll(); }

  private async save(state: RoomState): Promise<void> {
    await this.ctx.storage.put("state", state);
    await this.ctx.storage.setAlarm(Date.now() + roomLifetimeMs);
  }
}

async function readEndpoint(request: Request): Promise<Endpoint | null> {
  try {
    const value = await request.json<Endpoint>();
    if (typeof value.address !== "string" || value.address.length < 3 || value.address.length > 64 ||
        !Number.isInteger(value.port) || value.port < 1 || value.port > 65535 ||
        !Number.isInteger(value.version) || value.version < 1) return null;
    return value;
  } catch { return null; }
}

function createCode(): string {
  const bytes = new Uint8Array(6);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, value => alphabet[value % alphabet.length]).join("");
}

function json(value: unknown, status = 200): Response {
  return Response.json(value, { status, headers: { "cache-control": "no-store" } });
}
