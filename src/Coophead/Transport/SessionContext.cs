namespace Coophead.Transport
{
    internal struct SessionContext
    {
        public uint Sequence;
        public byte SaveSlot;
        public byte Flags;
        public byte Difficulty;
        public byte ResumeSeconds;
        public int CurrentMap;
        public int CurrentLevel;
        public uint LoadTransitionId;
        public uint GuestLoadoutRevision;
        public PlayerLoadoutSnapshot PlayerOneLoadout;
        public PlayerLoadoutSnapshot PlayerTwoLoadout;

        public bool HasSave => (Flags & 1) != 0;
        public bool PlayerOneIsMugman => (Flags & 2) != 0;
        public bool IsInLevel => (Flags & 4) != 0;
        public bool SessionSuspended => (Flags & 8) != 0;
        public bool SessionResuming => (Flags & 16) != 0;
        public bool LevelGateReleased => (Flags & 32) != 0;
    }
}
