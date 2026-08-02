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
    // ================================================================
    //  AstronautMod v3.6
    //
    //  v3.6 changes:
    //    - Renamed mod to "AstronautMod"
    //    - Description: "Enables the native astronaut/crew system on PC."
    //    - Hub "Astronauts" button dynamically positioned next to achievements
    //    - "Plant Flag" button moved to bottom-right corner
    //    - Blueprint: auto-open create dialog when no astronauts exist
    //    - Blueprint: show message + create button when all astronauts in EVA
    //
    //  问题：PC版SFS 1.6 已包含完整宇航员代码，但：
    //    a) Hub场景缺少 AstronautMenu GameObject（移动端有，PC版移除）
    //    b) Build场景不加载 AstronautState 数据（Hub和World场景会加载）
    //    c) 场景切换时 AstronautState 实例被销毁，宇航员数据丢失
    //    d) Seat.OnStart 清除 CrewWorld 状态的宇航员座位分配（v3.4修复）
    //    e) onPartUsed 事件可能未绑定到 OpenPartMenu_Seats（v3.4修复）
    //    f) World场景可能缺少 AttachableStatsMenu（v3.4修复）
    //    g) flagPrefab 为NULL，无法插旗（v3.5修复）
    //    h) RockSelector 可能不存在，无法捡石头（v3.5修复）
    //    i) 没有插旗按钮（v3.5添加ModGUI按钮）
    //
    //  方案：
    //    1. Harmony: DevSettings.DisableAstronauts => false
    //    2. 反射: 移除 DisableParts 中的 "Crew_New"
    //    3. Early_Load: 创建持久化 AstronautState (DontDestroyOnLoad)
    //    4. Harmony: 拦截 AstronautState.Awake，防止场景实例覆盖 main
    //    5. Harmony: 跳过 AstronautState.Start（自行管理数据加载）
    //    6. Harmony: 拦截 AstronautMenu.OpenMenu，用原生 MenuGenerator
    //       构建与游戏风格完全一致的UI
    //    7. Harmony: 跳过 AstronautMenu.Start/Update/OnOpen/OnClose/DrawList
    //    8. Build场景: 清空 crew_Build + 从 SavingCache 加载宇航员数据
    //    9. Hub场景: 激活 astronautsButton 或创建 ModGUI 替代按钮
    //   10. Harmony: Seat.OnStart 保留 CrewWorld/CrewBuild 状态的座位（v3.4）
    //   11. Harmony: Rocket.UseParts 为 CrewModule 部件提供菜单回退（v3.4）
    //   12. Harmony: CrewModule.OpenPartMenu 处理 null AttachableStatsMenu（v3.4）
    //   13. 创建 RockSelector 回退，使石头可被注册和捡取（v3.5）
    //   14. Harmony: SpawnFlag 处理 null flagPrefab，创建简易旗帜（v3.5）
    //   15. Harmony: Flag.Start 处理 null mapIcon（v3.5）
    //   16. ModGUI: EVA时显示"Plant Flag"按钮（v3.5）
    // ================================================================

    public class AstronautUnlockerMod : Mod
    {
        public static Harmony HarmonyInstance;

        public override string ModNameID => "astronaut_mod";
        public override string DisplayName => "AstronautMod";
        public override string Author => "A Future star";
        public override string MinimumGameVersionNecessary => "1.6";
        public override string ModVersion => "3.6.1";
        public override string Description => "Enables the native astronaut/crew system on PC.";

        public override void Early_Load()
        {
            HarmonyInstance = new Harmony("com.sfs.astronautunlocker");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            ModifyDisableParts();
            CreatePersistentAstronautState();
            Debug.Log("[AstronautMod] v3.6.1 loaded");
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
                LoadAstronautDataFromCache();
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
                // Clear crew_Build for fresh build session
                if (AstronautState.main.crew_Build == null)
                    AstronautState.main.crew_Build = new List<string>();
                else
                    AstronautState.main.crew_Build.Clear();
                // Build scene doesn't natively load astronaut data — load from cache
                LoadAstronautDataFromCache();
                Debug.Log("[AstronautUnlocker] Build scene ready, astronauts: " +
                    (AstronautState.main.state?.astronauts?.Count ?? 0));
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

                // v3.6: Dynamically find challengesButton (achievements) and place next to it
                float posX = -350f, posY = -150f; // Default fallback
                if (HubManager.main != null)
                {
                    FieldInfo chField = typeof(HubManager).GetField("challengesButton",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (chField != null)
                    {
                        GameObject challengesBtn = chField.GetValue(HubManager.main) as GameObject;
                        if (challengesBtn != null && challengesBtn.activeInHierarchy)
                        {
                            RectTransform chRT = challengesBtn.GetComponent<RectTransform>();
                            if (chRT != null)
                            {
                                // Re-parent our holder to the same parent as challengesButton
                                Transform chParent = challengesBtn.transform.parent;
                                if (chParent != null)
                                {
                                    hubHolder.transform.SetParent(chParent, false);
                                }
                                // Place to the right and above challengesButton
                                Vector2 chPos = chRT.anchoredPosition;
                                float chWidth = chRT.rect.width > 0 ? chRT.rect.width : 200f;
                                posX = chPos.x + chWidth + 20f;
                                posY = chPos.y + 80f; // Move up above the achievements button
                                Debug.Log("[AstronautUnlocker] challengesButton found at " + chPos +
                                    " width=" + chWidth + ", placing Astronauts at (" + posX + ", " + posY + ")");
                            }
                        }
                        else
                        {
                            Debug.Log("[AstronautUnlocker] challengesButton is null or inactive, using default position");
                        }
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

                AstronautState.State state = AstronautState.main.GetAstronautState(astronautName);

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
                return true; // Fall back to original on error
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
    // ================================================================
    public class UpdateDriver : MonoBehaviour
    {
        private float timer;
        private void Update()
        {
            timer += Time.deltaTime;
            if (timer >= 0.5f)
            {
                timer = 0f;
                PlantFlagButtonHelper.Update();
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
                    ((int)AstronautState.main.GetAstronautState(a.astronautName))
                    .CompareTo((int)AstronautState.main.GetAstronautState(b.astronautName)));

                foreach (var astro in sorted)
                {
                    string name = astro.astronautName;
                    AstronautState.State st = AstronautState.main.GetAstronautState(name);
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

            MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Current, elements.ToArray());
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
                    CloseMode.Current,
                    () => "Discharge " + name + "?",
                    () => "Discharge",
                    delegate
                    {
                        AstronautState.main.FireAstronaut(name);
                    });
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautUnlocker] Fire dialog error: " + e);
            }
        }
    }
}
