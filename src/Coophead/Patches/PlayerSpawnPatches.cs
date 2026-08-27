using HarmonyLib;

namespace Coophead.Patches
{
    [HarmonyPatch(typeof(PlayerManager), "Awake")]
    internal static class PlayerManagerAwakePatch
    {
        private static void Postfix()
        {
            RemoteInputLab.EnsureMultiplayerState();
        }
    }

    [HarmonyPatch(typeof(Map), "Awake")]
    internal static class MapAwakePatch
    {
        private static void Prefix()
        {
            RemoteInputLab.EnsureMultiplayerState();
        }
    }

    [HarmonyPatch(typeof(Map), "CreatePlayers")]
    internal static class MapCreatePlayersPatch
    {
        private static void Prefix()
        {
            RemoteInputLab.EnsureMultiplayerState();
        }
    }

    [HarmonyPatch(typeof(Level), "Start")]
    internal static class LevelStartPatch
    {
        private static void Prefix()
        {
            RemoteInputLab.EnsureMultiplayerState();
        }
    }
}
