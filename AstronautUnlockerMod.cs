using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ModLoader;
using ModLoader.Helpers;
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
        public override string ModVersion => "3.7";
        public override string Description => "Enables the native astronaut/crew system on PC.";

        public override void Early_Load()
        {
            HarmonyInstance = new Harmony("com.sfs.astronautunlocker");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            PatchVariableLists();
            ModifyDisableParts();
            CreatePersistentAstronautState();
            Debug.Log("[AstronautMod] v3.28 loaded");
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
                    Debug.Log("[AstronautUnlocker] VariableList<> type not found, skipping patch");
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
                            Debug.Log("[AstronautUnlocker] Patched VariableList<" + T.Name + ">.RegisterOnVariableChange");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log("[AstronautUnlocker] Failed to patch VariableList<" + T.Name + ">: " + e.Message);
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
                        Debug.Log("[AstronautUnlocker] Patched Composed_Float.GetResult Finalizer");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] PatchVariableLists error: " + e.Message);
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
            Debug.Log("[AstronautUnlocker] Scene hooks registered");
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
                Debug.Log("[AstronautUnlocker] Hub init error: " + e);
            }
        }

        private static void OnBuildSceneLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            try
            {
                EnsureAstronautState();
                EnsureAstronautMenuInstance();
                EnsureAllStateLists();
                if (AstronautState.main.crew_Build == null)
                    AstronautState.main.crew_Build = new List<string>();
                else
                    AstronautState.main.crew_Build.Clear();
                LoadAstronautDataFromCache();
                EnsureAllStateLists();
                Debug.Log("[AstronautUnlocker] Build scene ready, astronauts: " +
                    (AstronautState.main.state?.astronauts?.Count ?? 0));
                UpdateDriver.ScheduleCrewModuleRefresh();
                UpdateDriver.SchedulePickGridRefresh();
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Build init error: " + e);
            }
        }

        private static void OnWorldSceneLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            try
            {
                EnsureAstronautState();
                EnsureCrewBuildList();

                Patch_Rocket_UseParts.ClearPatchedParts();
                Debug.Log("[AstronautUnlocker] World scene loaded — cleared patchedParts cache for fresh separator listeners");

                if (AstronautManager.main == null)
                {
                    Debug.Log("[AstronautUnlocker] WARNING: AstronautManager.main is null in World scene! EVA will not work.");
                    GameObject go = new GameObject("__AstronautManagerFallback");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    AstronautManager mgr = go.AddComponent<AstronautManager>();
                    Debug.Log("[AstronautUnlocker] Created fallback AstronautManager (no prefabs — EVA limited)");
                }
                else
                {
                    Debug.Log("[AstronautUnlocker] AstronautManager.main exists. " +
                        "astronautPrefab: " + (AstronautManager.main.astronautPrefab != null ? "OK" : "NULL") +
                        ", flagPrefab: " + (AstronautManager.main.flagPrefab != null ? "OK" : "NULL") +
                        ", fadeToBlack: " + (AstronautManager.main.fadeToBlack != null ? "OK" : "NULL"));

                    if (AstronautManager.main.astronautPrefab == null)
                    {
                        Debug.Log("[AstronautUnlocker] WARNING: astronautPrefab is NULL! " +
                            "EVA SpawnEVA will fail. Attempting to find prefab in resources...");

                        Astronaut_EVA[] allEVA = UnityEngine.Object.FindObjectsOfType<Astronaut_EVA>(includeInactive: true);
                        if (allEVA != null && allEVA.Length > 0)
                        {
                            Debug.Log("[AstronautUnlocker] Found " + allEVA.Length + " Astronaut_EVA instances in scene");
                        }

                        GameObject prefabCandidate = UnityEngine.Resources.Load<GameObject>("Astronaut_EVA");
                        if (prefabCandidate != null)
                        {
                            Astronaut_EVA evaComp = prefabCandidate.GetComponent<Astronaut_EVA>();
                            if (evaComp != null)
                            {
                                typeof(AstronautManager).GetField("astronautPrefab",
                                    BindingFlags.Public | BindingFlags.Instance)
                                    .SetValue(AstronautManager.main, evaComp);
                                Debug.Log("[AstronautUnlocker] astronautPrefab loaded from Resources");
                            }
                        }
                    }
                }

                EnsureRockSelector();
                EnsureFlagPrefab();

                Debug.Log("[AstronautUnlocker] World scene ready. Astronauts in state: " +
                    (AstronautState.main.state?.astronauts?.Count ?? 0) +
                    ", RockSelector: " + (RockSelector.main != null ? "OK" : "NULL") +
                    ", flagPrefab: " + (AstronautManager.main?.flagPrefab != null ? "OK" : "NULL"));
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] World init error: " + e);
            }
        }

        private static void EnsureRockSelector()
        {
            try
            {
                if (RockSelector.main != null)
                {
                    Debug.Log("[AstronautUnlocker] RockSelector.main exists, rockInstances: " +
                        RockSelector.main.rockInstances.Count);
                    return;
                }
                GameObject go = new GameObject("__RockSelectorFallback");
                UnityEngine.Object.DontDestroyOnLoad(go);
                RockSelector rs = go.AddComponent<RockSelector>();
                Debug.Log("[AstronautUnlocker] Created fallback RockSelector");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] EnsureRockSelector error: " + e);
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
                        Debug.Log("[AstronautUnlocker] flagPrefab loaded from Resources");
                        return;
                    }
                }

                Flag[] existingFlags = UnityEngine.Object.FindObjectsOfType<Flag>(includeInactive: true);
                if (existingFlags != null && existingFlags.Length > 0)
                {
                    Debug.Log("[AstronautUnlocker] Found " + existingFlags.Length +
                        " Flag instances in scene, using first as prefab reference");
                    typeof(AstronautManager).GetField("flagPrefab",
                        BindingFlags.Public | BindingFlags.Instance)
                        .SetValue(AstronautManager.main, existingFlags[0]);
                    return;
                }

                Debug.Log("[AstronautUnlocker] flagPrefab is NULL and no resources found. " +
                    "Will use code-generated flag fallback.");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] EnsureFlagPrefab error: " + e);
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
                        Debug.Log("[AstronautUnlocker] Un-disabling part: " + parts[i]);
                        parts[i] = "";
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] DisableParts error: " + e);
            }
        }

        private static void EnsureAstronautMenuInstance()
        {
            if (AstronautMenu.main != null) return;
            GameObject go = new GameObject("__AstronautMenuHolder");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AstronautMenu>();
            Debug.Log("[AstronautUnlocker] AstronautMenu instance created");
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
                Debug.Log("[AstronautUnlocker] AstronautState safety instance created");
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
                Debug.Log("[AstronautUnlocker] Adopted existing AstronautState as persistent");
                return;
            }
            GameObject go = new GameObject("__PersistentAstronautState");
            UnityEngine.Object.DontDestroyOnLoad(go);
            persistentState = go.AddComponent<AstronautState>();
            if (persistentState.state == null)
                persistentState.state = new WorldSave.Astronauts();
            if (persistentState.crew_Build == null)
                persistentState.crew_Build = new List<string>();
            Debug.Log("[AstronautUnlocker] Persistent AstronautState created (DontDestroyOnLoad)");
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
                    Debug.Log("[AstronautUnlocker] Loaded " +
                        (save.astronauts.astronauts?.Count ?? 0) +
                        " astronauts from SavingCache");
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Load from cache error: " + e);
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
                                            Debug.Log("[AstronautUnlocker] TranslationSelector removed from clone");
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
                                                    Debug.Log("[AstronautUnlocker] TextAdapter isInit reset to false");
                                                }
                                                comp.GetType().GetProperty("Text")?.SetValue(comp, "Astronauts");
                                                Debug.Log("[AstronautUnlocker] TextAdapter text set to 'Astronauts'");
                                            }
                                            catch (Exception te)
                                            {
                                                Debug.Log("[AstronautUnlocker] TextAdapter set text error: " + te.Message);
                                            }
                                        }
                                    }
                                    Text textComp = clonedAstronautBtn.GetComponentInChildren<Text>(true);
                                    if (textComp != null)
                                    {
                                        textComp.text = "Astronauts";
                                        Debug.Log("[AstronautUnlocker] Text component set to 'Astronauts'");
                                    }
                                    var tmpTexts = clonedAstronautBtn.GetComponentsInChildren<TMPro.TMP_Text>(true);
                                    foreach (var tmp in tmpTexts)
                                    {
                                        tmp.text = "Astronauts";
                                        Debug.Log("[AstronautUnlocker] TMP_Text set to 'Astronauts'");
                                    }
                                    if (textComp == null && tmpTexts.Length == 0)
                                        Debug.Log("[AstronautUnlocker] WARNING: No text component found on cloned button!");

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
                                            Debug.Log("[AstronautUnlocker] clickEvent replaced and callback added");
                                        }

                                        FieldInfo onClickField = typeof(SFS.UI.Button).GetField("onClick",
                                            BindingFlags.Public | BindingFlags.Instance);
                                        if (onClickField != null)
                                        {
                                            object newOnClick = System.Activator.CreateInstance(onClickField.FieldType);
                                            onClickField.SetValue(sfsBtn, newOnClick);
                                        }

                                        sfsBtn.SetEnabled(true);
                                        Debug.Log("[AstronautUnlocker] Cloned resumeGameButton as Astronauts button at " +
                                            astroRT.anchoredPosition);
                                    }

                                    return;
                                }
                            }
                            else
                            {
                                Debug.Log("[AstronautUnlocker] resumeGameButton gameObject is null or inactive");
                            }
                        }
                        else
                        {
                            Debug.Log("[AstronautUnlocker] resumeGameButton is null, using ModGUI fallback");
                        }
                    }
                }

                hubHolder = ModGUIBuilder.CreateHolder(ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_HubBtn");
                hubAstronautButton = ModGUIBuilder.CreateButton(hubHolder.transform, 200, 50,
                    0, 80,
                    () => NativeAstronautUI.ShowMenu(null, null),
                    "Astronauts");
                Debug.Log("[AstronautUnlocker] ModGUI fallback button created in Hub at (0, 80)");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Button activate error: " + e);

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
                    Debug.Log("[AstronautUnlocker] ModGUI emergency fallback button created");
                }
                catch { }
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

    [HarmonyPatch(typeof(AstronautState), "Start")]
    public class Patch_AstronautState_Start
    {
        static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(GameManager), "LoadSave")]
    public class Patch_GameManager_LoadSave
    {
        private static List<WorldSave.Astronauts.Data> backupAstronauts;
        private static List<string> backupCrewBuild;

        static void Prefix(WorldSave save)
        {
            try
            {
                if (AstronautState.main?.state?.astronauts != null &&
                    AstronautState.main.state.astronauts.Count > 0)
                {
                    backupAstronauts = new List<WorldSave.Astronauts.Data>(
                        AstronautState.main.state.astronauts);
                    Debug.Log("[AstronautUnlocker] Backed up " + backupAstronauts.Count +
                        " astronauts before LoadSave");
                }

                if (AstronautState.main?.crew_Build != null && AstronautState.main.crew_Build.Count > 0)
                {
                    backupCrewBuild = new List<string>(AstronautState.main.crew_Build);
                    Debug.Log("[AstronautUnlocker] Backed up crew_Build: " +
                        string.Join(", ", backupCrewBuild));
                }
                else
                {
                    backupCrewBuild = null;
                }

                if (backupAstronauts != null && backupAstronauts.Count > 0 &&
                    save?.astronauts?.astronauts != null)
                {
                    int before = save.astronauts.astronauts.Count;
                    foreach (var astro in backupAstronauts)
                    {
                        bool exists = false;
                        foreach (var existing in save.astronauts.astronauts)
                        {
                            if (existing.astronautName == astro.astronautName)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            save.astronauts.astronauts.Add(astro);
                        }
                    }
                    int added = save.astronauts.astronauts.Count - before;
                    if (added > 0)
                    {
                        Debug.Log("[AstronautUnlocker] Injected " + added +
                            " astronauts into save data (save now has " +
                            save.astronauts.astronauts.Count + ")");
                    }
                }

                if (backupCrewBuild != null && backupCrewBuild.Count > 0 &&
                    save?.astronauts != null)
                {
                    if (save.astronauts.crew_World == null)
                        save.astronauts.crew_World = new List<WorldSave.Astronauts.Crew_World>();
                    if (save.astronauts.eva == null)
                        save.astronauts.eva = new List<WorldSave.Astronauts.EVA>();

                    int evaRemoved = 0;
                    save.astronauts.eva.RemoveAll(e =>
                    {
                        if (backupCrewBuild.Contains(e.astronautName))
                        {
                            evaRemoved++;
                            return true;
                        }
                        return false;
                    });
                    if (evaRemoved > 0)
                    {
                        Debug.Log("[AstronautUnlocker] Removed " + evaRemoved +
                            " crew_Build astronauts from save eva list");
                    }

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
                    Debug.Log("[AstronautUnlocker] Injected " + backupCrewBuild.Count +
                        " crew_Build astronauts into save crew_World list");
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Backup prefix error: " + e);
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

                    if (AstronautState.main.state != null)
                    {
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
                }

                if (backupAstronauts != null && backupAstronauts.Count > 0)
                {
                    if (AstronautState.main?.state?.astronauts != null)
                    {
                        int existing = AstronautState.main.state.astronauts.Count;
                        foreach (var astro in backupAstronauts)
                        {
                            bool exists = false;
                            foreach (var existingAstro in AstronautState.main.state.astronauts)
                            {
                                if (existingAstro.astronautName == astro.astronautName)
                                {
                                    exists = true;
                                    break;
                                }
                            }
                            if (!exists)
                            {
                                AstronautState.main.state.astronauts.Add(astro);
                            }
                        }
                        int added = AstronautState.main.state.astronauts.Count - existing;
                        if (added > 0)
                        {
                            Debug.Log("[AstronautUnlocker] Restored " + added +
                                " astronauts after LoadSave (total: " +
                                AstronautState.main.state.astronauts.Count + ")");
                        }
                    }
                    backupAstronauts = null;
                }

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
                        Debug.Log("[AstronautUnlocker] Restored crew_Build after LoadSave: " +
                            string.Join(", ", AstronautState.main.crew_Build));
                    }
                    backupCrewBuild = null;
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Restore postfix error: " + e);
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
                Debug.Log("[AstronautUnlocker] fadeToBlack is null, skipping death animation");
                try
                {
                    __instance.astronaut.alive = false;
                }
                catch { }
                AstronautManager.DestroyEVA(__instance, death: true);
                return false;
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
                    Debug.Log("[AstronautUnlocker] Seat.OnStart: AstronautState not ready, preserving seat for " + astronautName);
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
                    Debug.Log("[AstronautUnlocker] Seat.OnStart: " + astronautName + " is Available, added to crew");
                    return false;
                }
                else if (state == AstronautState.State.CrewWorld)
                {
                    if (BuildManager.main != null)
                    {
                        AstronautState.main.state.crew_World.RemoveAll(
                            c => c.astronautName == astronautName);
                        AstronautState.main.AddCrew(astronautName);
                        Debug.Log("[AstronautUnlocker] Seat.OnStart: " + astronautName +
                            " transitioned from CrewWorld to CrewBuild");
                    }
                    tr.Method("AddSeatedAstronaut").GetValue();
                    Debug.Log("[AstronautUnlocker] Seat.OnStart: " + astronautName +
                        " is CrewWorld, seat PRESERVED (not cleared)");
                    return false;
                }
                else if (state == AstronautState.State.CrewBuild)
                {
                    if (BuildManager.main == null)
                    {
                        AstronautState.main.crew_Build.Remove(astronautName);
                        AstronautState.main.AddCrew(astronautName);
                        Debug.Log("[AstronautUnlocker] Seat.OnStart: " + astronautName +
                            " transitioned from CrewBuild to CrewWorld (world mode)");
                    }
                    tr.Method("AddSeatedAstronaut").GetValue();
                    Debug.Log("[AstronautUnlocker] Seat.OnStart: " + astronautName +
                        " is CrewBuild, seat preserved");
                    return false;
                }
                else
                {
                    Debug.Log("[AstronautUnlocker] Seat.OnStart: " + astronautName +
                        " is " + state + ", clearing seat");
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
                Debug.Log("[AstronautUnlocker] Seat.OnStart prefix error: " + e);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(CrewModule.Seat), "OnDestroy")]
    public class Patch_Seat_OnDestroy
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

                if (AstronautState.main != null)
                {
                    AstronautState.main.RemoveCrew(astronautName);
                }

                Debug.Log("[AstronautUnlocker] Seat.OnDestroy: " + astronautName +
                    " removed from crew, alive status preserved (scene unload protection)");
                return false;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Seat.OnDestroy prefix error: " + e);
                return true;
            }
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

        static bool Prefix(bool fromStaging, (Part, PolygonData)[] regions,
            ref UsePartData[] __result)
        {
            try
            {
                if (regions == null || regions.Length == 0)
                {
                    __result = new UsePartData[0];
                    return false;
                }

                int listenersAdded = 0;
                int partsSkipped = 0;
                Debug.Log("[AstronautUnlocker] UseParts Prefix: Processing " + regions.Length +
                    " parts (fromStaging=" + fromStaging + ")");
                foreach (var region in regions)
                {
                    Part part = region.Item1;
                    if (part == null) { partsSkipped++; continue; }
                    if (part.onPartUsed == null) { partsSkipped++; continue; }

                    int eventCount = part.onPartUsed.GetPersistentEventCount();
                    int id = part.GetInstanceID();
                    if (patchedParts.Contains(id))
                    {
                        Debug.Log("[AstronautUnlocker] UseParts Prefix: Skipping '" + part.name +
                            "' — already patched (id=" + id + ", persistentEvents=" + eventCount + ")");
                        continue;
                    }

                    bool foundModule = false;

                    DetachModule[] detachModules = part.GetModules<DetachModule>();
                    if (detachModules != null && detachModules.Length > 0)
                    {
                        DetachModule dm = detachModules[0];
                        part.onPartUsed.AddListener((UsePartData data) =>
                        {
                            Debug.Log("[AstronautUnlocker] Listener: calling DetachModule.Detach() for '" +
                                part.name + "'");
                            try { dm.Detach(data); }
                            catch (Exception e)
                            {
                                Debug.Log("[AstronautUnlocker] DetachModule.Detach error: " + e);
                            }
                        });
                        patchedParts.Add(id);
                        listenersAdded++;
                        foundModule = true;
                        Debug.Log("[AstronautUnlocker] UseParts Prefix: Added DetachModule listener to '" +
                            part.name + "' (id=" + id + ", fromStaging=" + fromStaging + ")");
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
                                Debug.Log("[AstronautUnlocker] SplitModule.Split error: " + e);
                            }
                        });
                        patchedParts.Add(id);
                        listenersAdded++;
                        foundModule = true;
                        Debug.Log("[AstronautUnlocker] UseParts Prefix: Added SplitModule listener to '" +
                            part.name + "' (id=" + id + ", fromStaging=" + fromStaging + ")");
                    }

                    if (!foundModule)
                    {
                        MonoBehaviour[] allModules = part.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
                        string moduleNames = allModules != null
                            ? string.Join(", ", System.Array.ConvertAll(allModules, m => m != null ? m.GetType().Name : "null"))
                            : "none";
                        Debug.Log("[AstronautUnlocker] UseParts Prefix: Part '" + part.name +
                            "' (id=" + id + ") has no DetachModule/SplitModule. Modules: " + moduleNames);
                    }
                }
                Debug.Log("[AstronautUnlocker] UseParts Prefix: Step 1 done — " +
                    listenersAdded + " listener(s) added, " + partsSkipped + " part(s) skipped");

                UsePartData.SharedData sharedData = new UsePartData.SharedData(fromStaging);
                UsePartData[] array = new UsePartData[regions.Length];
                for (int i = 0; i < regions.Length; i++)
                {
                    var (part, clickPolygon) = regions[i];
                    array[i] = new UsePartData(sharedData, clickPolygon);
                    if (part != null && part.onPartUsed != null)
                    {
                        Debug.Log("[AstronautUnlocker] UseParts Prefix: Invoking onPartUsed for '" +
                            part.name + "' (index=" + i + ")");
                        part.onPartUsed.Invoke(array[i]);
                    }
                }
                Debug.Log("[AstronautUnlocker] UseParts Prefix: Invoking onPostPartsActivation");
                sharedData.onPostPartsActivation?.Invoke();
                __result = array;
                return false;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] UseParts prefix error: " + e);
                return true;
            }
        }

        static void Postfix(bool fromStaging, (Part, PolygonData)[] regions)
        {
            try
            {
                if (fromStaging) return;
                if (regions == null) return;

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
                        Debug.Log("[AstronautUnlocker] UseParts: CrewModule part has no onPartUsed events. " +
                            "Manually opening seat menu (" + crewModules[0].seats.Length + " seats, " +
                            crewModules[0].HasCrew + " hasCrew).");
                        crewModules[0].OpenPartMenu_Seats();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] UseParts postfix error: " + e);
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
                        Debug.Log("[AstronautUnlocker] OpenPartMenu: AttachableStatsMenu NULL in World! " +
                            "Using MenuGenerator fallback.");
                        SeatMenuFallback.Show(__instance, canBoardWorld);
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] OpenPartMenu prefix error: " + e);
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
                Debug.Log("[AstronautUnlocker] OpenPartMenu_Seats called: " +
                    occupied + "/" + seatCount + " seats occupied, HasCrew=" + __instance.HasCrew);
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] OpenPartMenu_Seats prefix error: " + e);
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

                bool hasControl = disableAstronauts || anyHasAstronaut || !needsCrew;

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
                    Debug.Log("[AstronautUnlocker] OnSeatChange Prefix: re-enabled interior on " +
                        __instance.gameObject.name);
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
                SFS.Parts.Part part = tr.Field("part").GetValue<SFS.Parts.Part>();
                if (part != null && part.mass != null)
                    part.mass.Value = baseMass + seatMass;

                return false;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] OnSeatChange Prefix error: " + e);
                return true;
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
                                    Debug.Log("[AstronautUnlocker] EVA Exit: " + astroName);
                                    Traverse.Create(capturedModule).Method("EVA_Exit", capturedSeat).GetValue();
                                }
                                else
                                {
                                    Debug.Log("[AstronautUnlocker] EVA Board requested");
                                    Traverse.Create(capturedModule).Method("EVA_Board", capturedSeat).GetValue();
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.Log("[AstronautUnlocker] EVA action error: " + e);
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
                Debug.Log("[AstronautUnlocker] Seat menu fallback shown (" +
                    (seats?.Length ?? 0) + " seats)");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] SeatMenuFallback.Show error: " + e);
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
                    return true;

                Debug.Log("[AstronautUnlocker] SpawnFlag: flagPrefab is NULL, creating fallback flag");
                __result = FlagFallback.CreateFlag(location, direction);
                return false;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] SpawnFlag prefix error: " + e);
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

                Debug.Log("[AstronautUnlocker] Flag.Start: holder=" + (holder != null ? "OK" : "NULL") +
                    ", mapIcon=" + (mapIcon != null ? "OK" : "NULL"));
                return false;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Flag.Start prefix error: " + e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(AstronautManager), "PlantFlag")]
    public class Patch_AstronautManager_PlantFlag
    {
        static void Postfix()
        {
            try
            {
                if (PlayerController.main?.player?.Value is Astronaut_EVA eva)
                {
                    Debug.Log("[AstronautUnlocker] PlantFlag called by " +
                        eva.astronaut.astronautName + ", flags now: " +
                        (AstronautManager.main?.flags?.Count ?? 0));
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] PlantFlag postfix error: " + e);
            }
        }
    }

    public static class FlagFallback
    {
        private static Sprite flagSprite;

        public static Flag CreateFlag(Location location, int direction)
        {
            GameObject root = new GameObject("__FallbackFlag");
            root.SetActive(false);

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
            sr.color = new Color(0.9f, 0.2f, 0.2f, 1f);
            sr.sortingOrder = 100;
            holderObj.transform.localScale = new Vector3(0.3f, 0.6f, 1f);
            holderObj.transform.localPosition = new Vector3(0f, 0.3f, 0f);

            tr.Field("holder").SetValue(holderObj.transform);

            tr.Field("direction").SetValue(direction);

            tr.Field("mapIcon").SetValue(null);

            root.transform.position = WorldView.ToLocalPosition(location.position);

            root.SetActive(true);

            Debug.Log("[AstronautUnlocker] Fallback flag created at " +
                location.position + ", direction=" + direction);
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

    public static class PlantFlagButtonHelper
    {
        private static ModGUIButton plantFlagButton;
        private static GameObject flagBtnHolder;

        public static void Update()
        {
            try
            {
                bool isEVA = PlayerController.main?.player?.Value is Astronaut_EVA;

                if (isEVA && plantFlagButton == null)
                {
                    flagBtnHolder = ModGUIBuilder.CreateHolder(
                        ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_FlagBtn");
                    plantFlagButton = ModGUIBuilder.CreateButton(
                        flagBtnHolder.transform, 150, 50,
                        450, -300,
                        () =>
                        {
                            if (AstronautManager.main != null)
                            {
                                AstronautManager.main.PlantFlag();
                            }
                        },
                        "Plant Flag");
                    Debug.Log("[AstronautUnlocker] Plant Flag button created (EVA active)");
                }
                else if (!isEVA && plantFlagButton != null)
                {
                    if (flagBtnHolder != null)
                        UnityEngine.Object.Destroy(flagBtnHolder);
                    plantFlagButton = null;
                    flagBtnHolder = null;
                    Debug.Log("[AstronautUnlocker] Plant Flag button removed (EVA ended)");
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] UpdatePlantFlagButton error: " + e);
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
            crewRefreshTimer = 1.0f;
        }

        public static void ScheduleMenuRefresh()
        {
            pendingMenuRefresh = true;
        }

        public static void SchedulePickGridRefresh()
        {
            pickGridRefreshTimer = 0.1f;
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
                    Debug.Log("[AstronautUnlocker] PickGrid category re-selected to fix render leak");
                }
                else
                {
                    pickGridRefreshTimer = 0.1f;
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] PickGrid refresh error: " + e);
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
                    Debug.Log("[AstronautUnlocker] PartIconCreator camera was leaking — force-disabled in UpdateDriver.Update (rect=0, depth=-100)");
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
                            Debug.Log("[AstronautUnlocker] Refresh: re-enabled interior on " +
                                cm.gameObject.name);
                        }

                        if (part != null && part.gameObject != null && !part.gameObject.activeSelf)
                        {
                            part.gameObject.SetActive(true);
                            Debug.Log("[AstronautUnlocker] Refresh: re-enabled part GameObject " +
                                part.gameObject.name);
                        }

                        if (part != null && part.gameObject != null)
                        {
                            MeshRenderer[] renderers = part.GetComponentsInChildren<MeshRenderer>(true);
                            foreach (var mr in renderers)
                            {
                                if (!mr.enabled)
                                {
                                    mr.enabled = true;
                                    Debug.Log("[AstronautUnlocker] Refresh: re-enabled MeshRenderer on " +
                                        mr.gameObject.name);
                                }
                            }
                            SkinnedMeshRenderer[] skinned = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                            foreach (var smr in skinned)
                            {
                                if (!smr.enabled)
                                {
                                    smr.enabled = true;
                                    Debug.Log("[AstronautUnlocker] Refresh: re-enabled SkinnedMeshRenderer on " +
                                        smr.gameObject.name);
                                }
                            }
                        }

                        refreshed++;
                    }
                    catch { }
                }
                if (refreshed > 0 || skipped > 0)
                    Debug.Log("[AstronautUnlocker] Refreshed " + refreshed + " CrewModules (visibility check), skipped " +
                        skipped + " PickGrid icon parts");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] RefreshCrewModuleVisuals error: " + e);
            }
        }
    }

    public static class NativeAstronautUI
    {
        private static CrewModule.Seat pendingSeat;
        private static Action pendingRedraw;

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
                Debug.Log("[AstronautUnlocker] Assign error: " + e);
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
                            Debug.Log("[AstronautUnlocker] Astronaut created: " + name);
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
                Debug.Log("[AstronautUnlocker] Create dialog error: " + e);
            }
        }

        private static void AskFire(string name)
        {
            try
            {
                MenuGenerator.OpenConfirmation(
                    CloseMode.Stack,
                    () => "Discharge " + name + "?",
                    () => "Discharge",
                    delegate
                    {
                        AstronautState.main.FireAstronaut(name);
                        Debug.Log("[AstronautUnlocker] Astronaut discharged: " + name);
                        UpdateDriver.ScheduleMenuRefresh();
                    });
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Fire dialog error: " + e);
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
                Debug.Log("[AstronautUnlocker] SafeGetAstronautState error for " +
                    astronautName + ": " + e.Message);
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
                    Debug.Log("[AstronautUnlocker] PartIconCreator camera disabled on Awake (rect=0, depth=-100, cullingMask=0, clearFlags=Nothing)");
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
                    Debug.Log("[AstronautUnlocker] PartIconCreator camera re-disabled on Start (rect=0, depth=-100)");
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
                        Debug.Log("[AstronautUnlocker] Fuel pipe: curved '" + part.name +
                            "' shrunk 60% (rect=" + rect.size + ", tex=" + width + "x" + height + ")");
                    }
                    else
                    {
                        float shrink = 0.5f;
                        Vector2 center = rect.center;
                        Vector2 newSize = rect.size * shrink;
                        rect = new Rect(center - newSize / 2f, newSize);
                        width *= 2;
                        height *= 2;
                        Debug.Log("[AstronautUnlocker] Fuel pipe: straight '" + part.name +
                            "' shrunk 50% + 2x tex (rect=" + rect.size + ", tex=" + width + "x" + height + ")");
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
        static bool Prefix(EngineModule __instance)
        {
            try
            {
                if (HomeManager.main != null)
                    return true;

                var tr = Traverse.Create(__instance);
                var source = tr.Field("source").GetValue<FlowModule>();
                var thrust = tr.Field("thrust").GetValue<object>();
                var ISP = tr.Field("ISP").GetValue<object>();
                var throttle_Out = tr.Field("throttle_Out").GetValue<object>();
                var engineOn = tr.Field("engineOn").GetValue<object>();
                var heatHolder = tr.Field("heatHolder").GetValue<GameObject>();

                if (source == null || thrust == null || ISP == null ||
                    throttle_Out == null || engineOn == null || heatHolder == null)
                {
                    Debug.Log("[AstronautUnlocker] EngineModule.Start: skipping for " +
                        __instance.name + " (null field detected, source=" + (source != null) +
                        " thrust=" + (thrust != null) + " ISP=" + (ISP != null) +
                        " throttle_Out=" + (throttle_Out != null) +
                        " engineOn=" + (engineOn != null) +
                        " heatHolder=" + (heatHolder != null) + ")");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] EngineModule.Start prefix error: " + e);
                return false;
            }
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

                Debug.Log("[AstronautUnlocker] DetachModule.Detach diag: " +
                    "cannotDetachIfCovered=" + cannotDetach +
                    ", surfaceCovered=" + surfaceCovered +
                    ", hasSepSurface=" + hasSepSurface +
                    ", sepSurfaceCount=" + sepSurfaceCount +
                    ", rocket=" + (rocket != null) +
                    ", connectedJoints=" + connectedJoints +
                    ", part=" + __instance.transform.GetComponentInParentTree<Part>()?.name);
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] DetachModule.Detach diag error: " + e);
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
                    Debug.Log("[AstronautUnlocker] Water_Astronaut component added to EVA");
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] SpawnEVA buoyancy patch error: " + e.Message);
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
                    Debug.Log("[AstronautUnlocker] Buoyancy Postfix is running. gravity=" + gravity +
                        " hasWater_Astronaut=" + (__instance.GetComponent<Water_Astronaut>() != null));
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

                if (altitude <= 5.0 && altitude >= -5.0)
                {
                    Debug.Log("[AstronautUnlocker] Buoyancy: alt=" + altitude.ToString("F2") +
                              " submerged=" + submergedRatio.ToString("F2") +
                              " isInWater=" + water.isInWater +
                              " gravityMag=" + gravity.magnitude.ToString("F2"));
                }

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
                Debug.Log("[AstronautUnlocker] Buoyancy error: " + e.Message);
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
                if (__result.Count > 0) return;

                PickCategory[] categories = UnityEngine.Resources.FindObjectsOfTypeAll<PickCategory>();
                if (categories.Length == 0)
                {
                    Debug.Log("[AstronautUnlocker] No PickCategory objects found");
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
                        Debug.Log("[AstronautUnlocker] Added Fuel Pipe to category: " + name);
                        return;
                    }
                }

                __result.Add(new Variants.PickTag { tag = categories[0], priority = 50 });
                Debug.Log("[AstronautUnlocker] Added Fuel Pipe to first category (fallback)");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] GetPickTags fuel pipe patch error: " + e.Message);
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
                    Debug.Log("[AstronautUnlocker] FuelPipeModule.FindNeighbours: skipping (surface null)");
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
                    Debug.Log("[AstronautUnlocker] DetachModule.Detach skipped: separationSurface is null on " +
                        __instance.transform.GetComponentInParentTree<Part>()?.name);
                    return false;
                }
                if (__instance.separationSurface.surfaces == null || __instance.separationSurface.surfaces.Count == 0)
                {
                    Debug.Log("[AstronautUnlocker] DetachModule.Detach skipped: separationSurface.surfaces is null/empty on " +
                        __instance.transform.GetComponentInParentTree<Part>()?.name);
                    return false;
                }
                var rocketProp = typeof(DetachModule).GetProperty("Rocket",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                object rocket = rocketProp?.GetValue(__instance);
                if (rocket == null)
                {
                    Debug.Log("[AstronautUnlocker] DetachModule.Detach skipped: Rocket is null on " +
                        __instance.transform.GetComponentInParentTree<Part>()?.name);
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] DetachModule.Detach prefix error: " + e);
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
                if (getVar == null) return true;

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
                Debug.Log("[AstronautUnlocker] Composed_Float.GetResult exception suppressed: " + __exception.Message);
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
                        Debug.Log("[AstronautUnlocker] Teleport: no planet selected");
                        return false;
                    }

                    longitude = Mathf.Clamp((longitude + 360f) % 360f, 0f, 360f);

                    Location targetLocation;
                    bool rotate;

                    if (mode == 0)
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
                    else
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
                    Debug.Log("[AstronautUnlocker] Astronaut teleported to " + selectedPlanet.DisplayName +
                        " (mode=" + (mode == 0 ? "Surface" : "Orbit") + ", lon=" + longitude + ", h=" + height + ")");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] TeleportMenu.ConfirmTeleport prefix error: " + e);
                return true;
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

                if (isEVA && teleportButton == null)
                {
                    teleportBtnHolder = ModGUIBuilder.CreateHolder(
                        ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_TeleportBtn");
                    teleportButton = ModGUIBuilder.CreateButton(
                        teleportBtnHolder.transform, 150, 50,
                        450, -370,
                        () =>
                        {
                            try
                            {
                                if (TeleportMenu.main != null)
                                {
                                    TeleportMenu.main.OpenFromCheats();
                                    Debug.Log("[AstronautUnlocker] Teleport menu opened for EVA");
                                }
                                else
                                {
                                    Debug.Log("[AstronautUnlocker] TeleportMenu.main is null");
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.Log("[AstronautUnlocker] Teleport button error: " + e);
                            }
                        },
                        "Teleport");
                    Debug.Log("[AstronautUnlocker] Teleport button created (EVA active)");
                }
                else if (!isEVA && teleportButton != null)
                {
                    if (teleportBtnHolder != null)
                        UnityEngine.Object.Destroy(teleportBtnHolder);
                    teleportButton = null;
                    teleportBtnHolder = null;
                    Debug.Log("[AstronautUnlocker] Teleport button removed (EVA ended)");
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] TeleportButtonHelper error: " + e);
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
                    Debug.Log("[AstronautUnlocker] Dashboard created (EVA active)");
                }
                else if (!isEVA && dashboardLabel != null)
                {
                    if (dashboardHolder != null)
                        UnityEngine.Object.Destroy(dashboardHolder);
                    dashboardLabel = null;
                    dashboardHolder = null;
                    Debug.Log("[AstronautUnlocker] Dashboard removed (EVA ended)");
                }

                if (isEVA && dashboardLabel != null &&
                    PlayerController.main?.player?.Value is Astronaut_EVA eva)
                {
                    updateTimer += Time.deltaTime;
                    if (updateTimer >= 0.01f)
                    {
                        updateTimer = 0f;
                        UpdateTelemetry(eva, dashboardLabel);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] AstronautDashboardHelper error: " + e);
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

                label.Text = "Speed: " + speed.ToString("F1") + " m/s\n" +
                             "Altitude: " + altStr + "\n" +
                             "Fuel: " + (fuel * 100).ToString("F0") + "%";
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] UpdateTelemetry error: " + e);
            }
        }
    }
}
