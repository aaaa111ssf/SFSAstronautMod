using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SFS.World;
using UnityEngine;

namespace AstronautMod
{
    // 座椅上（未出 EVA）宇航员的贴图自定义。
//
// 原生模型 Seated Astronaut 的结构（从 resources.assets 读出）：
//   Seated Astronaut
//     Character（整体缩放 0.7 x 1.3）
//       Sprite    → 4 个 SpriteRenderer，精灵 Astronaut_0（头盔+上身，164x329px）
//       Sprite(1) → 4 个 SpriteRenderer，精灵 Astronaut_1（躯干，120x115px）
//       Sprite(2) → 4 个 SpriteRenderer，精灵 Astronaut_2（腿+靴，145x135px）
//
// 两种模式互斥：
//   原生 Native  ：完全不干预，显示原版外观
//   整体 Whole   ：隐藏全部原生渲染器，用一张图铺满整个模型包围盒（同 EVA 做法）
//
// 贴图按宇航员分别保存（配置在 Hub → Astronauts → 选中某人 → Customize Texture），
// 座位上的模型按该座位所属宇航员的名字取自己的贴图。
//
// 注：早期版本还有「部分贴图」（头/身体/腿各一张），依赖精灵名分组识别与
// pivot/尺寸归一化，实测错误率过高（替换后错位成「空气」、切回原生重建失败），
// 已按需求整体移除，只保留整体贴图。
    public static class SeatAppearance
    {
        public const int ModeNative = 0;
        public const int ModeWhole = 1;

        const string OverlayName = "AstronautMod_SeatSuit";

        static ModSettings.Data Settings =>
            ModSettings.main != null ? ModSettings.main.settings : null;

        // 一个座椅模型的状态（按模型 GameObject 的 InstanceID 索引）。
        private class ModelState
        {
            public GameObject root;
            public readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
            public SpriteRenderer overlay;

            // 原生包围盒只算一次并缓存：整体贴图模式会把渲染器全禁用，
            // 之后再算 bounds 不可靠，导致切回原生时重建不出正确尺寸。
            public bool hasNativeBounds;
            public Vector3 nativeCenterLocal;
            public Vector3 nativeSizeLocal;

            // 已应用的状态（键 = 该座位上的宇航员名），用于跳过无变化的重复工作
            public string appliedAstronaut = "\u0000";   // "\u0000" = 尚未应用过
            public int appliedMode = -1;
            public string appliedWhole = "";
        }

        // 不要用 Unity 对象当字典键：已销毁对象 Equals 相等但哈希不同，会破坏字典约定
        static readonly Dictionary<int, ModelState> states = new Dictionary<int, ModelState>();
        static readonly Dictionary<string, Sprite> textureCache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        static bool loggedGeometry;

        // ---------------------------------------------------------------- 扫描与应用

        // 扫描场景内所有座椅模型并应用当前设置。由 UpdateDriver 定时与设置变更时调用。
        public static void ApplyAll()
        {
            try
            {
                CrewModule[] crews = UnityEngine.Object.FindObjectsByType<CrewModule>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                var live = new HashSet<int>();

                foreach (CrewModule crew in crews)
                {
                    if (crew == null || crew.seats == null) continue;
                    foreach (CrewModule.Seat seat in crew.seats)
                    {
                        if (seat == null) continue;
                        GameObject model = seat.astronautModel;
                        if (model == null) continue;
                        // 只改场景里的实例。万一拿到的是 prefab 资产（例如建造页复用），
                        // 改它的 sprite 等于永久改写资源文件，必须跳过。
                        if (!model.scene.IsValid()) continue;
                        // 模型未激活时渲染包围盒不可靠，等它被显示出来的那一轮再处理
                        if (!model.activeInHierarchy) continue;

                        int id = model.GetInstanceID();
                        live.Add(id);
                        ApplyToModel(model, CrewAppearance.GetForAstronaut(seat.astronaut?.Value));
                    }
                }

                Prune(live);
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Seat appearance scan", e);
            }
        }

