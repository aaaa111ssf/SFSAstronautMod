using HarmonyLib;
using SFS;
using SFS.IO;
using SFS.World;
using WorldBuild.Mod.Saving;

namespace WorldBuild.Mod.Patches
{
    [HarmonyPatch(typeof(WorldSave), nameof(WorldSave.Save))]
    public static class WorldSavePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FolderPath path, bool saveRocketsAndBranches, WorldSave worldSave, bool isCareer)
        {
            if (!saveRocketsAndBranches) return;

            AstronautSavingManager.main.OnSave(path);
            AstronautDataHelper.main.SaveData = new AstronautSaveData();
        }
    }
    [HarmonyPatch(typeof(WorldSave), nameof(WorldSave.TryLoad))]
    public static class WorldLoadPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FolderPath path, bool loadRocketsAndBranches, I_MsgLogger logger, ref WorldSave worldSave)
        {
            if (loadRocketsAndBranches)
                AstronautSavingManager.main.OnLoad(path);
        }
    }
}
