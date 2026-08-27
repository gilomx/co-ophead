using System;

namespace Coophead.Transport
{
    internal static class InputFramePacketCodec
    {
        public const byte ProtocolVersion = 1;
        public const int PacketSize = 23;

        public static byte[] Encode(InputFrame frame)
        {
            var packet = new byte[PacketSize];
            packet[0] = (byte)'C';
            packet[1] = (byte)'O';
            packet[2] = (byte)'O';
            packet[3] = (byte)'P';
            packet[4] = ProtocolVersion;
            WriteUInt32(packet, 5, frame.Tick);
            packet[9] = unchecked((byte)frame.Horizontal);
            packet[10] = unchecked((byte)frame.Vertical);
            WriteUInt32(packet, 11, (uint)frame.Held);
            WriteUInt32(packet, 15, (uint)frame.Pressed);
            WriteUInt32(packet, 19, (uint)frame.Released);
            return packet;
        }

        public static bool TryDecode(byte[] packet, out InputFrame frame)
        {
            frame = default(InputFrame);
            if (packet == null || packet.Length != PacketSize)
                return false;
            if (packet[0] != 'C' || packet[1] != 'O' || packet[2] != 'O' || packet[3] != 'P')
                return false;
            if (packet[4] != ProtocolVersion)
                return false;

            frame.Tick = ReadUInt32(packet, 5);
            frame.Horizontal = unchecked((sbyte)packet[9]);
            frame.Vertical = unchecked((sbyte)packet[10]);
            frame.Held = (InputButtons)ReadUInt32(packet, 11);
            frame.Pressed = (InputButtons)ReadUInt32(packet, 15);
            frame.Released = (InputButtons)ReadUInt32(packet, 19);
            return true;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                | buffer[offset + 1] << 8
                | buffer[offset + 2] << 16
                | buffer[offset + 3] << 24);
        }
    }
}
