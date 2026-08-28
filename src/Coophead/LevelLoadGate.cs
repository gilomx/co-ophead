using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Coophead
{
    internal static class LevelLoadGate
    {
        private const float WaitingMessageDelay = 2f;

        private static bool active;
        private static bool localReady;
        private static bool guestReady;
        private static bool releaseAnnounced;
        private static bool loaderReleased;
        private static int levelId = -1;
        private static float waitingSinceRealtime;
        private static float localReleaseAtRealtime;

        public static bool ShouldReportReady => active && localReady &&
            RemoteInputLab.IsClientSession && !releaseAnnounced;
        public static bool ReleaseAnnounced => active && releaseAnnounced;
        public static bool IsHoldingGameplay => active && !loaderReleased;
        public static bool ShowHostWaitingMessage => active && localReady &&
            RemoteInputLab.IsHostSession && !guestReady &&
            !RemoteInputLab.SessionOverlayVisible &&
            Time.realtimeSinceStartup - waitingSinceRealtime >= WaitingMessageDelay;
        public static string HostWaitingMessage => "TU INVITADO TALENTO...";

        public static void OnSceneLoaded(string sceneName)
        {
            Reset();
            if (!RemoteInputLab.Enabled ||
                (!RemoteInputLab.IsHostSession && !RemoteInputLab.IsClientSession) ||
                string.IsNullOrEmpty(sceneName) ||
                !sceneName.StartsWith("scene_level_"))
                return;

            active = true;
            try { levelId = (int)SceneLoader.CurrentLevel; }
            catch { levelId = -1; }
            Plugin.Log.LogInfo("[ReadyGate] Preparando compuerta para " + sceneName + ".");
        }

        public static void OnLevelStarted()
        {
            if (!active || !RemoteInputLab.Enabled)
                return;

            try
            {
                if (Level.Current != null)
                    levelId = (int)Level.Current.CurrentLevel;
            }
            catch { }

            localReady = true;
            waitingSinceRealtime = Time.realtimeSinceStartup;
            RemoteInputLab.SetLevelGatePause(true);
            Plugin.Log.LogMessage("[ReadyGate] Nivel local listo: " + levelId + ".");
        }

        public static bool OnGuestReady()
        {
            if (!active || !localReady || !RemoteInputLab.IsHostSession ||
                releaseAnnounced)
                return false;

            guestReady = true;
            releaseAnnounced = true;
            var ping = RemoteInputLab.PingMilliseconds;
            var alignmentDelay = ping < 0 ? 0.15f : Mathf.Clamp(ping / 2000f, 0.02f, 0.35f);
            localReleaseAtRealtime = Time.realtimeSinceStartup + alignmentDelay;
            Plugin.Log.LogMessage("[ReadyGate] Invitado listo; abriendo el iris.");
            return true;
        }

        public static void OnHostRelease(int remoteLevelId)
        {
            if (!active || !localReady || !RemoteInputLab.IsClientSession ||
                loaderReleased)
                return;
            if (levelId >= 0 && remoteLevelId >= 0 && levelId != remoteLevelId)
                return;

            releaseAnnounced = true;
            localReleaseAtRealtime = Time.realtimeSinceStartup;
            Plugin.Log.LogMessage("[ReadyGate] Host e invitado listos; abriendo el iris.");
        }

        public static void Update()
        {
            if (!active || !releaseAnnounced || loaderReleased ||
                Time.realtimeSinceStartup < localReleaseAtRealtime)
                return;

            loaderReleased = true;
            RemoteInputLab.SetLevelGatePause(false);
        }

        public static bool ShouldHoldLoaderExit()
        {
            return active && !loaderReleased;
        }

        public static void Reset()
        {
            var wasActive = active;
            active = false;
            localReady = false;
            guestReady = false;
            releaseAnnounced = false;
            loaderReleased = false;
            levelId = -1;
            waitingSinceRealtime = 0f;
            localReleaseAtRealtime = 0f;
            if (wasActive)
                RemoteInputLab.SetLevelGatePause(false);
        }
    }

    [HarmonyPatch(typeof(Level), "Start")]
    internal static class LevelLoadGateLevelStartPatch
    {
        private static void Postfix()
        {
            LevelLoadGate.OnLevelStarted();
        }
    }

    [HarmonyPatch]
    internal static class LevelLoadGateSceneLoaderLoopPatch
    {
        private static FieldInfo programCounterField;

        private static MethodBase TargetMethod()
        {
            var nestedTypes = typeof(SceneLoader).GetNestedTypes(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var nestedType in nestedTypes)
            {
                if (!nestedType.Name.StartsWith("<loop_cr>"))
                    continue;
                programCounterField = AccessTools.Field(nestedType, "$PC");
                return AccessTools.Method(nestedType, "MoveNext");
            }
            return null;
        }

        private static bool Prefix(object __instance, ref bool __result)
        {
            if (!LevelLoadGate.ShouldHoldLoaderExit() ||
                programCounterField == null || __instance == null)
                return true;

            var programCounter = (int)programCounterField.GetValue(__instance);
            if (programCounter != 4)
                return true;

            // Estado 4 es el punto confirmado entre UnloadUnusedAssets y el fade
            // del reloj de arena. Devolver true mantiene viva la coroutine sin
            // permitir que empiece la apertura del iris.
            __result = true;
            return false;
        }
    }
}
