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
        public override string ModVersion => "3.6.7";
        public override string Description => "Enables the native astronaut/crew system on PC.";

        public override void Early_Load()
        {
            HarmonyInstance = new Harmony("com.sfs.astronautunlocker");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            ModifyDisableParts();
            CreatePersistentAstronautState();
            Debug.Log("[AstronautMod] v3.6.7 loaded");
        }

        public override void Load()
        {
            SceneHelper.OnHubSceneLoaded += OnHubSceneLoaded;
            SceneHelper.OnBuildSceneLoaded += OnBuildSceneLoaded;
            SceneHelper.OnWorldSceneLoaded += OnWorldSceneLoaded;
            // v3.4: Register update driver for Plant Flag button
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
                EnsureAllStateLists(); // v3.6.7
                LoadAstronautDataFromCache();
                EnsureAllStateLists(); // v3.6.7: Re-check after load
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
                EnsureAllStateLists(); // v3.6.7: Prevent ArgumentNullException in GetAstronautState
                // Clear crew_Build for fresh build session
                if (AstronautState.main.crew_Build == null)
                    AstronautState.main.crew_Build = new List<string>();
                else
                    AstronautState.main.crew_Build.Clear();
                // Build scene doesn't natively load astronaut data — load from cache
                LoadAstronautDataFromCache();
                EnsureAllStateLists(); // v3.6.7: Re-check after LoadAstronautDataFromCache
                Debug.Log("[AstronautUnlocker] Build scene ready, astronauts: " +
                    (AstronautState.main.state?.astronauts?.Count ?? 0));
                // v3.6.4: Schedule a delayed refresh of CrewModule logic (hasControl/mass)
                UpdateDriver.ScheduleCrewModuleRefresh();
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

                // Diagnose AstronautManager and EVA prefab availability
                if (AstronautManager.main == null)
                {
                    Debug.Log("[AstronautUnlocker] WARNING: AstronautManager.main is null in World scene! EVA will not work.");
                    // Create a minimal AstronautManager as fallback
                    GameObject go = new GameObject("__AstronautManagerFallback");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    AstronautManager mgr = go.AddComponent<AstronautManager>();
                    // Awake() sets main = this
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

                        // Try to find Astronaut_EVA prefab in resource assets
                        Astronaut_EVA[] allEVA = UnityEngine.Object.FindObjectsOfType<Astronaut_EVA>(includeInactive: true);
                        if (allEVA != null && allEVA.Length > 0)
                        {
                            Debug.Log("[AstronautUnlocker] Found " + allEVA.Length + " Astronaut_EVA instances in scene");
                        }

                        // Try Resources.Load
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

                // v3.4: Ensure RockSelector exists (for rock collection)
                EnsureRockSelector();

                // v3.4: Try to find flagPrefab if missing
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

        // v3.4: Ensure RockSelector.main exists so DynamicTerrain can register rocks
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
                // Awake() sets main = this
                Debug.Log("[AstronautUnlocker] Created fallback RockSelector");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] EnsureRockSelector error: " + e);
            }
        }

        // v3.4: Try to find flagPrefab if null
        private static void EnsureFlagPrefab()
        {
            try
            {
                if (AstronautManager.main == null) return;
                if (AstronautManager.main.flagPrefab != null) return;

                // Try Resources.Load
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

                // Try finding Flag instances in scene
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

        // --- DevSettings.DisableParts: blank out "Crew_New" ---
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
                    if (parts[i] == "Crew_New")
                    {
                        parts[i] = "";
                        Debug.Log("[AstronautUnlocker] Crew_New un-disabled");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] DisableParts error: " + e);
            }
        }

        // --- Create AstronautMenu instance so .main is non-null ---
        private static void EnsureAstronautMenuInstance()
        {
            if (AstronautMenu.main != null) return;
            GameObject go = new GameObject("__AstronautMenuHolder");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AstronautMenu>();
            // Awake() sets main = this
            // Start/Update are patched to skip (null prefab refs)
            Debug.Log("[AstronautUnlocker] AstronautMenu instance created");
        }

        // --- Ensure AstronautState.main exists ---
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

        // --- Create persistent AstronautState that survives scene transitions ---
        // This is the key fix: without DontDestroyOnLoad, the AstronautState
        // instance is destroyed when leaving Hub, losing all astronaut data.
        private static AstronautState persistentState;

        private static void CreatePersistentAstronautState()
        {
            // If a valid instance already exists, adopt it
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
            // Awake() sets main = this
            if (persistentState.state == null)
                persistentState.state = new WorldSave.Astronauts();
            if (persistentState.crew_Build == null)
                persistentState.crew_Build = new List<string>();
            Debug.Log("[AstronautUnlocker] Persistent AstronautState created (DontDestroyOnLoad)");
        }

        // --- Ensure crew_Build list is initialized on the current main ---
        private static void EnsureCrewBuildList()
        {
            if (AstronautState.main != null && AstronautState.main.crew_Build == null)
                AstronautState.main.crew_Build = new List<string>();
        }

        // v3.6.7: Ensure ALL state lists are non-null.
        // GetAstronautState calls Enumerable.Any() on crew_Build, crew_World,
        // eva, and astronauts. If any is null, it throws ArgumentNullException
        // which aborts Part.InitializePart and causes 0 parts to load.
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

        // --- Load astronaut data from SavingCache ---
        // Hub scene does this via HubManager.LoadPersistent(), but Build scene
        // has no equivalent. This ensures astronauts created in Hub are available
        // when entering Build to assign to crew seats.
        private static void LoadAstronautDataFromCache()
        {
            try
            {
                if (AstronautState.main == null) return;

                // Skip if data already loaded (e.g., by HubManager)
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

        // --- Activate astronautsButton in Hub scene, or create one if missing ---
        private static ModGUIButton hubAstronautButton;
        private static GameObject hubHolder;

        private static void ActivateAstronautsButton()
        {
            try
            {
                // First try native button
                if (HubManager.main != null)
                {
                    FieldInfo btnField = typeof(HubManager).GetField("astronautsButton",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (btnField != null)
                    {
                        GameObject btn = btnField.GetValue(HubManager.main) as GameObject;
                        if (btn != null)
                        {
                            if (!btn.activeSelf)
                                btn.SetActive(true);
                            Debug.Log("[AstronautUnlocker] Native astronautsButton activated");
                            return; // Native button exists, no need for fallback
                        }
                    }
                }

                // Native button is null (PC scene doesn't have it) — create ModGUI button
                if (hubAstronautButton != null && hubAstronautButton.gameObject != null)
                    return; // Already created

                hubHolder = ModGUIBuilder.CreateHolder(ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_HubBtn");

                // v3.6: Find resumeGameButton and place Astronauts button above it
                float posX = 0f, posY = 100f; // Default fallback
                if (HubManager.main != null)
                {
                    // Try resumeGameButton (type SFS.UI.Button, a MonoBehaviour)
                    FieldInfo resumeField = typeof(HubManager).GetField("resumeGameButton",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (resumeField != null)
                    {
                        object resumeBtn = resumeField.GetValue(HubManager.main);
                        if (resumeBtn != null)
                        {
                            // SFS.UI.Button extends MonoBehaviour, so it has gameObject/transform
                            GameObject resumeGO = (resumeBtn as MonoBehaviour)?.gameObject;
                            if (resumeGO != null && resumeGO.activeInHierarchy)
                            {
                                RectTransform resumeRT = resumeGO.GetComponent<RectTransform>();
                                if (resumeRT != null)
                                {
                                    // Re-parent our holder to the same parent as resumeGameButton
                                    Transform resumeParent = resumeGO.transform.parent;
                                    if (resumeParent != null)
                                    {
                                        hubHolder.transform.SetParent(resumeParent, false);
                                    }
                                    // Place directly above resumeGameButton
                                    Vector2 resumePos = resumeRT.anchoredPosition;
                                    float resumeHeight = resumeRT.rect.height > 0 ? resumeRT.rect.height : 50f;
                                    posX = resumePos.x;
                                    posY = resumePos.y + resumeHeight + 20f;
                                    Debug.Log("[AstronautUnlocker] resumeGameButton found at " + resumePos +
                                        " height=" + resumeHeight + ", placing Astronauts at (" + posX + ", " + posY + ")");
                                }
                            }
                            else
                            {
                                Debug.Log("[AstronautUnlocker] resumeGameButton gameObject is null or inactive");
                            }
                        }
                        else
                        {
                            Debug.Log("[AstronautUnlocker] resumeGameButton is null, using default position");
                        }
                    }
                    else
                    {
                        Debug.Log("[AstronautUnlocker] resumeGameButton field not found, using default position");
                    }
                }

                hubAstronautButton = ModGUIBuilder.CreateButton(hubHolder.transform, 200, 50,
                    (int)posX, (int)posY,
                    () => NativeAstronautUI.ShowMenu(null, null),
                    "Astronauts");
                Debug.Log("[AstronautUnlocker] ModGUI fallback button created in Hub at (" + posX + ", " + posY + ")");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Button activate error: " + e);
            }
        }
    }

    // ================================================================
    //  Harmony Patches
    // ================================================================

    // --- Master switch: DisableAstronauts => false ---
    [HarmonyPatch(typeof(DevSettings), "get_DisableAstronauts")]
    public class Patch_DisableAstronauts
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    // --- Prevent scene-level AstronautState from overriding our persistent main ---
    // Without this patch, each scene's AstronautState.Awake() sets main = this,
    // destroying the reference to our DontDestroyOnLoad instance and its data.
    [HarmonyPatch(typeof(AstronautState), "Awake")]
    public class Patch_AstronautState_Awake
    {
        static bool Prefix(AstronautState __instance)
        {
            if (AstronautState.main != null && AstronautState.main != __instance)
            {
                // Our persistent instance is already main — don't let scene instances override
                return false;
            }
            return true;
        }
    }

    // --- Skip AstronautState.Start (we manage data loading in scene callbacks) ---
    // The original Start() loads from SavingCache only if selfManageSaving is true,
    // and it loads into the scene instance's state, not our persistent instance's state.
    [HarmonyPatch(typeof(AstronautState), "Start")]
    public class Patch_AstronautState_Start
    {
        static bool Prefix()
        {
            return false;
        }
    }

    // --- Backup/Inject astronaut data across GameManager.LoadSave ---
    // CRITICAL: GameManager.LoadSave() creates new AstronautState from save data,
    // THEN loads rockets (which triggers Seat.OnStart). If save has 0 astronauts,
    // Seat.OnStart clears seat assignments BEFORE our Postfix can restore them.
    // Fix: inject astronauts into save data in Prefix, before LoadSave runs.
    [HarmonyPatch(typeof(GameManager), "LoadSave")]
    public class Patch_GameManager_LoadSave
    {
        private static List<WorldSave.Astronauts.Data> backupAstronauts;

        static void Prefix(WorldSave save)
        {
            try
            {
                // Backup current astronauts
                if (AstronautState.main?.state?.astronauts != null &&
                    AstronautState.main.state.astronauts.Count > 0)
                {
                    backupAstronauts = new List<WorldSave.Astronauts.Data>(
                        AstronautState.main.state.astronauts);
                    Debug.Log("[AstronautUnlocker] Backed up " + backupAstronauts.Count +
                        " astronauts before LoadSave");
                }

                // CRITICAL: Inject astronauts into save data BEFORE LoadSave
                // Without this, Seat.OnStart() during rocket loading finds no astronauts
                // and clears seat assignments (astronaut.Value = "")
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
                // v3.6.7: CRITICAL — ensure all state lists are non-null
                // GetAstronautState accesses crew_Build, crew_World, eva, astronauts
                // via Enumerable.Any(). If any list is null, it throws
                // ArgumentNullException, which bubbles up through Seat.OnStart ->
                // CrewModule.Initialize -> Part.InitializePart -> PartsLoader.CreateParts
                // and ABORTS part spawning, resulting in 0 parts loaded.
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

                // Safety net: restore any astronauts still missing after LoadSave
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
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Restore postfix error: " + e);
            }
        }
    }

    // --- Handle null fadeToBlack in death animation ---
    // Astronaut_EVA.DeathAnimation accesses AstronautManager.main.fadeToBlack
    // which is NULL on PC. This patch skips the animation and destroys directly.
    [HarmonyPatch(typeof(Astronaut_EVA), "StartDeathAnimation")]
    public class Patch_EVA_DeathAnimation
    {
        static bool Prefix(Astronaut_EVA __instance, float startTime)
        {
            if (AstronautManager.main == null || AstronautManager.main.fadeToBlack == null)
            {
                Debug.Log("[AstronautUnlocker] fadeToBlack is null, skipping death animation");
                // Mark astronaut as dead and destroy without animation
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

    // --- Skip AstronautMenu.Start (accesses null menuHolder/elementPrefab) ---
    [HarmonyPatch(typeof(AstronautMenu), "Start")]
    public class Patch_AstronautMenu_Start
    {
        static bool Prefix() { return false; }
    }

    // --- Skip AstronautMenu.Update (accesses null fireButton) ---
    [HarmonyPatch(typeof(AstronautMenu), "Update")]
    public class Patch_AstronautMenu_Update
    {
        static bool Prefix() { return false; }
    }

    // --- Skip OnOpen/OnClose (access null menuHolder) ---
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

    // --- Skip DrawList (accesses null elementPrefab) ---
    [HarmonyPatch(typeof(AstronautMenu), "DrawList")]
    public class Patch_AstronautMenu_DrawList
    {
        static bool Prefix() { return false; }
    }

    // --- Skip CreateAstronaut/FireAstronaut (redirect to our implementation) ---
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

    // ================================================================
    //  Core: Intercept OpenMenu and build native UI via MenuGenerator
    // ================================================================
    [HarmonyPatch(typeof(AstronautMenu), "OpenMenu")]
    public class Patch_AstronautMenu_OpenMenu
    {
        static bool Prefix(AstronautMenu __instance, CrewModule.Seat seat, Action redrawSeat)
        {
            NativeAstronautUI.ShowMenu(seat, redrawSeat);
            return false; // skip original (which needs null prefabs)
        }
    }

    // ================================================================
    //  v3.4: Fix Seat.OnStart — preserve seats for CrewWorld/CrewBuild
    //  astronauts (ROOT CAUSE of "no exit button" when loading saves)
    //
    //  Original Seat.OnStart only accepts Available state. When loading
    //  a save, the astronaut is already in crew_World (state=CrewWorld),
    //  so the original code CLEARS the seat assignment. This makes the
    //  seat appear empty, so no "EVA Exit" button is drawn.
    //
    //  Fix: accept CrewWorld and CrewBuild states, preserve the seat.
    // ================================================================
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
                    Debug.Log("[AstronautUnlocker] Seat.OnStart: AstronautState not ready, preserving seat for " + astronautName);
                    return false; // Don't let original clear the seat
                }

                // v3.6.7: Ensure all lists are non-null before calling GetAstronautState.
                // GetAstronautState uses Enumerable.Any() on crew_Build, crew_World,
                // eva, and astronauts lists. If any is null, it throws
                // ArgumentNullException which aborts Part.InitializePart and
                // causes 0 parts to load.
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
                    // Original behavior: add to crew and show model
                    AstronautState.main.AddCrew(astronautName);
                    tr.Method("AddSeatedAstronaut").GetValue();
                    Debug.Log("[AstronautUnlocker] Seat.OnStart: " + astronautName + " is Available, added to crew");
                    return false;
                }
                else if (state == AstronautState.State.CrewWorld)
                {
                    // FIX: Astronaut was in crew_World (from World scene save data).
                    // When in Build mode, transition to crew_Build for proper tracking.
                    if (BuildManager.main != null)
                    {
                        // Remove from crew_World and add to crew_Build
                        AstronautState.main.state.crew_World.RemoveAll(
                            c => c.astronautName == astronautName);
                        AstronautState.main.AddCrew(astronautName); // Adds to crew_Build in Build mode
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
                    // In build mode, astronaut is in crew_Build. Preserve seat.
                    tr.Method("AddSeatedAstronaut").GetValue();
                    Debug.Log("[AstronautUnlocker] Seat.OnStart: " + astronautName +
                        " is CrewBuild, seat preserved");
                    return false;
                }
                else
                {
                    // Deceased or EVA — clear seat (original behavior)
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
                // v3.6.7: Return false instead of true — falling back to original
                // OnStart would also crash on null lists, aborting part creation.
                return false;
            }
        }
    }

    // ================================================================
    //  v3.6: Fix Seat.OnDestroy — prevent astronaut being killed on scene unload
    //
    //  ROOT CAUSE: When leaving World scene, rocket objects are destroyed,
    //  triggering Seat.OnDestroy. The original code checks GameManager.main
    //  (which is non-null in World) and sets astronaut.alive = false.
    //  This kills the astronaut! When returning to Build, Seat.OnStart
    //  finds the astronaut is Deceased and clears the seat.
    //
    //  Fix: Skip the alive=false line entirely. Astronauts should only
    //  die from actual gameplay events (crashes, etc.), not scene unloads.
    //  We still call RemoveCrew to clean up the crew list.
    // ================================================================
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
                    return false; // No astronaut, skip

                // Only remove from crew list, do NOT set alive = false
                if (AstronautState.main != null)
                {
                    AstronautState.main.RemoveCrew(astronautName);
                }

                Debug.Log("[AstronautUnlocker] Seat.OnDestroy: " + astronautName +
                    " removed from crew, alive status preserved (scene unload protection)");
                return false; // Skip original (which sets alive = false)
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Seat.OnDestroy prefix error: " + e);
                return true; // Fall back to original on error
            }
        }
    }

    // ================================================================
    //  v3.4: Fallback for onPartUsed not bound on PC part prefabs
    //  If the CrewModule part's onPartUsed has 0 persistent events,
    //  manually call OpenPartMenu_Seats to open the seat menu.
    // ================================================================
    [HarmonyPatch(typeof(Rocket), "UseParts")]
    public class Patch_Rocket_UseParts
    {
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

    // ================================================================
    //  v3.4: Fallback for null AttachableStatsMenu in World scene
    //  If the World scene lacks AttachableStatsMenu, use MenuGenerator
    //  to show a custom seat menu with EVA Exit/Board buttons.
    // ================================================================
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
                return true; // Let original run
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] OpenPartMenu prefix error: " + e);
                return true;
            }
        }
    }

    // ================================================================
    //  v3.4: Diagnostic logging for OpenPartMenu_Seats
    // ================================================================
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

    // ================================================================
    //  v3.6.6: Patch CrewModule.OnSeatChange — completely replace to
    //  prevent interior hiding on PC.
    //
    //  ROOT CAUSE: CrewModule.Initialize registers OnSeatChange as the
    //  OnChange handler for each seat's astronaut reference. When the
    //  build scene loads and seat values are set from save data, the
    //  event fires OnSeatChange, which calls:
    //    interior.SetActive(!hasControl)
    //  When an astronaut is present, hasControl=true, so interior is
    //  hidden. On PC, the interior GameObject contains the part's
    //  visual mesh, causing the entire part to disappear.
    //
    //  v3.6.5 Postfix approach didn't work — possibly because the event
    //  fires multiple times or interior is null while another mechanism
    //  hides the part.
    //
    //  v3.6.6 Fix: Prefix completely replaces OnSeatChange. We replicate
    //  all the original logic (hasControl, hatch, mass) EXCEPT we never
    //  hide the interior. Additionally, we proactively ensure interior
    //  is active and scan all MeshRenderers in the part hierarchy.
    // ================================================================
    [HarmonyPatch(typeof(CrewModule), "OnSeatChange")]
    public class Patch_CrewModule_OnSeatChange
    {
        static bool Prefix(CrewModule __instance)
        {
            try
            {
                var tr = Traverse.Create(__instance);

                // --- Replicate OnSeatChange logic (without hiding interior) ---

                bool disableAstronauts = DevSettings.DisableAstronauts;

                // Check if any seat has astronaut
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

                // Original logic: hasControl = disableAstronauts || anyHasAstronaut || !needsCrew
                bool hasControl = disableAstronauts || anyHasAstronaut || !needsCrew;

                // Update hasControl reference
                var hasControlRef = tr.Field("hasControl")
                    .GetValue<SFS.Variables.Bool_Reference>();
                if (hasControlRef != null)
                    hasControlRef.Value = hasControl;

                // Toggle hatch (original: hatch.SetActive(hasControl))
                var hatch = tr.Field("hatch").GetValue<GameObject>();
                if (hatch != null)
                    hatch.SetActive(hasControl);

                // --- CRITICAL DIFFERENCE: do NOT hide interior ---
                // Original: interior.SetActive(!hasControl)
                // We skip this entirely AND proactively ensure interior is active
                var interior = tr.Field("interior").GetValue<GameObject>();
                if (interior != null && !interior.activeSelf)
                {
                    interior.SetActive(true);
                    Debug.Log("[AstronautUnlocker] OnSeatChange Prefix: re-enabled interior on " +
                        __instance.gameObject.name);
                }

                // Update part mass: baseMass + 0.2 per seated astronaut
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
                Debug.Log("[AstronautUnlocker] OnSeatChange Prefix error: " + e);
                return true; // Fall back to original on error
            }
        }
    }

    // ================================================================
    //  v3.4: Custom seat menu using MenuGenerator (fallback when
    //  AttachableStatsMenu is missing in World scene).
    //  Calls CrewModule.EVA_Exit / EVA_Board via reflection.
    // ================================================================
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

    // ================================================================
    //  v3.4: Flag planting — handle null flagPrefab
    //  When flagPrefab is null, create a minimal flag with a simple
    //  visual sprite so the player can see where flags are planted.
    // ================================================================
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

    // --- Patch Flag.Start to handle null mapIcon ---
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
                return false; // Skip original (handles null safely)
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Flag.Start prefix error: " + e);
                return true;
            }
        }
    }

    // --- Patch PlantFlag to handle null flagPrefab and provide feedback ---
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

    // ================================================================
    //  v3.4: Fallback flag creation when flagPrefab is null
    //  Creates a minimal Flag with a simple colored sprite visual.
    // ================================================================
    public static class FlagFallback
    {
        private static Sprite flagSprite;

        public static Flag CreateFlag(Location location, int direction)
        {
            // Create root GameObject
            GameObject root = new GameObject("__FallbackFlag");
            root.SetActive(false); // Prevent OnEnable before setup

            // Add Flag component
            Flag flag = root.AddComponent<Flag>();

            // Set up location via reflection (WorldLocation)
            var tr = Traverse.Create(flag);
            var worldLoc = tr.Field<WorldLocation>("location").Value;
            if (worldLoc == null)
            {
                // WorldLocation is set up by Player/Flag via Unity serialization
                // We need to set it manually
                worldLoc = root.AddComponent<WorldLocation>();
                tr.Field("location").SetValue(worldLoc);
            }
            worldLoc.planet.Value = location.planet;
            worldLoc.position.Value = location.position;
            worldLoc.velocity.Value = location.velocity;

            // Create holder child with a visual sprite
            GameObject holderObj = new GameObject("Holder");
            holderObj.transform.SetParent(root.transform, false);
            holderObj.transform.localPosition = Vector3.zero;

            SpriteRenderer sr = holderObj.AddComponent<SpriteRenderer>();
            sr.sprite = GetFlagSprite();
            sr.color = new Color(0.9f, 0.2f, 0.2f, 1f); // Red flag
            sr.sortingOrder = 100;
            holderObj.transform.localScale = new Vector3(0.3f, 0.6f, 1f);
            holderObj.transform.localPosition = new Vector3(0f, 0.3f, 0f);

            // Set holder field
            tr.Field("holder").SetValue(holderObj.transform);

            // Set direction
            tr.Field("direction").SetValue(direction);

            // Skip mapIcon (set to null — patched Flag.Start handles it)
            tr.Field("mapIcon").SetValue(null);

            // Position the flag in the world
            root.transform.position = WorldView.ToLocalPosition(location.position);

            // Activate
            root.SetActive(true);

            // OnEnable adds to AstronautManager.main.flags
            Debug.Log("[AstronautUnlocker] Fallback flag created at " +
                location.position + ", direction=" + direction);
            return flag;
        }

        private static Sprite GetFlagSprite()
        {
            if (flagSprite != null) return flagSprite;

            // Try to find a sprite in the game's resources
            flagSprite = UnityEngine.Resources.Load<Sprite>("Flag");
            if (flagSprite != null) return flagSprite;

            // Create a simple white square sprite
            Texture2D tex = new Texture2D(4, 4);
            Color[] pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            flagSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return flagSprite;
        }
    }

    // ================================================================
    //  v3.4: Plant Flag button — ModGUI button shown when EVA
    //  Provides a button to plant flags since the native
    //  plantFlagButton may not be visible/bound on PC.
    // ================================================================
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
                    // Remove button when not EVA
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

    // ================================================================
    //  v3.4: UpdateDriver — runs UpdatePlantFlagButton each frame
    //  v3.6.4: Also handles delayed CrewModule logic refresh and
    //          delayed menu refresh after astronaut discharge
    // ================================================================
    public class UpdateDriver : MonoBehaviour
    {
        private float timer;
        private static float crewRefreshTimer = -1f;
        private static bool pendingMenuRefresh = false;

        public static void ScheduleCrewModuleRefresh()
        {
            crewRefreshTimer = 1.0f; // Wait 1 second for parts to fully initialize
        }

        // v3.6.4: Called from AskFire callback to schedule a menu refresh
        // on the next frame (after CloseMode.Stack has finished closing menus)
        public static void ScheduleMenuRefresh()
        {
            pendingMenuRefresh = true;
        }

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer >= 0.5f)
            {
                timer = 0f;
                PlantFlagButtonHelper.Update();
            }

            // v3.6.4: Delayed menu refresh after astronaut discharge
            if (pendingMenuRefresh)
            {
                pendingMenuRefresh = false;
                NativeAstronautUI.ShowMenu(null, null, CloseMode.None);
            }

            // v3.6.4: Delayed CrewModule logic refresh
            if (crewRefreshTimer > 0f)
            {
                crewRefreshTimer -= Time.deltaTime;
                if (crewRefreshTimer <= 0f)
                {
                    crewRefreshTimer = -1f;
                    RefreshCrewModuleVisuals();
                }
            }
        }

        // v3.6.6: Comprehensive visibility fix — ensures interior is active,
        // part GameObject is active, and all MeshRenderers are enabled.
        // This catches cases where OnSeatChange or other mechanisms hide
        // the part's visual mesh on PC.
        private static void RefreshCrewModuleVisuals()
        {
            try
            {
                CrewModule[] modules = UnityEngine.Object.FindObjectsOfType<CrewModule>(includeInactive: true);
                int refreshed = 0;
                foreach (CrewModule cm in modules)
                {
                    try
                    {
                        var tr = Traverse.Create(cm);

                        // Update hasControl
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

                        // Update part mass
                        float baseMass = tr.Field("baseMass").GetValue<float>();
                        float seatMass = 0f;
                        if (cm.seats != null)
                            foreach (var seat in cm.seats)
                                if (seat.HasAstronaut) seatMass += 0.2f;
                        SFS.Parts.Part part = tr.Field("part").GetValue<SFS.Parts.Part>();
                        if (part != null && part.mass != null)
                            part.mass.Value = baseMass + seatMass;

                        // v3.6.6: Ensure interior is active
                        var interior = tr.Field("interior").GetValue<GameObject>();
                        if (interior != null && !interior.activeSelf)
                        {
                            interior.SetActive(true);
                            Debug.Log("[AstronautUnlocker] Refresh: re-enabled interior on " +
                                cm.gameObject.name);
                        }

                        // v3.6.6: Ensure part's GameObject is active
                        if (part != null && part.gameObject != null && !part.gameObject.activeSelf)
                        {
                            part.gameObject.SetActive(true);
                            Debug.Log("[AstronautUnlocker] Refresh: re-enabled part GameObject " +
                                part.gameObject.name);
                        }

                        // v3.6.6: Enable all MeshRenderers in the part hierarchy
                        // (catches cases where the mesh renderer is disabled
                        // rather than the GameObject being inactive)
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
                if (refreshed > 0)
                    Debug.Log("[AstronautUnlocker] Refreshed " + refreshed + " CrewModules (visibility check)");
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] RefreshCrewModuleVisuals error: " + e);
            }
        }
    }

    // ================================================================
    //  Native UI implementation using the game's own MenuGenerator API.
    //  This produces menus identical in style to the rest of the game.
    // ================================================================
    public static class NativeAstronautUI
    {
        private static CrewModule.Seat pendingSeat;
        private static Action pendingRedraw;

        public static void ShowMenu(CrewModule.Seat seat, Action redrawSeat)
        {
            ShowMenu(seat, redrawSeat, CloseMode.Current);
        }

        // v3.6.4: closeMode parameter used by delayed refresh (CloseMode.None)
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
                        // Build mode: show Assign button for available astronauts
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
                        // Hub mode: show astronaut info + Fire button
                        string capturedName = name;
                        elements.Add(ButtonBuilder.CreateButton(carrier,
                            () => capturedName + " — " + statusText,
                                () => AskFire(capturedName),
                                CloseMode.None));
                    }
                }
            }

            // v3.6: In assign mode, if no available astronauts were found
            // (e.g., all in EVA, CrewWorld, or Deceased), show a helpful
            // message and a Create button instead of a blank page.
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
                            // Re-show the assign menu so user can assign the new astronaut
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
                        // v3.6.4: Use delayed refresh — calling ShowMenu from within
                        // the confirmation callback doesn't work because the menu
                        // system hasn't finished closing yet. Set a flag and let
                        // UpdateDriver open the fresh menu on the next frame.
                        UpdateDriver.ScheduleMenuRefresh();
                    });
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Fire dialog error: " + e);
            }
        }

        // v3.6.2: Safe wrapper for GetAstronautState — prevents NullReferenceException
        // when GameManager.main is non-null but AstronautManager.main is null
        // (happens during World->Build scene transition after astronaut death)
        // v3.6.7: Also ensures all state lists are non-null before calling
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
                // If we can't determine state, check if astronaut is alive in data
                var data = AstronautState.main?.state?.astronauts?
                    .FirstOrDefault(a => a.astronautName == astronautName);
                if (data != null && !data.alive)
                    return AstronautState.State.Deceased;
                return AstronautState.State.Available;
            }
        }
    }
}
