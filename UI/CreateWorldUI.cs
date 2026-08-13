using HarmonyLib;
using SFS.UI;

namespace WorldBuild.Mod.UI
{
    public class CreateWorldUI
    {
        
    }

    [HarmonyPatch(typeof(CreateWorldMenu))]
    public static class CreateWorldMenuPatch
    {
        [HarmonyPatch(nameof(CreateWorldMenu.Open))]
        [HarmonyPostfix]
        public static void Open(CreateWorldMenu __instance)
        {
            
        }
    }
}