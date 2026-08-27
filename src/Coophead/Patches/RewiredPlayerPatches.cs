using HarmonyLib;
using Rewired;

namespace Coophead.Patches
{
    internal static class RewiredPlayerPatchGuard
    {
        public static bool ShouldOverride(Player player)
        {
            return RemoteInputLab.Enabled && player != null && player.id == 1;
        }
    }

    [HarmonyPatch(typeof(Player), "GetAxis", new[] { typeof(int) })]
    internal static class RewiredPlayerGetAxisPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref float __result)
        {
            if (!RewiredPlayerPatchGuard.ShouldOverride(__instance))
                return true;

            __result = RemoteInputLab.GetAxis(actionId);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetAxisRaw", new[] { typeof(int) })]
    internal static class RewiredPlayerGetAxisRawPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref float __result)
        {
            if (!RewiredPlayerPatchGuard.ShouldOverride(__instance))
                return true;

            __result = RemoteInputLab.GetAxis(actionId);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetButton", new[] { typeof(int) })]
    internal static class RewiredPlayerGetButtonPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref bool __result)
        {
            if (!RewiredPlayerPatchGuard.ShouldOverride(__instance))
                return true;

            __result = RemoteInputLab.GetButton(actionId, ButtonPhase.Held);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetButtonDown", new[] { typeof(int) })]
    internal static class RewiredPlayerGetButtonDownPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref bool __result)
        {
            if (!RewiredPlayerPatchGuard.ShouldOverride(__instance))
                return true;

            __result = RemoteInputLab.GetButton(actionId, ButtonPhase.Pressed);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetButtonUp", new[] { typeof(int) })]
    internal static class RewiredPlayerGetButtonUpPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref bool __result)
        {
            if (!RewiredPlayerPatchGuard.ShouldOverride(__instance))
                return true;

            __result = RemoteInputLab.GetButton(actionId, ButtonPhase.Released);
            return false;
        }
    }
}
