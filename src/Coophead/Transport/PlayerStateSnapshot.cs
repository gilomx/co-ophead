namespace Coophead.Transport
{
    internal struct PlayerStateSnapshot
    {
        public uint Tick;
        public byte PresentMask;
        public byte DeadMask;
        public float PlayerOneX;
        public float PlayerOneY;
        public float PlayerTwoX;
        public float PlayerTwoY;
        public byte PlayerOneHealth;
        public byte PlayerTwoHealth;
    }
}
