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
    public class UpdateDriver : MonoBehaviour
    {
        private float timer;
        private static float crewRefreshTimer = -1f;
        private static bool pendingMenuRefresh = false;
        private static float pickGridRefreshTimer = -1f;
        private static Part pendingCrewCapacityPart;
        private static bool refreshMenuAfterCrewCapacityApply;
        private static float crewCapacityApplyTimer = -1f;

        public static void ScheduleCrewModuleRefresh()
        {
            crewRefreshTimer = 1.0f;
        }

        public static void ScheduleMenuRefresh()
        {
            pendingMenuRefresh = true;
        }

        public static void ScheduleCrewCapacityApply(Part part, bool refreshMenu = true)
        {
            pendingCrewCapacityPart = part;
            refreshMenuAfterCrewCapacityApply = refreshMenu;
            crewCapacityApplyTimer = 0.25f;
        }

        public static void SchedulePickGridRefresh()
        {
            pickGridRefreshTimer = 0.1f;
        }

        // 图标相机引用在场景内基本稳定 缓存一次 避免每帧 GetComponent 全场景查找
        private static Camera iconCamera;
        private static void DisableIconCameraIfNeeded()
        {
            if (PartIconCreator.main == null) return;
            if (iconCamera == null || !iconCamera)
                iconCamera = PartIconCreator.main.GetComponent<Camera>();
            if (iconCamera != null && !NativeAstronautUI.IsIconCameraDisabled(iconCamera))
                NativeAstronautUI.DisableIconCamera(iconCamera);
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
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 58", error);
            }
        }

        private void Update()
        {
            // 依赖模组的加载顺序不保证 这里持续尝试直到订阅成功
            CustomSaveBridge.Ensure();

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
                DisableIconCameraIfNeeded();
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

            if (crewCapacityApplyTimer > 0f)
            {
                crewCapacityApplyTimer -= Time.deltaTime;
                if (crewCapacityApplyTimer <= 0f)
                {
                    Part part = pendingCrewCapacityPart;
                    bool refreshMenu = refreshMenuAfterCrewCapacityApply;
                    pendingCrewCapacityPart = null;
                    refreshMenuAfterCrewCapacityApply = false;
                    crewCapacityApplyTimer = -1f;
                    if (part != null && AstronautUnlockerMod.ApplyCrewCapacity(part) && refreshMenu)
                        AstronautUnlockerMod.ReopenPartMenu(part);
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
            EVAStatsPanelHider.LateUpdate();
            if (PartIconCreator.main != null)
            {
                DisableIconCameraIfNeeded();
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
                catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 152", error);
            }

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
                        bool hasControl = DevSettings.DisableAstronauts ||
                            AstronautUnlockerMod.allowUncrewedControl || anyHasAstronaut;

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
                    catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 228", error);
            }
                }
            }
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 231", error);
            }
        }
    }

    public static class NativeAstronautUI
    {
        private static CrewModule.Seat pendingSeat;
        private static Action pendingRedraw;
        private static HashSet<string> persistentEvaDutyNames = new HashSet<string>();

        internal static Dictionary<string, double> savedInternalFuel = new Dictionary<string, double>();
        internal static double? pendingFuelOverride = null;

        public static void ShowMenu(CrewModule.Seat seat, Action redrawSeat)
        {
            ShowMenu(seat, redrawSeat, CloseMode.Current, 0);
        }

        private static void RefreshPersistentEvaDutyNames()
        {
            try
            {
                if (SavingCache.main == null) return;
                WorldSave save = SavingCache.main.LoadWorldPersistent(
                    MsgDrawer.main, needsRocketsAndBranches: false, eraseCache: false);
                if (save?.astronauts?.eva == null) return;

                persistentEvaDutyNames.Clear();
                foreach (WorldSave.Astronauts.EVA eva in save.astronauts.eva)
                    if (eva != null && !string.IsNullOrEmpty(eva.astronautName))
                        persistentEvaDutyNames.Add(eva.astronautName);
            }
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 266", error);
            }
        }

        private static HashSet<string> CollectOnDutyAstronautNames()
        {
            HashSet<string> names = new HashSet<string>(persistentEvaDutyNames);
            try
            {
                AstronautUnlockerMod.EnsureAllStateLists();
                foreach (string name in AstronautState.main.crew_Build)
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                foreach (WorldSave.Astronauts.Crew_World crew in AstronautState.main.state.crew_World)
                    if (crew != null && !string.IsNullOrEmpty(crew.astronautName)) names.Add(crew.astronautName);
                if (GameManager.main == null)
                {
                    foreach (WorldSave.Astronauts.EVA evaSave in AstronautState.main.state.eva)
                        if (evaSave != null && !string.IsNullOrEmpty(evaSave.astronautName)) names.Add(evaSave.astronautName);
                }

                foreach (Astronaut_EVA eva in UnityEngine.Object.FindObjectsOfType<Astronaut_EVA>(true))
                    if (eva != null && eva.astronaut != null && !string.IsNullOrEmpty(eva.astronaut.astronautName))
                        names.Add(eva.astronaut.astronautName);
                if (AstronautManager.main?.eva != null)
                {
                    foreach (Astronaut_EVA eva in AstronautManager.main.eva)
                        if (eva != null && eva.astronaut != null && !string.IsNullOrEmpty(eva.astronaut.astronautName))
                            names.Add(eva.astronaut.astronautName);
                }

                foreach (CrewModule crew in UnityEngine.Object.FindObjectsOfType<CrewModule>(true))
                {
                    if (crew?.seats == null) continue;
                    foreach (CrewModule.Seat seat in crew.seats)
                        if (seat?.astronaut != null && !string.IsNullOrEmpty(seat.astronaut.Value))
                            names.Add(seat.astronaut.Value);
                }
            }
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 303", error);
            }
            return names;
        }

        private static void SynchronizeActiveEVAState()
        {
            AstronautUnlockerMod.EnsureAllStateLists();
        }

        private static bool IsAstronautOnDuty(string astronautName)
        {
            return !string.IsNullOrEmpty(astronautName) &&
                CollectOnDutyAstronautNames().Contains(astronautName);
        }

        public static void ShowMenu(CrewModule.Seat seat, Action redrawSeat, CloseMode closeMode, int page = 0)
        {
            pendingSeat = seat;
            pendingRedraw = redrawSeat;

            if (AstronautState.main == null || AstronautState.main.state == null)
            {
                Menu.read.Open(() => "AstronautState not available");
                return;
            }

            SynchronizeActiveEVAState();
            RefreshPersistentEvaDutyNames();
            List<WorldSave.Astronauts.Data> astronauts = AstronautState.main.state.astronauts;
            bool assignMode = seat != null;
            const int perPage = 8;
            List<WorldSave.Astronauts.Data> visible = astronauts == null
                ? new List<WorldSave.Astronauts.Data>()
                : astronauts.Where(astro =>
                {
                    if (!assignMode) return true;
                    string name = astro.astronautName;
                    return astro.alive && SafeGetAstronautState(name) == AstronautState.State.Available &&
                        !IsAstronautOnDuty(name);
                }).OrderBy(astro => (int)SafeGetAstronautState(astro.astronautName))
                  .ThenBy(astro => astro.astronautName).ToList();

            int totalPages = Mathf.Max(1, Mathf.CeilToInt(visible.Count / (float)perPage));
            page = Mathf.Clamp(page, 0, totalPages - 1);
            List<MenuElement> elements = new List<MenuElement>();
            SizeSyncerBuilder.Carrier carrier;
            elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));

            if (astronauts == null || astronauts.Count == 0)
            {
                elements.Add(TextBuilder.CreateText(() =>
                    assignMode ? "No astronauts available.\nCreate one to assign to this seat."
                               : "No astronauts yet."));
            }

            foreach (WorldSave.Astronauts.Data astro in visible.Skip(page * perPage).Take(perPage))
            {
                string capturedName = astro.astronautName;
                AstronautState.State state = SafeGetAstronautState(capturedName);
                string statusText = AstronautState.main.GetAstronautStateText(state, assignMode);
                if (assignMode)
                {
                    elements.Add(ButtonBuilder.CreateButton(carrier,
                        () => capturedName + " — " + statusText,
                        () => AssignToSeat(capturedName),
                        CloseMode.Current));
                }
                else
                {
                    elements.Add(ButtonBuilder.CreateButton(carrier,
                        () => capturedName + " — " + statusText,
                        () => OpenAstronautActions(capturedName),
                        CloseMode.None));
                }
            }

            if (assignMode && visible.Count == 0)
            {
                if (astronauts != null && astronauts.Count > 0)
                    elements.Add(TextBuilder.CreateText(() => "No astronauts available for assignment."));
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Create New Astronaut",
                    () => OpenCreateDialog(true),
                    CloseMode.Current));
            }

            if (totalPages > 1)
            {
                if (page > 0)
                {
                    int previousPage = page - 1;
                    elements.Add(ButtonBuilder.CreateButton(carrier,
                        () => "← Previous Page (" + (page + 1) + "/" + totalPages + ")",
                        () => ShowMenu(seat, redrawSeat, CloseMode.Current, previousPage),
                        CloseMode.Current));
                }
                if (page < totalPages - 1)
                {
                    int nextPage = page + 1;
                    elements.Add(ButtonBuilder.CreateButton(carrier,
                        () => "Next Page (" + (page + 1) + "/" + totalPages + ") →",
                        () => ShowMenu(seat, redrawSeat, CloseMode.Current, nextPage),
                        CloseMode.Current));
                }
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
                    if (IsAstronautOnDuty(name))
                    {
                        Menu.read.Open(() => name + " is already assigned to a mission.");
                        return;
                    }
                    pendingSeat.Board(name, 1.0, float.NegativeInfinity);
                    pendingRedraw?.Invoke();
                }
            }
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 440", error);
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
                            else
                            {
                                // 新建后刷新列表，让玩家立刻看到（并确认已写入存档）
                                ShowMenu(null, null);
                            }
                        }
                    },
                    CloseMode.Current,
                    TextInputMenu.Element("Astronaut name", ""));
            }
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 468", error);
            }
        }

        private static void OpenAstronautActions(string name)
        {
            try
            {
                List<MenuElement> elements = new List<MenuElement>();
                SizeSyncerBuilder.Carrier carrier;
                elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));
                elements.Add(TextBuilder.CreateText(() => name));
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Customize Flag",
                    () => FlagCustomization.OpenStyleMenu(name, () => UpdateDriver.ScheduleMenuRefresh()),
                    CloseMode.Current));
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Discharge",
                    () => AskFire(name),
                    CloseMode.Current));
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Back",
                    () => ShowMenu(null, null),
                    CloseMode.Current));
                MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Current, elements.ToArray());
            }
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 496", error);
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
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 535", error);
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
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 594", error);
            }
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
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 611", error);
            }
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
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 631", error);
            }
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
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 644", error);
            }
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
            catch (Exception error)
            {
                ModLogger.ErrorOnce("Runtime helper line 729", error);
            }
        }
    }
}
