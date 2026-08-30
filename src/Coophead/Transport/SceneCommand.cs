namespace Coophead.Transport
{
    [System.Flags]
    internal enum SceneCommandFlags : byte
    {
        None = 0,
        CoordinatedTransition = 1 << 0,
        CancelCoordinatedTransition = 1 << 1,
    }

    internal struct SceneCommand
    {
        public uint Sequence;
        public byte LoadMode;
        public byte Difficulty;
        public SceneCommandFlags Flags;
        public int LevelId;
        public string SceneName;

        public bool IsCoordinatedTransition =>
            (Flags & SceneCommandFlags.CoordinatedTransition) != 0;
        public bool CancelsCoordinatedTransition =>
            (Flags & SceneCommandFlags.CancelCoordinatedTransition) != 0;
    }
}
