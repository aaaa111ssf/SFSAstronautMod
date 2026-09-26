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

namespace AstronautMod
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
        // 分类列表在运行时基本不变 缓存一次 避免每次 GetPickTags 都全场景扫描
        private static PickCategory[] cachedPickCategories;

        private static PickCategory[] GetPickCategories()
        {
            if (cachedPickCategories == null || cachedPickCategories.Length == 0)
                cachedPickCategories = UnityEngine.Resources.FindObjectsOfTypeAll<PickCategory>();
            return cachedPickCategories;
        }

        static void Postfix(VariantRef __instance, ref List<Variants.PickTag> __result)
        {
            try
            {
                if (__instance?.part == null || __result == null) return;
                if (!__instance.part.HasModule<FuelPipeModule>()) return;
                if (__result.Count > 0) return;

                PickCategory[] categories = GetPickCategories();
                if (categories == null || categories.Length == 0)
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
                ModLogger.ErrorOnce("AstronautModMain.cs line 3550", e);
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
                ModLogger.ErrorOnce("AstronautModMain.cs line 3562", e);
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
                ModLogger.ErrorOnce("AstronautModMain.cs line 3616", e);
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
                // 用 Unity 重载 != null 判断：C# `is` 对已销毁的宇航员对象仍返回 true
                Astronaut_EVA eva = PlayerController.main != null
                    ? PlayerController.main.player?.Value as Astronaut_EVA
                    : null;
                bool evaSelected = eva != null;
                if (!evaSelected)
                {
                    SafeUpdate(__instance);
                    return false;
                }

                if (__instance != null && __instance.menuHolder != null)
                    __instance.menuHolder.SetActive(false);
                if (__instance != null && __instance.timewarpText != null && WorldTime.main != null)
                    __instance.timewarpText.Text = WorldTime.main.timewarpSpeed + "x";
                return false;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("FlightInfoDrawer safe update", e);
                return false; // 已接管 出错也不回退到会崩溃的原方法
            }
        }

        // 复刻 FlightInfoDrawer.Update 原逻辑 分隔符缺失时退化为整串显示
        static void SafeUpdate(FlightInfoDrawer d)
        {
            if (d == null) return;

            if (PlayerController.main != null && PlayerController.main.player.Value is Rocket rocket)
            {
                if (d.menuHolder != null) d.menuHolder.SetActive(true);

                float mass = rocket.rb2d.mass;
                float thrust = rocket.partHolder.GetModules<EngineModule>()
                        .Sum(a => a.thrust.Value * a.throttle_Out.Value)
                    + rocket.partHolder.GetModules<BoosterModule>()
                        .Sum(b => b.thrustVector.Value.magnitude * b.throttle_Out.Value);

                if (d.massText != null)
                    d.massText.Text = ValueAfterColon(mass.ToMassString(true));
                if (d.thrustText != null)
                    d.thrustText.Text = ValueAfterColon(thrust.ToThrustString());
                if (d.thrustToWeightText != null)
                    d.thrustToWeightText.Text = ValueAfterColon((thrust / mass).ToTwrString());
                if (d.partCountText != null)
                    d.partCountText.Text = rocket.partHolder.parts.Count.ToString();
            }
            else
            {
                if (d.massText != null)
                    d.massText.Text = ValueAfterColon(0f.ToMassString(true));
                if (d.thrustText != null)
                    d.thrustText.Text = ValueAfterColon(0f.ToThrustString());
                if (d.thrustToWeightText != null)
                    d.thrustToWeightText.Text = ValueAfterColon(0f.ToTwrString());
                if (d.partCountText != null)
                    d.partCountText.Text = 0.ToString();
            }

            if (d.timewarpText != null && WorldTime.main != null)
                d.timewarpText.Text = WorldTime.main.timewarpSpeed + "x";
        }

        /// <summary>取本地化串冒号后的值段 缺冒号时返回整串（原版此处会数组越界）。</summary>
        internal static string ValueAfterColon(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf(':');
            return i < 0 ? s : s.Substring(i + 1);
        }
    }

    [HarmonyPatch(typeof(LocationDrawer), "Update")]
    public class Patch_LocationDrawer_SafeText
    {
        static MethodInfo getIdealAngle;

        static bool Prefix(LocationDrawer __instance)
        {
            try
            {
                SafeUpdate(__instance);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("LocationDrawer safe update", e);
            }
            return false; // 完全接管
        }

        static void SafeUpdate(LocationDrawer d)
        {
            if (d == null) return;

            Location location = (PlayerController.main != null && PlayerController.main.player.Value != null)
                ? PlayerController.main.player.Value.location.Value
                : WorldView.main.ViewLocation;
            SelectableObject target = Map.navigation != null ? Map.navigation.target : null;

            string text;
            string text2;
            if (target is MapRocket && (location.position - target.Location.position).Mag_LessThan(10000.0))
            {
                text = Loc.main.Velocity_Relative_Horizontal
                    .Inject((location.velocity - target.Location.velocity).magnitude.ToVelocityString(), "speed");
                text2 = Loc.main.Distance_Relative_Horizontal
                    .Inject((location.position - target.Location.position).magnitude.ToDistanceString(), "distance");
            }
            else
            {
                text = Loc.main.Velocity_Horizontal
                    .Inject(location.velocity.magnitude.ToVelocityString(), "speed");
                double terrainHeight = location.GetTerrainHeight(true);
                text2 = (!(terrainHeight < 2000.0) && !(location.Height < 500.0))
                    ? Loc.main.Height_Horizontal.Inject(location.Height.ToDistanceString(), "height")
                    : Loc.main.Height_Terrain_Horizontal.Inject(terrainHeight.ToDistanceString(), "height");
            }

            // GetIdealAngle 是原版 private static，供俯仰角箭头使用 必须照常驱动
            if (getIdealAngle == null)
                getIdealAngle = AccessTools.Method(typeof(LocationDrawer), "GetIdealAngle");
            if (getIdealAngle != null)
            {
                object[] args = { false, 0.0, 0.0 };
                getIdealAngle.Invoke(null, args);
                d.currentAngleInfo = new AngleInfo((bool)args[0], (double)args[1], (double)args[2]);
            }

            SplitTitleValue(text2, out string heightTitle, out string heightValue);
            if (d.heightTitle != null) d.heightTitle.Text = heightTitle;
            if (d.heightText != null) d.heightText.Text = heightValue;

            SplitTitleValue(text, out string velocityTitle, out string velocityValue);
            if (d.velocityTitle != null) d.velocityTitle.Text = velocityTitle;
            if (d.velocityText != null) d.velocityText.Text = velocityValue;
        }

        static void SplitTitleValue(string s, out string title, out string value)
        {
            if (string.IsNullOrEmpty(s))
            {
                title = "";
                value = "";
                return;
            }
            int i = s.IndexOf(':');
            if (i < 0)
            {
                // 本地化串没有分隔符：标题留空 值取整串（原版此处 Substring(0, -1) 崩溃）
                title = "";
                value = s;
                return;
            }
            title = s.Substring(0, i);
            value = s.Substring(i).Replace(": ", "");
        }
    }

    public static class EVAStatsPanelHider
    {
        // 飞行信息面板在 EVA 期间数量固定 缓存数组 避免每帧 FindObjectsOfType 全场景扫描
        private static FlightInfoDrawer[] cachedDrawers;

        public static void LateUpdate()
        {
            // 用 Unity 重载 != null：已销毁的宇航员引用不算 EVA
            Astronaut_EVA eva = PlayerController.main != null
                ? PlayerController.main.player?.Value as Astronaut_EVA
                : null;
            bool evaSelected = eva != null;
            if (!evaSelected) { cachedDrawers = null; return; }

            if (cachedDrawers == null || cachedDrawers.Length == 0 || cachedDrawers[0] == null)
                cachedDrawers = UnityEngine.Object.FindObjectsOfType<FlightInfoDrawer>(true);

            foreach (FlightInfoDrawer drawer in cachedDrawers)
            {
                if (drawer == null || drawer.menuHolder == null) continue;
                if (drawer.menuHolder.activeSelf) drawer.menuHolder.SetActive(false);
            }
        }
    }

    // 原 TeleportButtonHelper 已移除：传送改为可配置按键（见 FlagAndUiPatches.EvaKeys），
    // 按键在游戏设置 Keybindings 的 Astronaut Mod 分区自定义。

    public static class AstronautDashboardHelper
    {
        private static SFS.UI.ModGUI.Label dashboardLabel;
        private static GameObject dashboardHolder;
        private static float updateTimer;

        public static void Update()
        {
            try
            {
                // 用 Unity 重载 != null：已销毁的宇航员引用（回发射后残留）不算 EVA，
                // 否则仪表盘会在发射时错误显示、EVA 期间提醒/按键状态错乱
                Astronaut_EVA liveEva = PlayerController.main != null
                    ? PlayerController.main.player?.Value as Astronaut_EVA
                    : null;
                bool isEVA = liveEva != null;

                // 遥测面板总开关（可在游戏设置里关闭）设置未就绪时默认显示；
                // main 意外为空（重载时序竞态）时自愈重建设置对象
                if (ModSettings.main == null)
                    AstronautModMain.EnsureModSettings();
                bool showDash = ModSettings.main == null || ModSettings.main.settings == null ||
                    ModSettings.main.settings.showTelemetryDashboard;

                bool panelAlive = dashboardHolder != null && dashboardLabel != null &&
                    dashboardLabel.gameObject != null;

                if (isEVA && showDash && !panelAlive)
                {
                    DestroyDashboard();
                    dashboardHolder = ModGUIBuilder.CreateHolder(
                        ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_Dashboard");
                    dashboardLabel = ModGUIBuilder.CreateLabel(
                        dashboardHolder.transform, 340, 110,
                        -470, 240,
                        "");
                    dashboardLabel.Color = new Color(1f, 1f, 1f, 0.9f);
                    dashboardLabel.FontSize = 15;
                    updateTimer = 1f; // 立刻在下一 tick 填充数据
                }
                else if ((!isEVA || !showDash) && dashboardHolder != null)
                {
                    DestroyDashboard();
                }

                if (isEVA && showDash && dashboardLabel != null && liveEva != null)
                {
                    updateTimer += Time.deltaTime;
                    // 刷新间隔由游戏设置里的 Telemetry refresh rate（Hz）决定，默认 20Hz
                    if (updateTimer >= ModSettings.TelemetryRefreshInterval())
                    {
                        updateTimer = 0f;
                        UpdateTelemetry(liveEva, dashboardLabel);
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 3892", e);
            }
        }

        private static void DestroyDashboard()
        {
            if (dashboardHolder != null)
                UnityEngine.Object.Destroy(dashboardHolder);
            dashboardHolder = null;
            dashboardLabel = null;
        }

        private static void UpdateTelemetry(Astronaut_EVA eva, SFS.UI.ModGUI.Label label)
        {
            try
            {
                Double2 globalVel = WorldView.ToGlobalVelocity(eva.rb2d.linearVelocity);
                double speed = globalVel.magnitude;

                double altitude = 0.0;
                double vertical = 0.0;
                string planetName = "";
                if (eva.location != null && eva.location.planet.Value != null)
                {
                    var planet = eva.location.planet.Value;
                    Double2 pos = eva.location.position.Value;
                    Double2 vel = eva.location.velocity.Value;
                    double r = pos.magnitude;
                    altitude = r - planet.Radius;
                    // 垂直速度 = 本地速度在径向单位向量上的投影（正=上升 负=下降）
                    if (r > 1e-6)
                        vertical = (vel.x * pos.x + vel.y * pos.y) / r;
                    planetName = planet.codeName ?? "";
                }

                double fuel = eva.resources?.fuelPercent?.Value ?? 0.0;

                string altStr = altitude >= 1000.0
                    ? (altitude / 1000.0).ToString("F2") + " km"
                    : altitude.ToString("F1") + " m";

                label.Text = $"Planet: {planetName}\n" +
                             $"Speed: {speed:F1} m/s   Vert: {vertical:+0.0;-0.0} m/s\n" +
                             $"Altitude: {altStr}\n" +
                             $"Fuel: {fuel * 100:F0}%";
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 3921", e);
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

                bool hasNativeCrew = AstronautModMain.HasNativeCrewModule(__instance);

                bool canHostCrew = __instance.HasModule<ControlModule>();

                string partName = __instance.name;
                Part capturedPart = __instance;

                // 原生 CrewModule 已自带座位 只为其他部件显示 Enable EVA
                // 且必须有 ControlModule（防止往邮箱等部件里塞宇航员）
                if (!hasNativeCrew && canHostCrew)
                {
                    drawer.DrawToggle(-500,
                    () => "Enable EVA",
                    () =>
                    {
                        try
                        {
                            bool currentEnabled = AstronautModMain.evaConfig.ContainsKey(partName) &&
                                                   AstronautModMain.evaConfig[partName];
                            bool newEnabled = !currentEnabled;
                            AstronautModMain.evaConfig[partName] = newEnabled;
                            AstronautModMain.SaveEvaConfig();

                            if (newEnabled)
                            {
                                AstronautModMain.InjectCrewModule(capturedPart);
                            }
                            else
                            {
                                AstronautModMain.RemoveCrewModule(capturedPart);
                            }

                            // 清模块缓存使 HasModule<CrewModule> 返回正确结果
                            AstronautModMain.ClearModuleCache(capturedPart);

                            // 不关闭/重开菜单 避免菜单箭头因坐标问题"瞬移"
                            // getValue 回调会在下次重绘时自动反映新状态
                        }
                        catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 3979", e);
            }
                    },
                    () => AstronautModMain.evaConfig.ContainsKey(partName) &&
                           AstronautModMain.evaConfig[partName],
                    null, null);
                }

                // 仅模组适配部件可配置容量 原生座椅保留其真实单座位
                if (settings.build && !hasNativeCrew && canHostCrew)
                {
                    drawer.DrawButton(-501,
                        () => "Crew Capacity",
                        () => AstronautModMain.GetCrewCapacity(partName) + " / 5",
                        () => AstronautModMain.OpenCrewCapacityMenu(capturedPart),
                        () => true,
                        null, null);
                }

                // Bug 4: 世界模式下给所有带座舱的部件一个直通乘员菜单入口
                if (settings.game)
                {
                    CrewModule[] crewModules = __instance.GetModules<CrewModule>();
                    if (crewModules != null && crewModules.Length > 0)
                    {
                        CrewModule capturedCrew = crewModules[0];
                        drawer.DrawButton(-505,
                            () => "Astronaut Menu",
                            () => "Open",
                            () => capturedCrew.OpenPartMenu(canBoardWorld: true),
                            () => true,
                            null, null);

                        drawer.DrawText(-506,
                            ModSettings.BindingHint());
                    }
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 4000", e);
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
                    if (AstronautModMain.injectedPartIds.Contains(__instance.GetInstanceID()))
                        UpdateDriver.ScheduleCrewCapacityApply(__instance, refreshMenu: false);
                    return;
                }
                // 必须有 ControlModule 才允许注入（防止往邮箱等部件里塞宇航员）
                if (!__instance.HasModule<ControlModule>()) return;

                string partName = __instance.name;
                // 部件自带持久化乘员配置时也必须注入（重启/回退后仍能恢复）
                bool storedConfig = CrewPersistence.HasStoredConfig(__instance);
                if (storedConfig)
                    AstronautModMain.evaConfig[partName] = true;

                if (AstronautModMain.evaConfig.ContainsKey(partName) &&
                    AstronautModMain.evaConfig[partName])
                {
                    AstronautModMain.InjectCrewModule(__instance);
                    AstronautModMain.ClearModuleCache(__instance);
                    UpdateDriver.ScheduleCrewCapacityApply(__instance, refreshMenu: false);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 4033", e);
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
                    if (!AstronautModMain.injectedPartIds.Contains(partId)) continue;

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

                    // 同步部件自身持久化变量 确保写入的蓝图/存档带上当前配置
                    CrewModule injected = part.GetComponentInChildren<CrewModule>(true);
                    if (injected != null)
                        CrewPersistence.SavePartState(part, injected);

                    if (savedList.Count > 0)
                    {
                        AstronautModMain.savedAstronauts[part.name] = savedList;
                        
                    }
                }

                // 持久化到 JSON 场景重载后仍保留
                AstronautModMain.SaveEvaConfig();
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 4099", e);
            }
        }
    }
}