        static void Prune(HashSet<int> live)
        {
            if (states.Count == 0) return;
            List<int> dead = states.Keys.Where(id => !live.Contains(id)).ToList();
            foreach (int id in dead)
            {
                ModelState st = states[id];
                states.Remove(id);
                if (st != null) DestroyOverlay(st);
            }
        }

        // 设置刚被改动：强制所有已缓存模型重新应用一次。
        public static void Invalidate()
        {
            foreach (ModelState st in states.Values)
            {
                if (st == null) continue;
                st.appliedMode = -1;
            }
        }

        static void ApplyToModel(GameObject model, CrewAppearance.Style style)
        {
            int id = model.GetInstanceID();
            if (!states.TryGetValue(id, out ModelState st) || st == null || st.root != model)
            {
                st = Capture(model);
                states[id] = st;
                if (st == null) return;
            }

            // 空座位 / 未设置 → 原生外观
            int mode = style == null ? ModeNative : Mathf.Clamp(style.mode, ModeNative, ModeWhole);
            string whole = style == null ? "" : (style.textureFile ?? "");
            string who = style == null ? "" : style.astronautName;

            if (st.appliedMode == mode &&
                string.Equals(st.appliedWhole, whole, StringComparison.Ordinal) &&
                string.Equals(st.appliedAstronaut, who, StringComparison.Ordinal))
            {
                return;
            }

            // 无论切到什么模式，先把原生渲染器全部恢复 —— 这是「切回原生能正常重建」的关键
            RestoreNative(st);

            // 贴图文件缺失/加载失败时不记为「已应用」，下一轮扫描还会重试；
            // 否则状态会卡死成"已应用"，之后把文件补回来也不会生效。
            if (mode == ModeWhole && !ApplyWhole(st, whole)) return;

            st.appliedMode = mode;
            st.appliedWhole = whole;
            st.appliedAstronaut = who;

            if (!loggedGeometry)
            {
                loggedGeometry = true;
                LogGeometry(st);
            }
        }

        // ---------------------------------------------------------------- 采集与还原

        static ModelState Capture(GameObject model)
        {
            var st = new ModelState { root = model };

            foreach (SpriteRenderer sr in model.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null) continue;
                // 自己的覆盖层不是原生部件，必须排除
                if (sr.gameObject.name == OverlayName) continue;
                if (sr.gameObject.name.StartsWith("AstronautMod_")) continue;
                st.renderers.Add(sr);
            }
            if (st.renderers.Count == 0) return null;

            // 关键：先确保渲染器都处于启用状态，再算原生包围盒。
            // 若在整体贴图模式（渲染器已被禁用）下才首次采集，bounds 会算成空的，
            // 导致贴图铺不开、切回原生也重建不出正确尺寸。
            CacheNativeBounds(st);

            if (!loggedGeometry)
            {
                loggedGeometry = true;
                LogGeometry(st);
            }
            return st;
        }

        // 采集时把原生包围盒换算到模型根节点局部空间并缓存。 整体贴图模式隐藏全部原生渲染器后，bounds 就不再可靠，只能靠这份缓存还原。
        static void CacheNativeBounds(ModelState st)
        {
            try
            {
                Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool has = false;
                foreach (SpriteRenderer sr in st.renderers)
                {
                    if (sr == null || sr.sprite == null) continue;
                    if (!sr.enabled) sr.enabled = true;      // 采集前先恢复启用
                    if (!has) { bounds = sr.bounds; has = true; }
                    else bounds.Encapsulate(sr.bounds);
                }
                if (!has) return;

                Transform root = st.root.transform;
                Vector3 lossy = root.lossyScale;
                st.nativeCenterLocal = root.InverseTransformPoint(bounds.center);
                st.nativeSizeLocal = new Vector3(
                    Mathf.Abs(bounds.size.x / (Mathf.Abs(lossy.x) < 0.0001f ? 1f : lossy.x)),
                    Mathf.Abs(bounds.size.y / (Mathf.Abs(lossy.y) < 0.0001f ? 1f : lossy.y)),
                    1f);
                st.hasNativeBounds = true;
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Seat native bounds", e);
            }
        }

