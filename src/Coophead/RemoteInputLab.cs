using UnityEngine;
using UnityEngine.SceneManagement;
using Coophead.Transport;

namespace Coophead
{
    internal static class RemoteInputLab
    {
        private const uint SimulatedLatencyFrames = 3;
        private const uint ModVersionToken = 0x001000;

        private static IInputFrameTransport transport =
            new LoopbackInputTransport(SimulatedLatencyFrames);
        private static InputTransportMode transportMode = InputTransportMode.Loopback;

        private static InputFrame received;
        private static InputButtons previousHeld;
        private static bool playerTwoReported;
        private static bool rewiredReadReported;
        private static uint sourceTick;
        private static string lastTransportStatus;
        private static bool originalRunInBackground;
        private static bool runInBackgroundCaptured;
        private static SessionContext lastSentContext;
        private static bool hasLastSentContext;
        private static SessionContext latestRemoteContext;
        private static bool hasRemoteContext;

        public static bool Enabled { get; private set; }
        private static bool IsHost => transportMode == InputTransportMode.LanHost ||
            transportMode == InputTransportMode.InternetHost;
        private static bool IsClient => transportMode == InputTransportMode.LanClient ||
            transportMode == InputTransportMode.InternetClient;
        public static bool DrivesPlayerTwo => Enabled && !IsClient;
        public static string TransportStatus => transport.Status;
        public static string CurrentRoomCode
        {
            get
            {
                var relay = transport as RelayInputTransport;
                return relay == null ? string.Empty : relay.RoomCode;
            }
        }

        public static void StartInternet(bool host, string relayAddress, int relayPort, string roomCode)
        {
            if (!host && (roomCode == null || roomCode.Trim().Length != 6))
                throw new System.ArgumentException("El código debe tener seis caracteres.");
            Configure(host ? InputTransportMode.InternetHost : InputTransportMode.InternetClient,
                "127.0.0.1", 27182, relayAddress, relayPort, roomCode);
            SetEnabled(true);
        }

        public static void Configure(InputTransportMode mode, string hostAddress, int port,
            string relayAddress, int relayPort, string roomCode)
        {
            if (port < 1 || port > 65535)
                throw new System.ArgumentOutOfRangeException("port", "LanPort debe estar entre 1 y 65535.");

            IInputFrameTransport nextTransport;
            if (mode == InputTransportMode.LanHost)
                nextTransport = UdpInputTransport.CreateHost(port, ModVersionToken);
            else if (mode == InputTransportMode.LanClient)
                nextTransport = UdpInputTransport.CreateClient(hostAddress, port, ModVersionToken);
            else if (mode == InputTransportMode.InternetHost)
                nextTransport = new RelayInputTransport(relayAddress, relayPort, true, string.Empty);
            else if (mode == InputTransportMode.InternetClient)
                nextTransport = new RelayInputTransport(relayAddress, relayPort, false, roomCode);
            else
                nextTransport = new LoopbackInputTransport(SimulatedLatencyFrames);

            transport.Dispose();
            transport = nextTransport;
            transportMode = mode;
            if (!runInBackgroundCaptured)
            {
                originalRunInBackground = Application.runInBackground;
                runInBackgroundCaptured = true;
            }
            if (mode != InputTransportMode.Loopback)
                Application.runInBackground = true;
            Plugin.Log.LogInfo("[InputLab] Transporte configurado: " + transport.Description);
        }

