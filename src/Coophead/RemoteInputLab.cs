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
        private const uint ModVersionToken = 0x001210;
        // AirGPU/RDP puede entregar frames en ráfagas al cambiar de ventana.
        // Un hold de 1.25 s pausaba la partida varias veces por un microcorte.
        private const float PeerStallSeconds = 3f;
        private const float RemoteInputNeutralizeSeconds = 0.5f;
        private const float ResumeCountdownSeconds = 3f;
        private const float LongWaitSeconds = 15f;
        private const float BlockedSceneRequestTimeoutSeconds = 5f;
        private const float PendingSceneCommandTimeoutSeconds = 10f;
        private const float PlayerTwoStallProbeSeconds = 0.4f;
        private const float PlayerTwoLocalMovementThreshold = 3f;
        private const float PlayerTwoHostMovementThreshold = 12f;
        private const float RemotePlayerOneSuperDeferralSeconds = 0.35f;
        private const float RemotePlayerTwoSuperDeferralSeconds = 0.35f;
        private const float PlayerTwoSuperRequestAdvertiseSeconds = 3f;
        private const float DisconnectGraceSeconds = 0.75f;
        private const float ReturnToStartRetrySeconds = 1f;
        private const float RemoteStateInterpolationDelaySeconds = 0.075f;
        private const float RemoteStateNominalIntervalSeconds = 1f / 60f;
        private const int RemoteStateBufferCapacity = 24;
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
        private static readonly System.Reflection.MethodInfo GoToStartScreenMethod =
            AccessTools.Method(typeof(PlayerManager), "goToStartScreen");
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
        private static readonly System.Reflection.FieldInfo LevelPlayerMotorLastPositionField =
            AccessTools.Field(typeof(LevelPlayerMotor), "lastPosition");
        private static readonly System.Reflection.FieldInfo LevelPlayerMotorLastPositionFixedField =
            AccessTools.Field(typeof(LevelPlayerMotor), "lastPositionFixed");
        private static readonly System.Reflection.FieldInfo PlayerIsRevivingField =
            AccessTools.Field(typeof(AbstractPlayerController), "_isReviving");

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
        private static bool runInBackgroundForTesting;
        private static bool blockLocalInputWhenUnfocused;
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
        private static readonly System.Collections.Generic.List<BufferedPlayerState>
            remotePlayerStateBuffer =
                new System.Collections.Generic.List<BufferedPlayerState>(
                    RemoteStateBufferCapacity);
        private static float lastBufferedPlayerStateRealtime;
        private static float maxBufferedPlayerStateGap;
        private static float maxPlayerOneRenderError;
        private static float maxPlayerTwoRenderError;
        private static float lastRenderTelemetryRealtime;
        private static bool authoritativeMapRenderReported;
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
        private static bool loadoutHealthAgreementReported;
        private static bool loadoutHealthMismatchReported;
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
        private static float remotePlayerTwoSuperDeferralDeadline;
        private static uint hostPlayerOneSuperActionSequence;
        private static uint hostPlayerTwoSuperActionSequence;
        private static uint lastRemotePlayerOneSuperActionSequence;
        private static bool hasRemotePlayerOneSuperActionSequence;
        private static uint lastRemotePlayerTwoSuperActionSequence;
        private static bool hasRemotePlayerTwoSuperActionSequence;
        private static uint localPlayerTwoSuperRequestSequence;
        private static float localPlayerTwoSuperRequestAdvertiseDeadline;
        private static uint localInputSessionNonce;
        private static uint lastRemoteInputSessionNonce;
        private static uint localStateSessionNonce;
        private static uint lastRemoteStateSessionNonce;
        private static PlayerLoadoutSnapshot localGuestLoadout;
        private static uint localGuestLoadoutRevision;
        private static bool hasLocalGuestLoadout;
        private static PlayerLoadoutSnapshot remoteGuestLoadout;
        private static uint remoteGuestLoadoutRevision;
        private static bool hasRemoteGuestLoadout;
        private static PlayerData.PlayerLoadouts.PlayerLoadout
            hostPlayerTwoLoadoutOverlay;
        private static PlayerData.PlayerLoadouts.PlayerLoadout
            clientPlayerOneLoadoutOverlay;
        private static PlayerData.PlayerLoadouts.PlayerLoadout
            clientPlayerTwoLoadoutOverlay;
        private static bool hasClientPlayerOneLoadoutOverlay;
        private static bool hasClientPlayerTwoLoadoutOverlay;
        private static uint lastRemotePlayerTwoSuperRequestSequence;
        private static bool hasRemotePlayerTwoSuperRequestSequence;
        private static readonly System.Collections.Generic.Queue<PlayerTwoSuperRequest>
            hostPendingPlayerTwoSuperRequests =
                new System.Collections.Generic.Queue<PlayerTwoSuperRequest>();
        private static uint hostOfferedPlayerTwoSuperRequestSequence;
        private static readonly System.Collections.Generic.Queue<uint>
            localPlayerTwoSuperDispatchQueue =
                new System.Collections.Generic.Queue<uint>();
        private static uint localPlayerTwoSuperDispatchedForFixed;
        private static readonly System.Collections.Generic.HashSet<uint>
            localPredictedPlayerTwoSuperRequests =
                new System.Collections.Generic.HashSet<uint>();
        private static readonly System.Collections.Generic.Queue<uint>
            playerTwoConfirmedSuperQueue =
                new System.Collections.Generic.Queue<uint>();
        private static uint playerTwoConfirmedSuperDispatchedForFixed;
        private static float remoteMapAcceptDeadline;
        private static bool mapLevelInteractionInputProbeActive;
        private static float playerTwoMapNeutralSinceRealtime;
        private static int originalClientSaveSlot;
        private static bool originalClientPlayerOneIsMugman;
        private static bool originalClientSavePlayerOneIsMugman;
        private static Level.Mode originalClientDifficulty;
        private static bool originalClientInGame;
        private static string originalClientDialoguerState;
        private static bool originalClientContextCaptured;
        private static readonly bool[] clientSlotStateCaptured = new bool[3];
        private static readonly string[] clientSlotOriginalJson = new string[3];
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
        private static bool remoteInputNeutralizedForStall;
        private static bool remoteClientWaiting;
        private static bool clientHoldAcknowledgedByHost;
        private static bool sceneTransitionActive;
        private static uint sceneTransitionId;
        private static uint currentSceneEpoch;
        private static string sceneTransitionTarget = string.Empty;
        private static float sceneTransitionStartedRealtime;
        private static byte sceneTransitionDifficulty;
        private static bool backgroundSettingRepairReported;
        private static bool localPhysicalInputNeedsRearm;
        private static int localPhysicalInputRearmNotBeforeFrame;
        private static bool applyingHostSceneCommand;
        private static string blockedClientSceneRequest = string.Empty;
        private static float blockedClientSceneRequestRealtime;
        private static bool mapBootstrapCompleted;
        private static bool levelPlayerOneBootstrapCompleted;
        private static bool levelPlayerTwoBootstrapCompleted;
        private static bool remoteSceneLoadStarted;
        private static uint remoteSceneLoadObservedTransitionId;
        private static uint remoteSameSceneReloadTransitionId;
        private static uint deferredRemoteSceneTransitionId;
        private static bool deferredRemoteSceneRequiresReload;
        private static float deferredRemoteSceneReceivedRealtime;
        private static int deferredRemoteSceneLoaderIdleFrame = -1;
        private static bool returnToMapAfterAbortedLoad;
        private static bool internalPlayerLeave;
        private static bool returnToStartAfterSession;
        private static float returnToStartRetryNotBeforeRealtime;
        private static float clientRestoreRetryNotBeforeRealtime;
        private static bool sessionStopPending;
        private static bool pendingSessionStopReturnToStart;
        private static float pendingSessionStopDeadlineRealtime;
        private static int pendingSessionStopFinalizeNotBeforeFrame;
        private static string sessionNotice = string.Empty;
        private static float sessionNoticeDeadline;

        public static bool Enabled { get; private set; }
        private static bool IsHost => transportMode == InputTransportMode.LanHost ||
            transportMode == InputTransportMode.InternetHost || transportMode == InputTransportMode.P2pHost;
        private static bool IsClient => transportMode == InputTransportMode.LanClient ||
            transportMode == InputTransportMode.InternetClient || transportMode == InputTransportMode.P2pClient;
        public static bool DrivesPlayerTwo => Enabled;
        public static bool IsHostSession => Enabled && IsHost;
        public static bool IsClientSession => Enabled && IsClient;
        public static bool IsConnected => Enabled && transport.IsConnected;
        public static bool LoadoutHandshakeReady => IsConnected &&
            (IsHost ? hasRemoteGuestLoadout :
                (!IsClient || hasLocalGuestLoadout));
        public static bool ClientMapIsHostAuthoritative =>
            IsClientSession && Map.Current != null;
        public static bool IsSamplingLocalInput => samplingLocalInput;
        public static bool LocalPhysicalInputBlocked =>
            blockLocalInputWhenUnfocused &&
            (!Plugin.HasApplicationFocus || localPhysicalInputNeedsRearm);
        public static bool PreventLocalSave => IsClientSession ||
            originalClientContextCaptured ||
            HasCapturedClientSlotState();
        public static string TransportStatus => transport.Status;
        public static int PingMilliseconds => transport.PingMilliseconds;
        public static int EstimatedPacketLossPercent =>
            transport.EstimatedPacketLossPercent;
        public static string SessionNotice =>
            Time.realtimeSinceStartup <= sessionNoticeDeadline ?
                sessionNotice : string.Empty;
        public static bool SessionOverlayVisible => Enabled &&
            sessionHoldState != SessionHoldState.None;
        public static bool SceneTransitionActive => Enabled && sceneTransitionActive;
        internal static uint CurrentSceneEpoch => currentSceneEpoch;
        public static bool IsClientReadyForLoadGate(string sceneName)
        {
            if (IsClientSession && sceneTransitionActive &&
                sceneName == sceneTransitionTarget &&
                remoteSameSceneReloadTransitionId == sceneTransitionId &&
                (!remoteSceneLoadStarted ||
                remoteSceneLoadObservedTransitionId != sceneTransitionId))
                return false;
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

        public static void ContinueWaiting()
        {
            if (!SessionOverlayVisible || SessionIsResuming)
                return;
            sessionHoldStartedRealtime = Time.realtimeSinceStartup;
            Plugin.Log.LogMessage("[SessionHold] El jugador eligió seguir esperando.");
        }
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
            Plugin.Log.LogInfo("[Testing] RunInBackground: " +
                (enabled ? "ACTIVADO" : "DESACTIVADO") +
                " (Unity=" + Application.runInBackground + ").");
        }

        public static void SetBlockLocalInputWhenUnfocused(bool enabled)
        {
            blockLocalInputWhenUnfocused = enabled;
            if (!enabled)
            {
                localPhysicalInputNeedsRearm = false;
                localPhysicalInputRearmNotBeforeFrame = 0;
            }
            Plugin.Log.LogInfo("[Testing] Filtro de input local sin foco: " +
                (enabled ? "ACTIVADO" : "DESACTIVADO") + ".");
        }

        public static void OnApplicationFocusChanged(bool hasFocus)
        {
            if (!blockLocalInputWhenUnfocused)
                return;
            if (!hasFocus)
            {
                localPhysicalInputNeedsRearm = true;
                localPhysicalInputRearmNotBeforeFrame = int.MaxValue;
            }
            else if (localPhysicalInputNeedsRearm)
            {
                // Consume por completo el clic/tecla que devolvió el foco antes
                // de permitir que un menú o el motor de Player One lo vea.
                localPhysicalInputRearmNotBeforeFrame = Time.frameCount + 1;
            }
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

            // El invitado no puede continuar con una simulación autoritativa propia.
            // El host sí puede retirar a P2 y seguir jugando en solitario.
            BeginSessionStop(TransportDisconnectReason.Normal, true, IsClient);
        }

        private static void BeginSessionStop(TransportDisconnectReason reason,
            bool notifyPeer, bool returnToStart,
            bool flushTransportBeforeClose = false)
        {
            if (!Enabled)
                return;

            if (sessionStopPending)
            {
                pendingSessionStopReturnToStart |= returnToStart;
                return;
            }

            LevelLoadGate.Reset();
            sessionStopPending = true;
            pendingSessionStopReturnToStart = returnToStart;
            pendingSessionStopDeadlineRealtime = Time.realtimeSinceStartup +
                DisconnectGraceSeconds;
            pendingSessionStopFinalizeNotBeforeFrame = Time.frameCount +
                (flushTransportBeforeClose ? 1 : 0);

            if (notifyPeer)
            {
                try
                {
                    transport.RequestDisconnect(reason);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("[SessionSync] No se pudo enviar la " +
                        "despedida: " + ex.Message);
                    FinalizeSessionStop();
                    return;
                }
            }

            // Sin una conexión viva no hay ACK ni reintento que esperar. Cuando
            // acabamos de recibir una despedida, en cambio, conservamos un frame
            // adicional para que transportes con escritura encolada envíen el ACK.
            if (!flushTransportBeforeClose && !transport.IsConnected)
                FinalizeSessionStop();
        }

        private static void UpdatePendingSessionStop()
        {
            if (!sessionStopPending ||
                Time.frameCount < pendingSessionStopFinalizeNotBeforeFrame)
                return;

            if (transport.PeerDisconnected &&
                transport.PeerDisconnectReason ==
                    TransportDisconnectReason.RemovePlayer)
            {
                pendingSessionStopReturnToStart = true;
            }

            // UDP se vuelve terminal al recibir el ACK o al agotar su ráfaga de
            // reintentos. Relay/P2P también pueden cerrarse al recibir el ACK. El
            // deadline limita el cierre si el peer desapareció antes de responder.
            if (transport.IsConnected && !transport.PeerDisconnected &&
                Time.realtimeSinceStartup < pendingSessionStopDeadlineRealtime)
                return;

            FinalizeSessionStop();
        }

        private static void FinalizeSessionStop()
        {
            if (!Enabled)
                return;

            var shouldReturnToStart = pendingSessionStopReturnToStart;
            sessionStopPending = false;
            pendingSessionStopReturnToStart = false;
            pendingSessionStopDeadlineRealtime = 0f;
            pendingSessionStopFinalizeNotBeforeFrame = 0;

            internalPlayerLeave = true;
            try
            {
                TryLeavePlayerTwo();
            }
            finally
            {
                internalPlayerLeave = false;
            }
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

            if (shouldReturnToStart)
            {
                returnToStartAfterSession = true;
                returnToStartRetryNotBeforeRealtime = 0f;
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
            if (Enabled)
            {
                try
                {
                    transport.RequestDisconnect(TransportDisconnectReason.Normal);
                }
                catch { }
            }
            LevelLoadGate.Reset();
            RestoreClientContext();
            ResetSessionHoldState(true);
            Enabled = false;
            localPhysicalInputNeedsRearm = false;
            localPhysicalInputRearmNotBeforeFrame = 0;
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
            UpdateLocalPhysicalInputFocusGate();
            TryRestorePendingClientContext();
            TryReturnToStartAfterSession();

            if (!LocalPhysicalInputBlocked && Input.GetKeyDown(KeyCode.F8) && !Enabled)
                SetEnabled(true);

            if (!LocalPhysicalInputBlocked && Input.GetKeyDown(KeyCode.F7) && Enabled)
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
            if (sessionStopPending)
            {
                UpdatePendingSessionStop();
                return;
            }
            ProcessConnectionTransition();
            if (!Enabled || sessionStopPending)
                return;
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
                    if (!hasLocalGuestLoadout)
                        TryCaptureLocalGuestLoadout();
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
                        if ((sampled.Pressed & InputButtons.Super) != 0)
                        {
                            localPlayerTwoSuperRequestSequence++;
                            if (localPlayerTwoSuperRequestSequence == 0)
                                localPlayerTwoSuperRequestSequence = 1;
                            localPlayerTwoSuperDispatchQueue.Enqueue(
                                localPlayerTwoSuperRequestSequence);
                            localPlayerTwoSuperRequestAdvertiseDeadline =
                                Time.realtimeSinceStartup +
                                PlayerTwoSuperRequestAdvertiseSeconds;
                        }
                    }
                    sampled.PlayerTwoSuperRequestSequence =
                        Time.realtimeSinceStartup <=
                            localPlayerTwoSuperRequestAdvertiseDeadline ?
                            localPlayerTwoSuperRequestSequence : 0;
                    sampled.InputSessionNonce = localInputSessionNonce;
                    AttachLocalGuestLoadout(ref sampled);
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
                            " botones=" + (uint)sampled.Held +
                            "; fuente=Rewired Player One; respaldo fijo=apagado).");
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
                sampled.InputSessionNonce = localInputSessionNonce;
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
                    if (delivered.InputSessionNonce != 0 &&
                        delivered.InputSessionNonce != lastRemoteInputSessionNonce)
                        ResetRemoteInputEpoch(delivered.InputSessionNonce);
                    AcceptRemoteGuestLoadout(delivered);
                    if (delivered.PlayerTwoSuperRequestSequence != 0 &&
                        (!hasRemotePlayerTwoSuperRequestSequence ||
                        IsNewerTick(delivered.PlayerTwoSuperRequestSequence,
                            lastRemotePlayerTwoSuperRequestSequence)))
                    {
                        lastRemotePlayerTwoSuperRequestSequence =
                            delivered.PlayerTwoSuperRequestSequence;
                        hasRemotePlayerTwoSuperRequestSequence = true;
                        hostPendingPlayerTwoSuperRequests.Enqueue(
                            new PlayerTwoSuperRequest(
                                delivered.PlayerTwoSuperRequestSequence));
                        // La secuencia permanece en frames posteriores, así el
                        // host recupera el tap aunque el paquete del borde se pierda.
                        Plugin.Log.LogMessage("[InputSync] Solicitud fiable de " +
                            "EX/Super P2 recibida (#" +
                            delivered.PlayerTwoSuperRequestSequence + ").");
                    }
                    // En una sesión host, el pulso fiable de EX/Super sale sólo
                    // de la cola causal anterior. El borde UDP no puede adelantar
                    // ni duplicar una solicitud.
                    if (IsHost)
                        delivered.Pressed &= ~InputButtons.Super;
                    pressed |= delivered.Pressed;
                    released |= delivered.Released;
                    received = delivered;
                    deliveredAny = true;
                }
                if (deliveredAny)
                {
                    remoteInputNeutralizedForStall = false;
                    received.Pressed = pressed;
                    received.Released = released;
                    QueuePlayerTwoFixedEdges(pressed, released);
                    ReportPlayerTwoCombatButtons("recibió del invitado", received);
                    if (IsHost && Map.Current != null &&
                        (pressed & InputButtons.Accept) != 0)
                    {
                        var holdSeconds = Mathf.Clamp(
                            transport.PingMilliseconds * 0.001f + 0.1f,
                            0.25f, 0.75f);
                        remoteMapAcceptDeadline = Time.realtimeSinceStartup +
                            holdSeconds;
                    }
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

            NeutralizeStaleRemoteInput();
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

            var gameplayInputLocked = IsGameplayInputLocked();
            AdvanceHostPlayerTwoSuperRequestDeadline(gameplayInputLocked);

            playerTwoFixedPressed = playerTwoPendingPressed;
            playerTwoFixedReleased = playerTwoPendingReleased;
            localPlayerTwoSuperDispatchedForFixed = 0;
            playerTwoConfirmedSuperDispatchedForFixed = 0;
            if (IsClientSession || IsHostSession)
                playerTwoFixedPressed &= ~InputButtons.Super;
            if (!gameplayInputLocked && IsClientSession)
            {
                if (playerTwoConfirmedSuperQueue.Count != 0)
                {
                    playerTwoConfirmedSuperDispatchedForFixed =
                        playerTwoConfirmedSuperQueue.Peek();
                    playerTwoFixedPressed |= InputButtons.Super;
                    remotePlayerTwoSuperDeferralDeadline =
                        Time.realtimeSinceStartup +
                        RemotePlayerTwoSuperDeferralSeconds;
                }
                else if (localPlayerTwoSuperDispatchQueue.Count != 0)
                {
                    localPlayerTwoSuperDispatchedForFixed =
                        localPlayerTwoSuperDispatchQueue.Dequeue();
                    playerTwoFixedPressed |= InputButtons.Super;
                }
            }
            playerTwoPendingPressed = InputButtons.None;
            playerTwoPendingReleased = InputButtons.None;

            remotePlayerOneFixedPressed = remotePlayerOnePendingPressed;
            remotePlayerOneFixedReleased = remotePlayerOnePendingReleased;
            remotePlayerOnePendingPressed = InputButtons.None;
            remotePlayerOnePendingReleased = InputButtons.None;
            if (gameplayInputLocked &&
                (remotePlayerOneFixedPressed & InputButtons.Super) != 0)
            {
                remotePlayerOneFixedPressed &= ~InputButtons.Super;
                remotePlayerOnePendingPressed |= InputButtons.Super;
                remotePlayerOneSuperDeferralDeadline =
                    Time.realtimeSinceStartup +
                    RemotePlayerOneSuperDeferralSeconds;
            }
            else if ((remotePlayerOneFixedPressed & InputButtons.Super) != 0)
            {
                remotePlayerOneSuperDeferralDeadline =
                    Time.realtimeSinceStartup +
                    RemotePlayerOneSuperDeferralSeconds;
            }
        }

        private static void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                RestoreClientContext();
                ResetSessionHoldState(true);
            }
            Enabled = enabled;
            ResetSessionLoadouts();
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
            loadoutHealthAgreementReported = false;
            loadoutHealthMismatchReported = false;
            hostMapAxesReported = false;
            remoteMapAxesReported = false;
            playerOneVisualReported = false;
            p2PredictionIsolationReported = false;
            ResetPlayerTwoStallFallback();
            superButtonReported = false;
            lockButtonReported = false;
            hostPlayerOneSuperActionSequence = 0;
            hostPlayerTwoSuperActionSequence = 0;
            lastRemotePlayerOneSuperActionSequence = 0;
            hasRemotePlayerOneSuperActionSequence = false;
            lastRemotePlayerTwoSuperActionSequence = 0;
            hasRemotePlayerTwoSuperActionSequence = false;
            localPlayerTwoSuperRequestSequence = 0;
            localPlayerTwoSuperRequestAdvertiseDeadline = 0f;
            localInputSessionNonce = CreateInputSessionNonce();
            lastRemoteInputSessionNonce = 0;
            localStateSessionNonce = CreateInputSessionNonce();
            lastRemoteStateSessionNonce = 0;
            lastRemotePlayerTwoSuperRequestSequence = 0;
            hasRemotePlayerTwoSuperRequestSequence = false;
            ClearPlayerTwoSuperRequestQueues();
            remotePlayerOneSuperDeferralDeadline = 0f;
            remotePlayerTwoSuperDeferralDeadline = 0f;
            remoteMapAcceptDeadline = 0f;
            mapLevelInteractionInputProbeActive = false;
            playerTwoMapNeutralSinceRealtime = 0f;
            lastRemoteInputRealtime = 0f;
            hasRemoteInputActivity = false;
            remoteInputNeutralizedForStall = false;
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
            remoteSceneLoadObservedTransitionId = 0;
            remoteSameSceneReloadTransitionId = 0;
            ClearDeferredRemoteSceneTransition();
            backgroundSettingRepairReported = false;
            if (enabled)
            {
                returnToMapAfterAbortedLoad = false;
                ResetSessionHoldState(false);
            }
            if (enabled && IsClient)
            {
                CaptureOriginalClientContext();
                TryCaptureLocalGuestLoadout();
            }
            Plugin.Log.LogMessage("Remote Input Lab " + (Enabled ? "ACTIVADO" : "DESACTIVADO") +
                (Enabled ? " (" + transport.Description + ")." : "."));
        }

        private static void NeutralizeStaleRemoteInput()
        {
            if (!IsHost || !hasRemoteInputActivity ||
                remoteInputNeutralizedForStall ||
                Time.realtimeSinceStartup - lastRemoteInputRealtime <
                    RemoteInputNeutralizeSeconds)
                return;

            var held = received.Held;
            received.Horizontal = 0;
            received.Vertical = 0;
            received.Held = InputButtons.None;
            received.Pressed = InputButtons.None;
            received.Released |= held;
            QueuePlayerTwoFixedEdges(InputButtons.None, held);
            remoteInputNeutralizedForStall = true;
            Plugin.Log.LogWarning("[InputSync] P2 neutralizado tras " +
                RemoteInputNeutralizeSeconds.ToString("0.0") +
                " s sin frames; la sesión seguirá activa hasta el umbral de espera.");
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
            var durablePlayerOneSuper =
                (remotePlayerOnePendingPressed | remotePlayerOneFixedPressed) &
                InputButtons.Super;
            ResetInputEdgeLatches();
            remotePlayerOnePendingPressed |= durablePlayerOneSuper;
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
                    // Adelanta el primer envío de la cancelación antes de iniciar la
                    // despedida fiable de la sesión.
                    transport.Update();
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[ReadyGate] No se pudo avisar la cancelación: " +
                    ex.Message);
            }
            BeginSessionStop(TransportDisconnectReason.Normal, true, false);
            returnToMapAfterAbortedLoad = shouldReturnToMap;
        }

        private static void TryReturnToMapAfterAbortedLoad()
        {
            if (!returnToMapAfterAbortedLoad || sessionStopPending ||
                SceneLoader.CurrentlyLoading || PreventLocalSave)
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
            if (!Enabled || !transport.IsConnected ||
                (IsHost && !hasRemoteGuestLoadout))
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
            if (samplingLocalInput || player == null)
                return false;

            bool isPlayerOne;
            try
            {
                var expected = PlayerManager.GetPlayerInput(PlayerId.PlayerOne);
                isPlayerOne = object.ReferenceEquals(player, expected) ||
                    (expected == null && player.id == 0);
            }
            catch
            {
                isPlayerOne = player.id == 0;
            }

            if (!isPlayerOne)
                return false;
            if (LocalPhysicalInputBlocked)
                return true;
            if (!IsClientSession || !transport.IsConnected)
                return false;
            return Map.Current != null || Level.Current != null || HasPlayerTwoActor();
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
            if (!CanDriveRemotePlayerOneVisual() ||
                ClientMapIsHostAuthoritative)
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

            if (input.playerId != PlayerId.PlayerOne)
                return false;
            if (CanDriveRemotePlayerOneVisual())
            {
                horizontal = remotePlayerOneInput.Horizontal / 127f;
                vertical = remotePlayerOneInput.Vertical / 127f;
                return true;
            }
            return LocalPhysicalInputBlocked;
        }

        public static bool TryGetPlayerInputButton(PlayerInput input, CupheadButton button,
            out bool value)
        {
            value = false;
            if (input == null || samplingLocalInput)
                return false;
            if (ClientMapIsHostAuthoritative &&
                (input.playerId == PlayerId.PlayerOne ||
                    input.playerId == PlayerId.PlayerTwo))
                return true;
            if (input.playerId == PlayerId.PlayerTwo && DrivesPlayerTwo)
            {
                value = GetButton((int)button, ButtonPhase.Held);
                ReportRewiredRead();
                return true;
            }
            if (input.playerId != PlayerId.PlayerOne)
                return false;
            if (CanDriveRemotePlayerOneVisual())
            {
                value = GetRemotePlayerOneButton((int)button, ButtonPhase.Held);
                return true;
            }
            return LocalPhysicalInputBlocked;
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

        internal static bool ShouldDeferRemotePlayerSuperMeter(PlayerId id,
            float currentMeter, float authoritativeMeter)
        {
            var deadline = id == PlayerId.PlayerOne ?
                remotePlayerOneSuperDeferralDeadline :
                remotePlayerTwoSuperDeferralDeadline;
            var durablePlayerTwoConfirmation = id == PlayerId.PlayerTwo &&
                playerTwoConfirmedSuperQueue.Count != 0;
            return IsClientSession && transport.IsConnected &&
                authoritativeMeter < currentMeter &&
                (durablePlayerTwoConfirmation ||
                    (deadline > 0f &&
                    Time.realtimeSinceStartup <= deadline));
        }

        internal static void NotifySuperConsumed(PlayerStatsManager stats,
            float meterBefore, bool fullSuper)
        {
            if (!Enabled || stats == null)
                return;
            try
            {
                AbstractPlayerController playerOne = null;
                AbstractPlayerController playerTwo = null;
                try { playerOne = PlayerManager.GetPlayer(PlayerId.PlayerOne); }
                catch { }
                try { playerTwo = PlayerManager.GetPlayer(PlayerId.PlayerTwo); }
                catch { }
                var isPlayerOne = playerOne != null &&
                    object.ReferenceEquals(playerOne.stats, stats);
                var isPlayerTwo = playerTwo != null &&
                    object.ReferenceEquals(playerTwo.stats, stats);
                if (!isPlayerOne && !isPlayerTwo)
                    return;
                // OnEx también retorna temprano con ciertos charms. No se debe
                // confirmar una carta que Cuphead realmente no descontó.
                if (!fullSuper && stats.SuperMeter >= meterBefore - 0.01f)
                    return;
                if (IsHostSession && transport.IsConnected)
                {
                    if (isPlayerOne)
                    {
                        hostPlayerOneSuperActionSequence++;
                        if (hostPlayerOneSuperActionSequence == 0)
                            hostPlayerOneSuperActionSequence = 1;
                    }
                    else
                    {
                        if (hostOfferedPlayerTwoSuperRequestSequence == 0 ||
                            hostPendingPlayerTwoSuperRequests.Count == 0 ||
                            hostPendingPlayerTwoSuperRequests.Peek().Sequence !=
                                hostOfferedPlayerTwoSuperRequestSequence)
                            return;
                        var confirmedRequest =
                            hostPendingPlayerTwoSuperRequests.Dequeue();
                        hostPlayerTwoSuperActionSequence =
                            confirmedRequest.Sequence;
                        hostOfferedPlayerTwoSuperRequestSequence = 0;
                        Plugin.Log.LogMessage("[InputSync] El host confirmó " +
                            "EX/Super P2 (#" +
                            hostPlayerTwoSuperActionSequence + ").");
                    }
                    return;
                }
                if (!IsClientSession)
                    return;
                if (isPlayerOne)
                {
                    if (remotePlayerOneSuperDeferralDeadline <= 0f)
                        return;
                    remotePlayerOneSuperDeferralDeadline = 0f;
                    Plugin.Log.LogInfo("[StateSync] El EX/Super remoto consumió " +
                        "su carta visual; se reanuda el medidor autoritativo.");
                    return;
                }

                if (playerTwoConfirmedSuperDispatchedForFixed != 0 &&
                    playerTwoConfirmedSuperQueue.Count != 0 &&
                    playerTwoConfirmedSuperQueue.Peek() ==
                        playerTwoConfirmedSuperDispatchedForFixed)
                {
                    playerTwoConfirmedSuperQueue.Dequeue();
                    playerTwoConfirmedSuperDispatchedForFixed = 0;
                    remotePlayerTwoSuperDeferralDeadline = 0f;
                    Plugin.Log.LogInfo("[StateSync] La confirmación del host " +
                        "reprodujo el EX/Super P2 en el invitado.");
                }
                else if (localPlayerTwoSuperDispatchedForFixed != 0)
                {
                    localPredictedPlayerTwoSuperRequests.Add(
                        localPlayerTwoSuperDispatchedForFixed);
                    localPlayerTwoSuperDispatchedForFixed = 0;
                }
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
            else if (!connected && transport.PeerDisconnected)
            {
                var reason = transport.PeerDisconnectReason;
                var wasHost = IsHost;
                Plugin.Log.LogMessage("[SessionSync] Despedida explícita recibida (" +
                    reason + ").");
                if (reason == TransportDisconnectReason.RemovePlayer)
                {
                    ShowSessionNotice("PLAYER TWO FUE REMOVIDO. REGRESANDO AL INICIO.");
                    BeginSessionStop(reason, false, true, true);
                }
                else if (wasHost)
                {
                    ShowSessionNotice("EL INVITADO SE DESCONECTÓ. " +
                        "LA PARTIDA CONTINÚA EN SOLITARIO.");
                    BeginSessionStop(reason, false, false, true);
                }
                else
                {
                    ShowSessionNotice("EL ANFITRIÓN CERRÓ LA SESIÓN. " +
                        "REGRESANDO AL INICIO.");
                    BeginSessionStop(reason, false, true, true);
                }
                return;
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
                ResetInputEpochForReconnect();
                CancelSceneTransition("La conexión se interrumpió durante el cambio de escena.");
                if (interruptedLevelLoad)
                {
                    BeginSessionStop(TransportDisconnectReason.Normal,
                        false, false);
                    returnToMapAfterAbortedLoad = true;
                }
            }
            transportWasConnected = connected;
        }

        internal static bool InterceptPlayerTwoRemoval(PlayerId player)
        {
            if (player != PlayerId.PlayerTwo || !Enabled || internalPlayerLeave)
                return false;

            Plugin.Log.LogWarning("[SessionSync] Remover Player Two cierra la " +
                "sesión completa para evitar que vuelva a aparecer.");
            ShowSessionNotice("PLAYER TWO FUE REMOVIDO. REGRESANDO AL INICIO.");
            BeginSessionStop(TransportDisconnectReason.RemovePlayer, true, true);
            return true;
        }

        private static void ShowSessionNotice(string message)
        {
            sessionNotice = message ?? string.Empty;
            sessionNoticeDeadline = Time.realtimeSinceStartup + 5f;
        }

        private static void TryReturnToStartAfterSession()
        {
            if (!returnToStartAfterSession || SceneLoader.CurrentlyLoading)
                return;

            var now = Time.realtimeSinceStartup;
            if (now < returnToStartRetryNotBeforeRealtime)
                return;

            if (PreventLocalSave)
            {
                RestoreClientContext();
                if (PreventLocalSave)
                {
                    returnToStartRetryNotBeforeRealtime = now +
                        ReturnToStartRetrySeconds;
                    return;
                }
            }

            try
            {
                if (GoToStartScreenMethod == null)
                    throw new System.MissingMethodException(
                        "PlayerManager.goToStartScreen");
                GoToStartScreenMethod.Invoke(null, null);
                returnToStartAfterSession = false;
                returnToStartRetryNotBeforeRealtime = 0f;
                Plugin.Log.LogMessage("[SessionSync] Ambos juegos regresan al inicio.");
            }
            catch (System.Exception ex)
            {
                returnToStartRetryNotBeforeRealtime = now +
                    ReturnToStartRetrySeconds;
                var inner = ex.InnerException == null ? ex : ex.InnerException;
                Plugin.Log.LogWarning("[SessionSync] No se pudo volver al inicio; " +
                    "se reintentará: " + inner.Message);
            }
        }

        private static void TryRestorePendingClientContext()
        {
            if (IsClientSession || !PreventLocalSave)
                return;
            var now = Time.realtimeSinceStartup;
            if (now < clientRestoreRetryNotBeforeRealtime)
                return;
            clientRestoreRetryNotBeforeRealtime = now +
                ReturnToStartRetrySeconds;
            RestoreClientContext();
            if (!PreventLocalSave)
                clientRestoreRetryNotBeforeRealtime = 0f;
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
            remoteSceneLoadObservedTransitionId = 0;
            remoteSameSceneReloadTransitionId = 0;
            remoteMapAcceptDeadline = 0f;
            mapLevelInteractionInputProbeActive = false;
            playerTwoMapNeutralSinceRealtime = 0f;
            localPlayerTwoSuperRequestAdvertiseDeadline = 0f;
            if (sceneName.StartsWith("scene_map_"))
                mapBootstrapCompleted = false;
            levelPlayerOneBootstrapCompleted = false;
            loadoutHealthAgreementReported = false;
            loadoutHealthMismatchReported = false;
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
            if (IsClient &&
                remoteSameSceneReloadTransitionId == sceneTransitionId &&
                (!remoteSceneLoadStarted ||
                remoteSceneLoadObservedTransitionId != sceneTransitionId))
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
            remoteSceneLoadObservedTransitionId = 0;
            remoteSameSceneReloadTransitionId = 0;
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
            {
                Level.SetCurrentMode((Level.Mode)sceneTransitionDifficulty);
                if (Enabled && IsClient && remoteSceneLoadStarted)
                {
                    remoteSceneLoadObservedTransitionId = sceneTransitionId;
                    Plugin.Log.LogMessage("[SceneSync] Nueva generación observada para " +
                        sceneName + " en transición #" + sceneTransitionId + ".");
                }
            }
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
                        (hasPendingRemoteScene &&
                            pendingRemoteScene.SceneName == command.SceneName) ||
                        blockedClientSceneRequest == command.SceneName ||
                        SceneManager.GetActiveScene().name == command.SceneName;
                    if (!cancellationMatches)
                        continue;
                    var shouldReturnToMap = command.SceneName.StartsWith("scene_level_") &&
                        (remoteSceneLoadStarted || SceneLoader.CurrentlyLoading ||
                            SceneManager.GetActiveScene().name == command.SceneName);
                    CancelSceneTransition("El host canceló la carga coordinada.");
                    BeginSessionStop(TransportDisconnectReason.Normal,
                        true, false);
                    if (shouldReturnToMap)
                        returnToMapAfterAbortedLoad = true;
                    continue;
                }
                if (!IsStableScene(command.SceneName, (LoadSceneMode)command.LoadMode) ||
                    command.Difficulty > 2)
                    continue;
                var targetAlreadyActive =
                    SceneManager.GetActiveScene().name == command.SceneName;
                var startsNewTransition = command.IsCoordinatedTransition &&
                    (!sceneTransitionActive || sceneTransitionId != command.Sequence);
                var supersedesActiveLoader = startsNewTransition &&
                    sceneTransitionActive &&
                    (remoteSceneLoadStarted || SceneLoader.CurrentlyLoading);
                Level.SetCurrentMode((Level.Mode)command.Difficulty);
                if (supersedesActiveLoader)
                {
                    var supersededTransitionId = sceneTransitionId;
                    var supersededTarget = sceneTransitionTarget;
                    LevelLoadGate.ReleaseAndResetForSupersedingTransition(
                        supersededTransitionId);
                    if (deferredRemoteSceneTransitionId != command.Sequence)
                    {
                        deferredRemoteSceneReceivedRealtime =
                            Time.realtimeSinceStartup;
                        deferredRemoteSceneLoaderIdleFrame = -1;
                    }
                    deferredRemoteSceneTransitionId = command.Sequence;
                    deferredRemoteSceneRequiresReload = targetAlreadyActive ||
                        supersededTarget == command.SceneName;
                    Plugin.Log.LogWarning("[SceneSync] Transición #" +
                        command.Sequence + " quedó diferida hasta que el loader de #" +
                        supersededTransitionId + " termine por completo.");
                }
                else if (startsNewTransition)
                {
                    ClearDeferredRemoteSceneTransition();
                    BeginSceneTransition(command.SceneName, command.LevelId,
                        command.Sequence, command.Difficulty, true);
                    if (targetAlreadyActive)
                    {
                        remoteSameSceneReloadTransitionId = command.Sequence;
                        Plugin.Log.LogMessage("[SceneSync] Recarga coordinada de " +
                            command.SceneName + " requerida para transición #" +
                            command.Sequence + ".");
                    }
                }
                else if (!targetAlreadyActive && !command.IsCoordinatedTransition)
                    BeginSceneTransition(command.SceneName, command.LevelId,
                        command.Sequence, command.Difficulty, false);
                if (blockedClientSceneRequest == command.SceneName)
                {
                    blockedClientSceneRequest = string.Empty;
                    blockedClientSceneRequestRealtime = 0f;
                }
                pendingRemoteScene = command;
                hasPendingRemoteScene = true;
                if (!HasMatchingSessionContext(command))
                    Plugin.Log.LogMessage("[SceneSync] Esperando el contexto #" +
                        command.Sequence + " del host antes de cargar " +
                        command.SceneName + ".");
            }

            if (!hasPendingRemoteScene)
                return;
            if (deferredRemoteSceneTransitionId == pendingRemoteScene.Sequence)
            {
                if (SceneLoader.CurrentlyLoading)
                {
                    deferredRemoteSceneLoaderIdleFrame = -1;
                    return;
                }

                // El plugin corre antes que varios callbacks de Unity. Exigimos un
                // frame completo con el loader anterior inactivo para que ningún
                // callback tardío de A pueda acreditar la generación B.
                if (deferredRemoteSceneLoaderIdleFrame < 0)
                {
                    deferredRemoteSceneLoaderIdleFrame = Time.frameCount;
                    return;
                }
                if (Time.frameCount <= deferredRemoteSceneLoaderIdleFrame)
                    return;

                var forceReload = deferredRemoteSceneRequiresReload;
                var deferredCommand = pendingRemoteScene;
                ClearDeferredRemoteSceneTransition();
                BeginSceneTransition(deferredCommand.SceneName,
                    deferredCommand.LevelId, deferredCommand.Sequence,
                    deferredCommand.Difficulty, true);
                if (forceReload || SceneManager.GetActiveScene().name ==
                    deferredCommand.SceneName)
                    remoteSameSceneReloadTransitionId = deferredCommand.Sequence;
                Plugin.Log.LogMessage("[SceneSync] Loader anterior liberado; " +
                    "comienza la generación real #" + deferredCommand.Sequence +
                    " para " + deferredCommand.SceneName + ".");
            }
            var pendingTargetAlreadyActive =
                SceneManager.GetActiveScene().name == pendingRemoteScene.SceneName;
            var pendingRequiresSameSceneReload =
                pendingRemoteScene.IsCoordinatedTransition &&
                remoteSameSceneReloadTransitionId == pendingRemoteScene.Sequence;
            var pendingGenerationObserved =
                remoteSceneLoadObservedTransitionId == pendingRemoteScene.Sequence;
            if (pendingTargetAlreadyActive &&
                (!pendingRequiresSameSceneReload || pendingGenerationObserved))
            {
                Plugin.Log.LogMessage("[SceneSync] Escena remota sincronizada: " +
                    pendingRemoteScene.SceneName + ".");
                hasPendingRemoteScene = false;
                return;
            }
            if (!HasMatchingSessionContext(pendingRemoteScene))
                return;
            if (!SceneLoader.Exists || SceneLoader.CurrentlyLoading)
                return;

            ApplySceneCommand(pendingRemoteScene);
        }

        private static void ApplySceneCommand(SceneCommand command)
        {
            var targetAlreadyActive =
                SceneManager.GetActiveScene().name == command.SceneName;
            var requiresSameSceneReload = command.IsCoordinatedTransition &&
                remoteSameSceneReloadTransitionId == command.Sequence;
            if ((targetAlreadyActive && !requiresSameSceneReload) ||
                remoteSceneLoadStarted)
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

        private static bool HasMatchingSessionContext(SceneCommand command)
        {
            if (!RequiresSessionContext(command.SceneName))
                return true;
            if (!hasRemoteContext)
                return false;
            return !command.IsCoordinatedTransition ||
                latestRemoteContext.LoadTransitionId == command.Sequence;
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
                    remoteSceneLoadStarted = true;
                    SceneLoader.LoadLevel(
                        (Levels)command.LevelId,
                        SceneLoader.Transition.Fade,
                        SceneLoader.Icon.Hourglass,
                        null);
                    Plugin.Log.LogMessage("[SceneSync] Cuphead cargando nivel remoto " +
                        command.LevelId + " en dificultad " + command.Difficulty +
                        " (transición #" + command.Sequence + ").");
                    return;
                }

                if (!System.Enum.IsDefined(typeof(Scenes), sceneName))
                    throw new System.ArgumentException("Escena desconocida: " + sceneName);
                var scene = (Scenes)System.Enum.Parse(typeof(Scenes), sceneName);

                remoteSceneLoadStarted = true;
                SceneLoader.LoadScene(
                    scene,
                    SceneLoader.Transition.Fade,
                    SceneLoader.Transition.Fade,
                    SceneLoader.Icon.Hourglass,
                    null);
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
            if (!IsClient || !hasPendingRemoteScene)
                return;

            var pendingTransitionId = pendingRemoteScene.Sequence;
            if (pendingTransitionId != 0 &&
                remoteSceneLoadObservedTransitionId == pendingTransitionId)
                return;

            float watchdogStartedRealtime;
            if (deferredRemoteSceneTransitionId == pendingTransitionId)
                watchdogStartedRealtime = deferredRemoteSceneReceivedRealtime;
            else if (sceneTransitionActive &&
                sceneTransitionId == pendingTransitionId)
                watchdogStartedRealtime = sceneTransitionStartedRealtime;
            else
                return;
            if (watchdogStartedRealtime <= 0f ||
                Time.realtimeSinceStartup - watchdogStartedRealtime <
                    PendingSceneCommandTimeoutSeconds)
                return;

            CancelSceneTransition("No se observó la generación de escena indicada " +
                "por el host antes del timeout.");
        }

        private static void ClearDeferredRemoteSceneTransition()
        {
            deferredRemoteSceneTransitionId = 0;
            deferredRemoteSceneRequiresReload = false;
            deferredRemoteSceneReceivedRealtime = 0f;
            deferredRemoteSceneLoaderIdleFrame = -1;
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
            remoteSceneLoadObservedTransitionId = 0;
            remoteSameSceneReloadTransitionId = 0;
            ClearDeferredRemoteSceneTransition();
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
                var playerOneLoadout = default(PlayerLoadoutSnapshot);
                var playerTwoLoadout = default(PlayerLoadoutSnapshot);
                if (hasActiveSave)
                {
                    if (data.Loadouts == null || data.Loadouts.playerOne == null ||
                        data.Loadouts.playerTwo == null)
                        throw new System.InvalidOperationException(
                            "El save activo no contiene loadouts.");
                    playerOneLoadout = CaptureLoadout(data.Loadouts.playerOne);
                    playerTwoLoadout = hostPlayerTwoLoadoutOverlay != null ?
                        CaptureLoadout(hostPlayerTwoLoadoutOverlay) :
                        CaptureLoadout(data.Loadouts.playerTwo);
                    if (!IsValidLoadout(playerOneLoadout) ||
                        !IsValidLoadout(playerTwoLoadout))
                        throw new System.InvalidOperationException(
                            "El save activo contiene un loadout desconocido.");
                }
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
                    GuestLoadoutRevision = hasRemoteGuestLoadout ?
                        remoteGuestLoadoutRevision : 0,
                    PlayerOneLoadout = playerOneLoadout,
                    PlayerTwoLoadout = playerTwoLoadout,
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
                    context.ResumeSeconds + " loadoutGuest=" +
                    context.GuestLoadoutRevision);
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
            {
                CaptureOriginalClientContext();
                if (!originalClientContextCaptured)
                    return;
            }

            SessionContext context;
            while (transport.TryReceiveContext(out context))
            {
                if (!hasLocalGuestLoadout)
                    TryCaptureLocalGuestLoadout();
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
                    CaptureClientSlotState(context.SaveSlot);
                    var data = PlayerData.GetDataForSlot(context.SaveSlot);
                    if (!ApplyClientSessionLoadouts(context))
                        continue;
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
                originalClientDialoguerState = Dialoguer.GetGlobalVariablesState();
                originalClientContextCaptured = true;
            }
            catch
            {
                // El frontend todavía puede estar inicializando PlayerData.
            }
        }

        private static void RestoreClientContext()
        {
            if (!originalClientContextCaptured &&
                !HasCapturedClientSlotState())
                return;

            RestoreClientSaveData();
            if (originalClientContextCaptured)
            {
                try
                {
                    PlayerData.CurrentSaveFileIndex = originalClientSaveSlot;
                    PlayerManager.player1IsMugman = originalClientPlayerOneIsMugman;
                    PlayerData.GetDataForSlot(originalClientSaveSlot).isPlayer1Mugman =
                        originalClientSavePlayerOneIsMugman;
                    Level.SetCurrentMode(originalClientDifficulty);
                    PlayerData.inGame = originalClientInGame;
                    Dialoguer.SetGlobalVariablesState(originalClientDialoguerState);
                    originalClientDialoguerState = null;
                    originalClientContextCaptured = false;
                }
                catch (System.Exception ex)
                {
                    // Conserva el snapshot para volver a intentarlo durante shutdown.
                    Plugin.Log.LogWarning("[SaveSync] No se pudo restaurar el " +
                        "contexto global del invitado: " + ex.Message);
                }
            }
        }

        private static void CaptureClientSlotState(int slot)
        {
            if (slot < 0 || slot >= clientSlotStateCaptured.Length ||
                clientSlotStateCaptured[slot])
                return;

            var data = PlayerData.GetDataForSlot(slot);
            if (data == null)
                throw new System.InvalidOperationException(
                    "No existe el slot local que se iba a prestar.");
            var json = JsonUtility.ToJson(data);
            if (string.IsNullOrEmpty(json))
                throw new System.InvalidOperationException(
                    "No se pudo respaldar el slot local antes de la sesión.");
            clientSlotOriginalJson[slot] = json;
            clientSlotStateCaptured[slot] = true;
            Plugin.Log.LogInfo("[SaveSync] Slot local " + (slot + 1) +
                " respaldado en memoria para restaurarlo al salir.");
        }

        private static void RestoreClientSaveData()
        {
            for (var slot = 0; slot < clientSlotStateCaptured.Length; slot++)
            {
                if (!clientSlotStateCaptured[slot])
                    continue;
                try
                {
                    var data = PlayerData.GetDataForSlot(slot);
                    if (data == null)
                        throw new System.InvalidOperationException(
                            "El slot dejó de estar disponible.");
                    JsonUtility.FromJsonOverwrite(clientSlotOriginalJson[slot], data);
                    clientSlotStateCaptured[slot] = false;
                    clientSlotOriginalJson[slot] = null;
                    Plugin.Log.LogMessage("[SaveSync] Slot local " + (slot + 1) +
                        " restaurado; el progreso remoto no quedó en memoria.");
                }
                catch (System.Exception ex)
                {
                    // No borra el snapshot: OnDestroy volverá a intentar restaurarlo.
                    Plugin.Log.LogWarning("[SaveSync] No se pudo restaurar el slot " +
                        (slot + 1) + ": " + ex.Message);
                }
            }
        }

        private static bool HasCapturedClientSlotState()
        {
            for (var slot = 0; slot < clientSlotStateCaptured.Length; slot++)
                if (clientSlotStateCaptured[slot])
                    return true;
            return false;
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
                left.LoadTransitionId == right.LoadTransitionId &&
                left.GuestLoadoutRevision == right.GuestLoadoutRevision &&
                left.PlayerOneLoadout.SameAs(right.PlayerOneLoadout) &&
                left.PlayerTwoLoadout.SameAs(right.PlayerTwoLoadout);
        }

        private static void TryCaptureLocalGuestLoadout()
        {
            if (!IsClient || hasLocalGuestLoadout)
                return;

            try
            {
                var slot = originalClientContextCaptured ?
                    originalClientSaveSlot : PlayerData.CurrentSaveFileIndex;
                slot = Mathf.Clamp(slot, 0, 2);
                var data = PlayerData.GetDataForSlot(slot);
                var source = data == null || data.Loadouts == null ? null :
                    data.Loadouts.playerOne;
                if (source == null)
                    return;

                var snapshot = CaptureLoadout(source);
                if (!IsValidLoadout(snapshot))
                    return;

                localGuestLoadout = snapshot;
                localGuestLoadoutRevision = 1;
                hasLocalGuestLoadout = true;
                Plugin.Log.LogMessage("[LoadoutSync] El invitado ofrece su " +
                    "equipamiento local P1 como P2 (slot local=" +
                    (slot + 1) + ", arma=" + snapshot.PrimaryWeapon +
                    ", charm=" + snapshot.Charm + ").");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug("[LoadoutSync] El equipamiento local aún " +
                    "no está disponible: " + ex.Message);
            }
        }

        private static void AttachLocalGuestLoadout(ref InputFrame frame)
        {
            if (!hasLocalGuestLoadout)
                return;
            frame.GuestLoadoutRevision = localGuestLoadoutRevision;
            frame.GuestLoadout = localGuestLoadout;
        }

        private static void AcceptRemoteGuestLoadout(InputFrame frame)
        {
            if (!IsHost || frame.GuestLoadoutRevision == 0 ||
                !IsValidLoadout(frame.GuestLoadout))
                return;

            if (hasRemoteGuestLoadout)
            {
                if (frame.GuestLoadoutRevision == remoteGuestLoadoutRevision)
                    return;
                if (!IsNewerTick(frame.GuestLoadoutRevision,
                    remoteGuestLoadoutRevision))
                    return;
            }
            else if (remoteGuestLoadoutRevision != 0)
            {
                if (frame.GuestLoadoutRevision == remoteGuestLoadoutRevision)
                {
                    // Una época nueva vuelve a confirmar el snapshot negociado,
                    // pero no debe reemplazar el mismo objeto: Cuphead puede haber
                    // ajustado ahí el super de Chalice o cambios hechos en sesión.
                    hasRemoteGuestLoadout = true;
                    if (!frame.GuestLoadout.SameAs(remoteGuestLoadout))
                    {
                        remoteGuestLoadout = frame.GuestLoadout;
                        UpdateLoadoutOverlay(ref hostPlayerTwoLoadoutOverlay,
                            remoteGuestLoadout);
                    }
                    Plugin.Log.LogMessage("[LoadoutSync] El invitado revalidó " +
                        "el equipamiento P2 para la nueva época de input.");
                    CaptureAndSendContext(true);
                    return;
                }
                if (!IsNewerTick(frame.GuestLoadoutRevision,
                    remoteGuestLoadoutRevision))
                    return;
            }

            remoteGuestLoadout = frame.GuestLoadout;
            remoteGuestLoadoutRevision = frame.GuestLoadoutRevision;
            hasRemoteGuestLoadout = true;
            UpdateLoadoutOverlay(ref hostPlayerTwoLoadoutOverlay,
                remoteGuestLoadout);
            Plugin.Log.LogMessage("[LoadoutSync] El host aceptó el equipamiento " +
                "del invitado para P2 (revisión=" +
                remoteGuestLoadoutRevision + ", arma=" +
                remoteGuestLoadout.PrimaryWeapon + ", charm=" +
                remoteGuestLoadout.Charm + ").");
            CaptureAndSendContext(true);
        }

        private static PlayerLoadoutSnapshot CaptureLoadout(
            PlayerData.PlayerLoadouts.PlayerLoadout loadout)
        {
            var flags = PlayerLoadoutFlags.None;
            if (loadout.HasEquippedSecondaryRegularWeapon)
                flags |= PlayerLoadoutFlags.HasEquippedSecondaryRegularWeapon;
            if (loadout.HasEquippedSecondarySHMUPWeapon)
                flags |= PlayerLoadoutFlags.HasEquippedSecondaryShmupWeapon;
            if (loadout.MustNotifySwitchRegularWeapon)
                flags |= PlayerLoadoutFlags.MustNotifySwitchRegularWeapon;
            if (loadout.MustNotifySwitchSHMUPWeapon)
                flags |= PlayerLoadoutFlags.MustNotifySwitchShmupWeapon;
            return new PlayerLoadoutSnapshot
            {
                PrimaryWeapon = (int)loadout.primaryWeapon,
                SecondaryWeapon = (int)loadout.secondaryWeapon,
                Super = (int)loadout.super,
                Charm = (int)loadout.charm,
                Flags = flags,
            };
        }

        private static bool IsValidLoadout(PlayerLoadoutSnapshot loadout)
        {
            return System.Enum.IsDefined(typeof(Weapon), loadout.PrimaryWeapon) &&
                (Weapon)loadout.PrimaryWeapon != Weapon.None &&
                System.Enum.IsDefined(typeof(Weapon), loadout.SecondaryWeapon) &&
                System.Enum.IsDefined(typeof(Super), loadout.Super) &&
                System.Enum.IsDefined(typeof(Charm), loadout.Charm) &&
                (loadout.Flags & ~(PlayerLoadoutFlags.
                    HasEquippedSecondaryRegularWeapon |
                    PlayerLoadoutFlags.HasEquippedSecondaryShmupWeapon |
                    PlayerLoadoutFlags.MustNotifySwitchRegularWeapon |
                    PlayerLoadoutFlags.MustNotifySwitchShmupWeapon)) == 0;
        }

        private static void UpdateLoadoutOverlay(
            ref PlayerData.PlayerLoadouts.PlayerLoadout overlay,
            PlayerLoadoutSnapshot snapshot)
        {
            if (overlay == null)
                overlay = new PlayerData.PlayerLoadouts.PlayerLoadout();
            overlay.primaryWeapon = (Weapon)snapshot.PrimaryWeapon;
            overlay.secondaryWeapon = (Weapon)snapshot.SecondaryWeapon;
            overlay.super = (Super)snapshot.Super;
            overlay.charm = (Charm)snapshot.Charm;
            overlay.HasEquippedSecondaryRegularWeapon =
                (snapshot.Flags & PlayerLoadoutFlags.
                    HasEquippedSecondaryRegularWeapon) != 0;
            overlay.HasEquippedSecondarySHMUPWeapon =
                (snapshot.Flags & PlayerLoadoutFlags.
                    HasEquippedSecondaryShmupWeapon) != 0;
            overlay.MustNotifySwitchRegularWeapon =
                (snapshot.Flags & PlayerLoadoutFlags.
                    MustNotifySwitchRegularWeapon) != 0;
            overlay.MustNotifySwitchSHMUPWeapon =
                (snapshot.Flags & PlayerLoadoutFlags.
                    MustNotifySwitchShmupWeapon) != 0;
        }

        private static bool ApplyClientSessionLoadouts(SessionContext context)
        {
            if (!IsValidLoadout(context.PlayerOneLoadout) ||
                !IsValidLoadout(context.PlayerTwoLoadout))
            {
                Plugin.Log.LogWarning("[LoadoutSync] El contexto contenía un " +
                    "equipamiento desconocido; no se aplicará.");
                return false;
            }

            UpdateLoadoutOverlay(ref clientPlayerOneLoadoutOverlay,
                context.PlayerOneLoadout);
            UpdateLoadoutOverlay(ref clientPlayerTwoLoadoutOverlay,
                context.PlayerTwoLoadout);
            hasClientPlayerOneLoadoutOverlay = true;
            hasClientPlayerTwoLoadoutOverlay = true;
            return true;
        }

        internal static bool TryGetSessionLoadout(PlayerId player,
            out PlayerData.PlayerLoadouts.PlayerLoadout loadout)
        {
            loadout = null;
            if (!Enabled)
                return false;
            if (IsHost && player == PlayerId.PlayerTwo &&
                hostPlayerTwoLoadoutOverlay != null)
            {
                loadout = hostPlayerTwoLoadoutOverlay;
                return true;
            }
            if (!IsClient)
                return false;
            if (player == PlayerId.PlayerOne &&
                hasClientPlayerOneLoadoutOverlay &&
                clientPlayerOneLoadoutOverlay != null)
            {
                loadout = clientPlayerOneLoadoutOverlay;
                return true;
            }
            if (player == PlayerId.PlayerTwo &&
                hasClientPlayerTwoLoadoutOverlay &&
                clientPlayerTwoLoadoutOverlay != null)
            {
                loadout = clientPlayerTwoLoadoutOverlay;
                return true;
            }
            return false;
        }

        private static void ResetRemoteGuestLoadout(bool discardOverlay)
        {
            hasRemoteGuestLoadout = false;
            if (discardOverlay)
            {
                remoteGuestLoadout = default(PlayerLoadoutSnapshot);
                remoteGuestLoadoutRevision = 0;
                hostPlayerTwoLoadoutOverlay = null;
            }
        }

        private static void ResetSessionLoadouts()
        {
            localGuestLoadout = default(PlayerLoadoutSnapshot);
            localGuestLoadoutRevision = 0;
            hasLocalGuestLoadout = false;
            ResetRemoteGuestLoadout(true);
            clientPlayerOneLoadoutOverlay = null;
            clientPlayerTwoLoadoutOverlay = null;
            hasClientPlayerOneLoadoutOverlay = false;
            hasClientPlayerTwoLoadoutOverlay = false;
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
                PlayerTwoSuperActionSequence =
                    hostPlayerTwoSuperActionSequence,
                StateSessionNonce = localStateSessionNonce,
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
            if (!transport.IsConnected || LocalPhysicalInputBlocked)
            {
                hostPlayerOneInput = new InputFrame
                {
                    Tick = sourceTick,
                    Released = transport.IsConnected && LocalPhysicalInputBlocked ?
                        previousHostPlayerOneHeld : InputButtons.None,
                };
                previousHostPlayerOneHeld = InputButtons.None;
                return;
            }

            samplingLocalInput = true;
            try
            {
                var sampled = new InputFrame { Tick = sourceTick };
                MergeConfiguredPlayer(PlayerId.PlayerOne, ref sampled);
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
                {
                    state.PlayerOneHealthMax = (byte)Mathf.Clamp(
                        player.stats.HealthMax, 0, 255);
                    state.PlayerOneSuperMeter = player.stats.SuperMeter;
                }
                else
                {
                    state.PlayerTwoHealthMax = (byte)Mathf.Clamp(
                        player.stats.HealthMax, 0, 255);
                    state.PlayerTwoSuperMeter = player.stats.SuperMeter;
                }
            }

            var levelPlayer = player as LevelPlayerController;
            var motor = levelPlayer == null ? null : levelPlayer.motor;
            var motionFlags = PlayerMotionFlags.None;
            if (IsPlayerReviving(player))
                motionFlags |= PlayerMotionFlags.Reviving;
            if (motor != null)
            {
                if (motor.Dashing)
                    motionFlags |= PlayerMotionFlags.Dashing;
                if (motor.IsHit)
                    motionFlags |= PlayerMotionFlags.Hit;
                if (motor.IsUsingSuperOrEx)
                    motionFlags |= PlayerMotionFlags.UsingSuperOrEx;
            }
            if (id == PlayerId.PlayerOne)
                state.PlayerOneMotionFlags = motionFlags;
            else
            {
                state.PlayerTwoMotionFlags = motionFlags;
                if (motor != null)
                    state.PlayerTwoHitDirection = CaptureHitDirection(motor);
            }
        }

        private static bool IsPlayerReviving(AbstractPlayerController player)
        {
            if (player == null || PlayerIsRevivingField == null)
                return false;
            try
            {
                return (bool)PlayerIsRevivingField.GetValue(player);
            }
            catch
            {
                return false;
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
                if (state.StateSessionNonce != 0 &&
                    state.StateSessionNonce != lastRemoteStateSessionNonce)
                    BaselineRemoteStateEpoch(state);
                if (hasRemotePlayerStateTick &&
                    !IsNewerTick(state.Tick, lastRemotePlayerStateTick))
                    continue;
                if (sceneTransitionActive && state.TransitionId != sceneTransitionId)
                    continue;
                latestRemotePlayerState = state;
                latestRemotePlayerStateScene = SceneManager.GetActiveScene().name;
                BufferRemotePlayerState(state);
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
                if (state.PlayerTwoSuperActionSequence != 0 &&
                    (!hasRemotePlayerTwoSuperActionSequence ||
                        IsNewerTick(state.PlayerTwoSuperActionSequence,
                            lastRemotePlayerTwoSuperActionSequence)))
                {
                    lastRemotePlayerTwoSuperActionSequence =
                        state.PlayerTwoSuperActionSequence;
                    hasRemotePlayerTwoSuperActionSequence = true;
                    if (state.PlayerTwoSuperActionSequence ==
                        localPlayerTwoSuperRequestSequence)
                        localPlayerTwoSuperRequestAdvertiseDeadline = 0f;
                    if (localPredictedPlayerTwoSuperRequests.Remove(
                        state.PlayerTwoSuperActionSequence))
                    {
                        Plugin.Log.LogInfo("[StateSync] El host confirmó el " +
                            "EX/Super P2 ya predicho por el invitado (#" +
                            state.PlayerTwoSuperActionSequence + ").");
                    }
                    else
                    {
                        RemoveQueuedSequence(localPlayerTwoSuperDispatchQueue,
                            state.PlayerTwoSuperActionSequence);
                        playerTwoConfirmedSuperQueue.Enqueue(
                            state.PlayerTwoSuperActionSequence);
                        remotePlayerTwoSuperDeferralDeadline =
                            Time.realtimeSinceStartup +
                            RemotePlayerTwoSuperDeferralSeconds;
                        Plugin.Log.LogInfo("[StateSync] El host confirmó " +
                            "EX/Super P2; se reproducirá el pulso faltante (#" +
                            state.PlayerTwoSuperActionSequence + ").");
                    }
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
                if (firstLevelCorrection)
                {
                    CorrectPlayerPosition(PlayerId.PlayerOne,
                        latestRemotePlayerState.PlayerOneX,
                        latestRemotePlayerState.PlayerOneY, true);
                    levelPlayerOneBootstrapCompleted = true;
                    Plugin.Log.LogMessage("[StateSync] Player One remoto alineado al iniciar " +
                        "el combate.");
                }
            }
            if ((latestRemotePlayerState.PresentMask & 2) != 0)
            {
                if (Map.Current != null)
                {
                    // El mapa del invitado es sólo una representación. Movimiento,
                    // colisiones e interacciones se resuelven en el host y ambos
                    // actores se dibujan desde el buffer de snapshots en LateUpdate.
                    ResetPlayerTwoStallFallback();
                }
                else
                {
                    // P2 pertenece al invitado y se simula localmente. El snapshot
                    // del host ya recorrió cliente -> host -> cliente, así que
                    // corregirlo continuamente rebobinaría cada dash un RTT.
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
            }

            ReportLoadoutHealthAgreement(latestRemotePlayerState);
            SlimeBossSynchronizer.ApplyAuthoritativePlayerState(
                latestRemotePlayerState);
        }

        private static void ReportLoadoutHealthAgreement(
            PlayerStateSnapshot state)
        {
            if (Level.Current == null || !Level.Current.Started)
                return;

            var compared = 0;
            var mismatch = false;
            try
            {
                if ((state.PresentMask & 1) != 0 &&
                    state.PlayerOneHealthMax != 0)
                {
                    var player = PlayerManager.GetPlayer(PlayerId.PlayerOne);
                    if (player != null && player.stats != null)
                    {
                        compared++;
                        mismatch |= player.stats.HealthMax !=
                            state.PlayerOneHealthMax;
                    }
                }
                if ((state.PresentMask & 2) != 0 &&
                    state.PlayerTwoHealthMax != 0)
                {
                    var player = PlayerManager.GetPlayer(PlayerId.PlayerTwo);
                    if (player != null && player.stats != null)
                    {
                        compared++;
                        mismatch |= player.stats.HealthMax !=
                            state.PlayerTwoHealthMax;
                    }
                }
            }
            catch
            {
                return;
            }

            if (mismatch && !loadoutHealthMismatchReported)
            {
                loadoutHealthMismatchReported = true;
                Plugin.Log.LogWarning("[LoadoutSync] La vida máxima local no " +
                    "coincide con el host (host P1=" + state.PlayerOneHealthMax +
                    ", P2=" + state.PlayerTwoHealthMax + ").");
            }
            else if (!mismatch && compared == 2 &&
                !loadoutHealthAgreementReported)
            {
                loadoutHealthAgreementReported = true;
                Plugin.Log.LogMessage("[LoadoutSync] Vida máxima verificada en " +
                    "ambas PCs (P1=" + state.PlayerOneHealthMax + ", P2=" +
                    state.PlayerTwoHealthMax + ").");
            }
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
            remotePlayerTwoSuperDeferralDeadline = 0f;
            ClearPlayerTwoSuperRequestQueues();
            playerTwoMapNeutralSinceRealtime = 0f;
            remotePlayerStateBuffer.Clear();
            lastBufferedPlayerStateRealtime = 0f;
            maxBufferedPlayerStateGap = 0f;
            maxPlayerOneRenderError = 0f;
            maxPlayerTwoRenderError = 0f;
            lastRenderTelemetryRealtime = 0f;
            authoritativeMapRenderReported = false;
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
                    CaptureClientSlotState(PlayerData.CurrentSaveFileIndex);
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

        private static void BufferRemotePlayerState(PlayerStateSnapshot state)
        {
            var now = Time.realtimeSinceStartup;
            var sceneName = SceneManager.GetActiveScene().name;
            if (remotePlayerStateBuffer.Count != 0)
            {
                var previous = remotePlayerStateBuffer[
                    remotePlayerStateBuffer.Count - 1];
                if (previous.SceneName != sceneName)
                {
                    remotePlayerStateBuffer.Clear();
                    lastBufferedPlayerStateRealtime = 0f;
                }
                else if (HasDiscretePlayerStateChange(previous.State, state))
                {
                    remotePlayerStateBuffer.Clear();
                    Plugin.Log.LogInfo("[StateSync] Buffer reiniciado por cambio " +
                        "discreto (presentes=" + state.PresentMask +
                        ", muertos=" + state.DeadMask + ", revive=" +
                        GetRevivingMask(state) + ").");
                }
            }
            if (lastBufferedPlayerStateRealtime > 0f)
                maxBufferedPlayerStateGap = Mathf.Max(maxBufferedPlayerStateGap,
                    now - lastBufferedPlayerStateRealtime);
            lastBufferedPlayerStateRealtime = now;
            remotePlayerStateBuffer.Add(new BufferedPlayerState(
                state, sceneName, now));
            if (remotePlayerStateBuffer.Count > RemoteStateBufferCapacity)
                remotePlayerStateBuffer.RemoveAt(0);
            RebuildBufferedPlayerStateTimeline(now, state.Tick);
        }

        private static bool HasDiscretePlayerStateChange(
            PlayerStateSnapshot previous, PlayerStateSnapshot current)
        {
            return previous.PresentMask != current.PresentMask ||
                previous.DeadMask != current.DeadMask ||
                GetRevivingMask(previous) != GetRevivingMask(current);
        }

        private static byte GetRevivingMask(PlayerStateSnapshot state)
        {
            byte mask = 0;
            if ((state.PlayerOneMotionFlags & PlayerMotionFlags.Reviving) != 0)
                mask |= 1;
            if ((state.PlayerTwoMotionFlags & PlayerMotionFlags.Reviving) != 0)
                mask |= 2;
            return mask;
        }

        private static void RebuildBufferedPlayerStateTimeline(float newestRealtime,
            uint newestTick)
        {
            // UDP y TCP pueden entregar varios datagramas/frames en el mismo Update.
            // Se conservan todos y se reconstruye su separación con el tick del host,
            // terminando siempre en el instante real de la ráfaga. Así no quedan con
            // el mismo timestamp ni se empuja el snapshot más nuevo hacia el futuro.
            for (var i = 0; i < remotePlayerStateBuffer.Count; i++)
            {
                var buffered = remotePlayerStateBuffer[i];
                var tickDistance = unchecked((int)(newestTick -
                    buffered.State.Tick));
                if (tickDistance < 0)
                    tickDistance = 0;
                remotePlayerStateBuffer[i] = new BufferedPlayerState(
                    buffered.State, buffered.SceneName,
                    newestRealtime - tickDistance *
                        RemoteStateNominalIntervalSeconds);
            }
        }

        internal static void RenderBufferedRemotePlayersLate()
        {
            if (!IsClientSession || !transport.IsConnected ||
                SessionOverlayVisible || SceneTransitionActive ||
                !hasRemotePlayerState ||
                latestRemotePlayerStateScene != SceneManager.GetActiveScene().name ||
                Time.realtimeSinceStartup - lastRemotePlayerStateRealtime > 0.5f)
                return;

            PlayerStateSnapshot from;
            PlayerStateSnapshot to;
            float blend;
            if (!TryGetBufferedPlayerStates(out from, out to, out blend))
                return;

            if (Map.Current != null)
            {
                if (!mapBootstrapCompleted)
                    return;
                var players = Map.Current.players;
                if (players == null || players.Length < 2)
                    return;
                if ((to.PresentMask & 1) != 0 && players[0] != null)
                {
                    var target = InterpolatePlayerPosition(players[0].transform,
                        from.PlayerOneX, from.PlayerOneY,
                        to.PlayerOneX, to.PlayerOneY, blend);
                    maxPlayerOneRenderError = Mathf.Max(maxPlayerOneRenderError,
                        Vector2.Distance(players[0].transform.position, target));
                    RenderAuthoritativeMapPlayer(players[0], target);
                }
                if ((to.PresentMask & 2) != 0 && players[1] != null)
                {
                    var target = InterpolatePlayerPosition(players[1].transform,
                        from.PlayerTwoX, from.PlayerTwoY,
                        to.PlayerTwoX, to.PlayerTwoY, blend);
                    maxPlayerTwoRenderError = Mathf.Max(maxPlayerTwoRenderError,
                        Vector2.Distance(players[1].transform.position, target));
                    RenderAuthoritativeMapPlayer(players[1], target);
                }
                if (!authoritativeMapRenderReported)
                {
                    authoritativeMapRenderReported = true;
                    Plugin.Log.LogMessage("[MapAuthority] El invitado representa " +
                        "ambos jugadores desde la simulación del host.");
                }
                ReportRemoteRenderTelemetry();
                return;
            }

            if (Level.Current == null || !Level.Current.Started ||
                (to.Flags & PlayerStateFlags.GameplayStarted) == 0 ||
                (to.PresentMask & 1) == 0 || (to.DeadMask & 1) != 0 ||
                (to.PlayerOneMotionFlags & PlayerMotionFlags.Reviving) != 0)
                return;
            AbstractPlayerController playerOne;
            try { playerOne = PlayerManager.GetPlayer(PlayerId.PlayerOne); }
            catch { return; }
            if (playerOne == null || !playerOne.gameObject.activeInHierarchy)
                return;
            var levelTarget = InterpolatePlayerPosition(playerOne.transform,
                from.PlayerOneX, from.PlayerOneY,
                to.PlayerOneX, to.PlayerOneY, blend);
            maxPlayerOneRenderError = Mathf.Max(maxPlayerOneRenderError,
                Vector2.Distance(playerOne.transform.position, levelTarget));
            RenderAuthoritativeLevelPlayerOne(playerOne, levelTarget);
            ReportRemoteRenderTelemetry();
        }

        private static bool TryGetBufferedPlayerStates(
            out PlayerStateSnapshot from, out PlayerStateSnapshot to,
            out float blend)
        {
            from = default(PlayerStateSnapshot);
            to = default(PlayerStateSnapshot);
            blend = 0f;
            if (remotePlayerStateBuffer.Count == 0)
                return false;

            var targetRealtime = Time.realtimeSinceStartup -
                RemoteStateInterpolationDelaySeconds;
            while (remotePlayerStateBuffer.Count > 2 &&
                remotePlayerStateBuffer[1].ReceivedRealtime <= targetRealtime)
                remotePlayerStateBuffer.RemoveAt(0);

            var first = remotePlayerStateBuffer[0];
            if (remotePlayerStateBuffer.Count == 1)
            {
                from = first.State;
                to = first.State;
                return true;
            }

            var second = remotePlayerStateBuffer[1];
            from = first.State;
            to = second.State;
            if (targetRealtime <= first.ReceivedRealtime)
                blend = 0f;
            else if (targetRealtime >= second.ReceivedRealtime)
                blend = 1f;
            else
                blend = Mathf.InverseLerp(first.ReceivedRealtime,
                    second.ReceivedRealtime, targetRealtime);
            return true;
        }

        private static Vector3 InterpolatePlayerPosition(Transform player,
            float fromX, float fromY, float toX, float toY, float blend)
        {
            return new Vector3(Mathf.Lerp(fromX, toX, blend),
                Mathf.Lerp(fromY, toY, blend), player.position.z);
        }

        private static void RenderAuthoritativeMapPlayer(
            MapPlayerController player, Vector3 position)
        {
            player.transform.position = position;
            if (player.motor != null && MapPlayerMotorVelocityField != null)
                MapPlayerMotorVelocityField.SetValue(player.motor, Vector2.zero);
            var body = player.GetComponent<Rigidbody2D>();
            if (body != null)
                body.velocity = Vector2.zero;
        }

        private static void RenderAuthoritativeLevelPlayerOne(
            AbstractPlayerController player, Vector3 position)
        {
            player.transform.position = position;
            var levelPlayer = player as LevelPlayerController;
            var motor = levelPlayer == null ? null : levelPlayer.motor;
            if (motor == null)
                return;
            if (LevelPlayerMotorLastPositionField != null)
                LevelPlayerMotorLastPositionField.SetValue(motor,
                    (Vector2)position);
            if (LevelPlayerMotorLastPositionFixedField != null)
                LevelPlayerMotorLastPositionFixedField.SetValue(motor,
                    (Vector2)position);
        }

        private static void ReportRemoteRenderTelemetry()
        {
            var now = Time.realtimeSinceStartup;
            if (lastRenderTelemetryRealtime > 0f &&
                now - lastRenderTelemetryRealtime < 5f)
                return;
            lastRenderTelemetryRealtime = now;
            Plugin.Log.LogInfo("[StateSync] Buffer=" +
                remotePlayerStateBuffer.Count + " gapMax=" +
                (maxBufferedPlayerStateGap * 1000f).ToString("0") +
                "ms errorP1=" + maxPlayerOneRenderError.ToString("0.0") +
                " errorP2=" + maxPlayerTwoRenderError.ToString("0.0") +
                " ping=" + transport.PingMilliseconds + "ms pérdida=" +
                transport.EstimatedPacketLossPercent + "%.");
            maxBufferedPlayerStateGap = 0f;
            maxPlayerOneRenderError = 0f;
            maxPlayerTwoRenderError = 0f;
        }

        private static uint CreateInputSessionNonce()
        {
            var nonce = unchecked((uint)System.Guid.NewGuid().GetHashCode() ^
                (uint)System.Environment.TickCount ^
                (uint)System.DateTime.UtcNow.Ticks);
            return nonce == 0 ? 1u : nonce;
        }

        private static void BaselineRemoteStateEpoch(PlayerStateSnapshot state)
        {
            remotePlayerStateBuffer.Clear();
            lastBufferedPlayerStateRealtime = 0f;
            lastRemoteStateSessionNonce = state.StateSessionNonce;
            lastRemotePlayerStateTick = 0;
            hasRemotePlayerStateTick = false;
            lastRemotePlayerOneSuperActionSequence =
                state.PlayerOneSuperActionSequence;
            hasRemotePlayerOneSuperActionSequence = true;
            lastRemotePlayerTwoSuperActionSequence =
                state.PlayerTwoSuperActionSequence;
            hasRemotePlayerTwoSuperActionSequence = true;
            Plugin.Log.LogInfo("[StateSync] Nueva época de estado: " +
                state.StateSessionNonce.ToString("X8") + ".");
        }

        private static void ResetRemoteInputEpoch(uint nonce)
        {
            lastRemoteInputSessionNonce = nonce;
            localStateSessionNonce = CreateInputSessionNonce();
            ResetRemoteGuestLoadout(false);
            lastRemotePlayerTwoSuperRequestSequence = 0;
            hasRemotePlayerTwoSuperRequestSequence = false;
            ClearPlayerTwoSuperRequestQueues();
            hostPlayerTwoSuperActionSequence = 0;
            remoteMapAcceptDeadline = 0f;
            remoteInputNeutralizedForStall = false;
            ResetInputEdgeLatches();
            Plugin.Log.LogInfo("[InputSync] Nueva época de input P2: " +
                nonce.ToString("X8") + ".");
        }

        private static void ResetInputEpochForReconnect()
        {
            localInputSessionNonce = CreateInputSessionNonce();
            lastRemoteInputSessionNonce = 0;
            localStateSessionNonce = CreateInputSessionNonce();
            lastRemoteStateSessionNonce = 0;
            localPlayerTwoSuperRequestSequence = 0;
            localPlayerTwoSuperRequestAdvertiseDeadline = 0f;
            lastRemotePlayerTwoSuperRequestSequence = 0;
            hasRemotePlayerTwoSuperRequestSequence = false;
            ClearPlayerTwoSuperRequestQueues();
            hostPlayerTwoSuperActionSequence = 0;
            lastRemotePlayerTwoSuperActionSequence = 0;
            hasRemotePlayerTwoSuperActionSequence = false;
            remotePlayerTwoSuperDeferralDeadline = 0f;
            ResetRemoteGuestLoadout(false);
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
                CorrectMapTransform(players[index], id, x, y);
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

        private static void CorrectMapTransform(MapPlayerController player,
            PlayerId id, float x, float y)
        {
            var playerTransform = player.transform;
            var current = playerTransform.position;
            var target = new Vector3(x, y, current.z);
            var distance = Vector2.Distance(current, target);
            var actorMoving = id == PlayerId.PlayerOne ?
                latestRemotePlayerState.PlayerOneMapHorizontal != 0 ||
                    latestRemotePlayerState.PlayerOneMapVertical != 0 :
                Mathf.Abs(received.Horizontal) > 16 ||
                    Mathf.Abs(received.Vertical) > 16;

            // Una divergencia grande ya no es latencia normal: se corrige aun
            // con el stick mantenido para que las colisiones no separen mapas.
            if (distance > 32f)
            {
                AlignMapPlayer(player, target);
                if (id == PlayerId.PlayerTwo)
                    playerTwoMapNeutralSinceRealtime = 0f;
                return;
            }
            if (distance <= 0.35f)
                return;

            if (actorMoving)
            {
                if (id == PlayerId.PlayerTwo)
                    playerTwoMapNeutralSinceRealtime = 0f;
                return;
            }

            if (id == PlayerId.PlayerTwo)
            {
                var now = Time.realtimeSinceStartup;
                if (playerTwoMapNeutralSinceRealtime <= 0f)
                {
                    playerTwoMapNeutralSinceRealtime = now;
                    return;
                }
                var settleSeconds = Mathf.Clamp(
                    Mathf.Max(0, transport.PingMilliseconds) * 0.001f + 0.05f,
                    0.08f, 0.35f);
                if (now - playerTwoMapNeutralSinceRealtime < settleSeconds)
                    return;
            }

            if (distance > 12f)
                AlignMapPlayer(player, target);
            else
                AlignMapPlayer(player, Vector3.Lerp(current, target,
                    Mathf.Clamp01(Time.unscaledDeltaTime * 8f)));
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
            if (IsClientSession && LocalPhysicalInputBlocked)
                return 0f;
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
            if ((IsClientSession && LocalPhysicalInputBlocked) ||
                ClientMapIsHostAuthoritative ||
                IsGameplayInputLocked())
                return false;
            var button = MapButton(actionId);
            if (button == InputButtons.None)
                return false;

            if (phase == ButtonPhase.Pressed)
            {
                if (button == InputButtons.Super && IsHostSession &&
                    TryOfferHostPlayerTwoSuperRequest())
                    return true;
                if (button == InputButtons.Accept && IsHostSession &&
                    Map.Current != null && mapLevelInteractionInputProbeActive &&
                    Time.realtimeSinceStartup <= remoteMapAcceptDeadline)
                    return true;
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

        internal static void ConsumeRemoteMapLevelInteraction()
        {
            if (IsHostSession)
                remoteMapAcceptDeadline = 0f;
        }

        internal static void BeginMapLevelInteractionInputProbe()
        {
            mapLevelInteractionInputProbeActive = true;
        }

        internal static void EndMapLevelInteractionInputProbe()
        {
            mapLevelInteractionInputProbeActive = false;
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

        private static void AdvanceHostPlayerTwoSuperRequestDeadline(
            bool gameplayInputLocked)
        {
            if (hostPendingPlayerTwoSuperRequests.Count == 0)
            {
                hostOfferedPlayerTwoSuperRequestSequence = 0;
                return;
            }

            var request = hostPendingPlayerTwoSuperRequests.Peek();
            if (gameplayInputLocked)
            {
                // Un FixedUpdate de pausa/carga no consume tiempo elegible.
                request.PolledSinceLastFixedUpdate = false;
                return;
            }
            if (request.DeadlineStarted && request.PolledSinceLastFixedUpdate)
                request.RemainingEligibleSeconds -=
                    Mathf.Max(Time.fixedDeltaTime, 0.001f);
            request.PolledSinceLastFixedUpdate = false;
        }

        private static bool TryOfferHostPlayerTwoSuperRequest()
        {
            while (hostPendingPlayerTwoSuperRequests.Count != 0)
            {
                var request = hostPendingPlayerTwoSuperRequests.Peek();
                if (request.DeadlineStarted &&
                    request.RemainingEligibleSeconds <= 0f)
                {
                    hostPendingPlayerTwoSuperRequests.Dequeue();
                    if (hostOfferedPlayerTwoSuperRequestSequence ==
                        request.Sequence)
                        hostOfferedPlayerTwoSuperRequestSequence = 0;
                    Plugin.Log.LogWarning("[InputSync] La solicitud de " +
                        "EX/Super P2 expiró sin consumo (#" +
                        request.Sequence + ").");
                    continue;
                }

                if (!request.DeadlineStarted)
                {
                    request.DeadlineStarted = true;
                    request.RemainingEligibleSeconds = Mathf.Clamp(
                        transport.PingMilliseconds * 0.001f + 0.2f,
                        0.5f, 1.25f);
                }
                request.PolledSinceLastFixedUpdate = true;
                hostOfferedPlayerTwoSuperRequestSequence = request.Sequence;
                return true;
            }

            hostOfferedPlayerTwoSuperRequestSequence = 0;
            return false;
        }

        private static bool RemoveQueuedSequence(
            System.Collections.Generic.Queue<uint> queue, uint sequence)
        {
            var removed = false;
            var count = queue.Count;
            for (var i = 0; i < count; i++)
            {
                var queued = queue.Dequeue();
                if (!removed && queued == sequence)
                    removed = true;
                else
                    queue.Enqueue(queued);
            }
            return removed;
        }

        private static void ClearPlayerTwoSuperRequestQueues()
        {
            hostPendingPlayerTwoSuperRequests.Clear();
            hostOfferedPlayerTwoSuperRequestSequence = 0;
            localPlayerTwoSuperDispatchQueue.Clear();
            localPlayerTwoSuperDispatchedForFixed = 0;
            localPredictedPlayerTwoSuperRequests.Clear();
            playerTwoConfirmedSuperQueue.Clear();
            playerTwoConfirmedSuperDispatchedForFixed = 0;
            remotePlayerTwoSuperDeferralDeadline = 0f;
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
            localPlayerTwoSuperDispatchedForFixed = 0;
            playerTwoConfirmedSuperDispatchedForFixed = 0;
        }

        private sealed class PlayerTwoSuperRequest
        {
            public PlayerTwoSuperRequest(uint sequence)
            {
                Sequence = sequence;
            }

            public readonly uint Sequence;
            public bool DeadlineStarted;
            public float RemainingEligibleSeconds;
            public bool PolledSinceLastFixedUpdate;
        }

        private struct BufferedPlayerState
        {
            public BufferedPlayerState(PlayerStateSnapshot state,
                string sceneName, float receivedRealtime)
            {
                State = state;
                SceneName = sceneName;
                ReceivedRealtime = receivedRealtime;
            }

            public readonly PlayerStateSnapshot State;
            public readonly string SceneName;
            public readonly float ReceivedRealtime;
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
            if (LocalPhysicalInputBlocked)
                return new InputFrame();

            return SampleConfiguredPlayerInputUnfiltered();
        }

        private static InputFrame SampleConfiguredPlayerInputUnfiltered()
        {
            samplingLocalInput = true;
            try
            {
                var sampled = new InputFrame();
                // En la computadora del invitado sus dispositivos y bindings
                // pertenecen al perfil local de Player One. El frame ya contiene
                // acciones semánticas, por lo que mezclar también Player Two o
                // teclas hardcodeadas puede convertir una sola tecla en dos
                // acciones distintas.
                MergeConfiguredPlayer(PlayerId.PlayerOne, ref sampled);
                return sampled;
            }
            finally
            {
                samplingLocalInput = false;
            }
        }

        internal static InputFrame SampleLocalPlayerOneForUi()
        {
            if (LocalPhysicalInputBlocked)
                return new InputFrame();

            samplingLocalInput = true;
            try
            {
                var sampled = new InputFrame();
                MergeConfiguredPlayer(PlayerId.PlayerOne, ref sampled);
                return sampled;
            }
            finally
            {
                samplingLocalInput = false;
            }
        }

        private static void UpdateLocalPhysicalInputFocusGate()
        {
            if (!blockLocalInputWhenUnfocused || !Plugin.HasApplicationFocus ||
                !localPhysicalInputNeedsRearm)
                return;
            if (Time.frameCount < localPhysicalInputRearmNotBeforeFrame)
                return;

            var sampled = SampleConfiguredPlayerInputUnfiltered();
            const int axisDeadzone = 32;
            if (Mathf.Abs(sampled.Horizontal) > axisDeadzone ||
                Mathf.Abs(sampled.Vertical) > axisDeadzone ||
                sampled.Held != InputButtons.None ||
                sampled.Pressed != InputButtons.None)
                return;

            localPhysicalInputNeedsRearm = false;
            localPhysicalInputRearmNotBeforeFrame = 0;
            Plugin.Log.LogMessage("[Focus] Entrada local reactivada después de " +
                "soltar controles físicos.");
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
