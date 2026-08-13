using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using SFS.World;
using WorldBuild.Mod.Managers;

namespace WorldBuild.Mod.Patches
{
    [HarmonyPatch(typeof(WorldTime), nameof(WorldTime.CanTimewarp))]
    static class TimewarpPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            __result = AstronautSpawner.main.eva == null;
            return __result;
        }
    }
}
