using HarmonyLib;
using Rewired;
using UnityEngine;
using UnityEngine.SceneManagement;
using Coophead.Transport;

namespace Coophead
{
    internal static class RemoteInputLab
    {
        private const uint SimulatedLatencyFrames = 3;
        private const uint ModVersionToken = 0x001206;
        private const float PeerStallSeconds = 1.25f;
        private const float ResumeCountdownSeconds = 3f;
        private const float LongWaitSeconds = 15f;

        private static readonly System.Reflection.MethodInfo MapPlayerJoinedMethod =
            AccessTools.Method(typeof(Map), "OnPlayerJoined", new[] { typeof(PlayerId) });
        private static readonly System.Reflection.MethodInfo MapPlayerLeaveMethod =
            AccessTools.Method(typeof(Map), "OnPlayerLeave", new[] { typeof(PlayerId) });
        private static readonly System.Reflection.MethodInfo MapPlayerJumpCompleteMethod =
            AccessTools.Method(typeof(MapPlayerController), "OnJumpAnimationComplete",
                System.Type.EmptyTypes);
        private static readonly System.Reflection.MethodInfo LevelPlayerJoinedMethod =
            AccessTools.Method(typeof(Level), "OnPlayerJoined", new[] { typeof(PlayerId) });
        private static readonly System.Reflection.FieldInfo LevelAllowMultiplayerField =
            AccessTools.Field(typeof(Level), "allowMultiplayer");
        private static readonly System.Reflection.FieldInfo LevelPlayersField =
            AccessTools.Field(typeof(Level), "players");

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
        private static bool runInBackgroundForTesting = true;
        private static SessionContext lastSentContext;
        private static bool hasLastSentContext;
        private static SessionContext latestRemoteContext;
        private static bool hasRemoteContext;
        private static SceneCommand pendingRemoteScene;
        private static bool hasPendingRemoteScene;
        private static PlayerStateSnapshot latestRemotePlayerState;
        private static bool hasRemotePlayerState;
        private static bool remotePlayerStatePendingApply;
        private static float lastRemotePlayerStateRealtime;
        private static uint lastRemotePlayerStateTick;
        private static bool hasRemotePlayerStateTick;
        private static bool transportWasConnected;
        private static bool samplingLocalInput;
        private static bool lateSpawnFailureReported;
        private static bool lateSpawnAttempted;
        private static MapPlayerController lateSpawnedMapPlayer;
        private static float lateSpawnedMapPlayerRealtime;
        private static bool localFrameSentReported;
        private static bool remoteFrameReceivedReported;
        private static bool neutralInputWarningReported;
        private static uint localFramesSent;
        private static bool localInputReported;
        private static bool remoteInputReported;
        private static bool playerStateSentReported;
        private static bool playerStateReceivedReported;
        private static bool hostMapAxesReported;
        private static bool remoteMapAxesReported;
        private static bool playerOneVisualReported;
        private static int originalClientSaveSlot;
        private static bool originalClientPlayerOneIsMugman;
        private static bool originalClientSavePlayerOneIsMugman;
        private static Level.Mode originalClientDifficulty;
        private static bool originalClientInGame;
        private static bool originalClientContextCaptured;
        private static SessionHoldState sessionHoldState;
        private static string sessionHoldReason = string.Empty;
        private static float sessionHoldStartedRealtime;
        private static float resumeDeadlineRealtime;
        private static byte lastResumeSecondsSent = byte.MaxValue;
        private static bool pauseAppliedBySession;
        private static bool timeScalePausedBySession;
        private static float previousSessionTimeScale = 1f;
        private static bool levelGatePauseRequested;
        private static float lastRemoteInputRealtime;
        private static bool hasRemoteInputActivity;
        private static bool remoteClientWaiting;
        private static bool clientHoldAcknowledgedByHost;

        public static bool Enabled { get; private set; }
        private static bool IsHost => transportMode == InputTransportMode.LanHost ||
            transportMode == InputTransportMode.InternetHost || transportMode == InputTransportMode.P2pHost;
        private static bool IsClient => transportMode == InputTransportMode.LanClient ||
            transportMode == InputTransportMode.InternetClient || transportMode == InputTransportMode.P2pClient;
        public static bool DrivesPlayerTwo => Enabled;
        public static bool IsHostSession => Enabled && IsHost;
        public static bool IsClientSession => Enabled && IsClient;
        public static bool IsConnected => Enabled && transport.IsConnected;
        public static bool IsSamplingLocalInput => samplingLocalInput;
        public static bool PreventLocalSave => IsClientSession;
        public static string TransportStatus => transport.Status;
        public static int PingMilliseconds => transport.PingMilliseconds;
        public static int EstimatedPacketLossPercent =>
            transport.EstimatedPacketLossPercent;
        public static bool SessionOverlayVisible => Enabled &&
            sessionHoldState != SessionHoldState.None;
        public static bool SessionIsResuming =>
            sessionHoldState == SessionHoldState.Resuming;
        public static string SessionHoldReason => sessionHoldReason;
        public static int SessionResumeSeconds
        {
            get
            {
                if (!SessionIsResuming)
                    return 0;
                return Mathf.Max(1, Mathf.CeilToInt(
                    resumeDeadlineRealtime - Time.realtimeSinceStartup));
            }
        }
        public static bool CanLeaveInterruptedSession => SessionOverlayVisible &&
            !SessionIsResuming &&
            Time.realtimeSinceStartup - sessionHoldStartedRealtime >= LongWaitSeconds;
        public static string CurrentRoomCode
        {
            get
            {
                var relay = transport as RelayInputTransport;
                if (relay != null) return relay.RoomCode;
                var p2p = transport as P2pInputTransport;
                return p2p == null ? string.Empty : p2p.RoomCode;
            }
        }

        public static void SetRunInBackgroundForTesting(bool enabled)
        {
            runInBackgroundForTesting = enabled;
            Plugin.Log.LogInfo("[Testing] RunInBackground temporal: " +
                (enabled ? "ACTIVADO" : "DESACTIVADO") + ".");
        }

        public static void StartInternet(bool host, string signalingUrl, string stunHost,
            int stunPort, string roomCode)
        {
            if (!host && (roomCode == null || roomCode.Trim().Length != 6))
                throw new System.ArgumentException("El código debe tener seis caracteres.");
            Configure(host ? InputTransportMode.P2pHost : InputTransportMode.P2pClient,
                "127.0.0.1", 27182, "127.0.0.1", 27183, roomCode,
                signalingUrl, stunHost, stunPort);
            SetEnabled(true);
        }

