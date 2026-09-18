using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SFS.Builds;
using SFS.Input;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.UI;
using SFS.World;
using UnityEngine;

namespace AstronautUnlocker
{
    /// <summary>
    /// Pre-launch check for the "missing astronaut" case, with an English-only warning.
    ///
    /// Context: this mod injects the native CrewModule into custom capsules and hands control
    /// to our own RefreshRocketControl logic (any occupied crew seat / "Allow control without crew"
    /// counts as control). But once the mod is uninstalled the native CrewModule falls back to
    /// "empty seat => no control", so a pure crew-capsule rocket (no command pod / probe core)
    /// launches uncontrollable / cannot launch.
    ///
    /// This patches BuildManager.Launch: if the rocket has crew capsules that are all empty and has
    /// no other control source (native ControlModule with control, an occupied crew seat, or
    /// "Allow control without crew"), it shows an English-only warning with two choices:
    ///   - "Continue Anyway": ignore the warning and launch as-is (player accepts the no-control risk);
    ///   - "Cancel": close the prompt without launching.
    /// </summary>
    [HarmonyPatch(typeof(BuildManager), "Launch")]
    public class Patch_BuildManager_Launch_MissingAstronautWarning
    {
        // 抑制标志：点“仍继续”时置位，让 Prefix 在重入时直接放行真正执行原生 Launch。
        // （原生 Launch 已被 Harmony detour，直接调用会重新进入本 Prefix，故用标志绕过自身拦截。）
        private static bool _suppressWarning;

        // 缓存原生 Launch（已含 Harmony trampoline）。访问修饰符未知，故同时尝试 Public / NonPublic。
        private static Action<BuildManager> _originalLaunch;

        private static Action<BuildManager> OriginalLaunch
        {
            get
            {
                if (_originalLaunch == null)
                {
                    MethodInfo mi = typeof(BuildManager).GetMethod(
                        "Launch",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (mi != null)
                        _originalLaunch = (Action<BuildManager>)Delegate.CreateDelegate(typeof(Action<BuildManager>), mi);
                }
                return _originalLaunch;
            }
        }

        static bool Prefix(BuildManager __instance)
        {
            // 若用户已选择“仍继续”，放行真正执行原生 Launch（重入时走这里）
            if (_suppressWarning) return true;

            try
            {
                PartHolder holder = GetPartHolder(__instance);
                if (holder == null) return true;

                CrewModule[] crews = holder.GetModules<CrewModule>();
                if (crews == null || crews.Length == 0) return true; // 没有乘员舱，不归本模组管

                bool hasNativeControl = holder.GetModules<ControlModule>()
                    .Any(c => c != null && c.hasControl != null && c.hasControl.Value);

                bool anyOccupiedCrew = crews.Any(c =>
                    c.seats != null && c.seats.Any(s => s != null && s.HasAstronaut));

                bool allowUncrewed = AstronautUnlockerMod.allowUncrewedControl;

                // 存在任一控制来源：原生发射 + 模组控制逻辑会保证可控，正常放行
                if (hasNativeControl || anyOccupiedCrew || allowUncrewed) return true;

                // 乘员舱全部空座且无任何控制来源 -> 发射后将无控制，弹出提示让用户选择
                ShowMissingAstronautWarning(__instance);
                return false; // 先拦截原生发射；“仍继续”按钮会另行调用原生 Launch
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Launch missing-astronaut warning", e);
                return true; // 异常时放行，不阻断原生流程
            }
        }

        private static void ShowMissingAstronautWarning(BuildManager __instance)
        {
            try
            {
                BuildManager captured = __instance;
                List<MenuElement> elements = new List<MenuElement>();

                SizeSyncerBuilder.Carrier carrier;
                elements.Add(new SizeSyncerBuilder(out carrier).VerticalMode(SizeMode.MaxChildSize));

                elements.Add(TextBuilder.CreateText(() =>
                    "Missing astronaut\n\n" +
                    "This rocket has crew capsules but no astronaut and no other control source " +
                    "(command pod / probe core). It would launch with no control.\n\n" +
                    "Assign at least one astronaut, or enable \"Allow control without crew\" in Mods Settings."));

                // Continue Anyway: ignore the warning and launch as-is (via the real native Launch)
                elements.Add(ButtonBuilder.CreateButton(
                    carrier,
                    () => "Continue Anyway",
                    () =>
                    {
                        Action<BuildManager> orig = OriginalLaunch;
                        if (orig == null) return; // 极端情况：拿不到原生方法则不做任何事
                        _suppressWarning = true;
                        try
                        {
                            orig(captured); // 重入 Prefix 时因标志置位而放行，真正执行原生 Launch
                        }
                        finally
                        {
                            _suppressWarning = false;
                        }
                    },
                    CloseMode.Current));

                // Cancel: just close the prompt, do not launch
                elements.Add(ButtonBuilder.CreateButton(
                    carrier,
                    () => "Cancel",
                    () => { },
                    CloseMode.Current));

                MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Current, elements.ToArray());
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Show missing-astronaut warning menu", e);
            }
        }

        static PartHolder GetPartHolder(BuildManager bm)
        {
            try
            {
                var grid = Traverse.Create(bm).Field("buildGrid").GetValue<BuildGrid>();
                if (grid == null) return null;
                var active = Traverse.Create(grid).Field("activeGrid").GetValue<PartGrid>();
                if (active == null) return null;
                return Traverse.Create(active).Field("partsHolder").GetValue<PartHolder>();
            }
            catch
            {
                return null;
            }
        }
    }
}
