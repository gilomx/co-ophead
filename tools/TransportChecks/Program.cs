using Coophead;
using Coophead.Transport;

var transport = new LoopbackInputTransport(3);
var sentGuestLoadout = new PlayerLoadoutSnapshot
{
    PrimaryWeapon = 1456773641,
    SecondaryWeapon = 1466518900,
    Super = 1456815409,
    Charm = 1500641115,
    Flags = PlayerLoadoutFlags.HasEquippedSecondaryRegularWeapon |
        PlayerLoadoutFlags.MustNotifySwitchRegularWeapon,
};
var sent = new InputFrame
{
    Tick = 10,
    Horizontal = 127,
    Vertical = -127,
    Held = InputButtons.Jump | InputButtons.MenuLeft | InputButtons.Lock,
    Pressed = InputButtons.Jump | InputButtons.Super,
    Released = InputButtons.Dash | InputButtons.Lock,
    Flags = InputFrameFlags.WaitingForHost | InputFrameFlags.LevelReady |
        InputFrameFlags.Loading,
    ReadyTransitionId = 77,
    PlayerTwoSuperRequestSequence = 19,
    InputSessionNonce = 0x12345678,
    GuestLoadoutRevision = 7,
    GuestLoadout = sentGuestLoadout,
};

transport.Send(sent);

for (uint tick = 10; tick < 13; tick++)
    Assert(!transport.TryReceive(tick, out _), "El frame llegó antes de la latencia configurada.");

Assert(transport.TryReceive(13, out var received), "El frame no llegó en el tick esperado.");
Assert(received.Tick == 10, "El transporte alteró el tick de origen.");
Assert(received.Horizontal == 127, "El transporte alteró el eje horizontal.");
Assert(received.Vertical == -127, "El transporte alteró el eje vertical.");
Assert(received.HasHeld(InputButtons.Jump), "El transporte perdió botones mantenidos.");
    Assert(received.HasHeld(InputButtons.MenuLeft),
        "El transporte perdió las direcciones del menú.");
    Assert(received.HasHeld(InputButtons.Lock),
        "El transporte perdió el botón mantenido de fijar.");
    Assert(received.HasPressed(InputButtons.Jump), "El transporte perdió el borde de pulsación.");
    Assert(received.HasPressed(InputButtons.Super),
        "El transporte perdió el borde de EX/Super.");
    Assert(received.HasReleased(InputButtons.Dash), "El transporte perdió el borde de liberación.");
    Assert(received.HasReleased(InputButtons.Lock),
        "El transporte perdió la liberación de fijar.");
Assert((received.Flags & InputFrameFlags.WaitingForHost) != 0,
    "El transporte perdió el estado de espera del invitado.");
Assert((received.Flags & InputFrameFlags.LevelReady) != 0,
    "El transporte perdió la confirmación de nivel listo.");
Assert((received.Flags & InputFrameFlags.Loading) != 0 &&
    received.ReadyTransitionId == 77 &&
    received.PlayerTwoSuperRequestSequence == 19 &&
    received.InputSessionNonce == 0x12345678 &&
    received.GuestLoadoutRevision == 7 &&
    received.GuestLoadout.SameAs(sentGuestLoadout),
    "El transporte perdió el estado o identificador de carga.");
Assert(!transport.TryReceive(13, out _), "El transporte entregó el mismo frame dos veces.");

transport.Reset();
transport.Send(new InputFrame { Tick = 20 });
transport.Send(new InputFrame { Tick = 21 });
Assert(transport.TryReceive(24, out var first) && first.Tick == 20, "Se rompió el orden FIFO.");
Assert(transport.TryReceive(24, out var second) && second.Tick == 21, "No se entregó el segundo frame.");
transport.Dispose();

var encoded = InputFramePacketCodec.Encode(sent);
Assert(InputFramePacketCodec.ProtocolVersion == 13,
    "El protocolo de compatibilidad no se actualizó a v13.");
Assert(encoded.Length == InputFramePacketCodec.PacketSize, "El codec produjo un tamaño inesperado.");
Assert(InputFramePacketCodec.TryDecode(encoded, out var decoded), "El codec rechazó un paquete válido.");
    Assert(decoded.Tick == sent.Tick && decoded.Horizontal == sent.Horizontal &&
        decoded.Vertical == sent.Vertical && decoded.Held == sent.Held &&
        decoded.Pressed == sent.Pressed && decoded.Released == sent.Released &&
        decoded.Flags == sent.Flags &&
        decoded.ReadyTransitionId == sent.ReadyTransitionId &&
        decoded.PlayerTwoSuperRequestSequence ==
            sent.PlayerTwoSuperRequestSequence &&
        decoded.InputSessionNonce == sent.InputSessionNonce &&
        decoded.GuestLoadoutRevision == sent.GuestLoadoutRevision &&
        decoded.GuestLoadout.SameAs(sent.GuestLoadout),
    "El codec alteró el InputFrame.");
var invalidInputLoadout = InputFramePacketCodec.Encode(sent);
invalidInputLoadout[57] = 0x80;
Assert(!InputFramePacketCodec.TryDecode(invalidInputLoadout, out _),
    "El codec aceptó flags desconocidos en el loadout invitado.");
invalidInputLoadout = InputFramePacketCodec.Encode(sent);
Array.Clear(invalidInputLoadout, 41, 4);
Assert(!InputFramePacketCodec.TryDecode(invalidInputLoadout, out _),
    "El codec aceptó un arma primaria nula en el loadout invitado.");
Assert(!InputFramePacketCodec.TryDecode(
    InputFramePacketCodec.Encode(sent)[..^1], out _),
    "El codec aceptó un InputFrame truncado.");
encoded[4]++;
Assert(!InputFramePacketCodec.TryDecode(encoded, out _), "El codec aceptó otro protocolo.");

var goodbyePacket = LanControlPacketCodec.Encode(
    LanControlPacketCodec.Goodbye,
    (uint)TransportDisconnectReason.RemovePlayer);
Assert(LanControlPacketCodec.TryDecode(goodbyePacket,
    out var goodbyeType, out var goodbyeReason) &&
    goodbyeType == LanControlPacketCodec.Goodbye &&
    goodbyeReason == (uint)TransportDisconnectReason.RemovePlayer,
    "El codec de control perdió la despedida o su motivo.");
