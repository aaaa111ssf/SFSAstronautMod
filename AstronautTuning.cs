using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SFS;
using SFS.Input;
using SFS.UI;
using SFS.UI.ModGUI;
using SFS.World;
using TMPro;
using UITools;
using UnityEngine;
using UnityEngine.UI;
using Type = SFS.UI.ModGUI.Type;
using Button = SFS.UI.ModGUI.Button;
using Object = UnityEngine.Object;

namespace AstronautMod
{
    // EVA 宇航员可调参数：模型大小 / 浮力(重力系数) / 跳跃高度 / 移动速度 / 燃料消耗 / 出舱满燃料 / EVA 外观贴图。设置页使用 UITools 的可关闭可滚动窗口（同 PartEd
    // itor）。
    public static class AstronautTuning
    {
        public static string TexturesDirectory
        {
            get
            {
                try
                {
                    string dataPath = Application.dataPath;
                    var gameDirectory = string.IsNullOrWhiteSpace(dataPath)
                        ? null
                        : Directory.GetParent(dataPath);
                    if (gameDirectory != null)
                        return Path.Combine(gameDirectory.FullName, "Mods", "AstronautMod", "Textures");
                }
                catch { }
                return Path.Combine(Application.persistentDataPath, "AstronautMod", "Textures");
            }
        }

        static ModSettings.Data Settings =>
            ModSettings.main != null ? ModSettings.main.settings : null;

        // prefab 原始曲线（只取一次，之后按参数缩放，避免重复应用累积）
        static AnimationCurve baseJump, baseSpeed;
        static bool baseCaptured;

        static readonly Dictionary<int, Vector3> baseSpriteScale = new Dictionary<int, Vector3>();
        // 整套贴图：隐藏原部件，用一张图覆盖整个 EVA 身体+头盔
        static readonly Dictionary<int, SpriteRenderer> suitOverlays = new Dictionary<int, SpriteRenderer>();
        static readonly Dictionary<int, List<SpriteRenderer>> partCache = new Dictionary<int, List<SpriteRenderer>>();
        static readonly Dictionary<string, Sprite> textureCache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- 应用

        public static void ApplyAll()
        {
            try
            {
                if (AstronautManager.main == null) return;
                foreach (Astronaut_EVA eva in AstronautManager.main.eva)
                    ApplyTo(eva);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Astronaut tuning apply", e);
            }
        }

        public static void ApplyTo(Astronaut_EVA eva)
        {
            if (eva == null) return;
            ModSettings.Data s = Settings;
            if (s == null) return;

            EnsureBaseCurves();

            if (baseCaptured)
            {
                if (baseJump != null)
                    eva.jumpVelocity = Scaled(baseJump, Mathf.Clamp(s.evaJumpScale, 0.2f, 4f));
                if (baseSpeed != null)
                    eva.maxSpeed = Scaled(baseSpeed, Mathf.Clamp(s.evaSpeedScale, 0.2f, 4f));
            }

            ApplyModelScale(eva);
            ApplyTexture(eva);
            ApplyPhysics(eva);
        }

        // 刚体属性：原生 Astronaut_EVA.Start 里写死 mass=0.2 / centerOfMass=(0,1.1)，
        // 这里在建/每次应用时按设置覆盖（Start 只在生成时跑一次，运行中改设置也要即时生效）。
        public static void ApplyPhysics(Astronaut_EVA eva)
        {
            if (eva == null || eva.rb2d == null) return;
            ModSettings.Data s = Settings;
            if (s == null) return;
            try
            {
                eva.rb2d.mass = Mathf.Clamp(s.evaMass, 0.05f, 2f);
                eva.rb2d.centerOfMass = new Vector2(0f, Mathf.Clamp(s.evaCenterOfMass, 0.2f, 2f));
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA physics apply", e);
            }
        }

