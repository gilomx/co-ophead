using System;

namespace Coophead.Transport
{
    [Flags]
    internal enum PlayerStateFlags : byte
    {
        None = 0,
        GameplayStarted = 1 << 0,
    }

    [Flags]
    internal enum PlayerMotionFlags : byte
    {
        None = 0,
        Dashing = 1 << 0,
        Hit = 1 << 1,
        UsingSuperOrEx = 1 << 2,
    }

    internal struct PlayerStateSnapshot
    {
        public uint Tick;
        public uint TransitionId;
        public byte PresentMask;
        public byte DeadMask;
        public float PlayerOneX;
        public float PlayerOneY;
        public float PlayerTwoX;
        public float PlayerTwoY;
        public byte PlayerOneHealth;
        public byte PlayerTwoHealth;
        public float PlayerOneSuperMeter;
        public float PlayerTwoSuperMeter;
        public sbyte PlayerOneMapHorizontal;
        public sbyte PlayerOneMapVertical;
        public PlayerStateFlags Flags;
        public InputButtons PlayerOneHeld;
        public InputButtons PlayerOnePressed;
        public InputButtons PlayerOneReleased;
        public PlayerMotionFlags PlayerOneMotionFlags;
        public PlayerMotionFlags PlayerTwoMotionFlags;
        public sbyte PlayerTwoHitDirection;
        public uint PlayerOneSuperActionSequence;
    }
}