var goodbyeAckPacket = LanControlPacketCodec.Encode(
    LanControlPacketCodec.GoodbyeAck,
    (uint)TransportDisconnectReason.Normal);
Assert(LanControlPacketCodec.TryDecode(goodbyeAckPacket,
    out var goodbyeAckType, out var goodbyeAckReason) &&
    goodbyeAckType == LanControlPacketCodec.GoodbyeAck &&
    goodbyeAckReason == (uint)TransportDisconnectReason.Normal,
    "El codec de control perdió el ACK de despedida.");
var nonControlPacket = LanControlPacketCodec.Encode(
    LanPlayerStatePacketCodec.PacketType, 0);
Assert(!LanControlPacketCodec.TryDecode(nonControlPacket, out _, out _),
    "El codec de control aceptó un tipo de datos como control.");

var friendlySignalingError = typeof(P2pInputTransport).GetMethod(
    "FriendlySignalingError",
    System.Reflection.BindingFlags.Static |
    System.Reflection.BindingFlags.NonPublic);
Assert(friendlySignalingError != null,
    "No se encontró el traductor de errores de señalización.");
string FriendlyError(string serverError, long responseCode = 409)
{
    return (string)friendlySignalingError!.Invoke(null, new object[]
    {
        "{\"error\":\"" + serverError + "\"}",
        responseCode,
        false,
        "Generic/unknown HTTP error",
    })!;
}
Assert(FriendlyError("sala inexistente o expirada", 404).Contains("no existe",
        StringComparison.OrdinalIgnoreCase),
    "No se tradujo el código de sala inexistente.");
Assert(FriendlyError("sala llena").Contains("dos jugadores",
        StringComparison.OrdinalIgnoreCase),
    "No se tradujo la sala llena.");
Assert(FriendlyError("versión incompatible").Contains("versiones distintas",
        StringComparison.OrdinalIgnoreCase),
    "No se tradujo la versión incompatible.");

var sentBossState = new BossStateSnapshot
{
    Tick = 901,
    TransitionId = 77,
    LevelId = 12,
    Flags = BossStateFlags.Active,
    Phase = 2,
    ActiveActor = 1 | 4,
    ActionState = 3,
    CurrentHealth = 42.5f,
    TotalHealth = 100f,
    X = -123.25f,
    Y = 456.75f,
    ScaleX = -1.25f,
    ScaleY = 1.5f,
    AnimatorStateHash = unchecked((int)0x8BADF00D),
    AnimatorNormalizedTime = 2.75f,
};
var encodedBossState = LanBossStatePacketCodec.Encode(sentBossState);
Assert(encodedBossState.Length == LanBossStatePacketCodec.PacketSize,
    "El codec de jefe produjo un tamaño inesperado.");
Assert(LanBossStatePacketCodec.TryDecode(encodedBossState, out var decodedBossState),
    "El codec de jefe rechazó un paquete válido.");
Assert(decodedBossState.Tick == sentBossState.Tick &&
    decodedBossState.TransitionId == sentBossState.TransitionId &&
    decodedBossState.LevelId == sentBossState.LevelId &&
    decodedBossState.Flags == sentBossState.Flags &&
    decodedBossState.Phase == sentBossState.Phase &&
    decodedBossState.ActiveActor == sentBossState.ActiveActor &&
    decodedBossState.ActionState == sentBossState.ActionState &&
    decodedBossState.CurrentHealth == sentBossState.CurrentHealth &&
    decodedBossState.TotalHealth == sentBossState.TotalHealth &&
    decodedBossState.X == sentBossState.X && decodedBossState.Y == sentBossState.Y &&
    decodedBossState.ScaleX == sentBossState.ScaleX &&
    decodedBossState.ScaleY == sentBossState.ScaleY &&
    decodedBossState.AnimatorStateHash == sentBossState.AnimatorStateHash &&
    decodedBossState.AnimatorNormalizedTime == sentBossState.AnimatorNormalizedTime,
    "El codec alteró el snapshot del jefe.");
Assert(!LanBossStatePacketCodec.TryDecode(encodedBossState[..^1], out _),
    "El codec de jefe aceptó un paquete truncado.");
var invalidBossState = LanBossStatePacketCodec.Encode(sentBossState);
invalidBossState[18] = 0x80;
Assert(!LanBossStatePacketCodec.TryDecode(invalidBossState, out _),
    "El codec de jefe aceptó flags desconocidos.");
invalidBossState = LanBossStatePacketCodec.Encode(sentBossState);
invalidBossState[20] = 8;
Assert(!LanBossStatePacketCodec.TryDecode(invalidBossState, out _),
    "El codec de jefe aceptó un actor fuera del bitmask permitido.");
invalidBossState = LanBossStatePacketCodec.Encode(sentBossState);
Buffer.BlockCopy(BitConverter.GetBytes(float.NaN), 0, invalidBossState, 30, 4);
Assert(!LanBossStatePacketCodec.TryDecode(invalidBossState, out _),
    "El codec de jefe aceptó una posición no finita.");

var stunTransaction = Enumerable.Range(1, 12).Select(x => (byte)x).ToArray();
var stunRequest = StunPacketCodec.CreateBindingRequest(stunTransaction);
Assert(stunRequest.Length == 20 && stunRequest[0] == 0 && stunRequest[1] == 1,
    "La solicitud STUN no tiene formato Binding Request.");
