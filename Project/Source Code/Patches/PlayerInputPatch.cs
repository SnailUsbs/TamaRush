using HarmonyLib;
using Reptile;

namespace TamaRush.Patches
{
    [HarmonyPatch(typeof(Player), "SetInputs")]
    internal static class PlayerInputPatch
    {
        private static void Prefix(ref UserInputHandler.InputBuffer inputBuffer)
        {
            if (!TamaRushPlayMode.IsActive) return;

            bool danceNew  = inputBuffer.danceButtonNew;
            bool danceHeld = inputBuffer.danceButtonHeld;

            inputBuffer = default;

            inputBuffer.danceButtonNew  = danceNew;
            inputBuffer.danceButtonHeld = danceHeld;
        }
    }
}
