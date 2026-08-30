using HarmonyLib;

namespace Coophead.Patches
{
    [HarmonyPatch(typeof(CupheadGlyph), "Init")]
    internal static class CupheadGlyphClientBindingPatch
    {
        private struct BindingSwapState
        {
            public bool Applied;
            public int OriginalPlayerId;
        }

        private static void Prefix(ref int ___rewiredPlayerId,
            out BindingSwapState __state)
        {
            __state = new BindingSwapState
            {
                Applied = false,
                OriginalPlayerId = ___rewiredPlayerId,
            };
            if (RemoteInputLab.IsClientSession &&
                ___rewiredPlayerId == (int)PlayerId.PlayerTwo)
            {
                __state.Applied = true;
                ___rewiredPlayerId = (int)PlayerId.PlayerOne;
            }
        }

        private static void Postfix(ref int ___rewiredPlayerId,
            BindingSwapState __state)
        {
            RestoreBinding(ref ___rewiredPlayerId, __state);
        }

        private static System.Exception Finalizer(System.Exception __exception,
            ref int ___rewiredPlayerId, BindingSwapState __state)
        {
            // Harmony ejecuta el finalizer incluso si Init lanzó antes del postfix.
            RestoreBinding(ref ___rewiredPlayerId, __state);
            return __exception;
        }

        private static void RestoreBinding(ref int rewiredPlayerId,
            BindingSwapState state)
        {
            if (state.Applied)
                rewiredPlayerId = state.OriginalPlayerId;
        }
    }
}
