using System;

namespace Coophead.Transport
{
    [Flags]
    internal enum BossStateFlags : byte
    {
        None = 0,
        Active = 1 << 0,
        Defeated = 1 << 1,
    }

    internal struct BossStateSnapshot
    {
        public uint Tick;
        public uint TransitionId;
        public int LevelId;
        public BossStateFlags Flags;
        public byte Phase;
        public byte ActiveActor;
        public byte ActionState;
        public float CurrentHealth;
        public float TotalHealth;
        public float X;
        public float Y;
        public float ScaleX;
        public float ScaleY;
        public int AnimatorStateHash;
        public float AnimatorNormalizedTime;
    }
}
