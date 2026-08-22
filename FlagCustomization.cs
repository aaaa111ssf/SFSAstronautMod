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
        private static readonly Dictionary<int, SpriteRenderer> artworkRenderers =
            new Dictionary<int, SpriteRenderer>();
        private static readonly Dictionary<int, SpriteMask> artworkMasks =
            new Dictionary<int, SpriteMask>();
        private static readonly Dictionary<int, SpriteRenderer> poleRenderers =
            new Dictionary<int, SpriteRenderer>();
        private static Sprite flagFaceMaskSprite;

        private static string pendingAstronautName;
        private static FlagStyle pendingStyle;
        private static float pendingPlantTime = -1f;
        private static bool initialized;

        public static string FlagsDirectory
        {
            get
            {
                try
                {
                    string dataPath = Application.dataPath;
                    DirectoryInfo gameDirectory = string.IsNullOrWhiteSpace(dataPath)
                        ? null
                        : Directory.GetParent(dataPath);
                    if (gameDirectory != null)
                    {
                        return Path.Combine(gameDirectory.FullName, "Mods", "AstronautMod", "Flags");
                    }
                }
                catch { }
                return LegacyFlagsDirectory;
            }
        }

        private static string LegacyFlagsDirectory
        {
            get { return Path.Combine(Application.persistentDataPath, "AstronautMod", "Flags"); }
        }

        private static string ConfigPath
        {
            get { return Path.Combine(Application.persistentDataPath, ConfigFileName); }
        }

        public static void Initialize()
        {
            if (!initialized)
            {
                initialized = true;
                Load();
            }
            EnsureFlagsDirectory();
        }

        private static void EnsureFlagsDirectory()
        {
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

            // Keep native flags unchanged; custom flags use a separate face and pole.
            renderer.sprite = originalSprites[id];
            renderer.color = originalColors[id];
            Sprite customFace = null;
            Color customTint = ParseColor(style.colorHex, Color.white);

            if (!string.IsNullOrWhiteSpace(style.imageFile))
                customFace = LoadCustomSprite(style.imageFile);
            if (customFace == null && style.IsCustom())
                customFace = GetFlagFaceMaskSprite();

            if (customFace != null)
            {
                renderer.enabled = false;
                ConfigureArtworkRenderer(renderer, customFace, customTint);
                return;
            }

            // If an image file cannot be decoded, do not leave an invisible flag behind.
            RemoveArtworkRenderer(id);
            renderer.enabled = true;
            renderer.color = ParseColor(style.colorHex, originalColors[id]);
        }

        private static void RestoreStyle(Flag flag)
        {
            SpriteRenderer renderer = FindFlagRenderer(flag);
            if (renderer == null) return;

            int id = renderer.GetInstanceID();
            RemoveArtworkRenderer(id);
            renderer.enabled = true;
            if (originalSprites.TryGetValue(id, out Sprite originalSprite))
                renderer.sprite = originalSprite;
            if (originalColors.TryGetValue(id, out Color originalColor))
                renderer.color = originalColor;
        }

        private static void ConfigureArtworkRenderer(SpriteRenderer frameRenderer, Sprite image, Color tint)
        {
            int id = frameRenderer.GetInstanceID();
            if (!artworkRenderers.TryGetValue(id, out SpriteRenderer artwork) || artwork == null)
            {
                GameObject artworkObject = new GameObject("AstronautMod_FlagArtwork");
                artworkObject.transform.SetParent(frameRenderer.transform, false);
                artwork = artworkObject.AddComponent<SpriteRenderer>();
                artworkRenderers[id] = artwork;
            }

            artwork.sprite = image;
            artwork.color = tint;
            artwork.sortingLayerID = frameRenderer.sortingLayerID;
            artwork.sortingOrder = frameRenderer.sortingOrder + 2;

            Bounds frameBounds = frameRenderer.sprite.bounds;
            float frameWidth = frameBounds.size.x;
            float frameHeight = frameBounds.size.y;
            float imageWidth = Mathf.Max(0.0001f, image.bounds.size.x);
            float imageHeight = Mathf.Max(0.0001f, image.bounds.size.y);

            // The upper Icon Flag area is the cloth face.
            float availableWidth = frameWidth * 0.98f;
            float availableHeight = frameHeight * 0.34f;
            bool preserveOutline = HasTransparentOutline(image) ||
                Mathf.Abs((imageWidth / imageHeight) - (availableWidth / availableHeight)) > 0.20f;
            float uniformScale = preserveOutline
                ? Mathf.Min(availableWidth / imageWidth, availableHeight / imageHeight)
                : Mathf.Max(availableWidth / imageWidth, availableHeight / imageHeight);
            // Preserve the original image orientation during flag animation.
            FlagArtworkOrientation orientation = artwork.GetComponent<FlagArtworkOrientation>();
            if (orientation == null) orientation = artwork.gameObject.AddComponent<FlagArtworkOrientation>();
            orientation.SetBaseScale(uniformScale, uniformScale);
            Vector3 facePosition = new Vector3(
                frameBounds.center.x,
                frameBounds.max.y - availableHeight * 0.5f,
                0f);
            artwork.transform.localPosition = facePosition;
            artwork.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            ConfigureFaceMask(id, frameRenderer, artwork.sortingOrder, availableWidth,
                availableHeight, facePosition);
            ConfigureBlackPole(id, frameRenderer, frameBounds, availableHeight,
                facePosition);
            artwork.enabled = true;
        }

        private static void RemoveArtworkRenderer(int frameRendererId)
        {
            if (artworkMasks.TryGetValue(frameRendererId, out SpriteMask mask))
            {
                artworkMasks.Remove(frameRendererId);
                if (mask != null) UnityEngine.Object.Destroy(mask.gameObject);
            }
            if (poleRenderers.TryGetValue(frameRendererId, out SpriteRenderer pole))
            {
                poleRenderers.Remove(frameRendererId);
                if (pole != null) UnityEngine.Object.Destroy(pole.gameObject);
            }
            if (!artworkRenderers.TryGetValue(frameRendererId, out SpriteRenderer artwork)) return;
            artworkRenderers.Remove(frameRendererId);
            if (artwork != null) UnityEngine.Object.Destroy(artwork.gameObject);
        }

        private static void ConfigureFaceMask(int rendererId, SpriteRenderer frameRenderer,
            int artworkSortingOrder, float width, float height, Vector3 localPosition)
        {
            if (!artworkMasks.TryGetValue(rendererId, out SpriteMask mask) || mask == null)
            {
                GameObject maskObject = new GameObject("AstronautMod_FlagFaceMask");
                maskObject.transform.SetParent(frameRenderer.transform, false);
                mask = maskObject.AddComponent<SpriteMask>();
                artworkMasks[rendererId] = mask;
            }

            mask.sprite = GetFlagFaceMaskSprite();
            mask.isCustomRangeActive = true;
            mask.backSortingLayerID = frameRenderer.sortingLayerID;
            mask.frontSortingLayerID = frameRenderer.sortingLayerID;
            mask.backSortingOrder = artworkSortingOrder;
            mask.frontSortingOrder = artworkSortingOrder;
            mask.transform.localPosition = localPosition;
            mask.transform.localScale = new Vector3(width, height, 1f);
        }

        private static void ConfigureBlackPole(int rendererId, SpriteRenderer frameRenderer,
            Bounds frameBounds, float faceHeight, Vector3 facePosition)
        {
            if (!poleRenderers.TryGetValue(rendererId, out SpriteRenderer pole) || pole == null)
            {
                GameObject poleObject = new GameObject("AstronautMod_BlackFlagPole");
                poleObject.transform.SetParent(frameRenderer.transform, false);
                pole = poleObject.AddComponent<SpriteRenderer>();
                poleRenderers[rendererId] = pole;
            }

            // Keep the black pole on the native left edge; the custom face remains unframed.
            float poleWidth = Mathf.Max(0.015f, frameBounds.size.x * 0.07f);
            // Span the full flag height so the pole connects to the face.
            float poleTop = frameBounds.max.y;
            float poleBottom = frameBounds.min.y;
            float poleHeight = Mathf.Max(0.01f, poleTop - poleBottom);
            float poleCenterY = (poleBottom + poleTop) * 0.5f;
            float poleCenterX = facePosition.x - (frameBounds.size.x * 0.49f) +
                poleWidth * 0.55f;

            pole.sprite = GetFlagFaceMaskSprite();
            pole.color = Color.black;
            pole.sortingLayerID = frameRenderer.sortingLayerID;
            pole.sortingOrder = frameRenderer.sortingOrder + 1;
            pole.maskInteraction = SpriteMaskInteraction.None;
            pole.transform.localPosition = new Vector3(poleCenterX, poleCenterY, 0f);
            pole.transform.localScale = new Vector3(poleWidth, poleHeight, 1f);
        }

        private static Sprite GetFlagFaceMaskSprite()
        {
            if (flagFaceMaskSprite != null) return flagFaceMaskSprite;
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            texture.name = "AstronautMod_FlagFaceMask";
            flagFaceMaskSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);
            return flagFaceMaskSprite;
        }

        private static bool HasTransparentOutline(Sprite sprite)
        {
            try
            {
                Texture2D texture = sprite == null ? null : sprite.texture;
                if (texture == null) return false;
                Color32[] pixels = texture.GetPixels32();
                if (pixels == null || pixels.Length == 0) return false;
                int transparent = 0;
                foreach (Color32 pixel in pixels)
                {
                    if (pixel.a < 245) transparent++;
                }
                return transparent > pixels.Length / 100;
            }
            catch
            {
                // Failing safe preserves a normal rectangular image rather than distorting it.
                return false;
            }
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
                string path = ResolveFlagImagePath(fileName);
                if (string.IsNullOrWhiteSpace(path))
                {
                    Debug.Log("[AstronautMod] Custom flag image was not found in: " + FlagsDirectory);
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

        private static string ResolveFlagImagePath(string fileName)
        {
            string primaryPath = Path.Combine(FlagsDirectory, fileName);
            if (File.Exists(primaryPath)) return primaryPath;

            // Keep reading existing files created by older builds, but create new folders only
            // in the game Mod/AstronautMod/Flags location requested by the user.
            string legacyPath = Path.Combine(LegacyFlagsDirectory, fileName);
            return File.Exists(legacyPath) ? legacyPath : null;
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

    /// <summary>
    /// Cancels the native Holder's left/right flip for the inserted flag artwork only.
    /// The pole and original frame keep their native direction while the artwork stays
    /// in its original national-flag orientation.
    /// </summary>
    public sealed class FlagArtworkOrientation : MonoBehaviour
    {
        private float baseScaleX = 1f;
        private float baseScaleY = 1f;

        public void SetBaseScale(float x, float y)
        {
            baseScaleX = Mathf.Max(0.0001f, x);
            baseScaleY = Mathf.Max(0.0001f, y);
            ApplyOrientation();
        }

        private void OnEnable()
        {
            ApplyOrientation();
        }

        private void LateUpdate()
        {
            ApplyOrientation();
        }

        private void ApplyOrientation()
        {
            Transform parent = transform.parent;
            float parentX = parent == null ? 1f : parent.lossyScale.x;
            float compensation = Mathf.Abs(parentX) < 0.0001f ? 1f : Mathf.Sign(parentX);
            transform.localScale = new Vector3(baseScaleX * compensation, baseScaleY, 1f);
        }
    }
}
