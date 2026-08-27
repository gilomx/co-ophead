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
        public const string PluginVersion = "0.10.0";

        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        private Harmony harmony;
        private ConfigEntry<InputTransportMode> transportMode;
        private ConfigEntry<string> lanHostAddress;
        private ConfigEntry<int> lanPort;
        private ConfigEntry<string> relayAddress;
        private ConfigEntry<int> relayPort;
        private ConfigEntry<string> roomCode;
        private bool showOnlineMenu;
        private string joinCode = "";
        private string onlineMessage = "";
        private Rect onlineWindow = new Rect(30, 30, 390, 250);

        private void Awake()
        {
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
            try
            {
                RemoteInputLab.Configure(transportMode.Value, lanHostAddress.Value, lanPort.Value,
                    relayAddress.Value, relayPort.Value, roomCode.Value);
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
            if (Input.GetKeyDown(KeyCode.F6))
                showOnlineMenu = !showOnlineMenu;
            RemoteInputLab.Tick();
        }

        private void OnGUI()
        {
            if (!showOnlineMenu)
            {
                if (GUI.Button(new Rect(20, 20, 170, 38), "CO-OPHEAD ONLINE [F6]"))
                    showOnlineMenu = true;
                return;
            }
            onlineWindow = GUI.Window(78216, onlineWindow, DrawOnlineWindow, "CO-OPHEAD ONLINE");
        }

        private void DrawOnlineWindow(int id)
        {
            GUILayout.Space(8);
            GUILayout.Label("Crea una sala o escribe el código de tu amigo.");
            if (GUILayout.Button("CREAR PARTIDA", GUILayout.Height(36)))
                StartOnline(true);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Código", GUILayout.Width(55));
            joinCode = GUILayout.TextField(joinCode.ToUpperInvariant(), 6, GUILayout.Height(28));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("UNIRSE", GUILayout.Height(36)))
                StartOnline(false);

            var code = RemoteInputLab.CurrentRoomCode;
            if (!string.IsNullOrEmpty(code))
                GUILayout.Label("Sala: " + code);
            GUILayout.Label(string.IsNullOrEmpty(onlineMessage)
                ? "Estado: " + RemoteInputLab.TransportStatus
                : onlineMessage);
            if (GUILayout.Button("CERRAR")) showOnlineMenu = false;
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void StartOnline(bool host)
        {
            try
            {
                RemoteInputLab.StartInternet(host, relayAddress.Value, relayPort.Value, joinCode);
                onlineMessage = host ? "Creando sala..." : "Uniéndose a la sala...";
            }
            catch (System.Exception ex)
            {
                onlineMessage = "Error: " + ex.Message;
            }
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
        }
    }
}