        // 碰撞承受倍率：越高越抗摔（0.2~3，1=原生）。
        public static float ImpactTolerance
        {
            get
            {
                ModSettings.Data s = Settings;
                return s == null ? 1f : Mathf.Clamp(s.evaImpactTolerance, 0.2f, 3f);
            }
        }

        public static float WaterBuoyancy
        {
            get
            {
                ModSettings.Data s = Settings;
                return s == null ? 1f : Mathf.Clamp(s.evaWaterBuoyancy, 0f, 3f);
            }
        }

        public static float WaterDrag
        {
            get
            {
                ModSettings.Data s = Settings;
                return s == null ? 1f : Mathf.Clamp(s.evaWaterDrag, 0f, 3f);
            }
        }

        static void EnsureBaseCurves()
        {
            if (baseCaptured) return;
            try
            {
                var prefab = AstronautManager.main != null ? AstronautManager.main.astronautPrefab : null;
                if (prefab == null) return;
                baseJump = prefab.jumpVelocity;
                baseSpeed = prefab.maxSpeed;
                baseCaptured = baseJump != null || baseSpeed != null;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Astronaut tuning base curves", e);
            }
        }

        static AnimationCurve Scaled(AnimationCurve src, float factor)
        {
            if (src == null) return null;
            Keyframe[] srcKeys = src.keys;
            var keys = new Keyframe[srcKeys.Length];
            for (int i = 0; i < srcKeys.Length; i++)
            {
                Keyframe k = srcKeys[i];
                k.value *= factor;
                k.inTangent *= factor;
                k.outTangent *= factor;
                keys[i] = k;
            }
            var curve = new AnimationCurve(keys);
            curve.preWrapMode = src.preWrapMode;
            curve.postWrapMode = src.postWrapMode;
            return curve;
        }

        // EVA 每帧只改 sprite.localScale.x 的符号（保留绝对值），所以按基准值重算即可
        static void ApplyModelScale(Astronaut_EVA eva)
        {
            if (eva.sprite == null) return;
            int id = eva.GetInstanceID();
            if (!baseSpriteScale.TryGetValue(id, out Vector3 b))
            {
                b = eva.sprite.localScale;
                baseSpriteScale[id] = b;
            }
            float sign = eva.sprite.localScale.x < 0f ? -1f : 1f;
            float f = Mathf.Clamp(Settings.evaModelScale, 0.5f, 2.5f);
            eva.sprite.localScale = new Vector3(b.x * f * sign, b.y * f, b.z * f);
        }

        // 身体与头盔用同一张图：隐藏所有原生精灵，按整体包围盒铺一张自定义贴图
        const string OverlayName = "AstronautMod_EVASuit";

        // EVA 宇航员身上的贴图。与 SeatAppearance 同思路：不改原生 sprite， 只是禁用原生渲染器 + 加一张覆盖层，因此"还原"只需重新启用 + 销毁覆盖层， 不存在"缓存里存
        // 的是自定义贴图导致切回原生去不掉"的问题。 贴图按宇航员名字取（CrewAppearance 里按人配置），fileName 为空 = 原生外观。
        public static void ApplyEvaTexture(Astronaut_EVA eva, string fileName)
        {
            if (eva == null || eva.sprite == null) return;

            Transform model = eva.sprite.transform;
            int evaId = eva.GetInstanceID();

            if (!partCache.TryGetValue(evaId, out List<SpriteRenderer> parts) || parts == null)
            {
                parts = new List<SpriteRenderer>();
                foreach (SpriteRenderer sr in model.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr == null) continue;
                    if (sr.gameObject.name == OverlayName) continue;
                    if (sr.gameObject.name.StartsWith("AstronautMod_")) continue;
                    parts.Add(sr);
                }
                partCache[evaId] = parts;
            }
            if (parts.Count == 0) return;

            // 始终先恢复原生（贴图为空时这就是最终状态）
            RestoreParts(parts);

