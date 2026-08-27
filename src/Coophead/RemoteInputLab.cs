using UnityEngine;

namespace Coophead
{
    internal static class RemoteInputLab
    {
        private static InputFrame current;
        private static InputButtons previousHeld;
        private static bool playerTwoReported;

        public static bool Enabled { get; private set; }

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                Enabled = !Enabled;
                previousHeld = InputButtons.None;
                current = new InputFrame();
                playerTwoReported = false;
                Plugin.Log.LogMessage("Remote Input Lab " + (Enabled ? "ACTIVADO" : "DESACTIVADO") + ".");
            }

            if (!Enabled)
                return;

            EnsureMultiplayerState();
            ReportPlayerTwoWhenReady();

            var held = ReadButtons();
            current = new InputFrame
            {
                Tick = current.Tick + 1,
                Horizontal = ReadAxis(KeyCode.Keypad4, KeyCode.Keypad6),
                Vertical = ReadAxis(KeyCode.Keypad2, KeyCode.Keypad8),
                Held = held,
                Pressed = held & ~previousHeld,
                Released = previousHeld & ~held,
            };
            previousHeld = held;
        }

        public static void EnsureMultiplayerState()
        {
            if (!Enabled)
                return;

            try
            {
                PlayerManager.Multiplayer = true;
                PlayerManager.SetPlayerCanJoin(PlayerId.PlayerTwo, false, false);
                PlayerManager.SetPlayerCanSwitch(PlayerId.PlayerOne, false);
                PlayerManager.SetPlayerCanSwitch(PlayerId.PlayerTwo, false);
            }
            catch
            {
                // PlayerManager todavía no existe durante los primeros frames de arranque.
            }
        }

        private static void ReportPlayerTwoWhenReady()
        {
            if (playerTwoReported)
                return;

            try
            {
                if (PlayerManager.GetPlayer(PlayerId.PlayerTwo) == null)
                    return;

                playerTwoReported = true;
                Plugin.Log.LogMessage("Remote Input Lab detectó Player Two y tomó sus entradas.");
            }
            catch
            {
                // El diccionario de jugadores aún no está listo para esta escena.
            }
        }

        public static float GetAxis(int actionId) => current.GetAxis(actionId);

        public static bool GetButton(int actionId, ButtonPhase phase)
        {
            var button = MapButton(actionId);
            if (button == InputButtons.None)
                return false;

            if (phase == ButtonPhase.Pressed)
                return current.HasPressed(button);
            if (phase == ButtonPhase.Released)
                return current.HasReleased(button);
            return current.HasHeld(button);
        }

        private static sbyte ReadAxis(KeyCode negative, KeyCode positive)
        {
            var value = 0;
            if (Input.GetKey(negative))
                value--;
            if (Input.GetKey(positive))
                value++;
            return (sbyte)(value * 127);
        }

        private static InputButtons ReadButtons()
        {
            var buttons = InputButtons.None;
            AddIfHeld(ref buttons, KeyCode.Keypad0, InputButtons.Jump);
            AddIfHeld(ref buttons, KeyCode.Keypad1, InputButtons.Shoot);
            AddIfHeld(ref buttons, KeyCode.Keypad9, InputButtons.Super);
            AddIfHeld(ref buttons, KeyCode.Keypad7, InputButtons.SwitchWeapon);
            AddIfHeld(ref buttons, KeyCode.Keypad5, InputButtons.Lock);
            AddIfHeld(ref buttons, KeyCode.Keypad3, InputButtons.Dash);
            AddIfHeld(ref buttons, KeyCode.KeypadEnter, InputButtons.Pause);
            AddIfHeld(ref buttons, KeyCode.KeypadPeriod, InputButtons.Swap);
            return buttons;
        }

        private static void AddIfHeld(ref InputButtons buttons, KeyCode key, InputButtons button)
        {
            if (Input.GetKey(key))
                buttons |= button;
        }

        private static InputButtons MapButton(int actionId)
        {
            switch (actionId)
            {
                case 2: return InputButtons.Jump;
                case 3: return InputButtons.Shoot;
                case 4: return InputButtons.Super;
                case 5: return InputButtons.SwitchWeapon;
                case 6: return InputButtons.Lock;
                case 7: return InputButtons.Dash;
                case 8: return InputButtons.Pause;
                case 13: return InputButtons.Accept;
                case 14: return InputButtons.Cancel;
                case 15: return InputButtons.EquipMenu;
                case 26: return InputButtons.Swap;
                default: return InputButtons.None;
            }
        }
    }

    internal enum ButtonPhase
    {
        Held,
        Pressed,
        Released,
    }
}
