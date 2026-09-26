using System;
using HarmonyLib;
using SFS.IO;
using SFS.Input;
using SFS.UI;
using TMPro;
using UnityEngine;

namespace AstronautMod
{
    /// 把模组配置放进游戏自带的设置系统（与 ModsSettings / KeybindingsPC 同款机制）。
    public class ModSettings : SettingsBase<ModSettings.Data>
    {
        [Serializable]
        public class Data
        {
            // 打开乘员菜单的绑定：修饰键 + 主键
            // crewMenuModifier: 0=None 1=Shift 2=Ctrl 3=Alt
            public int crewMenuModifier = 3;                 // 默认 Alt
            public KeyCode crewMenuKey = KeyCode.Mouse0;    // 默认左键
            public bool allowUncrewedControl = false;
            public bool showTelemetryDashboard = true;      // EVA 遥测面板总开关
            public int telemetryRefreshHz = 20;             // 遥测刷新频率（Hz），1..120

            // EVA 快捷键（替代原插旗/传送悬浮按钮）：修饰键同上 0=None
            public int plantFlagModifier = 0;
            public KeyCode plantFlagKey = KeyCode.F;        // 默认 F
            public int teleportModifier = 0;
            public KeyCode teleportKey = KeyCode.G;         // 默认 G
        }

        public static ModSettings main;

        protected override string FileName => "AstronautModSettings";

        protected override void OnLoad() { }

        // SettingsBase.Save() 为 protected，这里暴露一个公开方法供外部（绑定行 / 属性）调用
        public void SaveSettings()
        {
            Save();
        }

        private void Awake()
        {
            main = this;
            if (settings == null)
                Load();
        }

        public static string ModifierName(int m)
        {
            switch (m)
            {
                case 1: return "Shift";
                case 2: return "Ctrl";
                case 3: return "Alt";
                default: return "";
            }
        }

