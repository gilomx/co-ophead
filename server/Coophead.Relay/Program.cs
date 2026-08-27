using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

const int defaultPort = 27183;
if (args.Contains("--self-test"))
{
    await RelaySelfTest.Run();
    return;
}

var port = ReadPort(args, defaultPort);
var server = new RelayServer(new IPEndPoint(IPAddress.Any, port));
Console.WriteLine($"Co-ophead Relay escuchando TCP :{port}");
await server.Run(CancellationToken.None);

static int ReadPort(string[] arguments, int fallback)
{
    var option = arguments.FirstOrDefault(x => x.StartsWith("--port="));
    return option != null && int.TryParse(option[7..], out var port) ? port : fallback;
}

internal static class RelayProtocol
{
    public const byte Create = 1, Created = 2, Join = 3, Joined = 4, Data = 5, Error = 6;
    public const int MaxPayload = 2048;

    public static async Task Write(NetworkStream stream, byte type, byte[] payload,
        CancellationToken cancellationToken)
    {
        var length = payload.Length + 1;
        var header = BitConverter.GetBytes(length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(new[] { type }, cancellationToken);
        if (payload.Length > 0) await stream.WriteAsync(payload, cancellationToken);
    }

    public static async Task<(byte Type, byte[] Payload)?> Read(NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await ReadExact(stream, header, cancellationToken)) return null;
        var length = BitConverter.ToInt32(header);
        if (length < 1 || length > MaxPayload + 1) throw new InvalidDataException("Frame inválido.");
        var body = new byte[length];
        if (!await ReadExact(stream, body, cancellationToken)) return null;
        return (body[0], body[1..]);
    }

    private static async Task<bool> ReadExact(NetworkStream stream, byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (count == 0) return false;
            offset += count;
        }
        return true;
    }
}

internal sealed class RelayServer
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly TcpListener listener;
    private readonly ConcurrentDictionary<string, Room> rooms = new();
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public RelayServer(IPEndPoint endpoint) { listener = new TcpListener(endpoint); }

    public async Task Run(CancellationToken cancellationToken)
    {
        listener.Start();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Handle(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task Handle(TcpClient client, CancellationToken serverToken)
    {
        string? roomCode = null;
        Room? room = null;
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var first = await RelayProtocol.Read(stream, serverToken);
                if (first == null) return;
                if (first.Value.Type == RelayProtocol.Create)
                {
                    roomCode = CreateCode();
                    room = new Room(client);
                    rooms[roomCode] = room;
                    await RelayProtocol.Write(stream, RelayProtocol.Created,
                        Encoding.ASCII.GetBytes(roomCode), serverToken);
                }
                else if (first.Value.Type == RelayProtocol.Join)
                {
                    roomCode = Encoding.ASCII.GetString(first.Value.Payload).ToUpperInvariant();
                    if (!rooms.TryGetValue(roomCode, out room) || !room.TryJoin(client))
                    {
                        await RelayProtocol.Write(stream, RelayProtocol.Error,
                            Encoding.UTF8.GetBytes("Sala inexistente o llena."), serverToken);
                        return;
                    }
                    await room.NotifyJoined(serverToken);
                }
                else return;

                while (true)
                {
                    var frame = await RelayProtocol.Read(stream, serverToken);
                    if (frame == null) break;
                    if (frame.Value.Type == RelayProtocol.Data)
                        await room.Forward(client, frame.Value.Payload, serverToken);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException) { }
        finally
        {
            if (room != null && roomCode != null && room.Remove(client)) rooms.TryRemove(roomCode, out _);
        }
    }

    private string CreateCode()
    {
        var random = new byte[6];
        while (true)
        {
            RandomNumberGenerator.Fill(random);
            var chars = random.Select(x => Alphabet[x % Alphabet.Length]).ToArray();
            var code = new string(chars);
            if (!rooms.ContainsKey(code)) return code;
        }
    }
}

internal sealed class Room
{
    private readonly TcpClient host;
    private TcpClient? guest;
    private readonly SemaphoreSlim hostWrite = new(1, 1), guestWrite = new(1, 1);
    public Room(TcpClient host) { this.host = host; }
    public bool TryJoin(TcpClient value) { lock (this) { if (guest != null) return false; guest = value; return true; } }

    public async Task NotifyJoined(CancellationToken token)
    {
        await Write(host, hostWrite, RelayProtocol.Joined, Array.Empty<byte>(), token);
        if (guest != null) await Write(guest, guestWrite, RelayProtocol.Joined, Array.Empty<byte>(), token);
    }

    public async Task Forward(TcpClient source, byte[] payload, CancellationToken token)
    {
        var target = ReferenceEquals(source, host) ? guest : host;
        if (target == null) return;
        await Write(target, ReferenceEquals(target, host) ? hostWrite : guestWrite,
            RelayProtocol.Data, payload, token);
    }

    public bool Remove(TcpClient value)
    {
        lock (this) { if (ReferenceEquals(value, guest)) guest = null; return ReferenceEquals(value, host); }
    }

    private static async Task Write(TcpClient client, SemaphoreSlim gate, byte type,
        byte[] payload, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { await RelayProtocol.Write(client.GetStream(), type, payload, token); }
        finally { gate.Release(); }
    }
}

internal static class RelaySelfTest
{
    public static async Task Run()
    {
        using var cancellation = new CancellationTokenSource();
        var server = new RelayServer(new IPEndPoint(IPAddress.Loopback, 0));
        var run = server.Run(cancellation.Token);
        await Task.Delay(50);
        using var host = new TcpClient(); using var guest = new TcpClient();
        await host.ConnectAsync(IPAddress.Loopback, server.Port);
        await RelayProtocol.Write(host.GetStream(), RelayProtocol.Create, Array.Empty<byte>(), default);
        var created = await RelayProtocol.Read(host.GetStream(), default);
        if (created?.Type != RelayProtocol.Created) throw new Exception("No se creó la sala.");
        await guest.ConnectAsync(IPAddress.Loopback, server.Port);
        await RelayProtocol.Write(guest.GetStream(), RelayProtocol.Join, created.Value.Payload, default);
        await RelayProtocol.Read(host.GetStream(), default); await RelayProtocol.Read(guest.GetStream(), default);
        var payload = Encoding.ASCII.GetBytes("snapshot");
        await RelayProtocol.Write(host.GetStream(), RelayProtocol.Data, payload, default);
        var relayed = await RelayProtocol.Read(guest.GetStream(), default);
        if (relayed?.Type != RelayProtocol.Data || !relayed.Value.Payload.SequenceEqual(payload))
            throw new Exception("El relay alteró los datos.");
        cancellation.Cancel();
        await run;
        Console.WriteLine("RelaySelfTest: OK");
    }
}
