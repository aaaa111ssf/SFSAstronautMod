using HarmonyLib;
using SFS.World;
using SFS.World.Maps;

namespace WorldBuild.Mod.Build
{
    public static class Patches
    {
        [HarmonyPatch(typeof(PlayerController), "OnDrag")]
        static class PlayerController_OnDrag
        {
            static bool Prefix()
            {
                return WorldBuildManager.main == null || !WorldBuildManager.main.BlocksWorldInteraction;
            }
        }

        [HarmonyPatch(typeof(PlayerController), "OnInputEnd")]
        static class PlayerController_OnInputEnd
        {
            static bool Prefix()
            {
                return WorldBuildManager.main == null || !WorldBuildManager.main.BlocksWorldInteraction;
            }
        }

        [HarmonyPatch(typeof(Rocket), nameof(Rocket.OnInputEnd_AsPlayer))]
        static class Rocket_OnInputEnd_AsPlayer
        {
            static bool Prefix()
            {
                return WorldBuildManager.main == null || !WorldBuildManager.main.BlocksWorldInteraction;
            }
        }

        [HarmonyPatch(typeof(MapManager), nameof(MapManager.ToggleMap))]
        static class MapManager_ToggleMap
        {
            static void Postfix()
            {
                WorldBuildManager.main?.ExitBuild();
            }
        }

        [HarmonyPatch(typeof(GameManager), "ClearWorld")]
        static class GameManager_ClearWorld
        {
            static void Prefix()
            {
                WorldBuildManager.main?.ExitBuild();
            }
        }
    }
}
