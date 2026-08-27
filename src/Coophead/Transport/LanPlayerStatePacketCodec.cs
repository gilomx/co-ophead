using System;

namespace Coophead.Transport
{
    internal static class LanPlayerStatePacketCodec
    {
        public const byte PacketType = 11;
        public const int PacketSize = 30;

        public static byte[] Encode(PlayerStateSnapshot state)
        {
            var packet = new byte[PacketSize];
            packet[0] = (byte)'C'; packet[1] = (byte)'O'; packet[2] = (byte)'O'; packet[3] = (byte)'P';
            packet[4] = InputFramePacketCodec.ProtocolVersion;
            packet[5] = PacketType;
            WriteUInt32(packet, 6, state.Tick);
            packet[10] = state.PresentMask;
            packet[11] = state.DeadMask;
            WriteFloat(packet, 12, state.PlayerOneX);
            WriteFloat(packet, 16, state.PlayerOneY);
            WriteFloat(packet, 20, state.PlayerTwoX);
            WriteFloat(packet, 24, state.PlayerTwoY);
            packet[28] = state.PlayerOneHealth;
            packet[29] = state.PlayerTwoHealth;
            return packet;
        }

        public static bool TryDecode(byte[] packet, out PlayerStateSnapshot state)
        {
            state = default(PlayerStateSnapshot);
            if (packet == null || packet.Length != PacketSize || packet[0] != 'C' ||
                packet[1] != 'O' || packet[2] != 'O' || packet[3] != 'P' ||
                packet[4] != InputFramePacketCodec.ProtocolVersion || packet[5] != PacketType)
                return false;
            state.Tick = ReadUInt32(packet, 6);
            state.PresentMask = packet[10];
            state.DeadMask = packet[11];
            state.PlayerOneX = ReadFloat(packet, 12);
            state.PlayerOneY = ReadFloat(packet, 16);
            state.PlayerTwoX = ReadFloat(packet, 20);
            state.PlayerTwoY = ReadFloat(packet, 24);
            state.PlayerOneHealth = packet[28];
            state.PlayerTwoHealth = packet[29];
            return state.Tick != 0 && (state.PresentMask & ~3) == 0 && (state.DeadMask & ~3) == 0;
        }

        private static void WriteFloat(byte[] buffer, int offset, float value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, buffer, offset, 4);
        }

        private static float ReadFloat(byte[] buffer, int offset)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value; buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16); buffer[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | buffer[offset + 1] << 8 |
                buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
        }
    }
}
