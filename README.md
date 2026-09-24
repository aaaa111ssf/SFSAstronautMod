# AstronautMod / 宇航员模组

## 安装方法 / Installation
1. 将 AstronautMod.dll 复制/移动到：SFS游戏目录/Mods/ 文件夹下  
   **Copy/Move AstronautMod.dll to: SFS game directory/Mods/ folder**
2. 启动游戏  
   **Launch the game**

---

## 使用方法 / Usage

### ■ 创建宇航员（Hub页面）/ Create Astronaut (Hub Page)
- 点击 "Astronauts" 按钮（位于成就按钮旁边）创建宇航员  
  **Click the "Astronauts" button (next to the Achievements button) to create astronauts**

### ■ 分配宇航员到座位（Build页面）/ Assign Astronaut to Seat (Build Page)
- 点击载入舱(CrewModule)部件  
  **Click on the CrewModule part**
- 在弹出的菜单中点击 "Assign" 分配宇航员到座位  
  **Click "Assign" in the popup menu to assign an astronaut to a seat**
- 如果没有宇航员，会自动弹出创建宇航员对话框  
  **If no astronauts exist, the create astronaut dialog will open automatically**
- 如果所有宇航员都在出舱(EVA)状态，会显示提示并可创建新宇航员  
  **If all astronauts are in EVA status, a message will be shown and you can create a new astronaut**

### ■ 出舱EVA（World页面）/ EVA Exit (World Page)
- 点击载入舱部件  
  **Click on the CrewModule part**
- 如果座位有宇航员，会显示 "EVA Exit" 按钮  
  **If the seat has an astronaut, an "EVA Exit" button will appear**
- 点击 "EVA Exit" 即可让宇航员出舱  
  **Click "EVA Exit" to let the astronaut exit the vehicle**

### ■ 配置空舱控制 / Configure Uncrewed Control
- 模组会在游戏安装目录的 `Mods/AstronautMod/config.txt` 创建配置文件。使用 `allowUncrewedControl=false` 时，所有已启用模组 Crew/EVA 适配的空舱都需要至少一名宇航员才可控制；设为 `true` 时，空舱仍可控制。
  **The mod creates `Mods/AstronautMod/config.txt` in the game installation directory. With `allowUncrewedControl=false`, every empty capsule using the mod's Crew/EVA adaptation requires at least one astronaut for control. Set it to `true` to allow uncrewed control.**

### ■ 宇航员旗帜自定义 / Customize Astronaut Flags
- 在 Hub 页面打开 **Astronauts**，点击目标宇航员后选择 **Customize Flag**。可选择白、红、蓝、绿、黄或紫色旗帜。
  **Open Astronauts in the Hub, select an astronaut, then choose Customize Flag. White, red, blue, green, yellow, and purple presets are available.**
- 如需使用图片，请把 `.png`、`.jpg` 或 `.jpeg` 文件放入游戏安装目录下的 `Mods/AstronautMod/Flags` 文件夹；在 **Set image file** 中输入文件名（例如 `mission.png`）。旧版持久化数据目录仍可读取，以避免既有配置失效。
  **To use an image, copy a `.png`, `.jpg`, or `.jpeg` file into `Mods/AstronautMod/Flags` under the game installation directory, then enter its filename (for example, `mission.png`) in Set image file. The previous persistent-data location remains readable for compatibility.**
- 设置会绑定到宇航员；之后由该宇航员插下的旗帜会记录独立外观。颜色、文件名与每面已插旗帜的位置均由模组配置保存，因此重新加载世界后仍会恢复。
  **The setting belongs to the astronaut. Flags subsequently planted by that astronaut retain an independent appearance. The mod persists colors, filenames, and each planted flag's location so appearances are restored after loading a world.**
- 选择 **Reset to native flag** 会取消该宇航员的自定义设置；其已插下的旗帜会立即还原为原生外观。
  **Reset to native flag removes that astronaut's customization and immediately restores the native appearance on their existing flags.**

### ■ 插旗（World页面，EVA状态）/ Plant Flag (World Page, EVA Status)
- 宇航员出舱后，屏幕右下角出现 "Plant Flag" 按钮  
  **After the astronaut exits, a "Plant Flag" button appears in the bottom-right corner**
