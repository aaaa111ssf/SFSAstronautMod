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

                __result = FlagFallback.CreateFlag(location, direction);
                return false;
            }
            catch (Exception e)
            {
                return true;
            }
        }

        static void Postfix(Flag __result, Location location, int direction)
        {
            try
            {
                FlagCustomization.OnFlagSpawned(__result, location, direction);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 2378", e);
            }
        }
    }

    [HarmonyPatch(typeof(AstronautManager), "DestroyFlag")]
    public class Patch_AstronautManager_DestroyFlag
    {
        static void Prefix(Flag flag)
        {
            try
            {
                FlagCustomization.ForgetFlag(flag);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 2391", e);
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

                return false;
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
                    FlagCustomization.BeginPlant(eva);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("AstronautModMain.cs line 2447", e);
            }
            return true;
        }

        static void Postfix()
        {

            FlagCustomization.CancelPendingPlant();
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

            sr.sortingOrder = -1;
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

    /// EVA 快捷键处理（替代原“Plant Flag / Teleport”悬浮按钮，按钮已按需求移除）。
    public static class EvaKeys
    {
        private static bool wasEva;

        // 消息栏不可用时的兜底提示（屏幕底部临时标签）
        private static GameObject hintHolder;
        private static SFS.UI.ModGUI.Label hintLabel;
        private static float hintUntil = -1f;

        public static void Update()
        {
            CleanupExpiredHint();

            Astronaut_EVA eva = null;
            try
            {
                if (PlayerController.main != null)
                    eva = PlayerController.main.player?.Value as Astronaut_EVA;
            }
            catch { }

            bool isEva = eva != null; // Unity 重载：已销毁对象 == null

            if (!isEva)
            {
                wasEva = false;
            }
            else if (!wasEva)
            {
                wasEva = true;
                ShowReminder();
            }

            int plantModifier;
            KeyCode plantKey;
            int teleportModifier;
            KeyCode teleportKey;
            GetBindings(out plantModifier, out plantKey, out teleportModifier, out teleportKey);

            try
            {
                if (KeyPressed(plantModifier, plantKey))
                {
                    if (isEva)
                    {
                        // PlantFlag 内部自带“此处不能插旗 / 附近已有旗”等提示
                        if (AstronautManager.main != null)
                            AstronautManager.main.PlantFlag();
                        else
                            Hint("Plant Flag unavailable in this scene.");
                    }
                    else
                    {
                        Hint("Plant Flag only works during EVA. (Press: " +
                            ModSettings.ComboName(plantModifier, plantKey) + ")");
                    }
                }

                if (KeyPressed(teleportModifier, teleportKey))
                {
                    bool cheatsAllowed = false;
                    try { cheatsAllowed = Base.worldBase != null && Base.worldBase.AllowsCheats; }
                    catch { }

                    if (!cheatsAllowed)
                        Hint("Teleport requires cheats to be enabled in this world.");
                    else if (TeleportMenu.main != null)
                        TeleportMenu.main.OpenFromCheats();
                    else
                        Hint("Teleport menu unavailable.");
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA key action", e);
            }
        }

        private static void GetBindings(out int plantModifier, out KeyCode plantKey,
            out int teleportModifier, out KeyCode teleportKey)
        {
            // 设置未就绪时退回默认键位，保证按键始终有响应
            plantModifier = 0;
            plantKey = KeyCode.F;
            teleportModifier = 0;
            teleportKey = KeyCode.G;

            ModSettings.Data settings = ModSettings.main != null ? ModSettings.main.settings : null;
            if (settings == null) return;

            plantModifier = settings.plantFlagModifier;
            plantKey = settings.plantFlagKey;
            teleportModifier = settings.teleportModifier;
            teleportKey = settings.teleportKey;
        }

        private static void ShowReminder()
        {
            int plantModifier;
            KeyCode plantKey;
            int teleportModifier;
            KeyCode teleportKey;
            GetBindings(out plantModifier, out plantKey, out teleportModifier, out teleportKey);

            Hint("EVA keys - Plant Flag: " +
                ModSettings.ComboName(plantModifier, plantKey) +
                "   |   Teleport: " +
                ModSettings.ComboName(teleportModifier, teleportKey));
        }

        private static void Hint(string message)
        {
            try
            {
                if (MsgDrawer.main != null)
                {
                    MsgDrawer.main.Log(message);
                    return;
                }
            }
            catch { }

            // 消息栏不可用（如某些场景）时退回到屏幕底部临时标签
            try
            {
                if (hintLabel == null || hintHolder == null)
                {
                    hintHolder = ModGUIBuilder.CreateHolder(
                        ModGUIBuilder.SceneToAttach.CurrentScene, "AstroUnlocker_KeyHint");
                    hintLabel = ModGUIBuilder.CreateLabel(hintHolder.transform,
                        620, 30, 0, -300, message);
                    hintLabel.Color = new Color(1f, 1f, 1f, 0.95f);
                }
                else
                {
                    hintLabel.Text = message;
                }
                hintUntil = Time.unscaledTime + 5f;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA key hint fallback", e);
            }
        }

        private static void CleanupExpiredHint()
        {
            if (hintLabel == null || hintHolder == null) return;
            if (hintUntil > 0f && Time.unscaledTime > hintUntil)
            {
                UnityEngine.Object.Destroy(hintHolder);
                hintHolder = null;
                hintLabel = null;
            }
        }

        // 修饰键必须精确匹配，避免“想按 F 却因挂着 Alt 误触其他绑定”
        private static bool KeyPressed(int modifier, KeyCode key)
        {
            if (key == KeyCode.None) return false;

            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            switch (modifier)
            {
                case 1: if (!shift) return false; break;
                case 2: if (!ctrl) return false; break;
                case 3: if (!alt) return false; break;
                default: if (alt || ctrl || shift) return false; break;
            }
            return Input.GetKeyDown(key);
        }
    }
}
