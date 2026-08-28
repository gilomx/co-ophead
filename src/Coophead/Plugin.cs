using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Coophead.Transport;

namespace Coophead
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Cuphead.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mx.gilomx.coophead";
        public const string PluginName = "Co-ophead";
        public const string PluginVersion = "0.12.4";

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
        private bool showOnlineMenu;
        private string joinCode = "";
        private string onlineMessage = "";
        private Rect onlineWindow = new Rect(30, 30, 390, 300);

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

        private void OnGUI()
        {
            if (!showOnlineMenu)
                return;
            onlineWindow = GUI.Window(78216, onlineWindow, DrawOnlineWindow, "CO-OPHEAD");
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
