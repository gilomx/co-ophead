using BepInEx;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace Coophead
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Cuphead.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mx.gilomx.coophead";
        public const string PluginName = "Co-ophead";
        public const string PluginVersion = "0.2.0";

        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo(PluginName + " " + PluginVersion + " cargado.");
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
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (harmony != null)
                harmony.UnpatchSelf();
        }
    }
}
