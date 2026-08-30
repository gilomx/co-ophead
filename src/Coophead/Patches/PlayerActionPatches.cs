using HarmonyLib;

namespace Coophead.Patches
{
    [HarmonyPatch(typeof(PlayerStatsManager), "OnEx")]
    internal static class PlayerStatsOnExPatch
    {
        private static void Prefix(PlayerStatsManager __instance, ref float __state)
        {
            __state = __instance == null ? 0f : __instance.SuperMeter;
        }

        private static void Postfix(PlayerStatsManager __instance, float __state)
        {
            RemoteInputLab.NotifySuperConsumed(__instance, __state, false);
        }
    }

    [HarmonyPatch(typeof(PlayerStatsManager), "OnSuper")]
    internal static class PlayerStatsOnSuperPatch
    {
        private static void Prefix(PlayerStatsManager __instance, ref float __state)
        {
            __state = __instance == null ? 0f : __instance.SuperMeter;
        }

        private static void Postfix(PlayerStatsManager __instance, float __state)
        {
            RemoteInputLab.NotifySuperConsumed(__instance, __state, true);
        }
    }
}
