using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SFS.Input;
using SFS.UI;
using SFS.World;
using UnityEngine;

namespace AstronautMod
{
    // 按宇航员分别保存的外观配置（座椅贴图 + EVA 贴图）。
// 入口：Hub → Astronauts → 点选某个宇航员 → Customize Texture。
// 每人独立一份 Style，配置写入 persistentDataPath/AstronautMod_appearance.json。
//
// 为什么放在 Hub 而不是模组设置页：外观本质上是「这个人的形象」，
// 和旗帜自定义一样属于按人配置；模组设置页只放全局性的东西（EVA 参数、旗帜版本等）。
    public static class CrewAppearance
    {
        private const string ConfigFileName = "AstronautMod_appearance.json";

        // 一个人的外观：座椅贴图 + EVA 贴图，各自独立。
        [Serializable]
        public class Style
        {
            public string astronautName;

            // 坐在座椅上时的贴图（0=原生 1=整体贴图）
            public int mode = SeatAppearance.ModeNative;
            public string textureFile = "";

            // EVA（出舱）时的贴图，0=原生 1=整体贴图
            public int evaMode = 0;
            public string evaTextureFile = "";

            public Style Clone()
            {
                return new Style
                {
                    astronautName = astronautName,
                    mode = mode,
                    textureFile = textureFile ?? "",
                    evaMode = evaMode,
                    evaTextureFile = evaTextureFile ?? ""
                };
            }

            // 座椅上是否有自定义贴图。
            public bool IsCustom()
            {
                return mode == SeatAppearance.ModeWhole &&
                       !string.IsNullOrWhiteSpace(textureFile);
            }

            // EVA 是否有自定义贴图。
            public bool HasEvaTexture()
            {
                return evaMode == 1 && !string.IsNullOrWhiteSpace(evaTextureFile);
            }

            // 任一状态有自定义贴图就需要落盘。
            public bool AnyCustom()
            {
                return IsCustom() || HasEvaTexture();
            }
        }

        [Serializable]
        private class ConfigData
        {
            public List<Style> entries = new List<Style>();
        }

        private static readonly Dictionary<string, Style> styles =
            new Dictionary<string, Style>(StringComparer.Ordinal);

        private static bool initialized;

        public static string TexturesDirectory => AstronautTuning.TexturesDirectory;

        private static string ConfigPath
        {
            get { return Path.Combine(Application.persistentDataPath, ConfigFileName); }
        }

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Load();
        }

        // ---------------------------------------------------------------- 读写

