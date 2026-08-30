using System;

namespace Coophead.Transport
{
    internal static class LanBossStatePacketCodec
    {
        public const byte PacketType = 12;
        public const int PacketSize = 54;

        public static byte[] Encode(BossStateSnapshot state)
        {
            var packet = new byte[PacketSize];
            packet[0] = (byte)'C';
            packet[1] = (byte)'O';
            packet[2] = (byte)'O';
            packet[3] = (byte)'P';
            packet[4] = InputFramePacketCodec.ProtocolVersion;
            packet[5] = PacketType;
            WriteUInt32(packet, 6, state.Tick);
            WriteUInt32(packet, 10, state.TransitionId);
            WriteInt32(packet, 14, state.LevelId);
            packet[18] = (byte)state.Flags;
            packet[19] = state.Phase;
            packet[20] = state.ActiveActor;
            packet[21] = state.ActionState;
            WriteFloat(packet, 22, state.CurrentHealth);
            WriteFloat(packet, 26, state.TotalHealth);
            WriteFloat(packet, 30, state.X);
            WriteFloat(packet, 34, state.Y);
            WriteFloat(packet, 38, state.ScaleX);
            WriteFloat(packet, 42, state.ScaleY);
            WriteInt32(packet, 46, state.AnimatorStateHash);
            WriteFloat(packet, 50, state.AnimatorNormalizedTime);
            return packet;
        }

        public static bool TryDecode(byte[] packet, out BossStateSnapshot state)
        {
            state = default(BossStateSnapshot);
            if (packet == null || packet.Length != PacketSize ||
                packet[0] != 'C' || packet[1] != 'O' ||
                packet[2] != 'O' || packet[3] != 'P' ||
                packet[4] != InputFramePacketCodec.ProtocolVersion ||
                packet[5] != PacketType)
                return false;

            state.Tick = ReadUInt32(packet, 6);
            state.TransitionId = ReadUInt32(packet, 10);
            state.LevelId = ReadInt32(packet, 14);
            state.Flags = (BossStateFlags)packet[18];
            state.Phase = packet[19];
            state.ActiveActor = packet[20];
            state.ActionState = packet[21];
            state.CurrentHealth = ReadFloat(packet, 22);
            state.TotalHealth = ReadFloat(packet, 26);
            state.X = ReadFloat(packet, 30);
            state.Y = ReadFloat(packet, 34);
            state.ScaleX = ReadFloat(packet, 38);
            state.ScaleY = ReadFloat(packet, 42);
            state.AnimatorStateHash = ReadInt32(packet, 46);
            state.AnimatorNormalizedTime = ReadFloat(packet, 50);

            if (state.Tick == 0 ||
                (state.Flags & ~(BossStateFlags.Active | BossStateFlags.Defeated)) != 0 ||
                (state.ActiveActor & ~7) != 0 ||
                !IsFinite(state.CurrentHealth) || !IsFinite(state.TotalHealth) ||
                !IsFinite(state.X) || !IsFinite(state.Y) ||
                !IsFinite(state.ScaleX) || !IsFinite(state.ScaleY) ||
                !IsFinite(state.AnimatorNormalizedTime) ||
                state.CurrentHealth < 0f || state.TotalHealth < 0f ||
                state.CurrentHealth > state.TotalHealth + 0.001f ||
                ((state.Flags & BossStateFlags.Active) != 0 &&
                    state.TotalHealth <= 0f))
            {
                state = default(BossStateSnapshot);
                return false;
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void WriteFloat(byte[] buffer, int offset, float value)
        {
            WriteUInt32(buffer, offset,
                BitConverter.ToUInt32(BitConverter.GetBytes(value), 0));
        }

        private static float ReadFloat(byte[] buffer, int offset)
        {
            return BitConverter.ToSingle(
                BitConverter.GetBytes(ReadUInt32(buffer, offset)), 0);
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
            return (uint)(buffer[offset] |
                buffer[offset + 1] << 8 |
                buffer[offset + 2] << 16 |
                buffer[offset + 3] << 24);
        }
    }
}
