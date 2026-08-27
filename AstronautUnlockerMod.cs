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
        public override string ModVersion => "3.9";
        public override string Description => "Enables the native astronaut/crew system on PC.";

        public override void Early_Load()
        {
            HarmonyInstance = new Harmony("com.sfs.astronautunlocker");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            PatchVariableLists();
            ModifyDisableParts();
            CreatePersistentAstronautState();
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
                if (AstronautState.main == null || SavingCache.main == null) return;

                WorldSave save = SavingCache.main.LoadWorldPersistent(
                    MsgDrawer.main, needsRocketsAndBranches: false, eraseCache: false);

                if (save?.astronauts != null)
                    AstronautState.main.state = save.astronauts;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Astronaut cache load", e);
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
                ModLogger.ErrorOnce("Astronaut cache save", e);
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
        public static bool allowUncrewedControl = false;

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

        public static void LoadModConfig()
        {
            try
            {
                string path = ModConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "# AstronautMod configuration\n" +
                        "# true: empty crew modules keep control\n" +
                        "# false: an astronaut is required for control\n" +
                        "allowUncrewedControl=false\n");
                    return;
                }

                string[] configLines = File.ReadAllLines(path);
                foreach (string line in configLines)
                {
                    string value = (line ?? "").Trim();
                    if (value.Length == 0 || value.StartsWith("#")) continue;
                    int separator = value.IndexOf('=');
                    if (separator < 0) continue;
                    string key = value.Substring(0, separator).Trim();
                    string setting = value.Substring(separator + 1).Trim();
                    if (string.Equals(key, "allowUncrewedControl", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(setting, out bool parsed))
                            allowUncrewedControl = parsed;
                        else if (setting == "1")
                            allowUncrewedControl = true;
                        else if (setting == "0")
                            allowUncrewedControl = false;
                    }
                }

                string[] cleanedLines = configLines.Where(line =>
                {
                    string value = (line ?? "").Trim();
                    return !value.StartsWith("# EVA parachute settings", StringComparison.OrdinalIgnoreCase) &&
                        !value.StartsWith("enableAstronautParachute=", StringComparison.OrdinalIgnoreCase) &&
                        !value.StartsWith("astronautParachuteDeployAltitude=", StringComparison.OrdinalIgnoreCase) &&
                        !value.StartsWith("astronautParachuteTerminalSpeed=", StringComparison.OrdinalIgnoreCase);
                }).ToArray();
                if (cleanedLines.Length != configLines.Length)
                    File.WriteAllLines(path, cleanedLines);
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
                            if (!evaConfig.ContainsKey(partName)) evaConfig[partName] = false;
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
                astronaut = new String_Reference(),
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

                int crewCapacity = GetCrewCapacity(part.name);
                CrewModule.Seat[] seats = new CrewModule.Seat[crewCapacity];
                Vector2 hatchPos = CalcHatchPosition(part);
                for (int index = 0; index < seats.Length; index++)
                {
                    CrewModule.Seat seat = new CrewModule.Seat();
                    var seatTr = Traverse.Create(seat);
                    seatTr.Field("hatchPosition").SetValue(hatchPos);
                    seatTr.Field("externalSeat").SetValue(false);
                    seatTr.Field("astronaut").SetValue(new String_Reference());
                    seatTr.Field("astronautModel").SetValue(null);
                    seatTr.Field("resources").SetValue(null);
                    seats[index] = seat;
                }
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

                // 恢复暂存乘员
                string partName = part.name;
                if (savedAstronauts.ContainsKey(partName) && savedAstronauts[partName].Count > 0)
                {
                    var names = new List<string>(savedAstronauts[partName]);
                    bool isRevert = Patch_GameManager_LoadSave.isRevertLoad;
                    var baseline = Patch_GameManager_LoadPersistentAndLaunch.launchDeadBaseline;

                    int seatIndex = 0;
                    foreach (string name in names)
                    {
                        if (seatIndex >= seats.Length) break;
                        // 本次加载计划恢复为 EVA 的人员不能同时恢复进座位
                        if (Patch_GameManager_LoadSave.IsPendingEVA(name)) continue;
                        try
                        {
                            CrewModule.Seat seat = seats[seatIndex];
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
                            seatIndex++;
                        }
                        catch (Exception be)
                        {
                            
                        }
                    }

                    // 保留 savedAstronauts 供后续回退恢复 离开世界时再清空
                }

                
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
                    UnityEngine.Object.DestroyImmediate(crew);
                }
                injectedPartIds.Remove(partId);
                ClearModuleCache(part);
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
