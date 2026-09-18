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
    public class AstronautUnlockerMod : Mod
    {
        public static Harmony HarmonyInstance;

        public override string ModNameID => "astronaut_mod";
        public override string DisplayName => "AstronautMod";
        public override string Author => "A Future star";
        public override string MinimumGameVersionNecessary => "1.6";
        public override string ModVersion => "3.9.2";
        public override string Description => "Enables the native astronaut/crew system on PC.";

        public override void Early_Load()
        {
            HarmonyInstance = new Harmony("com.sfs.astronautunlocker");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            PatchVariableLists();
            CrewPersistence.Install(HarmonyInstance);
            ModifyDisableParts();
            CreatePersistentAstronautState();
            EnsureModSettings();
            LoadModConfig();
            LoadEvaConfig();
            // 座位恢复缓存只服务当前任务 清除旧测试会话的残留 避免跨会话错配人员
            if (savedAstronauts.Count > 0)
            {
                savedAstronauts.Clear();
                SaveEvaConfig();
            }
            FlagCustomization.Initialize();
            ModLogger.Info("Initialized");
        }

        static void PatchVariableLists()
        {
            try
            {
                Type variableListGeneric = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = asm.GetType("SFS.Variables.VariableList`1");
                    if (t != null) { variableListGeneric = t; break; }
                }
                if (variableListGeneric == null)
                {
                    ModLogger.Warning("Variable list API was not found");
                    return;
                }

                foreach (Type T in new[] { typeof(double), typeof(bool), typeof(string) })
                {
                    try
                    {
                        Type concreteType = variableListGeneric.MakeGenericType(T);
                        MethodInfo original = AccessTools.Method(concreteType, "RegisterOnVariableChange");
                        if (original != null)
                        {
                            HarmonyMethod prefix = new HarmonyMethod(
                                typeof(VariableListPatches).GetMethod("RegisterOnVariableChange_Prefix"));
                            HarmonyInstance.Patch(original, prefix);
                        }
                    }
                    catch (Exception e)
                    {
                        ModLogger.ErrorOnce("Variable list patch", e);
                    }
                }

                Type composedFloatType = AccessTools.TypeByName("SFS.Variables.Composed_Float");
                if (composedFloatType != null)
                {
                    MethodInfo getResult = AccessTools.Method(composedFloatType, "GetResult");
                    if (getResult != null)
                    {
                        HarmonyMethod finalizer = new HarmonyMethod(
                            typeof(VariableListPatches).GetMethod("Composed_Float_GetResult_Finalizer"));
                        HarmonyInstance.Patch(getResult, finalizer: finalizer);
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Variable list initialization", e);
            }
        }

        public override void Load()
        {
            // 模组重载时，SFS 会再次调用 Load()。先取消自身旧订阅并按命名前缀清理上一实例可能残留的
            // DontDestroyOnLoad 对象，再重新订阅，避免重复初始化或旧实例对象残留。
            // 注意：场景加载处理器【只在这里】管理，绝不能挂到 SceneManager.sceneUnloaded 上——
            // 该事件在每次正常场景切换（标题→Hub、Hub→蓝图→世界…）都会触发，若在其中 -= 场景处理器
            // 或销毁活动 UI 对象，会导致 Hub 尚未初始化就被取消订阅（菜单打不开 / Hub UI 消失）的严重回归。
            CleanupModSubscriptions();
            DestroyPersistentObjects();
            DestroyOrphanObjects();

            SceneHelper.OnHubSceneLoaded += OnHubSceneLoaded;
            SceneHelper.OnBuildSceneLoaded += OnBuildSceneLoaded;
            SceneHelper.OnWorldSceneLoaded += OnWorldSceneLoaded;

            GameObject driverObj = new GameObject("__AstronautUnlockerUpdater");
            UnityEngine.Object.DontDestroyOnLoad(driverObj);
            RegisterPersistent(driverObj);
            driverObj.AddComponent<UpdateDriver>();

            // 重载安全：SFS 重载模组只会再次调用 Load()，并不会重新加载当前场景，
            // 因此 OnHubSceneLoaded / OnBuildSceneLoaded / OnWorldSceneLoaded 这些场景事件
            // 不会因“重载”而重新触发。若不在此手动重跑，重载后当前场景里的菜单 / 宇航员状态 /
            // EVA 基础设施会被上方清理掉却不再重建，必须手动切场景或重启游戏 UI 才会回来——
            // 这与“场景切换误删 Hub UI”是同族的“生命周期事件触发时机不对导致 UI 消失”问题。
            // 这里按当前实际处于的场景，重跑对应初始化（各 handler 内部均有 null 守卫，可重复执行）。
            try
            {
                if (HubManager.main != null)
                    OnHubSceneLoaded(default(UnityEngine.SceneManagement.Scene));
                else if (BuildManager.main != null)
                    OnBuildSceneLoaded(default(UnityEngine.SceneManagement.Scene));
                else if (Base.worldBase != null)
                    OnWorldSceneLoaded(default(UnityEngine.SceneManagement.Scene));
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Reload re-init", e);
            }
        }

        // ---------------------------------------------------------- 重载清理
        // Mod 基类没有 Unload 钩子，但 SFS 在重载模组时会再次调用 Load()——上面的 Load() 在开头
        // 已经先取消订阅 + 清理残留对象，因此重载安全。此处【不】挂 SceneManager.sceneUnloaded，
        // 以免正常场景切换时误删活动 UI / 误取消场景处理器（3.9.2 的回归已据此回退）。

        private static readonly System.Collections.Generic.HashSet<GameObject> persistentObjects =
            new System.Collections.Generic.HashSet<GameObject>();

        private static readonly string[] PersistentNamePrefixes =
            { "__Astronaut", "__Rock", "__Persistent" };

        // AstronautModSettings 由 Early_Load 每次重建，属关键对象；不纳入“孤儿”按名销毁，
        // 避免重载时序竞态下误删当前实例的设置对象（旧实例的会在 DestroyPersistentObjects 里按集合销毁）。
        private static readonly string[] SafeNamePrefixes = { "AstronautModSettings" };

        private static void RegisterPersistent(GameObject go)
        {
            if (go != null) persistentObjects.Add(go);
        }

        private static void CleanupModSubscriptions()
        {
            SceneHelper.OnHubSceneLoaded -= OnHubSceneLoaded;
            SceneHelper.OnBuildSceneLoaded -= OnBuildSceneLoaded;
            SceneHelper.OnWorldSceneLoaded -= OnWorldSceneLoaded;
        }

        // 销毁本实例追踪的 DontDestroyOnLoad 对象（同一程序集重载时调用）
        private static void DestroyPersistentObjects()
        {
            foreach (var go in persistentObjects)
                if (go != null) UnityEngine.Object.Destroy(go);
            persistentObjects.Clear();
        }

        // 按命名前缀销毁“不属于当前实例”的残留对象（跨程序集重载时，旧对象不在当前集合里）
        private static void DestroyOrphanObjects()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<GameObject>();
                if (all == null) return;
                foreach (var go in all)
                {
                    if (go == null || persistentObjects.Contains(go)) continue;
                    if (go.name == null) continue;
                    foreach (var prefix in SafeNamePrefixes)
                        if (go.name.StartsWith(prefix)) continue; // 关键对象，跳过
                    foreach (var prefix in PersistentNamePrefixes)
                    {
                        if (go.name.StartsWith(prefix))
                        {
                            UnityEngine.Object.Destroy(go);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Orphan cleanup", e);
            }
        }

        private static void OnHubSceneLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            try
            {
                EnsureAstronautState();
                EnsureCrewBuildList();
                EnsureAllStateLists();
                LoadAstronautDataFromCache();
                EnsureAllStateLists();
                EnsureAstronautMenuInstance();
                ActivateAstronautsButton();
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Hub scene initialization", e);
            }
        }

        private static void OnBuildSceneLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            try
            {
                EnsureAstronautState();
                EnsureAstronautMenuInstance();
                EnsureAllStateLists();

                NativeAstronautUI.pendingFuelOverride = null;
                NativeAstronautUI.savedInternalFuel.Clear();

                Astronaut_EVA[] leftoverEVA = UnityEngine.Object.FindObjectsOfType<Astronaut_EVA>();
                if (leftoverEVA != null && leftoverEVA.Length > 0)
                {
                    foreach (var eva in leftoverEVA)
                    {
                        if (eva != null && eva.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(eva.gameObject);
                        }
                    }
                }

                Rocket[] leftoverRockets = UnityEngine.Object.FindObjectsOfType<Rocket>();
                if (leftoverRockets != null && leftoverRockets.Length > 0)
                {
                    foreach (var rocket in leftoverRockets)
                    {
                        if (rocket != null && rocket.gameObject != null)
                        {
                            if (BuildManager.main != null && rocket.transform.IsChildOf(BuildManager.main.transform))
                                continue;
                            UnityEngine.Object.Destroy(rocket.gameObject);
                        }
                    }
                }

                if (AstronautState.main.crew_Build == null)
                    AstronautState.main.crew_Build = new List<string>();
                // 不清空 crew_Build 进入世界时再转换为 crew_World
                LoadAstronautDataFromCache();
                EnsureAllStateLists();
                UpdateDriver.ScheduleCrewModuleRefresh();

                UpdateDriver.SchedulePickGridRefresh();
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 187", e);
            }
        }

        private static void OnWorldSceneLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            try
            {
                EnsureAstronautState();
                EnsureCrewBuildList();
                EnsureAllStateLists();

                if (AstronautState.main.crew_Build != null && AstronautState.main.crew_Build.Count > 0)
                {
                    var namesToTransition = new List<string>(AstronautState.main.crew_Build);
                    foreach (string name in namesToTransition)
                    {
                        AstronautState.main.crew_Build.Remove(name);
                        AstronautState.main.AddCrew(name);
                    }
                }

                // 重试注入部件的乘员恢复（初始化时 AstronautState 可能为 null）
                RetryRestoreAstronauts();

                // 用 Custom Save Data 的备份兜底（若依赖模组可用）
                try { CustomSaveBridge.RecoverAfterWorldLoad(); }
                catch (Exception cse) { ModLogger.ErrorOnce("World recovery", cse); }

                Patch_Rocket_UseParts.ClearPatchedParts();

                if (AstronautManager.main == null)
                {
                    
                    GameObject go = new GameObject("__AstronautManagerFallback");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    RegisterPersistent(go);
                    AstronautManager mgr = go.AddComponent<AstronautManager>();
                }
                else
                {
                    if (AstronautManager.main.astronautPrefab == null)
                    {
                        

                        Astronaut_EVA[] allEVA = UnityEngine.Object.FindObjectsOfType<Astronaut_EVA>(includeInactive: true);

                        GameObject prefabCandidate = UnityEngine.Resources.Load<GameObject>("Astronaut_EVA");
                        if (prefabCandidate != null)
                        {
                            Astronaut_EVA evaComp = prefabCandidate.GetComponent<Astronaut_EVA>();
                            if (evaComp != null)
                            {
                                typeof(AstronautManager).GetField("astronautPrefab",
                                    BindingFlags.Public | BindingFlags.Instance)
                                    .SetValue(AstronautManager.main, evaComp);
                            }
                        }
                    }
                }

                EnsureRockSelector();

                EnsureFlagPrefab();
    }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 249", e);
            }
        }

        // 重试注入部件乘员的 AddCrew（初始化时仅设名字）
        private static void RetryRestoreAstronauts()
        {
            try
            {
                if (AstronautState.main == null) return;

                // 遍历场景中所有注入部件
                CrewModule[] allCrews = UnityEngine.Object.FindObjectsOfType<CrewModule>(true);
                foreach (CrewModule crew in allCrews)
                {
                    Part part = Traverse.Create(crew).Field("part").GetValue<Part>();
                    if (part == null) continue;
                    if (!injectedPartIds.Contains(part.GetInstanceID())) continue;
                    if (crew.seats == null) continue;

                    foreach (var seat in crew.seats)
                    {
                        if (!seat.HasAstronaut) continue;
                        string name = seat.astronaut.Value;

                        // 乘员仍为 Available 说明 AddCrew 未执行 补上
                        var state = AstronautState.main.GetAstronautState(name);
                        if (state == AstronautState.State.Available)
                        {
                            AstronautState.main.AddCrew(name);
                            
                        }
                    }
                }

                // 清理无待恢复数据的部件
                var keysToRemove = new List<string>();
                foreach (var kv in savedAstronauts)
                {
                    if (kv.Value == null || kv.Value.Count == 0)
                        keysToRemove.Add(kv.Key);
                }
                foreach (string key in keysToRemove)
                    savedAstronauts.Remove(key);
                if (keysToRemove.Count > 0)
                    SaveEvaConfig();
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 298", e);
            }
        }

        private static void EnsureRockSelector()
        {
            try
            {
                if (RockSelector.main != null)
                {
                    return;
                }
                GameObject go = new GameObject("__RockSelectorFallback");
                UnityEngine.Object.DontDestroyOnLoad(go);
                RegisterPersistent(go);
                RockSelector rs = go.AddComponent<RockSelector>();
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 316", e);
            }
        }

        private static void EnsureFlagPrefab()
        {
            try
            {
                if (AstronautManager.main == null) return;
                if (AstronautManager.main.flagPrefab != null) return;

                GameObject flagCandidate = UnityEngine.Resources.Load<GameObject>("Flag");
                if (flagCandidate != null)
                {
                    Flag flagComp = flagCandidate.GetComponent<Flag>();
                    if (flagComp != null)
                    {
                        typeof(AstronautManager).GetField("flagPrefab",
                            BindingFlags.Public | BindingFlags.Instance)
                            .SetValue(AstronautManager.main, flagComp);
                        return;
                    }
                }

                Flag[] existingFlags = UnityEngine.Object.FindObjectsOfType<Flag>(includeInactive: true);
                if (existingFlags != null && existingFlags.Length > 0)
                {
                    typeof(AstronautManager).GetField("flagPrefab",
                        BindingFlags.Public | BindingFlags.Instance)
                        .SetValue(AstronautManager.main, existingFlags[0]);
                    return;
                }

                
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 353", e);
            }
        }

        private static void ModifyDisableParts()
        {
            try
            {
                FieldInfo field = typeof(DevSettings).GetField("DisableParts",
                    BindingFlags.Static | BindingFlags.Public);
                if (field == null) return;
                string[] parts = (string[])field.GetValue(null);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == "Crew_New" || parts[i] == "Test")
                    {
                        parts[i] = "";
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 375", e);
            }
        }

        private static void EnsureAstronautMenuInstance()
        {
            if (AstronautMenu.main != null) return;
            GameObject go = new GameObject("__AstronautMenuHolder");
            UnityEngine.Object.DontDestroyOnLoad(go);
            RegisterPersistent(go);
            go.AddComponent<AstronautMenu>();
        }

        private static void EnsureAstronautState()
        {
            // 无论走哪条分支，都要保证 AstronautState 会在增删宇航员时自行写盘
            if (AstronautState.main != null)
                AstronautState.main.selfManageSaving = true;

            if (AstronautState.main != null && AstronautState.main.state != null) return;
            if (AstronautState.main == null)
            {
                GameObject go = new GameObject("__AstronautStateSafety");
                UnityEngine.Object.DontDestroyOnLoad(go);
                RegisterPersistent(go);
                AstronautState st = go.AddComponent<AstronautState>();
                if (st.state == null)
                    st.state = new WorldSave.Astronauts();
                if (st.crew_Build == null)
                    st.crew_Build = new List<string>();
                // 让 CreateAstronaut / FireAstronaut 自己写盘（原生 AstronautState.Save）
                st.selfManageSaving = true;
            }
            else if (AstronautState.main.state == null)
            {
                AstronautState.main.state = new WorldSave.Astronauts();
                if (AstronautState.main.crew_Build == null)
                    AstronautState.main.crew_Build = new List<string>();
            }

            if (AstronautState.main != null)
                AstronautState.main.selfManageSaving = true;
        }

        private static void CreatePersistentAstronautState()
        {
            if (AstronautState.main != null)
            {
                if (AstronautState.main.state == null)
                    AstronautState.main.state = new WorldSave.Astronauts();
                if (AstronautState.main.crew_Build == null)
                    AstronautState.main.crew_Build = new List<string>();
                return;
            }
            GameObject go = new GameObject("__PersistentAstronautState");
            UnityEngine.Object.DontDestroyOnLoad(go);
            RegisterPersistent(go);
            AstronautState st = go.AddComponent<AstronautState>();
            if (st.state == null)
                st.state = new WorldSave.Astronauts();
            if (st.crew_Build == null)
                st.crew_Build = new List<string>();
        }

        private static void EnsureCrewBuildList()
        {
            if (AstronautState.main != null && AstronautState.main.crew_Build == null)
                AstronautState.main.crew_Build = new List<string>();
        }

        public static void EnsureAllStateLists()
        {
            if (AstronautState.main == null) return;
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
            if (AstronautState.main.state.flags == null)
                AstronautState.main.state.flags =
                    new List<WorldSave.Astronauts.Flag>();
            if (AstronautState.main.state.collectedRocks == null)
                AstronautState.main.state.collectedRocks =
                    new Dictionary<string, HashSet<long>>();
        }

        /// <summary>
        /// 本局内被解雇（Discharge）的宇航员。用于阻止跨场景重载时把他们"复活"回来。
        /// </summary>
        public static readonly HashSet<string> DischargedAstronauts = new HashSet<string>();

        /// <summary>
        /// 直接把宇航员名册写进 World/Persistent/Astronauts.txt。
        ///
        /// 关键点：WorldSave.Save() 里有 `if (!DevSettings.DisableAstronauts)` 的判断，
        /// 而 DevSettings.DisableAstronauts 在 PC 版恒为 true（且是返回常量的属性，
        /// 极可能被 JIT 内联，导致本模组对 get_DisableAstronauts 的补丁失效），
        /// 因此 SavingCache.SaveWorldPersistent 实际上**永远不会**写入 Astronauts.txt。
        /// 结果是：在 Hub 里新建的宇航员只存在于内存，切到建造场景从磁盘重载后就消失了。
        /// 这里绕过该开关，同步落盘，并顺带刷新 SavingCache 的内存缓存。
        /// </summary>
        public static void SaveAstronautRosterToDisk()
        {
            try
            {
                if (AstronautState.main == null || AstronautState.main.state == null) return;
                if (Base.worldBase == null || Base.worldBase.paths == null) return;
                if (Base.worldBase.paths.worldPersistentPath == null) return;

                EnsureAllStateLists();
                WorldSave.Save_AstronautStates(
                    Base.worldBase.paths.worldPersistentPath,
                    AstronautState.main.state);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Save astronaut roster", e);
            }
        }

        private static void LoadAstronautDataFromCache()
        {
            try
            {
                if (AstronautState.main == null || SavingCache.main == null) return;

                WorldSave save = SavingCache.main.LoadWorldPersistent(
                    MsgDrawer.main, needsRocketsAndBranches: false, eraseCache: false);

                if (save?.astronauts == null) return;

                EnsureAllStateLists();
                MergeMissingAstronauts(save.astronauts);

                AstronautState.main.state = save.astronauts;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Astronaut cache load", e);
            }
        }

        /// <summary>
        /// 场景切换时磁盘/缓存里的名册可能还是旧的（异步保存、DevSettings 跳过写入等）。
        /// 把当前内存中已有、但磁盘副本里缺失的宇航员补回去，
        /// 避免"在 Hub 新建的宇航员进蓝图后找不到"。
        /// 已解雇的（本局 DischargedAstronauts）不补。
        /// </summary>
        private static void MergeMissingAstronauts(WorldSave.Astronauts loaded)
        {
            try
            {
                List<WorldSave.Astronauts.Data> current = AstronautState.main?.state?.astronauts;
                if (current == null || current.Count == 0) return;
                if (loaded.astronauts == null)
                    loaded.astronauts = new List<WorldSave.Astronauts.Data>();

                var known = new HashSet<string>();
                foreach (WorldSave.Astronauts.Data data in loaded.astronauts)
                    if (data != null && !string.IsNullOrEmpty(data.astronautName))
                        known.Add(data.astronautName);

                foreach (WorldSave.Astronauts.Data data in current)
                {
                    if (data == null || string.IsNullOrEmpty(data.astronautName)) continue;
                    if (known.Contains(data.astronautName)) continue;
                    if (DischargedAstronauts.Contains(data.astronautName)) continue;
                    loaded.astronauts.Add(new WorldSave.Astronauts.Data(data.astronautName, data.alive));
                    known.Add(data.astronautName);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Merge astronauts", e);
            }
        }

        public static void PersistAstronautStateToCache()
        {
            try
            {
                if (AstronautState.main == null || AstronautState.main.state == null) return;
                if (SavingCache.main == null) return;

                WorldSave save = SavingCache.main.LoadWorldPersistent(
                    MsgDrawer.main, needsRocketsAndBranches: false, eraseCache: false);

                if (save == null)
                {
                    
                    return;
                }

                WorldSave.Astronauts currentData = AstronautState.main.state;
                EnsureAllStateLists();
                save.astronauts = SavingCache.GetCopy(currentData);

                SavingCache.main.SaveWorldPersistent(save, cache: true,
                    saveRocketsAndBranches: false, addToRevert: false, deleteRevert: false);

                // SavingCache.SaveWorldPersistent 未必真的写 Astronauts.txt（见 SaveAstronautRosterToDisk 注释）
                // 这里补一次同步直写，确保名册一定落盘
                SaveAstronautRosterToDisk();
    }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Astronaut cache save", e);
            }
        }

        private static ModGUIButton hubAstronautButton;
        private static GameObject hubHolder;
        private static GameObject clonedAstronautBtn;

        private static void OnAstronautsButtonClick(SFS.Input.OnInputEndData data)
        {
            NativeAstronautUI.ShowMenu(null, null);
        }

        private static void ActivateAstronautsButton()
        {
            try
            {
                if (clonedAstronautBtn != null)
                {
                    UnityEngine.Object.Destroy(clonedAstronautBtn);
                    clonedAstronautBtn = null;
                }
                if (hubAstronautButton != null && hubAstronautButton.gameObject != null)
                {
                    UnityEngine.Object.Destroy(hubAstronautButton.gameObject);
                    hubAstronautButton = null;
                }
                if (hubHolder != null)
                {
                    UnityEngine.Object.Destroy(hubHolder);
                    hubHolder = null;
                }

                if (HubManager.main != null)
                {
                    FieldInfo resumeField = typeof(HubManager).GetField("resumeGameButton",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (resumeField != null)
                    {
                        object resumeBtn = resumeField.GetValue(HubManager.main);
                        if (resumeBtn != null)
                        {
                            GameObject resumeGO = (resumeBtn as MonoBehaviour)?.gameObject;
                            if (resumeGO != null && resumeGO.activeInHierarchy)
                            {
                                RectTransform resumeRT = resumeGO.GetComponent<RectTransform>();
                                if (resumeRT != null)
                                {
                                    clonedAstronautBtn = UnityEngine.Object.Instantiate(resumeGO, resumeGO.transform.parent);
                                    clonedAstronautBtn.name = "AstronautsButton_Clone";

                                    RectTransform astroRT = clonedAstronautBtn.GetComponent<RectTransform>();
                                    float resumeHeight = resumeRT.rect.height > 0 ? resumeRT.rect.height : 60f;
                                    astroRT.anchoredPosition = new Vector2(
                                        resumeRT.anchoredPosition.x,
                                        resumeRT.anchoredPosition.y + resumeHeight + 10f);

                                    Component[] allComponents = clonedAstronautBtn.GetComponentsInChildren<Component>(true);
                                    foreach (var comp in allComponents)
                                    {
                                        if (comp == null) continue;
                                        string typeName = comp.GetType().Name;
                                        if (typeName == "TranslationSelector")
                                        {
                                            UnityEngine.Object.Destroy(comp);
                                        }
                                        else if (typeName == "TextAdapter")
                                        {
                                            try
                                            {
                                                FieldInfo isInitField = comp.GetType().GetField("isInit",
                                                    BindingFlags.NonPublic | BindingFlags.Instance);
                                                if (isInitField != null)
                                                {
                                                    isInitField.SetValue(comp, false);
                                                }
                                                comp.GetType().GetProperty("Text")?.SetValue(comp, "Astronauts");
                                            }
                                            catch (Exception te)
                                            {
                                                
                                            }
                                        }
                                    }
                                    Text textComp = clonedAstronautBtn.GetComponentInChildren<Text>(true);
                                    if (textComp != null)
                                    {
                                        textComp.text = "Astronauts";
                                    }
                                    var tmpTexts = clonedAstronautBtn.GetComponentsInChildren<TMPro.TMP_Text>(true);
                                    foreach (var tmp in tmpTexts)
                                    {
                                        tmp.text = "Astronauts";
                                    }
                                    if (textComp == null && tmpTexts.Length == 0)
                                    {
                                        // 未找到文本组件
                                    }

                                    SFS.UI.Button sfsBtn = clonedAstronautBtn.GetComponent<SFS.UI.Button>();
                                    if (sfsBtn != null)
                                    {
                                        FieldInfo clickEventField = typeof(SFS.UI.Button).GetField("clickEvent",
                                            BindingFlags.Public | BindingFlags.Instance);
                                        if (clickEventField != null)
                                        {
                                            object newClickEvent = System.Activator.CreateInstance(clickEventField.FieldType);
                                            clickEventField.SetValue(sfsBtn, newClickEvent);

                                            Type unityActionType = typeof(UnityEngine.Events.UnityAction<SFS.Input.OnInputEndData>);
                                            MethodInfo callbackMethod = typeof(AstronautUnlockerMod)
                                                .GetMethod("OnAstronautsButtonClick",
                                                    BindingFlags.Static | BindingFlags.NonPublic);
                                            Delegate callback = Delegate.CreateDelegate(
                                                unityActionType, callbackMethod);

                                            MethodInfo addListener = newClickEvent.GetType()
                                                .GetMethod("AddListener", new Type[] { unityActionType });
                                            addListener?.Invoke(newClickEvent, new object[] { callback });
                                        }

                                        FieldInfo onClickField = typeof(SFS.UI.Button).GetField("onClick",
                                            BindingFlags.Public | BindingFlags.Instance);
                                        if (onClickField != null)
                                        {
                                            object newOnClick = System.Activator.CreateInstance(onClickField.FieldType);
                                            onClickField.SetValue(sfsBtn, newOnClick);
                                        }

                                        sfsBtn.SetEnabled(true);
                                    }

                                    return;
                                }
                            }
                            else
                            {
                                
                            }
                        }
                        else
                        {
                            
                        }
                    }
                }

                hubHolder = ModGUIBuilder.CreateHolder(ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_HubBtn");
                hubAstronautButton = ModGUIBuilder.CreateButton(hubHolder.transform, 200, 50,
                    0, 80,
                    () => NativeAstronautUI.ShowMenu(null, null),
                    "Astronauts");
            }
            catch (Exception e)
            {
                

                if (clonedAstronautBtn != null)
                {
                    UnityEngine.Object.Destroy(clonedAstronautBtn);
                    clonedAstronautBtn = null;
                }

                try
                {
                    hubHolder = ModGUIBuilder.CreateHolder(ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_HubBtn");
                    hubAstronautButton = ModGUIBuilder.CreateButton(hubHolder.transform, 200, 50,
                        0, 80,
                        () => NativeAstronautUI.ShowMenu(null, null),
                        "Astronauts");
                }
                catch (Exception fallbackError)
                {
                    ModLogger.ErrorOnce("Hub button fallback", fallbackError);
                }
            }
        }

        // ===== 模组部件 EVA 注入 =====

        // EVA 配置 部件名 -> 是否启用 EVA
        public static Dictionary<string, bool> evaConfig = new Dictionary<string, bool>();
        public static Dictionary<string, int> evaCrewCapacities = new Dictionary<string, int>();
        private const int DefaultCrewCapacity = 1;
        private const int MaxCrewCapacity = 5;
        // 配置现由 ModSettings（游戏设置文件夹下的 AstronautModSettings.json）持有
        public static bool allowUncrewedControl
        {
            get => ModSettings.main != null && ModSettings.main.settings != null &&
                ModSettings.main.settings.allowUncrewedControl;
            set
            {
                if (ModSettings.main?.settings != null)
                {
                    ModSettings.main.settings.allowUncrewedControl = value;
                    ModSettings.main.SaveSettings();
                }
            }
        }

        private static string ModConfigPath
        {
            get
            {
                try
                {
                    DirectoryInfo modDirectory = Directory.GetParent(FlagCustomization.FlagsDirectory);
                    if (modDirectory != null)
                        return Path.Combine(modDirectory.FullName, "config.txt");
                }
                catch (Exception e)
                {
                    ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 693", e);
                }
                return Path.Combine(Application.persistentDataPath, "AstronautMod", "config.txt");
            }
        }

        // 创建并加载模组设置（落到游戏设置文件夹，与 ModsSettings / KeybindingsPC 同机制）
        private static void EnsureModSettings()
        {
            if (ModSettings.main != null) return;
            GameObject go = new GameObject("AstronautModSettings");
            UnityEngine.Object.DontDestroyOnLoad(go);
            RegisterPersistent(go);
            go.AddComponent<ModSettings>(); // Awake 内 Load
        }

        // 旧版用 Mods 目录下的 config.txt 存 allowUncrewedControl，这里做一次迁移后删除该文件
        public static void LoadModConfig()
        {
            try
            {
                string path = ModConfigPath;
                if (!File.Exists(path)) return; // 默认值现由 ModSettings 提供

                foreach (string line in File.ReadAllLines(path))
                {
                    string value = (line ?? "").Trim();
                    if (value.Length == 0 || value.StartsWith("#")) continue;
                    int separator = value.IndexOf('=');
                    if (separator < 0) continue;
                    string key = value.Substring(0, separator).Trim();
                    string setting = value.Substring(separator + 1).Trim();
                    if (string.Equals(key, "allowUncrewedControl", StringComparison.OrdinalIgnoreCase))
                    {
                        bool parsed = setting == "1" ||
                            (bool.TryParse(setting, out bool p) && p);
                        allowUncrewedControl = parsed; // 写入 ModSettings
                    }
                }

                // 迁移完成 删除旧文件 避免重复读取
                try { File.Delete(path); } catch { }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Mod configuration", e);
            }
        }

        // 记录已由本模组注入 CrewModule 的部件 ID
        public static HashSet<int> injectedPartIds = new HashSet<int>();

        // 关闭 EVA 时暂存乘员 开启时恢复
        public static Dictionary<string, List<string>> savedAstronauts = new Dictionary<string, List<string>>();

        [Serializable]
        class EvaConfigEntry { public string key; public bool value; public int crewCapacity; }

        [Serializable]
        class EvaConfigData
        {
            public List<EvaConfigEntry> parts;
            public List<string> astronautPartNames;
            public List<string> astronautNames;
        }

        public static void LoadEvaConfig()
        {
            try
            {
                string path = Application.persistentDataPath + "/AstronautMod_eva.json";
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var data = JsonUtility.FromJson<EvaConfigData>(json);
                    if (data?.parts != null)
                    {
                        foreach (var entry in data.parts)
                        {
                            evaConfig[entry.key] = entry.value;
                            evaCrewCapacities[entry.key] = ClampCrewCapacity(entry.crewCapacity);
                        }
                    }
                    // 恢复暂存的乘员
                    if (data?.astronautPartNames != null && data.astronautNames != null &&
                        data.astronautPartNames.Count == data.astronautNames.Count)
                    {
                        for (int i = 0; i < data.astronautPartNames.Count; i++)
                        {
                            string pn = data.astronautPartNames[i];
                            if (!savedAstronauts.ContainsKey(pn))
                                savedAstronauts[pn] = new List<string>();
                            savedAstronauts[pn].Add(data.astronautNames[i]);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA configuration load", e);
            }
        }

        public static void SaveEvaConfig()
        {
            try
            {
                string path = Application.persistentDataPath + "/AstronautMod_eva.json";
                var data = new EvaConfigData();
                data.parts = evaConfig.Select(kv =>
                    new EvaConfigEntry
                    {
                        key = kv.Key,
                        value = kv.Value,
                        crewCapacity = GetCrewCapacity(kv.Key)
                    }).ToList();

                // 将暂存乘员以扁平列表持久化
                data.astronautPartNames = new List<string>();
                data.astronautNames = new List<string>();
                foreach (var kv in savedAstronauts)
                {
                    if (kv.Value == null) continue;
                    foreach (string name in kv.Value)
                    {
                        data.astronautPartNames.Add(kv.Key);
                        data.astronautNames.Add(name);
                    }
                }

                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA configuration save", e);
            }
        }

        // 离开世界时清空暂存乘员 避免新建火箭自动恢复旧乘员
        public static void ClearSavedAstronauts(string reason)
        {
            try
            {
                if (savedAstronauts.Count > 0)
                {
                    
                    savedAstronauts.Clear();
                    SaveEvaConfig();
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Saved crew cleanup", e);
            }
        }

        public static int GetCrewCapacity(string partName)
        {
            if (!string.IsNullOrWhiteSpace(partName) &&
                evaCrewCapacities.TryGetValue(partName, out int capacity))
                return ClampCrewCapacity(capacity);
            return DefaultCrewCapacity;
        }

        public static void SetCrewCapacity(string partName, int capacity)
        {
            if (string.IsNullOrWhiteSpace(partName)) return;
            evaCrewCapacities[partName] = ClampCrewCapacity(capacity);
        }

        /// <summary>
        /// Applies a new crew capacity to a concrete part and stores it on that part instance, so it is
        /// written into blueprints / rocket saves and survives restarts and reverts.
        /// </summary>
        public static void SetCrewCapacity(Part part, int capacity)
        {
            if (part == null) return;
            int clamped = ClampCrewCapacity(capacity);
            SetCrewCapacity(part.name, clamped);
            CrewPersistence.Write(part, CrewPersistence.CapacityVariable, clamped.ToString());
        }

        /// <summary>
        /// Re-reads the persisted configuration of a part and rebuilds its injected CrewModule.
        /// Used after external save data (Custom Save Data) has been merged back into a part.
        /// </summary>
        public static void ReimportCrewFromVariables(Part part)
        {
            if (part == null) return;
            try
            {
                RemoveCrewModule(part, keepPersistedData: true);
                CrewPersistence.Write(part, CrewPersistence.CapacityVariable,
                    ClampCrewCapacity(CrewPersistence.ReadCapacity(part)).ToString());
                InjectCrewModule(part);
                ClearModuleCache(part);
                UpdateDriver.ScheduleCrewCapacityApply(part, refreshMenu: false);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Crew re-import", e);
            }
        }

        public static int GetOccupiedCrewCount(Part part)
        {
            if (part == null) return 0;
            try
            {
                return part.GetComponentsInChildren<CrewModule>(true)
                    .Sum(crew => crew?.seats?.Count(seat => seat != null && seat.HasAstronaut) ?? 0);
            }
            catch { return 0; }
        }

        public static void OpenCrewCapacityMenu(Part part)
        {
            if (part == null || HasNativeCrewModule(part)) return;
            try
            {
                string partName = part.name;
                List<MenuElement> elements = new List<MenuElement>();
                SizeSyncerBuilder.Carrier carrier;
                elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));
                elements.Add(TextBuilder.CreateText(() => "Crew Capacity"));
                elements.Add(TextBuilder.CreateText(() =>
                    "The seat count refreshes automatically after this menu closes."));

                for (int value = DefaultCrewCapacity; value <= MaxCrewCapacity; value++)
                {
                    int capacity = value;
                    elements.Add(ButtonBuilder.CreateButton(carrier,
                        () => capacity == GetCrewCapacity(partName)
                            ? capacity + " Crew (Selected)"
                            : capacity + " Crew",
                        () =>
                        {
                            int occupied = GetOccupiedCrewCount(part);
                            if (capacity < occupied)
                            {
                                MenuGenerator.OpenConfirmation(
                                    CloseMode.Stack,
                                    () => "This part already has " + occupied +
                                        " astronauts. Crew capacity cannot be set below " + occupied + ".",
                                    () => "OK",
                                    delegate { });
                                return;
                            }

                            SetCrewCapacity(partName, capacity);
                            SetCrewCapacity(part, capacity);
                            if (!evaConfig.ContainsKey(partName)) evaConfig[partName] = false;
                            evaConfig[partName] = true;
                            SaveEvaConfig();
                            UpdateDriver.ScheduleCrewCapacityApply(part);
                        },
                        CloseMode.Current));
                }

                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Close",
                    () => { },
                    CloseMode.Current));
                MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Current, elements.ToArray());
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 932", e);
            }
        }

        public static bool ApplyCrewCapacity(Part part)
        {
            if (part == null || !injectedPartIds.Contains(part.GetInstanceID())) return false;
            try
            {
                CrewModule[] crews = part.GetComponentsInChildren<CrewModule>(true);
                if (crews == null || crews.Length == 0) return false;

                int targetCapacity = Mathf.Max(GetCrewCapacity(part.name), GetOccupiedCrewCount(part));
                if (targetCapacity != GetCrewCapacity(part.name))
                {
                    SetCrewCapacity(part.name, targetCapacity);
                    SaveEvaConfig();
                }

                bool changed = false;
                foreach (CrewModule crew in crews)
                    changed |= ResizeCrewSeats(part, crew, targetCapacity);
                changed |= RestoreSavedCrewToSeats(part, crews);
                if (changed) ClearModuleCache(part);

                // 容量与座位布局变化后回写到部件自身变量 保证蓝图/存档能带走
                foreach (CrewModule crew in crews)
                    CrewPersistence.SavePartState(part, crew);
                return changed;
            }
            catch { return false; }
        }

        private static bool RestoreSavedCrewToSeats(Part part, CrewModule[] crews)
        {
            if (part == null || crews == null ||
                !savedAstronauts.ContainsKey(part.name) || savedAstronauts[part.name] == null)
                return false;
            try
            {
                List<CrewModule.Seat> seats = crews
                    .Where(crew => crew?.seats != null)
                    .SelectMany(crew => crew.seats)
                    .Where(seat => seat != null && seat.astronaut != null)
                    .ToList();
                bool changed = false;
                foreach (string name in savedAstronauts[part.name])
                {
                    if (string.IsNullOrEmpty(name) || Patch_GameManager_LoadSave.IsPendingEVA(name) ||
                        seats.Any(seat => seat.astronaut.Value == name)) continue;
                    CrewModule.Seat empty = seats.FirstOrDefault(seat =>
                        string.IsNullOrEmpty(seat.astronaut.Value));
                    if (empty == null) break;
                    empty.astronaut.Value = name;
                    empty.OnStart();
                    changed = true;
                }
                foreach (CrewModule crew in crews)
                    RefreshCrewModuleState(crew);
                return changed;
            }
            catch { return false; }
        }

        private static bool ResizeCrewSeats(Part part, CrewModule crew, int targetCapacity)
        {
            if (crew == null) return false;
            CrewModule.Seat[] existing = crew.seats ?? new CrewModule.Seat[0];
            if (existing.Length == targetCapacity) return false;

            List<CrewModule.Seat> resized = existing
                .Where(seat => seat != null && seat.HasAstronaut)
                .Concat(existing.Where(seat => seat != null && !seat.HasAstronaut))
                .Take(targetCapacity).ToList();
            foreach (CrewModule.Seat removed in existing.Where(seat => seat != null && !resized.Contains(seat)))
            {
                if (!removed.externalSeat && removed.astronautModel != null)
                    UnityEngine.Object.Destroy(removed.astronautModel);
            }

            Vector2 hatchPosition = existing.Length > 0 && existing[0] != null
                ? existing[0].hatchPosition
                : CalcHatchPosition(part);
            while (resized.Count < targetCapacity)
            {
                CrewModule.Seat seat = CreateExtraSeat(part, crew, existing, hatchPosition, resized.Count);
                seat.astronaut.OnChange += () => RefreshCrewModuleState(crew);
                resized.Add(seat);
            }

            crew.seats = resized.ToArray();
            RefreshCrewModuleState(crew);
            return true;
        }

        private static CrewModule.Seat CreateExtraSeat(Part part, CrewModule crew,
            CrewModule.Seat[] existing, Vector2 hatchPosition, int index)
        {
            CrewModule.Seat template = existing.FirstOrDefault(seat => seat != null);
            GameObject model = null;
            if (template?.astronautModel != null)
            {
                Transform parent = template.astronautModel.transform.parent;
                model = UnityEngine.Object.Instantiate(template.astronautModel, parent);
                model.name = "AstronautMod_ExtraSeatModel_" + index;
                model.transform.localPosition += new Vector3(0.12f * index, 0f, 0f);
                model.SetActive(false);
            }

            return new CrewModule.Seat
            {
                // 绑定部件自身变量 扩展座位同样能进入蓝图/存档
                astronaut = CrewPersistence.Bind(part, CrewPersistence.SeatVariable(index)),
                hatchPosition = hatchPosition,
                externalSeat = false,
                astronautModel = model,
                resources = null
            };
        }

        private static void RefreshCrewModuleState(CrewModule crew)
        {
            try
            {
                Traverse.Create(crew).Method("OnSeatChange").GetValue();
                if (crew.seats == null) return;
                foreach (CrewModule.Seat seat in crew.seats)
                {
                    if (seat != null && !seat.externalSeat && seat.astronautModel != null)
                        seat.astronautModel.SetActive(seat.HasAstronaut);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1058", e);
            }
        }

        private static int ClampCrewCapacity(int capacity)
        {
            return Mathf.Clamp(capacity <= 0 ? DefaultCrewCapacity : capacity,
                DefaultCrewCapacity, MaxCrewCapacity);
        }

        // 该部件是否自带原生 CrewModule（非本模组注入）
        public static bool HasNativeCrewModule(Part part)
        {
            if (!part.HasModule<CrewModule>()) return false;
            return !injectedPartIds.Contains(part.GetInstanceID());
        }

        public static void InjectCrewModule(Part part)
        {
            try
            {
                int partId = part.GetInstanceID();

                // 跳过已注入或已带原生 CrewModule 的部件
                if (injectedPartIds.Contains(partId)) return;
                if (part.GetComponentInChildren<CrewModule>(true) != null) return;

                // 添加 CrewModule 组件
                CrewModule crew = part.gameObject.AddComponent<CrewModule>();

                var tr = Traverse.Create(crew);
                // 用当前质量作为 baseMass 避免 OnSeatChange 覆盖
                float existingMass = part.mass != null ? part.mass.Value : 0f;
                tr.Field("baseMass").SetValue(existingMass);
                tr.Field("part").SetValue(part);

                // 优先使用部件自身持久化数据（随蓝图/存档传输） 其次退回按部件名的全局配置
                int storedCapacity = CrewPersistence.ReadCapacity(part);
                bool hasStoredConfig = CrewPersistence.HasStoredConfig(part);
                int crewCapacity = storedCapacity > 0
                    ? ClampCrewCapacity(storedCapacity)
                    : GetCrewCapacity(part.name);
                if (storedCapacity > 0)
                {
                    // 让 UI 开关与实际状态保持一致（重启后仍知道该部件已启用）
                    evaConfig[part.name] = true;
                    evaCrewCapacities[part.name] = crewCapacity;
                }

                CrewModule.Seat[] seats = new CrewModule.Seat[crewCapacity];
                Vector2 hatchPos = CalcHatchPosition(part);
                for (int index = 0; index < seats.Length; index++)
                {
                    CrewModule.Seat seat = new CrewModule.Seat();
                    var seatTr = Traverse.Create(seat);
                    seatTr.Field("hatchPosition").SetValue(hatchPos);
                    seatTr.Field("externalSeat").SetValue(false);
                    // 绑定到部件自身的可保存变量 使乘员名进入蓝图/火箭存档
                    seatTr.Field("astronaut").SetValue(
                        CrewPersistence.Bind(part, CrewPersistence.SeatVariable(index)));
                    seatTr.Field("astronautModel").SetValue(null);
                    seatTr.Field("resources").SetValue(null);
                    seats[index] = seat;
                }

                // 载入已保存的乘员（原生字串变量在 PartSave 还原时已写好）
                for (int index = 0; index < seats.Length; index++)
                {
                    string storedName = CrewPersistence.ReadSeat(part, index);
                    if (string.IsNullOrEmpty(storedName)) continue;
                    var seatRef = Traverse.Create(seats[index]).Field("astronaut")
                        .GetValue<String_Reference>();
                    if (seatRef != null) seatRef.Value = storedName;
                }

                CrewPersistence.Write(part, CrewPersistence.CapacityVariable, crewCapacity.ToString());
                tr.Field("seats").SetValue(seats);

                var needsCrewRef = new Bool_Reference();
                tr.Field("needsCrewForControl").SetValue(needsCrewRef);

                // hasControl 独立于 ControlModule
                var hasControlRef = new Bool_Reference();
                tr.Field("hasControl").SetValue(hasControlRef);

                tr.Field("interior").SetValue(null);
                tr.Field("hatch").SetValue(null);

                // 先标记已注入 供 OnSeatChange 判断
                injectedPartIds.Add(partId);

                // 清模块缓存
                ClearModuleCache(part);

                // 初始化 注册回调并调用 Seat.OnStart
                try
                {
                    ((I_InitializePartModule)crew).Initialize();
                }
                catch (Exception ie)
                {
                    
                }

                needsCrewRef.Value = !allowUncrewedControl;
                hasControlRef.Value = allowUncrewedControl;

                // 兼容旧存档 仅在部件自身没有持久化数据时用全局缓存填补空座位
                string partName = part.name;
                if (!hasStoredConfig && savedAstronauts.ContainsKey(partName) &&
                    savedAstronauts[partName].Count > 0)
                {
                    var names = new List<string>(savedAstronauts[partName]);
                    bool isRevert = Patch_GameManager_LoadSave.isRevertLoad;
                    var baseline = Patch_GameManager_LoadPersistentAndLaunch.launchDeadBaseline;

                    foreach (string name in names)
                    {
                        // 本次加载计划恢复为 EVA 的人员不能同时恢复进座位
                        if (Patch_GameManager_LoadSave.IsPendingEVA(name)) continue;
                        CrewModule.Seat seat = seats.FirstOrDefault(candidate =>
                            candidate != null && candidate.astronaut != null &&
                            string.IsNullOrEmpty(candidate.astronaut.Value));
                        if (seat == null) break;
                        try
                        {
                            if (AstronautState.main != null)
                            {
                                var data = AstronautState.main.GetAstronautByName(name);
                                bool alive = data != null && data.alive;
                                bool diedThisMissionReverted = isRevert && !alive &&
                                    (baseline == null || !baseline.Contains(name));
                                if (!alive && !diedThisMissionReverted) continue;

                                seat.Board(name, 1.0, float.NegativeInfinity);
                            }
                            else
                            {
                                var seatAstroRef = Traverse.Create(seat)
                                    .Field("astronaut").GetValue<String_Reference>();
                                if (seatAstroRef != null)
                                    seatAstroRef.Value = name;
                            }
                        }
                        catch (Exception be)
                        {
                            
                        }
                    }

                    // 保留 savedAstronauts 供后续回退恢复 离开世界时再清空
                }

                // 回写最新的容量/乘员到部件自身变量
                CrewPersistence.SavePartState(part, crew);
                
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1185", e);
            }
        }

        static Vector2 CalcHatchPosition(Part part)
        {
            try
            {
                Collider2D[] cols = part.GetComponentsInChildren<Collider2D>();
                if (cols.Length > 0)
                {
                    Bounds bounds = cols[0].bounds;
                    foreach (var c in cols)
                        bounds.Encapsulate(c.bounds);

                    // 舱口位于部件顶部（局部坐标）
                    Vector3 topLocal = part.transform.InverseTransformPoint(
                        new Vector3(bounds.center.x, bounds.max.y, 0));
                    return new Vector2(topLocal.x, topLocal.y);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1208", e);
            }
            return new Vector2(0f, 0.5f); // 默认回退
        }

        public static void RemoveCrewModule(Part part)
        {
            RemoveCrewModule(part, keepPersistedData: false);
        }

        public static void RemoveCrewModule(Part part, bool keepPersistedData)
        {
            try
            {
                int partId = part.GetInstanceID();

                // 仅移除由本模组注入的
                if (!injectedPartIds.Contains(partId)) return;

                CrewModule[] crews = part.GetComponentsInChildren<CrewModule>(true);
                foreach (var crew in crews)
                {
                    // 移除前暂存乘员名
                    if (crew.seats != null)
                    {
                        var savedList = new List<string>();
                        foreach (var seat in crew.seats)
                        {
                            if (seat.HasAstronaut)
                            {
                                savedList.Add(seat.astronaut.Value);
                                try { seat.Exit(); }
                                catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1234", e);
            }
                            }
                        }
                        if (savedList.Count > 0)
                            savedAstronauts[part.name] = savedList;
                    }
                    UnityEngine.Object.Destroy(crew);
                }
                injectedPartIds.Remove(partId);
                ClearModuleCache(part);

                // 关闭 EVA 即彻底移除该部件的持久化配置 避免后续残留
                if (!keepPersistedData)
                    CrewPersistence.ClearPartState(part);

                SaveEvaConfig(); // 持久化乘员名到 JSON
                
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1247", e);
            }
        }

        public static void ClearModuleCache(Part part)
        {
            try
            {
                var modulesField = typeof(Part).GetField("modules",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var moduleCountField = typeof(Part).GetField("moduleCount",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                modulesField?.SetValue(part, new Dictionary<string, object>());
                moduleCountField?.SetValue(part, new Dictionary<string, int>());
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1264", e);
            }
        }

        public static void ReopenPartMenu(Part part)
        {
            try
            {
                AttachableStatsMenu menu = BuildManager.main != null
                    ? BuildManager.main.buildMenus.partMenu
                    : UnityEngine.Object.FindObjectOfType<AttachableStatsMenu>(true);
                if (menu == null) return;

                Func<Vector2> position = AttachWithArrow.FollowPart(part);
                try
                {
                    Func<Vector2> currentPosition = Traverse.Create(menu.attach)
                        .Field("getScreenPosition").GetValue<Func<Vector2>>();
                    if (currentPosition != null) position = currentPosition;
                }
                catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1286", e);
            }

                PartDrawSettings settings = BuildManager.main != null
                    ? PartDrawSettings.BuildSettings
                    : PartDrawSettings.WorldSettings;
                menu.Open_DrawPart(() => true, new Part[] { part }, settings, position,
                    dontUpdateOnZoomChange: false, skipAnimation: false);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 1294", e);
            }
        }
    }
}
