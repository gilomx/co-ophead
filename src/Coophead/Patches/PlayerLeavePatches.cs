using HarmonyLib;

namespace Coophead.Patches
{
    // Sólo la opción explícita "Remove Player 2" del menú de pausa debe cerrar
    // la sesión completa. PlayerManager.PlayerLeave también se usa para cambios
    // de dispositivo y cierre de sesión del sistema, así que parchearlo globalmente
    // convertiría eventos de hardware en expulsiones voluntarias.
    [HarmonyPatch(typeof(LevelPauseGUI), "Player2Leave")]
    internal static class PlayerTwoSessionLeavePatch
    {
        private static readonly System.Reflection.MethodInfo UnpauseMethod =
            AccessTools.Method(typeof(AbstractPauseGUI), "Unpause");

        private static bool Prefix(LevelPauseGUI __instance)
        {
            if (!RemoteInputLab.InterceptPlayerTwoRemoval(PlayerId.PlayerTwo))
                return true;

            // Player2Leave llama Unpause después de PlayerManager.PlayerLeave.
            // Como sustituimos el método completo, conservamos aquí esa salida
            // vanilla para limpiar blur, audio, canvas y estado del menú.
            try
            {
                if (__instance != null && UnpauseMethod != null)
                    UnpauseMethod.Invoke(__instance, null);
            }
            catch (System.Exception ex)
            {
                var inner = ex.InnerException == null ? ex : ex.InnerException;
                Plugin.Log.LogWarning("[SessionSync] No se pudo cerrar el menú " +
                    "de pausa nativo: " + inner.Message);
            }
            return false;
        }
    }
}