            Sprite custom = string.IsNullOrWhiteSpace(fileName) ? null : LoadTexture(fileName);
            if (custom == null)
            {
                DestroySuitOverlay(evaId);
                return;
            }

            // 整体包围盒（世界空间）→ 覆盖渲染器的本地位置与缩放
            Bounds bounds = parts[0].bounds;
            for (int i = 1; i < parts.Count; i++)
                bounds.Encapsulate(parts[i].bounds);

            Vector3 localCenter = model.InverseTransformPoint(bounds.center);
            Vector3 lossy = model.lossyScale;
            Vector3 localSize = new Vector3(
                Mathf.Abs(bounds.size.x / (Mathf.Abs(lossy.x) < 0.0001f ? 1f : lossy.x)),
                Mathf.Abs(bounds.size.y / (Mathf.Abs(lossy.y) < 0.0001f ? 1f : lossy.y)),
                1f);

            int order = 0;
            foreach (SpriteRenderer sr in parts)
                if (sr.sortingOrder > order) order = sr.sortingOrder;

            if (!suitOverlays.TryGetValue(evaId, out SpriteRenderer overlay) || overlay == null)
            {
                GameObject go = new GameObject(OverlayName);
                go.transform.SetParent(model, false);
                overlay = go.AddComponent<SpriteRenderer>();
                suitOverlays[evaId] = overlay;
            }

            overlay.sprite = custom;
            overlay.sortingOrder = order + 1;
            overlay.color = Color.white;
            overlay.transform.localPosition = localCenter;
            Vector2 spriteSize = custom.bounds.size;
            overlay.transform.localScale = new Vector3(
                spriteSize.x < 0.0001f ? 1f : localSize.x / spriteSize.x,
                spriteSize.y < 0.0001f ? 1f : localSize.y / spriteSize.y,
                1f);
            overlay.enabled = true;

            foreach (SpriteRenderer sr in parts) sr.enabled = false;
        }

        static void DestroySuitOverlay(int evaId)
        {
            if (!suitOverlays.TryGetValue(evaId, out SpriteRenderer overlay)) return;
            suitOverlays.Remove(evaId);
            if (overlay == null) return;
            // 先禁用再销毁：Destroy 延迟到帧末，同一帧内还会被遍历到
            GameObject go = overlay.gameObject;
            if (go != null)
            {
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
            }
        }

        // 按该 EVA 所属宇航员的配置应用贴图。
        static void ApplyTexture(Astronaut_EVA eva)
        {
            try
            {
                // 注意：eva.astronaut 是 WorldSave.Astronauts.Data（直接对象，非引用类型）
                string who = eva?.astronaut?.astronautName;
                if (string.IsNullOrEmpty(who))
                {
                    // 没有对应宇航员：恢复原生
                    if (eva != null && eva.sprite != null)
                        ApplyEvaTexture(eva, null);
                    return;
                }
                CrewAppearance.Style style = CrewAppearance.GetForAstronaut(who);
                ApplyEvaTexture(eva, style != null && style.HasEvaTexture()
                    ? style.evaTextureFile
                    : null);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA texture apply", e);
            }
        }

        static void RestoreParts(List<SpriteRenderer> parts)
        {
            if (parts == null) return;
            // 只恢复"启用"状态：贴图从未被改写过
            foreach (SpriteRenderer sr in parts)
            {
                if (sr != null && !sr.enabled) sr.enabled = true;
            }
        }

