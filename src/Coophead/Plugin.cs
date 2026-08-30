using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Coophead.Transport;

namespace Coophead
{
    // Los bordes de botones deben promoverse antes de que los motores y armas de
    // Cuphead ejecuten su FixedUpdate; de lo contrario un pulso de un solo frame
    // puede cargarse y borrarse sin que el consumidor llegue a verlo.
    [DefaultExecutionOrder(-10000)]
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Cuphead.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mx.gilomx.coophead";
        public const string PluginName = "Co-ophead";
        public const string PluginVersion = "0.12.6";

        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }
        internal static Plugin Instance { get; private set; }

        private Harmony harmony;
        private ConfigEntry<InputTransportMode> transportMode;
        private ConfigEntry<string> lanHostAddress;
        private ConfigEntry<int> lanPort;
        private ConfigEntry<string> relayAddress;
        private ConfigEntry<int> relayPort;
        private ConfigEntry<string> roomCode;
        private ConfigEntry<string> signalingUrl;
        private ConfigEntry<string> stunHost;
        private ConfigEntry<int> stunPort;
        private ConfigEntry<bool> runInBackgroundForTesting;
        private bool showOnlineMenu;
        private string joinCode = "";
        private string onlineMessage = "";
        private Rect onlineWindow = new Rect(30, 30, 390, 300);
        private bool focusLossRecorded;
        private int focusLostFrame;
        private float focusLostRealtime;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Logger.LogInfo(PluginName + " " + PluginVersion + " cargado.");
            transportMode = Config.Bind("InputLab", "Transport", InputTransportMode.Loopback,
                "Loopback, LanHost o LanClient.");
            lanHostAddress = Config.Bind("InputLab", "LanHostAddress", "127.0.0.1",
                "IP del host usada por LanClient.");
            lanPort = Config.Bind("InputLab", "LanPort", 27182,
                "Puerto UDP para los frames de entrada (1-65535).");
            relayAddress = Config.Bind("Internet", "RelayAddress", "127.0.0.1",
                "Servidor relay de Co-ophead.");
            relayPort = Config.Bind("Internet", "RelayPort", 27183,
                "Puerto TCP del relay.");
            roomCode = Config.Bind("Internet", "RoomCode", "",
                "Código para InternetClient; InternetHost genera uno.");
            signalingUrl = Config.Bind("P2P", "SignalingUrl",
                "https://coophead-signaling.coophead-signaling.workers.dev",
                "Servicio gratuito de señalización.");
            stunHost = Config.Bind("P2P", "StunHost", "stun.cloudflare.com",
                "Servidor STUN para descubrir el endpoint público.");
            stunPort = Config.Bind("P2P", "StunPort", 3478,
                "Puerto UDP del servidor STUN.");
            runInBackgroundForTesting = Config.Bind("Testing", "RunInBackground", true,
                "Temporal para pruebas: mantiene Cuphead activo al cambiar de ventana. " +
                "El comportamiento final será false.");
            RemoteInputLab.SetRunInBackgroundForTesting(
                runInBackgroundForTesting.Value);
            try
            {
                RemoteInputLab.Configure(transportMode.Value, lanHostAddress.Value, lanPort.Value,
                    relayAddress.Value, relayPort.Value, roomCode.Value,
                    signalingUrl.Value, stunHost.Value, stunPort.Value);
            }
            catch (System.Exception ex)
            {
                Logger.LogError("No se pudo configurar el transporte de Input Lab: " + ex.Message);
            }
            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Update()
        {
            if (MainMenuIntegration.MenuOpen)
                showOnlineMenu = false;
            else if (Input.GetKeyDown(KeyCode.F6))
            {
                if (showOnlineMenu)
                    CloseOnlineWindow();
                else
                    showOnlineMenu = true;
            }
            if (showOnlineMenu && Input.GetKeyDown(KeyCode.Escape))
                CloseOnlineWindow();
            RemoteInputLab.Tick();
        }

        private void FixedUpdate()
        {
            RemoteInputLab.AdvanceFixedInput();
        }

        private void LateUpdate()
        {
            RemoteInputLab.LateTick();
        }

        private void OnGUI()
        {
            if (showOnlineMenu)
                onlineWindow = GUI.Window(78216, onlineWindow, DrawOnlineWindow, "CO-OPHEAD");
            DrawConnectionQualityHud();
            DrawLevelLoadMessage();
            DrawSessionInterruptionOverlay();
        }

        private void DrawLevelLoadMessage()
        {
            var showWaitingMessage = LevelLoadGate.ShowHostWaitingMessage;
            if (!showWaitingMessage && !LevelLoadGate.CanAbort)
                return;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
            };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(20, Screen.height * 0.68f,
                Screen.width - 40, 50), showWaitingMessage ?
                    LevelLoadGate.HostWaitingMessage :
                    "LA CARGA NO TERMINÓ CORRECTAMENTE", style);

            if (LevelLoadGate.CanAbort)
            {
                var buttonWidth = 310f;
                var buttonText = LevelLoadGate.TargetIsLevel ?
                    "CANCELAR Y VOLVER AL MAPA" : "CANCELAR Y SALIR DE LA SESIÓN";
                if (GUI.Button(new Rect((Screen.width - buttonWidth) * 0.5f,
                    Screen.height * 0.76f, buttonWidth, 42f), buttonText))
                    RemoteInputLab.AbortCoordinatedLoad();
            }
        }

        private void DrawConnectionQualityHud()
        {
            if (!RemoteInputLab.IsConnected || RemoteInputLab.SessionOverlayVisible)
                return;

            var ping = RemoteInputLab.PingMilliseconds;
            var loss = RemoteInputLab.EstimatedPacketLossPercent;
            var text = "PING " + (ping < 0 ? "--" : ping.ToString()) + " ms   " +
                "PÉRDIDA " + (loss < 0 ? "--" : loss.ToString()) + "%";
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
            };
            if ((ping >= 0 && ping > 160) || loss > 8)
                style.normal.textColor = new Color(1f, 0.45f, 0.35f);
            else if ((ping >= 0 && ping > 90) || loss > 3)
                style.normal.textColor = new Color(1f, 0.82f, 0.35f);
            else
                style.normal.textColor = new Color(0.75f, 1f, 0.75f);
            GUI.Box(new Rect(Screen.width - 255, 14, 240, 30), text, style);
        }

        private void DrawSessionInterruptionOverlay()
        {
            if (!RemoteInputLab.SessionOverlayVisible)
                return;

            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            var width = Mathf.Min(620f, Screen.width - 40f);
            var height = RemoteInputLab.CanLeaveInterruptedSession ? 250f : 200f;
            GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f, width, height), GUI.skin.box);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
            titleStyle.normal.textColor = Color.white;
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                wordWrap = true,
            };
            bodyStyle.normal.textColor = Color.white;

            GUILayout.Space(14);
            GUILayout.Label(RemoteInputLab.SessionIsResuming ?
                "REANUDANDO" : "PARTIDA EN PAUSA", titleStyle);
            GUILayout.Space(12);
            if (RemoteInputLab.SessionIsResuming)
                GUILayout.Label(RemoteInputLab.SessionResumeSeconds.ToString(), titleStyle);
            else
                GUILayout.Label(RemoteInputLab.SessionHoldReason +
                    " La partida continuará cuando regrese.", bodyStyle);

            if (RemoteInputLab.CanLeaveInterruptedSession)
            {
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.Space(35);
                if (GUILayout.Button("SEGUIR ESPERANDO", GUILayout.Height(38)))
                {
                    // Esperar es el comportamiento predeterminado; el botón sirve
                    // para dejar explícita la elección sin alterar la sesión.
                }
                GUILayout.Space(12);
                if (GUILayout.Button("SALIR DE LA PARTIDA", GUILayout.Height(38)))
                    StopOnline();
                GUILayout.Space(35);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(14);
            GUILayout.EndArea();
            GUI.color = previousColor;
        }

        private void DrawOnlineWindow(int id)
        {
            GUILayout.Space(8);
            GUILayout.Label("Crea una sala o escribe el código de tu amigo.");
            if (RemoteInputLab.Enabled)
            {
                if (GUILayout.Button(RemoteInputLab.IsConnected ? "DESCONECTAR" : "CANCELAR",
                    GUILayout.Height(36)))
                    StopOnline();
            }
            else
            {
                if (GUILayout.Button("CREAR PARTIDA", GUILayout.Height(36)))
                    StartOnline(true);

                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Código", GUILayout.Width(55));
                joinCode = GUILayout.TextField(joinCode.ToUpperInvariant(), 6, GUILayout.Height(28));
                GUILayout.EndHorizontal();
                if (GUILayout.Button("UNIRSE", GUILayout.Height(36)))
                    StartOnline(false);
            }

            var code = RemoteInputLab.CurrentRoomCode;
            if (!string.IsNullOrEmpty(code))
            {
                GUILayout.Label("Sala: " + code);
                if (GUILayout.Button("COPIAR CÓDIGO", GUILayout.Height(30)))
                    CopyRoomCode();
            }
            GUILayout.Label("Estado: " + TransportStatus);
            if (!string.IsNullOrEmpty(onlineMessage))
                GUILayout.Label(onlineMessage);
            if (GUILayout.Button("VOLVER")) CloseOnlineWindow();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void StartOnline(bool host)
        {
            try
            {
                RemoteInputLab.StartInternet(host, signalingUrl.Value, stunHost.Value,
                    stunPort.Value, joinCode);
                onlineMessage = "";
            }
            catch (System.Exception ex)
            {
                onlineMessage = "Error: " + ex.Message;
            }
        }

        internal void CreateRoom()
        {
            StartOnline(true);
        }

        internal void JoinRoom(string code)
        {
            joinCode = NormalizeRoomCode(code);
            StartOnline(false);
        }

        internal void StopOnline()
        {
            try
            {
                var wasConnected = RemoteInputLab.IsConnected;
                RemoteInputLab.StopSession();
                onlineMessage = wasConnected ? "Sesión desconectada." : "Conexión cancelada.";
            }
            catch (System.Exception ex)
            {
                onlineMessage = "Error al cerrar la sesión: " + ex.Message;
            }
        }

        internal bool CopyRoomCode()
        {
            var code = RemoteInputLab.Enabled ? RemoteInputLab.CurrentRoomCode : string.Empty;
            if (string.IsNullOrEmpty(code))
            {
                onlineMessage = "La sala todavía no tiene código.";
                return false;
            }

            GUIUtility.systemCopyBuffer = code;
            onlineMessage = "Código " + code + " copiado.";
            return true;
        }

        internal string OnlineMessage => onlineMessage;
        internal string TransportStatus => RemoteInputLab.TransportStatus;

        internal void HideFallbackOnlineWindow()
        {
            showOnlineMenu = false;
        }

        private void CloseOnlineWindow()
        {
            if (RemoteInputLab.Enabled && !RemoteInputLab.IsConnected)
            {
                StopOnline();
                if (RemoteInputLab.Enabled)
                    return;
            }
            showOnlineMenu = false;
        }

        private static string NormalizeRoomCode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            value = value.Trim().ToUpperInvariant();
            return value.Length <= 6 ? value : value.Substring(0, 6);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo("Escena cargada: " + scene.name + " (" + mode + ")");
            RemoteInputLab.OnSceneLoaded(scene.name, mode);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                focusLossRecorded = true;
                focusLostFrame = Time.frameCount;
                focusLostRealtime = Time.realtimeSinceStartup;
                Logger.LogInfo("[Focus] Cuphead perdió el foco. frame=" +
                    focusLostFrame + " runInBackground=" + Application.runInBackground +
                    " sesión=" + RemoteInputLab.Enabled + ".");
                return;
            }

            var details = string.Empty;
            if (focusLossRecorded)
            {
                details = " framesEnSegundoPlano=" + (Time.frameCount - focusLostFrame) +
                    " segundos=" + (Time.realtimeSinceStartup - focusLostRealtime).ToString("0.00");
                focusLossRecorded = false;
            }
            Logger.LogInfo("[Focus] Cuphead recuperó el foco. frame=" + Time.frameCount +
                details + " runInBackground=" + Application.runInBackground +
                " sesión=" + RemoteInputLab.Enabled + ".");
        }

        private void OnApplicationPause(bool paused)
        {
            Logger.LogInfo("[Focus] OnApplicationPause=" + paused +
                " frame=" + Time.frameCount +
                " runInBackground=" + Application.runInBackground + ".");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (harmony != null)
                harmony.UnpatchSelf();
            RemoteInputLab.Shutdown();
            if (Instance == this)
                Instance = null;
        }
    }
}