var stunResponse = new byte[32];
stunResponse[0] = 0x01; stunResponse[1] = 0x01; stunResponse[3] = 12;
stunResponse[4] = 0x21; stunResponse[5] = 0x12; stunResponse[6] = 0xA4; stunResponse[7] = 0x42;
Buffer.BlockCopy(stunTransaction, 0, stunResponse, 8, 12);
stunResponse[20] = 0; stunResponse[21] = 0x20; stunResponse[23] = 8; stunResponse[25] = 1;
var expectedPort = 45678; var xorPort = expectedPort ^ 0x2112;
stunResponse[26] = (byte)(xorPort >> 8); stunResponse[27] = (byte)xorPort;
var expectedAddress = new byte[] { 203, 0, 113, 42 };
var cookie = new byte[] { 0x21, 0x12, 0xA4, 0x42 };
for (var i = 0; i < 4; i++) stunResponse[28 + i] = (byte)(expectedAddress[i] ^ cookie[i]);
System.Net.IPEndPoint stunEndpoint;
Assert(StunPacketCodec.TryReadBindingResponse(stunResponse, stunTransaction, out stunEndpoint) &&
    stunEndpoint.Port == expectedPort && stunEndpoint.Address.Equals(new System.Net.IPAddress(expectedAddress)),
    "No se decodificó XOR-MAPPED-ADDRESS de STUN.");

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

    client.Send(new InputFrame
    {
        Tick = 100,
        Horizontal = 127,
        Held = InputButtons.Shoot,
        GuestLoadoutRevision = 7,
        GuestLoadout = sentGuestLoadout,
    });
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
    Assert(networkFrame.GuestLoadoutRevision == 7 &&
        networkFrame.GuestLoadout.SameAs(sentGuestLoadout),
        "UDP alteró el loadout anunciado por el invitado.");
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

    client.Send(new InputFrame
    {
        Tick = 102,
        Held = InputButtons.None,
        Pressed = InputButtons.Super | InputButtons.Lock,
        Released = InputButtons.Dash | InputButtons.Lock,
    });
    arrived = false;
    for (var attempt = 0; attempt < 100 && !arrived; attempt++)
    {
        host.Update();
        arrived = host.TryReceive(0, out networkFrame);
        if (!arrived)
            Thread.Sleep(5);
    }
    Assert(arrived && networkFrame.HasPressed(InputButtons.Super) &&
        networkFrame.HasPressed(InputButtons.Lock) &&
        networkFrame.HasReleased(InputButtons.Dash) &&
        networkFrame.HasReleased(InputButtons.Lock),
        "UDP sobrescribió los bordes explícitos del cliente.");

    const uint firstInputNonce = 0x11111111;
    const uint secondInputNonce = 0x22222222;
    client.Send(new InputFrame
    {
        Tick = 5,
        InputSessionNonce = firstInputNonce,
        Held = InputButtons.Jump,
    });
    arrived = false;
    for (var attempt = 0; attempt < 100 && !arrived; attempt++)
    {
        host.Update();
        arrived = host.TryReceive(0, out networkFrame);
        if (!arrived) Thread.Sleep(5);
    }
    Assert(arrived && networkFrame.Tick == 5 &&
        networkFrame.InputSessionNonce == firstInputNonce,
        "UDP no adoptó la primera época de input.");

    client.Send(new InputFrame
    {
        Tick = 1,
        InputSessionNonce = secondInputNonce,
        Held = InputButtons.Dash,
    });
    arrived = false;
    for (var attempt = 0; attempt < 100 && !arrived; attempt++)
    {
        host.Update();
        arrived = host.TryReceive(0, out networkFrame);
        if (!arrived) Thread.Sleep(5);
    }
    Assert(arrived && networkFrame.Tick == 1 &&
        networkFrame.InputSessionNonce == secondInputNonce,
        "UDP no reinició el filtro de tick al cambiar época de input.");

    client.Send(new InputFrame
    {
        Tick = 6,
        InputSessionNonce = firstInputNonce,
        Held = InputButtons.Super,
    });
    for (var attempt = 0; attempt < 20; attempt++)
    {
        host.Update();
        Thread.Sleep(2);
    }
    Assert(!host.TryReceive(0, out _),
        "UDP volvió a una época de input retirada por un datagrama tardío.");

    client.Send(new InputFrame
    {
        Tick = 2,
        InputSessionNonce = secondInputNonce,
        Held = InputButtons.Dash,
    });
    arrived = false;
    for (var attempt = 0; attempt < 100 && !arrived; attempt++)
    {
        host.Update();
        arrived = host.TryReceive(0, out networkFrame);
        if (!arrived) Thread.Sleep(5);
    }
    Assert(arrived && networkFrame.Tick == 2 &&
        networkFrame.InputSessionNonce == secondInputNonce,
        "UDP perdió la época vigente después de rechazar un nonce retirado.");

    var sentSceneSequence = host.SendScene(new SceneCommand
    {
        SceneName = "scene_map_world_1", LoadMode = 0, LevelId = -1,
        Difficulty = 2, Flags = SceneCommandFlags.CoordinatedTransition,
    });
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
    Assert(receivedScene.Sequence == sentSceneSequence && receivedScene.LevelId == -1 &&
        receivedScene.Difficulty == 2 && receivedScene.IsCoordinatedTransition,
        "La escena llegó sin su secuencia, dificultad o tipo de transición.");

    for (var attempt = 0; attempt < 80; attempt++)
    {
        host.Update();
        client.Update();
        host.Update();
        Thread.Sleep(5);
    }
    Assert(!client.TryReceiveScene(out _), "El cliente entregó una escena duplicada.");

    var cancelSequence = host.SendScene(new SceneCommand
    {
        SceneName = "scene_map_world_1", LoadMode = 0, LevelId = -1,
        Difficulty = 2, Flags = SceneCommandFlags.CancelCoordinatedTransition,
    });
    SceneCommand receivedCancellation = default;
    var cancellationArrived = false;
    for (var attempt = 0; attempt < 200 && !cancellationArrived; attempt++)
    {
        host.Update();
        client.Update();
        host.Update();
        cancellationArrived = client.TryReceiveScene(out receivedCancellation);
        if (!cancellationArrived)
            Thread.Sleep(5);
    }
    Assert(cancellationArrived && receivedCancellation.Sequence == cancelSequence &&
        receivedCancellation.CancelsCoordinatedTransition &&
        !receivedCancellation.IsCoordinatedTransition,
        "El aviso fiable de cancelación no llegó con sus flags.");

    host.SendContext(new SessionContext
    {
        SaveSlot = 1,
        Flags = 1 | 2 | 4 | 8 | 16 | 32,
        Difficulty = 2,
        ResumeSeconds = 3,
        CurrentMap = 6,
        CurrentLevel = 42,
        LoadTransitionId = 77,
        GuestLoadoutRevision = 7,
        PlayerOneLoadout = new PlayerLoadoutSnapshot
        {
            PrimaryWeapon = 1456773649,
            SecondaryWeapon = int.MaxValue,
            Super = 1495012282,
            Charm = 1461001046,
        },
        PlayerTwoLoadout = sentGuestLoadout,
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
        receivedContext.SessionSuspended && receivedContext.SessionResuming &&
        receivedContext.LevelGateReleased &&
        receivedContext.ResumeSeconds == 3 &&
        receivedContext.CurrentMap == 6 && receivedContext.CurrentLevel == 42 &&
        receivedContext.LoadTransitionId == 77 &&
        receivedContext.GuestLoadoutRevision == 7 &&
        receivedContext.PlayerOneLoadout.PrimaryWeapon == 1456773649 &&
        receivedContext.PlayerOneLoadout.Charm == 1461001046 &&
        receivedContext.PlayerTwoLoadout.SameAs(sentGuestLoadout),
        "El transporte alteró el contexto de sesión.");
    Assert(receivedContext.Sequence != 0, "El contexto llegó sin secuencia.");

    var encodedContext = LanSessionContextPacketCodec.Encode(receivedContext);
    Assert(encodedContext.Length == LanSessionContextPacketCodec.PacketSize &&
        LanSessionContextPacketCodec.TryDecode(encodedContext,
            out var decodedContext) &&
        decodedContext.PlayerOneLoadout.SameAs(
            receivedContext.PlayerOneLoadout) &&
        decodedContext.PlayerTwoLoadout.SameAs(
            receivedContext.PlayerTwoLoadout),
        "El codec alteró los loadouts del contexto.");
    Assert(!LanSessionContextPacketCodec.TryDecode(
        encodedContext[..^1], out _),
        "El codec aceptó un contexto truncado.");
    var invalidContextLoadout =
        LanSessionContextPacketCodec.Encode(receivedContext);
    invalidContextLoadout[63] = 0x80;
    Assert(!LanSessionContextPacketCodec.TryDecode(
        invalidContextLoadout, out _),
        "El codec aceptó flags desconocidos en el loadout P2.");
    invalidContextLoadout =
        LanSessionContextPacketCodec.Encode(receivedContext);
    Array.Clear(invalidContextLoadout, 30, 4);
    Assert(!LanSessionContextPacketCodec.TryDecode(
        invalidContextLoadout, out _),
        "El codec aceptó un loadout P1 incompleto.");
    var lobbyContext = new SessionContext
    {
        Sequence = 1,
        GuestLoadoutRevision = 7,
    };
    Assert(LanSessionContextPacketCodec.TryDecode(
        LanSessionContextPacketCodec.Encode(lobbyContext), out _),
        "El codec exigió loadouts antes de que el host eligiera un save.");

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
        TransitionId = 77,
        PresentMask = 3,
        DeadMask = 2,
        PlayerOneX = 12.5f,
        PlayerOneY = -8.25f,
        PlayerTwoX = 44.75f,
        PlayerTwoY = 16.5f,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 0,
        PlayerOneHealthMax = 3,
        PlayerTwoHealthMax = 5,
        PlayerOneSuperMeter = 37.5f,
        PlayerTwoSuperMeter = 12.25f,
        PlayerOneMapHorizontal = -127,
        PlayerOneMapVertical = 64,
        Flags = PlayerStateFlags.GameplayStarted,
        PlayerOneHeld = InputButtons.Shoot | InputButtons.Lock,
        PlayerOnePressed = InputButtons.Super,
        PlayerOneReleased = InputButtons.Dash,
        PlayerOneMotionFlags = PlayerMotionFlags.UsingSuperOrEx,
        PlayerTwoMotionFlags = PlayerMotionFlags.Dashing |
            PlayerMotionFlags.Hit,
        PlayerTwoHitDirection = -1,
        PlayerOneSuperActionSequence = 42,
        PlayerTwoSuperActionSequence = 43,
        StateSessionNonce = 0x33333333,
    });
    PlayerStateSnapshot receivedState = default;
    var stateArrived = false;
    for (var attempt = 0; attempt < 100 && !stateArrived; attempt++)
    {
        host.Update(); client.Update();
        stateArrived = client.TryReceivePlayerState(out receivedState);
        if (!stateArrived) Thread.Sleep(5);
    }
    Assert(stateArrived && receivedState.Tick == 900 &&
        receivedState.TransitionId == 77 && receivedState.PresentMask == 3 &&
        receivedState.DeadMask == 2 && receivedState.PlayerOneX == 12.5f &&
        receivedState.PlayerTwoY == 16.5f && receivedState.PlayerOneHealth == 3 &&
        receivedState.PlayerOneHealthMax == 3 &&
        receivedState.PlayerTwoHealthMax == 5 &&
        receivedState.PlayerOneSuperMeter == 37.5f &&
        receivedState.PlayerTwoSuperMeter == 12.25f &&
        receivedState.PlayerOneMapHorizontal == -127 &&
        receivedState.PlayerOneMapVertical == 64 &&
        receivedState.Flags == PlayerStateFlags.GameplayStarted &&
        receivedState.PlayerOneHeld == (InputButtons.Shoot | InputButtons.Lock) &&
        receivedState.PlayerOnePressed == InputButtons.Super &&
        receivedState.PlayerOneReleased == InputButtons.Dash &&
        receivedState.PlayerOneMotionFlags ==
            PlayerMotionFlags.UsingSuperOrEx &&
        receivedState.PlayerTwoMotionFlags ==
            (PlayerMotionFlags.Dashing | PlayerMotionFlags.Hit) &&
        receivedState.PlayerTwoHitDirection == -1 &&
        receivedState.PlayerOneSuperActionSequence == 42 &&
        receivedState.PlayerTwoSuperActionSequence == 43 &&
        receivedState.StateSessionNonce == 0x33333333,
        "El transporte alteró el snapshot de jugadores.");

    var encodedPlayerState = LanPlayerStatePacketCodec.Encode(receivedState);
    Assert(encodedPlayerState.Length == LanPlayerStatePacketCodec.PacketSize &&
        LanPlayerStatePacketCodec.TryDecode(encodedPlayerState,
            out var decodedPlayerState) &&
        decodedPlayerState.PlayerTwoMotionFlags ==
            receivedState.PlayerTwoMotionFlags &&
        decodedPlayerState.PlayerOneHealthMax == 3 &&
        decodedPlayerState.PlayerTwoHealthMax == 5 &&
        decodedPlayerState.PlayerTwoHitDirection == -1 &&
        decodedPlayerState.PlayerOneSuperActionSequence == 42 &&
        decodedPlayerState.PlayerTwoSuperActionSequence == 43 &&
        decodedPlayerState.StateSessionNonce == 0x33333333,
        "El codec de jugadores alteró un snapshot válido.");
    Assert(!LanPlayerStatePacketCodec.TryDecode(
        encodedPlayerState[..^1], out _),
        "El codec de jugadores aceptó un paquete truncado.");

    var invalidPlayerState = LanPlayerStatePacketCodec.Encode(receivedState);
    invalidPlayerState[58] = 0x80;
    Assert(!LanPlayerStatePacketCodec.TryDecode(invalidPlayerState, out _),
        "El codec de jugadores aceptó flags de movimiento desconocidos.");
    invalidPlayerState = LanPlayerStatePacketCodec.Encode(receivedState);
    invalidPlayerState[59] = 2;
    Assert(!LanPlayerStatePacketCodec.TryDecode(invalidPlayerState, out _),
        "El codec de jugadores aceptó una dirección de golpe inválida.");
    Buffer.BlockCopy(BitConverter.GetBytes(float.NaN), 0,
        invalidPlayerState, 12, 4);
    Assert(!LanPlayerStatePacketCodec.TryDecode(invalidPlayerState, out _),
        "El codec de jugadores aceptó una posición no finita.");
    invalidPlayerState = LanPlayerStatePacketCodec.Encode(receivedState);
    invalidPlayerState[40] = 0x80;
    Assert(!LanPlayerStatePacketCodec.TryDecode(invalidPlayerState, out _),
        "El codec de jugadores aceptó botones desconocidos.");

    var crossEpochState = new PlayerStateSnapshot
    {
        Tick = 1,
        StateSessionNonce = 0x44444444,
    };
    LanPlayerStatePacketCodec.MergeTransientEvents(ref crossEpochState,
        new PlayerStateSnapshot
        {
            Tick = 901,
            StateSessionNonce = 0x33333333,
            PlayerOnePressed = InputButtons.Super,
            PlayerTwoMotionFlags = PlayerMotionFlags.Hit,
        });
    Assert(crossEpochState.PlayerOnePressed == InputButtons.None &&
        crossEpochState.PlayerTwoMotionFlags == PlayerMotionFlags.None,
        "El codec mezcló eventos transitorios entre épocas de estado.");

    const uint secondStateNonce = 0x44444444;
    host.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 1,
        StateSessionNonce = secondStateNonce,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 2,
    });
    stateArrived = false;
    for (var attempt = 0; attempt < 100 && !stateArrived; attempt++)
    {
        client.Update();
        stateArrived = client.TryReceivePlayerState(out receivedState);
        if (!stateArrived) Thread.Sleep(5);
    }
    Assert(stateArrived && receivedState.Tick == 1 &&
        receivedState.StateSessionNonce == secondStateNonce,
        "UDP no reinició el filtro de tick al cambiar época de estado.");

    host.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 901,
        StateSessionNonce = 0x33333333,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 2,
        PlayerOnePressed = InputButtons.Super,
    });
    for (var attempt = 0; attempt < 20; attempt++)
    {
        client.Update();
        Thread.Sleep(2);
    }
    Assert(!client.TryReceivePlayerState(out _),
        "UDP volvió a una época de estado retirada por un datagrama tardío.");

    host.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 2,
        StateSessionNonce = secondStateNonce,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 2,
    });
    stateArrived = false;
    for (var attempt = 0; attempt < 100 && !stateArrived; attempt++)
    {
        client.Update();
        stateArrived = client.TryReceivePlayerState(out receivedState);
        if (!stateArrived) Thread.Sleep(5);
    }
    Assert(stateArrived && receivedState.Tick == 2 &&
        receivedState.StateSessionNonce == secondStateNonce,
        "UDP perdió la época de estado vigente tras rechazar una retirada.");

    host.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 901,
        StateSessionNonce = secondStateNonce,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 2,
        PlayerTwoMotionFlags = PlayerMotionFlags.Hit,
        PlayerTwoHitDirection = 1,
    });
    host.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 902,
        StateSessionNonce = secondStateNonce,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 2,
    });
    var burstPlayerStates = new List<PlayerStateSnapshot>();
    for (var attempt = 0; attempt < 100 && burstPlayerStates.Count < 2; attempt++)
    {
        client.Update();
        while (client.TryReceivePlayerState(out receivedState))
            burstPlayerStates.Add(receivedState);
        if (burstPlayerStates.Count < 2) Thread.Sleep(5);
    }
    Assert(burstPlayerStates.Count == 2 &&
        burstPlayerStates[0].Tick == 901 &&
        (burstPlayerStates[0].PlayerTwoMotionFlags &
            PlayerMotionFlags.Hit) != 0 &&
        burstPlayerStates[0].PlayerTwoHitDirection == 1 &&
        burstPlayerStates[1].Tick == 902,
        "UDP no conservó en orden una ráfaga de PlayerState.");

    host.SendBossState(sentBossState);
    BossStateSnapshot receivedBossState = default;
    var bossStateArrived = false;
    for (var attempt = 0; attempt < 100 && !bossStateArrived; attempt++)
    {
        host.Update(); client.Update();
        bossStateArrived = client.TryReceiveBossState(out receivedBossState);
        if (!bossStateArrived) Thread.Sleep(5);
    }
    Assert(bossStateArrived && receivedBossState.Tick == sentBossState.Tick &&
        receivedBossState.TransitionId == sentBossState.TransitionId &&
        receivedBossState.LevelId == sentBossState.LevelId &&
        receivedBossState.Flags == sentBossState.Flags &&
        receivedBossState.Phase == sentBossState.Phase &&
        receivedBossState.ActiveActor == sentBossState.ActiveActor &&
        receivedBossState.ActionState == sentBossState.ActionState &&
        receivedBossState.CurrentHealth == sentBossState.CurrentHealth &&
        receivedBossState.TotalHealth == sentBossState.TotalHealth &&
        receivedBossState.X == sentBossState.X && receivedBossState.Y == sentBossState.Y &&
        receivedBossState.ScaleX == sentBossState.ScaleX &&
        receivedBossState.ScaleY == sentBossState.ScaleY &&
        receivedBossState.AnimatorStateHash == sentBossState.AnimatorStateHash &&
        receivedBossState.AnimatorNormalizedTime == sentBossState.AnimatorNormalizedTime,
        "UDP alteró el snapshot del jefe.");

    for (var attempt = 0; attempt < 100 &&
        (host.PingMilliseconds < 0 || client.PingMilliseconds < 0); attempt++)
    {
        host.Update(); client.Update(); host.Update();
        Thread.Sleep(5);
    }
    Assert(host.PingMilliseconds >= 0 && client.PingMilliseconds >= 0,
        "El transporte no midió el ping en ambos sentidos.");
    Assert(host.EstimatedPacketLossPercent == 0 &&
        client.EstimatedPacketLossPercent == 0,
        "El transporte reportó pérdidas en la prueba local sin pérdida.");
}