        public static void StopSession()
        {
            if (!Enabled)
                return;

            LevelLoadGate.Reset();
            TryLeavePlayerTwo();
            SetEnabled(false);
            var previousTransport = transport;
            transport = new LoopbackInputTransport(SimulatedLatencyFrames);
            transportMode = InputTransportMode.Loopback;
            try
            {
                previousTransport.Dispose();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[InputLab] El transporte falló al cerrarse: " +
                    ex.Message);
            }
            finally
            {
                if (runInBackgroundCaptured)
                {
                    Application.runInBackground = originalRunInBackground;
                    runInBackgroundCaptured = false;
                }
                RestoreVanillaPlayerManagerState();
            }
        }

        public static void Configure(InputTransportMode mode, string hostAddress, int port,
            string relayAddress, int relayPort, string roomCode, string signalingUrl,
            string stunHost, int stunPort)
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
            else if (mode == InputTransportMode.P2pHost)
                nextTransport = new P2pInputTransport(signalingUrl, true, "", stunHost, stunPort, ModVersionToken);
            else if (mode == InputTransportMode.P2pClient)
                nextTransport = new P2pInputTransport(signalingUrl, false, roomCode, stunHost, stunPort, ModVersionToken);
            else
                nextTransport = new LoopbackInputTransport(SimulatedLatencyFrames);