        public static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse0: return "LMB";
                case KeyCode.Mouse1: return "RMB";
                case KeyCode.Mouse2: return "MMB";
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return "Alt";
                case KeyCode.Return: return "Enter";
                default: return k.ToString();
            }
        }

        public static string BindingDisplayName()
        {
            if (main?.settings == null) return "Alt + LMB";
            return ComboName(main.settings.crewMenuModifier, main.settings.crewMenuKey);
        }

        /// <summary>修饰键 + 主键 的显示名（如 "F"、"Alt + G"）。</summary>
        public static string ComboName(int modifier, KeyCode key)
        {
            string mod = ModifierName(modifier);
            string k = KeyName(key);
            return mod.Length > 0 ? mod + " + " + k : k;
        }

        public static string BindingHint()
        {
            string mod = BindingDisplayName();
            if (main?.settings != null && main.settings.crewMenuKey == KeyCode.Mouse0)
                return "Tip: hold " + mod + " and click the capsule to open this menu.";
            return "Tip: press " + mod + " while pointing at the capsule to open this menu.";
        }

        // 遥测刷新间隔（秒），由可自定义的 Hz 推导；越界值夹到合理范围
        public static float TelemetryRefreshInterval()
        {
            int hz = (main != null && main.settings != null) ? main.settings.telemetryRefreshHz : 20;
            if (hz < 1) hz = 1;
            if (hz > 120) hz = 120;
            return 1f / hz;
        }

        public static readonly int[] TelemetryHzPresets = { 5, 10, 20, 30, 60 };
    }

    /// 自定义按键绑定行：游戏原生 KeybindingsPC.Key 只支持 Ctrl + 键，无法表达 Alt / 鼠标键，
    public class CrewMenuKeyBinder : MonoBehaviour
    {
        private ButtonPC button;
        private TMP_Text text;
        private bool capturing;

        private void Awake()
        {
            button = GetComponentInChildren<ButtonPC>(true);
            // 行内有两段文本：texts[0]=动作名（不能覆盖） texts[1]=当前键值
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            text = texts.Length > 1 ? texts[1] : texts[0];
            if (button != null)
                button.onClick += (Action)(() => BeginCapture());
            Refresh();
        }

        private void BeginCapture()
        {
            capturing = true;
            if (text != null) text.text = "-";
        }

        private void Update()
        {
            if (!capturing) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                capturing = false;
                Refresh();
                return;
            }

            KeyCode captured = CaptureAnyKey();
            if (captured != KeyCode.None)
            {
                ModSettings.Data d = ModSettings.main?.settings;
                if (d != null)
                {
                    d.crewMenuKey = captured;
                    d.crewMenuModifier = CurrentModifier();
                    ModSettings.main.SaveSettings();
                }
                capturing = false;
                Refresh();
            }
        }

        private static KeyCode CaptureAnyKey()
        {
            if (Input.GetKeyDown(KeyCode.Mouse0)) return KeyCode.Mouse0;
            if (Input.GetKeyDown(KeyCode.Mouse1)) return KeyCode.Mouse1;
            if (Input.GetKeyDown(KeyCode.Mouse2)) return KeyCode.Mouse2;
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (k == KeyCode.Mouse0 || k == KeyCode.Mouse1 || k == KeyCode.Mouse2) continue;
                if (k != KeyCode.Escape && Input.GetKeyDown(k))
                    return k;
            }
            return KeyCode.None;
        }

        private static int CurrentModifier()
        {
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return 3;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return 2;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return 1;
            return 0;
        }

        private void Refresh()
        {
            if (text != null) text.text = ModSettings.BindingDisplayName();
        }
    }

    /// 通用按键绑定行（捕获 修饰键(Alt/Shift/Ctrl/None) + 主键(含鼠标) 并回调写入任意配置项）。
    public class ModKeyBinder : MonoBehaviour
    {
        private ButtonPC button;
        private TMP_Text text;
        private bool capturing;
        private Action<int, KeyCode> apply;
        private Func<string> display;

        /// <summary>必须在 AddComponent 之后、捕获开始前调用。</summary>
        public void Setup(Action<int, KeyCode> applyBinding, Func<string> displayValue)
        {
            apply = applyBinding;
            display = displayValue;
            Refresh();
        }

        private void Start()
        {
            button = GetComponentInChildren<ButtonPC>(true);
            // 行内有两段文本：texts[0]=动作名（不能覆盖） texts[1]=当前键值
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            text = texts.Length > 1 ? texts[1] : texts[0];
            if (button != null)
                button.onClick += (Action)(() =>
                {
                    capturing = true;
                    if (text != null) text.text = "-";
                });
            Refresh();
        }

        private void Update()
        {
            if (!capturing) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                capturing = false;
                Refresh();
                return;
            }

            KeyCode captured = CaptureAnyKey();
            if (captured != KeyCode.None)
            {
                apply?.Invoke(CurrentModifier(), captured);
                capturing = false;
                Refresh();
            }
        }

        private static KeyCode CaptureAnyKey()
        {
            if (Input.GetKeyDown(KeyCode.Mouse0)) return KeyCode.Mouse0;
            if (Input.GetKeyDown(KeyCode.Mouse1)) return KeyCode.Mouse1;
            if (Input.GetKeyDown(KeyCode.Mouse2)) return KeyCode.Mouse2;
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (k == KeyCode.Mouse0 || k == KeyCode.Mouse1 || k == KeyCode.Mouse2) continue;
                if (k != KeyCode.Escape && Input.GetKeyDown(k))
                    return k;
            }
            return KeyCode.None;
        }

        private static int CurrentModifier()
        {
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return 3;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return 2;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return 1;
            return 0;
        }

        private void Refresh()
        {
            if (text != null && display != null) text.text = display();
        }
    }

    /// 在游戏设置界面的 Keybindings 列表末尾追加一行 "Open Astronaut Menu" 自定义绑定。
    [HarmonyPatch(typeof(KeybindingsPC), "Awake")]
    public class Patch_KeybindingsPC_Awake
    {
        static void Postfix(KeybindingsPC __instance)
        {
            try
            {
                if (__instance.keybindingPrefab == null || __instance.keybindingsHolder == null)
                    return;

                // 用一格空格与上方原生绑定分隔
                if (__instance.spacePrefab != null)
                    UnityEngine.Object.Instantiate(__instance.spacePrefab, __instance.keybindingsHolder);

                GameObject row = UnityEngine.Object.Instantiate(__instance.keybindingPrefab, __instance.keybindingsHolder);
                if (row == null) return;

                // 移除原生 KeyBinder（其捕获逻辑不支持 Alt / 鼠标键），换成我们自己的
                KeyBinder native = row.GetComponentInChildren<KeyBinder>(true);
                if (native != null) UnityEngine.Object.Destroy(native);

                TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = "Open Astronaut Menu";

                row.AddComponent<CrewMenuKeyBinder>();

                // 模组设置分区：把其他可自定义的选项也放进游戏设置（Controls 页）
                if (__instance.textPrefab != null)
                {
                    UnityEngine.Object.Instantiate(__instance.textPrefab, __instance.keybindingsHolder)
                        .GetComponentInChildren<TMP_Text>().text = "Astronaut Mod";
                }

                if (ModSettings.main?.settings != null)
                {
                    AddSettingRow(__instance, "Allow control without crew",
                        () => CurrentSettings().allowUncrewedControl ? "On" : "Off", () =>
                        {
                            ModSettings.Data d = CurrentSettings();
                            if (d == null) return;
                            d.allowUncrewedControl = !d.allowUncrewedControl;
                            ModSettings.main.SaveSettings();
                        });

                    AddSettingRow(__instance, "EVA telemetry dashboard",
                        () => CurrentSettings().showTelemetryDashboard ? "On" : "Off", () =>
                        {
                            ModSettings.Data d = CurrentSettings();
                            if (d == null) return;
                            d.showTelemetryDashboard = !d.showTelemetryDashboard;
                            ModSettings.main.SaveSettings();
                        });

                    int[] presets = ModSettings.TelemetryHzPresets;
                    AddSettingRow(__instance, "Telemetry refresh rate",
                        () => CurrentSettings().telemetryRefreshHz + " Hz", () =>
                        {
                            ModSettings.Data d = CurrentSettings();
                            if (d == null) return;
                            int idx = System.Array.IndexOf(presets, d.telemetryRefreshHz);
                            d.telemetryRefreshHz = presets[(idx + 1) % presets.Length];
                            ModSettings.main.SaveSettings();
                        });

                    AddKeybindRow(__instance, "Plant Flag (EVA)",
                        (mod, key) =>
                        {
                            ModSettings.Data d = CurrentSettings();
                            if (d == null) return;
                            d.plantFlagModifier = mod;
                            d.plantFlagKey = key;
                            ModSettings.main.SaveSettings();
                        },
                        () =>
                        {
                            ModSettings.Data d = CurrentSettings();
                            return d == null ? "F" : ModSettings.ComboName(d.plantFlagModifier, d.plantFlagKey);
                        });

                    AddKeybindRow(__instance, "Teleport (EVA)",
                        (mod, key) =>
                        {
                            ModSettings.Data d = CurrentSettings();
                            if (d == null) return;
                            d.teleportModifier = mod;
                            d.teleportKey = key;
                            ModSettings.main.SaveSettings();
                        },
                        () =>
                        {
                            ModSettings.Data d = CurrentSettings();
                            return d == null ? "G" : ModSettings.ComboName(d.teleportModifier, d.teleportKey);
                        });
                }
            }
            catch (Exception e)
            {
                ModLogger.ErrorOnce("Keybindings injection", e);
            }
        }

        // 点击时实时取当前实例的设置对象（重载后 main 会换成新实例，不能捕获旧引用）；
        // main 意外为空时（重载时序竞态）自愈重建设置对象
        private static ModSettings.Data CurrentSettings()
        {
            if (ModSettings.main == null)
                AstronautModMain.EnsureModSettings();
            return ModSettings.main != null ? ModSettings.main.settings : null;
        }

        // 按键绑定行：点击后捕获 修饰键 + 主键（Esc 取消）
        private static void AddKeybindRow(KeybindingsPC instance, string label,
            Action<int, KeyCode> apply, Func<string> display)
        {
            GameObject row = UnityEngine.Object.Instantiate(instance.keybindingPrefab, instance.keybindingsHolder);
            if (row == null) return;

            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length > 0) texts[0].text = label;

            KeyBinder native = row.GetComponentInChildren<KeyBinder>(true);
            if (native != null) UnityEngine.Object.Destroy(native);

            ModKeyBinder binder = row.AddComponent<ModKeyBinder>();
            binder.Setup(apply, display);
        }

        // 复用 keybindingPrefab 的行布局：左侧动作名 + 右侧 ButtonPC 显示当前值。
        // 移除原生 KeyBinder 组件后，用全新 OptionalDelegate 接管点击，避免触发原生的按键捕获屏。
        private static void AddSettingRow(KeybindingsPC instance, string label,
            System.Func<string> getValue, System.Action onClick)
        {
            GameObject row = UnityEngine.Object.Instantiate(instance.keybindingPrefab, instance.keybindingsHolder);
            if (row == null) return;

            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text actionLabel = texts.Length > 0 ? texts[0] : null;
            TMP_Text valueText = texts.Length > 1 ? texts[1] : actionLabel;
            if (actionLabel != null) actionLabel.text = label;

            KeyBinder native = row.GetComponentInChildren<KeyBinder>(true);
            if (native != null) UnityEngine.Object.Destroy(native);

            ButtonPC btn = row.GetComponentInChildren<ButtonPC>(true);
            if (btn != null)
            {
                btn.onClick = new OptionalDelegate<OnInputEndData>();
                btn.onClick += (System.Action)(() =>
                {
                    onClick();
                    if (valueText != null) valueText.text = getValue();
                });
                if (valueText != null) valueText.text = getValue();
            }
        }
    }
}
