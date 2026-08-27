using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.IO;
using System.Reflection.Emit;
using HarmonyLib;
using ModLoader;
using ModLoader.Helpers;
using SFS;
using SFS.Builds;
using SFS.Career;
using SFS.Input;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.Translations;
using SFS.UI;
using SFS.Variables;
using SFS.World;
using SFS.World.Maps;
using SFS.WorldBase;
using UnityEngine;
using UnityEngine.UI;
using ModGUIButton = SFS.UI.ModGUI.Button;
using ModGUIBuilder = SFS.UI.ModGUI.Builder;

namespace AstronautUnlocker
{
    [HarmonyPatch(typeof(DevSettings), "get_DisableAstronauts")]
    public class Patch_DisableAstronauts
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(AstronautState), "Awake")]
    public class Patch_AstronautState_Awake
    {
        static bool Prefix(AstronautState __instance)
        {
            if (AstronautState.main != null && AstronautState.main != __instance)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AstronautState), "CreateAstronaut")]
    public class Patch_AstronautState_CreateAstronaut
    {
        static bool Prefix(AstronautState __instance, string astronautName)
        {
            try
            {
                astronautName = Regex.Replace(astronautName, @"[^\p{L}\p{N} ]", "");
                astronautName = astronautName.Trim();

                if (astronautName == "")
                {
                    Menu.read.Open(() => Loc.main.Invalid_Astronaut_Name);
                    return false;
                }

                if (__instance.GetAstronautByName(astronautName) != null)
                {
                    Menu.read.Open(() => Loc.main.Astronaut_Already_Exists);
                    return false;
                }

                __instance.state.astronauts.Add(
                    new WorldSave.Astronauts.Data(astronautName, alive: true));

                if (__instance.selfManageSaving)
                {
                    Traverse.Create(__instance).Method("Save").GetValue();
                }

                return false; // 跳过原方法
            }
            catch (Exception e)
            {
                
                return true; // 出错时回退到原方法
            }
        }
    }

    [HarmonyPatch(typeof(AstronautState), "Start")]
    public class Patch_AstronautState_Start
    {
        static bool Prefix()
        {
            return false;
        }
    }

    // ============================================================
    // 回退复活 正常进入保持死亡
    // 判断依据 LoadSave 来自 LoadPersistentAndLaunch（正常进入）则保存死亡
    // 来自其他回退则复活本次任务死亡的乘员
    // ============================================================
    [HarmonyPatch(typeof(GameManager), "LoadPersistentAndLaunch")]
    public class Patch_GameManager_LoadPersistentAndLaunch
    {
        public static bool isPersistentEntry;
        // 任务开始前已死亡的乘员名单（回退时不得复活）
        public static List<string> launchDeadBaseline;

        static void Prefix()
        {
                            // 标记下一次 LoadSave 为正常进入
                isPersistentEntry = true;

                // 注入载入舱的座位不由原生世界存档记录 在进入世界前保留其分配
                Patch_GameManager_LoadSave.CaptureInjectedCrewToSavedForLaunch();

                // 记录任务开始前的死亡基线

            launchDeadBaseline = null;
            if (AstronautState.main?.state?.astronauts != null)
            {
                foreach (var a in AstronautState.main.state.astronauts)
                {
                    if (!a.alive)
                    {
                        if (launchDeadBaseline == null)
                            launchDeadBaseline = new List<string>();
                        launchDeadBaseline.Add(a.astronautName);
                    }
                }
            }
            
        }
    }

    [HarmonyPatch(typeof(GameManager), "LoadSave")]
    public class Patch_GameManager_LoadSave
    {
        private static List<WorldSave.Astronauts.Data> backupAstronauts;
        private static List<string> backupCrewBuild;
        // 正常发射时 AstronautState 可能持有持久 EVA 而传给 LoadSave 的任务快照尚未带上它们
        // 该名单只在本次加载中使用 确保 EVA 先以 EVA 身份恢复 绝不被座位恢复逻辑重复任用
        private static List<WorldSave.Astronauts.EVA> backupPersistentEva;
        private static HashSet<string> pendingEvaNames = new HashSet<string>();
        private static bool isPersistentEntry;
        // 标记当前 LoadSave 是否为回退（非正常进入）
        public static bool isRevertLoad;

        public static bool IsPendingEVA(string astronautName)
        {
            return !string.IsNullOrEmpty(astronautName) && pendingEvaNames.Contains(astronautName);
        }

        static void Prefix(WorldSave save, bool forLaunch)
        {
            try
            {
                bool markedPersistentEntry = Patch_GameManager_LoadPersistentAndLaunch.isPersistentEntry;
                isPersistentEntry = markedPersistentEntry || forLaunch;
                Patch_GameManager_LoadPersistentAndLaunch.isPersistentEntry = false;
                isRevertLoad = !isPersistentEntry;
                

                // 回退会重建世界 注入部件座椅不会被序列化 需先捕获乘员名
                if (isRevertLoad)
                {
                    CaptureInjectedCrewToSaved();
                }

                // --- 备份覆盖前的内存状态 ---
                if (AstronautState.main?.state?.astronauts != null &&
                    AstronautState.main.state.astronauts.Count > 0)
                {
                    backupAstronauts = new List<WorldSave.Astronauts.Data>(
                        AstronautState.main.state.astronauts);
                }

                backupPersistentEva = null;
                pendingEvaNames.Clear();
                if (isPersistentEntry && AstronautState.main?.state?.eva != null &&
                    AstronautState.main.state.eva.Count > 0)
                {
                    backupPersistentEva = new List<WorldSave.Astronauts.EVA>(
                        AstronautState.main.state.eva.Where(e => e != null));
                    foreach (WorldSave.Astronauts.EVA eva in backupPersistentEva)
                        if (!string.IsNullOrEmpty(eva.astronautName)) pendingEvaNames.Add(eva.astronautName);
                }

                // backupCrewBuild 仅用于建造到世界转换（保留 crew_Build）
                // 已死亡乘员不得重新加入 crew_Build/crew_World
                if (AstronautState.main?.crew_Build != null && AstronautState.main.crew_Build.Count > 0)
                {
                    backupCrewBuild = new List<string>(AstronautState.main.crew_Build);
                }
                else
                {
                    backupCrewBuild = null;
                }

                if (save != null && save.astronauts == null)
                {
                    save.astronauts = new WorldSave.Astronauts();
                    
                }
                if (save != null && save.astronauts != null)
                {
                    if (save.astronauts.astronauts == null)
                        save.astronauts.astronauts = new List<WorldSave.Astronauts.Data>();
                    if (save.astronauts.crew_World == null)
                        save.astronauts.crew_World = new List<WorldSave.Astronauts.Crew_World>();
                    if (save.astronauts.eva == null)
                        save.astronauts.eva = new List<WorldSave.Astronauts.EVA>();
                }

                // --- 将缺失的备份乘员注入存档 ---
                // 不覆盖已有条目的 alive 标志 存档值是权威的（回退存档为 alive=true）
                if (backupAstronauts != null && backupAstronauts.Count > 0 &&
                    save?.astronauts?.astronauts != null)
                {
                    foreach (var astro in backupAstronauts)
                    {
                        bool exists = save.astronauts.astronauts
                            .Any(a => a.astronautName == astro.astronautName);
                        if (!exists)
                        {
                            save.astronauts.astronauts.Add(astro);
                            
                        }
                    }
                }

                // 正常发射必须把持久 EVA 合并到任务快照 否则原生 LoadSave 会以空 EVA 列表
                // 重建 AstronautState 导致外出人员变成 Available 并可被再次任用
                if (backupPersistentEva != null && save?.astronauts?.eva != null)
                {
                    foreach (WorldSave.Astronauts.EVA eva in backupPersistentEva)
                    {
                        if (!save.astronauts.eva.Any(existing => existing != null &&
                            existing.astronautName == eva.astronautName))
                            save.astronauts.eva.Add(eva);
                    }

                    // 若旧座位缓存中混入 EVA 名称 EVA 身份优先 不允许恢复成座位乘员
                    foreach (List<string> names in AstronautUnlockerMod.savedAstronauts.Values)
                        if (names != null) names.RemoveAll(name => IsPendingEVA(name));
                }

                // --- 处理 backupCrewBuild（建造到世界转换）---
                // 确保 crew_Build 乘员进入世界的 crew_World
                if (backupCrewBuild != null && backupCrewBuild.Count > 0 &&
                    save?.astronauts != null)
                {
                    if (save.astronauts.crew_World == null)
                        save.astronauts.crew_World = new List<WorldSave.Astronauts.Crew_World>();
                    if (save.astronauts.eva == null)
                        save.astronauts.eva = new List<WorldSave.Astronauts.EVA>();

                    foreach (string name in backupCrewBuild)
                    {
                        // EVA 状态优先 不能因旧 crew_Build 缓存被改写为座舱乘员
                        bool isOnEVA = save.astronauts.eva.Any(e => e != null && e.astronautName == name);
                        if (isOnEVA) continue;

                        save.astronauts.crew_World.RemoveAll(c => c != null && c.astronautName == name);
                        bool exists = save.astronauts.crew_World.Any(c => c != null && c.astronautName == name);
                        if (!exists)
                        {
                            save.astronauts.crew_World.Add(new WorldSave.Astronauts.Crew_World
                            {
                                astronautName = name
                            });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1549", e);
            }
        }

        static void Postfix()
        {
            try
            {
                if (AstronautState.main != null)
                {
                    if (AstronautState.main.crew_Build == null)
                        AstronautState.main.crew_Build = new List<string>();

                    if (AstronautState.main.state == null)
                        AstronautState.main.state = new WorldSave.Astronauts();

                    if (AstronautState.main.state.crew_World == null)
                        AstronautState.main.state.crew_World =
                            new List<WorldSave.Astronauts.Crew_World>();
                    if (AstronautState.main.state.eva == null)
                        AstronautState.main.state.eva =
                            new List<WorldSave.Astronauts.EVA>();
                    if (AstronautState.main.state.astronauts == null)
                        AstronautState.main.state.astronauts =
                            new List<WorldSave.Astronauts.Data>();
                }

                if (backupAstronauts != null && backupAstronauts.Count > 0)
                {
                    if (AstronautState.main?.state?.astronauts != null)
                    {
                        foreach (var astro in backupAstronauts)
                        {
                            // 仅添加缺失项 不覆盖 alive 标志（存档值为权威）
                            bool exists = AstronautState.main.state.astronauts
                                .Any(a => a.astronautName == astro.astronautName);
                            if (!exists)
                            {
                                AstronautState.main.state.astronauts.Add(astro);
                                
                            }
                        }
                    }
                    backupAstronauts = null;
                }

                // 仅在建造场景保留 crew_Build 进入世界后不得把旧建造缓存重新写回
                if (BuildManager.main != null && backupCrewBuild != null && backupCrewBuild.Count > 0)
                {
                    if (AstronautState.main?.crew_Build != null)
                    {
                        foreach (string name in backupCrewBuild)
                        {
                            if (!AstronautState.main.crew_Build.Contains(name))
                                AstronautState.main.crew_Build.Add(name);
                        }
                    }
                }
                backupCrewBuild = null;

                Patch_Seat_OnDestroy.destroyedSeatAstronauts.Clear();

                // --- 回退复活 ---
                // 这是真正的回退（非正常进入） 存档可能带 stale alive=false
                // 仅复活任务开始前仍存活 本次任务死亡的乘员
                
                if (!isPersistentEntry && AstronautState.main?.state?.astronauts != null)
                {
                    var baseline = Patch_GameManager_LoadPersistentAndLaunch.launchDeadBaseline;
                    foreach (var member in AstronautState.main.state.astronauts)
                    {
                        if (!member.alive &&
                            (baseline == null || !baseline.Contains(member.astronautName)))
                        {
                            member.alive = true;
                            
                        }
                    }
                }
                isPersistentEntry = false;
                isRevertLoad = false;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1633", e);
            }
        }

        // 回退前捕获注入部件座椅上的乘员名到 savedAstronauts
        // 供重建后的座椅恢复（回退会清空世界）
        private static void CaptureInjectedCrewToSaved()
        {
            CaptureCrewToSaved(injectedOnly: true);
        }

        public static void CaptureInjectedCrewToSavedForLaunch()
        {
            CaptureCrewToSaved(injectedOnly: true);
        }

        public static void CaptureAllCrewToSaved()
        {
            CaptureCrewToSaved(injectedOnly: false);
        }

        private static void CaptureCrewToSaved(bool injectedOnly)
        {
            try
            {
                CrewModule[] allCrews = UnityEngine.Object.FindObjectsOfType<CrewModule>(true);
                bool changed = false;
                foreach (CrewModule crew in allCrews)
                {
                    if (crew == null || crew.seats == null) continue;
                    Part part = Traverse.Create(crew).Field("part").GetValue<Part>();
                    if (part == null || string.IsNullOrEmpty(part.name)) continue;
                    if (injectedOnly && !AstronautUnlockerMod.injectedPartIds.Contains(part.GetInstanceID())) continue;

                    List<string> names = crew.seats
                        .Where(seat => seat?.astronaut != null && !string.IsNullOrEmpty(seat.astronaut.Value))
                        .Select(seat => seat.astronaut.Value)
                        .Distinct().ToList();
                    if (names.Count == 0) continue;

                    if (!AstronautUnlockerMod.savedAstronauts.ContainsKey(part.name))
                        AstronautUnlockerMod.savedAstronauts[part.name] = new List<string>();
                    foreach (string name in names)
                    {
                        if (!AstronautUnlockerMod.savedAstronauts[part.name].Contains(name))
                        {
                            AstronautUnlockerMod.savedAstronauts[part.name].Add(name);
                            changed = true;
                        }
                    }
                }
                if (changed) AstronautUnlockerMod.SaveEvaConfig();
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1688", e);
            }
        }
    }

    // --- 正常退出世界清空 savedAstronauts ---
    // 离开世界（新建火箭 / 返回中心 / 主菜单）即进入全新上下文
    // 清空可防止此前捕获的（可能已死亡）乘员被自动恢复到新建造中
    [HarmonyPatch(typeof(GameManager), "ExitToBuild")]
    public class Patch_GameManager_ExitToBuild_ClearSaved
    {
        static void Prefix()
        {
            AstronautUnlockerMod.ClearSavedAstronauts("ExitToBuild");
        }
    }

    [HarmonyPatch(typeof(GameManager), "ExitToHub")]
    public class Patch_GameManager_ExitToHub_ClearSaved
    {
        static void Prefix()
        {
            AstronautUnlockerMod.ClearSavedAstronauts("ExitToHub");
        }
    }

    [HarmonyPatch(typeof(GameManager), "ExitToMainMenu")]
    public class Patch_GameManager_ExitToMainMenu_ClearSaved
    {
        static void Prefix()
        {
            AstronautUnlockerMod.ClearSavedAstronauts("ExitToMainMenu");
        }
    }

    // --- 回退到建造也复活 ---
    // RevertToBuild 不走 LoadSave 而是用 deleteRevert=true 持久化发射快照
    // 此处同样复活 使回退撤销死亡 正常保存/退出（deleteRevert=false）保持死亡
    [HarmonyPatch(typeof(SavingCache), "SaveWorldPersistent")]
    public class Patch_SavingCache_SaveWorldPersistent_ReviveOnRevertBuild
    {
        static void Prefix(WorldSave new_WorldPersistent, bool deleteRevert)
        {
            if (!deleteRevert) return;
            try
            {
                if (new_WorldPersistent?.astronauts?.astronauts == null) return;
                var baseline = Patch_GameManager_LoadPersistentAndLaunch.launchDeadBaseline;
                foreach (var sd in new_WorldPersistent.astronauts.astronauts)
                {
                    // 仅复活本次任务死亡的乘员 任务前已死亡的保持死亡
                    if (!sd.alive &&
                        (baseline == null || !baseline.Contains(sd.astronautName)))
                    {
                        sd.alive = true;
                        
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1746", e);
            }
        }
    }

    [HarmonyPatch(typeof(Astronaut_EVA), "StartDeathAnimation")]
    public class Patch_EVA_DeathAnimation
    {
        static bool Prefix(Astronaut_EVA __instance, float startTime)
        {
            if (AstronautManager.main == null || AstronautManager.main.fadeToBlack == null)
            {
                
                try
                {
                    __instance.astronaut.alive = false;
                }
                catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1765", e);
            }
                AstronautManager.DestroyEVA(__instance, death: true);
                return false; // 跳过原 StartDeathAnimation
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AstronautMenu), "Start")]
    public class Patch_AstronautMenu_Start
    {
        static bool Prefix() { return false; }
    }

    [HarmonyPatch(typeof(AstronautMenu), "Update")]
    public class Patch_AstronautMenu_Update
    {
        static bool Prefix() { return false; }
    }

    [HarmonyPatch(typeof(AstronautMenu), "OnOpen")]
    public class Patch_AstronautMenu_OnOpen
    {
        static bool Prefix() { return false; }
    }

    [HarmonyPatch(typeof(AstronautMenu), "OnClose")]
    public class Patch_AstronautMenu_OnClose
    {
        static bool Prefix() { return false; }
    }

    [HarmonyPatch(typeof(AstronautMenu), "DrawList")]
    public class Patch_AstronautMenu_DrawList
    {
        static bool Prefix() { return false; }
    }

    [HarmonyPatch(typeof(AstronautMenu), "CreateAstronaut")]
    public class Patch_AstronautMenu_CreateAstronaut
    {
        static bool Prefix()
        {
            NativeAstronautUI.OpenCreateDialog(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(AstronautMenu), "FireAstronaut")]
    public class Patch_AstronautMenu_FireAstronaut
    {
        static bool Prefix() { return false; }
    }

    [HarmonyPatch(typeof(AstronautMenu), "OpenMenu")]
    public class Patch_AstronautMenu_OpenMenu
    {
        static bool Prefix(AstronautMenu __instance, CrewModule.Seat seat, Action redrawSeat)
        {
            NativeAstronautUI.ShowMenu(seat, redrawSeat);
            return false;
        }
    }

    [HarmonyPatch(typeof(CrewModule.Seat), "OnStart")]
    public class Patch_Seat_OnStart
    {
        static bool Prefix(CrewModule.Seat __instance)
        {
            try
            {
                var tr = Traverse.Create(__instance);
                var astronautRef = tr.Field("astronaut").GetValue<String_Reference>();
                string astronautName = astronautRef?.Value;

                if (string.IsNullOrEmpty(astronautName))
                    return false;

                if (AstronautState.main == null || AstronautState.main.state == null)
                {
                    return false;
                }

                if (AstronautState.main.crew_Build == null)
                    AstronautState.main.crew_Build = new List<string>();
                if (AstronautState.main.state.crew_World == null)
                    AstronautState.main.state.crew_World =
                        new List<WorldSave.Astronauts.Crew_World>();
                if (AstronautState.main.state.eva == null)
                    AstronautState.main.state.eva =
                        new List<WorldSave.Astronauts.EVA>();
                if (AstronautState.main.state.astronauts == null)
                    AstronautState.main.state.astronauts =
                        new List<WorldSave.Astronauts.Data>();

                AstronautState.State state = NativeAstronautUI.SafeGetAstronautState(astronautName);
                

                if (state == AstronautState.State.Available)
                {
                    
                    AstronautState.main.AddCrew(astronautName);
                    tr.Method("AddSeatedAstronaut").GetValue();
                    return false;
                }
                else if (state == AstronautState.State.CrewWorld)
                {
                    
                    tr.Method("AddSeatedAstronaut").GetValue();
                    return false;
                }
                else if (state == AstronautState.State.CrewBuild)
                {
                    
                    if (BuildManager.main == null)
                    {
                        AstronautState.main.crew_Build.Remove(astronautName);
                        AstronautState.main.AddCrew(astronautName);
                    }
                    tr.Method("AddSeatedAstronaut").GetValue();
                    return false;
                }
                else
                {
                    
                    // 回退时存档的 alive 标志已过时（本次任务死亡但被回退复活）
                    // 保留座椅乘员不清空 但仅限本次任务死亡的乘员 任务前已死亡的不恢复
                    var baseline = Patch_GameManager_LoadPersistentAndLaunch.launchDeadBaseline;
                    bool deadBeforeLaunch = baseline != null && baseline.Contains(astronautName);
                    if (Patch_GameManager_LoadSave.isRevertLoad && !deadBeforeLaunch)
                    {
                        AstronautState.main.AddCrew(astronautName);
                        tr.Method("AddSeatedAstronaut").GetValue();
                        
                        return false;
                    }

                    
                    astronautRef.Value = "";
                    bool externalSeat = tr.Field<bool>("externalSeat").Value;
                    if (externalSeat)
                    {
                        var resources = tr.Field("resources").GetValue<EVA_Resources>();
                        if (resources != null)
                            resources.fuelPercent.Value = -1.0;
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(CrewModule.Seat), "OnDestroy")]
    public class Patch_Seat_OnDestroy
    {
        public static List<string> destroyedSeatAstronauts = new List<string>();

        static bool Prefix(CrewModule.Seat __instance)
        {
            try
            {
                var tr = Traverse.Create(__instance);
                var astronautRef = tr.Field("astronaut").GetValue<String_Reference>();
                string astronautName = astronautRef?.Value;

                if (string.IsNullOrEmpty(astronautName))
                    return false; // 无乘员则跳过

                if (!destroyedSeatAstronauts.Contains(astronautName))
                    destroyedSeatAstronauts.Add(astronautName);

                

                // 从 crew_Build（建造）或 crew_World（世界）移除
                if (AstronautState.main != null)
                {
                    AstronautState.main.RemoveCrew(astronautName);

                    // 世界场景中舱体销毁即乘员死亡（原游戏行为）
                    if (GameManager.main != null)
                    {
                        var data = AstronautState.main.GetAstronautByName(astronautName);
                        if (data != null)
                        {
                            data.alive = false;
                            
                        }
                        else
                        {
                            
                        }
                    }
                }

                return false; // 跳过原方法
            }
            catch (Exception e)
            {
                
                return true; // 出错时回退到原方法
            }
        }
    }

    [HarmonyPatch(typeof(CrewModule.Seat), "Board")]
    public class Patch_Seat_Board_SaveFuel
    {
        static void Postfix(CrewModule.Seat __instance, string astronautName, double fuelPercent)
        {
            try
            {
                if (!__instance.externalSeat)
                {
                    NativeAstronautUI.savedInternalFuel[astronautName] = fuelPercent;
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1986", e);
            }
        }
    }

    [HarmonyPatch(typeof(CrewModule), "EVA_Exit")]
    public class Patch_CrewModule_EVA_Exit_Fuel
    {
        static bool Prefix(CrewModule __instance, CrewModule.Seat seat)
        {
            try
            {
                if (!PlanetSurfaceHelper.IsSolidPlanet(__instance))
                {
                    Menu.read.Open(() => "Cannot perform EVA on a gas giant — no solid surface to walk on!");
                    return false;
                }

                string name = seat.astronaut?.Value;
                if (!string.IsNullOrEmpty(name) && !seat.externalSeat)
                {
                    if (NativeAstronautUI.savedInternalFuel.ContainsKey(name))
                    {
                        NativeAstronautUI.pendingFuelOverride = NativeAstronautUI.savedInternalFuel[name];
                        NativeAstronautUI.savedInternalFuel.Remove(name);
                    }
                    else
                    {
                        NativeAstronautUI.pendingFuelOverride = 1.0;
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2017", e);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AstronautManager), "SpawnEVA")]
    public class Patch_AstronautManager_SpawnEVA_Fuel
    {
        static void Prefix(ref double fuelPercent)
        {
            if (NativeAstronautUI.pendingFuelOverride.HasValue)
            {
                fuelPercent = NativeAstronautUI.pendingFuelOverride.Value;
                NativeAstronautUI.pendingFuelOverride = null;
            }
        }

        static void Postfix(Astronaut_EVA __result)
        {
            try
            {




                EVAControlRecovery.Attach(__result);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2044", e);
            }
        }
    }

    // EndMissionMenu 检查 HasCrew 为 true 会强制销毁流程（无法回收）
    // 本模组在 PC 端启用乘员 座椅有名字导致 HasCrew=true 阻止回收
    // 补丁返回 false 以走正常回收/销毁流程
    [HarmonyPatch(typeof(CrewModule), "get_HasCrew")]
    public class Patch_CrewModule_HasCrew
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Rocket), "UseParts")]
    public class Patch_Rocket_UseParts
    {
        static HashSet<int> patchedParts = new HashSet<int>();

        public static void ClearPatchedParts()
        {
            patchedParts.Clear();
        }

        static bool Prefix(bool fromStaging, (Part, PolygonData)[] regions)
        {
            try
            {
                if (regions == null || regions.Length == 0)
                    return true;

                foreach (var region in regions)
                {
                    Part part = region.Item1;
                    if (part == null || part.onPartUsed == null) continue;

                    int id = part.GetInstanceID();
                    if (patchedParts.Contains(id)) continue;

                    DetachModule[] detachModules = part.GetModules<DetachModule>();
                    if (detachModules != null && detachModules.Length > 0)
                    {
                        DetachModule dm = detachModules[0];
                        part.onPartUsed.AddListener((UsePartData data) =>
                        {
                            try { dm.Detach(data); }
                            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2093", e);
            }
                        });
                        patchedParts.Add(id);
                    }

                    SplitModule[] splitModules = part.GetModules<SplitModule>();
                    if (splitModules != null && splitModules.Length > 0)
                    {
                        SplitModule sm = splitModules[0];
                        part.onPartUsed.AddListener((UsePartData data) =>
                        {
                            try { sm.Split(data); }
                            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2108", e);
            }
                        });
                        patchedParts.Add(id);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                
                return true;
            }
        }

        static void Postfix(bool fromStaging, (Part, PolygonData)[] regions,
            ref UsePartData[] __result)
        {
            try
            {
                if (regions == null) return;

                // PC 部件无持久化事件 原 UseParts 会跳过它们
                // 手动用结果数据调用 onPartUsed
                if (__result != null && __result.Length == regions.Length)
                {
                    for (int i = 0; i < regions.Length; i++)
                    {
                        Part part = regions[i].Item1;
                        if (part == null || part.onPartUsed == null) continue;

                        int eventCount = part.onPartUsed.GetPersistentEventCount();
                        if (eventCount == 0)
                        {
                            part.onPartUsed.Invoke(__result[i]);
                        }
                    }
                }

                if (fromStaging) return;

                foreach (var region in regions)
                {
                    Part part = region.Item1;
                    if (part == null) continue;

                    CrewModule[] crewModules = part.GetModules<CrewModule>();
                    if (crewModules == null || crewModules.Length == 0) continue;

                    int eventCount = (part.onPartUsed != null)
                        ? part.onPartUsed.GetPersistentEventCount() : 0;
                    if (eventCount == 0)
                    {
                        crewModules[0].OpenPartMenu_Seats();
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2167", e);
            }
        }
    }

    [HarmonyPatch(typeof(CrewModule), "OpenPartMenu")]
    public class Patch_CrewModule_OpenPartMenu
    {
        static bool Prefix(CrewModule __instance, bool canBoardWorld)
        {
            try
            {
                if (BuildManager.main == null)
                {
                    AttachableStatsMenu menu = UnityEngine.Object.FindObjectOfType<AttachableStatsMenu>(includeInactive: true);
                    if (menu == null)
                    {
                        
                        SeatMenuFallback.Show(__instance, canBoardWorld);
                        return false;
                    }
                }
                return true; // 让原方法运行
            }
            catch (Exception e)
            {
                
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(CrewModule), "OpenPartMenu_Seats")]
    public class Patch_CrewModule_OpenPartMenu_Seats
    {
        static void Prefix(CrewModule __instance)
        {
            try
            {
                int seatCount = __instance.seats?.Length ?? 0;
                int occupied = __instance.seats?.Count(s => s.HasAstronaut) ?? 0;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2211", e);
            }
        }
    }

    [HarmonyPatch(typeof(CrewModule), "OnSeatChange")]
    public class Patch_CrewModule_OnSeatChange
    {
        static bool Prefix(CrewModule __instance)
        {
            try
            {
                var tr = Traverse.Create(__instance);

                SFS.Parts.Part part = tr.Field("part").GetValue<SFS.Parts.Part>();
                bool disableAstronauts = DevSettings.DisableAstronauts;

                bool anyHasAstronaut = false;
                if (__instance.seats != null)
                {
                    foreach (var seat in __instance.seats)
                    {
                        if (seat.HasAstronaut) { anyHasAstronaut = true; break; }
                    }
                }

                bool hasControl = disableAstronauts ||
                    AstronautUnlockerMod.allowUncrewedControl || anyHasAstronaut;

                var hasControlRef = tr.Field("hasControl")
                    .GetValue<SFS.Variables.Bool_Reference>();
                if (hasControlRef != null)
                    hasControlRef.Value = hasControl;

                var hatch = tr.Field("hatch").GetValue<GameObject>();
                if (hatch != null)
                    hatch.SetActive(hasControl);

                var interior = tr.Field("interior").GetValue<GameObject>();
                if (interior != null && !interior.activeSelf)
                {
                    interior.SetActive(true);
                }

                float baseMass = tr.Field("baseMass").GetValue<float>();
                float seatMass = 0f;
                if (__instance.seats != null)
                {
                    foreach (var seat in __instance.seats)
                    {
                        if (seat.HasAstronaut) seatMass += 0.2f;
                    }
                }
                if (part != null && part.mass != null)
                    part.mass.Value = baseMass + seatMass;

                return false; // 完全跳过原 OnSeatChange
            }
            catch (Exception e)
            {
                
                return true; // 出错时回退到原方法
            }
        }
    }

    public static class SeatMenuFallback
    {
        public static void Show(CrewModule crewModule, bool canBoardWorld)
        {
            try
            {
                List<MenuElement> elements = new List<MenuElement>();
                SizeSyncerBuilder.Carrier carrier;
                elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));

                CrewModule.Seat[] seats = crewModule.seats;
                if (seats == null || seats.Length == 0)
                {
                    elements.Add(TextBuilder.CreateText(() => "No seats"));
                }

                foreach (CrewModule.Seat seat in seats)
                {
                    CrewModule.Seat capturedSeat = seat;
                    bool hasAstro = capturedSeat.HasAstronaut;
                    string astroName = hasAstro ? capturedSeat.astronaut.Value : "";
                    bool enabled = hasAstro || canBoardWorld;

                    if (!enabled)
                    {
                        elements.Add(TextBuilder.CreateText(() => "(Empty seat)"));
                        continue;
                    }

                    string displayText = hasAstro
                        ? ("EVA Exit — " + astroName)
                        : "EVA Board";
                    CrewModule capturedModule = crewModule;
                    bool capturedHasAstro = hasAstro;

                    elements.Add(ButtonBuilder.CreateButton(carrier,
                        () => displayText,
                        () =>
                        {
                            try
                            {
                                if (capturedHasAstro)
                                {
                                    Traverse.Create(capturedModule).Method("EVA_Exit", capturedSeat).GetValue();
                                }
                                else
                                {
                                    Traverse.Create(capturedModule).Method("EVA_Board", capturedSeat).GetValue();
                                }
                            }
                            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2329", e);
            }
                        },
                        CloseMode.Current));
                }

                elements.Add(ElementGenerator.VerticalSpace(20));
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Close",
                    () => { },
                    CloseMode.Current));

                MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Current, elements.ToArray());
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2345", e);
            }
        }
    }
}
