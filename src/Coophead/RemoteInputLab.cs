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
        private const uint ModVersionToken = 0x00120B;
        private const float PeerStallSeconds = 1.25f;
        private const float ResumeCountdownSeconds = 3f;
        private const float LongWaitSeconds = 15f;
        private const float BlockedSceneRequestTimeoutSeconds = 5f;
        private const float PendingSceneCommandTimeoutSeconds = 10f;
        private const float PlayerTwoStallProbeSeconds = 0.4f;
        private const float PlayerTwoLocalMovementThreshold = 3f;
        private const float PlayerTwoHostMovementThreshold = 12f;
        private const float RemotePlayerOneSuperDeferralSeconds = 0.35f;
        private const InputButtons FixedGameplayButtons = InputButtons.Jump |
            InputButtons.Shoot | InputButtons.Super | InputButtons.SwitchWeapon |
            InputButtons.Lock | InputButtons.Dash;

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
        private static readonly System.Reflection.FieldInfo MapPlayerMotorVelocityField =
            AccessTools.Field(typeof(MapPlayerMotor), "<velocity>k__BackingField");
        private static readonly System.Reflection.FieldInfo LevelPlayerMotorHitManagerField =
            AccessTools.Field(typeof(LevelPlayerMotor), "hitManager");
        private static readonly System.Reflection.FieldInfo LevelPlayerMotorHitDirectionField =
            LevelPlayerMotorHitManagerField == null ? null :
                AccessTools.Field(LevelPlayerMotorHitManagerField.FieldType,
                    "direction");

        private static IInputFrameTransport transport =
            new LoopbackInputTransport(SimulatedLatencyFrames);
        private static InputTransportMode transportMode = InputTransportMode.Loopback;

        private static InputFrame received;
        private static InputButtons previousHeld;
        private static InputButtons playerTwoPendingPressed;
        private static InputButtons playerTwoPendingReleased;
        private static InputButtons playerTwoFixedPressed;
        private static InputButtons playerTwoFixedReleased;
        private static InputFrame remotePlayerOneInput;
        private static InputButtons remotePlayerOnePendingPressed;
        private static InputButtons remotePlayerOnePendingReleased;
        private static InputButtons remotePlayerOneFixedPressed;
        private static InputButtons remotePlayerOneFixedReleased;
        private static InputFrame hostPlayerOneInput;
        private static InputButtons previousHostPlayerOneHeld;
        private static InputButtons previousRemotePlayerOneHeld;
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
        private static string latestRemotePlayerStateScene = string.Empty;
        private static bool hasRemotePlayerState;
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
        private static bool p2PredictionIsolationReported;
        private static bool playerTwoStallProbeActive;
        private static bool playerTwoHostFallbackActive;
        private static float playerTwoStallProbeStartedRealtime;
        private static Vector2 playerTwoStallProbeLastLocalPosition;
        private static Vector2 playerTwoStallProbeLastHostPosition;
        private static float playerTwoStallProbeLocalDistance;
        private static float playerTwoStallProbeHostDistance;
        private static int playerTwoConsecutiveStallProbes;
        private static bool superButtonReported;
        private static bool lockButtonReported;
        private static float remotePlayerOneSuperDeferralDeadline;
        private static uint hostPlayerOneSuperActionSequence;
        private static uint lastRemotePlayerOneSuperActionSequence;
        private static bool hasRemotePlayerOneSuperActionSequence;
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
        private static bool sceneTransitionActive;
        private static uint sceneTransitionId;
        private static uint currentSceneEpoch;
        private static string sceneTransitionTarget = string.Empty;
        private static float sceneTransitionStartedRealtime;
        private static byte sceneTransitionDifficulty;
        private static bool backgroundSettingRepairReported;
        private static bool applyingHostSceneCommand;
        private static string blockedClientSceneRequest = string.Empty;
        private static float blockedClientSceneRequestRealtime;
        private static bool mapBootstrapCompleted;
        private static bool levelPlayerOneBootstrapCompleted;
        private static bool levelPlayerTwoBootstrapCompleted;
        private static bool remoteSceneLoadStarted;
        private static bool returnToMapAfterAbortedLoad;

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
        public static bool SceneTransitionActive => Enabled && sceneTransitionActive;
        internal static uint CurrentSceneEpoch => currentSceneEpoch;
        public static bool IsClientReadyForLoadGate(string sceneName)
        {
            if (!sceneName.StartsWith("scene_map_"))
                return true;
            if (SceneManager.GetActiveScene().name != sceneName || Map.Current == null ||
                CupheadMapCamera.Current == null)
                return false;
            var players = Map.Current.players;
            if (players == null || players.Length < 2 ||
                players[0] == null || players[1] == null)
                return false;
            return !IsClientSession || mapBootstrapCompleted;
        }
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
            if (!runInBackgroundCaptured)
            {
                originalRunInBackground = Application.runInBackground;
                runInBackgroundCaptured = true;
            }
            runInBackgroundForTesting = enabled;
            Application.runInBackground = enabled;
            backgroundSettingRepairReported = false;
            Plugin.Log.LogInfo("[Testing] RunInBackground temporal: " +
                (enabled ? "ACTIVADO" : "DESACTIVADO") +
                " (Unity=" + Application.runInBackground + ").");
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

            if (runInBackgroundForTesting && !Application.runInBackground)
            {
                Application.runInBackground = true;
                if (!backgroundSettingRepairReported)
                {
                    backgroundSettingRepairReported = true;
                    Plugin.Log.LogWarning("[Testing] RunInBackground fue restaurado a true.");
                }
            }

            TryReturnToMapAfterAbortedLoad();

            if (!Enabled)
                return;

            // Los bordes visibles en Update duran exactamente un frame. Los de
            // gameplay se copian por separado al siguiente FixedUpdate.
            received.Pressed = InputButtons.None;
            received.Released = InputButtons.None;
            remotePlayerOneInput.Pressed = InputButtons.None;
            remotePlayerOneInput.Released = InputButtons.None;

            transport.Update();
            ProcessConnectionTransition();
            if (lastTransportStatus != transport.Status)
            {
                lastTransportStatus = transport.Status;
                Plugin.Log.LogMessage("[InputLab] " + transport.Status);
            }
            ProcessSessionContexts();
            ProcessSceneCommands();
            UpdateBlockedClientSceneRequest();
            UpdatePendingSceneCommandWatchdog();
            ProcessPlayerStates();
            SlimeBossSynchronizer.ProcessIncoming(transport);
            LevelLoadGate.Update();
            CompleteSceneTransitionIfPossible();

            EnsureMultiplayerState();
            EnsurePlayerTwoPresent();
            ReportPlayerTwoWhenReady();
            ApplyRemotePlayerState();

            sourceTick++;
            if (IsHost && sourceTick % 30 == 0)
                CaptureAndSendContext(false);
            if (IsHost)
            {
                CaptureHostPlayerOneInput();
                CaptureAndSendPlayerState();
            }

            if (IsClient)
            {
                if (transport.IsConnected)
                {
                    var sampled = SampleConfiguredPlayerInput();
                    sampled.Tick = sourceTick;
                    var physicalHeld = sampled.Held;
                    var inputLocked = sessionHoldState != SessionHoldState.None ||
                        SceneTransitionActive || LevelLoadGate.IsHoldingGameplay ||
                        IsLevelIntroActive();
                    if (inputLocked)
                    {
                        sampled.Horizontal = 0;
                        sampled.Vertical = 0;
                        sampled.Held = InputButtons.None;
                        sampled.Pressed = InputButtons.None;
                        sampled.Released = InputButtons.None;
                    }
                    if (sessionHoldState != SessionHoldState.None)
                        sampled.Flags |= InputFrameFlags.WaitingForHost;
                    if (LevelLoadGate.ShouldReportReady)
                    {
                        sampled.Flags |= InputFrameFlags.LevelReady;
                        sampled.ReadyTransitionId = LevelLoadGate.TransitionId;
                    }
                    else if (SceneTransitionActive)
                        sampled.Flags |= InputFrameFlags.Loading;
                    if (!inputLocked)
                    {
                        sampled.Pressed |= sampled.Held & ~previousHeld;
                        sampled.Released |= previousHeld & ~sampled.Held;
                    }
                    previousHeld = physicalHeld;
                    received = sampled;
                    QueuePlayerTwoFixedEdges(sampled.Pressed, sampled.Released);
                    ReportPlayerTwoCombatButtons("detectó localmente", sampled);
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
                sampled.Pressed |= sampled.Held & ~previousHeld;
                sampled.Released |= previousHeld & ~sampled.Held;
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
                    QueuePlayerTwoFixedEdges(pressed, released);
                    ReportPlayerTwoCombatButtons("recibió del invitado", received);
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
                    if (!SceneTransitionActive && clientWaiting &&
                        !remoteClientWaiting && IsHost)
                        BeginHostResumeCountdown("El invitado espera al anfitrión.");
                    remoteClientWaiting = clientWaiting;
                    if ((received.Flags & InputFrameFlags.LevelReady) != 0 &&
                        LevelLoadGate.OnGuestReady(
                            received.ReadyTransitionId))
                        CaptureAndSendContext(true);
                }
            }

            UpdateSessionHold();

        }

        public static void LateTick()
        {
            if (!Enabled || !transport.IsConnected)
                return;

            if (IsHost)
            {
                var transitionId = sceneTransitionActive &&
                    SceneManager.GetActiveScene().name == sceneTransitionTarget ?
                    sceneTransitionId : currentSceneEpoch;
                SlimeBossSynchronizer.CaptureAndSend(
                    transport, sourceTick, transitionId);
            }
            else if (IsClient)
            {
                SlimeBossSynchronizer.ApplyLatest();
                ApplyPlayerTwoHostFallback();
            }
        }

        public static void AdvanceFixedInput()
        {
            if (!Enabled)
            {
                ResetInputEdgeLatches();
                return;
            }

            playerTwoFixedPressed = playerTwoPendingPressed;
            playerTwoFixedReleased = playerTwoPendingReleased;
            playerTwoPendingPressed = InputButtons.None;
            playerTwoPendingReleased = InputButtons.None;
            remotePlayerOneFixedPressed = remotePlayerOnePendingPressed;
            remotePlayerOneFixedReleased = remotePlayerOnePendingReleased;
            remotePlayerOnePendingPressed = InputButtons.None;
            remotePlayerOnePendingReleased = InputButtons.None;
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
            previousHostPlayerOneHeld = InputButtons.None;
            received = new InputFrame();
            remotePlayerOneInput = new InputFrame();
            ResetInputEdgeLatches();
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
            p2PredictionIsolationReported = false;
            ResetPlayerTwoStallFallback();
            superButtonReported = false;
            lockButtonReported = false;
            hostPlayerOneSuperActionSequence = 0;
            lastRemotePlayerOneSuperActionSequence = 0;
            hasRemotePlayerOneSuperActionSequence = false;
            lastRemoteInputRealtime = 0f;
            hasRemoteInputActivity = false;
            remoteClientWaiting = false;
            clientHoldAcknowledgedByHost = false;
            sceneTransitionActive = false;
            sceneTransitionId = 0;
            currentSceneEpoch = 0;
            sceneTransitionTarget = string.Empty;
            sceneTransitionStartedRealtime = 0f;
            sceneTransitionDifficulty = (byte)Level.Mode.Normal;
            applyingHostSceneCommand = false;
            blockedClientSceneRequest = string.Empty;
            blockedClientSceneRequestRealtime = 0f;
            mapBootstrapCompleted = false;
            levelPlayerOneBootstrapCompleted = false;
            levelPlayerTwoBootstrapCompleted = false;
            remoteSceneLoadStarted = false;
            backgroundSettingRepairReported = false;
            if (enabled)
            {
                returnToMapAfterAbortedLoad = false;
                ResetSessionHoldState(false);
            }
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
                if (!SceneTransitionActive && transport.IsConnected && hasRemoteInputActivity &&
                    sessionHoldState == SessionHoldState.None &&
                    now - lastRemoteInputRealtime >= PeerStallSeconds)
                {
                    EnterSessionWait("Esperando al invitado.");
                    CaptureAndSendContext(true);
                }
                else if (!SceneTransitionActive && transport.IsConnected && hasRemoteInputActivity &&
                    sessionHoldState == SessionHoldState.Waiting &&
                    now - lastRemoteInputRealtime < 0.25f)
                {
                    BeginHostResumeCountdown("El invitado volvió.");
                }

                if (sessionHoldState == SessionHoldState.Resuming)
                    UpdateHostResumeCountdown(now);
                return;
            }

            // La ausencia breve de snapshots no equivale a pérdida de foco: durante
            // SceneLoader no existen jugadores que capturar. Una caída real queda a
            // cargo del heartbeat/timeout del transporte y del aviso explícito de foco.
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
            remotePlayerOneInput.Pressed = InputButtons.None;
            remotePlayerOneInput.Released = InputButtons.None;
            ResetInputEdgeLatches();
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

        public static void AbortCoordinatedLoad()
        {
            if (!Enabled || !LevelLoadGate.IsActive)
                return;
            var shouldReturnToMap = LevelLoadGate.TargetIsLevel;
            Plugin.Log.LogWarning("[ReadyGate] El usuario canceló la carga coordinada.");
            try
            {
                if (IsHost && transport.IsConnected &&
                    !string.IsNullOrEmpty(sceneTransitionTarget))
                {
                    transport.SendScene(new SceneCommand
                    {
                        SceneName = sceneTransitionTarget,
                        LoadMode = (byte)LoadSceneMode.Single,
                        LevelId = -1,
                        Difficulty = sceneTransitionDifficulty,
                        Flags = SceneCommandFlags.CancelCoordinatedTransition,
                    });
                    // UdpInputTransport conserva los comandos hasta recibir ACK, pero
                    // StopSession cierra el socket enseguida. Este Update envía el
                    // aviso al menos una vez antes de cerrar; el timeout de conexión
                    // sigue siendo la recuperación secundaria del invitado.
                    transport.Update();
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[ReadyGate] No se pudo avisar la cancelación: " +
                    ex.Message);
            }
            StopSession();
            returnToMapAfterAbortedLoad = shouldReturnToMap;
        }

        private static void TryReturnToMapAfterAbortedLoad()
        {
            if (!returnToMapAfterAbortedLoad || SceneLoader.CurrentlyLoading)
                return;

            returnToMapAfterAbortedLoad = false;
            try
            {
                if (SceneLoader.Exists && PlayerData.Data != null)
                {
                    PlayerData.inGame = true;
                    // Esta carga es una recuperación interna, no una selección local
                    // del invitado; debe atravesar el prefijo de autoridad de escena.
                    applyingHostSceneCommand = true;
                    try
                    {
                        SceneLoader.LoadLastMap();
                    }
                    finally
                    {
                        applyingHostSceneCommand = false;
                    }
                    Plugin.Log.LogMessage("[ReadyGate] Regresando al mapa tras cancelar la carga.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[ReadyGate] No se pudo regresar al mapa: " +
                    ex.Message);
            }
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
                    if (!IsMapReadyForPlayerTwoCreation(players))
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
            var heldAtCoordinatedMapExit = SceneTransitionActive &&
                LevelLoadGate.IsActive && sceneTransitionTarget.StartsWith("scene_map_") &&
                SceneManager.GetActiveScene().name == sceneTransitionTarget;
            var loaderReady = SceneLoader.Exists &&
                ((!SceneLoader.CurrentlyLoading && !SceneLoader.IsInIrisTransition &&
                !SceneLoader.IsInBlurTransition) || heldAtCoordinatedMapExit);
            return Map.Current != null && Map.Current.CurrentState == Map.State.Ready &&
                loaderReady &&
                players != null && players.Length > 0 && players[0] != null &&
                players[0].state != MapPlayerController.State.Stationary;
        }

        private static bool IsMapReadyForPlayerTwoCreation(MapPlayerController[] players)
        {
            var heldAtCoordinatedMapExit = SceneTransitionActive &&
                LevelLoadGate.IsActive && sceneTransitionTarget.StartsWith("scene_map_") &&
                SceneManager.GetActiveScene().name == sceneTransitionTarget;
            if (!heldAtCoordinatedMapExit)
                return IsMapReadyForLateSpawn(players);

            // Durante el gate, start_cr debe seguir vivo y puede no haber llegado a
            // Map.State.Ready. Para el bootstrap sólo hace falta que P1 y el flujo
            // cooperativo de creación ya existan; P2 puede seguir Stationary hasta
            // que Cuphead termine naturalmente sus eventos del mapa.
            return SceneLoader.Exists && Map.Current != null &&
                players != null && players.Length > 0 && players[0] != null;
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
                var expected = PlayerManager.GetPlayerInput(PlayerId.PlayerTwo);
                return object.ReferenceEquals(player, expected) ||
                    (expected == null && player.id == 1);
            }
            catch
            {
                return player.id == 1;
            }
        }

        public static bool ShouldSuppressPlayerOne(Player player)
        {
            if (!IsClientSession || !transport.IsConnected || samplingLocalInput ||
                player == null)
                return false;

            try
            {
                if (Map.Current == null && Level.Current == null && !HasPlayerTwoActor())
                    return false;
                var expected = PlayerManager.GetPlayerInput(PlayerId.PlayerOne);
                return object.ReferenceEquals(player, expected) ||
                    (expected == null && player.id == 0);
            }
            catch
            {
                return false;
            }
        }

        public static bool ShouldOverridePlayerOneVisual(Player player)
        {
            if (!CanDriveRemotePlayerOneVisual() || player == null)
                return false;

            try
            {
                var expected = PlayerManager.GetPlayerInput(PlayerId.PlayerOne);
                return object.ReferenceEquals(player, expected) ||
                    (expected == null && player.id == 0);
            }
            catch
            {
                return player.id == 0;
            }
        }

        public static float GetRemotePlayerOneAxis(int actionId)
        {
            if (!CanDriveRemotePlayerOneVisual())
                return 0f;
            if (!playerOneVisualReported)
            {
                playerOneVisualReported = true;
                Plugin.Log.LogMessage("[StateSync] Player One remoto activó su motor visual " +
                    "(H=" + remotePlayerOneInput.Horizontal + " V=" +
                    remotePlayerOneInput.Vertical + " botones=" +
                    (uint)remotePlayerOneInput.Held + ").");
            }
            return remotePlayerOneInput.GetAxis(actionId);
        }

        public static bool GetRemotePlayerOneButton(int actionId, ButtonPhase phase)
        {
            if (!CanDriveRemotePlayerOneVisual())
                return false;
            var button = MapButton(actionId);
            if (button == InputButtons.None)
                return false;
            if (phase == ButtonPhase.Pressed)
            {
                var edges = (FixedGameplayButtons & button) != 0 ?
                    remotePlayerOneFixedPressed : remotePlayerOneInput.Pressed;
                return (edges & button) != 0;
            }
            if (phase == ButtonPhase.Released)
            {
                var edges = (FixedGameplayButtons & button) != 0 ?
                    remotePlayerOneFixedReleased : remotePlayerOneInput.Released;
                return (edges & button) != 0;
            }
            return remotePlayerOneInput.HasHeld(button);
        }

        public static float GetRemotePlayerOneButtonTimePressed(int actionId)
        {
            return GetRemotePlayerOneButton(actionId, ButtonPhase.Held) ?
                Mathf.Max(Time.fixedDeltaTime, 0.001f) : 0f;
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
                horizontal = GetAxis(0);
                vertical = GetAxis(1);
                ReportRewiredRead();
                return true;
            }

            if (input.playerId != PlayerId.PlayerOne || !CanDriveRemotePlayerOneVisual())
                return false;
            horizontal = remotePlayerOneInput.Horizontal / 127f;
            vertical = remotePlayerOneInput.Vertical / 127f;
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
            if (input.playerId != PlayerId.PlayerOne || !CanDriveRemotePlayerOneVisual())
                return false;
            value = GetRemotePlayerOneButton((int)button, ButtonPhase.Held);
            return true;
        }

        private static bool CanDriveRemotePlayerOneVisual()
        {
            if (!IsClientSession || !transport.IsConnected || samplingLocalInput ||
                !HasPlayerTwoActor() || !hasRemotePlayerState ||
                Time.realtimeSinceStartup - lastRemotePlayerStateRealtime > 0.5f)
                return false;
            if ((latestRemotePlayerState.PresentMask & 1) == 0)
                return false;
            if (Map.Current != null)
                return true;
            return Level.Current != null && Level.Current.Started &&
                (latestRemotePlayerState.Flags & PlayerStateFlags.GameplayStarted) != 0;
        }

        internal static bool ShouldDeferRemotePlayerOneSuperMeter(
            float currentMeter, float authoritativeMeter)
        {
            return IsClientSession && transport.IsConnected &&
                remotePlayerOneSuperDeferralDeadline > 0f &&
                authoritativeMeter < currentMeter &&
                Time.realtimeSinceStartup <=
                    remotePlayerOneSuperDeferralDeadline;
        }

        internal static void NotifyPlayerOneSuperConsumed(
            PlayerStatsManager stats)
        {
            if (!Enabled || stats == null)
                return;
            try
            {
                var player = PlayerManager.GetPlayer(PlayerId.PlayerOne);
                if (player == null || !object.ReferenceEquals(player.stats, stats))
                    return;
                if (IsHostSession && transport.IsConnected)
                {
                    hostPlayerOneSuperActionSequence++;
                    if (hostPlayerOneSuperActionSequence == 0)
                        hostPlayerOneSuperActionSequence = 1;
                    return;
                }
                if (!IsClientSession ||
                    remotePlayerOneSuperDeferralDeadline <= 0f)
                    return;
                remotePlayerOneSuperDeferralDeadline = 0f;
                Plugin.Log.LogInfo("[StateSync] El EX/Super remoto consumió " +
                    "su carta visual; se reanuda el medidor autoritativo.");
            }
            catch
            {
                // El jugador puede estar cerrándose al cambiar de escena.
            }
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
                var interruptedLevelLoad = IsClient &&
                    sceneTransitionTarget.StartsWith("scene_level_") &&
                    (remoteSceneLoadStarted || SceneLoader.CurrentlyLoading ||
                        SceneManager.GetActiveScene().name == sceneTransitionTarget);
                if (IsClient && !interruptedLevelLoad)
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
                CancelSceneTransition("La conexión se interrumpió durante el cambio de escena.");
                if (interruptedLevelLoad)
                {
                    StopSession();
                    returnToMapAfterAbortedLoad = true;
                }
            }
            transportWasConnected = connected;
        }

        public static bool OnSceneLoadRequested(string sceneName)
        {
            if (!Enabled || !IsStableScene(sceneName, LoadSceneMode.Single))
                return true;

            if (IsClient && transport.IsConnected && !applyingHostSceneCommand)
            {
                blockedClientSceneRequest = sceneName;
                blockedClientSceneRequestRealtime = Time.realtimeSinceStartup;
                Plugin.Log.LogMessage("[SceneSync] El invitado solicitó " + sceneName +
                    "; esperando la orden autoritativa del host.");
                return false;
            }

            OnLocalSceneLoadStarting(sceneName);
            return true;
        }

        private static void OnLocalSceneLoadStarting(string sceneName)
        {
            if (!Enabled || !IsHost || !transport.IsConnected ||
                !IsStableScene(sceneName, LoadSceneMode.Single))
                return;

            var levelId = -1;
            if (sceneName.StartsWith("scene_level_"))
            {
                try
                {
                    var candidate = SceneLoader.CurrentLevel;
                    if (LevelProperties.GetLevelScene(candidate) == sceneName)
                        levelId = (int)candidate;
                }
                catch { }
            }

            var difficulty = (byte)Level.CurrentMode;
            var transitionId = transport.SendScene(new SceneCommand
            {
                SceneName = sceneName,
                LoadMode = (byte)LoadSceneMode.Single,
                LevelId = levelId,
                Difficulty = difficulty,
                Flags = SceneCommandFlags.CoordinatedTransition,
            });
            BeginSceneTransition(sceneName, levelId, transitionId, difficulty, true);
            CaptureAndSendContext(true);
            // El prefijo corre justo antes de que Unity pueda bloquear el hilo con
            // la carga; forzamos el primer envío en este mismo frame.
            transport.Update();
            Plugin.Log.LogInfo("[SceneSync] Transición #" + transitionId +
                " preanunciada: " + sceneName + ".");
        }

        private static void BeginSceneTransition(string sceneName, int levelId,
            uint transitionId, byte difficulty, bool coordinated)
        {
            if (transitionId == 0 || string.IsNullOrEmpty(sceneName))
                return;
            sceneTransitionActive = true;
            sceneTransitionId = transitionId;
            currentSceneEpoch = transitionId;
            sceneTransitionTarget = sceneName;
            sceneTransitionStartedRealtime = Time.realtimeSinceStartup;
            sceneTransitionDifficulty = difficulty;
            remoteSceneLoadStarted = false;
            if (sceneName.StartsWith("scene_map_"))
                mapBootstrapCompleted = false;
            levelPlayerOneBootstrapCompleted = false;
            ClearRemotePlayerState();
            received = new InputFrame();
            previousHeld = InputButtons.None;
            previousHostPlayerOneHeld = InputButtons.None;
            ResetInputEdgeLatches();
            remoteClientWaiting = false;
            lastRemoteInputRealtime = Time.realtimeSinceStartup;
            if (coordinated)
                LevelLoadGate.BeginTransition(sceneName, levelId, transitionId);
            else
                LevelLoadGate.Reset();
        }

        private static void CompleteSceneTransitionIfPossible()
        {
            if (!sceneTransitionActive || SceneLoader.CurrentlyLoading ||
                LevelLoadGate.IsHoldingGameplay ||
                Time.realtimeSinceStartup - sceneTransitionStartedRealtime < 0.25f ||
                SceneManager.GetActiveScene().name != sceneTransitionTarget)
                return;
            if (sceneTransitionTarget.StartsWith("scene_level_") &&
                (Level.Current == null || !Level.Current.Started))
                return;

            var completedId = sceneTransitionId;
            sceneTransitionActive = false;
            sceneTransitionId = 0;
            sceneTransitionTarget = string.Empty;
            sceneTransitionStartedRealtime = 0f;
            sceneTransitionDifficulty = (byte)Level.Mode.Normal;
            remoteSceneLoadStarted = false;
            received = new InputFrame();
            remotePlayerOneInput = new InputFrame();
            previousHeld = InputButtons.None;
            previousHostPlayerOneHeld = InputButtons.None;
            ResetInputEdgeLatches();
            remoteClientWaiting = false;
            lastRemoteInputRealtime = Time.realtimeSinceStartup;
            LevelLoadGate.Reset();
            Plugin.Log.LogMessage("[SceneSync] Transición #" + completedId +
                " completada sin activar espera de sesión.");
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
                LevelId = Level.Current == null ? -1 : (int)Level.Current.CurrentLevel,
                Difficulty = (byte)Level.CurrentMode,
                Flags = SceneCommandFlags.None,
            });
            Plugin.Log.LogInfo("[SceneSync] Escena encolada: " + scene.name);
        }

        public static void OnSceneLoaded(string sceneName, LoadSceneMode mode)
        {
            LevelLoadGate.OnSceneLoaded(sceneName);
            if (sceneTransitionActive && sceneName == sceneTransitionTarget)
                Level.SetCurrentMode((Level.Mode)sceneTransitionDifficulty);
            ClearRemotePlayerState();
            SlimeBossSynchronizer.Reset();
            levelPlayerOneBootstrapCompleted = false;
            levelPlayerTwoBootstrapCompleted = false;
            if (Enabled && IsClient && sceneName.StartsWith("scene_map_"))
                mapBootstrapCompleted = false;
            lateSpawnAttempted = false;
            ResetLateSpawnRecovery();
            lateSpawnFailureReported = false;
            if (!Enabled || !IsHost || !IsStableScene(sceneName, mode))
                return;

            CaptureAndSendContext(true);
        }

        private static void ProcessSceneCommands()
        {
            if (!IsClient)
                return;

            SceneCommand command;
            while (transport.TryReceiveScene(out command))
            {
                Plugin.Log.LogMessage("[SceneSync] Escena recibida: " + command.SceneName +
                    " #" + command.Sequence + " difficulty=" + command.Difficulty +
                    " coordinated=" + command.IsCoordinatedTransition + ".");
                if (command.CancelsCoordinatedTransition)
                {
                    var cancellationMatches =
                        sceneTransitionTarget == command.SceneName ||
                        blockedClientSceneRequest == command.SceneName ||
                        SceneManager.GetActiveScene().name == command.SceneName;
                    if (!cancellationMatches)
                        continue;
                    var shouldReturnToMap = command.SceneName.StartsWith("scene_level_") &&
                        (remoteSceneLoadStarted || SceneLoader.CurrentlyLoading ||
                            SceneManager.GetActiveScene().name == command.SceneName);
                    CancelSceneTransition("El host canceló la carga coordinada.");
                    StopSession();
                    if (shouldReturnToMap)
                        returnToMapAfterAbortedLoad = true;
                    continue;
                }
                if (!IsStableScene(command.SceneName, (LoadSceneMode)command.LoadMode) ||
                    command.Difficulty > 2)
                    continue;
                var targetAlreadyActive =
                    SceneManager.GetActiveScene().name == command.SceneName;
                Level.SetCurrentMode((Level.Mode)command.Difficulty);
                if (command.IsCoordinatedTransition &&
                    (!sceneTransitionActive || sceneTransitionId != command.Sequence))
                    BeginSceneTransition(command.SceneName, command.LevelId,
                        command.Sequence, command.Difficulty, true);
                else if (!targetAlreadyActive && !command.IsCoordinatedTransition)
                    BeginSceneTransition(command.SceneName, command.LevelId,
                        command.Sequence, command.Difficulty, false);
                if (targetAlreadyActive && command.IsCoordinatedTransition &&
                    !SceneLoader.CurrentlyLoading)
                    LevelLoadGate.AdoptAlreadyLoadedTransition(command.SceneName);
                if (blockedClientSceneRequest == command.SceneName)
                {
                    blockedClientSceneRequest = string.Empty;
                    blockedClientSceneRequestRealtime = 0f;
                }
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
                LoadRemoteScene(command);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[SceneSync] No se pudo cargar " + command.SceneName +
                    ": " + ex.Message);
                CancelSceneTransition("Falló la carga de " + command.SceneName + ".");
            }
        }

        private static bool RequiresSessionContext(string sceneName)
        {
            return sceneName != "scene_title" && sceneName != "scene_slot_select";
        }

        private static void LoadRemoteScene(SceneCommand command)
        {
            var sceneName = command.SceneName;
            if (!SceneLoader.Exists || SceneLoader.CurrentlyLoading)
            {
                Plugin.Log.LogWarning("[SceneSync] El cargador de Cuphead todavía no está disponible.");
                return;
            }

            applyingHostSceneCommand = true;
            try
            {
                Level.SetCurrentMode((Level.Mode)command.Difficulty);
                var canLoadAsLevel = false;
                if (sceneName.StartsWith("scene_level_") && command.LevelId >= 0)
                {
                    try
                    {
                        canLoadAsLevel = LevelProperties.GetLevelScene(
                            (Levels)command.LevelId) == sceneName;
                    }
                    catch { }
                }
                if (canLoadAsLevel)
                {
                    SceneLoader.LoadLevel(
                        (Levels)command.LevelId,
                        SceneLoader.Transition.Fade,
                        SceneLoader.Icon.Hourglass,
                        null);
                    remoteSceneLoadStarted = true;
                    Plugin.Log.LogMessage("[SceneSync] Cuphead cargando nivel remoto " +
                        command.LevelId + " en dificultad " + command.Difficulty +
                        " (transición #" + command.Sequence + ").");
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
                remoteSceneLoadStarted = true;
                Plugin.Log.LogMessage("[SceneSync] Cuphead cargando escena remota " +
                    sceneName + ".");
            }
            finally
            {
                applyingHostSceneCommand = false;
            }
        }

        private static void UpdateBlockedClientSceneRequest()
        {
            if (!IsClient || string.IsNullOrEmpty(blockedClientSceneRequest) ||
                sceneTransitionActive ||
                Time.realtimeSinceStartup - blockedClientSceneRequestRealtime <
                    BlockedSceneRequestTimeoutSeconds)
                return;

            var timedOutScene = blockedClientSceneRequest;
            blockedClientSceneRequest = string.Empty;
            blockedClientSceneRequestRealtime = 0f;
            RestoreBlockedClientMapMenus();
            Plugin.Log.LogWarning("[SceneSync] El host no confirmó " + timedOutScene +
                " en " + BlockedSceneRequestTimeoutSeconds +
                " segundos; se cerró el menú bloqueado del invitado.");
        }

        private static void RestoreBlockedClientMapMenus()
        {
            if (Map.Current == null)
                return;
            try
            {
                if (MapDifficultySelectStartUI.Current != null &&
                    MapDifficultySelectStartUI.Current.CurrentState ==
                        AbstractMapSceneStartUI.State.Loading)
                    MapDifficultySelectStartUI.Current.Out();
                if (MapConfirmStartUI.Current != null &&
                    MapConfirmStartUI.Current.CurrentState ==
                        AbstractMapSceneStartUI.State.Loading)
                    MapConfirmStartUI.Current.Out();
                if (MapBasicStartUI.Current != null &&
                    MapBasicStartUI.Current.CurrentState ==
                        AbstractMapSceneStartUI.State.Loading)
                    MapBasicStartUI.Current.Out();
            }
            catch { }
        }

        private static void UpdatePendingSceneCommandWatchdog()
        {
            if (!IsClient || !sceneTransitionActive || !hasPendingRemoteScene ||
                remoteSceneLoadStarted ||
                Time.realtimeSinceStartup - sceneTransitionStartedRealtime <
                    PendingSceneCommandTimeoutSeconds)
                return;

            CancelSceneTransition("No fue posible iniciar la escena indicada por el host.");
        }

        private static void CancelSceneTransition(string reason)
        {
            var wasActive = sceneTransitionActive || hasPendingRemoteScene ||
                !string.IsNullOrEmpty(blockedClientSceneRequest);
            // Primero se suelta cualquier pausa del gate para que Out() pueda
            // devolver el menú y su fade a un estado consistente.
            LevelLoadGate.Reset();
            RestoreBlockedClientMapMenus();
            sceneTransitionActive = false;
            sceneTransitionId = 0;
            sceneTransitionTarget = string.Empty;
            sceneTransitionStartedRealtime = 0f;
            sceneTransitionDifficulty = (byte)Level.Mode.Normal;
            remoteSceneLoadStarted = false;
            pendingRemoteScene = default(SceneCommand);
            hasPendingRemoteScene = false;
            blockedClientSceneRequest = string.Empty;
            blockedClientSceneRequestRealtime = 0f;
            ClearRemotePlayerState();
            // Si seguimos en el mapa anterior, no se debe aplicar allí un snapshot
            // retenido que pertenecía al destino cancelado.
            mapBootstrapCompleted = Map.Current != null;
            received = new InputFrame();
            previousHeld = InputButtons.None;
            if (wasActive)
                Plugin.Log.LogWarning("[SceneSync] Transición cancelada: " + reason);
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
                    LoadTransitionId = LevelLoadGate.TransitionId,
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
                    " transition=" + context.LoadTransitionId +
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
                    context.SessionSuspended + " transition=" + context.LoadTransitionId +
                    " resume=" + context.ResumeSeconds);
                if (context.SaveSlot > 2 || context.Difficulty > 2)
                    continue;
                ApplyClientSessionHold(context);
                if (context.LevelGateReleased)
                    LevelLoadGate.OnHostRelease(context.CurrentLevel,
                        context.LoadTransitionId);
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
                    if (!LevelLoadGate.IsActive ||
                        context.LoadTransitionId == LevelLoadGate.TransitionId)
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
                left.CurrentLevel == right.CurrentLevel &&
                left.LoadTransitionId == right.LoadTransitionId;
        }

        private static void CaptureAndSendPlayerState()
        {
            if (!transport.IsConnected)
                return;

            var state = new PlayerStateSnapshot
            {
                Tick = sourceTick,
                TransitionId = sceneTransitionActive &&
                    SceneManager.GetActiveScene().name == sceneTransitionTarget ?
                    sceneTransitionId : 0,
                PlayerOneMapHorizontal = hostPlayerOneInput.Horizontal,
                PlayerOneMapVertical = hostPlayerOneInput.Vertical,
                PlayerOneHeld = hostPlayerOneInput.Held,
                PlayerOnePressed = hostPlayerOneInput.Pressed,
                PlayerOneReleased = hostPlayerOneInput.Released,
                PlayerOneSuperActionSequence =
                    hostPlayerOneSuperActionSequence,
            };
            if (Level.Current != null && Level.Current.Started)
                state.Flags |= PlayerStateFlags.GameplayStarted;
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
            hostPlayerOneInput.Pressed = InputButtons.None;
            hostPlayerOneInput.Released = InputButtons.None;
        }

        private static void CaptureHostPlayerOneInput()
        {
            if (!transport.IsConnected)
            {
                hostPlayerOneInput = new InputFrame { Tick = sourceTick };
                previousHostPlayerOneHeld = InputButtons.None;
                return;
            }

            samplingLocalInput = true;
            try
            {
                var sampled = new InputFrame { Tick = sourceTick };
                MergeConfiguredPlayer(PlayerId.PlayerOne, ref sampled);
                MergeInput(ref sampled, SampleUnityFallbackInput());
                if (IsGameplayInputLocked())
                {
                    previousHostPlayerOneHeld = sampled.Held;
                    hostPlayerOneInput = new InputFrame { Tick = sourceTick };
                    return;
                }
                sampled.Pressed |= sampled.Held & ~previousHostPlayerOneHeld;
                sampled.Released |= previousHostPlayerOneHeld & ~sampled.Held;
                previousHostPlayerOneHeld = sampled.Held;
                hostPlayerOneInput = sampled;
            }
            finally
            {
                samplingLocalInput = false;
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
            if (id == PlayerId.PlayerOne && !hostMapAxesReported &&
                (state.PlayerOneMapHorizontal != 0 || state.PlayerOneMapVertical != 0))
            {
                hostMapAxesReported = true;
                Plugin.Log.LogMessage("[StateSync] El host transmite movimiento " +
                    "del mapa H=" + state.PlayerOneMapHorizontal + " V=" +
                    state.PlayerOneMapVertical + ".");
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
            if (player.stats != null)
            {
                if (id == PlayerId.PlayerOne)
                    state.PlayerOneSuperMeter = player.stats.SuperMeter;
                else
                    state.PlayerTwoSuperMeter = player.stats.SuperMeter;
            }

            var levelPlayer = player as LevelPlayerController;
            var motor = levelPlayer == null ? null : levelPlayer.motor;
            if (motor == null)
                return;
            var motionFlags = PlayerMotionFlags.None;
            if (motor.Dashing)
                motionFlags |= PlayerMotionFlags.Dashing;
            if (motor.IsHit)
                motionFlags |= PlayerMotionFlags.Hit;
            if (motor.IsUsingSuperOrEx)
                motionFlags |= PlayerMotionFlags.UsingSuperOrEx;
            if (id == PlayerId.PlayerOne)
                state.PlayerOneMotionFlags = motionFlags;
            else
            {
                state.PlayerTwoMotionFlags = motionFlags;
                state.PlayerTwoHitDirection = CaptureHitDirection(motor);
            }
        }

        private static sbyte CaptureHitDirection(LevelPlayerMotor motor)
        {
            if (motor == null || LevelPlayerMotorHitManagerField == null ||
                LevelPlayerMotorHitDirectionField == null)
                return 0;
            try
            {
                var hitManager = LevelPlayerMotorHitManagerField.GetValue(motor);
                if (hitManager == null)
                    return 0;
                return (sbyte)Mathf.Clamp((int)
                    LevelPlayerMotorHitDirectionField.GetValue(hitManager), -1, 1);
            }
            catch
            {
                return 0;
            }
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
                if (sceneTransitionActive && state.TransitionId != sceneTransitionId)
                    continue;
                latestRemotePlayerState = state;
                latestRemotePlayerStateScene = SceneManager.GetActiveScene().name;
                var pressed = state.PlayerOnePressed |
                    (state.PlayerOneHeld & ~previousRemotePlayerOneHeld);
                var released = state.PlayerOneReleased |
                    (previousRemotePlayerOneHeld & ~state.PlayerOneHeld);
                // EX/Super se reproduce sólo cuando el host confirma que el juego
                // consumió una carta. La secuencia persiste en snapshots posteriores,
                // por lo que un tap corto no depende de que llegue un datagrama exacto.
                pressed &= ~InputButtons.Super;
                if (state.PlayerOneSuperActionSequence != 0 &&
                    (!hasRemotePlayerOneSuperActionSequence ||
                        IsNewerTick(state.PlayerOneSuperActionSequence,
                            lastRemotePlayerOneSuperActionSequence)))
                {
                    lastRemotePlayerOneSuperActionSequence =
                        state.PlayerOneSuperActionSequence;
                    hasRemotePlayerOneSuperActionSequence = true;
                    pressed |= InputButtons.Super;
                    remotePlayerOneSuperDeferralDeadline =
                        Time.realtimeSinceStartup +
                        RemotePlayerOneSuperDeferralSeconds;
                }
                previousRemotePlayerOneHeld = state.PlayerOneHeld;
                remotePlayerOneInput.Tick = state.Tick;
                remotePlayerOneInput.Horizontal = state.PlayerOneMapHorizontal;
                remotePlayerOneInput.Vertical = state.PlayerOneMapVertical;
                remotePlayerOneInput.Held = state.PlayerOneHeld;
                remotePlayerOneInput.Pressed |= pressed;
                remotePlayerOneInput.Released |= released;
                QueueRemotePlayerOneFixedEdges(pressed, released);
                hasRemotePlayerState = true;
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
                SessionOverlayVisible || latestRemotePlayerStateScene !=
                    SceneManager.GetActiveScene().name ||
                Time.realtimeSinceStartup - lastRemotePlayerStateRealtime > 0.5f)
                return;

            if (Map.Current != null && !mapBootstrapCompleted)
            {
                TryBootstrapClientMapFromHost();
                if (!mapBootstrapCompleted)
                    return;
            }

            if (SceneTransitionActive)
                return;

            var onLevel = Level.Current != null;
            if (onLevel && (!Level.Current.Started ||
                (latestRemotePlayerState.Flags & PlayerStateFlags.GameplayStarted) == 0))
                return;

            if ((latestRemotePlayerState.PresentMask & 1) != 0)
            {
                var firstLevelCorrection = onLevel &&
                    !levelPlayerOneBootstrapCompleted;
                CorrectPlayerPosition(PlayerId.PlayerOne,
                    latestRemotePlayerState.PlayerOneX, latestRemotePlayerState.PlayerOneY,
                    firstLevelCorrection);
                if (firstLevelCorrection)
                {
                    levelPlayerOneBootstrapCompleted = true;
                    Plugin.Log.LogMessage("[StateSync] Player One remoto alineado al iniciar " +
                        "el combate.");
                }
            }
            if ((latestRemotePlayerState.PresentMask & 2) != 0)
            {
                // P2 pertenece al invitado y se simula localmente. El snapshot del
                // host ya recorrió cliente -> host -> cliente, así que usarlo como
                // corrección continua rebobina cada dash aproximadamente un RTT.
                // Sólo se usa para colocar al actor una vez al abrir el nivel; la
                // reconciliación continua futura necesitará ACK + replay de inputs.
                var firstLevelCorrection = onLevel &&
                    !levelPlayerTwoBootstrapCompleted;
                if (firstLevelCorrection)
                {
                    CorrectPlayerPosition(PlayerId.PlayerTwo,
                        latestRemotePlayerState.PlayerTwoX,
                        latestRemotePlayerState.PlayerTwoY, true);
                    levelPlayerTwoBootstrapCompleted = true;
                    ResetPlayerTwoStallFallback();
                }
                UpdatePlayerTwoStallFallback();
                if (!p2PredictionIsolationReported)
                {
                    p2PredictionIsolationReported = true;
                    Plugin.Log.LogInfo("[StateSync] P2 queda bajo predicción local; " +
                        "el host sólo interviene si detectamos que quedó inmóvil.");
                }
            }

            SlimeBossSynchronizer.ApplyAuthoritativePlayerState(
                latestRemotePlayerState);
        }

        private static void ClearRemotePlayerState()
        {
            latestRemotePlayerState = default(PlayerStateSnapshot);
            latestRemotePlayerStateScene = string.Empty;
            remotePlayerOneInput = new InputFrame();
            previousRemotePlayerOneHeld = InputButtons.None;
            remotePlayerOnePendingPressed = InputButtons.None;
            remotePlayerOnePendingReleased = InputButtons.None;
            remotePlayerOneFixedPressed = InputButtons.None;
            remotePlayerOneFixedReleased = InputButtons.None;
            hasRemotePlayerState = false;
            lastRemotePlayerStateRealtime = 0f;
            remotePlayerOneSuperDeferralDeadline = 0f;
            ResetPlayerTwoStallFallback();
        }

        private static void ResetRemotePlayerState()
        {
            ClearRemotePlayerState();
            SlimeBossSynchronizer.Reset();
            lastRemotePlayerStateTick = 0;
            hasRemotePlayerStateTick = false;
            mapBootstrapCompleted = false;
            levelPlayerOneBootstrapCompleted = false;
            levelPlayerTwoBootstrapCompleted = false;
        }

        private static void TryBootstrapClientMapFromHost()
        {
            if (mapBootstrapCompleted || Map.Current == null ||
                (latestRemotePlayerState.PresentMask & 3) != 3)
                return;
            if (sceneTransitionActive &&
                SceneManager.GetActiveScene().name != sceneTransitionTarget)
                return;
            var expectedTransitionId = LevelLoadGate.TransitionId;
            if (expectedTransitionId != 0 &&
                latestRemotePlayerState.TransitionId != expectedTransitionId)
                return;

            var players = Map.Current.players;
            if (players == null || players.Length < 2 ||
                players[0] == null || players[1] == null)
                return;

            var playerOnePosition = new Vector3(latestRemotePlayerState.PlayerOneX,
                latestRemotePlayerState.PlayerOneY, players[0].transform.position.z);
            var playerTwoPosition = new Vector3(latestRemotePlayerState.PlayerTwoX,
                latestRemotePlayerState.PlayerTwoY, players[1].transform.position.z);
            AlignMapPlayer(players[0], playerOnePosition);
            AlignMapPlayer(players[1], playerTwoPosition);

            try
            {
                if (PlayerData.Data != null)
                {
                    PlayerData.Data.CurrentMapData.playerOnePosition = playerOnePosition;
                    PlayerData.Data.CurrentMapData.playerTwoPosition = playerTwoPosition;
                }
            }
            catch { }

            if (CupheadMapCamera.Current != null)
            {
                var cameraPosition = CupheadMapCamera.Current.transform.position;
                cameraPosition.x = (playerOnePosition.x + playerTwoPosition.x) * 0.5f;
                cameraPosition.y = (playerOnePosition.y + playerTwoPosition.y) * 0.5f;
                CupheadMapCamera.Current.transform.position = cameraPosition;
            }

            mapBootstrapCompleted = true;
            Plugin.Log.LogMessage("[StateSync] Mapa del invitado alineado con el host " +
                "antes de abrir la carga.");
        }

        private static void AlignMapPlayer(MapPlayerController player, Vector3 position)
        {
            player.transform.position = position;
            if (player.motor != null && MapPlayerMotorVelocityField != null)
                MapPlayerMotorVelocityField.SetValue(player.motor, Vector2.zero);
            var body = player.GetComponent<Rigidbody2D>();
            if (body != null)
                body.velocity = Vector2.zero;
        }

        private static bool IsNewerTick(uint candidate, uint current)
        {
            return candidate != current && unchecked((int)(candidate - current)) > 0;
        }

        private static void UpdatePlayerTwoStallFallback()
        {
            if (Map.Current != null || Level.Current == null ||
                !Level.Current.Started ||
                (latestRemotePlayerState.PresentMask & 2) == 0 ||
                (latestRemotePlayerState.DeadMask & 2) != 0)
            {
                ResetPlayerTwoStallFallback();
                return;
            }

            AbstractPlayerController player;
            try { player = PlayerManager.GetPlayer(PlayerId.PlayerTwo); }
            catch { return; }
            if (player == null)
                return;

            var localPosition = (Vector2)player.transform.position;
            var hostPosition = new Vector2(
                latestRemotePlayerState.PlayerTwoX,
                latestRemotePlayerState.PlayerTwoY);
            if (playerTwoHostFallbackActive)
                return;

            var movementRequested = Mathf.Abs(received.Horizontal) >= 32 ||
                Mathf.Abs(received.Vertical) >= 32 ||
                (received.Held & (InputButtons.Jump | InputButtons.Dash)) != 0;
            if (!movementRequested)
            {
                playerTwoStallProbeActive = false;
                playerTwoStallProbeLocalDistance = 0f;
                playerTwoStallProbeHostDistance = 0f;
                playerTwoConsecutiveStallProbes = 0;
                return;
            }

            var now = Time.realtimeSinceStartup;
            if (!playerTwoStallProbeActive)
            {
                playerTwoStallProbeActive = true;
                playerTwoStallProbeStartedRealtime = now;
                playerTwoStallProbeLastLocalPosition = localPosition;
                playerTwoStallProbeLastHostPosition = hostPosition;
                playerTwoStallProbeLocalDistance = 0f;
                playerTwoStallProbeHostDistance = 0f;
                return;
            }

            playerTwoStallProbeLocalDistance += Vector2.Distance(
                playerTwoStallProbeLastLocalPosition, localPosition);
            playerTwoStallProbeHostDistance += Vector2.Distance(
                playerTwoStallProbeLastHostPosition, hostPosition);
            playerTwoStallProbeLastLocalPosition = localPosition;
            playerTwoStallProbeLastHostPosition = hostPosition;
            if (now - playerTwoStallProbeStartedRealtime <
                PlayerTwoStallProbeSeconds)
                return;

            if (playerTwoStallProbeLocalDistance <
                    PlayerTwoLocalMovementThreshold &&
                playerTwoStallProbeHostDistance >
                    PlayerTwoHostMovementThreshold)
            {
                playerTwoConsecutiveStallProbes++;
                if (playerTwoConsecutiveStallProbes >= 2)
                {
                    playerTwoHostFallbackActive = true;
                    Plugin.Log.LogWarning("[StateSync] P2 no avanzó localmente " +
                        "durante dos comprobaciones aunque el host sí lo movió; " +
                        "se activa seguimiento de rescate hasta cambiar de escena.");
                    return;
                }
            }
            else
            {
                playerTwoConsecutiveStallProbes = 0;
            }

            playerTwoStallProbeStartedRealtime = now;
            playerTwoStallProbeLocalDistance = 0f;
            playerTwoStallProbeHostDistance = 0f;
        }

        private static void ApplyPlayerTwoHostFallback()
        {
            if (!playerTwoHostFallbackActive || !IsClient ||
                SessionOverlayVisible || SceneTransitionActive ||
                !hasRemotePlayerState || Level.Current == null ||
                !Level.Current.Started || latestRemotePlayerStateScene !=
                    SceneManager.GetActiveScene().name ||
                Time.realtimeSinceStartup - lastRemotePlayerStateRealtime > 0.5f)
                return;

            AbstractPlayerController player;
            try { player = PlayerManager.GetPlayer(PlayerId.PlayerTwo); }
            catch { return; }
            if (player == null)
                return;
            FollowPlayerTwoFromHost(player.transform, new Vector2(
                latestRemotePlayerState.PlayerTwoX,
                latestRemotePlayerState.PlayerTwoY));
        }

        private static void FollowPlayerTwoFromHost(Transform playerTransform,
            Vector2 hostPosition)
        {
            var current = playerTransform.position;
            var target = new Vector3(hostPosition.x, hostPosition.y, current.z);
            var distance = Vector2.Distance(current, target);
            if (distance > 80f)
                playerTransform.position = target;
            else if (distance > 0.5f)
                playerTransform.position = Vector3.Lerp(current, target,
                    Mathf.Clamp01(Time.unscaledDeltaTime * 18f));
        }

        private static void ResetPlayerTwoStallFallback()
        {
            playerTwoStallProbeActive = false;
            playerTwoHostFallbackActive = false;
            playerTwoStallProbeStartedRealtime = 0f;
            playerTwoStallProbeLastLocalPosition = Vector2.zero;
            playerTwoStallProbeLastHostPosition = Vector2.zero;
            playerTwoStallProbeLocalDistance = 0f;
            playerTwoStallProbeHostDistance = 0f;
            playerTwoConsecutiveStallProbes = 0;
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
            var hostActivelyMoving = remotePlayerOneInput.Horizontal != 0 ||
                remotePlayerOneInput.Vertical != 0 ||
                (remotePlayerOneInput.Held &
                    (InputButtons.Jump | InputButtons.Dash)) != 0;
            if (authoritative || distance > 80f)
                playerTransform.position = target;
            else if (!hostActivelyMoving && distance > 3f)
                playerTransform.position = Vector3.Lerp(current, target,
                    Mathf.Clamp01(Time.unscaledDeltaTime * 8f));
        }

        private static void CorrectMapTransform(Transform playerTransform, float x, float y)
        {
            var current = playerTransform.position;
            var target = new Vector3(x, y, current.z);
            var distance = Vector2.Distance(current, target);
            var hostMoving = latestRemotePlayerState.PlayerOneMapHorizontal != 0 ||
                latestRemotePlayerState.PlayerOneMapVertical != 0;
            if (distance > 20f)
                playerTransform.position = target;
            else if (!hostMoving && distance > 0.5f)
                playerTransform.position = Vector3.Lerp(current, target,
                    Mathf.Clamp01(Time.unscaledDeltaTime * 6f));
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

        public static float GetAxis(int actionId)
        {
            return IsGameplayInputLocked() ? 0f : received.GetAxis(actionId);
        }

        public static void ReportRewiredRead()
        {
            if (rewiredReadReported)
                return;

            rewiredReadReported = true;
            Plugin.Log.LogMessage("[InputLab] Rewired Player 2 está consumiendo frames del transporte.");
        }

        public static bool GetButton(int actionId, ButtonPhase phase)
        {
            if (IsGameplayInputLocked())
                return false;
            var button = MapButton(actionId);
            if (button == InputButtons.None)
                return false;

            if (phase == ButtonPhase.Pressed)
            {
                var edges = (FixedGameplayButtons & button) != 0 ?
                    playerTwoFixedPressed : received.Pressed;
                return (edges & button) != 0;
            }
            if (phase == ButtonPhase.Released)
            {
                var edges = (FixedGameplayButtons & button) != 0 ?
                    playerTwoFixedReleased : received.Released;
                return (edges & button) != 0;
            }
            return received.HasHeld(button);
        }

        public static float GetButtonTimePressed(int actionId)
        {
            return GetButton(actionId, ButtonPhase.Held) ?
                Mathf.Max(Time.fixedDeltaTime, 0.001f) : 0f;
        }

        private static bool IsGameplayInputLocked()
        {
            return sessionHoldState != SessionHoldState.None || SceneTransitionActive ||
                LevelLoadGate.IsHoldingGameplay || IsLevelIntroActive();
        }

        private static bool IsLevelIntroActive()
        {
            return Level.Current != null && !Level.Current.Started;
        }

        private static void QueuePlayerTwoFixedEdges(InputButtons pressed,
            InputButtons released)
        {
            playerTwoPendingPressed |= pressed & FixedGameplayButtons;
            playerTwoPendingReleased |= released & FixedGameplayButtons;
        }

        private static void QueueRemotePlayerOneFixedEdges(InputButtons pressed,
            InputButtons released)
        {
            remotePlayerOnePendingPressed |= pressed & FixedGameplayButtons;
            remotePlayerOnePendingReleased |= released & FixedGameplayButtons;
        }

        private static void ResetInputEdgeLatches()
        {
            playerTwoPendingPressed = InputButtons.None;
            playerTwoPendingReleased = InputButtons.None;
            playerTwoFixedPressed = InputButtons.None;
            playerTwoFixedReleased = InputButtons.None;
            remotePlayerOnePendingPressed = InputButtons.None;
            remotePlayerOnePendingReleased = InputButtons.None;
            remotePlayerOneFixedPressed = InputButtons.None;
            remotePlayerOneFixedReleased = InputButtons.None;
        }

        private static void ReportPlayerTwoCombatButtons(string source, InputFrame frame)
        {
            if (!superButtonReported &&
                ((frame.Held | frame.Pressed) & InputButtons.Super) != 0)
            {
                superButtonReported = true;
                Plugin.Log.LogMessage("[InputSync] P2 " + source +
                    " el botón EX/Super.");
            }
            if (!lockButtonReported && (frame.Held & InputButtons.Lock) != 0)
            {
                lockButtonReported = true;
                Plugin.Log.LogMessage("[InputSync] P2 " + source +
                    " el botón de fijar.");
            }
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
                    Pressed = ReadConfiguredButtonEdges(player, true),
                    Released = ReadConfiguredButtonEdges(player, false),
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
            target.Pressed |= candidate.Pressed;
            target.Released |= candidate.Released;
        }

        private static InputFrame SampleUnityFallbackInput()
        {
            var sampled = SampleLabKeyboardInput();
            var arrows = new InputFrame
            {
                Horizontal = ReadAxis(KeyCode.LeftArrow, KeyCode.RightArrow),
                Vertical = ReadAxis(KeyCode.DownArrow, KeyCode.UpArrow),
                Held = ReadMenuDirections(KeyCode.UpArrow, KeyCode.LeftArrow,
                    KeyCode.DownArrow, KeyCode.RightArrow),
            };
            var wasd = new InputFrame
            {
                Horizontal = ReadAxis(KeyCode.A, KeyCode.D),
                Vertical = ReadAxis(KeyCode.S, KeyCode.W),
                Held = ReadMenuDirections(KeyCode.W, KeyCode.A,
                    KeyCode.S, KeyCode.D),
            };
            MergeInput(ref sampled, arrows);
            MergeInput(ref sampled, wasd);
            AddKeyInput(ref sampled, KeyCode.Escape,
                InputButtons.Pause | InputButtons.Cancel);
            AddKeyInput(ref sampled, KeyCode.Z,
                InputButtons.Jump | InputButtons.Accept);
            AddKeyInput(ref sampled, KeyCode.Return, InputButtons.Accept);
            AddKeyInput(ref sampled, KeyCode.X, InputButtons.Shoot);
            AddKeyInput(ref sampled, KeyCode.V, InputButtons.Super);
            AddKeyInput(ref sampled, KeyCode.Tab, InputButtons.SwitchWeapon);
            AddKeyInput(ref sampled, KeyCode.C, InputButtons.Lock);
            AddKeyInput(ref sampled, KeyCode.LeftShift,
                InputButtons.Dash | InputButtons.EquipMenu);
            AddKeyInput(ref sampled, KeyCode.RightShift,
                InputButtons.Dash | InputButtons.EquipMenu);

            // Respaldo para mandos XInput que Unity ve, pero Rewired todavía no
            // asignó a uno de los dos perfiles (algo común por escritorio remoto).
            AddKeyInput(ref sampled, KeyCode.JoystickButton0,
                InputButtons.Jump | InputButtons.Accept);
            AddKeyInput(ref sampled, KeyCode.JoystickButton1,
                InputButtons.Super | InputButtons.Cancel);
            AddKeyInput(ref sampled, KeyCode.JoystickButton2, InputButtons.Shoot);
            AddKeyInput(ref sampled, KeyCode.JoystickButton3, InputButtons.Dash);
            AddKeyInput(ref sampled, KeyCode.JoystickButton4,
                InputButtons.SwitchWeapon);
            AddKeyInput(ref sampled, KeyCode.JoystickButton5, InputButtons.Lock);
            AddKeyInput(ref sampled, KeyCode.JoystickButton7, InputButtons.Pause);
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
            AddIfHeld(ref buttons, player, 16, InputButtons.MenuUp);
            AddIfHeld(ref buttons, player, 18, InputButtons.MenuLeft);
            AddIfHeld(ref buttons, player, 19, InputButtons.MenuDown);
            AddIfHeld(ref buttons, player, 20, InputButtons.MenuRight);
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

        private static InputButtons ReadMenuDirections(KeyCode up, KeyCode left,
            KeyCode down, KeyCode right)
        {
            var buttons = InputButtons.None;
            AddIfHeld(ref buttons, up, InputButtons.MenuUp);
            AddIfHeld(ref buttons, left, InputButtons.MenuLeft);
            AddIfHeld(ref buttons, down, InputButtons.MenuDown);
            AddIfHeld(ref buttons, right, InputButtons.MenuRight);
            return buttons;
        }

        private static InputButtons ReadConfiguredButtonEdges(Player player, bool pressed)
        {
            var buttons = InputButtons.None;
            AddIfEdge(ref buttons, player, 2, InputButtons.Jump, pressed);
            AddIfEdge(ref buttons, player, 3, InputButtons.Shoot, pressed);
            AddIfEdge(ref buttons, player, 4, InputButtons.Super, pressed);
            AddIfEdge(ref buttons, player, 5, InputButtons.SwitchWeapon, pressed);
            AddIfEdge(ref buttons, player, 6, InputButtons.Lock, pressed);
            AddIfEdge(ref buttons, player, 7, InputButtons.Dash, pressed);
            AddIfEdge(ref buttons, player, 8, InputButtons.Pause, pressed);
            AddIfEdge(ref buttons, player, 13, InputButtons.Accept, pressed);
            AddIfEdge(ref buttons, player, 14, InputButtons.Cancel, pressed);
            AddIfEdge(ref buttons, player, 15, InputButtons.EquipMenu, pressed);
            AddIfEdge(ref buttons, player, 16, InputButtons.MenuUp, pressed);
            AddIfEdge(ref buttons, player, 18, InputButtons.MenuLeft, pressed);
            AddIfEdge(ref buttons, player, 19, InputButtons.MenuDown, pressed);
            AddIfEdge(ref buttons, player, 20, InputButtons.MenuRight, pressed);
            AddIfEdge(ref buttons, player, 26, InputButtons.Swap, pressed);
            return buttons;
        }

        private static void AddIfEdge(ref InputButtons buttons, Player player,
            int actionId, InputButtons button, bool pressed)
        {
            if (pressed ? player.GetButtonDown(actionId) : player.GetButtonUp(actionId))
                buttons |= button;
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

        private static void AddKeyInput(ref InputFrame frame, KeyCode key,
            InputButtons buttons)
        {
            if (Input.GetKey(key))
                frame.Held |= buttons;
            if (Input.GetKeyDown(key))
                frame.Pressed |= buttons;
            if (Input.GetKeyUp(key))
                frame.Released |= buttons;
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
                case 16: return InputButtons.MenuUp;
                case 18: return InputButtons.MenuLeft;
                case 19: return InputButtons.MenuDown;
                case 20: return InputButtons.MenuRight;
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
