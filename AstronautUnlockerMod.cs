using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
        public override string ModVersion => "3.38";
        public override string Description => "Enables the native astronaut/crew system on PC.";

        public override void Early_Load()
        {
            HarmonyInstance = new Harmony("com.sfs.astronautunlocker");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            PatchVariableLists();
            ModifyDisableParts();
            CreatePersistentAstronautState();
            Debug.Log("[AstronautMod] v3.38 loaded");
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
                    Debug.Log("[AU] VariableList<> type not found, skipping patch");
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
                        Debug.Log("[AU] Failed to patch VariableList<" + T.Name + ">: " + e.Message);
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
                Debug.Log("[AU] PatchVariableLists error: " + e.Message);
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
                Debug.Log("[AU] Hub init error: " + e);
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
                else
                    AstronautState.main.crew_Build.Clear();
                LoadAstronautDataFromCache();
                EnsureAllStateLists();
                UpdateDriver.ScheduleCrewModuleRefresh();

                UpdateDriver.SchedulePickGridRefresh();
            }
            catch (Exception e)
            {
                Debug.Log("[AU] Build init error: " + e);
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

                Patch_Rocket_UseParts.ClearPatchedParts();

                if (AstronautManager.main == null)
                {
                    Debug.Log("[AU] WARNING: AstronautManager.main is null in World scene! EVA will not work.");
                    GameObject go = new GameObject("__AstronautManagerFallback");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    AstronautManager mgr = go.AddComponent<AstronautManager>();
                }
                else
                {
                    if (AstronautManager.main.astronautPrefab == null)
                    {
                        Debug.Log("[AU] WARNING: astronautPrefab is NULL! " +
                            "EVA SpawnEVA will fail. Attempting to find prefab in resources...");

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
                Debug.Log("[AU] World init error: " + e);
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
                Debug.Log("[AU] EnsureRockSelector error: " + e);
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

                Debug.Log("[AU] flagPrefab is NULL and no resources found. " +
                    "Will use code-generated flag fallback.");
            }
            catch (Exception e)
            {
                Debug.Log("[AU] EnsureFlagPrefab error: " + e);
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
                Debug.Log("[AU] DisableParts error: " + e);
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
                Debug.Log("[AU] Load from cache error: " + e);
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
                    Debug.Log("[AU] PersistAstronautStateToCache: no existing world save, skipping");
                    return;
                }

                WorldSave.Astronauts currentData = AstronautState.main.state;
                save.astronauts = SavingCache.GetCopy(currentData);

                SavingCache.main.SaveWorldPersistent(save, cache: true,
                    saveRocketsAndBranches: false, addToRevert: false, deleteRevert: false);
    }
            catch (Exception e)
            {
                Debug.Log("[AU] PersistAstronautStateToCache error: " + e);
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
                                                Debug.Log("[AU] TextAdapter set text error: " + te.Message);
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
                                        Debug.Log("[AU] WARNING: No text component found on cloned button!");

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
                                Debug.Log("[AU] resumeGameButton gameObject is null or inactive");
                            }
                        }
                        else
                        {
                            Debug.Log("[AU] resumeGameButton is null, using ModGUI fallback");
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
                Debug.Log("[AU] Button activate error: " + e);

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

                return false; // Skip original method
            }
            catch (Exception e)
            {
                Debug.Log("[AU] CreateAstronaut prefix error: " + e);
                return true; // Fall back to original on error
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
                }

                if (AstronautState.main?.crew_Build != null && AstronautState.main.crew_Build.Count > 0)
                {
                    backupCrewBuild = new List<string>(AstronautState.main.crew_Build);
                }
                else if (Patch_Seat_OnDestroy.destroyedSeatAstronauts.Count > 0)
                {
                    backupCrewBuild = new List<string>(Patch_Seat_OnDestroy.destroyedSeatAstronauts);
                }
                else
                {
                    backupCrewBuild = null;
                }

                if (save != null && save.astronauts == null)
                {
                    save.astronauts = new WorldSave.Astronauts();
                    Debug.Log("[AU] save.astronauts was null — created new");
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

                if (backupAstronauts != null && backupAstronauts.Count > 0 &&
                    save?.astronauts?.astronauts != null)
                {
                    foreach (var astro in backupAstronauts)
                    {
                        bool exists = save.astronauts.astronauts.Any(a => a.astronautName == astro.astronautName);
                        if (!exists)
                        {
                            save.astronauts.astronauts.Add(astro);
                        }
                    }
                }

                if (backupCrewBuild != null && backupCrewBuild.Count > 0 &&
                    save?.astronauts != null)
                {
                    if (save.astronauts.crew_World == null)
                        save.astronauts.crew_World = new List<WorldSave.Astronauts.Crew_World>();
                    if (save.astronauts.eva == null)
                        save.astronauts.eva = new List<WorldSave.Astronauts.EVA>();int worldRemoved = save.astronauts.crew_World.RemoveAll(c => backupCrewBuild.Contains(c.astronautName));

                    int evaRemoved = 0;
                    save.astronauts.eva.RemoveAll(e => { if (backupCrewBuild.Contains(e.astronautName)) { evaRemoved++; return true; } return false; });

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
                Debug.Log("[AU] Backup prefix error: " + e);
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
                    }
                    backupCrewBuild = null;
                }

                Patch_Seat_OnDestroy.destroyedSeatAstronauts.Clear();
            }
            catch (Exception e)
            {
                Debug.Log("[AU] Restore postfix error: " + e);
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
                Debug.Log("[AU] fadeToBlack is null, skipping death animation");
                try
                {
                    __instance.astronaut.alive = false;
                }
                catch { }
                AstronautManager.DestroyEVA(__instance, death: true);
                return false; // Skip original StartDeathAnimation
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
                Debug.Log("[AU] Seat.OnStart prefix error: " + e);
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
                    return false; // No astronaut, skip

                if (!destroyedSeatAstronauts.Contains(astronautName))
                    destroyedSeatAstronauts.Add(astronautName);

                if (AstronautState.main != null && BuildManager.main != null)
                {
                    AstronautState.main.RemoveCrew(astronautName);
                }

                return false; // Skip original (which sets alive = false)
            }
            catch (Exception e)
            {
                Debug.Log("[AU] Seat.OnDestroy prefix error: " + e);
                return true; // Fall back to original on error
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
                foreach (var region in regions)
                {
                    Part part = region.Item1;
                    if (part == null) { partsSkipped++; continue; }
                    if (part.onPartUsed == null) { partsSkipped++; continue; }

                    int eventCount = part.onPartUsed.GetPersistentEventCount();
                    int id = part.GetInstanceID();
                    if (patchedParts.Contains(id))
                    {
                        Debug.Log("[AU] UseParts Prefix: Skipping '" + part.name +
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
                            try { dm.Detach(data); }
                            catch (Exception e)
                            {
                                Debug.Log("[AU] DetachModule.Detach error: " + e);
                            }
                        });
                        patchedParts.Add(id);
                        listenersAdded++;
                        foundModule = true;
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
                                Debug.Log("[AU] SplitModule.Split error: " + e);
                            }
                        });
                        patchedParts.Add(id);
                        listenersAdded++;
                        foundModule = true;
                    }

                    if (!foundModule)
                    {
                        MonoBehaviour[] allModules = part.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
                        string moduleNames = allModules != null
                            ? string.Join(", ", System.Array.ConvertAll(allModules, m => m != null ? m.GetType().Name : "null"))
                            : "none";
                    }
                }

                UsePartData.SharedData sharedData = new UsePartData.SharedData(fromStaging);
                UsePartData[] array = new UsePartData[regions.Length];
                for (int i = 0; i < regions.Length; i++)
                {
                    var (part, clickPolygon) = regions[i];
                    array[i] = new UsePartData(sharedData, clickPolygon);
                    if (part != null && part.onPartUsed != null)
                    {
                        part.onPartUsed.Invoke(array[i]);
                    }
                }
                sharedData.onPostPartsActivation?.Invoke();
                __result = array;
                return false; // Skip original method
            }
            catch (Exception e)
            {
                Debug.Log("[AU] UseParts prefix error: " + e);
                return true; // Fall back to original on error
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
                        crewModules[0].OpenPartMenu_Seats();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AU] UseParts postfix error: " + e);
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
                        Debug.Log("[AU] OpenPartMenu: AttachableStatsMenu NULL in World! " +
                            "Using MenuGenerator fallback.");
                        SeatMenuFallback.Show(__instance, canBoardWorld);
                        return false;
                    }
                }
                return true; // Let original run
            }
            catch (Exception e)
            {
                Debug.Log("[AU] OpenPartMenu prefix error: " + e);
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
                Debug.Log("[AU] OpenPartMenu_Seats prefix error: " + e);
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

                return false; // Skip original OnSeatChange entirely
            }
            catch (Exception e)
            {
                Debug.Log("[AU] OnSeatChange Prefix error: " + e);
                return true; // Fall back to original on error
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
                                Debug.Log("[AU] EVA action error: " + e);
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
                Debug.Log("[AU] SeatMenuFallback.Show error: " + e);
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

                Debug.Log("[AU] SpawnFlag: flagPrefab is NULL, creating fallback flag");
                __result = FlagFallback.CreateFlag(location, direction);
                return false;
            }
            catch (Exception e)
            {
                Debug.Log("[AU] SpawnFlag prefix error: " + e);
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

                Debug.Log("[AU] Flag.Start: holder=" + (holder != null ? "OK" : "NULL") +
                    ", mapIcon=" + (mapIcon != null ? "OK" : "NULL"));
                return false; // Skip original (handles null safely)
            }
            catch (Exception e)
            {
                Debug.Log("[AU] Flag.Start prefix error: " + e);
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
                Debug.Log("[AU] PlantFlag prefix error: " + e);
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
                        450, -300,
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
                Debug.Log("[AU] UpdatePlantFlagButton error: " + e);
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
                Debug.Log("[AU] PickGrid refresh error: " + e);
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
                Debug.Log("[AU] RefreshCrewModuleVisuals error: " + e);
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
                Debug.Log("[AU] Assign error: " + e);
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
                Debug.Log("[AU] Create dialog error: " + e);
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
                Debug.Log("[AU] Fire dialog error: " + e);
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
                Debug.Log("[AU] SafeGetAstronautState error for " +
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
                Debug.Log("[AU] DetachModule.Detach diag error: " + e);
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
                Debug.Log("[AU] SpawnEVA buoyancy patch error: " + e.Message);
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
                Debug.Log("[AU] Buoyancy error: " + e.Message);
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
                Debug.Log("[AU] GetPickTags fuel pipe patch error: " + e.Message);
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
                    Debug.Log("[AU] FuelPipeModule.FindNeighbours: skipping (surface null)");
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
                    Debug.Log("[AU] DetachModule.Detach skipped: separationSurface is null on " +
                        __instance.transform.GetComponentInParentTree<Part>()?.name);
                    return false;
                }
                if (__instance.separationSurface.surfaces == null || __instance.separationSurface.surfaces.Count == 0)
                {
                    Debug.Log("[AU] DetachModule.Detach skipped: separationSurface.surfaces is null/empty on " +
                        __instance.transform.GetComponentInParentTree<Part>()?.name);
                    return false;
                }
                var rocketProp = typeof(DetachModule).GetProperty("Rocket",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                object rocket = rocketProp?.GetValue(__instance);
                if (rocket == null)
                {
                    Debug.Log("[AU] DetachModule.Detach skipped: Rocket is null on " +
                        __instance.transform.GetComponentInParentTree<Part>()?.name);
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AU] DetachModule.Detach prefix error: " + e);
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
                Debug.Log("[AU] TeleportMenu.ConfirmTeleport prefix error: " + e);
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
                        450, -370,
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
                                    Debug.Log("[AU] TeleportMenu.main is null");
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.Log("[AU] Teleport button error: " + e);
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
                Debug.Log("[AU] TeleportButtonHelper error: " + e);
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
                Debug.Log("[AU] AstronautDashboardHelper error: " + e);
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
                Debug.Log("[AU] UpdateTelemetry error: " + e);
            }
        }
    }
}

