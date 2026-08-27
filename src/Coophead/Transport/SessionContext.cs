namespace Coophead.Transport
{
    internal struct SessionContext
    {
        public uint Sequence;
        public byte SaveSlot;
        public byte Flags;
        public byte Difficulty;
        public int CurrentMap;
        public int CurrentLevel;

        public bool HasSave => (Flags & 1) != 0;
        public bool PlayerOneIsMugman => (Flags & 2) != 0;
        public bool IsInLevel => (Flags & 4) != 0;
    }
}