        static Sprite LoadTexture(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            if (textureCache.TryGetValue(fileName, out Sprite cached) && cached != null)
                return cached;

            try
            {
                string path = Path.Combine(TexturesDirectory, fileName);
                if (!File.Exists(path)) return null;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var conversion = System.Type.GetType(
                    "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var load = conversion?.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
                if (load == null) return null;
                object ok = load.Invoke(null, new object[] { texture, File.ReadAllBytes(path), false });
                if (!(ok is bool) || !(bool)ok) return null;

                texture.name = "AstronautMod_EVA_" + fileName;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                Sprite sprite = Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = texture.name;
                textureCache[fileName] = sprite;
                return sprite;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA texture load", e);
                return null;
            }
        }

        // ---------------------------------------------------------------- 设置页（游戏设置 → Mods Settings → Astronaut）

        // UITools 初始化晚于本模组时首次调用会失败，UpdateDriver 每帧重试直到成功
        static bool registered;
        static int registerAttempts;

        public static void TryRegisterSettingsPage()
        {
            if (registered) return;
            if (registerAttempts > 600) return; // 约 10 秒仍未就绪则放弃
            registerAttempts++;
            try
            {
                registered = true;
                // 左侧只留一个分类标题「Astronaut」（模组名），下面两个按钮 Attribute / Sticker。
                // UITools 每次 Add 会新建一个分类标题块，所以只调一次、给两个按钮 ——
                // 既避免六个子分类把左栏撑得又高又挤，也不留多余的分组标题。
                ConfigurationMenu.Add("Astronaut",
                    new (string, Func<Transform, GameObject>)[]
                    {
                        ("Attribute", CreateAttributePage),
                        ("Sticker", CreateStickerPage),
                    });
            }
            catch
            {
                registered = false; // UITools 尚未初始化，下一帧重试
            }
        }

        // 统一字号：分区标题 / 行标题 / 提示
        const float FontSection = 27f;
        const float FontRow = 22f;
        const float FontHint = 18f;
        const int RowHeight = 42;
        // 内容区总高（UITools 固定 height-100 = 1100）。行高乘以数量后必须留得下，
        // 否则整块超出窗口被裁（Seat 页行数最多，按它算预算）。
        const int ContentHeight = 1100;
        // 宽度按设置窗口内容区动态计算；写死会超出内容区被窗口 Mask 裁掉左边
        static int contentWidth = 500;
        static int labelWidth = 290;
        static int inputWidth = 200;

        // ---------------------------------------------------------------- 两个分类页
        // 左侧分类按钮只保留 Attribute 与 Sticker 两个；Physique / Fuel / Physics /
        // Look / Seat / Flag 全部作为页内区块（标题）呈现，靠滚动查看 ——
        // 六个分类按钮会把左侧栏撑得又高又挤，非常占空间。

        static GameObject CreateAttributePage(Transform parent)
        {
            ModSettings.Data s = Settings;
            if (s == null) return null;
            return BuildPage(parent, box =>
            {
                AddSection(box, "EVA Physique");
                AddNumberRow(box, "Model size", s.evaModelScale, 0.1f, 0.5f, 2.5f, v => s.evaModelScale = v);
                AddNumberRow(box, "Buoyancy (gravity)", s.evaBuoyancy, 0.05f, 0f, 1.5f, v => s.evaBuoyancy = v);
                AddNumberRow(box, "Jump height", s.evaJumpScale, 0.1f, 0.2f, 4f, v => s.evaJumpScale = v);
                AddNumberRow(box, "Move speed", s.evaSpeedScale, 0.1f, 0.2f, 4f, v => s.evaSpeedScale = v);
                AddHint(box, "0 buoyancy = float like in vacuum, 1 = normal gravity");

                AddSection(box, "Fuel");
                AddNumberRow(box, "Fuel drain rate", s.evaFuelDrain, 0.1f, 0f, 5f, v => s.evaFuelDrain = v);
                AddToggleRow(box, "Full fuel on EVA exit",
                    () => Settings.evaFullFuelOnEVA, v => Settings.evaFullFuelOnEVA = v);
                AddHint(box, "Drain rate 0 = unlimited fuel, 1 = vanilla consumption");

                AddSection(box, "Collision");
                AddNumberRow(box, "Body mass", s.evaMass, 0.05f, 0.05f, 2f, v => s.evaMass = v);
                AddNumberRow(box, "Center of mass", s.evaCenterOfMass, 0.05f, 0.2f, 2f,
                    v => s.evaCenterOfMass = v);
                AddNumberRow(box, "Impact tolerance", s.evaImpactTolerance, 0.1f, 0.2f, 3f,
                    v => s.evaImpactTolerance = v);
                AddHint(box, "Higher mass = harder to push. Higher impact tolerance = survives faster landings.");

                AddSection(box, "Water");
                AddNumberRow(box, "Water buoyancy", s.evaWaterBuoyancy, 0.05f, 0f, 3f,
                    v => s.evaWaterBuoyancy = v);
                AddNumberRow(box, "Water drag", s.evaWaterDrag, 0.05f, 0f, 3f, v => s.evaWaterDrag = v);
                AddHint(box, "Applies when the astronaut is submerged in water.");
            });
        }

        static GameObject CreateStickerPage(Transform parent)
        {
            if (Settings == null) return null;
            return BuildPage(parent, box =>
            {
                // 座椅贴图与 EVA 贴图都按宇航员单独配置，入口在 Hub，这里只留指引。
                AddSection(box, "Astronaut Textures");
                AddHint(box, "Seat and EVA textures are configured per astronaut.");
                AddHint(box, "Set them in the Hub: Astronauts > pick astronaut > Customize Texture.");

                FillFlagBlock(box);
            });
        }

        // Sticker 页里的「Flag Appearance」区块。
        static void FillFlagBlock(Box box)
        {
            AddSection(box, "Flag Appearance");
            AddCycleRow(box, "Flag version", FlagVersionName,
                () =>
                {
                    ModSettings.Data d = Settings;
                    if (d == null) return;
                    d.flagTextureVersion = d.flagTextureVersion == 0 ? 1 : 0;
                    SaveAndApply();
                });

            AddSection(box, FlagVersionDetailTitle);
            AddHint(box, FlagVersionDetailHint);

            AddSection(box, "Per-Astronaut Options");
            AddHint(box, "Colors, flag face image and pole settings are stored per astronaut.");
            AddHint(box, "Set them in the Hub: Astronauts > pick astronaut > Customize Flag.");
            AddHint(box, FlagPoleNote);
        }

        static string FlagVersionName()
        {
            ModSettings.Data s = Settings;
            return s != null && s.flagTextureVersion == 1 ? "New (whole flag)" : "Old (face + pole)";
        }

        static string FlagVersionDetailTitle
        {
            get
            {
                ModSettings.Data s = Settings;
                return s != null && s.flagTextureVersion == 1 ? "New Version" : "Old Version";
            }
        }

        static string FlagVersionDetailHint
        {
            get
            {
                ModSettings.Data s = Settings;
                bool isNew = s != null && s.flagTextureVersion == 1;
                return isNew
                    ? "One image covers the whole flag, pole included. " +
                      "The image is fitted to the native flag bounds, keeping its aspect ratio. " +
                      "No separate pole or mask is generated, and the pole color/width/height options do not apply."
                    : "The image is used as the flag face only. " +
                      "A pole is drawn separately and can be recolored, resized in width and height. " +
                      "Flag color presets and a color-only mode are also available.";
            }
        }

        static string FlagPoleNote =>
            "Switching versions redraws every planted flag immediately. " +
            "Both versions share the same per-astronaut flag configuration.";

        static GameObject BuildPage(Transform parent, Action<Box> fill)
        {
            Vector2Int size = ConfigurationMenu.ContentSize;
            // ContentSize 已经是「扣掉分类按钮列与主窗口 padding 后」的内容区尺寸（UITools 里
            // = width-260 / height-100）。这里只再减去自身 layout 的左右 padding(10+10)，
            // 不能再放宽上限 —— 放宽会让整块超出窗口、被窗口 Mask 从左边裁掉（曾出现的现象）。
            contentWidth = Mathf.Clamp(size.x - 24, 280, 560);
            labelWidth = Mathf.RoundToInt(contentWidth * 0.56f);
            inputWidth = contentWidth - labelWidth - 10;

            // 设置窗口本身没有滚动容器（UITools 的 ConfigurationWindow 是固定尺寸），
            // 所以给每个页面套一层原生 SFS.UI.ScrollElement：超出内容区时可滚轮/拖拽，
            // 不再被裁掉。结构：viewport(Mask, 固定高) → scroll(content, 可上下移动)。
            int viewportHeight = Mathf.Max(200, size.y - 10);

            GameObject viewport = new GameObject("AstronautMod_PageViewport");
            viewport.transform.SetParent(parent, false);
            RectTransform viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.sizeDelta = new Vector2(contentWidth, viewportHeight);
            viewport.AddComponent<CanvasRenderer>();
            var viewportImage = viewport.AddComponent<UnityEngine.UI.Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);   // 需可见才能作为 Mask graphic
            viewport.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;

            Box box = Builder.CreateBox(viewport.transform, contentWidth, 10);
            box.CreateLayoutGroup(Type.Vertical, spacing: 6f, padding: new RectOffset(10, 10, 8, 8));
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var scroll = box.gameObject.AddComponent<ScrollElement>();
            scroll.vertical = true;
            scroll.horizontal = false;
            scroll.clampType = ScrollElement.ClampType.Mask;
            scroll.startPivot = new Vector2(0.5f, 1f);   // 0,1 = 顶部对齐，打开时从最上开始
            scroll.backupPivot = new Vector2(0.5f, 1f);
            scroll.resetPositionAtEnable = true;
            scroll.momentumDecayTime = 0.1f;

            fill(box);
            LayoutRebuilder.ForceRebuildLayoutImmediate(box.gameObject.transform as RectTransform);
            // 把页面上所有按钮注册给 ScrollElement，按钮才会接收拖拽/滚轮并带动滚动。
            // RegisterScrolling 每次只收一个 Button，必须逐个注册。
            foreach (var btn in viewport.GetComponentsInChildren<SFS.UI.Button>(true))
            {
                if (btn != null) scroll.RegisterScrolling(btn);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewportRect);
            // 返回 viewport：UITools 会对返回值做布局重建，而我们需要滚动的是它的子节点
            return viewport;
        }

