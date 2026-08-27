using System;
using System.Net;
using System.Net.Sockets;

namespace Coophead.Transport
{
    internal sealed class UdpInputTransport : IInputFrameTransport
    {
        private readonly Socket socket;
        private readonly bool host;
        private readonly EndPoint target;
        private readonly byte[] receiveBuffer = new byte[InputFramePacketCodec.PacketSize];
        private InputButtons lastReceivedHeld;
        private uint lastReceivedTick;

        private UdpInputTransport(Socket socket, bool host, EndPoint target, string description)
        {
            this.socket = socket;
            this.host = host;
            this.target = target;
            Description = description;
        }

        public string Description { get; }

        public static UdpInputTransport CreateHost(int port)
        {
            var socket = CreateSocket();
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return new UdpInputTransport(socket, true, null, "LAN host UDP :" + port);
        }

        public static UdpInputTransport CreateClient(string hostAddress, int port)
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
                "LAN client UDP -> " + target);
        }

        public void Reset()
        {
            lastReceivedHeld = InputButtons.None;
            lastReceivedTick = 0;
            DrainSocket();
        }

        public void Send(InputFrame frame)
        {
            if (host)
                return;

            var packet = InputFramePacketCodec.Encode(frame);
            socket.SendTo(packet, target);
        }

        public bool TryReceive(uint receiverTick, out InputFrame frame)
        {
            frame = default(InputFrame);
            if (!host || !socket.Poll(0, SelectMode.SelectRead))
                return false;

            EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            int length;
            try
            {
                length = socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ref sender);
            }
            catch (SocketException)
            {
                return false;
            }

            if (length != InputFramePacketCodec.PacketSize)
                return false;

            var packet = new byte[length];
            Buffer.BlockCopy(receiveBuffer, 0, packet, 0, length);
            if (!InputFramePacketCodec.TryDecode(packet, out frame))
                return false;
            if (frame.Tick <= lastReceivedTick && lastReceivedTick != 0)
                return false;

            // Derivar bordes del estado mantenido hace que la siguiente trama válida
            // repare una pulsación o liberación cuyo datagrama se haya perdido.
            frame.Pressed = frame.Held & ~lastReceivedHeld;
            frame.Released = lastReceivedHeld & ~frame.Held;
            lastReceivedHeld = frame.Held;
            lastReceivedTick = frame.Tick;
            return true;
        }

        public void Dispose()
        {
            socket.Close();
        }

        private static Socket CreateSocket()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;
            return socket;
        }

        private void DrainSocket()
        {
            while (host && socket.Poll(0, SelectMode.SelectRead))
            {
                EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ref sender);
                }
                catch (SocketException)
                {
                    return;
                }
            }
        }
    }
}
