using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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
        public const string PluginVersion = "0.9.0";

        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        private Harmony harmony;
        private ConfigEntry<InputTransportMode> transportMode;
        private ConfigEntry<string> lanHostAddress;
        private ConfigEntry<int> lanPort;
        private ConfigEntry<string> relayAddress;
        private ConfigEntry<int> relayPort;
        private ConfigEntry<string> roomCode;

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
            RemoteInputLab.Tick();
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
