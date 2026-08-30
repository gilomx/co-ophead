using HarmonyLib;

namespace Coophead.Patches
{
    [HarmonyPatch(typeof(MapLevelLoader), "Activate", new[]
    {
        typeof(MapPlayerController)
    })]
    internal static class MapLevelLoaderHostAuthorityPatch
    {
        private static bool Prefix(MapPlayerController player)
        {
            if (!RemoteInputLab.ClientMapIsHostAuthoritative)
            {
                if (player != null && player.id == PlayerId.PlayerTwo)
                    RemoteInputLab.ConsumeRemoteMapLevelInteraction();
                return true;
            }

            MapStartUiRestorer.CloseAll();
            Plugin.Log.LogMessage("[MapAuthority] El invitado pidió abrir un nivel con " +
                (player == null ? "un jugador desconocido" : player.id.ToString()) +
                "; el host resolverá la interacción.");
            return false;
        }
    }

    [HarmonyPatch(typeof(AbstractMapInteractiveEntity), "Update")]
    internal static class MapLevelInteractionInputScopePatch
    {
        private static void Prefix(AbstractMapInteractiveEntity __instance,
            out bool __state)
        {
            __state = __instance is MapLevelLoader;
            if (__state)
                RemoteInputLab.BeginMapLevelInteractionInputProbe();
        }

        private static System.Exception Finalizer(System.Exception __exception,
            bool __state)
        {
            if (__state)
                RemoteInputLab.EndMapLevelInteractionInputProbe();
            return __exception;
        }
    }

    internal static class MapStartUiRestorer
    {
        public static void CloseAll()
        {
            if (!RemoteInputLab.ClientMapIsHostAuthoritative)
                return;

            try
            {
                Close(MapDifficultySelectStartUI.Current);
                Close(MapConfirmStartUI.Current);
                Close(MapBasicStartUI.Current);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[MapAuthority] No se pudo cerrar un menú " +
                    "de entrada residual: " + ex.Message);
            }
        }

        private static void Close(AbstractMapSceneStartUI ui)
        {
            if (ui != null && (ui.CurrentState ==
                AbstractMapSceneStartUI.State.Active || ui.CurrentState ==
                AbstractMapSceneStartUI.State.Loading))
                ui.Out();
        }
    }

    [HarmonyPatch(typeof(SceneLoader), "LoadScene", new[]
    {
        typeof(Scenes), typeof(SceneLoader.Transition),
        typeof(SceneLoader.Transition), typeof(SceneLoader.Icon),
        typeof(SceneLoader.Context)
    })]
    internal static class MapStartUiSceneLoadPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix()
        {
            MapStartUiRestorer.CloseAll();
        }
    }
}
