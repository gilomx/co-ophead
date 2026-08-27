using System;

namespace Coophead
{
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
    }

    internal struct InputFrame
    {
        public uint Tick;
        public sbyte Horizontal;
        public sbyte Vertical;
        public InputButtons Held;
        public InputButtons Pressed;
        public InputButtons Released;

        public float GetAxis(int actionId)
        {
            if (actionId == 0)
                return Horizontal / 127f;
            if (actionId == 1)
                return Vertical / 127f;
            return 0f;
        }

        public bool HasHeld(InputButtons button) => (Held & button) != 0;
        public bool HasPressed(InputButtons button) => (Pressed & button) != 0;
        public bool HasReleased(InputButtons button) => (Released & button) != 0;
    }
}
