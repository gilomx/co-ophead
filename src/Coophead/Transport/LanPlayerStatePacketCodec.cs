using System;

namespace Coophead.Transport
{
    internal static class LanPlayerStatePacketCodec
    {
        public const byte PacketType = 11;
        public const int PacketSize = 64;
        private const InputButtons AllInputButtons =
            (InputButtons)((1u << 15) - 1u);

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
            packet[30] = unchecked((byte)state.PlayerOneMapHorizontal);
            packet[31] = unchecked((byte)state.PlayerOneMapVertical);
            WriteUInt32(packet, 32, state.TransitionId);
            packet[36] = (byte)state.Flags;
            WriteUInt32(packet, 37, (uint)state.PlayerOneHeld);
            WriteUInt32(packet, 41, (uint)state.PlayerOnePressed);
            WriteUInt32(packet, 45, (uint)state.PlayerOneReleased);
            WriteFloat(packet, 49, state.PlayerOneSuperMeter);
            WriteFloat(packet, 53, state.PlayerTwoSuperMeter);
            packet[57] = (byte)state.PlayerOneMotionFlags;
            packet[58] = (byte)state.PlayerTwoMotionFlags;
            packet[59] = unchecked((byte)state.PlayerTwoHitDirection);
            WriteUInt32(packet, 60, state.PlayerOneSuperActionSequence);
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
            state.PlayerOneMapHorizontal = unchecked((sbyte)packet[30]);
            state.PlayerOneMapVertical = unchecked((sbyte)packet[31]);
            state.TransitionId = ReadUInt32(packet, 32);
            state.Flags = (PlayerStateFlags)packet[36];
            state.PlayerOneHeld = (InputButtons)ReadUInt32(packet, 37);
            state.PlayerOnePressed = (InputButtons)ReadUInt32(packet, 41);
            state.PlayerOneReleased = (InputButtons)ReadUInt32(packet, 45);
            state.PlayerOneSuperMeter = ReadFloat(packet, 49);
            state.PlayerTwoSuperMeter = ReadFloat(packet, 53);
            state.PlayerOneMotionFlags = (PlayerMotionFlags)packet[57];
            state.PlayerTwoMotionFlags = (PlayerMotionFlags)packet[58];
            state.PlayerTwoHitDirection = unchecked((sbyte)packet[59]);
            state.PlayerOneSuperActionSequence = ReadUInt32(packet, 60);
            return state.Tick != 0 && (state.PresentMask & ~3) == 0 &&
                (state.DeadMask & ~3) == 0 &&
                (state.Flags & ~PlayerStateFlags.GameplayStarted) == 0 &&
                (state.PlayerOneMotionFlags & ~(PlayerMotionFlags.Dashing |
                    PlayerMotionFlags.Hit |
                    PlayerMotionFlags.UsingSuperOrEx)) == 0 &&
                (state.PlayerTwoMotionFlags & ~(PlayerMotionFlags.Dashing |
                    PlayerMotionFlags.Hit |
                    PlayerMotionFlags.UsingSuperOrEx)) == 0 &&
                state.PlayerTwoHitDirection >= -1 &&
                state.PlayerTwoHitDirection <= 1 &&
                (state.PlayerOneHeld & ~AllInputButtons) == 0 &&
                (state.PlayerOnePressed & ~AllInputButtons) == 0 &&
                (state.PlayerOneReleased & ~AllInputButtons) == 0 &&
                IsFinite(state.PlayerOneX) &&
                IsFinite(state.PlayerOneY) &&
                IsFinite(state.PlayerTwoX) &&
                IsFinite(state.PlayerTwoY) &&
                IsFinite(state.PlayerOneSuperMeter) &&
                IsFinite(state.PlayerTwoSuperMeter) &&
                state.PlayerOneSuperMeter >= 0f &&
                state.PlayerTwoSuperMeter >= 0f &&
                state.PlayerOneSuperMeter <= 100f &&
                state.PlayerTwoSuperMeter <= 100f;
        }

        public static void MergeTransientEvents(ref PlayerStateSnapshot state,
            PlayerStateSnapshot skipped)
        {
            state.PlayerOnePressed |= skipped.PlayerOnePressed;
            state.PlayerOneReleased |= skipped.PlayerOneReleased;
            if ((skipped.PlayerTwoMotionFlags & PlayerMotionFlags.Hit) == 0)
                return;
            var stateWasHit = (state.PlayerTwoMotionFlags &
                PlayerMotionFlags.Hit) != 0;
            state.PlayerTwoMotionFlags |= PlayerMotionFlags.Hit;
            if (!stateWasHit || state.PlayerTwoHitDirection == 0)
                state.PlayerTwoHitDirection = skipped.PlayerTwoHitDirection;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
