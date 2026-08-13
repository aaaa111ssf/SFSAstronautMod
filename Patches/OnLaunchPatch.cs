using HarmonyLib;
using SFS.Builds;
using WorldBuild.Mod.Saving;

namespace WorldBuild.Mod.Patches
{
    [HarmonyPatch(typeof(BuildManager), nameof(BuildManager.Launch))]
    public static class OnLaunchPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            AstronautSavingManager.main.astronautSwitchBlocked = true;
        }
    }
}