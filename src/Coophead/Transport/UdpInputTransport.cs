using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Coophead.Transport
{
    internal sealed class UdpInputTransport : IInputFrameTransport
    {
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
        private readonly Socket socket;
        private readonly bool host;
        private readonly EndPoint configuredTarget;
        private readonly uint versionToken;
        private readonly Queue<InputFrame> receivedFrames = new Queue<InputFrame>();
        private readonly byte[] receiveBuffer = new byte[InputFramePacketCodec.PacketSize];
        private EndPoint peer;
        private InputButtons lastReceivedHeld;
        private uint lastReceivedTick;
        private DateTime lastHelloSentUtc;
        private DateTime lastPingSentUtc;
        private DateTime lastPacketReceivedUtc;

        private UdpInputTransport(Socket socket, bool host, EndPoint target,
            string description, uint versionToken)
        {
            this.socket = socket;
            this.host = host;
            configuredTarget = target;
            this.versionToken = versionToken;
            Description = description;
            Status = host ? "esperando cliente" : "buscando host";
            PingMilliseconds = -1;
        }

        public string Description { get; }
        public string Status { get; private set; }
        public bool IsConnected { get; private set; }
        public int PingMilliseconds { get; private set; }

        public static UdpInputTransport CreateHost(int port, uint versionToken)
        {
            return CreateHost(port, versionToken, IPAddress.Any);
        }

        internal static UdpInputTransport CreateHost(int port, uint versionToken, IPAddress bindAddress)
        {
            var socket = CreateSocket();
            socket.Bind(new IPEndPoint(bindAddress, port));
            return new UdpInputTransport(socket, true, null, "LAN host UDP :" + port, versionToken);
        }

        public static UdpInputTransport CreateClient(string hostAddress, int port, uint versionToken)
        {
            IPAddress address;
            if (!IPAddress.TryParse(hostAddress, out address))
            {
                var addresses = Dns.GetHostAddresses(hostAddress);
                if (addresses.Length == 0)
                    throw new InvalidOperationException("No se pudo resolver el host LAN: " + hostAddress);
                address = addresses[0];
            }
            var socket = CreateSocket();
            var target = new IPEndPoint(address, port);
            return new UdpInputTransport(socket, false, target,
                "LAN client UDP -> " + target, versionToken);
        }

        public void Reset()
        {
            receivedFrames.Clear();
            lastReceivedHeld = InputButtons.None;
            lastReceivedTick = 0;
            PingMilliseconds = -1;
            DrainSocket();
        }

        public void Update()
        {
            var now = DateTime.UtcNow;
            ReceiveAvailable(now);
            if (IsConnected && now - lastPacketReceivedUtc > Timeout)
                Disconnect(host ? "cliente desconectado; esperando" : "timeout; buscando host");
            if (!host && !IsConnected && now - lastHelloSentUtc >= RetryInterval)
            {
                SendControl(LanControlPacketCodec.Hello, versionToken, configuredTarget);
                lastHelloSentUtc = now;
                Status = "buscando host";
            }
            if (!host && IsConnected && now - lastPingSentUtc >= RetryInterval)
            {
                SendControl(LanControlPacketCodec.Ping, unchecked((uint)Environment.TickCount), peer);
                lastPingSentUtc = now;
            }
        }

        public void Send(InputFrame frame)
        {
            if (!host && IsConnected)
                SendPacket(InputFramePacketCodec.Encode(frame), peer);
        }

        public bool TryReceive(uint receiverTick, out InputFrame frame)
        {
            if (!host || receivedFrames.Count == 0)
            {
                frame = default(InputFrame);
                return false;
            }
            frame = receivedFrames.Dequeue();
            return true;
        }

        public void Dispose()
        {
            socket.Close();
        }

        private void ReceiveAvailable(DateTime now)
        {
            var processed = 0;
            while (processed++ < 64 && socket.Poll(0, SelectMode.SelectRead))
            {
                EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                int length;
                try
                {
                    length = socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length,
                        SocketFlags.None, ref sender);
                }
                catch (SocketException)
                {
                    return;
                }
                var packet = new byte[length];
                Buffer.BlockCopy(receiveBuffer, 0, packet, 0, length);
                HandlePacket(packet, sender, now);
            }
        }

        private void HandlePacket(byte[] packet, EndPoint sender, DateTime now)
        {
            byte type;
            uint value;
            if (LanControlPacketCodec.TryDecode(packet, out type, out value))
            {
                HandleControl(type, value, sender, now);
                return;
            }
            if (!host || !IsConnected || !SameEndpoint(sender, peer))
                return;
            InputFrame frame;
            if (!InputFramePacketCodec.TryDecode(packet, out frame))
                return;
            if (frame.Tick <= lastReceivedTick && lastReceivedTick != 0)
                return;
            frame.Pressed = frame.Held & ~lastReceivedHeld;
            frame.Released = lastReceivedHeld & ~frame.Held;
            lastReceivedHeld = frame.Held;
            lastReceivedTick = frame.Tick;
            lastPacketReceivedUtc = now;
            receivedFrames.Enqueue(frame);
        }

        private void HandleControl(byte type, uint value, EndPoint sender, DateTime now)
        {
            if (host && type == LanControlPacketCodec.Hello)
            {
                if (value != versionToken)
                {
                    SendControl(LanControlPacketCodec.Reject, versionToken, sender);
                    Status = "cliente rechazado: versión incompatible";
                    return;
                }
                peer = sender;
                IsConnected = true;
                lastPacketReceivedUtc = now;
                Status = "cliente conectado: " + sender;
                SendControl(LanControlPacketCodec.HelloAck, versionToken, peer);
                return;
            }
            if (!host && type == LanControlPacketCodec.HelloAck && value == versionToken &&
                SameEndpoint(sender, configuredTarget))
            {
                peer = sender;
                IsConnected = true;
                lastPacketReceivedUtc = now;
                Status = "conectado al host";
                return;
            }
            if (!host && type == LanControlPacketCodec.Reject && SameEndpoint(sender, configuredTarget))
            {
                Status = "rechazado: versión incompatible";
                IsConnected = false;
                return;
            }
            if (host && IsConnected && type == LanControlPacketCodec.Ping && SameEndpoint(sender, peer))
            {
                lastPacketReceivedUtc = now;
                SendControl(LanControlPacketCodec.Pong, value, peer);
                return;
            }
            if (!host && IsConnected && type == LanControlPacketCodec.Pong && SameEndpoint(sender, peer))
            {
                lastPacketReceivedUtc = now;
                PingMilliseconds = unchecked(Environment.TickCount - (int)value);
                if (PingMilliseconds < 0)
                    PingMilliseconds = 0;
                Status = "conectado al host; ping " + PingMilliseconds + " ms";
            }
        }

        private void Disconnect(string status)
        {
            IsConnected = false;
            peer = null;
            lastReceivedHeld = InputButtons.None;
            lastReceivedTick = 0;
            receivedFrames.Clear();
            PingMilliseconds = -1;
            Status = status;
        }

        private void SendControl(byte type, uint value, EndPoint destination)
        {
            SendPacket(LanControlPacketCodec.Encode(type, value), destination);
        }

        private void SendPacket(byte[] packet, EndPoint destination)
        {
            if (destination == null)
                return;
            try { socket.SendTo(packet, destination); }
            catch (SocketException) { }
        }

        private void DrainSocket()
        {
            while (socket.Poll(0, SelectMode.SelectRead))
            {
                EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length,
                        SocketFlags.None, ref sender);
                }
                catch (SocketException) { return; }
            }
        }

        private static bool SameEndpoint(EndPoint left, EndPoint right)
        {
            return left != null && right != null && left.ToString() == right.ToString();
        }

        private static Socket CreateSocket()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;
            return socket;
        }
    }
}
