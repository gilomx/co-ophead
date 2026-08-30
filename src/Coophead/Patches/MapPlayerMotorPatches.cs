using HarmonyLib;
using UnityEngine;

namespace Coophead.Patches
{
    [HarmonyPatch(typeof(MapPlayerMotor), "Update")]
    internal static class ClientMapMotorUpdatePatch
    {
        private static readonly System.Reflection.FieldInfo VelocityField =
            AccessTools.Field(typeof(MapPlayerMotor), "<velocity>k__BackingField");

        private static bool Prefix(MapPlayerMotor __instance)
        {
            return !Neutralize(__instance);
        }

        internal static bool Neutralize(MapPlayerMotor motor)
        {
            if (!RemoteInputLab.ClientMapIsHostAuthoritative || motor == null)
                return false;
            if (VelocityField != null)
                VelocityField.SetValue(motor, Vector2.zero);
            var body = motor.GetComponent<Rigidbody2D>();
            if (body != null)
                body.velocity = Vector2.zero;
            return true;
        }
    }

    [HarmonyPatch(typeof(MapPlayerMotor), "LateUpdate")]
    internal static class ClientMapMotorLateUpdatePatch
    {
        private static bool Prefix(MapPlayerMotor __instance)
        {
            return !ClientMapMotorUpdatePatch.Neutralize(__instance);
        }
    }

    [HarmonyPatch(typeof(MapPlayerController), "LadderEnter")]
    internal static class ClientMapLadderEnterPatch
    {
        private static bool Prefix()
        {
            return !RemoteInputLab.ClientMapIsHostAuthoritative;
        }
    }

    [HarmonyPatch(typeof(MapPlayerController), "LadderExit")]
    internal static class ClientMapLadderExitPatch
    {
        private static bool Prefix()
        {
            return !RemoteInputLab.ClientMapIsHostAuthoritative;
        }
    }

    [HarmonyPatch(typeof(MapPlayerController), "TryActivateDjimmi")]
    internal static class ClientMapDjimmiAuthorityPatch
    {
        private static bool Prefix()
        {
            return !RemoteInputLab.IsClientSession;
        }
    }
}