            transport.Dispose();
            transport = nextTransport;
            transportMode = mode;
            ResetRemotePlayerState();
            transportWasConnected = false;
            if (!runInBackgroundCaptured)
            {
                originalRunInBackground = Application.runInBackground;
                runInBackgroundCaptured = true;
            }
            if (mode != InputTransportMode.Loopback)
                Application.runInBackground = runInBackgroundForTesting;
            Plugin.Log.LogInfo("[InputLab] Transporte configurado: " + transport.Description);
        }

        public static void Shutdown()
        {
            LevelLoadGate.Reset();
            RestoreClientContext();
            ResetSessionHoldState(true);
            Enabled = false;
            ResetRemotePlayerState();
            transport.Dispose();
            if (runInBackgroundCaptured)
            {
                Application.runInBackground = originalRunInBackground;
                runInBackgroundCaptured = false;
            }
        }

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.F8) && !Enabled)
                SetEnabled(true);

            if (Input.GetKeyDown(KeyCode.F7) && Enabled)
                StopSession();

            if (!Enabled)
                return;

            transport.Update();
            ProcessConnectionTransition();
            if (lastTransportStatus != transport.Status)
            {
                lastTransportStatus = transport.Status;
                Plugin.Log.LogMessage("[InputLab] " + transport.Status);
            }
            ProcessSessionContexts();
            ProcessSceneCommands();
            ProcessPlayerStates();
            LevelLoadGate.Update();

            EnsureMultiplayerState();
            EnsurePlayerTwoPresent();
            ReportPlayerTwoWhenReady();
            ApplyRemotePlayerState();

            sourceTick++;
            if (IsHost && sourceTick % 30 == 0)
                CaptureAndSendContext(false);
            if (IsHost && sourceTick % 3 == 0)
                CaptureAndSendPlayerState();

            // Los bordes solo viven durante un tick receptor. Si un transporte no
            // entrega un frame nuevo, mantenemos ejes/botones pero no repetimos Down/Up.
            received.Pressed = InputButtons.None;
            received.Released = InputButtons.None;

            if (IsClient)
            {
                if (transport.IsConnected)
                {
                    var sampled = SampleConfiguredPlayerInput();
                    sampled.Tick = sourceTick;
                    if (sessionHoldState != SessionHoldState.None ||
                        LevelLoadGate.IsHoldingGameplay)
                    {
                        sampled.Horizontal = 0;
                        sampled.Vertical = 0;
                        sampled.Held = InputButtons.None;
                    }
                    if (sessionHoldState != SessionHoldState.None)
                        sampled.Flags |= InputFrameFlags.WaitingForHost;
                    if (LevelLoadGate.ShouldReportReady)
                        sampled.Flags |= InputFrameFlags.LevelReady;
                    sampled.Pressed = sampled.Held & ~previousHeld;
                    sampled.Released = previousHeld & ~sampled.Held;
                    previousHeld = sampled.Held;
                    received = sampled;
                    transport.Send(sampled);
                    localFramesSent++;
                    if (!localFrameSentReported)
                    {
                        localFrameSentReported = true;
                        Plugin.Log.LogMessage("[InputSync] El invitado comenzó a enviar frames " +
                            "(H=" + sampled.Horizontal + " V=" + sampled.Vertical +
                            " botones=" + (uint)sampled.Held + ").");
                    }
                    if (!localInputReported && HasInput(sampled))
                    {
                        localInputReported = true;
                        Plugin.Log.LogMessage("[InputSync] El invitado envía H=" +
                            sampled.Horizontal + " V=" + sampled.Vertical + " botones=" +
                            (uint)sampled.Held + ".");
                    }
                    else if (!localInputReported && !neutralInputWarningReported &&
                        localFramesSent >= 300)
                    {
                        neutralInputWarningReported = true;
                        Plugin.Log.LogWarning("[InputSync] El invitado lleva 300 frames sin " +
                            "detectar ejes ni botones. Revisa el foco de la VM y el dispositivo.");
                    }
                }
            }
            else if (!IsHost)
            {
                var sampled = SampleLabKeyboardInput();
                sampled.Tick = sourceTick;
                sampled.Pressed = sampled.Held & ~previousHeld;
                sampled.Released = previousHeld & ~sampled.Held;
                previousHeld = sampled.Held;
                transport.Send(sampled);
            }

            if (!IsClient)
            {
                InputFrame delivered;
                var deliveredAny = false;
                var pressed = InputButtons.None;
                var released = InputButtons.None;
                while (transport.TryReceive(sourceTick, out delivered))
                {
                    pressed |= delivered.Pressed;
                    released |= delivered.Released;
                    received = delivered;
                    deliveredAny = true;
                }
                if (deliveredAny)
                {
                    received.Pressed = pressed;
                    received.Released = released;
                    if (!remoteFrameReceivedReported)
                    {
                        remoteFrameReceivedReported = true;
                        Plugin.Log.LogMessage("[InputSync] El host comenzó a recibir frames " +
                            "(H=" + received.Horizontal + " V=" + received.Vertical +
                            " botones=" + (uint)received.Held + ").");
                    }
                    if (!remoteInputReported && HasInput(received))
                    {
                        remoteInputReported = true;
                        Plugin.Log.LogMessage("[InputSync] El host recibió H=" +
                            received.Horizontal + " V=" + received.Vertical + " botones=" +
                            (uint)received.Held + ".");
                    }
                    lastRemoteInputRealtime = Time.realtimeSinceStartup;
                    hasRemoteInputActivity = true;
                    var clientWaiting = (received.Flags &
                        InputFrameFlags.WaitingForHost) != 0;
                    if (clientWaiting && !remoteClientWaiting && IsHost)
                        BeginHostResumeCountdown("El invitado espera al anfitrión.");
                    remoteClientWaiting = clientWaiting;
                    if ((received.Flags & InputFrameFlags.LevelReady) != 0 &&
                        LevelLoadGate.OnGuestReady())
                        CaptureAndSendContext(true);
                }
            }

            UpdateSessionHold();

        }

        private static void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                RestoreClientContext();
                ResetSessionHoldState(true);
            }
            Enabled = enabled;
            previousHeld = InputButtons.None;
            received = new InputFrame();
            sourceTick = 0;
            transport.Reset();
            playerTwoReported = false;
            rewiredReadReported = false;
            lastTransportStatus = null;
            hasRemoteContext = false;
            hasPendingRemoteScene = false;
            ResetRemotePlayerState();
            hasLastSentContext = false;
            transportWasConnected = false;
            lateSpawnFailureReported = false;
            lateSpawnAttempted = false;
            ResetLateSpawnRecovery();
            localFrameSentReported = false;
            remoteFrameReceivedReported = false;
            neutralInputWarningReported = false;
            localFramesSent = 0;
            localInputReported = false;
            remoteInputReported = false;
            playerStateSentReported = false;
            playerStateReceivedReported = false;
            hostMapAxesReported = false;
            remoteMapAxesReported = false;
            playerOneVisualReported = false;
            lastRemoteInputRealtime = 0f;
            hasRemoteInputActivity = false;
            remoteClientWaiting = false;
            clientHoldAcknowledgedByHost = false;
            if (enabled)
                ResetSessionHoldState(false);
            if (enabled && IsClient)
                CaptureOriginalClientContext();
            Plugin.Log.LogMessage("Remote Input Lab " + (Enabled ? "ACTIVADO" : "DESACTIVADO") +
                (Enabled ? " (" + transport.Description + ")." : "."));
        }

        private static void UpdateSessionHold()
        {
            if (!Enabled)
                return;

            if (sessionHoldState != SessionHoldState.None)
                ApplySessionPause();

            var now = Time.realtimeSinceStartup;
            if (IsHost)
            {
                if (transport.IsConnected && hasRemoteInputActivity &&
                    sessionHoldState == SessionHoldState.None &&
                    now - lastRemoteInputRealtime >= PeerStallSeconds)
                {
                    EnterSessionWait("Esperando al invitado.");
                    CaptureAndSendContext(true);
                }
                else if (transport.IsConnected && hasRemoteInputActivity &&
                    sessionHoldState == SessionHoldState.Waiting &&
                    now - lastRemoteInputRealtime < 0.25f)
                {
                    BeginHostResumeCountdown("El invitado volvió.");
                }

                if (sessionHoldState == SessionHoldState.Resuming)
                    UpdateHostResumeCountdown(now);
                return;
            }

            if (!IsClient || sessionHoldState != SessionHoldState.None ||
                !hasRemotePlayerState)
                return;
            if (now - lastRemotePlayerStateRealtime >= PeerStallSeconds)
                EnterSessionWait("Esperando al anfitrión.");
        }

        private static void EnterSessionWait(string reason)
        {
            if (sessionHoldState == SessionHoldState.Waiting)
            {
                if (!string.IsNullOrEmpty(reason))
                    sessionHoldReason = reason;
                return;
            }

            sessionHoldState = SessionHoldState.Waiting;
            sessionHoldReason = string.IsNullOrEmpty(reason) ?
                "Esperando al otro jugador." : reason;
            sessionHoldStartedRealtime = Time.realtimeSinceStartup;
            resumeDeadlineRealtime = 0f;
            lastResumeSecondsSent = byte.MaxValue;
            received = new InputFrame();
            ApplySessionPause();
            Plugin.Log.LogWarning("[SessionHold] " + sessionHoldReason);
        }

        private static void BeginHostResumeCountdown(string reason)
        {
            if (!IsHostSession || !transport.IsConnected ||
                sessionHoldState == SessionHoldState.Resuming)
                return;
            if (sessionHoldState == SessionHoldState.None)
                EnterSessionWait(reason);

            sessionHoldState = SessionHoldState.Resuming;
            sessionHoldReason = "El compañero volvió. Reanudando la partida.";
            resumeDeadlineRealtime = Time.realtimeSinceStartup + ResumeCountdownSeconds;
            lastResumeSecondsSent = byte.MaxValue;
            received = new InputFrame();
            ApplySessionPause();
            CaptureAndSendContext(true);
            Plugin.Log.LogMessage("[SessionHold] Ambos jugadores listos; cuenta regresiva iniciada.");
        }

        private static void UpdateHostResumeCountdown(float now)
        {
            var remaining = Mathf.Max(0, Mathf.CeilToInt(resumeDeadlineRealtime - now));
            var encoded = (byte)Mathf.Clamp(remaining, 0, 255);
            if (encoded != lastResumeSecondsSent)
            {
                lastResumeSecondsSent = encoded;
                CaptureAndSendContext(true);
            }
            if (now < resumeDeadlineRealtime)
                return;

            sessionHoldState = SessionHoldState.None;
            sessionHoldReason = string.Empty;
            received = new InputFrame();
            ReleaseSessionPause();
            CaptureAndSendContext(true);
            Plugin.Log.LogMessage("[SessionHold] Partida reanudada.");
        }

        private static void ApplyClientSessionHold(SessionContext context)
        {
            if (!IsClient)
                return;

            if (context.SessionResuming)
            {
                clientHoldAcknowledgedByHost = true;
                if (sessionHoldState == SessionHoldState.None)
                    EnterSessionWait("Esperando al anfitrión.");
                sessionHoldState = SessionHoldState.Resuming;
                sessionHoldReason = "El anfitrión volvió. Reanudando la partida.";
                resumeDeadlineRealtime = Time.realtimeSinceStartup +
                    Mathf.Max(1, context.ResumeSeconds);
                ApplySessionPause();
                return;
            }

            if (context.SessionSuspended)
            {
                clientHoldAcknowledgedByHost = true;
                EnterSessionWait("Esperando al anfitrión.");
                return;
            }

            if (sessionHoldState == SessionHoldState.None ||
                !clientHoldAcknowledgedByHost)
                return;
            sessionHoldState = SessionHoldState.None;
            sessionHoldReason = string.Empty;
            clientHoldAcknowledgedByHost = false;
            received = new InputFrame();
            previousHeld = InputButtons.None;
            ReleaseSessionPause();
            Plugin.Log.LogMessage("[SessionHold] El invitado reanudó la partida.");
        }

        private static void ApplySessionPause()
        {
            try
            {
                if (PauseManager.state != PauseManager.State.Paused)
                {
                    PauseManager.Pause();
                    pauseAppliedBySession = true;
                }
                if (PauseManager.state == PauseManager.State.Paused)
                    return;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[SessionHold] No se pudo pausar Cuphead: " + ex.Message);
            }

            if (!timeScalePausedBySession && Time.timeScale != 0f)
            {
                previousSessionTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                timeScalePausedBySession = true;
            }
        }

        private static void ReleaseSessionPause()
        {
            if (sessionHoldState != SessionHoldState.None || levelGatePauseRequested)
                return;
            if (!pauseAppliedBySession)
            {
                if (timeScalePausedBySession)
                {
                    Time.timeScale = previousSessionTimeScale;
                    timeScalePausedBySession = false;
                }
                return;
            }

            pauseAppliedBySession = false;
            try
            {
                if (PauseManager.state == PauseManager.State.Paused)
                    PauseManager.Unpause();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[SessionHold] No se pudo reanudar Cuphead: " + ex.Message);
            }
            if (timeScalePausedBySession)
            {
                Time.timeScale = previousSessionTimeScale;
                timeScalePausedBySession = false;
            }
        }

        public static void SetLevelGatePause(bool paused)
        {
            levelGatePauseRequested = paused;
            if (paused)
                ApplySessionPause();
            else
                ReleaseSessionPause();
        }

        private static void ResetSessionHoldState(bool releasePause)
        {
            sessionHoldState = SessionHoldState.None;
            sessionHoldReason = string.Empty;
            sessionHoldStartedRealtime = 0f;
            resumeDeadlineRealtime = 0f;
            lastResumeSecondsSent = byte.MaxValue;
            clientHoldAcknowledgedByHost = false;
            remoteClientWaiting = false;
            if (releasePause)
                ReleaseSessionPause();
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

        private static void EnsurePlayerTwoPresent()
        {
            if (!Enabled || !transport.IsConnected)
                return;

            RecoverLateSpawnedMapPlayer();
            if (lateSpawnAttempted || sourceTick % 10 != 0)
                return;

            try
            {
                object target = null;
                System.Reflection.MethodInfo method = null;
                if (Map.Current != null)
                {
                    var players = Map.Current.players;
                    if (players == null || players.Length < 2 || players[0] == null ||
                        players[1] != null ||
                        LevelNewPlayerGUI.Current == null)
                        return;
                    if (!IsMapReadyForLateSpawn(players))
                        return;
                    target = Map.Current;
                    method = MapPlayerJoinedMethod;
                }
                else if (Level.Current != null)
                {
                    var playerOne = PlayerManager.GetPlayer(PlayerId.PlayerOne);
                    if (playerOne == null || PlayerManager.GetPlayer(PlayerId.PlayerTwo) != null)
                        return;
                    var players = LevelPlayersField == null ? null :
                        LevelPlayersField.GetValue(Level.Current) as AbstractPlayerController[];
                    var allowsMultiplayer = LevelAllowMultiplayerField != null &&
                        (bool)LevelAllowMultiplayerField.GetValue(Level.Current);
                    if (!Level.Current.PlayersCreated || !allowsMultiplayer || players == null ||
                        players.Length < 2 || players[0] == null || LevelNewPlayerGUI.Current == null)
                        return;
                    target = Level.Current;
                    method = LevelPlayerJoinedMethod;
                }

                if (target == null || method == null)
                    return;

                lateSpawnAttempted = true;
                method.Invoke(target, new object[] { PlayerId.PlayerTwo });
                if (HasPlayerTwoActor())
                {
                    lateSpawnFailureReported = false;
                    if (Map.Current != null)
                    {
                        var mapPlayers = Map.Current.players;
                        if (mapPlayers != null && mapPlayers.Length > 1)
                        {
                            lateSpawnedMapPlayer = mapPlayers[1];
                            lateSpawnedMapPlayerRealtime = Time.realtimeSinceStartup;
                        }
                    }
                    Plugin.Log.LogMessage("[CoopSpawn] Player Two creado mediante el flujo cooperativo de Cuphead.");
                }
                else
                {
                    lateSpawnAttempted = false;
                    if (!lateSpawnFailureReported)
                    {
                        lateSpawnFailureReported = true;
                        Plugin.Log.LogWarning("[CoopSpawn] Cuphead rechazó la creación de Player Two en esta escena.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                lateSpawnAttempted = false;
                if (lateSpawnFailureReported)
                    return;
                lateSpawnFailureReported = true;
                var inner = ex.InnerException == null ? ex : ex.InnerException;
                Plugin.Log.LogWarning("[CoopSpawn] El mapa aún no pudo crear Player Two: " + inner.Message);
            }
        }

        private static bool IsMapReadyForLateSpawn(MapPlayerController[] players)
        {
            return Map.Current != null && Map.Current.CurrentState == Map.State.Ready &&
                SceneLoader.Exists && !SceneLoader.CurrentlyLoading &&
                !SceneLoader.IsInIrisTransition && !SceneLoader.IsInBlurTransition &&
                players != null && players.Length > 0 && players[0] != null &&
                players[0].state != MapPlayerController.State.Stationary;
        }

        private static void RecoverLateSpawnedMapPlayer()
        {
            var player = lateSpawnedMapPlayer;
            if (player == null)
                return;
            if (player.state != MapPlayerController.State.Stationary)
            {
                ResetLateSpawnRecovery();
                return;
            }
            if (Time.realtimeSinceStartup - lateSpawnedMapPlayerRealtime < 1f)
                return;
            var players = Map.Current == null ? null : Map.Current.players;
            if (!IsMapReadyForLateSpawn(players))
                return;

            try
            {
                if (MapPlayerJumpCompleteMethod != null)
                    MapPlayerJumpCompleteMethod.Invoke(player, null);
                if (player.state == MapPlayerController.State.Stationary)
                    player.Enable();
                Plugin.Log.LogMessage("[CoopSpawn] Player Two recuperado después de la carga tardía.");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[CoopSpawn] No se pudo recuperar Player Two: " +
                    ex.Message);
            }
            ResetLateSpawnRecovery();
        }

        private static void ResetLateSpawnRecovery()
        {
            lateSpawnedMapPlayer = null;
            lateSpawnedMapPlayerRealtime = 0f;
        }

        public static bool ShouldOverridePlayerTwo(Player player)
        {
            if (!DrivesPlayerTwo || samplingLocalInput || player == null)
                return false;

            try
            {
                return object.ReferenceEquals(player, PlayerManager.GetPlayerInput(PlayerId.PlayerTwo));
            }
            catch
            {
                return player.id == 1;
            }
        }

        public static bool ShouldSuppressPlayerOne(Player player)
        {
            if (!IsClientSession || samplingLocalInput || player == null)
                return false;

            try
            {
                if (!HasPlayerTwoActor())
                    return false;
                return object.ReferenceEquals(player, PlayerManager.GetPlayerInput(PlayerId.PlayerOne));
            }
            catch
            {
                return false;
            }
        }

        public static bool ShouldOverridePlayerOneVisual(Player player)
        {
            if (!CanDriveRemotePlayerOneMapVisual() || player == null)
                return false;

            try
            {
                return object.ReferenceEquals(player,
                    PlayerManager.GetPlayerInput(PlayerId.PlayerOne));
            }
            catch
            {
                return player.id == 0;
            }
        }

        public static float GetRemotePlayerOneMapAxis(int actionId)
        {
            if (!CanDriveRemotePlayerOneMapVisual())
                return 0f;
            if (!playerOneVisualReported)
            {
                playerOneVisualReported = true;
                Plugin.Log.LogMessage("[StateSync] Player One remoto activó su motor visual " +
                    "(H=" + latestRemotePlayerState.PlayerOneMapHorizontal + " V=" +
                    latestRemotePlayerState.PlayerOneMapVertical + ").");
            }
            if (actionId == 0)
                return latestRemotePlayerState.PlayerOneMapHorizontal / 127f;
            if (actionId == 1)
                return latestRemotePlayerState.PlayerOneMapVertical / 127f;
            return 0f;
        }

        public static bool TryGetPlayerInputAxes(PlayerInput input, out float horizontal,
            out float vertical)
        {
            horizontal = 0f;
            vertical = 0f;
            if (input == null || samplingLocalInput)
                return false;

            if (input.playerId == PlayerId.PlayerTwo && DrivesPlayerTwo)
            {
                horizontal = received.Horizontal / 127f;
                vertical = received.Vertical / 127f;
                ReportRewiredRead();
                return true;
            }

            if (input.playerId != PlayerId.PlayerOne || !CanDriveRemotePlayerOneMapVisual())
                return false;
            horizontal = latestRemotePlayerState.PlayerOneMapHorizontal / 127f;
            vertical = latestRemotePlayerState.PlayerOneMapVertical / 127f;
            return true;
        }

        public static bool TryGetPlayerInputButton(PlayerInput input, CupheadButton button,
            out bool value)
        {
            value = false;
            if (input == null || samplingLocalInput)
                return false;
            if (input.playerId == PlayerId.PlayerTwo && DrivesPlayerTwo)
            {
                value = GetButton((int)button, ButtonPhase.Held);
                ReportRewiredRead();
                return true;
            }
            return input.playerId == PlayerId.PlayerOne &&
                CanDriveRemotePlayerOneMapVisual();
        }

        private static bool CanDriveRemotePlayerOneMapVisual()
        {
            return IsClientSession && transport.IsConnected && !samplingLocalInput &&
                Map.Current != null && HasPlayerTwoActor() && hasRemotePlayerState &&
                (latestRemotePlayerState.PresentMask & 1) != 0 &&
                Time.realtimeSinceStartup - lastRemotePlayerStateRealtime <= 0.5f;
        }

        private static void ProcessConnectionTransition()
        {
            var connected = transport.IsConnected;
            if (connected && !transportWasConnected)
            {
                lateSpawnAttempted = false;
                lateSpawnFailureReported = false;
                Plugin.Log.LogMessage("[SessionSync] Ambos jugadores conectados.");
                if (IsHost)
                {
                    CaptureAndSendContext(true);
                    SendCurrentScene();
                }
            }
            else if (!connected && transportWasConnected)
            {
                if (IsClient)
                    EnterSessionWait("Se perdió la señal del anfitrión. Reconectando.");
                else if (IsHost)
                    EnterSessionWait("Se perdió la señal del invitado. Reconectando.");
                lateSpawnAttempted = false;
                ResetLateSpawnRecovery();
                received = new InputFrame();
                previousHeld = InputButtons.None;
                ResetRemotePlayerState();
                hasRemoteContext = false;
                hasPendingRemoteScene = false;
                hasRemoteInputActivity = false;
            }
            transportWasConnected = connected;
        }

        private static void SendCurrentScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!IsStableScene(scene.name, LoadSceneMode.Single))
                return;
            transport.SendScene(new SceneCommand
            {
                SceneName = scene.name,
                LoadMode = (byte)LoadSceneMode.Single,
            });
            Plugin.Log.LogInfo("[SceneSync] Escena encolada: " + scene.name);
        }

        public static void OnSceneLoaded(string sceneName, LoadSceneMode mode)
        {
            LevelLoadGate.OnSceneLoaded(sceneName);
            ClearRemotePlayerState();
            lateSpawnAttempted = false;
            ResetLateSpawnRecovery();
            lateSpawnFailureReported = false;
            if (!Enabled || !IsHost || !IsStableScene(sceneName, mode))
                return;

            CaptureAndSendContext(true);
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
                pendingRemoteScene = command;
                hasPendingRemoteScene = true;
                if (RequiresSessionContext(command.SceneName) && !hasRemoteContext)
                    Plugin.Log.LogMessage("[SceneSync] Esperando el save del host antes de cargar " +
                        command.SceneName + ".");
            }

            if (!hasPendingRemoteScene)
                return;
            if (SceneManager.GetActiveScene().name == pendingRemoteScene.SceneName)
            {
                Plugin.Log.LogMessage("[SceneSync] Escena remota sincronizada: " +
                    pendingRemoteScene.SceneName + ".");
                hasPendingRemoteScene = false;
                return;
            }
            if (RequiresSessionContext(pendingRemoteScene.SceneName) && !hasRemoteContext)
                return;
            if (!SceneLoader.Exists || SceneLoader.CurrentlyLoading)
                return;

            ApplySceneCommand(pendingRemoteScene);
        }

        private static void ApplySceneCommand(SceneCommand command)
        {
            if (SceneManager.GetActiveScene().name == command.SceneName)
                return;
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

        private static bool RequiresSessionContext(string sceneName)
        {
            return sceneName != "scene_title" && sceneName != "scene_slot_select";
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

        private static void CaptureAndSendContext(bool force)
        {
            try
            {
                var data = PlayerData.Data;
                var hasActiveSave = data != null && PlayerData.inGame;
                var context = new SessionContext
                {
                    SaveSlot = (byte)UnityEngine.Mathf.Clamp(PlayerData.CurrentSaveFileIndex, 0, 2),
                    Flags = (byte)((hasActiveSave ? 1 : 0) |
                        (PlayerManager.player1IsMugman ? 2 : 0) |
                        (Level.Current != null ? 4 : 0) |
                        (sessionHoldState != SessionHoldState.None ? 8 : 0) |
                        (sessionHoldState == SessionHoldState.Resuming ? 16 : 0) |
                        (LevelLoadGate.ReleaseAnnounced ? 32 : 0)),
                    Difficulty = (byte)Level.CurrentMode,
                    ResumeSeconds = sessionHoldState == SessionHoldState.Resuming ?
                        (byte)Mathf.Clamp(SessionResumeSeconds, 0, 255) : (byte)0,
                    CurrentMap = hasActiveSave ? (int)data.CurrentMap : -1,
                    CurrentLevel = Level.Current == null ? -1 : (int)Level.Current.CurrentLevel,
                };

                if (!force && hasLastSentContext && ContextEquals(context, lastSentContext) &&
                    sourceTick % 300 != 0)
                    return;
                lastSentContext = context;
                hasLastSentContext = true;
                transport.SendContext(context);
                Plugin.Log.LogInfo("[SessionSync] Contexto enviado: slot=" + context.SaveSlot +
                    " mugman=" + context.PlayerOneIsMugman + " difficulty=" + context.Difficulty +
                    " map=" + context.CurrentMap + " level=" + context.CurrentLevel +
                    " suspended=" + context.SessionSuspended + " resume=" +
                    context.ResumeSeconds);
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
            if (!originalClientContextCaptured)
                CaptureOriginalClientContext();

            SessionContext context;
            while (transport.TryReceiveContext(out context))
            {
                Plugin.Log.LogMessage("[SessionSync] Contexto recibido #" + context.Sequence +
                    ": slot=" + context.SaveSlot + " mugman=" + context.PlayerOneIsMugman +
                    " difficulty=" + context.Difficulty + " map=" + context.CurrentMap +
                    " level=" + context.CurrentLevel + " suspended=" +
                    context.SessionSuspended + " resume=" + context.ResumeSeconds);
                if (context.SaveSlot > 2 || context.Difficulty > 2)
                    continue;
                ApplyClientSessionHold(context);
                if (context.LevelGateReleased)
                    LevelLoadGate.OnHostRelease(context.CurrentLevel);
                if (!context.HasSave)
                {
                    hasRemoteContext = false;
                    PlayerData.inGame = false;
                    continue;
                }
                try
                {
                    var firstSessionContext = !hasRemoteContext;
                    var data = PlayerData.GetDataForSlot(context.SaveSlot);
                    PlayerData.CurrentSaveFileIndex = context.SaveSlot;
                    PlayerManager.player1IsMugman = context.PlayerOneIsMugman;
                    data.isPlayer1Mugman = context.PlayerOneIsMugman;
                    Level.SetCurrentMode((Level.Mode)context.Difficulty);
                    if (context.CurrentMap >= 0 &&
                        System.Enum.IsDefined(typeof(Scenes), context.CurrentMap))
                        data.CurrentMap = (Scenes)context.CurrentMap;
                    if (firstSessionContext)
                    {
                        DLCManager.RefreshDLC();
                        Level.ResetPreviousLevelInfo();
                    }
                    PlayerData.inGame = true;
                    latestRemoteContext = context;
                    hasRemoteContext = true;
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("[SessionSync] No se pudo aplicar contexto: " + ex.Message);
                }
            }
        }

        private static void CaptureOriginalClientContext()
        {
            if (originalClientContextCaptured)
                return;
            try
            {
                originalClientSaveSlot = PlayerData.CurrentSaveFileIndex;
                originalClientPlayerOneIsMugman = PlayerManager.player1IsMugman;
                var data = PlayerData.GetDataForSlot(originalClientSaveSlot);
                originalClientSavePlayerOneIsMugman = data.isPlayer1Mugman;
                originalClientDifficulty = Level.CurrentMode;
                originalClientInGame = PlayerData.inGame;
                originalClientContextCaptured = true;
            }
            catch
            {
                // El frontend todavía puede estar inicializando PlayerData.
            }
        }

        private static void RestoreClientContext()
        {
            if (!originalClientContextCaptured)
                return;
            try
            {
                PlayerData.CurrentSaveFileIndex = originalClientSaveSlot;
                PlayerManager.player1IsMugman = originalClientPlayerOneIsMugman;
                PlayerData.GetDataForSlot(originalClientSaveSlot).isPlayer1Mugman =
                    originalClientSavePlayerOneIsMugman;
                Level.SetCurrentMode(originalClientDifficulty);
                PlayerData.inGame = originalClientInGame;
            }
            catch
            {
                // El juego puede estar cerrándose y haber destruido sus singletons.
            }
            originalClientContextCaptured = false;
        }

        private static void TryLeavePlayerTwo()
        {
            try
            {
                if (Map.Current != null)
                {
                    var players = Map.Current.players;
                    if (players != null && players.Length > 1 && players[1] != null)
                    {
                        if (MapPlayerLeaveMethod != null)
                            MapPlayerLeaveMethod.Invoke(Map.Current,
                                new object[] { PlayerId.PlayerTwo });
                        else
                            UnityEngine.Object.Destroy(players[1].gameObject);
                        players[1] = null;
                    }
                    return;
                }
                if (PlayerManager.GetPlayer(PlayerId.PlayerTwo) != null)
                    PlayerManager.PlayerLeave(PlayerId.PlayerTwo);
            }
            catch
            {
                // No hay PlayerManager activo en el frontend o la escena se está cerrando.
            }
        }

        private static void RestoreVanillaPlayerManagerState()
        {
            try
            {
                PlayerManager.Multiplayer = false;
                PlayerManager.SetPlayerCanJoin(PlayerId.PlayerTwo, true, true);
                PlayerManager.SetPlayerCanSwitch(PlayerId.PlayerOne, true);
                PlayerManager.SetPlayerCanSwitch(PlayerId.PlayerTwo, true);
            }
            catch
            {
                // PlayerManager todavía no existe o ya fue destruido.
            }
        }

        private static bool ContextEquals(SessionContext left, SessionContext right)
        {
            return left.SaveSlot == right.SaveSlot && left.Flags == right.Flags &&
                left.Difficulty == right.Difficulty &&
                left.ResumeSeconds == right.ResumeSeconds &&
                left.CurrentMap == right.CurrentMap &&
                left.CurrentLevel == right.CurrentLevel;
        }

        private static void CaptureAndSendPlayerState()
        {
            if (!transport.IsConnected)
                return;

            var state = new PlayerStateSnapshot { Tick = sourceTick };
            if (Map.Current != null)
            {
                CaptureMapPlayer(PlayerId.PlayerOne, 1, ref state);
                CaptureMapPlayer(PlayerId.PlayerTwo, 2, ref state);
            }
            else
            {
                CaptureLevelPlayer(PlayerId.PlayerOne, 1, ref state);
                CaptureLevelPlayer(PlayerId.PlayerTwo, 2, ref state);
            }
            if (state.PresentMask != 0)
            {
                transport.SendPlayerState(state);
                if (!playerStateSentReported)
                {
                    playerStateSentReported = true;
                    Plugin.Log.LogMessage("[StateSync] El host envió su primer snapshot (jugadores=" +
                        state.PresentMask + ").");
                }
            }
        }

        private static void CaptureMapPlayer(PlayerId id, byte mask,
            ref PlayerStateSnapshot state)
        {
            var players = Map.Current == null ? null : Map.Current.players;
            var index = (int)id;
            if (players == null || index < 0 || index >= players.Length || players[index] == null)
                return;
            state.PresentMask |= mask;
            var position = players[index].transform.position;
            SetSnapshotPosition(id, position, 0, ref state);
            if (id == PlayerId.PlayerOne)
            {
                try
                {
                    var input = PlayerManager.GetPlayerInput(PlayerId.PlayerOne);
                    if (input != null)
                    {
                        state.PlayerOneMapHorizontal = QuantizeAxis(input.GetAxisRaw(0));
                        state.PlayerOneMapVertical = QuantizeAxis(input.GetAxisRaw(1));
                        if (!hostMapAxesReported &&
                            (state.PlayerOneMapHorizontal != 0 ||
                            state.PlayerOneMapVertical != 0))
                        {
                            hostMapAxesReported = true;
                            Plugin.Log.LogMessage("[StateSync] El host transmite movimiento " +
                                "del mapa H=" + state.PlayerOneMapHorizontal + " V=" +
                                state.PlayerOneMapVertical + ".");
                        }
                    }
                }
                catch
                {
                    // El snapshot de posición sigue siendo válido aunque falte Rewired.
                }
            }
        }

        private static void CaptureLevelPlayer(PlayerId id, byte mask,
            ref PlayerStateSnapshot state)
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
            SetSnapshotPosition(id, position, health, ref state);
        }

        private static void SetSnapshotPosition(PlayerId id, Vector3 position, int health,
            ref PlayerStateSnapshot state)
        {
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
            if (!IsClient || !transport.IsConnected)
                return;
            PlayerStateSnapshot state;
            while (transport.TryReceivePlayerState(out state))
            {
                if (hasRemotePlayerStateTick &&
                    !IsNewerTick(state.Tick, lastRemotePlayerStateTick))
                    continue;
                latestRemotePlayerState = state;
                hasRemotePlayerState = true;
                remotePlayerStatePendingApply = true;
                lastRemotePlayerStateRealtime = Time.realtimeSinceStartup;
                lastRemotePlayerStateTick = state.Tick;
                hasRemotePlayerStateTick = true;
                if (!playerStateReceivedReported)
                {
                    playerStateReceivedReported = true;
                    Plugin.Log.LogMessage("[StateSync] El invitado recibió su primer snapshot (jugadores=" +
                        state.PresentMask + ", H=" + state.PlayerOneMapHorizontal + " V=" +
                        state.PlayerOneMapVertical + ").");
                }
                if (!remoteMapAxesReported &&
                    (state.PlayerOneMapHorizontal != 0 || state.PlayerOneMapVertical != 0))
                {
                    remoteMapAxesReported = true;
                    Plugin.Log.LogMessage("[StateSync] El invitado recibió movimiento del host " +
                        "H=" + state.PlayerOneMapHorizontal + " V=" +
                        state.PlayerOneMapVertical + ".");
                }
            }
        }

        private static void ApplyRemotePlayerState()
        {
            if (!IsClient || !transport.IsConnected || !hasRemotePlayerState ||
                !remotePlayerStatePendingApply)
                return;

            remotePlayerStatePendingApply = false;

            if ((latestRemotePlayerState.PresentMask & 1) != 0)
                CorrectPlayerPosition(PlayerId.PlayerOne,
                    latestRemotePlayerState.PlayerOneX, latestRemotePlayerState.PlayerOneY, true);
            if ((latestRemotePlayerState.PresentMask & 2) != 0)
                CorrectPredictedPlayerTwoPosition(
                    latestRemotePlayerState.PlayerTwoX,
                    latestRemotePlayerState.PlayerTwoY);
        }

        private static void ClearRemotePlayerState()
        {
            latestRemotePlayerState = default(PlayerStateSnapshot);
            hasRemotePlayerState = false;
            remotePlayerStatePendingApply = false;
            lastRemotePlayerStateRealtime = 0f;
        }

        private static void ResetRemotePlayerState()
        {
            ClearRemotePlayerState();
            lastRemotePlayerStateTick = 0;
            hasRemotePlayerStateTick = false;
        }

        private static bool IsNewerTick(uint candidate, uint current)
        {
            return candidate != current && unchecked((int)(candidate - current)) > 0;
        }

        private static void CorrectPlayerPosition(PlayerId id, float x, float y, bool authoritative)
        {
            if (Map.Current != null)
            {
                var players = Map.Current.players;
                var index = (int)id;
                if (players == null || index < 0 || index >= players.Length || players[index] == null)
                    return;
                CorrectMapTransform(players[index].transform, x, y);
                return;
            }

            AbstractPlayerController player;
            try { player = PlayerManager.GetPlayer(id); }
            catch { return; }
            if (player == null)
                return;

            CorrectTransform(player.transform, x, y, authoritative);
        }

        private static void CorrectTransform(Transform playerTransform, float x, float y,
            bool authoritative)
        {
            var current = playerTransform.position;
            var target = new Vector3(x, y, current.z);
            var distance = Vector2.Distance(current, target);
            if (authoritative || distance > 80f)
                playerTransform.position = target;
            else if (distance > 1f)
                playerTransform.position = Vector3.Lerp(current, target, 0.2f);
        }

        private static void CorrectMapTransform(Transform playerTransform, float x, float y)
        {
            var current = playerTransform.position;
            var target = new Vector3(x, y, current.z);
            var distance = Vector2.Distance(current, target);
            if (distance > 4f)
                playerTransform.position = target;
            else if (distance > 0.05f)
                playerTransform.position = Vector3.Lerp(current, target, 0.35f);
        }

        private static void CorrectPredictedPlayerTwoPosition(float x, float y)
        {
            Transform playerTransform;
            var onMap = Map.Current != null;
            if (onMap)
            {
                var players = Map.Current.players;
                if (players == null || players.Length < 2 || players[1] == null)
                    return;
                playerTransform = players[1].transform;
            }
            else
            {
                AbstractPlayerController player;
                try { player = PlayerManager.GetPlayer(PlayerId.PlayerTwo); }
                catch { return; }
                if (player == null)
                    return;
                playerTransform = player.transform;
            }

            var current = playerTransform.position;
            var target = new Vector3(x, y, current.z);
            var distance = Vector2.Distance(current, target);
            var activelyPredicting = received.Horizontal != 0 || received.Vertical != 0 ||
                received.Held != InputButtons.None;
            var snapDistance = onMap ? 12f : 80f;
            var idleCorrectionDistance = onMap ? 1f : 3f;

            // Mientras el invitado controla a P2, una corrección suave basada en un
            // snapshot atrasado lo arrastra hacia atrás y reduce su velocidad aparente.
            // Solo aceptamos un salto autoritativo grande; al quedar neutral convergemos.
            if (distance >= snapDistance)
                playerTransform.position = target;
            else if (!activelyPredicting && distance >= idleCorrectionDistance)
                playerTransform.position = Vector3.Lerp(current, target, 0.15f);
        }

        private static void ReportPlayerTwoWhenReady()
        {
            if (playerTwoReported)
                return;

            try
            {
                if (!HasPlayerTwoActor())
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

        private static InputFrame SampleConfiguredPlayerInput()
        {
            samplingLocalInput = true;
            try
            {
                var sampled = new InputFrame();
                MergeConfiguredPlayer(PlayerId.PlayerOne, ref sampled);
                MergeConfiguredPlayer(PlayerId.PlayerTwo, ref sampled);
                MergeInput(ref sampled, SampleUnityFallbackInput());
                return sampled;
            }
            finally
            {
                samplingLocalInput = false;
            }
        }

        private static void MergeConfiguredPlayer(PlayerId id, ref InputFrame sampled)
        {
            try
            {
                var player = PlayerManager.GetPlayerInput(id);
                if (player == null)
                    return;
                MergeInput(ref sampled, new InputFrame
                {
                    Horizontal = QuantizeAxis(player.GetAxisRaw(0)),
                    Vertical = QuantizeAxis(player.GetAxisRaw(1)),
                    Held = ReadConfiguredButtons(player),
                });
            }
            catch
            {
                // El perfil puede no existir todavía durante el cambio de escena.
            }
        }

        private static void MergeInput(ref InputFrame target, InputFrame candidate)
        {
            if (Mathf.Abs(candidate.Horizontal) > Mathf.Abs(target.Horizontal))
                target.Horizontal = candidate.Horizontal;
            if (Mathf.Abs(candidate.Vertical) > Mathf.Abs(target.Vertical))
                target.Vertical = candidate.Vertical;
            target.Held |= candidate.Held;
        }

        private static InputFrame SampleUnityFallbackInput()
        {
            var sampled = SampleLabKeyboardInput();
            var arrows = new InputFrame
            {
                Horizontal = ReadAxis(KeyCode.LeftArrow, KeyCode.RightArrow),
                Vertical = ReadAxis(KeyCode.DownArrow, KeyCode.UpArrow),
            };
            var wasd = new InputFrame
            {
                Horizontal = ReadAxis(KeyCode.A, KeyCode.D),
                Vertical = ReadAxis(KeyCode.S, KeyCode.W),
            };
            MergeInput(ref sampled, arrows);
            MergeInput(ref sampled, wasd);
            if (Input.GetKey(KeyCode.Escape))
                sampled.Held |= InputButtons.Pause | InputButtons.Cancel;
            try
            {
                MergeInput(ref sampled, new InputFrame
                {
                    Horizontal = QuantizeAxis(Input.GetAxisRaw("Horizontal")),
                    Vertical = QuantizeAxis(Input.GetAxisRaw("Vertical")),
                });
            }
            catch
            {
                // Algunas instalaciones no exponen los ejes clásicos de Unity.
            }
            return sampled;
        }

        private static InputFrame SampleLabKeyboardInput()
        {
            return new InputFrame
            {
                Horizontal = ReadAxis(KeyCode.Keypad4, KeyCode.Keypad6),
                Vertical = ReadAxis(KeyCode.Keypad2, KeyCode.Keypad8),
                Held = ReadLabButtons(),
            };
        }

        private static bool HasPlayerTwoActor()
        {
            if (Map.Current != null)
            {
                var players = Map.Current.players;
                return players != null && players.Length > 1 && players[1] != null;
            }

            try { return PlayerManager.GetPlayer(PlayerId.PlayerTwo) != null; }
            catch { return false; }
        }

        private static bool HasInput(InputFrame frame)
        {
            return frame.Horizontal != 0 || frame.Vertical != 0 ||
                frame.Held != InputButtons.None || frame.Pressed != InputButtons.None ||
                frame.Released != InputButtons.None;
        }

        private static sbyte QuantizeAxis(float value)
        {
            return (sbyte)Mathf.Clamp(Mathf.RoundToInt(value * 127f), -127, 127);
        }

        private static InputButtons ReadConfiguredButtons(Player player)
        {
            var buttons = InputButtons.None;
            AddIfHeld(ref buttons, player, 2, InputButtons.Jump);
            AddIfHeld(ref buttons, player, 3, InputButtons.Shoot);
            AddIfHeld(ref buttons, player, 4, InputButtons.Super);
            AddIfHeld(ref buttons, player, 5, InputButtons.SwitchWeapon);
            AddIfHeld(ref buttons, player, 6, InputButtons.Lock);
            AddIfHeld(ref buttons, player, 7, InputButtons.Dash);
            AddIfHeld(ref buttons, player, 8, InputButtons.Pause);
            AddIfHeld(ref buttons, player, 13, InputButtons.Accept);
            AddIfHeld(ref buttons, player, 14, InputButtons.Cancel);
            AddIfHeld(ref buttons, player, 15, InputButtons.EquipMenu);
            AddIfHeld(ref buttons, player, 26, InputButtons.Swap);
            return buttons;
        }

        private static void AddIfHeld(ref InputButtons buttons, Player player,
            int actionId, InputButtons button)
        {
            if (player.GetButton(actionId))
                buttons |= button;
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

        private static InputButtons ReadLabButtons()
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

    internal enum SessionHoldState
    {
        None,
        Waiting,
        Resuming,
    }
}
