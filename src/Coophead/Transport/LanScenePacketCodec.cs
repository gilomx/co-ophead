using System;
using System.Text;

namespace Coophead.Transport
{
    internal static class LanScenePacketCodec
    {
        public const byte ScenePacketType = 7;
        public const int HeaderSize = 12;
        public const int MaxSceneNameBytes = 96;

        public static byte[] Encode(SceneCommand command)
        {
            var nameBytes = Encoding.UTF8.GetBytes(command.SceneName ?? string.Empty);
            if (nameBytes.Length == 0 || nameBytes.Length > MaxSceneNameBytes)
                throw new ArgumentException("Nombre de escena LAN inválido.", "command");

            var packet = new byte[HeaderSize + nameBytes.Length];
            packet[0] = (byte)'C';
            packet[1] = (byte)'O';
            packet[2] = (byte)'O';
            packet[3] = (byte)'P';
            packet[4] = InputFramePacketCodec.ProtocolVersion;
            packet[5] = ScenePacketType;
            WriteUInt32(packet, 6, command.Sequence);
            packet[10] = command.LoadMode;
            packet[11] = (byte)nameBytes.Length;
            Buffer.BlockCopy(nameBytes, 0, packet, HeaderSize, nameBytes.Length);
            return packet;
        }

        public static bool TryDecode(byte[] packet, out SceneCommand command)
        {
            command = default(SceneCommand);
            if (packet == null || packet.Length < HeaderSize)
                return false;
            if (packet[0] != 'C' || packet[1] != 'O' || packet[2] != 'O' || packet[3] != 'P')
                return false;
            if (packet[4] != InputFramePacketCodec.ProtocolVersion || packet[5] != ScenePacketType)
                return false;

            var nameLength = packet[11];
            if (nameLength == 0 || nameLength > MaxSceneNameBytes || packet.Length != HeaderSize + nameLength)
                return false;

            command.Sequence = ReadUInt32(packet, 6);
            command.LoadMode = packet[10];
            command.SceneName = Encoding.UTF8.GetString(packet, HeaderSize, nameLength);
            return command.Sequence != 0;
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
