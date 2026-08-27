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

const uint versionToken = 0x000600;
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

    host.SendScene(new SceneCommand { SceneName = "scene_map_world_1", LoadMode = 0 });
    SceneCommand receivedScene = default;
    var sceneArrived = false;
    for (var attempt = 0; attempt < 200 && !sceneArrived; attempt++)
    {
        host.Update();
        client.Update();
        host.Update();
        sceneArrived = client.TryReceiveScene(out receivedScene);
        if (!sceneArrived)
            Thread.Sleep(5);
    }
    Assert(sceneArrived && receivedScene.SceneName == "scene_map_world_1",
        "El comando fiable de escena no llegó.");
    Assert(receivedScene.Sequence != 0, "La escena llegó sin secuencia.");

    for (var attempt = 0; attempt < 80; attempt++)
    {
        host.Update();
        client.Update();
        host.Update();
        Thread.Sleep(5);
    }
    Assert(!client.TryReceiveScene(out _), "El cliente entregó una escena duplicada.");

    host.SendContext(new SessionContext
    {
        SaveSlot = 1,
        Flags = 1 | 2 | 4,
        Difficulty = 2,
        CurrentMap = 6,
        CurrentLevel = 42,
    });
    SessionContext receivedContext = default;
    var contextArrived = false;
    for (var attempt = 0; attempt < 200 && !contextArrived; attempt++)
    {
        host.Update();
        client.Update();
        host.Update();
        contextArrived = client.TryReceiveContext(out receivedContext);
        if (!contextArrived)
            Thread.Sleep(5);
    }
    Assert(contextArrived, "El contexto fiable de sesión no llegó.");
    Assert(receivedContext.SaveSlot == 1 && receivedContext.PlayerOneIsMugman &&
        receivedContext.IsInLevel && receivedContext.Difficulty == 2 &&
        receivedContext.CurrentMap == 6 && receivedContext.CurrentLevel == 42,
        "El transporte alteró el contexto de sesión.");
    Assert(receivedContext.Sequence != 0, "El contexto llegó sin secuencia.");

    for (var attempt = 0; attempt < 80; attempt++)
    {
        host.Update();
        client.Update();
        host.Update();
        Thread.Sleep(5);
    }
    Assert(!client.TryReceiveContext(out _), "El cliente entregó un contexto duplicado.");

    host.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 900,
        PresentMask = 3,
        DeadMask = 2,
        PlayerOneX = 12.5f,
        PlayerOneY = -8.25f,
        PlayerTwoX = 44.75f,
        PlayerTwoY = 16.5f,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 0,
    });
    PlayerStateSnapshot receivedState = default;
    var stateArrived = false;
    for (var attempt = 0; attempt < 100 && !stateArrived; attempt++)
    {
        host.Update(); client.Update();
        stateArrived = client.TryReceivePlayerState(out receivedState);
        if (!stateArrived) Thread.Sleep(5);
    }
    Assert(stateArrived && receivedState.Tick == 900 && receivedState.PresentMask == 3 &&
        receivedState.DeadMask == 2 && receivedState.PlayerOneX == 12.5f &&
        receivedState.PlayerTwoY == 16.5f && receivedState.PlayerOneHealth == 3,
        "El transporte alteró el snapshot de jugadores.");
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

var relayPortListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
relayPortListener.Start();
var relayPort = ((System.Net.IPEndPoint)relayPortListener.LocalEndpoint).Port;
relayPortListener.Stop();
var relayDll = Path.GetFullPath("server/Coophead.Relay/bin/Debug/net8.0/Coophead.Relay.dll");
using (var relayProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = $"\"{relayDll}\" --port={relayPort}",
    UseShellExecute = false,
    CreateNoWindow = true,
}))
{
    Thread.Sleep(150);
    using var internetHost = new RelayInputTransport("127.0.0.1", relayPort, true, "");
    for (var attempt = 0; attempt < 200 && internetHost.RoomCode.Length != 6; attempt++)
    { internetHost.Update(); Thread.Sleep(5); }
    Assert(internetHost.RoomCode.Length == 6, "El relay no entregó código de sala.");
    using var internetClient = new RelayInputTransport("127.0.0.1", relayPort, false, internetHost.RoomCode);
    for (var attempt = 0; attempt < 200 && (!internetHost.IsConnected || !internetClient.IsConnected); attempt++)
    { internetHost.Update(); internetClient.Update(); Thread.Sleep(5); }
    Assert(internetHost.IsConnected && internetClient.IsConnected, "Los clientes no se unieron a la sala.");
    internetClient.Send(new InputFrame { Tick = 77, Horizontal = 127, Held = InputButtons.Jump });
    InputFrame relayFrame = default;
    var relayFrameArrived = false;
    for (var attempt = 0; attempt < 200 && !relayFrameArrived; attempt++)
    { internetClient.Update(); internetHost.Update(); relayFrameArrived = internetHost.TryReceive(0, out relayFrame); Thread.Sleep(5); }
    Assert(relayFrameArrived && relayFrame.Tick == 77 && relayFrame.Horizontal == 127 &&
        relayFrame.HasHeld(InputButtons.Jump), "El relay alteró el frame de entrada.");
    relayProcess.Kill(true);
    relayProcess.WaitForExit();
}

Console.WriteLine("TransportChecks: OK");
return;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
