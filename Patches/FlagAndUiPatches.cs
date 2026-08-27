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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2378", e);
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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2391", e);
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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2447", e);
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
                        450, -250,
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
                ModLogger.ErrorOnce("AstronautUnlockerMod.cs line 2603", e);
            }
        }
    }
}