- 点击按钮在当前位置插旗；旗帜会使用当前宇航员的自定义外观。
  **Click the button to plant a flag at the current position; the flag uses the current astronaut's customized appearance.**
- 注意：不能距已有旗帜30米以内插旗  
  **Note: Cannot plant a flag within 30 meters of an existing flag**

### ■ 捡石头（World页面，EVA状态）/ Collect Rocks (World Page, EVA Status)
- 宇航员出舱后，左键点击地表上的石头  
  **After the astronaut exits, left-click on rocks on the surface**
- 选中石头后会弹出 "Collect Rock" 选项  
  **After selecting a rock, a "Collect Rock" option will appear**
- 点击收集石头  
  **Click to collect the rock**

---

## 更新日志 / Changelog

### 【v3.9.3 更新 / v3.9.3 Update】

- **修复：三类每帧异常（共约 7700 次）**
  - ① 缺少可选依赖 Custom Save Data 时，保存桥接层每帧抛 `FileNotFoundException`。已重写为纯反射桥接（含 DynamicMethod 事件代理），未安装该依赖时静默停用；已安装则功能照常。
  - ② 游戏原生 `FlightInfoDrawer.Update` 用 `Split(':')[1]` 取值，本地化串缺冒号时每帧数组越界。已由模组安全接管，缺失分隔符时退化为整串显示。
  - ③ 游戏原生 `LocationDrawer.Update` 的 `Substring(0, IndexOf(":"))` 在同样场景下抛 `ArgumentOutOfRangeException`。已同样安全接管，俯仰角箭头逻辑照常驱动。
  **Fixed: ① the Custom Save Data bridge spammed `FileNotFoundException` every frame when the optional dependency was missing — rewritten as a pure-reflection bridge that silently disables itself; ② the native `FlightInfoDrawer.Update` crashed with `IndexOutOfRangeException` when the localized string lacked a colon — taken over with safe slicing; ③ the native `LocationDrawer.Update` threw `ArgumentOutOfRangeException` for the same reason — taken over as well, pitch-angle logic preserved.**

- **修复：回发射后残留的已销毁宇航员引用被误判为 EVA**
  C# 的 `is Astronaut_EVA` 对已销毁对象仍返回 true，导致：发射时错误显示 EVA 遥测仪表盘、进入 EVA 时按键提醒不出现、插旗/传送按键静默失败。所有"玩家是否 EVA"的判断已改为 Unity 重载的 `!= null`（销毁对象视为 null）。此外：按键未配置/设置未就绪时回退默认键位；非 EVA 状态按键会给出明确提示。
  **Fixed: C# `is Astronaut_EVA` still matches destroyed astronaut objects left in `player.Value` after reverting to launch — the dashboard wrongly appeared during launch, the EVA key reminder never fired, and flag/teleport keys silently failed. All EVA checks now use Unity's overloaded `!= null`. Keys also fall back to defaults when settings are unavailable, and pressing them outside EVA shows a clear hint.**

- **变更：移除插旗/传送悬浮按钮，改为可配置快捷键（默认 F / G）**
  在 游戏设置 → Keybindings → Astronaut Mod 分区可自定义（支持 Shift/Ctrl/Alt 修饰键与鼠标键，Esc 取消）。进入 EVA 时通过消息栏提醒一次当前按键；设置界面的绑定行同时显示动作名与键值。
  **Changed: the "Plant Flag" / "Teleport" floating buttons are removed in favor of configurable keybinds (defaults F / G) under Settings → Keybindings → Astronaut Mod (modifier keys and mouse buttons supported, Esc to cancel). Entering EVA shows a one-time key reminder; binding rows now display both the action name and the key.**

### 【v3.9.2 更新 / v3.9.2 Update】

本次更新修复"重载模组后世界 / 蓝图页面出问题"与"宇航员(EVA)仪表盘消失"两大回归，并改进发射前"缺失宇航员"提示为可选择的双按钮。
**This release fixes two regressions — "World/Blueprint pages break after reloading the mod" and "the astronaut (EVA) telemetry dashboard disappeared" — and upgrades the pre-launch "missing astronaut" prompt into a choosable two-button dialog.**

