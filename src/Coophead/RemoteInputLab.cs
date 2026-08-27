using UnityEngine;
using Coophead.Transport;

namespace Coophead
{
    internal static class RemoteInputLab
    {
        private const uint SimulatedLatencyFrames = 3;

        private static IInputFrameTransport transport =
            new LoopbackInputTransport(SimulatedLatencyFrames);
        private static InputTransportMode transportMode = InputTransportMode.Loopback;

        private static InputFrame received;
        private static InputButtons previousHeld;
        private static bool playerTwoReported;
        private static bool rewiredReadReported;
        private static uint sourceTick;

        public static bool Enabled { get; private set; }
        public static bool DrivesPlayerTwo => Enabled && transportMode != InputTransportMode.LanClient;

        public static void Configure(InputTransportMode mode, string hostAddress, int port)
        {
            if (port < 1 || port > 65535)
                throw new System.ArgumentOutOfRangeException("port", "LanPort debe estar entre 1 y 65535.");

            IInputFrameTransport nextTransport;
            if (mode == InputTransportMode.LanHost)
                nextTransport = UdpInputTransport.CreateHost(port);
            else if (mode == InputTransportMode.LanClient)
                nextTransport = UdpInputTransport.CreateClient(hostAddress, port);
            else
                nextTransport = new LoopbackInputTransport(SimulatedLatencyFrames);

            transport.Dispose();
            transport = nextTransport;
            transportMode = mode;
            Plugin.Log.LogInfo("[InputLab] Transporte configurado: " + transport.Description);
        }

        public static void Shutdown()
        {
            Enabled = false;
            transport.Dispose();
        }

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.F8) && !Enabled)
                SetEnabled(true);

            if (Input.GetKeyDown(KeyCode.F7) && Enabled)
                SetEnabled(false);

            if (!Enabled)
                return;

            if (DrivesPlayerTwo)
            {
                EnsureMultiplayerState();
                ReportPlayerTwoWhenReady();
            }

            sourceTick++;
            if (transportMode != InputTransportMode.LanHost)
            {
                var held = ReadButtons();
                var sampled = new InputFrame
                {
                    Tick = sourceTick,
                    Horizontal = ReadAxis(KeyCode.Keypad4, KeyCode.Keypad6),
                    Vertical = ReadAxis(KeyCode.Keypad2, KeyCode.Keypad8),
                    Held = held,
                    Pressed = held & ~previousHeld,
                    Released = previousHeld & ~held,
                };
                previousHeld = held;
                transport.Send(sampled);
            }

            // Los bordes solo viven durante un tick receptor. Si un transporte no
            // entrega un frame nuevo, mantenemos ejes/botones pero no repetimos Down/Up.
            received.Pressed = InputButtons.None;
            received.Released = InputButtons.None;

            if (transportMode != InputTransportMode.LanClient)
            {
                InputFrame delivered;
                while (transport.TryReceive(sourceTick, out delivered))
                    received = delivered;
            }

        }

        private static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            previousHeld = InputButtons.None;
            received = new InputFrame();
            sourceTick = 0;
            transport.Reset();
            playerTwoReported = false;
            rewiredReadReported = false;
            Plugin.Log.LogMessage("Remote Input Lab " + (Enabled ? "ACTIVADO" : "DESACTIVADO") +
                (Enabled ? " (" + transport.Description + ")." : "."));
        }

        public static void EnsureMultiplayerState()
        {
            if (!DrivesPlayerTwo)
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

        public static float GetAxis(int actionId) => received.GetAxis(actionId);

        public static void ReportRewiredRead()
        {
            if (rewiredReadReported)
                return;

            rewiredReadReported = true;
            Plugin.Log.LogMessage("[InputLab] Rewired Player 2 está consumiendo frames loopback.");
        }

        public static bool GetButton(int actionId, ButtonPhase phase)
        {
            var button = MapButton(actionId);
            if (button == InputButtons.None)
                return false;

            if (phase == ButtonPhase.Pressed)
                return received.HasPressed(button);
            if (phase == ButtonPhase.Released)
                return received.HasReleased(button);
            return received.HasHeld(button);
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
