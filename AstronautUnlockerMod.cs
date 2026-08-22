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
        public override string ModVersion => "3.7.2";
        public override string Description => "Enables the native astronaut/crew system on PC.";

        public override void Early_Load()
        {
            HarmonyInstance = new Harmony("com.sfs.astronautunlocker");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            PatchVariableLists();
            ModifyDisableParts();
            CreatePersistentAstronautState();
            LoadEvaConfig();
            
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
                
            }
        }

        public override void Load()
        {
            SceneHelper.OnHubSceneLoaded += OnHubSceneLoaded;
            SceneHelper.OnBuildSceneLoaded += OnBuildSceneLoaded;
            SceneHelper.OnWorldSceneLoaded += OnWorldSceneLoaded;
            GameObject driverObj = new GameObject("__AstronautUnlockerUpdater");
            UnityEngine.Object.DontDestroyOnLoad(driverObj);
            driverObj.AddComponent<UpdateDriver>();
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
                // 不清空 crew_Build，进入世界时再转换为 crew_World
                LoadAstronautDataFromCache();
                EnsureAllStateLists();
                UpdateDriver.ScheduleCrewModuleRefresh();

                UpdateDriver.SchedulePickGridRefresh();
            }
            catch (Exception e)
            {
                
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

                Patch_Rocket_UseParts.ClearPatchedParts();

                if (AstronautManager.main == null)
                {
                    
                    GameObject go = new GameObject("__AstronautManagerFallback");
                    UnityEngine.Object.DontDestroyOnLoad(go);
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

                        // 乘员仍为 Available 说明 AddCrew 未执行，补上
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
                RockSelector rs = go.AddComponent<RockSelector>();
            }
            catch (Exception e)
            {
                
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
                
            }
        }

        private static void EnsureAstronautMenuInstance()
        {
            if (AstronautMenu.main != null) return;
            GameObject go = new GameObject("__AstronautMenuHolder");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AstronautMenu>();
        }

        private static void EnsureAstronautState()
        {
            if (AstronautState.main != null && AstronautState.main.state != null) return;
            if (AstronautState.main == null)
            {
                GameObject go = new GameObject("__AstronautStateSafety");
                UnityEngine.Object.DontDestroyOnLoad(go);
                AstronautState st = go.AddComponent<AstronautState>();
                if (st.state == null)
                    st.state = new WorldSave.Astronauts();
                if (st.crew_Build == null)
                    st.crew_Build = new List<string>();
            }
            else if (AstronautState.main.state == null)
            {
                AstronautState.main.state = new WorldSave.Astronauts();
                if (AstronautState.main.crew_Build == null)
                    AstronautState.main.crew_Build = new List<string>();
            }
        }

        private static AstronautState persistentState;

        private static void CreatePersistentAstronautState()
        {
            if (AstronautState.main != null)
            {
                persistentState = AstronautState.main;
                if (persistentState.state == null)
                    persistentState.state = new WorldSave.Astronauts();
                if (persistentState.crew_Build == null)
                    persistentState.crew_Build = new List<string>();
                return;
            }
            GameObject go = new GameObject("__PersistentAstronautState");
            UnityEngine.Object.DontDestroyOnLoad(go);
            persistentState = go.AddComponent<AstronautState>();
            if (persistentState.state == null)
                persistentState.state = new WorldSave.Astronauts();
            if (persistentState.crew_Build == null)
                persistentState.crew_Build = new List<string>();
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
        }

        private static void LoadAstronautDataFromCache()
        {
            try
            {
                if (AstronautState.main == null) return;

                if (AstronautState.main.state?.astronauts != null &&
                    AstronautState.main.state.astronauts.Count > 0)
                    return;

                if (SavingCache.main == null) return;

                WorldSave save = SavingCache.main.LoadWorldPersistent(
                    MsgDrawer.main, needsRocketsAndBranches: false, eraseCache: false);

                if (save?.astronauts != null)
                {
                    AstronautState.main.state = save.astronauts;
                }
            }
            catch (Exception e)
            {
                
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
                save.astronauts = SavingCache.GetCopy(currentData);

                SavingCache.main.SaveWorldPersistent(save, cache: true,
                    saveRocketsAndBranches: false, addToRevert: false, deleteRevert: false);
    }
            catch (Exception e)
            {
                
            }
        }

        private static ModGUIButton hubAstronautButton;
        private static GameObject hubHolder;
        private static GameObject clonedAstronautBtn;
        public static bool buoyancyPostfixLogged = false;

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
                catch { }
            }
        }

        // ===== 模组部件 EVA 注入 =====

        // EVA 配置：部件名 -> 是否启用 EVA
        public static Dictionary<string, bool> evaConfig = new Dictionary<string, bool>();

        // 记录已由本模组注入 CrewModule 的部件 ID
        public static HashSet<int> injectedPartIds = new HashSet<int>();

        // 关闭 EVA 时暂存乘员，开启时恢复
        public static Dictionary<string, List<string>> savedAstronauts = new Dictionary<string, List<string>>();

        [Serializable]
        class EvaConfigEntry { public string key; public bool value; }

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
                            evaConfig[entry.key] = entry.value;
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
            catch (Exception e) {  }
        }

        public static void SaveEvaConfig()
        {
            try
            {
                string path = Application.persistentDataPath + "/AstronautMod_eva.json";
                var data = new EvaConfigData();
                data.parts = evaConfig.Select(kv =>
                    new EvaConfigEntry { key = kv.Key, value = kv.Value }).ToList();

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
            catch (Exception e) {  }
        }

        // 离开世界时清空暂存乘员，避免新建火箭自动恢复旧乘员
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
            catch (Exception e) {  }
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
                // 用当前质量作为 baseMass，避免 OnSeatChange 覆盖
                float existingMass = part.mass != null ? part.mass.Value : 0f;
                tr.Field("baseMass").SetValue(existingMass);
                tr.Field("part").SetValue(part);

                // 创建座椅
                var seat = new CrewModule.Seat();
                var seatTr = Traverse.Create(seat);

                // 根据碰撞体计算舱口位置
                Vector2 hatchPos = CalcHatchPosition(part);
                seatTr.Field("hatchPosition").SetValue(hatchPos);
                seatTr.Field("externalSeat").SetValue(false);

                // 创建乘员引用（初始为空）
                var astronautRef = new String_Reference();
                seatTr.Field("astronaut").SetValue(astronautRef);

                seatTr.Field("astronautModel").SetValue(null);
                seatTr.Field("resources").SetValue(null);

                tr.Field("seats").SetValue(new CrewModule.Seat[] { seat });

                // 注入部件不要求乘员即可控制
                var needsCrewRef = new Bool_Reference();
                tr.Field("needsCrewForControl").SetValue(needsCrewRef);

                // hasControl 独立于 ControlModule
                var hasControlRef = new Bool_Reference();
                tr.Field("hasControl").SetValue(hasControlRef);

                tr.Field("interior").SetValue(null);
                tr.Field("hatch").SetValue(null);

                // 先标记已注入，供 OnSeatChange 判断
                injectedPartIds.Add(partId);

                // 清模块缓存
                ClearModuleCache(part);

                // 初始化：注册回调并调用 Seat.OnStart
                try
                {
                    ((I_InitializePartModule)crew).Initialize();
                }
                catch (Exception ie)
                {
                    
                }

                // 初始化后重新强制 needsCrewForControl=false
                needsCrewRef.Value = false;
                hasControlRef.Value = true;

                // 恢复暂存乘员
                string partName = part.name;
                if (savedAstronauts.ContainsKey(partName) && savedAstronauts[partName].Count > 0)
                {
                    var names = new List<string>(savedAstronauts[partName]); // Copy
                    bool isRevert = Patch_GameManager_LoadSave.isRevertLoad;
                    var baseline = Patch_GameManager_LoadPersistentAndLaunch.launchDeadBaseline;

                    foreach (string name in names)
                    {
                        try
                        {
                            if (AstronautState.main != null)
                            {
                                // 只恢复当前存活、或本次任务死亡被回退复活的乘员。
                                // 否则建筑新火箭可能错误复活已死乘员。
                                var data = AstronautState.main.GetAstronautByName(name);
                                bool alive = data != null && data.alive;
                                bool diedThisMissionReverted = isRevert && !alive &&
                                    (baseline == null || !baseline.Contains(name));
                                if (!alive && !diedThisMissionReverted)
                                {
                                    
                                    continue;
                                }

                                // Board 会调用 AddCrew 并设置乘员名
                                seat.Board(name, 1.0, float.NegativeInfinity);
                                
                            }
                            else
                            {
                                // AstronautState 未就绪时仅设名字，进入世界时再 AddCrew
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

                    // 保留 savedAstronauts，供后续回退恢复；离开世界时再清空
                }

                
            }
            catch (Exception e)
            {
                
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
            catch { }
            return new Vector2(0f, 0.5f); // 默认回退
        }

        public static void RemoveCrewModule(Part part)
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
                                catch { }
                            }
                        }
                        if (savedList.Count > 0)
                            savedAstronauts[part.name] = savedList;
                    }
                    UnityEngine.Object.DestroyImmediate(crew);
                }
                injectedPartIds.Remove(partId);
                ClearModuleCache(part);
                SaveEvaConfig(); // 持久化乘员名到 JSON
                
            }
            catch (Exception e)
            {
                
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
                
            }
        }

        public static void ReopenPartMenu(Part part)
        {
            try
            {
                if (BuildManager.main != null)
                {
                    // 建造模式
                    AttachableStatsMenu menu = BuildManager.main.buildMenus.partMenu;
                    if (menu != null)
                    {
                        PartDrawSettings settings = PartDrawSettings.BuildSettings;
                        menu.Open_DrawPart(() => true, new Part[] { part },
                            settings, () => (Vector2)part.transform.position,
                            false, false);
                    }
                }
                else
                {
                    // 世界模式
                    AttachableStatsMenu menu = UnityEngine.Object.FindObjectOfType<AttachableStatsMenu>(true);
                    if (menu != null)
                    {
                        PartDrawSettings settings = PartDrawSettings.WorldSettings;
                        menu.Open_DrawPart(() => true, new Part[] { part },
                            settings, () => (Vector2)part.transform.position,
                            false, false);
                    }
                }
            }
            catch (Exception e)
            {
                
            }
        }
    }

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
    // 回退复活、正常进入保持死亡。
    // 判断依据：LoadSave 来自 LoadPersistentAndLaunch（正常进入）则保存死亡；
    // 来自其他回退则复活本次任务死亡的乘员。
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
        private static bool isPersistentEntry;
        // 标记当前 LoadSave 是否为回退（非正常进入）
        public static bool isRevertLoad;

        static void Prefix(WorldSave save)
        {
            try
            {
                // 消费正常进入标记
                isPersistentEntry = Patch_GameManager_LoadPersistentAndLaunch.isPersistentEntry;
                Patch_GameManager_LoadPersistentAndLaunch.isPersistentEntry = false;
                // 非正常进入的 LoadSave 即为回退
                isRevertLoad = !isPersistentEntry;
                

                // 回退会重建世界，注入部件座椅不会被序列化，需先捕获乘员名
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

                // backupCrewBuild 仅用于建造到世界转换（保留 crew_Build）。
                // 已死亡乘员不得重新加入 crew_Build/crew_World。
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
                // 不覆盖已有条目的 alive 标志，存档值是权威的（回退存档为 alive=true）
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

                // --- 处理 backupCrewBuild（建造到世界转换）---
                // 确保 crew_Build 乘员进入世界的 crew_World
                if (backupCrewBuild != null && backupCrewBuild.Count > 0 &&
                    save?.astronauts != null)
                {
                    if (save.astronauts.crew_World == null)
                        save.astronauts.crew_World = new List<WorldSave.Astronauts.Crew_World>();
                    if (save.astronauts.eva == null)
                        save.astronauts.eva = new List<WorldSave.Astronauts.EVA>();

                    save.astronauts.crew_World.RemoveAll(c => backupCrewBuild.Contains(c.astronautName));
                    save.astronauts.eva.RemoveAll(e => backupCrewBuild.Contains(e.astronautName));

                    foreach (string name in backupCrewBuild)
                    {
                        bool exists = save.astronauts.crew_World.Any(c => c.astronautName == name);
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
                            // 仅添加缺失项，不覆盖 alive 标志（存档值为权威）
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

                // 恢复 crew_Build（仅建造到世界转换）。已死亡乘员不在 backupCrewBuild 中
                if (backupCrewBuild != null && backupCrewBuild.Count > 0)
                {
                    if (AstronautState.main?.crew_Build != null)
                    {
                        foreach (string name in backupCrewBuild)
                        {
                            if (!AstronautState.main.crew_Build.Contains(name))
                            {
                                AstronautState.main.crew_Build.Add(name);
                            }
                        }
                    }
                    backupCrewBuild = null;
                }

                Patch_Seat_OnDestroy.destroyedSeatAstronauts.Clear();

                // --- 回退复活 ---
                // 这是真正的回退（非正常进入）。存档可能带 stale alive=false，
                // 仅复活任务开始前仍存活、本次任务死亡的乘员。
                
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
                
            }
        }

        // 回退前捕获注入部件座椅上的乘员名到 savedAstronauts，
        // 供重建后的座椅恢复（回退会清空世界）
        private static void CaptureInjectedCrewToSaved()
        {
            try
            {
                if (GameManager.main == null) return;
                CrewModule[] allCrews = UnityEngine.Object.FindObjectsOfType<CrewModule>(true);
                bool changed = false;
                foreach (CrewModule crew in allCrews)
                {
                    if (crew == null || crew.seats == null) continue;
                    Part part = Traverse.Create(crew).Field("part").GetValue<Part>();
                    if (part == null) continue;
                    if (!AstronautUnlockerMod.injectedPartIds.Contains(part.GetInstanceID())) continue;
                    if (part.name == null) continue;

                    var names = new List<string>();
                    foreach (var seat in crew.seats)
                    {
                        if (seat == null || seat.astronaut == null) continue;
                        if (!string.IsNullOrEmpty(seat.astronaut.Value))
                            names.Add(seat.astronaut.Value);
                    }
                    if (names.Count == 0) continue;

                    if (!AstronautUnlockerMod.savedAstronauts.ContainsKey(part.name))
                        AstronautUnlockerMod.savedAstronauts[part.name] = new List<string>();
                    foreach (string n in names)
                    {
                        if (!AstronautUnlockerMod.savedAstronauts[part.name].Contains(n))
                        {
                            AstronautUnlockerMod.savedAstronauts[part.name].Add(n);
                            changed = true;
                        }
                    }
                    
                }
                if (changed)
                    AstronautUnlockerMod.SaveEvaConfig();
            }
            catch (Exception e)
            {
                
            }
        }
    }

    // --- 正常退出世界清空 savedAstronauts ---
    // 离开世界（新建火箭 / 返回中心 / 主菜单）即进入全新上下文。
    // 清空可防止此前捕获的（可能已死亡）乘员被自动恢复到新建造中。
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
    // RevertToBuild 不走 LoadSave，而是用 deleteRevert=true 持久化发射快照。
    // 此处同样复活，使回退撤销死亡；正常保存/退出（deleteRevert=false）保持死亡。
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
                    // 仅复活本次任务死亡的乘员；任务前已死亡的保持死亡
                    if (!sd.alive &&
                        (baseline == null || !baseline.Contains(sd.astronautName)))
                    {
                        sd.alive = true;
                        
                    }
                }
            }
            catch (Exception e)
            {
                
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
                catch { }
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
            return false; // skip original (which needs null prefabs)
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
                    return false; // No astronaut, skip (original returns early too)

                if (AstronautState.main == null || AstronautState.main.state == null)
                {
                    return false; // Don't let original clear the seat
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
                        AstronautState.main.AddCrew(astronautName); // Adds to crew_World in world mode
                    }
                    tr.Method("AddSeatedAstronaut").GetValue();
                    return false;
                }
                else
                {
                    
                    // 回退时存档的 alive 标志已过时（本次任务死亡但被回退复活）。
                    // 保留座椅乘员不清空，但仅限本次任务死亡的乘员，任务前已死亡的不恢复。
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
            catch { }
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
            catch { }
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
    }

    // EndMissionMenu 检查 HasCrew：为 true 会强制销毁流程（无法回收）。
    // 本模组在 PC 端启用乘员，座椅有名字导致 HasCrew=true 阻止回收。
    // 补丁返回 false 以走正常回收/销毁流程。
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
                    return true; // Let original handle empty case

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
                                
                            }
                        });
                        patchedParts.Add(id);
                    }
                }
                return true; // Let original method run — preserves recovery logic
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

                // PC 部件无持久化事件，原 UseParts 会跳过它们。
                // 手动用结果数据调用 onPartUsed。
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

                // 判断是否为注入的 CrewModule
                SFS.Parts.Part part = tr.Field("part").GetValue<SFS.Parts.Part>();
                bool isInjected = part != null &&
                    AstronautUnlockerMod.injectedPartIds.Contains(part.GetInstanceID());

                bool disableAstronauts = DevSettings.DisableAstronauts;

                bool anyHasAstronaut = false;
                if (__instance.seats != null)
                {
                    foreach (var seat in __instance.seats)
                    {
                        if (seat.HasAstronaut) { anyHasAstronaut = true; break; }
                    }
                }

                var needsCrewRef = tr.Field("needsCrewForControl")
                    .GetValue<SFS.Variables.Bool_Reference>();
                bool needsCrew = needsCrewRef != null && needsCrewRef.Value;

                // 注入部件恒为 hasControl=true；原生部件走原逻辑
                bool hasControl = isInjected
                    ? true
                    : (disableAstronauts || anyHasAstronaut || !needsCrew);

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
                
            }
        }
    }

    [HarmonyPatch(typeof(AstronautManager), "SpawnFlag")]
    public class Patch_AstronautManager_SpawnFlag
    {
        static bool Prefix(AstronautManager __instance, ref Flag __result,
            Location location, int direction)
        {
            try
            {
                if (__instance.flagPrefab != null)
                    return true; // Original prefab exists, use original

                
                __result = FlagFallback.CreateFlag(location, direction);
                return false;
            }
            catch (Exception e)
            {
                
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Flag), "Start")]
    public class Patch_Flag_Start
    {
        static bool Prefix(Flag __instance)
        {
            try
            {
                var tr = Traverse.Create(__instance);
                Transform holder = tr.Field<Transform>("holder").Value;
                MapIcon mapIcon = tr.Field<MapIcon>("mapIcon").Value;
                int direction = tr.Field<int>("direction").Value;

                if (holder != null)
                {
                    holder.localScale = new Vector2(direction, 1f);
                    holder.rotation = Quaternion.Euler(0f, 0f,
                        (float)__instance.location.position.Value.AngleDegrees - 90f);
                }

                if (mapIcon != null && mapIcon.mapIcon != null)
                {
                    mapIcon.SetRotation(holder.rotation.eulerAngles.z + 90f);
                }

                
                return false; // Skip original (handles null safely)
            }
            catch (Exception e)
            {
                
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(AstronautManager), "PlantFlag")]
    public class Patch_AstronautManager_PlantFlag
    {
        static bool Prefix()
        {
            try
            {
                if (PlayerController.main?.player?.Value is Astronaut_EVA eva)
                {
                    if (!PlanetSurfaceHelper.IsSolidPlanet(eva))
                    {
                        Menu.read.Open(() => "Cannot plant a flag on a gas giant — no solid surface!");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                
            }
            return true;
        }
    }

    public static class FlagFallback
    {
        private static Sprite flagSprite;

        public static Flag CreateFlag(Location location, int direction)
        {
            GameObject root = new GameObject("__FallbackFlag");
            root.SetActive(false); // Prevent OnEnable before setup

            Flag flag = root.AddComponent<Flag>();

            var tr = Traverse.Create(flag);
            var worldLoc = tr.Field<WorldLocation>("location").Value;
            if (worldLoc == null)
            {
                worldLoc = root.AddComponent<WorldLocation>();
                tr.Field("location").SetValue(worldLoc);
            }
            worldLoc.planet.Value = location.planet;
            worldLoc.position.Value = location.position;
            worldLoc.velocity.Value = location.velocity;

            GameObject holderObj = new GameObject("Holder");
            holderObj.transform.SetParent(root.transform, false);
            holderObj.transform.localPosition = Vector3.zero;

            SpriteRenderer sr = holderObj.AddComponent<SpriteRenderer>();
            sr.sprite = GetFlagSprite();
            sr.color = new Color(0.9f, 0.2f, 0.2f, 1f); // Red flag
            sr.sortingOrder = 100;
            holderObj.transform.localScale = new Vector3(0.3f, 0.6f, 1f);
            holderObj.transform.localPosition = new Vector3(0f, 0.3f, 0f);

            tr.Field("holder").SetValue(holderObj.transform);

            tr.Field("direction").SetValue(direction);

            tr.Field("mapIcon").SetValue(null);

            root.transform.position = WorldView.ToLocalPosition(location.position);

            root.SetActive(true);

            return flag;
        }

        private static Sprite GetFlagSprite()
        {
            if (flagSprite != null) return flagSprite;

            flagSprite = UnityEngine.Resources.Load<Sprite>("Flag");
            if (flagSprite != null) return flagSprite;

            Texture2D tex = new Texture2D(4, 4);
            Color[] pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            flagSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return flagSprite;
        }
    }

    public static class PlanetSurfaceHelper
    {
        public static bool IsSolidPlanet(Astronaut_EVA eva)
        {
            if (eva == null) return true;
            try
            {
                WorldLocation wl = eva.location;
                if (wl == null) return true;
                Planet planet = wl.planet.Value;
                if (planet == null || planet.data == null) return true;
                return planet.data.hasTerrain;
            }
            catch { return true; }
        }

        public static bool IsSolidPlanet(CrewModule crewModule)
        {
            if (crewModule == null) return true;
            try
            {
                Rocket rocket = crewModule.GetComponent<Rocket>();
                if (rocket == null) return true;
                WorldLocation wl = rocket.location;
                if (wl == null) return true;
                Planet planet = wl.planet.Value;
                if (planet == null || planet.data == null) return true;
                return planet.data.hasTerrain;
            }
            catch { return true; }
        }
    }

    public static class PlantFlagButtonHelper
    {
        private static ModGUIButton plantFlagButton;
        private static GameObject flagBtnHolder;

        public static void Update()
        {
            try
            {
                bool isEVA = PlayerController.main?.player?.Value is Astronaut_EVA;

                bool hasSolidSurface = true;
                if (isEVA)
                {
                    Astronaut_EVA eva = PlayerController.main.player.Value as Astronaut_EVA;
                    hasSolidSurface = PlanetSurfaceHelper.IsSolidPlanet(eva);
                }

                if (isEVA && hasSolidSurface && plantFlagButton == null)
                {
                    flagBtnHolder = ModGUIBuilder.CreateHolder(
                        ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_FlagBtn");
                    plantFlagButton = ModGUIBuilder.CreateButton(
                        flagBtnHolder.transform, 150, 50,
                        450, -250,
                        () =>
                        {
                            if (AstronautManager.main != null)
                            {
                                AstronautManager.main.PlantFlag();
                            }
                        },
                        "Plant Flag");
                }
                else if ((!isEVA || !hasSolidSurface) && plantFlagButton != null)
                {
                    if (flagBtnHolder != null)
                        UnityEngine.Object.Destroy(flagBtnHolder);
                    plantFlagButton = null;
                    flagBtnHolder = null;
                }
            }
            catch (Exception e)
            {
                
            }
        }
    }

    public class UpdateDriver : MonoBehaviour
    {
        private float timer;
        private static float crewRefreshTimer = -1f;
        private static bool pendingMenuRefresh = false;
        private static float pickGridRefreshTimer = -1f;

        public static void ScheduleCrewModuleRefresh()
        {
            crewRefreshTimer = 1.0f; // Wait 1 second for parts to fully initialize
        }

        public static void ScheduleMenuRefresh()
        {
            pendingMenuRefresh = true;
        }

        public static void SchedulePickGridRefresh()
        {
            pickGridRefreshTimer = 0.1f; // Wait 0.1s for scene to fully load
        }

        private static void DoPickGridRefresh()
        {
            try
            {
                if (BuildManager.main == null || BuildManager.main.pickGrid == null)
                {
                    pickGridRefreshTimer = 0.1f;
                    return;
                }

                var pickGrid = BuildManager.main.pickGrid;
                var catMenu = pickGrid.categoriesMenu;

                var tr = Traverse.Create(catMenu);
                var selected = tr.Field("selected").GetValue<PickGridUI.CategoryParts>();

                if (selected != null)
                {
                    catMenu.SelectCategory(selected);
                }
                else
                {
                    pickGridRefreshTimer = 0.1f;
                }
            }
            catch (Exception e)
            {
                
            }
        }

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer >= 0.5f)
            {
                timer = 0f;
                PlantFlagButtonHelper.Update();
                TeleportButtonHelper.Update();
                AstronautDashboardHelper.Update();
            }

            if (PartIconCreator.main != null)
            {
                Camera iconCam = PartIconCreator.main.GetComponent<Camera>();
                if (iconCam != null && !NativeAstronautUI.IsIconCameraDisabled(iconCam))
                {
                    NativeAstronautUI.DisableIconCamera(iconCam);
                }
            }

            if (pendingMenuRefresh)
            {
                pendingMenuRefresh = false;
                NativeAstronautUI.ShowMenu(null, null, CloseMode.None);
            }

            if (crewRefreshTimer > 0f)
            {
                crewRefreshTimer -= Time.deltaTime;
                if (crewRefreshTimer <= 0f)
                {
                    crewRefreshTimer = -1f;
                    RefreshCrewModuleVisuals();
                }
            }

            if (pickGridRefreshTimer > 0f)
            {
                pickGridRefreshTimer -= Time.deltaTime;
                if (pickGridRefreshTimer <= 0f)
                {
                    pickGridRefreshTimer = -1f;
                    DoPickGridRefresh();
                }
            }
        }

        private void LateUpdate()
        {
            if (PartIconCreator.main != null)
            {
                Camera iconCam = PartIconCreator.main.GetComponent<Camera>();
                if (iconCam != null && !NativeAstronautUI.IsIconCameraDisabled(iconCam))
                {
                    NativeAstronautUI.DisableIconCamera(iconCam);
                }
            }
        }

        private static void RefreshCrewModuleVisuals()
        {
            try
            {
                Transform pickGridHolder = null;
                try
                {
                    if (BuildManager.main != null && BuildManager.main.pickGrid != null &&
                        BuildManager.main.pickGrid.createdPartsHolder != null)
                    {
                        pickGridHolder = BuildManager.main.pickGrid.createdPartsHolder.transform;
                    }
                }
                catch { }

                CrewModule[] modules = UnityEngine.Object.FindObjectsOfType<CrewModule>(includeInactive: true);
                int refreshed = 0;
                int skipped = 0;
                foreach (CrewModule cm in modules)
                {
                    try
                    {
                        var tr = Traverse.Create(cm);

                        bool anyHasAstronaut = false;
                        if (cm.seats != null)
                        {
                            foreach (var seat in cm.seats)
                            {
                                if (seat.HasAstronaut) { anyHasAstronaut = true; break; }
                            }
                        }
                        var needsCrewRef = tr.Field("needsCrewForControl")
                            .GetValue<SFS.Variables.Bool_Reference>();
                        bool needsCrew = needsCrewRef != null && needsCrewRef.Value;
                        bool hasControl = anyHasAstronaut || !needsCrew;

                        var hasControlRef = tr.Field("hasControl")
                            .GetValue<SFS.Variables.Bool_Reference>();
                        if (hasControlRef != null)
                            hasControlRef.Value = hasControl;

                        float baseMass = tr.Field("baseMass").GetValue<float>();
                        float seatMass = 0f;
                        if (cm.seats != null)
                            foreach (var seat in cm.seats)
                                if (seat.HasAstronaut) seatMass += 0.2f;
                        SFS.Parts.Part part = tr.Field("part").GetValue<SFS.Parts.Part>();
                        if (part != null && part.mass != null)
                            part.mass.Value = baseMass + seatMass;

                        if (pickGridHolder != null && part != null &&
                            part.transform.IsChildOf(pickGridHolder))
                        {
                            skipped++;
                            continue;
                        }

                        var interior = tr.Field("interior").GetValue<GameObject>();
                        if (interior != null && !interior.activeSelf)
                        {
                            interior.SetActive(true);
                        }

                        if (part != null && part.gameObject != null && !part.gameObject.activeSelf)
                        {
                            part.gameObject.SetActive(true);
                        }

                        if (part != null && part.gameObject != null)
                        {
                            MeshRenderer[] renderers = part.GetComponentsInChildren<MeshRenderer>(true);
                            foreach (var mr in renderers)
                            {
                                if (!mr.enabled)
                                {
                                    mr.enabled = true;
                                }
                            }
                            SkinnedMeshRenderer[] skinned = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                            foreach (var smr in skinned)
                            {
                                if (!smr.enabled)
                                {
                                    smr.enabled = true;
                                }
                            }
                        }

                        refreshed++;
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                
            }
        }
    }

    public static class NativeAstronautUI
    {
        private static CrewModule.Seat pendingSeat;
        private static Action pendingRedraw;

        internal static Dictionary<string, double> savedInternalFuel = new Dictionary<string, double>();
        internal static double? pendingFuelOverride = null;

        public static void ShowMenu(CrewModule.Seat seat, Action redrawSeat)
        {
            ShowMenu(seat, redrawSeat, CloseMode.Current);
        }

        public static void ShowMenu(CrewModule.Seat seat, Action redrawSeat, CloseMode closeMode)
        {
            pendingSeat = seat;
            pendingRedraw = redrawSeat;

            if (AstronautState.main == null || AstronautState.main.state == null)
            {
                Menu.read.Open(() => "AstronautState not available");
                return;
            }

            List<WorldSave.Astronauts.Data> astronauts = AstronautState.main.state.astronauts;
            bool assignMode = seat != null;

            List<MenuElement> elements = new List<MenuElement>();
            SizeSyncerBuilder.Carrier carrier;

            elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));

            int availableCount = 0;

            if (astronauts == null || astronauts.Count == 0)
            {
                elements.Add(TextBuilder.CreateText(() =>
                    assignMode ? "No astronauts available.\nCreate one to assign to this seat."
                               : "No astronauts yet."));
            }

            if (astronauts != null)
            {
                var sorted = astronauts.ToList();
                sorted.Sort((a, b) =>
                    ((int)SafeGetAstronautState(a.astronautName))
                    .CompareTo((int)SafeGetAstronautState(b.astronautName)));

                foreach (var astro in sorted)
                {
                    string name = astro.astronautName;
                    AstronautState.State st = SafeGetAstronautState(name);
                    string statusText = AstronautState.main.GetAstronautStateText(st, assignMode);

                    if (assignMode)
                    {
                        if (st == AstronautState.State.Available && astro.alive)
                        {
                            availableCount++;
                            string capturedName = name;
                            elements.Add(ButtonBuilder.CreateButton(carrier,
                                () => capturedName + " — " + statusText,
                                () => AssignToSeat(capturedName),
                                CloseMode.Current));
                        }
                    }
                    else
                    {
                        string capturedName = name;
                        elements.Add(ButtonBuilder.CreateButton(carrier,
                            () => capturedName + " — " + statusText,
                                () => AskFire(capturedName),
                                CloseMode.None));
                    }
                }
            }

            if (assignMode && availableCount == 0)
            {
                if (astronauts != null && astronauts.Count > 0)
                {
                    elements.Add(TextBuilder.CreateText(() =>
                        "No astronauts available for assignment."));
                    elements.Add(ElementGenerator.VerticalSpace(10));
                }
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Create New Astronaut",
                    () => OpenCreateDialog(true),
                    CloseMode.Current));
            }

            elements.Add(ElementGenerator.VerticalSpace(20));

            if (!assignMode)
            {
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Create New Astronaut",
                    () => OpenCreateDialog(false),
                    CloseMode.Current));
            }

            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Close",
                () => { },
                CloseMode.Current));

            MenuGenerator.OpenMenu(CancelButton.Close, closeMode, elements.ToArray());
        }

        private static void AssignToSeat(string name)
        {
            try
            {
                if (pendingSeat != null)
                {
                    pendingSeat.Board(name, 1.0, float.NegativeInfinity);
                    pendingRedraw?.Invoke();
                }
            }
            catch (Exception e)
            {
                
            }
        }

        public static void OpenCreateDialog(bool reopenAssignMenu)
        {
            try
            {
                Menu.textInput.Open(
                    "Cancel", "Create",
                    delegate(string[] input)
                    {
                        string name = input.Length > 0 ? input[0] : "";
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            AstronautState.main.CreateAstronaut(name);
                            AstronautUnlockerMod.PersistAstronautStateToCache();
                            if (reopenAssignMenu && pendingSeat != null)
                            {
                                ShowMenu(pendingSeat, pendingRedraw);
                            }
                        }
                    },
                    CloseMode.Current,
                    TextInputMenu.Element("Astronaut name", ""));
            }
            catch (Exception e)
            {
                
            }
        }

        private static void AskFire(string name)
        {
            try
            {
                AstronautState.State state = SafeGetAstronautState(name);
                if (state == AstronautState.State.CrewBuild ||
                    state == AstronautState.State.CrewWorld ||
                    state == AstronautState.State.EVA)
                {
                    MenuGenerator.OpenConfirmation(
                        CloseMode.Stack,
                        () => "Cannot discharge " + name + " while on duty. Remove from seat/EVA first.",
                        () => "OK",
                        delegate { });
                    return;
                }

                MenuGenerator.OpenConfirmation(
                    CloseMode.Stack,
                    () => "Discharge " + name + "?",
                    () => "Discharge",
                    delegate
                    {
                        if (AstronautState.main.crew_Build != null)
                            AstronautState.main.crew_Build.RemoveAll(n => n == name);
                        if (AstronautState.main.state?.crew_World != null)
                            AstronautState.main.state.crew_World.RemoveAll(c => c.astronautName == name);
                        if (AstronautState.main.state?.eva != null)
                            AstronautState.main.state.eva.RemoveAll(e => e.astronautName == name);
                        AstronautState.main.FireAstronaut(name);

                        AstronautUnlockerMod.PersistAstronautStateToCache();

                        UpdateDriver.ScheduleMenuRefresh();
                    });
            }
            catch (Exception e)
            {
                
            }
        }

        public static AstronautState.State SafeGetAstronautState(string astronautName)
        {
            try
            {
                AstronautUnlockerMod.EnsureAllStateLists();
                return AstronautState.main.GetAstronautState(astronautName);
            }
            catch (Exception e)
            {
                
                var data = AstronautState.main?.state?.astronauts?
                    .FirstOrDefault(a => a.astronautName == astronautName);
                if (data != null && !data.alive)
                    return AstronautState.State.Deceased;
                return AstronautState.State.Available;
            }
        }

        public static void DisableIconCamera(Camera cam)
        {
            if (cam == null) return;
            cam.enabled = false;
            cam.cullingMask = 0;
            cam.clearFlags = CameraClearFlags.Nothing;
            cam.targetTexture = null;
            cam.forceIntoRenderTexture = false;
            cam.transform.position = new Vector3(0, 0, -10000f);
            cam.rect = new Rect(0f, 0f, 0f, 0f);
            cam.depth = -100f;
        }

        public static bool IsIconCameraDisabled(Camera cam)
        {
            return cam != null && !cam.enabled && cam.cullingMask == 0 &&
                   cam.clearFlags == CameraClearFlags.Nothing &&
                   cam.rect == new Rect(0f, 0f, 0f, 0f) &&
                   cam.depth == -100f;
        }
    }

    [HarmonyPatch(typeof(PartIconCreator), "Awake")]
    public class Patch_PartIconCreator_Awake
    {
        static void Postfix(PartIconCreator __instance)
        {
            try
            {
                Camera cam = __instance.GetComponent<Camera>();
                if (cam != null)
                {
                    NativeAstronautUI.DisableIconCamera(cam);
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PartIconCreator), "Start")]
    public class Patch_PartIconCreator_Start
    {
        static void Postfix(PartIconCreator __instance)
        {
            try
            {
                Camera cam = __instance.GetComponent<Camera>();
                if (cam != null && !NativeAstronautUI.IsIconCameraDisabled(cam))
                {
                    NativeAstronautUI.DisableIconCamera(cam);
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PartIconCreator), "Render")]
    public class Patch_PartIconCreator_Render
    {
        static void Prefix(PartIconCreator __instance)
        {
            try
            {
                Camera cam = __instance.GetComponent<Camera>();
                if (cam != null)
                {
                    cam.cullingMask = 1 << LayerMask.NameToLayer("Part Icon");
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.rect = new Rect(0f, 0f, 1f, 1f);
                    cam.depth = 0f;
                }
            }
            catch { }
        }

        static void Postfix(PartIconCreator __instance)
        {
            try
            {
                Camera cam = __instance.GetComponent<Camera>();
                if (cam != null)
                {
                    NativeAstronautUI.DisableIconCamera(cam);
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PartIconCreator), "Render")]
    public class Patch_PartIconCreator_Render_FuelPipeSize
    {
        static void Prefix(Part[] createdParts, ref Rect rect, ref int width, ref int height)
        {
            try
            {
                if (createdParts == null || createdParts.Length == 0) return;
                foreach (Part part in createdParts)
                {
                    if (part == null || !part.HasModule<FuelPipeModule>()) continue;

                    string partNameLower = (part.name ?? "").ToLower();

                    bool isCurved = false;
                    if (partNameLower.Contains("corner") || partNameLower.Contains("curve") ||
                        partNameLower.Contains("elbow") || partNameLower.Contains("turn") ||
                        partNameLower.Contains("bend"))
                    {
                        isCurved = true;
                    }
                    else
                    {
                        PipeData[] pipeDatas = part.GetComponentsInChildren<PipeData>(includeInactive: true);
                        if (pipeDatas != null && pipeDatas.Length > 0)
                        {
                            PipeData pd = pipeDatas[0];
                            string tn = pd.GetType().Name;
                            if (tn == "CurvePipe" || tn == "EdgePipe")
                            {
                                isCurved = true;
                            }
                            else if (pd.pipe != null && pd.pipe.points != null && pd.pipe.points.Count >= 3)
                            {
                                int pc = pd.pipe.points.Count;
                                Vector2 first = pd.pipe.points[0].position;
                                Vector2 last = pd.pipe.points[pc - 1].position;
                                Vector2 dir = last - first;
                                float dirSqrMag = dir.sqrMagnitude;
                                if (dirSqrMag > 0.0001f)
                                {
                                    for (int j = 1; j < pc - 1; j++)
                                    {
                                        Vector2 mid = pd.pipe.points[j].position;
                                        Vector2 toMid = mid - first;
                                        float proj = Vector2.Dot(toMid, dir) / dirSqrMag;
                                        Vector2 perp = toMid - dir * proj;
                                        if (perp.sqrMagnitude / dirSqrMag > 0.01f)
                                        {
                                            isCurved = true;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    isCurved = true;
                                }
                            }
                        }
                    }

                    if (isCurved)
                    {
                        float shrink = 0.6f;
                        Vector2 center = rect.center;
                        Vector2 newSize = rect.size * shrink;
                        rect = new Rect(center - newSize / 2f, newSize);
                    }
                    else
                    {
                        float shrink = 0.5f;
                        Vector2 center = rect.center;
                        Vector2 newSize = rect.size * shrink;
                        rect = new Rect(center - newSize / 2f, newSize);
                        width *= 2;
                        height *= 2;
                    }
                    break;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(EngineModule), "Start")]
    public class Patch_EngineModule_Start
    {
        private static HashSet<string> loggedEngineErrors = new HashSet<string>();

        static Exception Finalizer(Exception __exception, EngineModule __instance)
        {
            if (__exception != null)
                {
                    if (!loggedEngineErrors.Contains(__instance.name))
                    {
                        loggedEngineErrors.Add(__instance.name);
                    }
                    return null;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(DetachModule), "Detach")]
    public class Patch_DetachModule_Detach_Diag
    {
        static void Prefix(DetachModule __instance, UsePartData data)
        {
            try
            {
                bool cannotDetach = __instance.cannotDetachIfSurfaceCovered;
                bool hasSepSurface = __instance.separationSurface != null;
                int sepSurfaceCount = hasSepSurface && __instance.separationSurface.surfaces != null
                    ? __instance.separationSurface.surfaces.Count : 0;

                Rocket rocket = (Rocket)typeof(DetachModule)
                    .GetProperty("Rocket", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(__instance);

                bool surfaceCovered = false;
                if (cannotDetach && __instance.surfaceForCover != null)
                {
                    surfaceCovered = SurfaceData.IsSurfaceCovered(__instance.surfaceForCover);
                }

                int connectedJoints = 0;
                if (rocket != null && rocket.jointsGroup != null)
                {
                    Part part = __instance.transform.GetComponentInParentTree<Part>();
                    if (part != null)
                    {
                        connectedJoints = rocket.jointsGroup.GetConnectedJoints(part).Count;
                    }
                }
    }
            catch (Exception e)
            {
                
            }
        }
    }

    public class Water_Astronaut : MonoBehaviour
    {
        public bool isInWater;
    }

    [HarmonyPatch(typeof(AstronautManager), "SpawnEVA")]
    public class Patch_AstronautManager_SpawnEVA_Buoyancy
    {
        static void Postfix(Astronaut_EVA __result)
        {
            try
            {
                if (__result != null && __result.GetComponent<Water_Astronaut>() == null)
                {
                    __result.gameObject.AddComponent<Water_Astronaut>();
                }
            }
            catch (Exception e)
            {
                
            }
        }
    }

    [HarmonyPatch(typeof(Astronaut_EVA), "OnFixedUpdate")]
    public class Patch_Astronaut_EVA_OnFixedUpdate_Buoyancy
    {
        static void Postfix(Astronaut_EVA __instance, Vector2 gravity)
        {
            try
            {
                if (!AstronautUnlockerMod.buoyancyPostfixLogged)
                {
                    AstronautUnlockerMod.buoyancyPostfixLogged = true;
                }

                Water_Astronaut water = __instance.GetComponent<Water_Astronaut>();
                if (water == null) return;

                WorldLocation wl = __instance.location;
                if (wl == null) return;

                Planet planet = wl.planet.Value;
                if (planet == null || planet.data == null || !planet.data.hasWater)
                {
                    water.isInWater = false;
                    return;
                }

                Double2 position = wl.position.Value;
                double altitude = position.magnitude - planet.Radius;

                if (altitude > 0.5) { water.isInWater = false; return; }

                float astroRadius = 0.3f;
                double waterDepth = -altitude;
                float submergedRatio = Mathf.Clamp01((float)(waterDepth / (astroRadius * 2.0)) + 0.5f);
                water.isInWater = submergedRatio > 0f;

                if (submergedRatio <= 0f) return;

                Rigidbody2D rb = __instance.rb2d;
                if (rb == null) return;

                float gravityMag = (float)gravity.magnitude;
                float dt = Time.fixedDeltaTime;

                Double2 globalVel = WorldView.ToGlobalVelocity(rb.linearVelocity);

                Double2 upDir = position.normalized;

                float buoyancyAccel = submergedRatio * gravityMag * 5.0f;
                globalVel += upDir * buoyancyAccel;

                if (submergedRatio > 0.3f)
                {
                    globalVel -= gravity * submergedRatio;
                }

                double speed = globalVel.magnitude;
                if (speed > 0.01)
                {
                    double dragMag = Mathf.Pow((float)speed, 1.2f) * 2.0f * astroRadius * submergedRatio * dt;
                    globalVel -= globalVel.normalized * dragMag;
                }

                rb.linearVelocity = WorldView.ToLocalVelocity(globalVel);

                rb.angularVelocity *= Mathf.Pow(0.3f, dt * 2f);
            }
            catch (Exception e)
            {
                
            }
        }
    }

    [HarmonyPatch(typeof(Astronaut_EVA), "CanTimewarp")]
    public class Patch_Astronaut_EVA_CanTimewarp_Buoyancy
    {
        static void Postfix(Astronaut_EVA __instance, ref bool __result, ref bool isInWater)
        {
            try
            {
                Water_Astronaut water = __instance.GetComponent<Water_Astronaut>();
                if (water != null && water.isInWater)
                {
                    isInWater = true;
                    __result = false;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(VariantRef), "GetPickTags")]
    public class Patch_VariantRef_GetPickTags_FuelPipe
    {
        static void Postfix(VariantRef __instance, ref List<Variants.PickTag> __result)
        {
            try
            {
                if (__instance?.part == null || __result == null) return;
                if (!__instance.part.HasModule<FuelPipeModule>()) return;
                if (__result.Count > 0) return; // Already has tags

                PickCategory[] categories = UnityEngine.Resources.FindObjectsOfTypeAll<PickCategory>();
                if (categories.Length == 0)
                {
                    return;
                }

                foreach (var cat in categories)
                {
                    string name = "";
                    try
                    {
                        if (cat.displayName != null && cat.displayName.Field != null)
                            name = cat.displayName.Field.ToString();
                    }
                    catch { }

                    if (name.Contains("Fuel") || name.Contains("Tank") ||
                        name.Contains("fuel") || name.Contains("tank"))
                    {
                        __result.Add(new Variants.PickTag { tag = cat, priority = 50 });
                        return;
                    }
                }

                __result.Add(new Variants.PickTag { tag = categories[0], priority = 50 });
            }
            catch (Exception e)
            {
                
            }
        }
    }

    [HarmonyPatch(typeof(FuelPipeModule), "FindNeighbours")]
    public class Patch_FuelPipeModule_FindNeighbours
    {
        static bool Prefix(FuelPipeModule __instance)
        {
            try
            {
                if (__instance.surface_In == null || __instance.surface_Out == null)
                {
                    
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(DetachModule), "Detach")]
    public class Patch_DetachModule_Detach
    {
        static bool Prefix(DetachModule __instance, UsePartData data)
        {
            try
            {
                if (__instance.separationSurface == null)
                {
                    
                    return false;
                }
                if (__instance.separationSurface.surfaces == null || __instance.separationSurface.surfaces.Count == 0)
                {
                    
                    return false;
                }
                var rocketProp = typeof(DetachModule).GetProperty("Rocket",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                object rocket = rocketProp?.GetValue(__instance);
                if (rocket == null)
                {
                    
                    return false;
                }
            }
            catch (Exception e)
            {
                
            }
            return true;
        }
    }

    public static class VariableListPatches
    {
        public static bool RegisterOnVariableChange_Prefix(object __instance, string variableName)
        {
            try
            {
                MethodInfo getVar = __instance.GetType().GetMethod("GetVariable",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getVar == null) return true; // Can't check, let original run

                object variable = getVar.Invoke(__instance, new object[] { variableName });
                if (variable == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static Exception Composed_Float_GetResult_Finalizer(Exception __exception, ref float __result)
        {
            if (__exception != null)
            {
                __result = 0f;
                return null;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(TeleportMenu), "ConfirmTeleport")]
    public class Patch_TeleportMenu_ConfirmTeleport
    {
        static bool Prefix(TeleportMenu __instance)
        {
            try
            {
                Player value = PlayerController.main.player.Value;
                if (value is Astronaut_EVA eva)
                {
                    var tr = Traverse.Create(__instance);
                    Planet selectedPlanet = tr.Field("selectedPlanet").GetValue<Planet>();
                    int mode = tr.Field("mode").GetValue<int>();
                    float longitude = tr.Field("longitude").GetValue<float>();
                    float height = tr.Field("height").GetValue<float>();
                    bool prograde = tr.Field("prograde").GetValue<bool>();

                    if (selectedPlanet == null)
                    {
                        return false;
                    }

                    longitude = Mathf.Clamp((longitude + 360f) % 360f, 0f, 360f);

                    Location targetLocation;
                    bool rotate;

                    if (mode == 0) // Surface
                    {
                        double angleRad = (double)((0f - longitude + 90f) * (Mathf.PI / 180f));
                        double terrainHeight = selectedPlanet.GetTerrainHeightAtAngle(angleRad, clampToWater: true);
                        double radius = selectedPlanet.Radius + terrainHeight + 1.0 + (double)height;
                        targetLocation = new Location(
                            WorldTime.main.worldTime,
                            selectedPlanet,
                            new Double2(Math.Cos(angleRad) * radius, Math.Sin(angleRad) * radius),
                            Double2.zero);
                        rotate = true;
                    }
                    else // Orbit
                    {
                        double orbitRadius = selectedPlanet.Radius + (double)(height * 1000f);
                        double orbitalVel = Math.Sqrt(selectedPlanet.mass / orbitRadius) + 0.0001;
                        Double2 pos = new Double2(orbitRadius, 0.0);
                        Double2 vel = new Double2(0.0, 0.0 - orbitalVel);
                        float angleOffset = (0f - longitude + 90f) * (Mathf.PI / 180f);
                        pos = pos.Rotate(angleOffset);
                        vel = vel.Rotate(angleOffset);
                        if (!prograde) vel *= -1.0;
                        targetLocation = new Location(WorldTime.main.worldTime, selectedPlanet, pos, vel);
                        rotate = false;
                    }

                    PlayerController.main.player.Value = null;
                    eva.physics.PhysicsMode = false;
                    eva.physics.SetLocationAndState(targetLocation, physicsMode: false);
                    eva.physics.PhysicsMode = true;
                    PlayerController.main.player.Value = eva;

                    Map.view.SetViewSmooth(new MapView.View(
                        targetLocation.planet.mapPlanet,
                        targetLocation.position,
                        (double)Map.view.view.distance * 0.8));

                    eva.physics.SetLocationAndState(targetLocation, physicsMode: true);

                    if (rotate)
                    {
                        float targetAngle = Astronaut_EVA.GetTargetAngle(targetLocation);
                        eva.rb2d.rotation = targetAngle;
                        eva.rb2d.transform.rotation = Quaternion.Euler(0f, 0f, targetAngle);
                        eva.rb2d.angularVelocity = 0f;
                    }

                    ScreenManager.main.CloseStack();
                    return false; // Skip original Rocket-only logic
                }
                return true; // Not an astronaut, let original run
            }
            catch (Exception e)
            {
                
                return true; // Fall back to original on error
            }
        }
    }

    public static class TeleportButtonHelper
    {
        private static ModGUIButton teleportButton;
        private static GameObject teleportBtnHolder;

        public static void Update()
        {
            try
            {
                bool isEVA = PlayerController.main?.player?.Value is Astronaut_EVA;

                bool cheatsAllowed = false;
                try
                {
                    cheatsAllowed = Base.worldBase != null && Base.worldBase.AllowsCheats;
                }
                catch { }

                if (isEVA && cheatsAllowed && teleportButton == null)
                {
                    teleportBtnHolder = ModGUIBuilder.CreateHolder(
                        ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_TeleportBtn");
                    teleportButton = ModGUIBuilder.CreateButton(
                        teleportBtnHolder.transform, 150, 50,
                        450, -200,
                        () =>
                        {
                            try
                            {
                                if (TeleportMenu.main != null)
                                {
                                    TeleportMenu.main.OpenFromCheats();
                                }
                                else
                                {
                                    
                                }
                            }
                            catch (Exception e)
                            {
                                
                            }
                        },
                        "Teleport");
                }
                else if ((!isEVA || !cheatsAllowed) && teleportButton != null)
                {
                    if (teleportBtnHolder != null)
                        UnityEngine.Object.Destroy(teleportBtnHolder);
                    teleportButton = null;
                    teleportBtnHolder = null;
                }
            }
            catch (Exception e)
            {
                
            }
        }
    }

    public static class AstronautDashboardHelper
    {
        private static SFS.UI.ModGUI.Label dashboardLabel;
        private static GameObject dashboardHolder;
        private static float updateTimer;

        public static void Update()
        {
            try
            {
                bool isEVA = PlayerController.main?.player?.Value is Astronaut_EVA;

                if (isEVA && dashboardLabel == null)
                {
                    dashboardHolder = ModGUIBuilder.CreateHolder(
                        ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_Dashboard");
                    dashboardLabel = ModGUIBuilder.CreateLabel(
                        dashboardHolder.transform, 280, 80,
                        -450, 300,
                        "");
                    dashboardLabel.Color = new Color(1f, 1f, 1f, 0.9f);
                    dashboardLabel.FontSize = 14;
                }
                else if (!isEVA && dashboardLabel != null)
                {
                    if (dashboardHolder != null)
                        UnityEngine.Object.Destroy(dashboardHolder);
                    dashboardLabel = null;
                    dashboardHolder = null;
                }

                if (isEVA && dashboardLabel != null &&
                    PlayerController.main?.player?.Value is Astronaut_EVA eva)
                {
                    updateTimer += Time.deltaTime;
                    if (updateTimer >= 0.01f) // Update 100x per second (10ms)
                    {
                        updateTimer = 0f;
                        UpdateTelemetry(eva, dashboardLabel);
                    }
                }
            }
            catch (Exception e)
            {
                
            }
        }

        private static void UpdateTelemetry(Astronaut_EVA eva, SFS.UI.ModGUI.Label label)
        {
            try
            {
                Double2 globalVel = WorldView.ToGlobalVelocity(eva.rb2d.linearVelocity);
                double speed = globalVel.magnitude;

                double altitude = 0.0;
                if (eva.location != null && eva.location.planet.Value != null)
                {
                    altitude = eva.location.position.Value.magnitude - eva.location.planet.Value.Radius;
                }

                double fuel = eva.resources?.fuelPercent?.Value ?? 0.0;

                string altStr = altitude >= 1000.0
                    ? (altitude / 1000.0).ToString("F2") + " km"
                    : altitude.ToString("F1") + " m";

                label.Text = $"Speed: {speed:F1} m/s\n" +
                             $"Altitude: {altStr}\n" +
                             $"Fuel: {fuel * 100:F0}%";
            }
            catch (Exception e)
            {
                
            }
        }
    }

    // ===== EVA 注入 Harmony 补丁 =====

    // 在模组控制部件的右键菜单添加"启用 EVA"开关
    [HarmonyPatch(typeof(Part), "DrawPartStats")]
    public class Patch_Part_DrawPartStats_EVA
    {
        static void Postfix(Part __instance, Part[] allParts, StatsMenu drawer, PartDrawSettings settings)
        {
            try
            {
                // 仅在建造/世界模式显示（非部件选择界面）
                if (!settings.build && !settings.game) return;

                // 需为控制部件
                if (!__instance.HasModule<ControlModule>()) return;

                // 跳过自带原生 CrewModule 的部件
                if (AstronautUnlockerMod.HasNativeCrewModule(__instance)) return;

                string partName = __instance.name;
                Part capturedPart = __instance;

                // 在菜单底部绘制 EVA 开关（priority -500 = 最底部）
                drawer.DrawToggle(-500,
                    () => "Enable EVA",
                    () =>
                    {
                        try
                        {
                            bool currentEnabled = AstronautUnlockerMod.evaConfig.ContainsKey(partName) &&
                                                   AstronautUnlockerMod.evaConfig[partName];
                            bool newEnabled = !currentEnabled;
                            AstronautUnlockerMod.evaConfig[partName] = newEnabled;
                            AstronautUnlockerMod.SaveEvaConfig();

                            if (newEnabled)
                            {
                                AstronautUnlockerMod.InjectCrewModule(capturedPart);
                            }
                            else
                            {
                                AstronautUnlockerMod.RemoveCrewModule(capturedPart);
                            }

                            // 清模块缓存使 HasModule<CrewModule> 返回正确结果
                            AstronautUnlockerMod.ClearModuleCache(capturedPart);

                            // 不关闭/重开菜单，避免菜单箭头因坐标问题"瞬移"。
                            // getValue 回调会在下次重绘时自动反映新状态。
                        }
                        catch (Exception e)
                        {
                            
                        }
                    },
                    () => AstronautUnlockerMod.evaConfig.ContainsKey(partName) &&
                           AstronautUnlockerMod.evaConfig[partName],
                    null, null);
            }
            catch (Exception e)
            {
                
            }
        }
    }

    // 启用 EVA 的部件初始化时自动注入 CrewModule
    [HarmonyPatch(typeof(Part), "InitializePart")]
    public class Patch_Part_InitializePart_EVA
    {
        static void Postfix(Part __instance)
        {
            try
            {
                // 已有 CrewModule 或非控制部件则跳过
                if (__instance.HasModule<CrewModule>()) return;
                if (!__instance.HasModule<ControlModule>()) return;

                string partName = __instance.name;

                // 该部件启用了 EVA 才注入
                if (AstronautUnlockerMod.evaConfig.ContainsKey(partName) &&
                    AstronautUnlockerMod.evaConfig[partName])
                {
                    AstronautUnlockerMod.InjectCrewModule(__instance);
                    AstronautUnlockerMod.ClearModuleCache(__instance);
                }
            }
            catch (Exception e)
            {
                
            }
        }
    }

    // 加宽大型模组舱体的 EVA 登舱距离（20 → 50）
    [HarmonyPatch(typeof(CrewModule), "EVA_Board")]
    public class Patch_EVA_Board_Distance
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            foreach (var c in codes)
            {
                if (c.opcode == OpCodes.Ldc_R4 && (float)c.operand == 400f)
                    c.operand = 2500f;
                yield return c;
            }
        }
    }

    // 发射前把注入部件座椅上的乘员名存入 savedAstronauts。
    // 注入的 CrewModule 不在部件 JSON 中，PartSave.CreateSaves() 不会序列化座椅乘员，
    // 需手动保存并在世界场景重注入时恢复。
    [HarmonyPatch(typeof(BuildManager), "Launch")]
    public class Patch_BuildManager_Launch_SaveAstronauts
    {
        static void Prefix()
        {
            try
            {
                if (BuildManager.main == null) return;

                // 获取建造网格中的所有部件
                PartHolder partsHolder = BuildManager.main.buildGrid.activeGrid.partsHolder;
                if (partsHolder == null || partsHolder.parts == null) return;

                foreach (Part part in partsHolder.parts)
                {
                    int partId = part.GetInstanceID();
                    if (!AstronautUnlockerMod.injectedPartIds.Contains(partId)) continue;

                    // 注入部件——保存其乘员名
                    CrewModule crew = part.GetComponentInChildren<CrewModule>(true);
                    if (crew == null || crew.seats == null) continue;

                    var savedList = new List<string>();
                    foreach (var seat in crew.seats)
                    {
                        if (seat.HasAstronaut)
                        {
                            savedList.Add(seat.astronaut.Value);
                        }
                    }

                    if (savedList.Count > 0)
                    {
                        AstronautUnlockerMod.savedAstronauts[part.name] = savedList;
                        
                    }
                }

                // 持久化到 JSON，场景重载后仍保留
                AstronautUnlockerMod.SaveEvaConfig();
            }
            catch (Exception e)
            {
                
            }
        }
    }
}

