using HarmonyLib;
using Rewired;

namespace Coophead.Patches
{
    internal static class RewiredPlayerPatchGuard
    {
        public static bool ShouldOverridePlayerTwo(Player player)
        {
            var shouldOverride = RemoteInputLab.ShouldOverridePlayerTwo(player);
            if (shouldOverride)
                RemoteInputLab.ReportRewiredRead();
            return shouldOverride;
        }

        public static bool ShouldSuppressPlayerOne(Player player)
        {
            return RemoteInputLab.ShouldSuppressPlayerOne(player);
        }

        public static bool ShouldOverridePlayerOneVisual(Player player)
        {
            return RemoteInputLab.ShouldOverridePlayerOneVisual(player);
        }
    }

    [HarmonyPatch(typeof(Player), "GetAxis", new[] { typeof(int) })]
    internal static class RewiredPlayerGetAxisPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref float __result)
        {
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerTwo(__instance))
            {
                __result = RemoteInputLab.GetAxis(actionId);
                return false;
            }
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerOneVisual(__instance))
            {
                __result = RemoteInputLab.GetRemotePlayerOneAxis(actionId);
                return false;
            }
            if (!RewiredPlayerPatchGuard.ShouldSuppressPlayerOne(__instance))
                return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetAxisRaw", new[] { typeof(int) })]
    internal static class RewiredPlayerGetAxisRawPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref float __result)
        {
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerTwo(__instance))
            {
                __result = RemoteInputLab.GetAxis(actionId);
                return false;
            }
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerOneVisual(__instance))
            {
                __result = RemoteInputLab.GetRemotePlayerOneAxis(actionId);
                return false;
            }
            if (!RewiredPlayerPatchGuard.ShouldSuppressPlayerOne(__instance))
                return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetButton", new[] { typeof(int) })]
    internal static class RewiredPlayerGetButtonPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref bool __result)
        {
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerTwo(__instance))
            {
                __result = RemoteInputLab.GetButton(actionId, ButtonPhase.Held);
                return false;
            }
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerOneVisual(__instance))
            {
                __result = RemoteInputLab.GetRemotePlayerOneButton(actionId,
                    ButtonPhase.Held);
                return false;
            }
            if (!RewiredPlayerPatchGuard.ShouldSuppressPlayerOne(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetButtonDown", new[] { typeof(int) })]
    internal static class RewiredPlayerGetButtonDownPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref bool __result)
        {
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerTwo(__instance))
            {
                __result = RemoteInputLab.GetButton(actionId, ButtonPhase.Pressed);
                return false;
            }
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerOneVisual(__instance))
            {
                __result = RemoteInputLab.GetRemotePlayerOneButton(actionId,
                    ButtonPhase.Pressed);
                return false;
            }
            if (!RewiredPlayerPatchGuard.ShouldSuppressPlayerOne(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetButtonUp", new[] { typeof(int) })]
    internal static class RewiredPlayerGetButtonUpPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref bool __result)
        {
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerTwo(__instance))
            {
                __result = RemoteInputLab.GetButton(actionId, ButtonPhase.Released);
                return false;
            }
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerOneVisual(__instance))
            {
                __result = RemoteInputLab.GetRemotePlayerOneButton(actionId,
                    ButtonPhase.Released);
                return false;
            }
            if (!RewiredPlayerPatchGuard.ShouldSuppressPlayerOne(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "GetButtonTimePressed", new[] { typeof(int) })]
    internal static class RewiredPlayerGetButtonTimePressedPatch
    {
        private static bool Prefix(Player __instance, int actionId, ref float __result)
        {
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerTwo(__instance))
            {
                __result = RemoteInputLab.GetButtonTimePressed(actionId);
                return false;
            }
            if (RewiredPlayerPatchGuard.ShouldOverridePlayerOneVisual(__instance))
            {
                __result = RemoteInputLab.GetRemotePlayerOneButtonTimePressed(actionId);
                return false;
            }
            if (!RewiredPlayerPatchGuard.ShouldSuppressPlayerOne(__instance))
                return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerInput), "GetAxis", new[] { typeof(PlayerInput.Axis) })]
    internal static class PlayerInputGetAxisPatch
    {
        private static bool Prefix(PlayerInput __instance, PlayerInput.Axis axis,
            ref float __result)
        {
            float horizontal;
            float vertical;
            if (!RemoteInputLab.TryGetPlayerInputAxes(__instance, out horizontal,
                out vertical))
                return true;
            __result = axis == PlayerInput.Axis.X ? horizontal : vertical;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerInput), "GetAxisInt", new[]
    {
        typeof(PlayerInput.Axis), typeof(bool), typeof(bool)
    })]
    internal static class PlayerInputGetAxisIntPatch
    {
        private static bool Prefix(PlayerInput __instance, PlayerInput.Axis axis,
            bool crampedDiagonal, bool duckMod, ref int __result)
        {
            float horizontal;
            float vertical;
            if (!RemoteInputLab.TryGetPlayerInputAxes(__instance, out horizontal,
                out vertical))
                return true;

            var magnitude = UnityEngine.Mathf.Sqrt(horizontal * horizontal +
                vertical * vertical);
            if (magnitude < 0.375f)
            {
                __result = 0;
                return false;
            }

            var threshold = crampedDiagonal ? 0.5f : 0.38268f;
            var component = (axis == PlayerInput.Axis.X ? horizontal : vertical) /
                magnitude;
            if (component > threshold)
                __result = 1;
            else if (component < (duckMod ? -0.705f : -threshold))
                __result = -1;
            else
                __result = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerInput), "GetButton", new[] { typeof(CupheadButton) })]
    internal static class PlayerInputGetButtonPatch
    {
        private static bool Prefix(PlayerInput __instance, CupheadButton button,
            ref bool __result)
        {
            return !RemoteInputLab.TryGetPlayerInputButton(__instance, button,
                out __result);
        }
    }
}
