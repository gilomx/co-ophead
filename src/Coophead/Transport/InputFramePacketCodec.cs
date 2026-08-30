using System;

namespace Coophead.Transport
{
    internal static class InputFramePacketCodec
    {
        public const byte ProtocolVersion = 13;
        public const byte InputPacketType = 1;
        public const int PacketSize = 58;
        private const PlayerLoadoutFlags AllLoadoutFlags =
            PlayerLoadoutFlags.HasEquippedSecondaryRegularWeapon |
            PlayerLoadoutFlags.HasEquippedSecondaryShmupWeapon |
            PlayerLoadoutFlags.MustNotifySwitchRegularWeapon |
            PlayerLoadoutFlags.MustNotifySwitchShmupWeapon;

        public static byte[] Encode(InputFrame frame)
        {
            var packet = new byte[PacketSize];
            packet[0] = (byte)'C';
            packet[1] = (byte)'O';
            packet[2] = (byte)'O';
            packet[3] = (byte)'P';
            packet[4] = ProtocolVersion;
            packet[5] = InputPacketType;
            WriteUInt32(packet, 6, frame.Tick);
            packet[10] = unchecked((byte)frame.Horizontal);
            packet[11] = unchecked((byte)frame.Vertical);
            WriteUInt32(packet, 12, (uint)frame.Held);
            WriteUInt32(packet, 16, (uint)frame.Pressed);
            WriteUInt32(packet, 20, (uint)frame.Released);
            packet[24] = (byte)frame.Flags;
            WriteUInt32(packet, 25, frame.ReadyTransitionId);
            WriteUInt32(packet, 29, frame.PlayerTwoSuperRequestSequence);
            WriteUInt32(packet, 33, frame.InputSessionNonce);
            WriteUInt32(packet, 37, frame.GuestLoadoutRevision);
            WriteInt32(packet, 41, frame.GuestLoadout.PrimaryWeapon);
            WriteInt32(packet, 45, frame.GuestLoadout.SecondaryWeapon);
            WriteInt32(packet, 49, frame.GuestLoadout.Super);
            WriteInt32(packet, 53, frame.GuestLoadout.Charm);
            packet[57] = (byte)frame.GuestLoadout.Flags;
            return packet;
        }

        public static bool TryDecode(byte[] packet, out InputFrame frame)
        {
            frame = default(InputFrame);
            if (packet == null || packet.Length != PacketSize)
                return false;
            if (packet[0] != 'C' || packet[1] != 'O' || packet[2] != 'O' || packet[3] != 'P')
                return false;
            if (packet[4] != ProtocolVersion || packet[5] != InputPacketType)
                return false;

            frame.Tick = ReadUInt32(packet, 6);
            frame.Horizontal = unchecked((sbyte)packet[10]);
            frame.Vertical = unchecked((sbyte)packet[11]);
            frame.Held = (InputButtons)ReadUInt32(packet, 12);
            frame.Pressed = (InputButtons)ReadUInt32(packet, 16);
            frame.Released = (InputButtons)ReadUInt32(packet, 20);
            frame.Flags = (InputFrameFlags)packet[24];
            frame.ReadyTransitionId = ReadUInt32(packet, 25);
            frame.PlayerTwoSuperRequestSequence = ReadUInt32(packet, 29);
            frame.InputSessionNonce = ReadUInt32(packet, 33);
            frame.GuestLoadoutRevision = ReadUInt32(packet, 37);
            frame.GuestLoadout.PrimaryWeapon = ReadInt32(packet, 41);
            frame.GuestLoadout.SecondaryWeapon = ReadInt32(packet, 45);
            frame.GuestLoadout.Super = ReadInt32(packet, 49);
            frame.GuestLoadout.Charm = ReadInt32(packet, 53);
            frame.GuestLoadout.Flags = (PlayerLoadoutFlags)packet[57];

            if (frame.GuestLoadoutRevision == 0)
                return true;
            return (frame.GuestLoadout.Flags & ~AllLoadoutFlags) == 0 &&
                frame.GuestLoadout.PrimaryWeapon != 0 &&
                frame.GuestLoadout.SecondaryWeapon != 0 &&
                frame.GuestLoadout.Super != 0 &&
                frame.GuestLoadout.Charm != 0;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            WriteUInt32(buffer, offset, unchecked((uint)value));
        }

        private static int ReadInt32(byte[] buffer, int offset)
        {
            return unchecked((int)ReadUInt32(buffer, offset));
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