        static void AddSection(Box box, string title)
        {
            Label label = Builder.CreateLabel(box, contentWidth, 38, 0, 0, title);
            label.TextAlignment = TextAlignmentOptions.MidlineLeft;
            label.AutoFontResize = false;
            label.FontSize = FontSection;
            EnableWrapping(label, 1);
        }

        static void AddHint(Box box, string text)
        {
            // 按文本长度估算行数决定高度：写死高度会把长提示裁掉；
            // 关掉 AutoFontResize 保证所有提示字号一致（否则每条缩放比例不同）。
            int lines = EstimateLines(text);
            int height = Mathf.Max(26, lines * 22 + 4);
            Label hint = Builder.CreateLabel(box, contentWidth, height, 0, 0, text);
            hint.TextAlignment = TextAlignmentOptions.MidlineLeft;
            hint.AutoFontResize = false;
            hint.FontSize = FontHint;
            hint.Opacity = 0.7f;
            // Label 内部是 TextMeshProUGUI：显式开换行，否则长文件名/长句会横向溢出被裁
            EnableWrapping(hint, lines);
        }

        // 给 Label 开自动换行并禁止溢出裁切。 SFS 的 Label 没暴露这两个属性，但底层就是 TextMeshProUGUI，可直接取。
        static void EnableWrapping(Label label, int maxLines)
        {
            try
            {
                var tmp = label.gameObject != null
                    ? label.gameObject.GetComponent<TMPro.TextMeshProUGUI>()
                    : null;
                if (tmp == null) return;
                tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
                tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
                tmp.maxVisibleLines = Mathf.Max(maxLines, 1);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Settings label wrap", e);
            }
        }

