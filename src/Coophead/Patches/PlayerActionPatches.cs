using HarmonyLib;

namespace Coophead.Patches
{
    [HarmonyPatch(typeof(PlayerStatsManager), "OnEx")]
    internal static class PlayerStatsOnExPatch
    {
        private static void Postfix(PlayerStatsManager __instance)
        {
            RemoteInputLab.NotifyPlayerOneSuperConsumed(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerStatsManager), "OnSuper")]
    internal static class PlayerStatsOnSuperPatch
    {
        private static void Postfix(PlayerStatsManager __instance)
        {
            RemoteInputLab.NotifyPlayerOneSuperConsumed(__instance);
        }
    }
}
