using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ModLoader;
using ModLoader.Helpers;
using SFS;
using SFS.Input;
using SFS.UI;
using SFS.World;
using SFS.WorldBase;
using UnityEngine;

namespace AstronautUnlocker
{
    /// <summary>
    /// Stores flag appearances outside the game's WorldSave schema.  The native schema only
    /// persists a flag's location and direction, therefore each planted custom flag is indexed
    /// by its saved planet code and surface position.
    /// </summary>
    public static class FlagCustomization
    {
        private const string ConfigFileName = "AstronautMod_flags.json";
        private const float PendingPlantLifetime = 5f;

        [Serializable]
        private class FlagStyle
        {
            public string colorHex = "#FFFFFF";
            public string imageFile = "";

            public FlagStyle Clone()
            {
                return new FlagStyle
                {
                    colorHex = colorHex ?? "#FFFFFF",
                    imageFile = imageFile ?? ""
                };
            }

            public bool IsCustom()
            {
                return !string.IsNullOrWhiteSpace(imageFile) ||
                       !string.Equals(colorHex ?? "#FFFFFF", "#FFFFFF",
                           StringComparison.OrdinalIgnoreCase);
            }
        }

        [Serializable]
        private class AstronautStyleEntry
        {
            public string astronautName;
            public FlagStyle style;
        }

        [Serializable]
        private class FlagPlacementEntry
        {
            public string key;
            public string astronautName;
            public FlagStyle style;
        }

        [Serializable]
        private class FlagCustomizationData
        {
            public List<AstronautStyleEntry> astronautStyles = new List<AstronautStyleEntry>();
            public List<FlagPlacementEntry> placements = new List<FlagPlacementEntry>();
        }

        private static readonly Dictionary<string, FlagStyle> astronautStyles =
            new Dictionary<string, FlagStyle>();
        private static readonly Dictionary<string, FlagPlacementEntry> placements =
            new Dictionary<string, FlagPlacementEntry>();
        private static readonly Dictionary<string, Sprite> customSprites =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Sprite> originalSprites =
            new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Color> originalColors =
            new Dictionary<int, Color>();

        private static string pendingAstronautName;
        private static FlagStyle pendingStyle;
        private static float pendingPlantTime = -1f;
        private static bool initialized;

        public static string FlagsDirectory
        {
            get { return Path.Combine(Application.persistentDataPath, "AstronautMod", "Flags"); }
        }

