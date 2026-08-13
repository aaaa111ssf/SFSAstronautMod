using HarmonyLib;
using SFS.World;
using SFS.World.Maps;

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldBuild.Mod.Build
{
    public static class Patches
    {
        /// <summary>
        /// Skip camera movement when dragging a part in-world.
        /// </summary>
        [HarmonyPatch(typeof(PlayerController), "OnDrag")]
        static class PlayerController_OnDrag
        {
            static bool Prefix()
            {
                return !WorldBuildManager.main.draggingPart;
            }
        }

        /// <summary>
        /// Skip interactions with parts on a rocket when building in-world.
        /// </summary>
        [HarmonyPatch(typeof(Rocket), nameof(Rocket.OnInputEnd_AsPlayer))]
        static class Rocket_OnInputEnd_AsPlayer
        {
            static bool Prefix()
            {
                return !WorldBuildManager.main.worldBuildActive;
            }
        }

        /// <summary>
        /// Close world build when map is opened.
        /// </summary>
        [HarmonyPatch(typeof(MapManager), nameof(MapManager.ToggleMap))]
        static class MapManager_ToggleMap
        {
            static void Postfix()
            {
                WorldBuildManager.main.ExitBuild();
            }
        }

        /// <summary>
        /// Close world build when world is unloaded.
        /// </summary>
        [HarmonyPatch(typeof(GameManager), "ClearWorld")]
        static class GameManager_ClearWorld
        {
            static void Prefix()
            {
                WorldBuildManager.main.ExitBuild();
            }
        }
    }
}