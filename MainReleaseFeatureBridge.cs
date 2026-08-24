using HarmonyLib;
using SFS.World;

namespace AstronautUnlocker
{
    // 每个 EVA 都加载控制恢复组件，不影响世界建筑和存档。
    [HarmonyPatch(typeof(Astronaut_EVA), "Start")]
    public static class Patch_AstronautEVA_Start_ControlRecovery
    {
        static void Postfix(Astronaut_EVA __instance)
        {
            EVAControlRecovery.Attach(__instance);
        }
    }
}