var goodbyePort = port + 2;
using (var host = UdpInputTransport.CreateHost(goodbyePort, versionToken,
    System.Net.IPAddress.Loopback))
using (var client = UdpInputTransport.CreateClient("127.0.0.1", goodbyePort,
    versionToken))
{
    for (var attempt = 0; attempt < 400 &&
        (!host.IsConnected || !client.IsConnected); attempt++)
    {
        client.Update(); host.Update(); client.Update();
        if (!host.IsConnected || !client.IsConnected)
            Thread.Sleep(5);
    }
    Assert(host.IsConnected && client.IsConnected,
        "No se conectó la pareja UDP para probar la despedida.");

    client.RequestDisconnect(TransportDisconnectReason.RemovePlayer);
    for (var attempt = 0; attempt < 200 &&
        (!host.PeerDisconnected || client.IsConnected); attempt++)
    {
        host.Update(); client.Update(); host.Update();
        if (!host.PeerDisconnected || client.IsConnected)
            Thread.Sleep(5);
    }
    Assert(host.PeerDisconnected && !host.IsConnected &&
        host.PeerDisconnectReason == TransportDisconnectReason.RemovePlayer,
        "El host no recibió la despedida terminal RemovePlayer.");
    Assert(!client.IsConnected && !client.PeerDisconnected,
        "El emisor no cerró mediante el ACK o confundió su salida con la del peer.");

    // Supera el intervalo normal de Hello (1 s): una despedida terminal no
    // debe convertirse en una reconexión automática tardía.
    for (var attempt = 0; attempt < 240; attempt++)
    {
        client.Update(); host.Update(); Thread.Sleep(5);
    }
    Assert(!host.IsConnected && !client.IsConnected,
        "UDP volvió a ejecutar el handshake después de una despedida terminal.");
}