        // 还原成原生外观：销毁覆盖层 + 把所有原生渲染器重新启用。 整体贴图模式只改 enabled、不改原生 sprite，所以还原只需启用即可， 不需要缓存/恢复 sprite —— 这也避免了"缓存里存
        // 的是自定义贴图"导致切不回去的问题。
        static void RestoreNative(ModelState st)
        {
            DestroyOverlay(st);
            foreach (SpriteRenderer sr in st.renderers)
            {
                if (sr != null && !sr.enabled) sr.enabled = true;
            }
        }

        static void DestroyOverlay(ModelState st)
        {
            if (st.overlay == null) return;
            // 立刻置空并 Disable：Destroy 是帧末才生效的，同一帧内 Capture 遍历
            // GetComponentsInChildren 仍会捞到它。先关掉保证不会被当成原生部件捕获。
            GameObject go = st.overlay.gameObject;
            st.overlay = null;
            if (go != null)
            {
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
            }
        }

        // ---------------------------------------------------------------- 整体贴图

        static bool ApplyWhole(ModelState st, string fileName)
        {
            Sprite custom = LoadTexture(fileName);
            if (custom == null) return false;
            if (!st.hasNativeBounds) return false;

            Vector3 localSize = st.nativeSizeLocal;
            if (localSize.x < 0.0001f || localSize.y < 0.0001f) return false;

            int order = 0;
            foreach (SpriteRenderer sr in st.renderers)
                if (sr != null && sr.sortingOrder > order) order = sr.sortingOrder;

            if (st.overlay == null)
            {
                GameObject go = new GameObject(OverlayName);
                go.transform.SetParent(st.root.transform, false);
                st.overlay = go.AddComponent<SpriteRenderer>();
            }

            st.overlay.sprite = custom;
            st.overlay.color = Color.white;
            st.overlay.sortingOrder = order + 1;
            st.overlay.transform.localPosition = st.nativeCenterLocal;
            // 铺满原生包围盒（整体模式就是让一张图盖住整个人形）
            Vector2 spriteSize = custom.bounds.size;
            st.overlay.transform.localScale = new Vector3(
                spriteSize.x < 0.0001f ? 1f : localSize.x / spriteSize.x,
                spriteSize.y < 0.0001f ? 1f : localSize.y / spriteSize.y,
                1f);
            st.overlay.enabled = true;

            foreach (SpriteRenderer sr in st.renderers) if (sr != null) sr.enabled = false;
            return true;
        }

        // ---------------------------------------------------------------- 贴图加载

        // 从 Mods/AstronautMod/Textures 读图并生成精灵。 整体模式靠 localScale 铺满包围盒，所以这里固定 PPU=100 即可。
        static Sprite LoadTexture(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            if (textureCache.TryGetValue(fileName, out Sprite cached) && cached != null) return cached;

            try
            {
                string path = Path.Combine(AstronautTuning.TexturesDirectory, fileName);
                if (!File.Exists(path)) return null;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Type conversion = System.Type.GetType(
                    "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                MethodInfo load = conversion?.GetMethod("LoadImage",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
                if (load == null) return null;

                object ok = load.Invoke(null, new object[] { texture, File.ReadAllBytes(path), false });
                if (!(ok is bool) || !(bool)ok) return null;

                texture.name = "AstronautMod_Seat_" + fileName;
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
                ModLogger.ErrorOnce("Seat texture load", e);
                return null;
            }
        }

        // ---------------------------------------------------------------- 诊断

        static void LogGeometry(ModelState st)
        {
            try
            {
                var lines = new List<string>();
                foreach (SpriteRenderer sr in st.renderers)
                {
                    if (sr == null) continue;
                    string spriteName = sr.sprite != null ? sr.sprite.name : "None";
                    lines.Add("obj='" + sr.gameObject.name + "' sprite=" + spriteName +
                        " order=" + sr.sortingOrder);
                }
                lines.Add("nativeBounds=" + st.nativeSizeLocal.ToString("F3") +
                    " center=" + st.nativeCenterLocal.ToString("F3"));
                ModLogger.Info("Seat model '" + st.root.name + "' (" + st.renderers.Count +
                    " renderers):\n  " + string.Join("\n  ", lines));
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Seat geometry log", e);
            }
        }
    }
}