        // 取某人的外观设置；没有人配置或名字为空时返回 null（= 原生）。
        public static Style GetForAstronaut(string astronautName)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(astronautName)) return null;
            if (!styles.TryGetValue(astronautName, out Style s) || s == null) return null;
            return s.Clone();
        }

        public static void Set(string astronautName, Style style)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(astronautName)) return;

            if (style == null || !style.AnyCustom())
            {
                styles.Remove(astronautName);
            }
            else
            {
                Style clone = style.Clone();
                clone.astronautName = astronautName;
                styles[astronautName] = clone;
            }
            Save();
            // 立刻刷新座椅模型与已在舱外的 EVA
            SeatAppearance.Invalidate();
            SeatAppearance.ApplyAll();
            ApplyEvaTextures();
            ModLogger.Info("Appearance updated: " + astronautName +
                " seat=" + (style != null && style.IsCustom() ? style.textureFile : "native") +
                " eva=" + (style != null && style.HasEvaTexture() ? style.evaTextureFile : "native"));
        }

        // 把 EVA 贴图应用到当前舱外的所有 EVA 宇航员（按名字匹配）。
        private static void ApplyEvaTextures()
        {
            try
            {
                if (AstronautManager.main == null) return;
                foreach (Astronaut_EVA eva in AstronautManager.main.eva)
                {
                    if (eva == null) continue;
                    // 注意：eva.astronaut 是 WorldSave.Astronauts.Data（直接对象，非引用类型）
                    string who = eva.astronaut?.astronautName;
                    if (string.IsNullOrEmpty(who)) continue;
                    Style s = GetForAstronaut(who);
                    if (s == null || !s.HasEvaTexture()) continue;
                    AstronautTuning.ApplyEvaTexture(eva, s.evaTextureFile);
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Apply EVA textures", e);
            }
        }

        // ---------------------------------------------------------------- Hub 菜单

        // 区分是给座椅还是 EVA 选贴图。
        private enum Kind { Seat, Eva }

        // Hub 里「Customize Texture」按钮的入口：同时管座椅与 EVA 两种贴图。
        public static void OpenStyleMenu(string astronautName, Action onChanged)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(astronautName)) return;

            Style current = GetForAstronaut(astronautName) ?? new Style { astronautName = astronautName };

            var elements = new List<MenuElement>();
            SizeSyncerBuilder.Carrier carrier;
            elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));
            elements.Add(TextBuilder.CreateText(() => "Textures for " + astronautName));
            elements.Add(TextBuilder.CreateText(() =>
                "Seat = appearance while seated. EVA = appearance outside the rocket."));

            elements.Add(ElementGenerator.VerticalSpace(10));

            // ---- 座椅贴图 ----
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Seat: " + (current.IsCustom() ? current.textureFile : "native"),
                () => OpenImagePicker(astronautName, Kind.Seat, onChanged),
                CloseMode.Stack));
            if (current.IsCustom())
            {
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "Seat: use native",
                    () =>
                    {
                        Style s = GetForAstronaut(astronautName) ??
                                  new Style { astronautName = astronautName };
                        s.mode = SeatAppearance.ModeNative;
                        s.textureFile = "";
                        Set(astronautName, s);
                        onChanged?.Invoke();
                        OpenStyleMenu(astronautName, onChanged);
                    },
                    CloseMode.Stack));
            }

            elements.Add(ElementGenerator.VerticalSpace(10));

            // ---- EVA 贴图 ----
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "EVA: " + (current.HasEvaTexture() ? current.evaTextureFile : "native"),
                () => OpenImagePicker(astronautName, Kind.Eva, onChanged),
                CloseMode.Stack));
            if (current.HasEvaTexture())
            {
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => "EVA: use native",
                    () =>
                    {
                        Style s = GetForAstronaut(astronautName) ??
                                  new Style { astronautName = astronautName };
                        s.evaMode = 0;
                        s.evaTextureFile = "";
                        Set(astronautName, s);
                        onChanged?.Invoke();
                        OpenStyleMenu(astronautName, onChanged);
                    },
                    CloseMode.Stack));
            }

            elements.Add(ElementGenerator.VerticalSpace(10));
            elements.Add(TextBuilder.CreateText(() => "Image folder: Mods/AstronautMod/Textures"));
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Back",
                () => { onChanged?.Invoke(); },
                CloseMode.Stack));

            MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Stack, elements.ToArray());
        }

        // 贴图选择器：列出 Textures 目录下的 PNG/JPG，支持翻页。 kind 决定写入座椅贴图还是 EVA 贴图。
        private static void OpenImagePicker(string astronautName, Kind kind,
            Action onChanged, int page = 0)
        {
            Initialize();
            List<string> files = new List<string>();
            try
            {
                Directory.CreateDirectory(TexturesDirectory);
                foreach (string file in Directory.GetFiles(TexturesDirectory))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                        files.Add(Path.GetFileName(file));
                }
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Crew appearance picker", e);
            }

            var elements = new List<MenuElement>();
            SizeSyncerBuilder.Carrier carrier;
            elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));
            elements.Add(TextBuilder.CreateText(() =>
                "Pick an image for " + astronautName +
                (kind == Kind.Eva ? " (EVA suit)" : " (seat)")));
            if (files.Count == 0)
                elements.Add(TextBuilder.CreateText(() =>
                    "No PNG/JPG found.\nPut images into Mods/AstronautMod/Textures"));

            const int perPage = 8;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(files.Count / (float)perPage));
            page = Mathf.Clamp(page, 0, totalPages - 1);
            Style current = GetForAstronaut(astronautName);

            foreach (string file in files.Skip(page * perPage).Take(perPage))
            {
                string captured = file;
                bool isCurrent = current != null &&
                    (kind == Kind.Eva ? current.evaTextureFile == captured
                                      : current.textureFile == captured);
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => (isCurrent ? "> " : "") + captured,
                    () =>
                    {
                        // 只改对应的那一种，另一半保留
                        Style s = GetForAstronaut(astronautName) ??
                                  new Style { astronautName = astronautName };
                        if (kind == Kind.Eva)
                        {
                            s.evaMode = 1;
                            s.evaTextureFile = captured;
                        }
                        else
                        {
                            s.mode = SeatAppearance.ModeWhole;
                            s.textureFile = captured;
                        }
                        Set(astronautName, s);
                        onChanged?.Invoke();
                        OpenStyleMenu(astronautName, onChanged);
                    },
                    CloseMode.Current));
            }

            if (totalPages > 1)
            {
                if (page > 0)
                {
                    int previous = page - 1;
                    elements.Add(ButtonBuilder.CreateButton(carrier,
                        () => "< Page " + page + " / " + totalPages,
                        () => OpenImagePicker(astronautName, kind, onChanged, previous),
                        CloseMode.Current));
                }
                if (page < totalPages - 1)
                {
                    int next = page + 1;
                    elements.Add(ButtonBuilder.CreateButton(carrier,
                        () => "Page " + (page + 2) + " / " + totalPages + " >",
                        () => OpenImagePicker(astronautName, kind, onChanged, next),
                        CloseMode.Current));
                }
            }

            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Back",
                () => OpenStyleMenu(astronautName, onChanged),
                CloseMode.Current));

            MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Current, elements.ToArray());
        }

        // ---------------------------------------------------------------- 持久化

        private static void Load()
        {
            try
            {
                styles.Clear();
                if (!File.Exists(ConfigPath)) return;

                // 必须用 Newtonsoft 而不是 JsonUtility：Unity 的 JsonUtility 不支持
                // 嵌套类（nested type），而 Style/ConfigData 都定义在本类内部，
                // 用它序列化会得到空对象 {}，重启后配置就全丢了。
                ConfigData data = JsonConvert.DeserializeObject<ConfigData>(
                    File.ReadAllText(ConfigPath));
                if (data?.entries == null)
                {
                    ModLogger.Warning("Appearance config present but empty: " + ConfigPath);
                    return;
                }

                int loaded = 0;
                foreach (Style entry in data.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.astronautName)) continue;
                    styles[entry.astronautName] = entry.Clone();
                    loaded++;
                }
                ModLogger.Info("Appearance config loaded: " + loaded + " astronaut(s)");
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Crew appearance load", e);
            }
        }

        private static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                ConfigData data = new ConfigData
                {
                    entries = styles.Values
                        .Where(s => s != null && s.AnyCustom())
                        .OrderBy(s => s.astronautName, StringComparer.Ordinal)
                        .Select(s => s.Clone())
                        .ToList()
                };
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                ModLogger.Info("Appearance config saved: " + data.entries.Count + " astronaut(s)");
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Crew appearance save", e);
            }
        }
    }
}