var normalGoodbyePort = port + 3;
using (var host = UdpInputTransport.CreateHost(normalGoodbyePort, versionToken,
    System.Net.IPAddress.Loopback))
using (var client = UdpInputTransport.CreateClient("127.0.0.1",
    normalGoodbyePort, versionToken))
{
    for (var attempt = 0; attempt < 400 &&
        (!host.IsConnected || !client.IsConnected); attempt++)
    {
        client.Update(); host.Update(); client.Update();
        if (!host.IsConnected || !client.IsConnected)
            Thread.Sleep(5);
    }
    Assert(host.IsConnected && client.IsConnected,
        "No se conectó la pareja UDP para probar la salida normal.");

    host.RequestDisconnect(TransportDisconnectReason.Normal);
    for (var attempt = 0; attempt < 200 &&
        (!client.PeerDisconnected || host.IsConnected); attempt++)
    {
        client.Update(); host.Update(); client.Update();
        if (!client.PeerDisconnected || host.IsConnected)
            Thread.Sleep(5);
    }
    Assert(client.PeerDisconnected && !client.IsConnected &&
        client.PeerDisconnectReason == TransportDisconnectReason.Normal,
        "El invitado no recibió la despedida terminal normal.");
    Assert(!host.IsConnected && !host.PeerDisconnected,
        "El host emisor confundió su propia salida con la del peer.");
}