- **修复：重载模组后世界 / 蓝图页面报错，并恢复 EVA 宇航员仪表盘**
  根因：`Mod` 基类没有卸载钩子，重载模组时旧场景事件与一批 `DontDestroyOnLoad` 对象（驱动、宇航员状态、乘员菜单、RockSelector、ModSettings 等）会残留。最初用 `SceneManager.sceneUnloaded` 做清理，但该事件在**每次正常场景切换（标题→Hub、Hub→蓝图、蓝图→世界…）**都会触发——既会在 Hub 初始化前 `-=` 取消 `OnHubSceneLoaded` 等场景处理器（**菜单打不开、Hub UI 消失**），又会按 `__Astronaut` 前缀销毁活动对象，连托管 EVA 仪表盘刷新循环的 `UpdateDriver` 也被误删，导致**仪表盘消失、模组几乎无法使用**。
  修复：清理逻辑**只在 `Load()` 内进行**（SFS 重载模组会再次调用 `Load()`）。`Load()` 开头先 `DestroyPersistentObjects()`（销毁上一实例追踪的对象并清空集合）再 `DestroyOrphanObjects()`（按前缀清理跨程序集残留），绝不触碰正常场景切换。EVA 遥测仪表盘（速度 / 高度 / 燃料）随之恢复正常。此外，`Load()` 末尾会按**当前实际处于的场景**手动重跑对应初始化（Hub / 蓝图 / 世界）：因为重载模组只重调 `Load()`、不重新加载场景，若不在 `Load()` 里补这一刀，重载后当前场景的菜单 / 宇航员状态 / EVA 基础设施会被清掉却不再重建，必须手动切场景或重启游戏 UI 才回来。
  **Fix: the root cause was a `SceneManager.sceneUnloaded` cleanup that fired on every normal scene transition — it both unsubscribed the Hub/Build/World scene handlers before they ran (menu couldn't open, Hub UI vanished) and destroyed live `__Astronaut*` objects by prefix, including the `UpdateDriver` that refreshes the EVA telemetry dashboard, so the dashboard disappeared. Cleanup now runs only inside `Load()`: on reload it first destroys its tracked objects then name-prefixed orphans, never touching normal play. The EVA telemetry dashboard (speed/altitude/fuel) is restored. `Load()` also re-runs the active scene's init (Hub/Build/World) at its end, since reloading a mod re-invokes `Load()` without reloading the scene — without this, the current scene's menu/state/EVA infra would be cleaned yet never rebuilt, requiring a manual scene switch or game restart.**

- **改进：发射前"缺失宇航员"警告改为双按钮（仍继续 / 取消）**
  拦截条件不变：若火箭含乘员舱、却全部为空座、且不存在任何控制来源（原生 `ControlModule` 有控制、或某个乘员舱已有人、或开启"无乘员控制"），在 `BuildManager.Launch` 之前弹出提示。现在提供两个选择：
  - **仍继续 / Continue Anyway**：忽略警告，照常发射（由玩家自行承担无控制风险）；
  - **取消 / Cancel**：关闭提示，不发射，回到蓝图修正后再发射。
  混合舰队（探测器 + 空乘员舱）因为有探测器提供控制，不会触发此警告。
  **Improved: a pre-launch "missing astronaut" warning. Intercept condition unchanged. The dialog now offers two choices: "Continue Anyway" launches regardless (player accepts the no-control risk); "Cancel" closes the prompt without launching. Mixed fleets (probe + empty crew seat) keep control via the probe and are not warned.**

### 【v3.9.1 更新 / v3.9.1 Update】

本次更新重写了注入型乘员舱的数据存储方式，解决了"发射后火箭消失"、"回退后失去控制"与"重启后配置丢失"这三个长期问题的共同根因。
**This release rewrites how injected crew capsules store their data, fixing the common root cause of the "rocket disappears after launch", "control lost after revert" and "config lost after restart" bugs.**

- **修复：重启 SFS 后自定义太空舱配置丢失（原 Bug 3）**
  每个被模组适配的部件实例会把自身的"Crew Capacity / 每个座位的宇航员"写入该部件原生的文本变量（`AstronautMod_Cap`、`AstronautMod_Seat0…`）。这些变量随 `PartSave.TEXT_VARIABLES` 走原生存档管线，因此会随蓝图、火箭存档、世界存档、快速存档一起落盘，不再依赖全局 JSON 或 InstanceID 缓存。
  **Fix: per-part state (capacity + astronaut per seat) is now written into the part's own native text variables, so it travels inside blueprints, rocket saves, and world saves through the vanilla pipeline.**
- **新增（可选依赖）：Custom Save Data 集成**
  若安装了 [Custom-Save-Data-SFS](https://github.com/AstroTheRabbit/Custom-Save-Data-SFS)（`CustomSaveData.dll`），模组还会把乘员配置额外写入蓝图 customData 与世界存档 customData 作为冗余备份；未安装时该桥接自动禁用，模组照常工作。
  **New (optional dependency): when Custom Save Data is installed, crew data is also mirrored into blueprint and world save custom data. Without it, the bridge disables itself.**
- **修复：发射后火箭消失、地图视图锁死在太阳（原 Bug 1）**
  过去发射时乘员名要通过脆弱的"部件名 → 宇航员名"全局缓存重建；一旦某个座舱恢复失败，`hasControl` 就为假，`RocketManager.SpawnBlueprint` 找不到可控火箭，玩家目标为空、摄像机停在原点（太阳）。现在乘员随部件恢复，同时在 `SpawnBlueprint` 之后强制校正控制权、玩家目标与地图视图目标。
  **Fix: after a blueprint is spawned the mod now guarantees a controllable rocket exists, assigns the player target and re-points the map view, so neither the rocket nor the camera can get stranded.**
- **修复："恢复到发射状态" / 回退 30 秒 / 3 分钟后宇航员消失并失去控制（原 Bug 2）**
  新增"世界重建窗口"：载入存档、回退与重新生成火箭期间，旧座舱的销毁不会把宇航员判定为阵亡，新座椅的初始化也不会清空已经记录的乘员。乘员通过部件变量恢复，控制权随之恢复。
  **Fix: a world-rebuild window prevents old capsules from killing their crew and new seats from wiping restored occupants during loads and reverts.**
- **修复：已有 action 的自定义太空舱（如 Vanilla Redstone 的 Mercury 舱）无法进入宇航员菜单（原 Bug 4）**
  在 World 场景中，若太空舱自带 action（点击只触发该 action），请 **按住 Alt 再点击该舱**（默认绑定，可在游戏设置 → Keybindings 中自定义为任意 修饰键 + 键/鼠标），即可直接打开乘员菜单；部件统计菜单中另提供 **Astronaut Menu** 直达按钮。
  注意：**"Enable EVA" 仍然只提供给带 `ControlModule` 的部件** —— 这条限制是为了避免把宇航员塞进邮箱、配重块这类非载人部件，不能放开。
  **Fix: in world, hold Alt and click a capsule with its own action to open the crew menu directly (default binding; rebindable in Settings → Keybindings to any modifier + key/mouse); an "Astronaut Menu" button is also added to the part stats menu. Note: the "Enable EVA" toggle still requires a `ControlModule` — that requirement exists to keep astronauts out of non-crew parts such as mailboxes.**
- **新增：模组配置接入游戏自带设置系统 / Mod config now lives in the game's settings**
  模组的配置不再散落在 `Mods` 目录的 `config.txt`，而是走与游戏 `ModsSettings` / `KeybindingsPC` 同款的 `SettingsBase` 机制，持久化到游戏设置文件夹下的 `AstronautModSettings.json`。乘员菜单的打开绑定作为一行 **"Open Astronaut Menu"** 注入到游戏 **设置 → Keybindings** 列表，默认 `Alt + 左键`，点击该行即可重新捕获（支持 Alt / Shift / Ctrl + 键 或 鼠标键）。旧 `config.txt` 中的 `allowUncrewedControl` 会在首次启动时自动迁移并删除。
  **New: mod config uses the game's own `SettingsBase` system (file: `AstronautModSettings.json` in the settings folder). The crew-menu open binding appears as an "Open Astronaut Menu" row in Settings → Keybindings, defaulting to Alt + LMB and fully rebindable (Alt/Shift/Ctrl + key or mouse). The legacy `config.txt` `allowUncrewedControl` is migrated once and then removed.**
- **修复：在 Hub 新建的宇航员进入蓝图（建造场景）后找不到**
  根因是 `WorldSave.Save()` 中的 `if (!DevSettings.DisableAstronauts)`：PC 版该开关恒为 `true` 且是返回常量的属性（很可能被 JIT 内联），导致所有走 `SavingCache` 的存档**从不**写入 `Astronauts.txt`，新建的宇航员只活在内存里，切场景重载即丢失。
  现在名册会在创建/解雇时同步直写 `Astronauts.txt`，并在每次 `WorldSave.Save` 后无条件补写；同时 Hub 每 10 秒的自动保存会先同步最新名册，避免用旧引用覆盖；场景重载时还会把内存中缺失的宇航员补回（已解雇的除外）。
  **Fix: the astronaut roster is now written straight to `Astronauts.txt` on create/discharge and after every `WorldSave.Save`, the Hub's periodic save syncs the live roster before writing, and scene reloads merge back any in-memory astronauts missing from disk.**

- **修复：探测器 + 空座位混编舰队，重启后火箭丢失控制权（Bug 5）**
  根因在 `RefreshRocketControl`：旧实现把火箭上**所有** `ControlModule.hasControl` 统一设成同一个值（= 任意座位有宇航员 或 允许无人控制）。只要火箭上存在任意一个 `CrewModule`（例如一个空座位）且 `allowUncrewedControl=false`，就会把探测器核心等原生控制部件的 `hasControl` 一并清零，导致火箭失去控制。首启时该空座位的 `CrewModule` 尚未注入（`crews.Length==0` 提前返回），所以仍有控制；重启后 `CrewModule` 已从存档恢复，于是被清零——与"首启有控制、重启无控制"的现象完全吻合（玩家怀疑是"按最后一个被检查的部件取状态、顺序不定"，真实机制同构：控制权不该被空座位清零）。
  新实现按**部件**取"逻辑或"：只管理带 `CrewModule` 的部件（按其自身座位占用决定控制权）；探测器/指令舱等**只带 `ControlModule`、不带 `CrewModule`** 的原生控制部件保留其原生控制权，绝不被空座位规则清零。因此只要火箭上任意部件有控制权，整箭就有控制权，与遍历顺序无关。
  **Fix: mixed fleets (probe + empty seat) lost control after restart. `RefreshRocketControl` used to overwrite *every* `ControlModule.hasControl` with one shared value, so a single empty seat (a `CrewModule` with no astronaut) zeroed the probe's control too. Now control is recomputed per part: only parts that carry a `CrewModule` are governed by seat occupancy; native control parts (probes/command pods with no `CrewModule`) keep their own control. The rocket has control if *any* part does — independent of iteration order.**

- **性能与清理 / Performance & cleanup**
  移除了全局 `Debug.Log` 拦截补丁（不再对每条日志做 Harmony 前缀，也不隐藏数值型诊断日志）；删除了未使用的 `buoyancyPostfixLogged` / `persistentState` 字段与一段只计算局部变量、从不落地的"分离模块"诊断补丁。  每帧热路径改为缓存：图标相机引用、飞行信息面板数组、燃料管分类数组只扫描一次；EVA 仪表盘刷新频率可在游戏设置里自定义（默认 20Hz，对驾驶员等同实时，且比 100Hz 少 5 倍 UI 开销）。

- **新增：更多可自定义的模组设置（游戏设置 → Keybindings 内的 "Astronaut Mod" 分区）**
  在乘员菜单绑定之外，下列选项现在也放进游戏设置，可随时开关 / 调节并即时生效：
  - **Allow control without crew**（无乘员也保留控制权）：开 / 关。
  - **EVA telemetry dashboard**（EVA 遥测面板总开关）：开 / 关，关闭后不再显示速度 / 高度 / 氧气等面板。
  - **Telemetry refresh rate**（遥测刷新频率）：在 5 / 10 / 20 / 30 / 60 Hz 之间点击循环，控制速度、高度、氧气等数值的更新频率。
  这些设置同样持久化到 `AstronautModSettings.json`，不再需要手动改文件。
  **New: more mod options are now in-game settings (the "Astronaut Mod" section inside Settings → Keybindings): Allow control without crew (on/off), EVA telemetry dashboard (on/off), and Telemetry refresh rate (cycles 5/10/20/30/60 Hz). All persist to `AstronautModSettings.json`.**
  **Removed the global `Debug.Log` interceptor and a dead diagnostic patch; cached the icon-camera reference, the flight-info panel array and the fuel-pipe category array so they are no longer re-scanned every frame; the EVA dashboard now refreshes at 20 Hz instead of 100 Hz.**

### 【v3.9 更新 / v3.9 Update】  
- 修复：外出执行 EVA 的宇航员不会再在任用菜单中被错误显示为可用人员 任用名单会同时检查持久 EVA 状态、世界中的 EVA 实体、飞行乘员与座位占用，避免同一宇航员被重复任用  
  **Fix: Astronauts on EVA are no longer incorrectly shown as available in the assignment menu. The roster checks persistent EVA state, live EVA entities, in-flight crew, and occupied seats to prevent duplicate assignment.**  
- 修复：从建造进入世界、再返回建造时，EVA 任务状态会正确恢复，避免宇航员消失、座位身份冲突或状态错位  
  **Fix: EVA mission state is restored correctly when moving from Build to World and back, preventing disappearing astronauts, conflicting seat identities, and roster desynchronization.**  
- 调整：长宇航员名单改为每页 8 人 并提供 Previous/Next Page 翻页按钮 原生隐藏的 Astronaut Seat 保持真实单座位行为；不会伪造不受存档与 EVA 系统支持的额外座位  
  **Changed: Long astronaut rosters now show 8 entries per page with Previous/Next Page controls.**

### 【v3.8 更新 / v3.8 Update】
- 新增：**按宇航员配置旗帜外观**，支持六种颜色预设以及持久化 PNG/JPG 自定义图片；默认设置下仍使用原版旗帜。
  **New: Per-astronaut flag appearance settings, with six color presets and persistent PNG/JPG custom images; the native flag remains unchanged by default.**
- 新增：自定义图片旗帜使用无边框旗面与单根黑色竖杆；旗面保持正向，特殊比例图片会保留轮廓。
  **New: Image flags use an unframed custom face with one black vertical pole; artwork remains upright and non-rectangular flags preserve their silhouette.**
- 修复：EVA 控制交接后偶发的 “No control” 状态，以及 EVA 时顶部火箭统计显示零值的问题。
  **Fix: Occasional “No control” after EVA handoff and zero-value rocket statistics shown during EVA.**
- 调整：宇航员列表点击后显示操作菜单，集中提供旗帜自定义与解雇入口。
  **Changed: Selecting an astronaut now opens an actions menu containing flag customization and discharge.**

### 【v3.38 更新 / v3.38 Update】
- 修复：传送功能在禁用作弊时仍能使用 — 现在已禁用  
  **Fix: Teleport feature was still usable when cheats are disabled — now disabled**
- 修复：宇航员可以在气态行星上行走、插旗 — 添加检查，现在禁止在无地形行星出舱和插旗  
  **Fix: Astronauts could walk and plant flags on gas giants — added checks to prevent EVA and flag planting on planets without terrain**
- 修复：现在宇航员的名字支持所有 Unicode 字符  
  **Fix: Astronaut names now support all Unicode characters**
- 优化：精简代码  
  **Optimization: Code streamlined**

### 【v3.7 重大更新 / v3.7 Major Update】
- 严重修复：进入建造场景时的渲染泄漏  
  **Critical fix: Rendering leak when entering build scene**
- 修复：宇航员EVA无浮力 — 添加 Water_Astronaut 组件  
  **Fix: Astronaut EVA had no buoyancy — added Water_Astronaut component**
- 修复：燃料管分离/输油问题  
  **Fix: Fuel pipe separation/fuel transfer issues**
- 修改：航天中心hub宇航员按钮  
  **Change: Space center hub astronaut button**
- 新增：宇航员传送 — 支持 Astronaut_EVA 传送  
  **New: Astronaut teleport — supports Astronaut_EVA teleport**
- 新增：宇航员仪表盘 — 显示速度、高度、燃料  
  **New: Astronaut dashboard — displays speed, altitude, fuel**

### 【v3.6.7 更新 / v3.6.7 Update】
- 修复：返回建造场景时部件数量为0（幽灵部件）  
  **Fix: Part count was 0 when returning to build scene (ghost parts)**

### 【v3.6.6 更新 / v3.6.6 Update】
- 修复：带有航天员的部件仍然消失（v3.6.5 修复无效）  
  **Fix: Parts with astronauts still disappearing (v3.6.5 fix was ineffective)**

### 【v3.6.5 更新 / v3.6.5 Update】
- 修复：返回建造场景时带有航天员的部件仍会消失  
  **Fix: Parts with astronauts still disappearing when returning to build scene**

### 【v3.6.4 更新 / v3.6.4 Update】
- 修复：已解雇的航天员在列表中仍然可见（渲染问题）  
  **Fix: Dismissed astronauts still visible in list (rendering issue)**
- 修复：返回建造场景时带有航天员的部件消失  
  **Fix: Parts with astronauts disappearing when returning to build scene**

### 【v3.6 更新 / v3.6 Update】
1. 模组重命名为 "AstronautMod"，简介更新为 "Enables the native astronaut/crew system on PC."  
   **Mod renamed to "AstronautMod", description updated to "Enables the native astronaut/crew system on PC."**
2. Hub "Astronauts" 按钮动态定位到成就按钮旁边  
   **Hub "Astronauts" button dynamically positioned next to the Achievements button**
3. "Plant Flag" 按钮移动到右下角  
   **"Plant Flag" button moved to bottom-right corner**
4. 蓝图页面修复 / Blueprint page fixes:
   - 当没有宇航员时，点击添加宇航员会自动进入创建宇航员页面  
     **When no astronauts exist, clicking add astronaut auto-navigates to create astronaut page**
   - 当宇航员在出舱(EVA)状态时，蓝图页面不再显示空白  
     **When astronauts are in EVA status, blueprint page no longer shows blank**
   - 无可用宇航员时显示提示信息和 "Create New Astronaut" 按钮  
     **When no astronauts are available, shows hint message and "Create New Astronaut" button**

### 【v3.5 更新 / v3.5 Update】
1. 插旗功能 / Plant Flag feature
   - 修复 flagPrefab 为NULL时无法插旗的问题  
     **Fixed inability to plant flag when flagPrefab is NULL**
   - 当旗帜预制体缺失时，自动创建简易旗帜（红色方块视觉）  
     **When flag prefab is missing, auto-creates simple flag (red cube visual)**
   - 修复 Flag.Start 在 mapIcon 为null时崩溃的问题  
     **Fixed Flag.Start crash when mapIcon is null**
   - 添加 ModGUI "Plant Flag" 按钮，仅在EVA状态时显示（右下角）  
     **Added ModGUI "Plant Flag" button, only shown during EVA status (bottom-right corner)**

2. 捡石头功能 / Rock Collection feature
   - 创建 RockSelector 回退实例（如果World场景中缺失）  
     **Created RockSelector fallback instance (if missing in World scene)**
   - 确保 DynamicTerrain 能正常注册石头到 RockSelector.rockInstances  
     **Ensured DynamicTerrain properly registers rocks to RockSelector.rockInstances**
   - 宇航员EVA状态下左键点击石头即可选中  
     **Astronaut in EVA can left-click rocks on surface to select them**
   - 选中后点击 "Collect Rock" 按钮收集石头  
     **Click "Collect Rock" button to collect after selection**

### 【v3.4 更新 / v3.4 Update】
1. 修复 Seat.OnStart 清除已分配座位的BUG（根本原因）  
   **Fixed the root cause of Seat.OnStart clearing assigned seats**
2. 添加 onPartUsed 事件未绑定的回退机制  
   **Added fallback mechanism for unbound onPartUsed event**
3. 添加 AttachableStatsMenu 缺失的回退机制  
   **Added fallback mechanism for missing AttachableStatsMenu**
4. 添加诊断日志  
   **Added diagnostic logging**
