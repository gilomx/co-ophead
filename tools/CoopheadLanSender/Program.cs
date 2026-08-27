using System.Diagnostics;
using System.Runtime.InteropServices;
using Coophead;
using Coophead.Transport;

const uint VersionToken = 0x000500;
var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 27182;

Console.Title = "Co-ophead LAN Sender 0.5.0";
Console.WriteLine("Co-ophead LAN Sender 0.5.0");
Console.WriteLine($"Destino: {host}:{port}");
Console.WriteLine("Mantén Cuphead enfocado. F7 cierra este sender.");
Console.WriteLine();

using var transport = UdpInputTransport.CreateClient(host, port, VersionToken);
var stopwatch = Stopwatch.StartNew();
var nextFrameAt = stopwatch.Elapsed;
var frameInterval = TimeSpan.FromSeconds(1.0 / 60.0);
var previousHeld = InputButtons.None;
var lastStatus = string.Empty;
var lastPingPrintedUtc = DateTime.MinValue;
uint tick = 0;

while (!KeyDown(VirtualKey.F7))
{
    transport.Update();
    if (transport.Status != lastStatus)
    {
        lastStatus = transport.Status;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {lastStatus}");
    }
    if (transport.IsConnected && transport.PingMilliseconds >= 0 &&
        DateTime.UtcNow - lastPingPrintedUtc >= TimeSpan.FromSeconds(5))
    {
        lastPingPrintedUtc = DateTime.UtcNow;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ping {transport.PingMilliseconds} ms");
    }
    SceneCommand scene;
    while (transport.TryReceiveScene(out scene))
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] escena #{scene.Sequence}: {scene.SceneName}");

    if (stopwatch.Elapsed >= nextFrameAt)
    {
        nextFrameAt += frameInterval;
        tick++;
        var held = ReadButtons();
        transport.Send(new InputFrame
        {
            Tick = tick,
            Horizontal = ReadAxis(VirtualKey.Numpad4, VirtualKey.Numpad6),
            Vertical = ReadAxis(VirtualKey.Numpad2, VirtualKey.Numpad8),
            Held = held,
            Pressed = held & ~previousHeld,
            Released = previousHeld & ~held,
        });
        previousHeld = held;
    }

    Thread.Sleep(1);
}

Console.WriteLine("Sender cerrado.");

static sbyte ReadAxis(VirtualKey negative, VirtualKey positive)
{
    var value = 0;
    if (KeyDown(negative)) value--;
    if (KeyDown(positive)) value++;
    return (sbyte)(value * 127);
}

static InputButtons ReadButtons()
{
    var buttons = InputButtons.None;
    AddIfDown(ref buttons, VirtualKey.Numpad0, InputButtons.Jump);
    AddIfDown(ref buttons, VirtualKey.Numpad1, InputButtons.Shoot);
    AddIfDown(ref buttons, VirtualKey.Numpad9, InputButtons.Super);
    AddIfDown(ref buttons, VirtualKey.Numpad7, InputButtons.SwitchWeapon);
    AddIfDown(ref buttons, VirtualKey.Numpad5, InputButtons.Lock);
    AddIfDown(ref buttons, VirtualKey.Numpad3, InputButtons.Dash);
    AddIfDown(ref buttons, VirtualKey.Decimal, InputButtons.Swap);
    return buttons;
}

static void AddIfDown(ref InputButtons buttons, VirtualKey key, InputButtons button)
{
    if (KeyDown(key)) buttons |= button;
}

static bool KeyDown(VirtualKey key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

[DllImport("user32.dll")]
static extern short GetAsyncKeyState(int virtualKey);

enum VirtualKey
{
    F7 = 0x76,
    Numpad0 = 0x60,
    Numpad1 = 0x61,
    Numpad2 = 0x62,
    Numpad3 = 0x63,
    Numpad4 = 0x64,
    Numpad5 = 0x65,
    Numpad6 = 0x66,
    Numpad7 = 0x67,
    Numpad8 = 0x68,
    Numpad9 = 0x69,
    Decimal = 0x6E,
}
