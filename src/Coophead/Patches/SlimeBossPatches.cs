using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Coophead.Patches
{
    // El invitado conserva los objetos de las tres formas, pero no ejecuta su IA.
    // La forma, posición y animación visibles llegan desde el host en LateUpdate.
    [HarmonyPatch]
    internal static class SlimeClientSimulationPatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new[]
            {
                AccessTools.Method(typeof(SlimeLevel), "OnLevelStart"),
                AccessTools.Method(typeof(SlimeLevelSlime), "StartJump"),
                AccessTools.Method(typeof(SlimeLevelSlime), "StartPunch"),
                AccessTools.Method(typeof(SlimeLevelSlime), "Transform"),
                AccessTools.Method(typeof(SlimeLevelSlime), "TurnBig"),
                AccessTools.Method(typeof(SlimeLevelSlime), "DeathTransform"),
                AccessTools.Method(typeof(SlimeLevelSlime), "Explode"),
                AccessTools.Method(typeof(SlimeLevelSlime), "PunchTurn"),
                AccessTools.Method(typeof(SlimeLevelSlime), "OnBossDeath"),
                AccessTools.Method(typeof(SlimeLevelSlime), "OnCollisionPlayer"),
                AccessTools.Method(typeof(SlimeLevelTombstone), "StartIntro"),
                AccessTools.Method(typeof(SlimeLevelTombstone), "StartMove"),
                AccessTools.Method(typeof(SlimeLevelTombstone), "StartSmash"),
                AccessTools.Method(typeof(SlimeLevelTombstone), "OnBossDeath"),
                AccessTools.Method(typeof(SlimeLevelTombstone), "OnCollisionPlayer"),
            };
            for (var i = 0; i < methods.Length; i++)
            {
                if (methods[i] != null)
                    yield return methods[i];
            }
        }

        private static bool Prefix()
        {
            return !SlimeBossSynchronizer.ShouldSuppressClientSimulation ||
                SlimeBossSynchronizer.ApplyingAuthoritativeBossEvent;
        }
    }

    // Los proyectiles siguen impactando visualmente, pero el invitado no puede
    // decidir HP, hit-pause ni fases del jefe.
    [HarmonyPatch]
    internal static class SlimeBossDamageReceiverPatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DamageReceiver), "TakeDamage");
            yield return AccessTools.Method(typeof(DamageReceiver),
                "TakeDamageBruteForce");
        }

        private static bool Prefix(DamageReceiver __instance)
        {
            if (!SlimeBossSynchronizer.ShouldSuppressClientSimulation ||
                __instance == null)
                return true;
            return __instance.GetComponent<SlimeLevelSlime>() == null &&
                __instance.GetComponent<SlimeLevelTombstone>() == null;
        }
    }

    // El contacto que ve el invitado puede llevar hasta un RTT de diferencia. Sólo
    // se reproduce el golpe cuando aparece en el snapshot autoritativo del host.
    [HarmonyPatch(typeof(PlayerDamageReceiver), "TakeDamage")]
    internal static class SlimePlayerDamageReceiverPatch
    {
        private static bool Prefix()
        {
            return !SlimeBossSynchronizer.ShouldSuppressLocalPlayerDamage;
        }
    }
}
