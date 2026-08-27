using Coophead;
using Coophead.Transport;

var transport = new LoopbackInputTransport(3);
var sent = new InputFrame
{
    Tick = 10,
    Horizontal = 127,
    Vertical = -127,
    Held = InputButtons.Jump,
    Pressed = InputButtons.Jump,
    Released = InputButtons.Dash,
};

transport.Send(sent);

for (uint tick = 10; tick < 13; tick++)
    Assert(!transport.TryReceive(tick, out _), "El frame llegó antes de la latencia configurada.");

Assert(transport.TryReceive(13, out var received), "El frame no llegó en el tick esperado.");
Assert(received.Tick == 10, "El transporte alteró el tick de origen.");
Assert(received.Horizontal == 127, "El transporte alteró el eje horizontal.");
Assert(received.Vertical == -127, "El transporte alteró el eje vertical.");
Assert(received.HasHeld(InputButtons.Jump), "El transporte perdió botones mantenidos.");
Assert(received.HasPressed(InputButtons.Jump), "El transporte perdió el borde de pulsación.");
Assert(received.HasReleased(InputButtons.Dash), "El transporte perdió el borde de liberación.");
Assert(!transport.TryReceive(13, out _), "El transporte entregó el mismo frame dos veces.");

transport.Reset();
transport.Send(new InputFrame { Tick = 20 });
transport.Send(new InputFrame { Tick = 21 });
Assert(transport.TryReceive(24, out var first) && first.Tick == 20, "Se rompió el orden FIFO.");
Assert(transport.TryReceive(24, out var second) && second.Tick == 21, "No se entregó el segundo frame.");
transport.Dispose();

var encoded = InputFramePacketCodec.Encode(sent);
Assert(encoded.Length == InputFramePacketCodec.PacketSize, "El codec produjo un tamaño inesperado.");
Assert(InputFramePacketCodec.TryDecode(encoded, out var decoded), "El codec rechazó un paquete válido.");
Assert(decoded.Tick == sent.Tick && decoded.Horizontal == sent.Horizontal &&
    decoded.Vertical == sent.Vertical && decoded.Held == sent.Held,
    "El codec alteró el InputFrame.");
encoded[4]++;
Assert(!InputFramePacketCodec.TryDecode(encoded, out _), "El codec aceptó otro protocolo.");

const uint versionToken = 0x000400;
var port = 32000 + Environment.ProcessId % 10000;
using (var host = UdpInputTransport.CreateHost(port, versionToken, System.Net.IPAddress.Loopback))
using (var client = UdpInputTransport.CreateClient("127.0.0.1", port, versionToken))
{
    for (var attempt = 0; attempt < 400 && (!host.IsConnected || !client.IsConnected); attempt++)
    {
        client.Update();
        host.Update();
        client.Update();
        if (!host.IsConnected || !client.IsConnected)
            Thread.Sleep(5);
    }
    Assert(host.IsConnected && client.IsConnected, "El handshake UDP no se completó.");

    client.Send(new InputFrame { Tick = 100, Horizontal = 127, Held = InputButtons.Shoot });
    InputFrame networkFrame = default;
    var arrived = false;
    for (var attempt = 0; attempt < 100 && !arrived; attempt++)
    {
        host.Update();
        arrived = host.TryReceive(0, out networkFrame);
        if (!arrived)
            Thread.Sleep(5);
    }

    Assert(arrived, "El datagrama UDP local no llegó.");
    Assert(networkFrame.Tick == 100 && networkFrame.Horizontal == 127,
        "UDP alteró el frame recibido.");
    Assert(networkFrame.HasPressed(InputButtons.Shoot),
        "UDP no reconstruyó el borde de pulsación.");

    client.Send(new InputFrame { Tick = 101, Held = InputButtons.None });
    arrived = false;
    for (var attempt = 0; attempt < 100 && !arrived; attempt++)
    {
        host.Update();
        arrived = host.TryReceive(0, out networkFrame);
        if (!arrived)
            Thread.Sleep(5);
    }
    Assert(arrived && networkFrame.HasReleased(InputButtons.Shoot),
        "UDP no reconstruyó el borde de liberación.");
}

var rejectPort = port + 1;
using (var host = UdpInputTransport.CreateHost(rejectPort, versionToken, System.Net.IPAddress.Loopback))
using (var incompatibleClient = UdpInputTransport.CreateClient("127.0.0.1", rejectPort, 0x000399))
{
    for (var attempt = 0; attempt < 400 && !host.Status.Contains("incompatible"); attempt++)
    {
        incompatibleClient.Update();
        host.Update();
        incompatibleClient.Update();
        if (!host.Status.Contains("incompatible"))
            Thread.Sleep(5);
    }
    Assert(!host.IsConnected && !incompatibleClient.IsConnected,
        "Se conectaron versiones incompatibles.");
    Assert(host.Status.Contains("incompatible"), "El host no informó el rechazo de versión.");
}

Console.WriteLine("TransportChecks: OK");
return;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
