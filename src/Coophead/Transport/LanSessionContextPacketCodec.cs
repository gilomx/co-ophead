namespace Coophead.Transport
{
    internal static class LanSessionContextPacketCodec
    {
        public const byte ContextPacketType = 9;
        public const int PacketSize = 26;

        public static byte[] Encode(SessionContext context)
        {
            var packet = new byte[PacketSize];
            packet[0] = (byte)'C'; packet[1] = (byte)'O';
            packet[2] = (byte)'O'; packet[3] = (byte)'P';
            packet[4] = InputFramePacketCodec.ProtocolVersion;
            packet[5] = ContextPacketType;
            WriteUInt32(packet, 6, context.Sequence);
            packet[10] = context.SaveSlot;
            packet[11] = context.Flags;
            packet[12] = context.Difficulty;
            packet[13] = context.ResumeSeconds;
            WriteUInt32(packet, 14, unchecked((uint)context.CurrentMap));
            WriteUInt32(packet, 18, unchecked((uint)context.CurrentLevel));
            WriteUInt32(packet, 22, context.LoadTransitionId);
            return packet;
        }

        public static bool TryDecode(byte[] packet, out SessionContext context)
        {
            context = default(SessionContext);
            if (packet == null || packet.Length != PacketSize)
                return false;
            if (packet[0] != 'C' || packet[1] != 'O' || packet[2] != 'O' || packet[3] != 'P')
                return false;
            if (packet[4] != InputFramePacketCodec.ProtocolVersion || packet[5] != ContextPacketType)
                return false;
            context.Sequence = ReadUInt32(packet, 6);
            context.SaveSlot = packet[10];
            context.Flags = packet[11];
            context.Difficulty = packet[12];
            context.ResumeSeconds = packet[13];
            context.CurrentMap = unchecked((int)ReadUInt32(packet, 14));
            context.CurrentLevel = unchecked((int)ReadUInt32(packet, 18));
            context.LoadTransitionId = ReadUInt32(packet, 22);
            return context.Sequence != 0 && context.Difficulty <= 2;
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
            return (uint)(buffer[offset] | buffer[offset + 1] << 8 |
                buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
        }
    }
}
