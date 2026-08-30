using System;

namespace Coophead
{
    [Flags]
    internal enum InputFrameFlags : byte
    {
        None = 0,
        WaitingForHost = 1 << 0,
        LevelReady = 1 << 1,
        Loading = 1 << 2,
    }

    [Flags]
    internal enum InputButtons : uint
    {
        None = 0,
        Jump = 1u << 0,
        Shoot = 1u << 1,
        Super = 1u << 2,
        SwitchWeapon = 1u << 3,
        Lock = 1u << 4,
        Dash = 1u << 5,
        Pause = 1u << 6,
        Accept = 1u << 7,
        Cancel = 1u << 8,
        EquipMenu = 1u << 9,
        Swap = 1u << 10,
        MenuUp = 1u << 11,
        MenuLeft = 1u << 12,
        MenuDown = 1u << 13,
        MenuRight = 1u << 14,
    }

    [Flags]
    internal enum PlayerLoadoutFlags : byte
    {
        None = 0,
        HasEquippedSecondaryRegularWeapon = 1 << 0,
        HasEquippedSecondaryShmupWeapon = 1 << 1,
        MustNotifySwitchRegularWeapon = 1 << 2,
        MustNotifySwitchShmupWeapon = 1 << 3,
    }

    // IDs serializados del juego. Se mantienen como enteros para que el
    // transporte y sus herramientas no dependan de Assembly-CSharp.
    internal struct PlayerLoadoutSnapshot
    {
        public int PrimaryWeapon;
        public int SecondaryWeapon;
        public int Super;
        public int Charm;
        public PlayerLoadoutFlags Flags;

        public bool SameAs(PlayerLoadoutSnapshot other)
        {
            return PrimaryWeapon == other.PrimaryWeapon &&
                SecondaryWeapon == other.SecondaryWeapon &&
                Super == other.Super && Charm == other.Charm &&
                Flags == other.Flags;
        }
    }

    internal struct InputFrame
    {
        public uint Tick;
        public sbyte Horizontal;
        public sbyte Vertical;
        public InputFrameFlags Flags;
        public uint ReadyTransitionId;
        // Secuencia persistente: permite que un tap de EX sobreviva aunque se
        // pierda el datagrama exacto que contenía el borde Pressed.
        public uint PlayerTwoSuperRequestSequence;
        public uint InputSessionNonce;
        // El invitado repite su selección hasta que el host la reciba. La época
        // de input distingue una revisión reutilizada después de reconectar.
        public uint GuestLoadoutRevision;
        public PlayerLoadoutSnapshot GuestLoadout;
        public InputButtons Held;
        public InputButtons Pressed;
        public InputButtons Released;

        public float GetAxis(int actionId)
        {
            if (actionId == 0 || actionId == 22)
                return Horizontal / 127f;
            if (actionId == 1 || actionId == 23)
                return Vertical / 127f;
            return 0f;
        }

        public bool HasHeld(InputButtons button) => (Held & button) != 0;
        public bool HasPressed(InputButtons button) => (Pressed & button) != 0;
        public bool HasReleased(InputButtons button) => (Released & button) != 0;
    }
}
