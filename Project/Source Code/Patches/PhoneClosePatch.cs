using HarmonyLib;

namespace TamaRush.Patches
{
    [HarmonyPatch(typeof(Reptile.Phone.Phone), "CloseCurrentApp")]
    internal static class PhoneClosePatch
    {
        private static bool Prefix()
        {
            if (TamaRushPlayMode.IsActive)
                return false;

            return true;
        }
    }
}
