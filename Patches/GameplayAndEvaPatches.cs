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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3393", e);
            }
        }
    }

    public class Water_Astronaut : MonoBehaviour
    {
        public bool isInWater;
        public bool fatalWaterImpactHandled;
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
                ModLogger.ErrorOnce("EVA water setup", e);
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
                Water_Astronaut water = __instance.GetComponent<Water_Astronaut>();
                if (water == null) return;

                WorldLocation worldLocation = __instance.location;
                if (worldLocation == null) return;

                Planet planet = worldLocation.planet.Value;
                if (planet == null || planet.data == null || !planet.data.hasWater)
                {
                    water.isInWater = false;
                    return;
                }

                Double2 position = worldLocation.position.Value;
                double altitude = position.magnitude - planet.Radius;
                if (altitude > 0.5)
                {
                    water.isInWater = false;
                    return;
                }

                const float astronautRadius = 0.3f;
                double waterDepth = -altitude;
                float submergedRatio = Mathf.Clamp01((float)(waterDepth / (astronautRadius * 2.0)) + 0.5f);
                water.isInWater = submergedRatio > 0f;
                if (submergedRatio <= 0f) return;

                Rigidbody2D rigidbody = __instance.rb2d;
                if (rigidbody == null) return;

                Double2 globalVelocity = WorldView.ToGlobalVelocity(rigidbody.linearVelocity);
                Double2 upDirection = position.normalized;
                double radialVelocity = globalVelocity.x * upDirection.x + globalVelocity.y * upDirection.y;
                double inwardSpeed = -radialVelocity;

                if (!water.fatalWaterImpactHandled && (inwardSpeed > 15.0 || globalVelocity.magnitude > 30.0))
                {
                    water.fatalWaterImpactHandled = true;
                    water.isInWater = false;
                    rigidbody.linearVelocity = Vector2.zero;
                    rigidbody.angularVelocity = 0f;
                    __instance.hasControl.Value = false;
                    if (__instance.astronaut != null) __instance.astronaut.alive = false;
                    AstronautManager.DestroyEVA(__instance, death: true);
                    return;
                }

                float fixedDeltaTime = Time.fixedDeltaTime;
                float buoyancyAcceleration = submergedRatio * gravity.magnitude * 1.05f;
                globalVelocity += upDirection * (buoyancyAcceleration * fixedDeltaTime);

                double speed = globalVelocity.magnitude;
                if (speed > 0.01)
                {
                    double dragMagnitude = Mathf.Pow((float)speed, 1.2f) * 2.0f * astronautRadius * submergedRatio * fixedDeltaTime;
                    globalVelocity -= globalVelocity.normalized * Math.Min(dragMagnitude, speed);
                }

                radialVelocity = globalVelocity.x * upDirection.x + globalVelocity.y * upDirection.y;
                if (radialVelocity > 0.4)
                    globalVelocity -= upDirection * (radialVelocity - 0.4);

                rigidbody.linearVelocity = WorldView.ToLocalVelocity(globalVelocity);
                rigidbody.angularVelocity *= Mathf.Pow(0.3f, fixedDeltaTime * 2f);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA water physics", e);
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
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA water timewarp", e);
            }
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
                    catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3550", e);
            }

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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3562", e);
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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3616", e);
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




                    eva.physics.PhysicsMode = false;
                    eva.physics.SetLocationAndState(targetLocation, physicsMode: false);
                    eva.physics.PhysicsMode = true;

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

                    EVAControlRecovery.Attach(eva);
                    ScreenManager.main.CloseStack();
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(FlightInfoDrawer), "Update")]
    public class Patch_FlightInfoDrawer_HideForEVA
    {
        static bool Prefix(FlightInfoDrawer __instance)
        {
            try
            {
                bool evaSelected = PlayerController.main?.player?.Value is Astronaut_EVA;
                if (!evaSelected) return true;



                if (__instance != null && __instance.menuHolder != null)
                    __instance.menuHolder.SetActive(false);
                if (__instance != null && __instance.timewarpText != null)
                    __instance.timewarpText.Text = WorldTime.main.timewarpSpeed + "x";
                return false;
            }
            catch
            {
                return true;
            }
        }
    }


    public static class EVAStatsPanelHider
    {

        public static void LateUpdate()
        {
            bool evaSelected = PlayerController.main?.player?.Value is Astronaut_EVA;
            if (!evaSelected) return;

            FlightInfoDrawer[] drawers = UnityEngine.Object.FindObjectsOfType<FlightInfoDrawer>(true);
            foreach (FlightInfoDrawer drawer in drawers)
            {
                if (drawer == null || drawer.menuHolder == null) continue;
                if (drawer.menuHolder.activeSelf) drawer.menuHolder.SetActive(false);
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
                catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3806", e);
            }

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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3828", e);
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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3843", e);
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
                    if (updateTimer >= 0.01f)
                    {
                        updateTimer = 0f;
                        UpdateTelemetry(eva, dashboardLabel);
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3892", e);
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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3921", e);
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

                bool hasNativeCrew = AstronautUnlockerMod.HasNativeCrewModule(__instance);

                string partName = __instance.name;
                Part capturedPart = __instance;

                // 原生 CrewModule 已自带座位 只为其他控制部件显示 Enable EVA
                if (!hasNativeCrew)
                {
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

                            // 不关闭/重开菜单 避免菜单箭头因坐标问题"瞬移"
                            // getValue 回调会在下次重绘时自动反映新状态
                        }
                        catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 3979", e);
            }
                    },
                    () => AstronautUnlockerMod.evaConfig.ContainsKey(partName) &&
                           AstronautUnlockerMod.evaConfig[partName],
                    null, null);
                }

                // 仅模组适配部件可配置容量 原生座椅保留其真实单座位
                if (settings.build && !hasNativeCrew)
                {
                    drawer.DrawButton(-501,
                        () => "Crew Capacity",
                        () => AstronautUnlockerMod.GetCrewCapacity(partName) + " / 5",
                        () => AstronautUnlockerMod.OpenCrewCapacityMenu(capturedPart),
                        () => true,
                        null, null);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 4000", e);
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
                // 只有模组注入的载入舱支持可变容量 原生座椅保留真实单座位行为
                if (__instance.HasModule<CrewModule>())
                {
                    if (AstronautUnlockerMod.injectedPartIds.Contains(__instance.GetInstanceID()))
                        UpdateDriver.ScheduleCrewCapacityApply(__instance, refreshMenu: false);
                    return;
                }
                if (!__instance.HasModule<ControlModule>()) return;

                string partName = __instance.name;
                if (AstronautUnlockerMod.evaConfig.ContainsKey(partName) &&
                    AstronautUnlockerMod.evaConfig[partName])
                {
                    AstronautUnlockerMod.InjectCrewModule(__instance);
                    AstronautUnlockerMod.ClearModuleCache(__instance);
                    UpdateDriver.ScheduleCrewCapacityApply(__instance, refreshMenu: false);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 4033", e);
            }
        }
    }

    // 加宽大型模组舱体的 EVA 登舱距离（20 50）
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

    // 发射前把注入部件座椅上的乘员名存入 savedAstronauts
    // 注入的 CrewModule 不在部件 JSON 中 PartSave.CreateSaves() 不会序列化座椅乘员
    // 需手动保存并在世界场景重注入时恢复
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

                    // 注入部件 保存其乘员名
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

                // 持久化到 JSON 场景重载后仍保留
                AstronautUnlockerMod.SaveEvaConfig();
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 4099", e);
            }
        }
    }
}
