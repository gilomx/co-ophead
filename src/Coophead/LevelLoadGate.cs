using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Coophead
{
    internal static class LevelLoadGate
    {
        private const float WaitingMessageDelay = 2f;
        private const float CancelOptionDelay = 12f;

        private static bool active;
        private static bool sceneActivated;
        private static bool levelStarted;
        private static bool requiresLevelStart;
        private static bool localReady;
        private static bool guestReady;
        private static bool releaseAnnounced;
        private static bool loaderReleased;
        private static int levelId = -1;
        private static uint transitionId;
        private static string targetScene = string.Empty;
        private static float waitingSinceRealtime;
        private static float localReleaseAtRealtime;
        private static bool hostReleasePending;
        private static int pendingHostLevelId = -1;
        private static uint pendingHostTransitionId;

        public static bool IsActive => active;
        public static uint TransitionId => active ? transitionId : 0;
        public static bool ShouldReportReady => active && localReady &&
            RemoteInputLab.IsClientSession && !releaseAnnounced &&
            RemoteInputLab.IsClientReadyForLoadGate(targetScene);
        public static bool ReleaseAnnounced => active && releaseAnnounced;
        public static bool IsHoldingGameplay => active && !loaderReleased;
        public static bool ShowHostWaitingMessage => active && localReady &&
            RemoteInputLab.IsHostSession && !guestReady && !loaderReleased &&
            !RemoteInputLab.SessionOverlayVisible &&
            Time.realtimeSinceStartup - waitingSinceRealtime >= WaitingMessageDelay;
        public static string HostWaitingMessage => "TU INVITADO TALENTO...";
        public static bool CanAbort => active && !loaderReleased &&
            RemoteInputLab.IsHostSession && !guestReady &&
            Time.realtimeSinceStartup - waitingSinceRealtime >= CancelOptionDelay;
        public static bool TargetIsLevel => active &&
            targetScene.StartsWith("scene_level_");

        public static void BeginTransition(string sceneName, int requestedLevelId,
            uint requestedTransitionId)
        {
            if (!RemoteInputLab.Enabled || !IsCoordinatedScene(sceneName) ||
                requestedTransitionId == 0)
            {
                Reset();
                return;
            }

            if (active && transitionId == requestedTransitionId &&
                targetScene == sceneName)
                return;

            Reset();
            active = true;
            targetScene = sceneName;
            levelId = requestedLevelId;
            transitionId = requestedTransitionId;
            requiresLevelStart = sceneName.StartsWith("scene_level_");
            waitingSinceRealtime = Time.realtimeSinceStartup;
            Plugin.Log.LogInfo("[ReadyGate] Transición #" + transitionId +
                " anunciada antes de cargar " + targetScene + ".");
        }

        public static void OnSceneLoaded(string sceneName)
        {
            if (!active)
                return;
            if (sceneName == "scene_load_helper")
                return;
            if (sceneName != targetScene)
            {
                Reset();
                return;
            }

            sceneActivated = true;
            try { levelId = (int)SceneLoader.CurrentLevel; }
            catch { }
            Plugin.Log.LogInfo("[ReadyGate] Escena objetivo activada para transición #" +
                transitionId + ".");
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

            levelStarted = true;
            RemoteInputLab.SetLevelGatePause(true);
            Plugin.Log.LogInfo("[ReadyGate] Level.Start alcanzado para transición #" +
                transitionId + "; esperando al cargador.");
        }

        public static void OnLoaderExitReached()
        {
            if (!active || localReady || !sceneActivated ||
                (requiresLevelStart && !levelStarted) ||
                SceneLoader.SceneName != targetScene ||
                (!requiresLevelStart &&
                    !RemoteInputLab.IsClientReadyForLoadGate(targetScene)))
                return;

            MarkLocalReady("Loader retenido antes de ocultar el reloj");
        }

        public static void AdoptAlreadyLoadedTransition(string sceneName)
        {
            if (!active || sceneName != targetScene || localReady)
                return;

            sceneActivated = true;
            if (requiresLevelStart)
                levelStarted = Level.Current != null;
            if ((requiresLevelStart && !levelStarted) ||
                (!requiresLevelStart &&
                    !RemoteInputLab.IsClientReadyForLoadGate(targetScene)))
                return;

            MarkLocalReady("Transición adoptada después de activar la escena");
        }

        private static void MarkLocalReady(string reason)
        {
            if (localReady)
                return;

            localReady = true;
            // En mapas, el propio start_cr todavía puede necesitar tiempo de juego
            // para crear jugadores y colocar la cámara. Sólo retenemos el iris;
            // pausar TimeScale aquí impediría alcanzar Map.State.Ready.
            if (requiresLevelStart)
                RemoteInputLab.SetLevelGatePause(true);
            Plugin.Log.LogMessage("[ReadyGate] " + reason + "; transición #" +
                transitionId + " lista localmente.");
            ApplyPendingHostRelease();
        }

        public static bool OnGuestReady(uint remoteTransitionId)
        {
            if (!active || !localReady || !RemoteInputLab.IsHostSession ||
                releaseAnnounced || remoteTransitionId == 0 ||
                remoteTransitionId != transitionId)
                return false;

            guestReady = true;
            releaseAnnounced = true;
            var ping = RemoteInputLab.PingMilliseconds;
            var alignmentDelay = ping < 0 ? 0.15f :
                Mathf.Clamp(ping / 2000f, 0.02f, 0.35f);
            localReleaseAtRealtime = Time.realtimeSinceStartup + alignmentDelay;
            Plugin.Log.LogMessage("[ReadyGate] Invitado listo para transición #" +
                transitionId + "; abriendo el iris.");
            return true;
        }

        public static void OnHostRelease(int remoteLevelId, uint remoteTransitionId)
        {
            if (!active || !RemoteInputLab.IsClientSession ||
                loaderReleased || remoteTransitionId == 0 ||
                remoteTransitionId != transitionId)
                return;

            if (!localReady)
            {
                hostReleasePending = true;
                pendingHostLevelId = remoteLevelId;
                pendingHostTransitionId = remoteTransitionId;
                Plugin.Log.LogInfo("[ReadyGate] Liberación del host recibida antes de " +
                    "terminar la carga local; quedó pendiente para transición #" +
                    transitionId + ".");
                return;
            }

            ApplyHostRelease(remoteLevelId, remoteTransitionId);
        }

        private static void ApplyPendingHostRelease()
        {
            if (!hostReleasePending || !localReady ||
                pendingHostTransitionId != transitionId)
                return;
            var pendingLevel = pendingHostLevelId;
            var pendingTransition = pendingHostTransitionId;
            hostReleasePending = false;
            pendingHostLevelId = -1;
            pendingHostTransitionId = 0;
            ApplyHostRelease(pendingLevel, pendingTransition);
        }

        private static void ApplyHostRelease(int remoteLevelId, uint remoteTransitionId)
        {
            if (levelId >= 0 && remoteLevelId >= 0 && levelId != remoteLevelId)
                Plugin.Log.LogWarning("[ReadyGate] El LevelId local difiere del host " +
                    "para la transición #" + transitionId + "; se acepta el " +
                    "TransitionId autoritativo.");

            releaseAnnounced = true;
            localReleaseAtRealtime = Time.realtimeSinceStartup;
            Plugin.Log.LogMessage("[ReadyGate] Host liberó transición #" +
                transitionId + "; abriendo el iris.");
        }

        public static void Update()
        {
            // Fallback exclusivo para una transición adoptada cuando la escena ya
            // estaba activa. Durante una carga normal, Ready sólo puede marcarse
            // desde PC0 de iconFadeOut_cr para no liberar el gate antes de tiempo.
            if (active && !localReady && !requiresLevelStart && sceneActivated &&
                !SceneLoader.CurrentlyLoading &&
                SceneLoader.SceneName == targetScene &&
                RemoteInputLab.IsClientReadyForLoadGate(targetScene))
                MarkLocalReady("Mapa inicializado y alineado");

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

        public static void ReleaseAndResetForSupersedingTransition(
            uint supersededTransitionId)
        {
            if (!active)
                return;

            var releasedTransitionId = transitionId;
            if (supersededTransitionId != 0 &&
                releasedTransitionId != supersededTransitionId)
                Plugin.Log.LogWarning("[ReadyGate] El gate activo #" +
                    releasedTransitionId + " no coincidía con la transición " +
                    "suplantada #" + supersededTransitionId +
                    "; se libera para evitar retener el loader anterior.");
            else
                Plugin.Log.LogMessage("[ReadyGate] Transición #" +
                    releasedTransitionId + " suplantada; se libera su loader.");

            // ShouldHoldLoaderExit deja de aplicar en cuanto Reset desactiva el
            // gate. B todavía no se anuncia: primero debe terminar el loader de A.
            Reset();
        }

        public static void Reset()
        {
            var wasActive = active;
            active = false;
            sceneActivated = false;
            levelStarted = false;
            requiresLevelStart = false;
            localReady = false;
            guestReady = false;
            releaseAnnounced = false;
            loaderReleased = false;
            levelId = -1;
            transitionId = 0;
            targetScene = string.Empty;
            waitingSinceRealtime = 0f;
            localReleaseAtRealtime = 0f;
            hostReleasePending = false;
            pendingHostLevelId = -1;
            pendingHostTransitionId = 0;
            if (wasActive)
                RemoteInputLab.SetLevelGatePause(false);
        }

        private static bool IsCoordinatedScene(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName) &&
                (sceneName.StartsWith("scene_level_") ||
                sceneName.StartsWith("scene_map_"));
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
                if (!nestedType.Name.StartsWith("<iconFadeOut_cr>"))
                    continue;
                programCounterField = AccessTools.Field(nestedType, "$PC");
                return AccessTools.Method(nestedType, "MoveNext");
            }
            return null;
        }

        private static bool Prefix(object __instance, ref bool __result)
        {
            if (programCounterField == null || __instance == null)
                return true;

            var programCounter = (int)programCounterField.GetValue(__instance);
            if (programCounter != 0)
                return true;

            LevelLoadGate.OnLoaderExitReached();
            if (!LevelLoadGate.ShouldHoldLoaderExit())
                return true;

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(SceneLoader), "LoadScene", new[]
    {
        typeof(Scenes), typeof(SceneLoader.Transition),
        typeof(SceneLoader.Transition), typeof(SceneLoader.Icon),
        typeof(SceneLoader.Context)
    })]
    internal static class SceneLoaderTransitionAnnouncementPatch
    {
        private static bool Prefix(Scenes scene)
        {
            if (SceneLoader.CurrentlyLoading)
                return true;
            return RemoteInputLab.OnSceneLoadRequested(scene.ToString());
        }
    }
}