var retryGoodbyePort = port + 4;
using (var host = UdpInputTransport.CreateHost(retryGoodbyePort, versionToken,
    System.Net.IPAddress.Loopback))
using (var client = UdpInputTransport.CreateClient("127.0.0.1",
    retryGoodbyePort, versionToken))
{
    for (var attempt = 0; attempt < 400 &&
        (!host.IsConnected || !client.IsConnected); attempt++)
    {
        client.Update(); host.Update(); client.Update();
        if (!host.IsConnected || !client.IsConnected)
            Thread.Sleep(5);
    }
    Assert(host.IsConnected && client.IsConnected,
        "No se conectó la pareja UDP para probar el reintento de despedida.");

    client.RequestDisconnect(TransportDisconnectReason.Normal);
    var drainSocket = typeof(UdpInputTransport).GetMethod("DrainSocket",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    Assert(drainSocket != null,
        "No se encontró el drenado de socket para simular pérdida UDP.");
    drainSocket!.Invoke(host, null);
    Assert(client.IsConnected,
        "Solicitar el cierre liberó el transporte antes de poder reintentar.");

    // Las tres copias iniciales se descartaron. Sólo puede completar este cierre
    // si el transporte solicitante permanece vivo hasta enviar un reintento y
    // procesar el ACK correspondiente.
    for (var attempt = 0; attempt < 240 &&
        (!host.PeerDisconnected || client.IsConnected); attempt++)
    {
        client.Update(); host.Update(); client.Update();
        if (!host.PeerDisconnected || client.IsConnected)
            Thread.Sleep(5);
    }
    Assert(host.PeerDisconnected && !host.IsConnected &&
        host.PeerDisconnectReason == TransportDisconnectReason.Normal,
        "El retry no recuperó una despedida cuyo burst inicial se perdió.");
    Assert(!client.IsConnected && !client.PeerDisconnected,
        "El solicitante no procesó el ACK después de recuperar la despedida.");
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
    const uint relayInputNonceA = 0x55555555;
    const uint relayInputNonceB = 0x66666666;
    internetClient.Send(new InputFrame
    {
        Tick = 77,
        Horizontal = 127,
        Held = InputButtons.Jump,
        InputSessionNonce = relayInputNonceA,
        GuestLoadoutRevision = 7,
        GuestLoadout = sentGuestLoadout,
    });
    InputFrame relayFrame = default;
    var relayFrameArrived = false;
    for (var attempt = 0; attempt < 200 && !relayFrameArrived; attempt++)
    { internetClient.Update(); internetHost.Update(); relayFrameArrived = internetHost.TryReceive(0, out relayFrame); Thread.Sleep(5); }
    Assert(relayFrameArrived && relayFrame.Tick == 77 && relayFrame.Horizontal == 127 &&
        relayFrame.HasHeld(InputButtons.Jump) &&
        relayFrame.GuestLoadoutRevision == 7 &&
        relayFrame.GuestLoadout.SameAs(sentGuestLoadout),
        "El relay alteró el frame de entrada o el loadout.");

    internetClient.Send(new InputFrame
    {
        Tick = 1,
        Held = InputButtons.Dash,
        InputSessionNonce = relayInputNonceB,
    });
    relayFrameArrived = false;
    for (var attempt = 0; attempt < 200 && !relayFrameArrived; attempt++)
    {
        internetClient.Update(); internetHost.Update();
        relayFrameArrived = internetHost.TryReceive(0, out relayFrame);
        Thread.Sleep(5);
    }
    Assert(relayFrameArrived && relayFrame.Tick == 1 &&
        relayFrame.InputSessionNonce == relayInputNonceB,
        "El relay no reinició el tick al cambiar época de input.");

    internetClient.Send(new InputFrame
    {
        Tick = 78,
        Held = InputButtons.Super,
        InputSessionNonce = relayInputNonceA,
    });
    for (var attempt = 0; attempt < 30; attempt++)
    {
        internetClient.Update(); internetHost.Update(); Thread.Sleep(2);
    }
    Assert(!internetHost.TryReceive(0, out _),
        "El relay volvió a una época de input retirada.");

    internetClient.Send(new InputFrame
    {
        Tick = 2,
        Held = InputButtons.Dash,
        InputSessionNonce = relayInputNonceB,
    });
    relayFrameArrived = false;
    for (var attempt = 0; attempt < 200 && !relayFrameArrived; attempt++)
    {
        internetClient.Update(); internetHost.Update();
        relayFrameArrived = internetHost.TryReceive(0, out relayFrame);
        Thread.Sleep(5);
    }
    Assert(relayFrameArrived && relayFrame.Tick == 2 &&
        relayFrame.InputSessionNonce == relayInputNonceB,
        "El relay perdió la época vigente después de un nonce retirado.");

    const uint relayStateNonceA = 0x77777777;
    const uint relayStateNonceB = 0x88888888;
    for (uint stateTick = 1000; stateTick < 1250; stateTick++)
    {
        internetHost.SendPlayerState(new PlayerStateSnapshot
        {
            Tick = stateTick,
            StateSessionNonce = relayStateNonceA,
            PresentMask = 3,
            PlayerOneHealth = 3,
            PlayerTwoHealth = 3,
            PlayerOnePressed = stateTick == 1000 ?
                InputButtons.Super : InputButtons.None,
            PlayerOneReleased = stateTick == 1001 ?
                InputButtons.Super : InputButtons.None,
            PlayerTwoMotionFlags = stateTick == 1002 ?
                PlayerMotionFlags.Hit : PlayerMotionFlags.None,
            PlayerTwoHitDirection = stateTick == 1002 ? (sbyte)-1 : (sbyte)0,
        });
        var relayBossState = sentBossState;
        relayBossState.Tick = stateTick;
        internetHost.SendBossState(relayBossState);
    }
    PlayerStateSnapshot relayPlayerState = default;
    BossStateSnapshot relayBossSnapshot = default;
    var relayPlayerStateArrived = false;
    var relayBossStateArrived = false;
    for (var attempt = 0; attempt < 200 &&
        (!relayPlayerStateArrived || !relayBossStateArrived); attempt++)
    {
        internetHost.Update();
        internetClient.Update();
        if (!relayPlayerStateArrived)
            relayPlayerStateArrived = internetClient.TryReceivePlayerState(out relayPlayerState);
        if (!relayBossStateArrived)
            relayBossStateArrived = internetClient.TryReceiveBossState(out relayBossSnapshot);
        if (!relayPlayerStateArrived || !relayBossStateArrived)
            Thread.Sleep(5);
    }
    Assert(relayPlayerStateArrived && relayPlayerState.Tick == 1249 &&
        relayPlayerState.StateSessionNonce == relayStateNonceA &&
        relayPlayerState.PlayerOnePressed == InputButtons.Super &&
        relayPlayerState.PlayerOneReleased == InputButtons.Super &&
        (relayPlayerState.PlayerTwoMotionFlags & PlayerMotionFlags.Hit) != 0 &&
        relayPlayerState.PlayerTwoHitDirection == -1,
        "El relay no conservó el PlayerState más reciente y sus bordes.");
    Assert(relayBossStateArrived && relayBossSnapshot.Tick == 1249,
        "El relay no conservó únicamente el BossState más reciente.");
    Assert(!internetClient.TryReceivePlayerState(out _) &&
        !internetClient.TryReceiveBossState(out _),
        "El relay entregó snapshots obsoletos de la cola congestionada.");

    internetHost.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 1,
        StateSessionNonce = relayStateNonceB,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 3,
    });
    relayPlayerStateArrived = false;
    for (var attempt = 0; attempt < 200 && !relayPlayerStateArrived; attempt++)
    {
        internetHost.Update(); internetClient.Update();
        relayPlayerStateArrived = internetClient.TryReceivePlayerState(
            out relayPlayerState);
        Thread.Sleep(5);
    }
    Assert(relayPlayerStateArrived && relayPlayerState.Tick == 1 &&
        relayPlayerState.StateSessionNonce == relayStateNonceB,
        "El relay no reinició el filtro de tick al cambiar época de estado.");

    internetHost.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 1250,
        StateSessionNonce = relayStateNonceA,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 3,
        PlayerOnePressed = InputButtons.Super,
    });
    for (var attempt = 0; attempt < 30; attempt++)
    {
        internetHost.Update(); internetClient.Update(); Thread.Sleep(2);
    }
    Assert(!internetClient.TryReceivePlayerState(out _),
        "El relay volvió a una época de estado retirada.");

    internetHost.SendPlayerState(new PlayerStateSnapshot
    {
        Tick = 2,
        StateSessionNonce = relayStateNonceB,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 3,
    });
    relayPlayerStateArrived = false;
    for (var attempt = 0; attempt < 200 && !relayPlayerStateArrived; attempt++)
    {
        internetHost.Update(); internetClient.Update();
        relayPlayerStateArrived = internetClient.TryReceivePlayerState(
            out relayPlayerState);
        Thread.Sleep(5);
    }
    Assert(relayPlayerStateArrived && relayPlayerState.Tick == 2 &&
        relayPlayerState.StateSessionNonce == relayStateNonceB,
        "El relay perdió la época de estado vigente tras rechazar una retirada.");

    var relayPacketHandler = typeof(RelayInputTransport).GetMethod(
        "HandleGamePacket",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    var relayBurstFirst = new PlayerStateSnapshot
    {
        Tick = 3,
        StateSessionNonce = relayStateNonceB,
        PresentMask = 3,
        PlayerOneHealth = 3,
        PlayerTwoHealth = 2,
        PlayerTwoMotionFlags = PlayerMotionFlags.Hit,
        PlayerTwoHitDirection = -1,
    };
    var relayBurstSecond = relayBurstFirst;
    relayBurstSecond.Tick = 4;
    relayBurstSecond.PlayerTwoMotionFlags = PlayerMotionFlags.None;
    relayBurstSecond.PlayerTwoHitDirection = 0;
    relayPacketHandler!.Invoke(internetClient, new object[]
    {
        LanPlayerStatePacketCodec.Encode(relayBurstFirst)
    });
    relayPacketHandler.Invoke(internetClient, new object[]
    {
        LanPlayerStatePacketCodec.Encode(relayBurstSecond)
    });
    Assert(internetClient.TryReceivePlayerState(out var relayBurstReceivedFirst) &&
        internetClient.TryReceivePlayerState(out var relayBurstReceivedSecond) &&
        relayBurstReceivedFirst.Tick == 3 &&
        (relayBurstReceivedFirst.PlayerTwoMotionFlags &
            PlayerMotionFlags.Hit) != 0 &&
        relayBurstReceivedSecond.Tick == 4 &&
        !internetClient.TryReceivePlayerState(out _),
        "El relay no conservó en orden una ráfaga recibida de PlayerState.");

    var incompatibleRelayPacket = InputFramePacketCodec.Encode(new InputFrame
    {
        Tick = 3,
        InputSessionNonce = relayInputNonceB,
    });
    incompatibleRelayPacket[4]--;
    relayPacketHandler!.Invoke(internetHost,
        new object[] { incompatibleRelayPacket });
    Assert(!internetHost.IsConnected &&
        internetHost.Status.Contains("incompatible"),
        "El relay dejó una versión incompatible como conectada.");
    relayProcess.Kill(true);
    relayProcess.WaitForExit();
}