        private static string ConfigPath
        {
            get { return Path.Combine(Application.persistentDataPath, ConfigFileName); }
        }

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Load();
            try
            {
                Directory.CreateDirectory(FlagsDirectory);
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautMod] Could not create custom flag directory: " + e.Message);
            }
        }

        public static void BeginPlant(Astronaut_EVA eva)
        {
            Initialize();
            if (eva == null || eva.astronaut == null) return;

            pendingAstronautName = eva.astronaut.astronautName;
            pendingStyle = GetStyle(pendingAstronautName);
            pendingPlantTime = Time.realtimeSinceStartup;
        }

        public static void CancelPendingPlant()
        {
            ClearPendingPlant();
        }

        public static void OnFlagSpawned(Flag flag, Location location, int direction)
        {
            Initialize();
            if (flag == null || location == null) return;

            string key = GetPlacementKey(location, direction);
            FlagStyle style = null;

            if (HasPendingPlant())
            {
                string owner = pendingAstronautName;
                FlagStyle selectedStyle = pendingStyle != null ? pendingStyle.Clone() : null;
                ClearPendingPlant();

                if (selectedStyle != null && selectedStyle.IsCustom())
                {
                    placements[key] = new FlagPlacementEntry
                    {
                        key = key,
                        astronautName = owner,
                        style = selectedStyle
                    };
                    style = selectedStyle;
                }
                else
                {
                    placements.Remove(key);
                }
                Save();
            }
            else if (placements.TryGetValue(key, out FlagPlacementEntry placement) &&
                     placement.style != null && placement.style.IsCustom())
            {
                style = placement.style;
            }

            ApplyStyle(flag, style);
        }

        public static void ForgetFlag(Flag flag)
        {
            Initialize();
            if (flag == null || flag.location == null) return;

            string key = GetPlacementKey(flag.location.Value, flag.direction);
            if (placements.Remove(key))
            {
                Save();
            }
        }

        public static void OpenStyleMenu(string astronautName, Action onChanged)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(astronautName)) return;

            FlagStyle current = GetStyle(astronautName);
            List<MenuElement> elements = new List<MenuElement>();
            SizeSyncerBuilder.Carrier carrier;
            elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));
            elements.Add(TextBuilder.CreateText(() => "Flag appearance for " + astronautName));
            elements.Add(TextBuilder.CreateText(() =>
                "Applies to flags planted by this astronaut. Existing custom flags update immediately."));
            elements.Add(ElementGenerator.VerticalSpace(10));

            AddColorButton(elements, carrier, astronautName, "White", "#FFFFFF", onChanged);
            AddColorButton(elements, carrier, astronautName, "Red", "#D94444", onChanged);
            AddColorButton(elements, carrier, astronautName, "Blue", "#3A78D4", onChanged);
            AddColorButton(elements, carrier, astronautName, "Green", "#4AA564", onChanged);
            AddColorButton(elements, carrier, astronautName, "Yellow", "#E2B93B", onChanged);
            AddColorButton(elements, carrier, astronautName, "Purple", "#8A5AC2", onChanged);

            elements.Add(ElementGenerator.VerticalSpace(10));
            string currentImage = string.IsNullOrWhiteSpace(current.imageFile)
                ? "None (use color)"
                : current.imageFile;
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Set image file: " + currentImage,
                () => OpenImageFileDialog(astronautName, onChanged),
                CloseMode.Current));
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Use color only",
                () =>
                {
                    FlagStyle style = GetStyle(astronautName);
                    style.imageFile = "";
                    SetStyle(astronautName, style);
                    onChanged?.Invoke();
                },
                CloseMode.Current));
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Reset to native flag",
                () =>
                {
                    astronautStyles.Remove(astronautName);
                    RefreshPlacedFlagsForAstronaut(astronautName, null);
                    Save();
                    onChanged?.Invoke();
                },
                CloseMode.Current));

            elements.Add(ElementGenerator.VerticalSpace(10));
            elements.Add(TextBuilder.CreateText(() => "Image folder: " + FlagsDirectory));
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Close",
                () => { },
                CloseMode.Current));

            MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Current, elements.ToArray());
        }

        private static void AddColorButton(List<MenuElement> elements, SizeSyncerBuilder.Carrier carrier,
            string astronautName, string label, string colorHex, Action onChanged)
        {
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Flag color: " + label,
                () =>
                {
                    FlagStyle style = GetStyle(astronautName);
                    style.colorHex = colorHex;
                    SetStyle(astronautName, style);
                    onChanged?.Invoke();
                },
                CloseMode.Current));
        }

        private static void OpenImageFileDialog(string astronautName, Action onChanged)
        {
            FlagStyle current = GetStyle(astronautName);
            Menu.textInput.Open(
                "Cancel", "Use image",
                delegate(string[] input)
                {
                    string enteredName = input != null && input.Length > 0 ? input[0] : "";
                    enteredName = (enteredName ?? "").Trim();
                    string fileName = Path.GetFileName(enteredName);
                    string extension = Path.GetExtension(fileName).ToLowerInvariant();

                    if (string.IsNullOrWhiteSpace(fileName) ||
                        (extension != ".png" && extension != ".jpg" && extension != ".jpeg"))
                    {
                        Menu.read.Open(() => "Enter a PNG or JPG filename placed in:\n" + FlagsDirectory);
                        return;
                    }

                    string fullPath = Path.Combine(FlagsDirectory, fileName);
                    if (!File.Exists(fullPath))
                    {
                        Menu.read.Open(() => "Image not found:\n" + fullPath);
                        return;
                    }

                    FlagStyle style = GetStyle(astronautName);
                    style.imageFile = fileName;
                    SetStyle(astronautName, style);
                    onChanged?.Invoke();
                },
                CloseMode.Current,
                TextInputMenu.Element("PNG/JPG file name", current.imageFile ?? ""));
        }

        private static void SetStyle(string astronautName, FlagStyle style)
        {
            if (string.IsNullOrWhiteSpace(astronautName)) return;
            astronautStyles[astronautName] = style != null ? style.Clone() : new FlagStyle();
            RefreshPlacedFlagsForAstronaut(astronautName, astronautStyles[astronautName]);
            Save();
        }

        private static FlagStyle GetStyle(string astronautName)
        {
            if (!string.IsNullOrWhiteSpace(astronautName) &&
                astronautStyles.TryGetValue(astronautName, out FlagStyle style) && style != null)
            {
                return style.Clone();
            }
            return new FlagStyle();
        }

        private static void RefreshPlacedFlagsForAstronaut(string astronautName, FlagStyle style)
        {
            bool useCustomStyle = style != null && style.IsCustom();
            bool changed = false;

            List<string> placementKeys = placements
                .Where(pair => string.Equals(pair.Value.astronautName, astronautName,
                    StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToList();
            foreach (string key in placementKeys)
            {
                if (useCustomStyle)
                {
                    placements[key].style = style.Clone();
                }
                else
                {
                    placements.Remove(key);
                }
                changed = true;
            }

            Flag[] flags = UnityEngine.Object.FindObjectsOfType<Flag>(true);
            foreach (Flag flag in flags)
            {
                if (flag == null || flag.location == null) continue;
                string key = GetPlacementKey(flag.location.Value, flag.direction);
                if (!placementKeys.Contains(key)) continue;

                if (useCustomStyle)
                {
                    ApplyStyle(flag, style);
                }
                else
                {
                    RestoreStyle(flag);
                }
            }

            if (changed) Save();
        }

        private static bool HasPendingPlant()
        {
            if (pendingPlantTime < 0f) return false;
            if (Time.realtimeSinceStartup - pendingPlantTime <= PendingPlantLifetime) return true;
            ClearPendingPlant();
            return false;
        }

        private static void ClearPendingPlant()
        {
            pendingAstronautName = null;
            pendingStyle = null;
            pendingPlantTime = -1f;
        }

        private static string GetPlacementKey(Location location, int direction)
        {
            if (location == null) return "";
            Planet planet = location.planet;
            string planetCode = planet != null && !string.IsNullOrWhiteSpace(planet.codeName)
                ? planet.codeName
                : "UnknownPlanet";
            Double2 position = location.position;
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1:R}|{2:R}|{3}",
                planetCode, position.x, position.y, direction);
        }

        private static void ApplyStyle(Flag flag, FlagStyle style)
        {
            if (flag == null) return;
            if (style == null || !style.IsCustom())
            {
                RestoreStyle(flag);
                return;
            }

            SpriteRenderer renderer = FindFlagRenderer(flag);
            if (renderer == null) return;

            int id = renderer.GetInstanceID();
            if (!originalSprites.ContainsKey(id)) originalSprites[id] = renderer.sprite;
            if (!originalColors.ContainsKey(id)) originalColors[id] = renderer.color;

            if (!string.IsNullOrWhiteSpace(style.imageFile))
            {
                Sprite sprite = LoadCustomSprite(style.imageFile);
                if (sprite != null) renderer.sprite = sprite;
            }

            renderer.color = ParseColor(style.colorHex, originalColors[id]);
        }

        private static void RestoreStyle(Flag flag)
        {
            SpriteRenderer renderer = FindFlagRenderer(flag);
            if (renderer == null) return;

            int id = renderer.GetInstanceID();
            if (originalSprites.TryGetValue(id, out Sprite originalSprite))
                renderer.sprite = originalSprite;
            if (originalColors.TryGetValue(id, out Color originalColor))
                renderer.color = originalColor;
        }

        private static SpriteRenderer FindFlagRenderer(Flag flag)
        {
            SpriteRenderer[] renderers = flag.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers == null || renderers.Length == 0) return null;

            SpriteRenderer namedRenderer = renderers.FirstOrDefault(renderer =>
                renderer != null &&
                (renderer.gameObject.name.IndexOf("flag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 renderer.gameObject.name.IndexOf("cloth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 renderer.gameObject.name.IndexOf("banner", StringComparison.OrdinalIgnoreCase) >= 0));
            if (namedRenderer != null) return namedRenderer;

            return renderers
                .Where(renderer => renderer != null)
                .OrderByDescending(renderer =>
                {
                    Bounds bounds = renderer.bounds;
                    return Math.Abs(bounds.size.x * bounds.size.y);
                })
                .FirstOrDefault();
        }

        private static Sprite LoadCustomSprite(string imageFile)
        {
            string fileName = Path.GetFileName(imageFile ?? "");
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            if (customSprites.TryGetValue(fileName, out Sprite cached)) return cached;

            try
            {
                string path = Path.Combine(FlagsDirectory, fileName);
                if (!File.Exists(path))
                {
                    Debug.Log("[AstronautMod] Custom flag image was not found: " + path);
                    return null;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!TryLoadImage(texture, File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(texture);
                    Debug.Log("[AstronautMod] Could not decode custom flag image: " + path);
                    return null;
                }

                texture.name = "AstronautMod_Flag_" + fileName;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                Sprite sprite = Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = texture.name;
                customSprites[fileName] = sprite;
                return sprite;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautMod] Could not load custom flag image: " + e.Message);
                return null;
            }
        }

        private static bool TryLoadImage(Texture2D texture, byte[] imageBytes)
        {
            try
            {
                Type imageConversion = Type.GetType(
                    "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                MethodInfo loadImage = imageConversion?.GetMethod("LoadImage",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
                if (loadImage == null) return false;
                object loaded = loadImage.Invoke(null, new object[] { texture, imageBytes, false });
                return loaded is bool && (bool)loaded;
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautMod] Image decoder is unavailable: " + e.Message);
                return false;
            }
        }

        private static Color ParseColor(string colorHex, Color fallback)
        {
            Color color;
            return !string.IsNullOrWhiteSpace(colorHex) &&
                   ColorUtility.TryParseHtmlString(colorHex, out color)
                ? color
                : fallback;
        }

        private static void Load()
        {
            try
            {
                astronautStyles.Clear();
                placements.Clear();
                if (!File.Exists(ConfigPath)) return;

                FlagCustomizationData data = JsonUtility.FromJson<FlagCustomizationData>(
                    File.ReadAllText(ConfigPath));
                if (data?.astronautStyles != null)
                {
                    foreach (AstronautStyleEntry entry in data.astronautStyles)
                    {
                        if (!string.IsNullOrWhiteSpace(entry?.astronautName) && entry.style != null)
                            astronautStyles[entry.astronautName] = entry.style.Clone();
                    }
                }
                if (data?.placements != null)
                {
                    foreach (FlagPlacementEntry entry in data.placements)
                    {
                        if (!string.IsNullOrWhiteSpace(entry?.key) && entry.style != null)
                        {
                            placements[entry.key] = new FlagPlacementEntry
                            {
                                key = entry.key,
                                astronautName = entry.astronautName ?? "",
                                style = entry.style.Clone()
                            };
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautMod] Could not load custom flag settings: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                FlagCustomizationData data = new FlagCustomizationData
                {
                    astronautStyles = astronautStyles
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new AstronautStyleEntry
                        {
                            astronautName = pair.Key,
                            style = pair.Value.Clone()
                        }).ToList(),
                    placements = placements.Values
                        .OrderBy(entry => entry.key, StringComparer.Ordinal)
                        .Select(entry => new FlagPlacementEntry
                        {
                            key = entry.key,
                            astronautName = entry.astronautName,
                            style = entry.style.Clone()
                        }).ToList()
                };
                File.WriteAllText(ConfigPath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.Log("[AstronautMod] Could not save custom flag settings: " + e.Message);
            }
        }
    }
}
