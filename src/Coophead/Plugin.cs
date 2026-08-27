using BepInEx;
using UnityEngine.SceneManagement;

namespace Coophead
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Cuphead.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mx.gilomx.coophead";
        public const string PluginName = "Co-ophead";
        public const string PluginVersion = "0.1.0-dev";

        private void Awake()
        {
            Logger.LogInfo(PluginName + " " + PluginVersion + " cargado.");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo("Escena cargada: " + scene.name + " (" + mode + ")");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}