var signalingUrl = Environment.GetEnvironmentVariable("COOPHEAD_SIGNALING_URL");
if (!string.IsNullOrEmpty(signalingUrl))
{
    using var fakeStun = new System.Net.Sockets.UdpClient(
        new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
    var fakeStunPort = ((System.Net.IPEndPoint)fakeStun.Client.LocalEndPoint!).Port;
    var stunRunning = true;
    var stunThread = new Thread(() =>
    {
        while (stunRunning)
        {
            try
            {
                var sender = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                var request = fakeStun.Receive(ref sender);
                if (request.Length != 20) continue;
                var response = CreateStunResponse(request, sender);
                fakeStun.Send(response, response.Length, sender);
            }
            catch (System.Net.Sockets.SocketException) { if (!stunRunning) return; }
        }
    }) { IsBackground = true };
    stunThread.Start();
    using var p2pHost = new P2pInputTransport(signalingUrl, true, "", "127.0.0.1", fakeStunPort, versionToken);
    for (var attempt = 0; attempt < 800 && p2pHost.RoomCode.Length != 6; attempt++)
    { p2pHost.Update(); Thread.Sleep(5); }
    Assert(p2pHost.RoomCode.Length == 6, "P2P no creó código de sala: " + p2pHost.Status);
    using var p2pGuest = new P2pInputTransport(signalingUrl, false, p2pHost.RoomCode,
        "127.0.0.1", fakeStunPort, versionToken);
    for (var attempt = 0; attempt < 1200 && (!p2pHost.IsConnected || !p2pGuest.IsConnected); attempt++)
    { p2pHost.Update(); p2pGuest.Update(); Thread.Sleep(5); }
    Assert(p2pHost.IsConnected && p2pGuest.IsConnected,
        "P2P no completó hole punching: host=" + p2pHost.Status + ", guest=" + p2pGuest.Status);
    p2pGuest.Send(new InputFrame
    {
        Tick = 808,
        Held = InputButtons.Dash,
        GuestLoadoutRevision = 7,
        GuestLoadout = sentGuestLoadout,
    });
    InputFrame p2pFrame = default; var p2pArrived = false;
    for (var attempt = 0; attempt < 200 && !p2pArrived; attempt++)
    { p2pGuest.Update(); p2pHost.Update(); p2pArrived = p2pHost.TryReceive(0, out p2pFrame); Thread.Sleep(5); }
    Assert(p2pArrived && p2pFrame.Tick == 808 &&
        p2pFrame.HasHeld(InputButtons.Dash) &&
        p2pFrame.GuestLoadoutRevision == 7 &&
        p2pFrame.GuestLoadout.SameAs(sentGuestLoadout),
        "El frame o el loadout no atravesaron P2P.");
    stunRunning = false; fakeStun.Close(); stunThread.Join(1000);
}

Console.WriteLine("TransportChecks: OK");
return;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static byte[] CreateStunResponse(byte[] request, System.Net.IPEndPoint endpoint)
{
    var response = new byte[32];
    response[0] = 1; response[1] = 1; response[3] = 12;
    response[4] = 0x21; response[5] = 0x12; response[6] = 0xA4; response[7] = 0x42;
    Buffer.BlockCopy(request, 8, response, 8, 12);
    response[21] = 0x20; response[23] = 8; response[25] = 1;
    var port = endpoint.Port ^ 0x2112; response[26] = (byte)(port >> 8); response[27] = (byte)port;
    var address = endpoint.Address.GetAddressBytes(); var cookie = new byte[] { 0x21, 0x12, 0xA4, 0x42 };
    for (var i = 0; i < 4; i++) response[28 + i] = (byte)(address[i] ^ cookie[i]);
    return response;
}