        public static void Shutdown()
        {
            Enabled = false;
            transport.Dispose();
            if (runInBackgroundCaptured)
                Application.runInBackground = originalRunInBackground;
        }

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.F8) && !Enabled)
                SetEnabled(true);

            if (Input.GetKeyDown(KeyCode.F7) && Enabled)
                SetEnabled(false);

            if (!Enabled)
                return;

            transport.Update();
            if (lastTransportStatus != transport.Status)
            {
                lastTransportStatus = transport.Status;
                Plugin.Log.LogMessage("[InputLab] " + transport.Status);
            }
            ProcessSceneCommands();
            ProcessSessionContexts();
            ProcessPlayerStates();

            if (DrivesPlayerTwo)
            {
                EnsureMultiplayerState();
                ReportPlayerTwoWhenReady();
            }

            sourceTick++;
            if (IsHost && sourceTick % 30 == 0)
                CaptureAndSendContext();
            if (IsHost && sourceTick % 3 == 0)
                CaptureAndSendPlayerState();
            if (!IsHost)
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

            if (!IsClient)
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
            lastTransportStatus = null;
            hasRemoteContext = false;
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

        public static void OnSceneLoaded(string sceneName, LoadSceneMode mode)
        {
            if (!IsHost || !IsStableScene(sceneName, mode))
                return;

            transport.SendScene(new SceneCommand
            {
                SceneName = sceneName,
                LoadMode = (byte)mode,
            });
            Plugin.Log.LogInfo("[SceneSync] Escena encolada: " + sceneName);
        }

        private static void ProcessSceneCommands()
        {
            if (!IsClient)
                return;

            SceneCommand command;
            while (transport.TryReceiveScene(out command))
            {
                Plugin.Log.LogMessage("[SceneSync] Escena recibida: " + command.SceneName +
                    " #" + command.Sequence);
                if (!IsStableScene(command.SceneName, (LoadSceneMode)command.LoadMode))
                    continue;
                if (SceneManager.GetActiveScene().name == command.SceneName)
                    continue;

                try
                {
                    LoadRemoteScene(command.SceneName);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("[SceneSync] No se pudo cargar " + command.SceneName +
                        ": " + ex.Message);
                }
            }
        }

        private static void LoadRemoteScene(string sceneName)
        {
            if (!SceneLoader.Exists || SceneLoader.CurrentlyLoading)
            {
                Plugin.Log.LogWarning("[SceneSync] El cargador de Cuphead todavía no está disponible.");
                return;
            }

            if (sceneName.StartsWith("scene_level_") && hasRemoteContext &&
                (latestRemoteContext.Flags & 4) != 0 && latestRemoteContext.CurrentLevel >= 0)
            {
                SceneLoader.LoadLevel(
                    (Levels)latestRemoteContext.CurrentLevel,
                    SceneLoader.Transition.Fade,
                    SceneLoader.Icon.Hourglass,
                    null);
                Plugin.Log.LogMessage("[SceneSync] Cuphead cargando nivel remoto " +
                    latestRemoteContext.CurrentLevel + ".");
                return;
            }

            if (!System.Enum.IsDefined(typeof(Scenes), sceneName))
                throw new System.ArgumentException("Escena desconocida: " + sceneName);
            var scene = (Scenes)System.Enum.Parse(typeof(Scenes), sceneName);

            SceneLoader.LoadScene(
                scene,
                SceneLoader.Transition.Fade,
                SceneLoader.Transition.Fade,
                SceneLoader.Icon.Hourglass,
                null);
            Plugin.Log.LogMessage("[SceneSync] Cuphead cargando escena remota " + sceneName + ".");
        }

        private static bool IsStableScene(string sceneName, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || string.IsNullOrEmpty(sceneName))
                return false;
            return sceneName != "scene_start" && sceneName != "scene_load_helper";
        }

        private static void CaptureAndSendContext()
        {
            try
            {
                var data = PlayerData.Data;
                var context = new SessionContext
                {
                    SaveSlot = (byte)UnityEngine.Mathf.Clamp(PlayerData.CurrentSaveFileIndex, 0, 2),
                    Flags = (byte)((data != null ? 1 : 0) |
                        (PlayerManager.player1IsMugman ? 2 : 0) |
                        (Level.Current != null ? 4 : 0)),
                    Difficulty = (byte)Level.CurrentMode,
                    CurrentMap = data == null ? -1 : (int)data.CurrentMap,
                    CurrentLevel = Level.Current == null ? -1 : (int)Level.Current.CurrentLevel,
                };

                if (hasLastSentContext && ContextEquals(context, lastSentContext) && sourceTick % 300 != 0)
                    return;
                lastSentContext = context;
                hasLastSentContext = true;
                transport.SendContext(context);
                Plugin.Log.LogInfo("[SessionSync] Contexto enviado: slot=" + context.SaveSlot +
                    " mugman=" + context.PlayerOneIsMugman + " difficulty=" + context.Difficulty +
                    " map=" + context.CurrentMap + " level=" + context.CurrentLevel);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[SessionSync] No se pudo capturar contexto: " + ex.Message);
            }
        }

        private static void ProcessSessionContexts()
        {
            if (!IsClient)
                return;

            SessionContext context;
            while (transport.TryReceiveContext(out context))
            {
                Plugin.Log.LogMessage("[SessionSync] Contexto recibido #" + context.Sequence +
                    ": slot=" + context.SaveSlot + " mugman=" + context.PlayerOneIsMugman +
                    " difficulty=" + context.Difficulty + " map=" + context.CurrentMap +
                    " level=" + context.CurrentLevel);
                if (!context.HasSave || context.SaveSlot > 2 || context.Difficulty > 2)
                    continue;
                latestRemoteContext = context;
                hasRemoteContext = true;
                try
                {
                    PlayerData.CurrentSaveFileIndex = context.SaveSlot;
                    PlayerManager.player1IsMugman = context.PlayerOneIsMugman;
                    Level.SetCurrentMode((Level.Mode)context.Difficulty);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("[SessionSync] No se pudo aplicar contexto: " + ex.Message);
                }
            }
        }

        private static bool ContextEquals(SessionContext left, SessionContext right)
        {
            return left.SaveSlot == right.SaveSlot && left.Flags == right.Flags &&
                left.Difficulty == right.Difficulty && left.CurrentMap == right.CurrentMap &&
                left.CurrentLevel == right.CurrentLevel;
        }

        private static void CaptureAndSendPlayerState()
        {
            var state = new PlayerStateSnapshot { Tick = sourceTick };
            CapturePlayer(PlayerId.PlayerOne, 1, ref state);
            CapturePlayer(PlayerId.PlayerTwo, 2, ref state);
            if (state.PresentMask != 0)
                transport.SendPlayerState(state);
        }

        private static void CapturePlayer(PlayerId id, byte mask, ref PlayerStateSnapshot state)
        {
            AbstractPlayerController player;
            try { player = PlayerManager.GetPlayer(id); }
            catch { return; }
            if (player == null)
                return;
            state.PresentMask |= mask;
            if (player.IsDead)
                state.DeadMask |= mask;
            var position = player.transform.position;
            var health = player.stats == null ? 0 : player.stats.Health;
            if (id == PlayerId.PlayerOne)
            {
                state.PlayerOneX = position.x; state.PlayerOneY = position.y;
                state.PlayerOneHealth = (byte)Mathf.Clamp(health, 0, 255);
            }
            else
            {
                state.PlayerTwoX = position.x; state.PlayerTwoY = position.y;
                state.PlayerTwoHealth = (byte)Mathf.Clamp(health, 0, 255);
            }
        }

        private static void ProcessPlayerStates()
        {
            if (!IsClient)
                return;
            PlayerStateSnapshot state;
            while (transport.TryReceivePlayerState(out state))
            {
                // Etapa de observación: recibir snapshots sin corregir aún la simulación local.
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
            Plugin.Log.LogMessage("[InputLab] Rewired Player 2 está consumiendo frames del transporte.");
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
