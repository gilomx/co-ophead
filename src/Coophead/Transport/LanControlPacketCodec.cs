using System;

namespace Coophead.Transport
{
    internal static class LanControlPacketCodec
    {
        public const byte Hello = 2;
        public const byte HelloAck = 3;
        public const byte Ping = 4;
        public const byte Pong = 5;
        public const byte Reject = 6;
        public const byte SceneAck = 8;
        public const byte ContextAck = 10;
        public const int PacketSize = 10;

        public static byte[] Encode(byte type, uint value)
        {
            var packet = new byte[PacketSize];
            packet[0] = (byte)'C';
            packet[1] = (byte)'O';
            packet[2] = (byte)'O';
            packet[3] = (byte)'P';
            packet[4] = InputFramePacketCodec.ProtocolVersion;
            packet[5] = type;
            WriteUInt32(packet, 6, value);
            return packet;
        }

        public static bool TryDecode(byte[] packet, out byte type, out uint value)
        {
            type = 0;
            value = 0;
            if (packet == null || packet.Length != PacketSize)
                return false;
            if (packet[0] != 'C' || packet[1] != 'O' || packet[2] != 'O' || packet[3] != 'P')
                return false;
            if (packet[4] != InputFramePacketCodec.ProtocolVersion)
                return false;

            type = packet[5];
            value = ReadUInt32(packet, 6);
            return type >= Hello && type <= ContextAck &&
                type != LanScenePacketCodec.ScenePacketType &&
                type != LanSessionContextPacketCodec.ContextPacketType;
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
