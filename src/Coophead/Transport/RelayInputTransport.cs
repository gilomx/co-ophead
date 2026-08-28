using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Coophead.Transport
{
    internal sealed class RelayInputTransport : IInputFrameTransport
    {
        private const byte Create = 1, Created = 2, Join = 3, Joined = 4, Data = 5, Error = 6;
        private readonly Socket socket;
        private readonly bool host;
        private readonly Queue<byte[]> outgoing = new Queue<byte[]>();
        private readonly List<byte> incoming = new List<byte>();
        private readonly Queue<InputFrame> frames = new Queue<InputFrame>();
        private readonly Queue<SceneCommand> scenes = new Queue<SceneCommand>();
        private readonly Queue<SessionContext> contexts = new Queue<SessionContext>();
        private readonly Queue<PlayerStateSnapshot> playerStates = new Queue<PlayerStateSnapshot>();
        private readonly byte[] receiveBuffer = new byte[4096];
        private byte[] sending;
        private int sendOffset;
        private uint nextSceneSequence = 1, nextContextSequence = 1;
        private uint lastInputTick, lastSceneSequence, lastContextSequence, lastStateTick;
        private InputButtons lastHeld;
        private bool handshakeQueued;

        public RelayInputTransport(string address, int port, bool host, string roomCode)
        {
            this.host = host;
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Blocking = false;
            var addresses = Dns.GetHostAddresses(address);
            if (addresses.Length == 0) throw new InvalidOperationException("Relay no encontrado.");
            try { socket.Connect(new IPEndPoint(addresses[0], port)); }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode != SocketError.WouldBlock && ex.SocketErrorCode != SocketError.InProgress)
                    throw;
            }
            RoomCode = (roomCode ?? string.Empty).Trim().ToUpperInvariant();
            Description = (host ? "Internet host" : "Internet client") + " -> " + address + ":" + port;
            Status = "conectando al relay";
            PingMilliseconds = -1;
        }

        public string Description { get; private set; }
        public string Status { get; private set; }
        public string RoomCode { get; private set; }
        public bool IsConnected { get; private set; }
        public int PingMilliseconds { get; private set; }
        public int EstimatedPacketLossPercent => -1;

        public void Update()
        {
            if (!socket.Connected)
            {
                try { if (!socket.Poll(0, SelectMode.SelectWrite)) return; }
                catch (SocketException) { Status = "relay desconectado"; return; }
            }
            if (!handshakeQueued)
            {
                QueueFrame(host ? Create : Join, host ? new byte[0] : Encoding.ASCII.GetBytes(RoomCode));
                handshakeQueued = true;
            }
            Flush();
            Receive();
            ParseFrames();
        }

        public void Reset()
        {
            frames.Clear(); scenes.Clear(); contexts.Clear(); playerStates.Clear();
            lastInputTick = lastSceneSequence = lastContextSequence = lastStateTick = 0;
            lastHeld = InputButtons.None;
        }

        public void Send(InputFrame frame) { if (!host && IsConnected) QueueData(InputFramePacketCodec.Encode(frame)); }
        public bool TryReceive(uint receiverTick, out InputFrame frame)
        {
            if (!host || frames.Count == 0) { frame = default(InputFrame); return false; }
            frame = frames.Dequeue(); return true;
        }
        public void SendScene(SceneCommand command)
        {
            if (!host || !IsConnected) return;
            command.Sequence = nextSceneSequence++; QueueData(LanScenePacketCodec.Encode(command));
        }
        public bool TryReceiveScene(out SceneCommand command)
        {
            if (host || scenes.Count == 0) { command = default(SceneCommand); return false; }
            command = scenes.Dequeue(); return true;
        }
        public void SendContext(SessionContext context)
        {
            if (!host || !IsConnected) return;
            context.Sequence = nextContextSequence++; QueueData(LanSessionContextPacketCodec.Encode(context));
        }
        public bool TryReceiveContext(out SessionContext context)
        {
            if (host || contexts.Count == 0) { context = default(SessionContext); return false; }
            context = contexts.Dequeue(); return true;
        }
        public void SendPlayerState(PlayerStateSnapshot state)
        {
            if (host && IsConnected) QueueData(LanPlayerStatePacketCodec.Encode(state));
        }
        public bool TryReceivePlayerState(out PlayerStateSnapshot state)
        {
            if (host || playerStates.Count == 0) { state = default(PlayerStateSnapshot); return false; }
            state = playerStates.Dequeue(); return true;
        }

        private void HandleRelayFrame(byte type, byte[] payload)
        {
            if (type == Created) { RoomCode = Encoding.ASCII.GetString(payload); Status = "sala " + RoomCode + "; esperando jugador"; }
            else if (type == Joined) { IsConnected = true; Status = "conectado; sala " + RoomCode; }
            else if (type == Error) { Status = "relay: " + Encoding.UTF8.GetString(payload); }
            else if (type == Data && IsConnected) HandleGamePacket(payload);
        }

        private void HandleGamePacket(byte[] packet)
        {
            InputFrame input;
            if (InputFramePacketCodec.TryDecode(packet, out input) && host)
            {
                if (input.Tick <= lastInputTick && lastInputTick != 0) return;
                input.Pressed = input.Held & ~lastHeld; input.Released = lastHeld & ~input.Held;
                lastHeld = input.Held; lastInputTick = input.Tick; frames.Enqueue(input); return;
            }
            SceneCommand scene;
            if (LanScenePacketCodec.TryDecode(packet, out scene) && !host && scene.Sequence > lastSceneSequence)
            { lastSceneSequence = scene.Sequence; scenes.Enqueue(scene); return; }
            SessionContext context;
            if (LanSessionContextPacketCodec.TryDecode(packet, out context) && !host && context.Sequence > lastContextSequence)
            { lastContextSequence = context.Sequence; contexts.Enqueue(context); return; }
            PlayerStateSnapshot state;
            if (LanPlayerStatePacketCodec.TryDecode(packet, out state) && !host && state.Tick > lastStateTick)
            { lastStateTick = state.Tick; playerStates.Clear(); playerStates.Enqueue(state); }
        }

        private void QueueData(byte[] payload) { QueueFrame(Data, payload); }
        private void QueueFrame(byte type, byte[] payload)
        {
            var frame = new byte[payload.Length + 5];
            Buffer.BlockCopy(BitConverter.GetBytes(payload.Length + 1), 0, frame, 0, 4);
            frame[4] = type; Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            outgoing.Enqueue(frame);
        }
        private void Flush()
        {
            try
            {
                while (true)
                {
                    if (sending == null) { if (outgoing.Count == 0) return; sending = outgoing.Dequeue(); sendOffset = 0; }
                    sendOffset += socket.Send(sending, sendOffset, sending.Length - sendOffset, SocketFlags.None);
                    if (sendOffset < sending.Length) return;
                    sending = null;
                }
            }
            catch (SocketException ex) { if (ex.SocketErrorCode != SocketError.WouldBlock) Status = "relay desconectado"; }
        }
        private void Receive()
        {
            try
            {
                while (socket.Poll(0, SelectMode.SelectRead))
                {
                    var count = socket.Receive(receiveBuffer);
                    if (count == 0) { Status = "relay desconectado"; IsConnected = false; return; }
                    for (var i = 0; i < count; i++) incoming.Add(receiveBuffer[i]);
                }
            }
            catch (SocketException ex) { if (ex.SocketErrorCode != SocketError.WouldBlock) Status = "relay desconectado"; }
        }
        private void ParseFrames()
        {
            while (incoming.Count >= 5)
            {
                var length = incoming[0] | incoming[1] << 8 | incoming[2] << 16 | incoming[3] << 24;
                if (length < 1 || length > 2049) { Status = "relay envió frame inválido"; return; }
                if (incoming.Count < length + 4) return;
                var payload = incoming.GetRange(5, length - 1).ToArray();
                var type = incoming[4]; incoming.RemoveRange(0, length + 4);
                HandleRelayFrame(type, payload);
            }
        }
        public void Dispose() { try { socket.Close(); } catch { } }
    }
}