        // 估算提示需要几行。每行可容纳字符数按标签宽度与字号粗算： FontHint 下每个 ASCII 字符宽约 FontHint*0.5，中文按 2 倍宽计。 显式换行各自占一行。
        static int EstimateLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int charsPerLine = Mathf.Max(16,
                Mathf.RoundToInt(contentWidth / (FontHint * 0.5f)));
            int weighted = 0;
            foreach (char c in text) weighted += c > 0x2E80 ? 2 : 1;
            int lines = Mathf.Max(1, Mathf.CeilToInt(weighted / (float)charsPerLine));
            foreach (char c in text)
                if (c == '\n') lines++;
            return lines;
        }

        static void AddNumberRow(Box box, string label, float value, float step,
            float min, float max, Action<float> apply)
        {
            Container container = Builder.CreateContainer(box);
            container.CreateLayoutGroup(Type.Horizontal, spacing: 10f);

            Label title = Builder.CreateLabel(container, labelWidth, RowHeight, 0, 0, label);
            title.TextAlignment = TextAlignmentOptions.MidlineLeft;
            title.AutoFontResize = false;
            title.FontSize = FontRow;
            EnableWrapping(title, 2);

            NumberInput input = UIToolsBuilder.CreateNumberInput(container, inputWidth, RowHeight, value, step);
            input.OnValueChangedEvent += v =>
            {
                apply(Mathf.Clamp(v, min, max));
                SaveAndApply();
            };
        }

        static void AddToggleRow(Box box, string label, bool value, Action<bool> apply)
        {
            AddToggleRow(box, label, () => value, apply);
        }

        static void AddToggleRow(Box box, string label, Func<bool> getValue, Action<bool> apply)
        {
            ToggleWithLabel toggle = Builder.CreateToggleWithLabel(box, contentWidth - 10, RowHeight,
                getValue,
                () =>
                {
                    apply(!getValue());
                    SaveAndApply();
                },
                labelText: label);
            toggle.label.TextAlignment = TextAlignmentOptions.MidlineLeft;
            toggle.label.AutoFontResize = false;
            toggle.label.FontSize = FontRow;
        }

        // 循环切换一行（枚举型设置用）。设置页不会在点击后重建，
        // 所以按钮自己改自己的文案，切换后立刻能看到新值。
        static void AddCycleRow(Box box, string label, Func<string> display, Action onClick)
        {
            Container container = Builder.CreateContainer(box);
            container.CreateLayoutGroup(Type.Horizontal, spacing: 10f);

            Label title = Builder.CreateLabel(container, labelWidth, RowHeight, 0, 0, label);
            title.TextAlignment = TextAlignmentOptions.MidlineLeft;
            title.AutoFontResize = false;
            title.FontSize = FontRow;
            EnableWrapping(title, 2);

            Button button = Builder.CreateButton(container, inputWidth, RowHeight, 0, 0,
                () => { }, display());
            button.OnClick = () =>
            {
                onClick();
                if (button != null) button.Text = display();
            };
        }

        static void AddButtonRow(Box box, string label, Action onClick)
        {
            Container container = Builder.CreateContainer(box);
            container.CreateLayoutGroup(Type.Horizontal, spacing: 10f);
            Label title = Builder.CreateLabel(container, labelWidth, RowHeight, 0, 0, label);
            title.TextAlignment = TextAlignmentOptions.MidlineLeft;
            title.AutoFontResize = false;
            title.FontSize = FontRow;
            EnableWrapping(title, 2);
            Builder.CreateButton(container, inputWidth, RowHeight, 0, 0, onClick, "Choose...");
        }

        static void SaveAndApply()
        {
            ModSettings.Data s = Settings;
            int flagVersionBefore = s != null ? s.flagTextureVersion : 0;

            ModSettings.main?.SaveSettings();
            ApplyAll();
            // 座椅贴图独立于 EVA：强制重扫一遍所有座椅模型
            SeatAppearance.Invalidate();
            SeatAppearance.ApplyAll();

            // 旗帜版本变了要重刷已插下的旗帜（旧版生成的独立旗杆得拆掉）
            if (Settings != null && Settings.flagTextureVersion != flagVersionBefore)
                FlagCustomization.RefreshAllForVersionChange();
        }

        // ---------------------------------------------------------------- 贴图选择（MenuGenerator 菜单）

        // 贴图选择（MenuGenerator 菜单）。getCurrent 供按钮显示当前选择，
        // pick 写入选中的文件名，clear 用于「恢复原生」。
        static void OpenTexturePicker(string title, Func<string> getCurrent,
            Action<string> pick, Action clear)
        {
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
                ModLogger.ErrorOnce("Texture picker", e);
            }

            var elements = new List<MenuElement>();
            SizeSyncerBuilder.Carrier carrier;
            elements.Add(new SizeSyncerBuilder(out carrier).HorizontalMode(SizeMode.MaxChildSize));
            elements.Add(TextBuilder.CreateText(() => title));

            if (files.Count == 0)
            {
                elements.Add(TextBuilder.CreateText(() =>
                    "No PNG/JPG found.\nPut images into:\n" + TexturesDirectory));
            }

            foreach (string file in files.Take(20))
            {
                string captured = file;
                elements.Add(ButtonBuilder.CreateButton(carrier,
                    () => (captured == getCurrent() ? "> " : "") + captured,
                    () =>
                    {
                        pick(captured);
                        SaveAndApply();
                    },
                    CloseMode.Current));
            }

            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Use native",
                () =>
                {
                    clear();
                    SaveAndApply();
                },
                CloseMode.Current));
            elements.Add(ButtonBuilder.CreateButton(carrier,
                () => "Close",
                () => { },
                CloseMode.Current));

            MenuGenerator.OpenMenu(CancelButton.Close, CloseMode.Current, elements.ToArray());
        }
    }

    // ---------------------------------------------------------------- Harmony 补丁

    // 浮力：EVA 空中受重力 manually 施加（OnFixedUpdate 的 gravity 参数），缩放即"漂浮/低重力"
    [HarmonyPatch(typeof(Astronaut_EVA), "OnFixedUpdate")]
    public class Patch_EVA_Buoyancy
    {
        static void Prefix(ref Vector2 gravity)
        {
            ModSettings.Data s = ModSettings.main != null ? ModSettings.main.settings : null;
            if (s == null) return;
            float f = Mathf.Clamp(s.evaBuoyancy, 0f, 1.5f);
            if (!Mathf.Approximately(f, 1f))
                gravity *= f;
        }
    }

    // 燃料消耗倍率
    [HarmonyPatch(typeof(EVA_Resources), "ConsumeFuel")]
    public class Patch_EVA_FuelDrain
    {
        static void Prefix(ref double amount)
        {
            ModSettings.Data s = ModSettings.main != null ? ModSettings.main.settings : null;
            if (s == null) return;
            float f = Mathf.Clamp(s.evaFuelDrain, 0f, 5f);
            if (!Mathf.Approximately(f, 1f))
                amount *= f;
        }
    }

    // 出舱生成：满燃料 + 应用全部参数
    [HarmonyPatch(typeof(AstronautManager), "SpawnEVA")]
    public class Patch_EVA_SpawnApply
    {
        static void Postfix(Astronaut_EVA __result)
        {
            try
            {
                ModSettings.Data s = ModSettings.main != null ? ModSettings.main.settings : null;
                if (s != null && s.evaFullFuelOnEVA && __result != null && __result.resources != null)
                    __result.resources.fuelPercent.Value = 1.0;
                AstronautTuning.ApplyTo(__result);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("EVA spawn apply", e);
            }
        }
    }
}
