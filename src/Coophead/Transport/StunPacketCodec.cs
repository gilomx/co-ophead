using System;
using System.Net;

namespace Coophead.Transport
{
    internal static class StunPacketCodec
    {
        private const uint MagicCookie = 0x2112A442;

        public static byte[] CreateBindingRequest(byte[] transactionId)
        {
            if (transactionId == null || transactionId.Length != 12)
                throw new ArgumentException("STUN requiere un transaction ID de 12 bytes.");
            var packet = new byte[20];
            WriteUInt16(packet, 0, 0x0001); WriteUInt32(packet, 4, MagicCookie);
            Buffer.BlockCopy(transactionId, 0, packet, 8, 12);
            return packet;
        }

        public static bool TryReadBindingResponse(byte[] packet, byte[] transactionId,
            out IPEndPoint endpoint)
        {
            endpoint = null;
            if (packet == null || packet.Length < 20 || transactionId == null || transactionId.Length != 12 ||
                ReadUInt16(packet, 0) != 0x0101 || ReadUInt32(packet, 4) != MagicCookie) return false;
            for (var i = 0; i < 12; i++) if (packet[8 + i] != transactionId[i]) return false;
            var declaredEnd = Math.Min(packet.Length, 20 + ReadUInt16(packet, 2));
            for (var offset = 20; offset + 4 <= declaredEnd;)
            {
                var type = ReadUInt16(packet, offset); var length = ReadUInt16(packet, offset + 2);
                var value = offset + 4;
                if (value + length > declaredEnd) return false;
                if (type == 0x0020 && length >= 8 && packet[value + 1] == 0x01)
                {
                    var port = ReadUInt16(packet, value + 2) ^ (int)(MagicCookie >> 16);
                    var address = new byte[4];
                    address[0] = (byte)(packet[value + 4] ^ 0x21); address[1] = (byte)(packet[value + 5] ^ 0x12);
                    address[2] = (byte)(packet[value + 6] ^ 0xA4); address[3] = (byte)(packet[value + 7] ^ 0x42);
                    endpoint = new IPEndPoint(new IPAddress(address), port); return true;
                }
                offset = value + ((length + 3) & ~3);
            }
            return false;
        }

        private static void WriteUInt16(byte[] data, int offset, int value)
        { data[offset] = (byte)(value >> 8); data[offset + 1] = (byte)value; }
        private static int ReadUInt16(byte[] data, int offset) { return data[offset] << 8 | data[offset + 1]; }
        private static void WriteUInt32(byte[] data, int offset, uint value)
        { data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16); data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value; }
        private static uint ReadUInt32(byte[] data, int offset)
        { return (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]); }
    }
}
