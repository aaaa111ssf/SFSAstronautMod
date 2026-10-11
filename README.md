# AstronautMod

**English (you are here)** | [中文文档](#中文文档)

- [Installation](#installation)
- [Usage](#usage)
  - [■ Create Astronaut (Hub Page)](#-create-astronaut-hub-page)
  - [■ Assign Astronaut to Seat (Build Page)](#-assign-astronaut-to-seat-build-page)
  - [■ EVA Exit (World Page)](#-eva-exit-world-page)
  - [■ Configure Uncrewed Control](#-configure-uncrewed-control)
  - [■ Customize Astronaut Flags](#-customize-astronaut-flags)
  - [■ EVA Keybinds (World Page, EVA Status)](#-eva-keybinds-world-page-eva-status)
  - [■ Astronaut Settings](#-astronaut-settings)
  - [■ Astronaut Textures (Seat / EVA)](#-astronaut-textures-seat-eva)
  - [■ Flag Version](#-flag-version)
  - [■ Collision & Buoyancy Parameters](#-collision-buoyancy-parameters)
  - [■ Collect Rocks (World Page, EVA Status)](#-collect-rocks-world-page-eva-status)
- [Changelog](#changelog)
  - [v3.9.5 Update](#v395-update)
  - [v3.9.4 Update](#v394-update)
  - [v3.9.4 Update](#v394-update)
  - [v3.9.2 Update](#v392-update)
  - [v3.9.1 Update](#v391-update)
  - [v3.9 Update](#v39-update)
  - [v3.8 Update](#v38-update)
  - [v3.38 Update](#v338-update)
  - [v3.7 Major Update](#v37-major-update)
  - [v3.6.7 Update](#v367-update)
  - [v3.6.6 Update](#v366-update)
  - [v3.6.5 Update](#v365-update)
  - [v3.6.4 Update](#v364-update)
  - [v3.6 Update](#v36-update)
  - [v3.5 Update](#v35-update)
  - [v3.4 Update](#v34-update)

---

## Installation
1. Copy/Move AstronautMod.dll to: SFS game directory/Mods/ folder  
2. **Launch the game**
---

## Usage

### ■ Create Astronaut (Hub Page)
- Click the "Astronauts" button (next to the Achievements button) to create astronauts  

### ■ Assign Astronaut to Seat (Build Page)
- Click on the CrewModule part  
- Click "Assign" in the popup menu to assign an astronaut to a seat  
- If no astronauts exist, the create astronaut dialog will open automatically  
- If all astronauts are in EVA status, a message will be shown and you can create a new astronaut  

### ■ EVA Exit (World Page)
- Click on the CrewModule part  
- If the seat has an astronaut, an "EVA Exit" button will appear  
- Click "EVA Exit" to let the astronaut exit the vehicle  

### ■ Configure Uncrewed Control
- The mod creates `Mods/AstronautMod/config.txt` in the game installation directory. With `allowUncrewedControl=false`, every empty capsule using the mod's Crew/EVA adaptation requires at least one astronaut for control. Set it to `true` to allow uncrewed control.  

### ■ Customize Astronaut Flags
- Open Astronauts in the Hub, select an astronaut, then choose Customize Flag. White, red, blue, green, yellow, and purple presets are available.  
- To use an image, copy a `.png`, `.jpg`, or `.jpeg` file into `Mods/AstronautMod/Flags` under the game installation directory, then pick it in Select image file (8 per page, paged) — no manual filename typing. The previous persistent-data location remains readable for compatibility.  
- The setting belongs to the astronaut. Flags subsequently planted by that astronaut retain an independent appearance. The mod persists colors, filenames, and each planted flag's location so appearances are restored after loading a world.  
- Reset to native flag removes that astronaut's customization and immediately restores the native appearance on their existing flags.  

### ■ EVA Keybinds (World Page, EVA Status)
- When entering EVA, the current keybinds are shown once at the top of the screen (defaults: Plant Flag **F**, Teleport **G**)  
- Press F to plant a flag at the current position; the flag uses the current astronaut's customized appearance. Cannot plant within 30 meters of another flag or on gas giants  
- Press G to open the teleport menu (cheats must be enabled in the world)  
- Keybinds are customizable under Settings → Keybindings → Astronaut Mod (modifier keys and mouse buttons supported, Esc to cancel)  

### ■ Astronaut Settings
  - Open the game Settings (Esc) → Mods Settings. Under the "Astronaut" category there are two buttons: Attribute (Physique / Fuel / Collision / Water) and Sticker (Astronaut Textures / Flag). Requires UITools.  
- Changes apply instantly and are saved automatically; pages scroll with the mouse wheel or by dragging when the content exceeds the window (implemented with the game's native `SFS.UI.ScrollElement`).  
  -

### ■ Astronaut Textures (Seat / EVA)
  - Open Hub → Astronauts → pick an astronaut → Customize Texture. Separate textures for Seat (while seated) and EVA (outside the rocket), each restorable to native independently.  
- Textures are picked from the `Mods/AstronautMod/Textures` folder. No need to match the native resolution — replacements are normalized to the model's native size.  
- Switching back to native immediately destroys the overlay and restores the original renderers; switching back and forth is safe.  
- Per-astronaut settings are stored in `AstronautMod_appearance.json`.  
- Changed: the per-body-part seat texture mode was removed entirely — group detection and size normalization proved unreliable. Seat textures are now whole-body only.  

### ■ Flag Version
  - Open Settings → Mods Settings → Sticker and switch Flag version between Old (the image is the flag face only and the pole is generated separately — the previous behavior; pole color/width/height options only appear in this mode) and New (pole and flag as one single image fitted to the native flag bounds, with no separate pole or mask).  
- Switching versions immediately redraws every planted flag; both versions share the same per-astronaut flag configuration.  
- Per-astronaut flag colors and images are still set in the Hub → Astronauts menu.  

### ■ Collision & Buoyancy Parameters
- Open Settings → Mods Settings → Attribute, Collision section (requires UITools)  
- Body mass — rigidbody mass 0.05–2 (native hardcoded 0.2). Higher makes the astronaut harder to push around.  
- Center of mass height 0.2–2 (native 1.1) — affects tumbling and how the astronaut lands.  
- Impact tolerance 0.2–3 (native 1). The native fatal-impact check uses hardcoded inline thresholds (local x>30 or -y>15), so they are rescaled as fatal-speed × multiplier: below that the astronaut only staggers (ragdoll) instead of dying. The same multiplier applies to fatal water entry speed.  
- Water buoyancy / Water drag 0–3× (native 1×), applied only while submerged.  

### ■ Collect Rocks (World Page, EVA Status)
- After the astronaut exits, left-click on rocks on the surface  
- After selecting a rock, a "Collect Rock" option will appear  
- Click to collect the rock  
---

## Changelog

### v3.9.5 Update
- Attribute
Sticker
Improved: the settings sidebar is condensed into a single "Astronaut" category with two buttons (Attribute / Sticker); the six former sub-categories are now in-page sections reachable by scrolling.
- ，
SeatEVA
Changed: seat and EVA textures are now configured per astronaut from the Hub (Astronauts → pick astronaut → Customize Texture), with separate seat and EVA textures, each restorable to native. The mod settings page no longer hosts texture selection.
- Removed: the per-body-part seat texture mode, which proved too unreliable; only the whole-body texture remains.  
  - Fixed: settings content overflowing or being clipped on the left, inconsistent hint font sizes, and the layout jumping when switching texture modes.  
- Fixed: switching back to native after applying a texture now always works — the native sprites are never overwritten, only hidden behind an overlay.  
- Fixed: replaced textures no longer render misplaced — sprites are normalized to the native size instead of using a fixed pixels-per-unit.  
- ；
New: crew survival now depends on how the rocket was destroyed — crashes kill, recovery / launchpad cleanup / player-initiated destruction return the crew to the Hub, and world-rebuilding windows (launch, revert, load) leave crew state untouched. Destroying a rocket with a crew aboard prompts for confirmation.
- Fixed: astronauts no longer reappearing in a freshly created rocket — seat assignments stored on the parts themselves are now cleared too.  
- New: searchable crew-lifecycle diagnostics, including roster and seat-occupancy snapshots at key transitions.  

### v3.9.4 Update
- New: an astronaut tuning page in the game settings (Mods Settings → Astronaut, via UITools ConfigurationMenu — same approach as InfoOverload): Physique (model size / buoyancy / jump height / move speed), Fuel (drain rate / full fuel on EVA exit), Look (custom suit texture). Changes apply instantly and persist.  
- New: flagpole customization — color, width, and height, cycled through buttons in the existing flag menu (no preview, no overflow).  

### v3.9.4 Update
This release (all iterations since 3.9.3, published as 3.9.4) fixes the per-frame exception floods, settings breaking after mod reload, several EVA keybind/dashboard issues, and officially renames the mod to AstronautMod.
  - Fixed: ① the Custom Save Data bridge spammed `FileNotFoundException` every frame when the optional dependency was missing — rewritten as a pure-reflection bridge; ② the native `FlightInfoDrawer.Update` crashed with `IndexOutOfRangeException` on localized strings without a colon — taken over with safe slicing; ③ the native `LocationDrawer.Update` threw `ArgumentOutOfRangeException` the same way — taken over as well.  
- Fixed: after reloading the mod, `ModSettings.main` pointed at a destroyed component — settings toggles went dead and the telemetry panel couldn't be turned off. `Load()` now recreates the settings object, rows resolve the current instance on click, and the panel lifetime is tracked via its GameObject.  
- Fixed: C# `is Astronaut_EVA` still matches destroyed astronaut objects left in `player.Value`, wrongly showing the dashboard during launch and breaking EVA reminders/keys. All checks now use Unity's overloaded `!= null`.  
- Changed: "Plant Flag" / "Teleport" floating buttons replaced by configurable keybinds (defaults F / G) under Settings → Keybindings → Astronaut Mod. Entering EVA shows a one-time key reminder; teleport works only during EVA with cheats enabled.  
- Enhanced: the EVA dashboard shows planet / speed / vertical speed / altitude / fuel (temperature removed — SFS has no temperature system).  
- Changed: flag customization kept in its simple form — per-astronaut colors, click-to-pick images from `Mods/AstronautMod/Flags` (no filename typing), pole-side anchoring fix, and geometry log lines. The layout presets / live preview window were removed after testing.  
- Changed: renamed AstronautUnlocker → AstronautMod (assembly, namespace, Harmony ID); the DLL name now matches its folder. Delete the old AstronautUnlocker.dll when upgrading.  

### v3.9.2 Update
This release fixes two regressions — "World/Blueprint pages break after reloading the mod" and "the astronaut (EVA) telemetry dashboard disappeared" — and upgrades the pre-launch "missing astronaut" prompt into a choosable two-button dialog.
- 。
Fix: the root cause was a `SceneManager.sceneUnloaded` cleanup that fired on every normal scene transition — it both unsubscribed the Hub/Build/World scene handlers before they ran (menu couldn't open, Hub UI vanished) and destroyed live `__Astronaut*` objects by prefix, including the `UpdateDriver` that refreshes the EVA telemetry dashboard, so the dashboard disappeared. Cleanup now runs only inside `Load()`: on reload it first destroys its tracked objects then name-prefixed orphans, never touching normal play. The EVA telemetry dashboard (speed/altitude/fuel) is restored. `Load()` also re-runs the active scene's init (Hub/Build/World) at its end, since reloading a mod re-invokes `Load()` without reloading the scene — without this, the current scene's menu/state/EVA infra would be cleaned yet never rebuilt, requiring a manual scene switch or game restart.
  - Improved: a pre-launch "missing astronaut" warning. Intercept condition unchanged. The dialog now offers two choices: "Continue Anyway" launches regardless (player accepts the no-control risk); "Cancel" closes the prompt without launching. Mixed fleets (probe + empty crew seat) keep control via the probe and are not warned.  

### v3.9.1 Update
This release rewrites how injected crew capsules store their data, fixing the common root cause of the "rocket disappears after launch", "control lost after revert" and "config lost after restart" bugs.
- Fix: per-part state (capacity + astronaut per seat) is now written into the part's own native text variables, so it travels inside blueprints, rocket saves, and world saves through the vanilla pipeline.  
- New (optional dependency): when Custom Save Data is installed, crew data is also mirrored into blueprint and world save custom data. Without it, the bridge disables itself.  
- Fix: after a blueprint is spawned the mod now guarantees a controllable rocket exists, assigns the player target and re-points the map view, so neither the rocket nor the camera can get stranded.  
- Fix: a world-rebuild window prevents old capsules from killing their crew and new seats from wiping restored occupants during loads and reverts.  
- Astronaut Menu
Fix: in world, hold Alt and click a capsule with its own action to open the crew menu directly (default binding; rebindable in Settings → Keybindings to any modifier + key/mouse); an "Astronaut Menu" button is also added to the part stats menu. Note: the "Enable EVA" toggle still requires a `ControlModule` — that requirement exists to keep astronauts out of non-crew parts such as mailboxes.
- "Open Astronaut Menu"
New: mod config uses the game's own `SettingsBase` system (file: `AstronautModSettings.json` in the settings folder). The crew-menu open binding appears as an "Open Astronaut Menu" row in Settings → Keybindings, defaulting to Alt + LMB and fully rebindable (Alt/Shift/Ctrl + key or mouse). The legacy `config.txt` `allowUncrewedControl` is migrated once and then removed.
- Fix: the astronaut roster is now written straight to `Astronauts.txt` on create/discharge and after every `WorldSave.Save`, the Hub's periodic save syncs the live roster before writing, and scene reloads merge back any in-memory astronauts missing from disk.  
- Fix: mixed fleets (probe + empty seat) lost control after restart. `RefreshRocketControl` used to overwrite *every* `ControlModule.hasControl` with one shared value, so a single empty seat (a `CrewModule` with no astronaut) zeroed the probe's control too. Now control is recomputed per part: only parts that carry a `CrewModule` are governed by seat occupancy; native control parts (probes/command pods with no `CrewModule`) keep their own control. The rocket has control if *any* part does — independent of iteration order.  
  - New: more mod options are now in-game settings (the "Astronaut Mod" section inside Settings → Keybindings): Allow control without crew (on/off), EVA telemetry dashboard (on/off), and Telemetry refresh rate (cycles 5/10/20/30/60 Hz). All persist to `AstronautModSettings.json`.  
Removed the global `Debug.Log` interceptor and a dead diagnostic patch; cached the icon-camera reference, the flight-info panel array and the fuel-pipe category array so they are no longer re-scanned every frame; the EVA dashboard now refreshes at 20 Hz instead of 100 Hz.

### v3.9 Update
- Fix: Astronauts on EVA are no longer incorrectly shown as available in the assignment menu. The roster checks persistent EVA state, live EVA entities, in-flight crew, and occupied seats to prevent duplicate assignment.  
- Fix: EVA mission state is restored correctly when moving from Build to World and back, preventing disappearing astronauts, conflicting seat identities, and roster desynchronization.  
- Changed: Long astronaut rosters now show 8 entries per page with Previous/Next Page controls.  

### v3.8 Update
- New: Per-astronaut flag appearance settings, with six color presets and persistent PNG/JPG custom images; the native flag remains unchanged by default.  
- New: Image flags use an unframed custom face with one black vertical pole; artwork remains upright and non-rectangular flags preserve their silhouette.  
- Fix: Occasional “No control” after EVA handoff and zero-value rocket statistics shown during EVA.  
- Changed: Selecting an astronaut now opens an actions menu containing flag customization and discharge.  

### v3.38 Update
- Fix: Teleport feature was still usable when cheats are disabled — now disabled  
- Fix: Astronauts could walk and plant flags on gas giants — added checks to prevent EVA and flag planting on planets without terrain  
- Fix: Astronaut names now support all Unicode characters  
- Optimization: Code streamlined  

### v3.7 Major Update
- Critical fix: Rendering leak when entering build scene  
- Fix: Astronaut EVA had no buoyancy — added Water_Astronaut component  
- Fix: Fuel pipe separation/fuel transfer issues  
- Change: Space center hub astronaut button  
- New: Astronaut teleport — supports Astronaut_EVA teleport  
- New: Astronaut dashboard — displays speed, altitude, fuel  

### v3.6.7 Update
- Fix: Part count was 0 when returning to build scene (ghost parts)  

### v3.6.6 Update
- Fix: Parts with astronauts still disappearing (v3.6.5 fix was ineffective)  

### v3.6.5 Update
- Fix: Parts with astronauts still disappearing when returning to build scene  

### v3.6.4 Update
- Fix: Dismissed astronauts still visible in list (rendering issue)  
- Fix: Parts with astronauts disappearing when returning to build scene  

### v3.6 Update
1. Mod renamed to "AstronautMod", description updated to "Enables the native astronaut/crew system on PC."  
2. Hub "Astronauts" button dynamically positioned next to the Achievements button  
3. "Plant Flag" button moved to bottom-right corner  
   - When no astronauts exist, clicking add astronaut auto-navigates to create astronaut page  
   - When astronauts are in EVA status, blueprint page no longer shows blank  
   - When no astronauts are available, shows hint message and "Create New Astronaut" button  

### v3.5 Update
   - Fixed inability to plant flag when flagPrefab is NULL  
   - When flag prefab is missing, auto-creates simple flag (red cube visual)  
   - Fixed Flag.Start crash when mapIcon is null  
   - Added ModGUI "Plant Flag" button, only shown during EVA status (bottom-right corner)  
   - Created RockSelector fallback instance (if missing in World scene)  
   - Ensured DynamicTerrain properly registers rocks to RockSelector.rockInstances  
   - Astronaut in EVA can left-click rocks on surface to select them  
   - Click "Collect Rock" button to collect after selection  

### v3.4 Update
1. Fixed the root cause of Seat.OnStart clearing assigned seats  
2. Added fallback mechanism for unbound onPartUsed event  
3. Added fallback mechanism for missing AttachableStatsMenu  
4. Added diagnostic logging  

---

## 中文文档

[English](#astronautmod) | **中文（你在看的语言）**

- [安装方法](#安装方法)
- [使用方法](#使用方法)
  - [■ 创建宇航员（Hub页面）](#-创建宇航员hub页面)
  - [■ 分配宇航员到座位（Build页面）](#-分配宇航员到座位build页面)
  - [■ 出舱EVA（World页面）](#-出舱evaworld页面)
  - [■ 配置空舱控制](#-配置空舱控制)
  - [■ 宇航员旗帜自定义](#-宇航员旗帜自定义)
  - [■ EVA 快捷键（World页面，EVA状态）](#-eva-快捷键world页面eva状态)
  - [■ 宇航员参数设置](#-宇航员参数设置)
  - [■ 宇航员贴图（座椅 / EVA）](#-宇航员贴图座椅-eva)
  - [■ 旗帜版本切换](#-旗帜版本切换)
  - [■ 宇航员碰撞与浮力参数](#-宇航员碰撞与浮力参数)
  - [■ 捡石头（World页面，EVA状态）](#-捡石头world页面eva状态)
- [更新日志](#更新日志)
  - [v3.9.5 更新（设置页精简 / 贴图按人配置 / 乘员回收修正）](#v395-更新设置页精简-贴图按人配置-乘员回收修正)
  - [v3.9.4 更新（补充：宇航员参数自定义）](#v394-更新补充宇航员参数自定义)
  - [v3.9.4 更新](#v394-更新)
  - [v3.9.2 更新](#v392-更新)
  - [v3.9.1 更新](#v391-更新)
  - [v3.9 更新](#v39-更新)
  - [v3.8 更新](#v38-更新)
  - [v3.38 更新](#v338-更新)
  - [v3.7 重大更新](#v37-重大更新)
  - [v3.6.7 更新](#v367-更新)
  - [v3.6.6 更新](#v366-更新)
  - [v3.6.5 更新](#v365-更新)
  - [v3.6.4 更新](#v364-更新)
  - [v3.6 更新](#v36-更新)
  - [v3.5 更新](#v35-更新)
  - [v3.4 更新](#v34-更新)

---

## 安装方法
1. 将 AstronautMod.dll 复制/移动到：SFS游戏目录/Mods/ 文件夹下
2. 启动游戏

## 使用方法

### ■ 创建宇航员（Hub页面）
- 点击 "Astronauts" 按钮（位于成就按钮旁边）创建宇航员

### ■ 分配宇航员到座位（Build页面）
- 点击载入舱(CrewModule)部件
- 在弹出的菜单中点击 "Assign" 分配宇航员到座位
- 如果没有宇航员，会自动弹出创建宇航员对话框
- 如果所有宇航员都在出舱(EVA)状态，会显示提示并可创建新宇航员

### ■ 出舱EVA（World页面）
- 点击载入舱部件
- 如果座位有宇航员，会显示 "EVA Exit" 按钮
- 点击 "EVA Exit" 即可让宇航员出舱

### ■ 配置空舱控制
- 模组会在游戏安装目录的 `Mods/AstronautMod/config.txt` 创建配置文件。使用 `allowUncrewedControl=false` 时，所有已启用模组 Crew/EVA 适配的空舱都需要至少一名宇航员才可控制；设为 `true` 时，空舱仍可控制。

### ■ 宇航员旗帜自定义
- 在 Hub 页面打开 Astronauts，点击目标宇航员后选择 Customize Flag。可选择白、红、蓝、绿、黄或紫色旗帜。
- 如需使用图片，请把 `.png`、`.jpg` 或 `.jpeg` 文件放入游戏安装目录下的 `Mods/AstronautMod/Flags` 文件夹，然后在 Select image file 中点选即可应用（每页 8 个、可翻页，无需手动输入文件名）。旧版持久化数据目录仍可读取，以避免既有配置失效。
- 设置会绑定到宇航员；之后由该宇航员插下的旗帜会记录独立外观。颜色、文件名与每面已插旗帜的位置均由模组配置保存，因此重新加载世界后仍会恢复。
- 选择 Reset to native flag 会取消该宇航员的自定义设置；其已插下的旗帜会立即还原为原生外观。

### ■ EVA 快捷键（World页面，EVA状态）
- 宇航员出舱进入 EVA 时，屏幕上方会提示一次当前按键（默认：插旗 F、传送 G
- 按 F 在当前位置插旗；旗帜会使用当前宇航员的自定义外观。不能距已有旗帜 30 米以内插旗，气态行星无法插旗
- 按 G 打开传送菜单（需要该世界开启作弊）
- 按键可在 游戏设置 → Keybindings → Astronaut Mod 分区自定义（支持 Shift/Ctrl/Alt 修饰键与鼠标键，Esc 取消）

### ■ 宇航员参数设置
- 打开游戏 设置（Esc）→ 左侧 Mods Settings，本模组只有两个分类按钮
  - Attribute —— 影响宇航员本身能力的参数（Physique / Fuel / Collision / Water 四个区块）
  - Sticker —— 外观相关（Astronaut Textures / Flag 两个区块）
点进去后各区块在页内上下滚动查看（内容超出窗口可滚轮/拖拽滚动）。
区块标题不是分类按钮 —— 六个分类按钮会把左侧栏撑得又高又挤，非常占空间。
（需要安装 UITools）
- 修改即时生效并自动保存；页面内容超出窗口时可滚轮/拖拽滚动（用游戏原生 `SFS.UI.ScrollElement` 实现的滚动视口）
- 可调参数：
  - Model size 模型大小 0.5–2.5×
  - Buoyancy (gravity) 浮力/重力系数 0（漂浮）–1.5
  - Jump height 跳跃高度 0.2–4×
  - Move speed 移动速度 0.2–4×
  - Fuel drain rate 燃料消耗倍率 0–5×
  - Full fuel on EVA exit 出舱自动满燃料（开关）
  - Body mass / Center of mass / Impact tolerance / Water buoyancy / Water drag 见下方「碰撞与浮力参数」
  - 座椅 / EVA 贴图 不在设置页，按宇航员单独设置，见下方「宇航员贴图」
  - Flag version 旗帜外观版本，见下方「旗帜版本切换」

### ■ 宇航员贴图（座椅 / EVA）
- 打开 Hub → Astronauts → 点选某个宇航员 → Customize Texture每个宇航员单独配置
菜单内有两套贴图：
  - Seat: 坐在座椅上时的外观；EVA: 出舱后的外观
  - 每套都能单独点 use native 还原为原版外观
- 贴图从 `Mods/AstronautMod/Textures` 点选，尺寸无需与原图一致，任意分辨率都会按原尺寸归一化后贴合
- 切回原生会立即销毁覆盖层并恢复原生渲染器，可反复来回切换
- 配置按宇航员名保存在 `AstronautMod_appearance.json`
- 变更：早期版本提供过座椅「部分贴图」（头/身体/腿各一张），因分组识别与尺寸归一化
错误率较高（替换后错位、切回原生重建失败），已整体移除，座椅只保留整体贴图

### ■ 旗帜版本切换
- 打开 设置 → Mods Settings → Sticker 页的 Flag 区块，Flag version 点击切换：
  - Old (cloth only)：旧版（原有行为）—— 贴图只作为旗面，旗杆由模组单独生成（旗杆的颜色/粗细/高度选项仅在旧版下显示）
  - New (pole + cloth)：新版 —— 旗杆 + 旗面作为一整张贴图，按原生旗帜包围盒整体铺放，不再单独生成旗杆与遮罩
- 切换版本后所有已插下的旗帜立即重绘；新旧版本共用同一份按宇航员保存的旗帜配置
- 每面旗帜的颜色与图片仍在 Hub → Astronauts 里按宇航员单独设置

### ■ 宇航员碰撞与浮力参数
- 打开 设置 → Mods Settings → Attribute 页的 Collision 区块（需要 UITools）
- Body mass 碰撞强度：刚体质量 0.05–2（原生写死 0.2）。越大越难被推动
- Center of mass 重心高度 0.2–2（原生 1.1）：影响翻滚与落地手感
- Impact tolerance 碰撞承受倍率 0.2–3（原生 1）：原生摔死判定是内联硬编码阈值（局部 x>30 或 -y>15），这里改为「致命速度 × 倍率」，低于该值只摔踉跄（进入 ragdoll）不再死亡；同样作用于入水致命速度
- Water buoyancy / Water drag 水中浮力与水中阻力 0–3×（原生 1×），只在浸水时生效

### ■ 捡石头（World页面，EVA状态）
- 宇航员出舱后，左键点击地表上的石头
- 选中石头后会弹出 "Collect Rock" 选项
- 点击收集石头

## 更新日志

### v3.9.5 更新（设置页精简 / 贴图按人配置 / 乘员回收修正）
- 改进：设置页左侧栏精简为一个分类、两个按钮
此前是 6 个子分类（Physique / Fuel / Physics / Look / Seat / Flag），
六个分类按钮把左栏撑得又高又挤。现在只保留模组名作分类标题、下面两个按钮：
Attribute（Physique / Fuel / Collision / Water 四个区块）与
Sticker（Astronaut Textures / Flag 两个区块）。区块标题在页内呈现，
配合滚轮/拖拽滚动浏览（用游戏原生 `SFS.UI.ScrollElement` 实现的滚动视口）。
- 改进：座椅与 EVA 贴图改为按宇航员配置，入口移到 Hub
模组设置页不再提供贴图选择。改为
Hub → Astronauts → 点选某个宇航员 → Customize Texture
菜单内分 Seat（坐在座椅上）与 EVA（出舱）两套贴图，各自独立、可单独还原为原生。
配置按宇航员名保存在 `AstronautMod_appearance.json`。
- 移除：座椅「部分贴图」（分身体/腿/脚）
该模式依赖分组识别与尺寸/pivot 归一化，实测错误率过高（替换后错位成"空气"、
切回原生重建失败），已整体移除，只保留整体贴图。
- 修复：设置页显示不全 / 文字被裁 / 切换后错位
  - 内容宽度此前算宽了超出窗口被遮罩从左边裁掉，现严格限制在净内容区内。
  - 提示文字改为按长度估算行数决定高度并固定字号（此前写死高度 + 自动缩放导致长短不一）。
  - 模式切换改用「换一个新容器」而非在旧容器上删除再添加（`Destroy` 延迟到帧末，
会让子节点数翻倍、布局按翻倍数量计算而整页乱飘）。
- 修复：替换贴图后无法切回原生
整体模式改为不修改原生 sprite，只禁用原生渲染器并叠加一张覆盖层；
还原时只需销毁覆盖层并重新启用渲染器。此前需要缓存/回写原生精灵，
一旦把自定义贴图误当作"原始状态"存入缓存，就会导致切回原生后怎么都去不掉。
- 修复：贴图替换后显示成"空气"
精灵生成不再固定 PPU=100（原图 164×329px 等，换成任意分辨率的图后世界尺寸完全不同，
配上原组缩放就整体错位），改为按被替换部位的原生尺寸动态归一化。
- 新增：宇航员回收 / 生死判定
原生所有火箭销毁都走 `RocketManager.DestroyRocket(rocket, reason)`，此前一律判定乘员死亡。
现在按场景区分：坠毁/撞击 → 判定死亡；回收火箭 / 发射台清场 / 玩家主动摧毁 → 回 Hub 待命
世界重建窗口（发射/回退/读档）内不动任何状态。
玩家主动摧毁且座舱有人时，会弹出确认框询问"杀死"还是"保留（取消销毁）"。
- 修复：新建火箭后宇航员跟着一块回来
根因是乘员名有两处存储：除内存缓存外，座位名还绑定在部件的可保存变量上
（新部件建出来时会读回来填座位）。现在新建火箭会一并清空部件上的座位变量（保留容量设置）。
- 新增：乘员生命周期诊断日志
乘员状态变化会输出可检索的链路日志（如 `[Crew] crash-killed Alice reason=TerrainCollision`），
退出到 Hub / 新建火箭时会附赠名册与座位占用快照，便于排查。

### v3.9.4 更新（补充：宇航员参数自定义）
- 新增：宇航员可调参数页（游戏设置 → Mods Settings → Astronaut，UITools ConfigurationMenu 实现，与 InfoOverload 同款）
三个子页：Physique（模型大小 / 浮力（重力系数）/ 跳跃高度 / 移动速度）、Fuel（燃料消耗倍率 / 出舱满燃料）、Look（EVA 外观贴图，`Mods/AstronautMod/Textures` 内点选）。参数改动即时生效并自动保存，页面随设置窗口自适应。
实现要点：浮力通过 Harmony 前缀缩放 `Astronaut_EVA.OnFixedUpdate` 的 gravity 参数实现（游戏本身手动施加重力）；跳跃/速度通过缩放 prefab 的 `jumpVelocity` / `maxSpeed` 曲线（基准曲线只取一次，避免重复累积）；燃料通过前缀缩放 `EVA_Resources.ConsumeFuel` 的消耗量；出舱生成时应用全部参数并（可选）补满燃料。
- 新增：旗杆自定义
旗帜样式菜单新增三个循环选项：Pole color（黑/灰/棕/金/白）、Pole width（窄/正常/宽）、Pole height（短/全高/加高，矮杆从地面向上生长）。本次未加入预览，选项在原有菜单内以按钮循环切换，不会造成界面溢出。

### v3.9.4 更新
本次更新（涵盖 3.9.3 以来的全部迭代，对外版本号为 3.9.4）集中解决了三类每帧刷屏的异常、模组重载后设置失灵、EVA 快捷键与遥测面板的多个问题，并将模组正式更名为 AstronautMod。
- 修复：三类每帧异常（共约 7700 次，此前原生崩溃退出的主要来源）
  - ① 缺少可选依赖 Custom Save Data 时，保存桥接层每帧抛 `FileNotFoundException`。已重写为纯反射桥接（含 DynamicMethod 事件代理），未安装该依赖时静默停用。
  - ② 游戏原生 `FlightInfoDrawer.Update` 用 `Split(':')[1]` 取值，本地化串缺冒号时每帧数组越界。已由模组安全接管，缺失分隔符时退化为整串显示。
  - ③ 游戏原生 `LocationDrawer.Update` 的 `Substring(0, IndexOf(":"))` 同样每帧抛 `ArgumentOutOfRangeException`。已同样安全接管，俯仰角逻辑照常驱动。
- 修复：模组重载后设置全部失灵 / 面板关不掉
重载模组时 `Load()` 销毁设置对象但未重建，`ModSettings.main` 指向已销毁组件（Unity 假 null）→ 设置行点击失灵、遥测面板默认显示且无法关闭。现在 `Load()` 重建设置对象，设置行点击时实时解析当前实例，面板存活以 GameObject 判断（跨场景自愈）。
- 修复：已销毁宇航员引用被误判为 EVA
C# `is Astronaut_EVA` 对已销毁对象仍返回 true，导致发射时错误显示遥测面板、EVA 提醒与按键错乱。所有相关判断改为 Unity 重载 `!= null`。
- 变更：插旗/传送改为可配置快捷键（默认 F / G），移除悬浮按钮
在 游戏设置 → Keybindings → Astronaut Mod 分区可改键（支持修饰键与鼠标键）；进 EVA 时提示一次当前按键；传送仅限 EVA 且需开启作弊。
- 增强：EVA 遥测仪表盘
EVA 时显示行星 / 速度 / 垂直速度 / 高度 / 燃料；温度已移除（SFS 无温度系统）。
- 变更：自定义旗帜简化，保留本体功能
按宇航员染六色、从 `Mods/AstronautMod/Flags` 的 PNG/JPG 点选贴图（免手输名字）、贴图贴杆修复（从旗杆一侧起铺，不再居中）、几何日志（Player.log 的 `Flag geometry:` 行）。旗帜布局预设与实时预览窗口试运行后已按反馈移除。
- 变更：模组更名 AstronautUnlocker → AstronautMod
程序集、命名空间、Harmony ID 统一为 AstronautMod，DLL 名与文件夹名一致（`Mods/AstronautMod/AstronautMod.dll`）。升级时请删除旧的 AstronautUnlocker.dll。

### v3.9.2 更新
本次更新修复"重载模组后世界 / 蓝图页面出问题"与"宇航员(EVA)仪表盘消失"两大回归，并改进发射前"缺失宇航员"提示为可选择的双按钮。
- 修复：重载模组后世界 / 蓝图页面报错，并恢复 EVA 宇航员仪表盘
根因：`Mod` 基类没有卸载钩子，重载模组时旧场景事件与一批 `DontDestroyOnLoad` 对象（驱动、宇航员状态、乘员菜单、RockSelector、ModSettings 等）会残留。最初用 `SceneManager.sceneUnloaded` 做清理，但该事件在每次正常场景切换（标题→Hub、Hub→蓝图、蓝图→世界…）都会触发——既会在 Hub 初始化前 `-=` 取消 `OnHubSceneLoaded` 等场景处理器（菜单打不开、Hub UI 消失），又会按 `__Astronaut` 前缀销毁活动对象，连托管 EVA 仪表盘刷新循环的 `UpdateDriver` 也被误删，导致仪表盘消失、模组几乎无法使用
修复：清理逻辑只在 `Load()` 内进行（SFS 重载模组会再次调用 `Load()`）。`Load()` 开头先 `DestroyPersistentObjects()`（销毁上一实例追踪的对象并清空集合）再 `DestroyOrphanObjects()`（按前缀清理跨程序集残留），绝不触碰正常场景切换。EVA 遥测仪表盘（速度 / 高度 / 燃料）随之恢复正常。此外，`Load()` 末尾会按当前实际处于的场景手动重跑对应初始化（Hub / 蓝图 / 世界）：因为重载模组只重调 `Load()`、不重新加载场景，若不在 `Load()` 里补这一刀，重载后当前场景的菜单 / 宇航员状态 / EVA 基础设施会被清掉却不再重建，必须手动切场景或重启游戏 UI 才回来。
- 改进：发射前"缺失宇航员"警告改为双按钮（仍继续 / 取消）
拦截条件不变：若火箭含乘员舱、却全部为空座、且不存在任何控制来源（原生 `ControlModule` 有控制、或某个乘员舱已有人、或开启"无乘员控制"），在 `BuildManager.Launch` 之前弹出提示。现在提供两个选择：
  - 仍继续 / Continue Anyway：忽略警告，照常发射（由玩家自行承担无控制风险）；
  - 取消 / Cancel：关闭提示，不发射，回到蓝图修正后再发射。
混合舰队（探测器 + 空乘员舱）因为有探测器提供控制，不会触发此警告。

### v3.9.1 更新
本次更新重写了注入型乘员舱的数据存储方式，解决了"发射后火箭消失"、"回退后失去控制"与"重启后配置丢失"这三个长期问题的共同根因。
- 修复：重启 SFS 后自定义太空舱配置丢失（原 Bug 3）
每个被模组适配的部件实例会把自身的"Crew Capacity / 每个座位的宇航员"写入该部件原生的文本变量（`AstronautMod_Cap`、`AstronautMod_Seat0…`）。这些变量随 `PartSave.TEXT_VARIABLES` 走原生存档管线，因此会随蓝图、火箭存档、世界存档、快速存档一起落盘，不再依赖全局 JSON 或 InstanceID 缓存。
- 新增（可选依赖）：Custom Save Data 集成
若安装了 [Custom-Save-Data-SFS](https://github.com/AstroTheRabbit/Custom-Save-Data-SFS)（`CustomSaveData.dll`），模组还会把乘员配置额外写入蓝图 customData 与世界存档 customData 作为冗余备份；未安装时该桥接自动禁用，模组照常工作。
- 修复：发射后火箭消失、地图视图锁死在太阳（原 Bug 1）
过去发射时乘员名要通过脆弱的"部件名 → 宇航员名"全局缓存重建；一旦某个座舱恢复失败，`hasControl` 就为假，`RocketManager.SpawnBlueprint` 找不到可控火箭，玩家目标为空、摄像机停在原点（太阳）。现在乘员随部件恢复，同时在 `SpawnBlueprint` 之后强制校正控制权、玩家目标与地图视图目标。
- 修复："恢复到发射状态" / 回退 30 秒 / 3 分钟后宇航员消失并失去控制（原 Bug 2）
新增"世界重建窗口"：载入存档、回退与重新生成火箭期间，旧座舱的销毁不会把宇航员判定为阵亡，新座椅的初始化也不会清空已经记录的乘员。乘员通过部件变量恢复，控制权随之恢复。
- 修复：已有 action 的自定义太空舱（如 Vanilla Redstone 的 Mercury 舱）无法进入宇航员菜单（原 Bug 4）
在 World 场景中，若太空舱自带 action（点击只触发该 action），请 按住 Alt 再点击该舱（默认绑定，可在游戏设置 → Keybindings 中自定义为任意 修饰键 + 键/鼠标），即可直接打开乘员菜单；部件统计菜单中另提供 Astronaut Menu 直达按钮。
注意："Enable EVA" 仍然只提供给带 `ControlModule` 的部件 —— 这条限制是为了避免把宇航员塞进邮箱、配重块这类非载人部件，不能放开。
- 新增：模组配置接入游戏自带设置系统 / Mod config now lives in the game's settings
模组的配置不再散落在 `Mods` 目录的 `config.txt`，而是走与游戏 `ModsSettings` / `KeybindingsPC` 同款的 `SettingsBase` 机制，持久化到游戏设置文件夹下的 `AstronautModSettings.json`。乘员菜单的打开绑定作为一行 "Open Astronaut Menu" 注入到游戏 设置 → Keybindings 列表，默认 `Alt + 左键`，点击该行即可重新捕获（支持 Alt / Shift / Ctrl + 键 或 鼠标键）。旧 `config.txt` 中的 `allowUncrewedControl` 会在首次启动时自动迁移并删除。
- 修复：在 Hub 新建的宇航员进入蓝图（建造场景）后找不到
根因是 `WorldSave.Save()` 中的 `if (!DevSettings.DisableAstronauts)`：PC 版该开关恒为 `true` 且是返回常量的属性（很可能被 JIT 内联），导致所有走 `SavingCache` 的存档从不写入 `Astronauts.txt`，新建的宇航员只活在内存里，切场景重载即丢失。
现在名册会在创建/解雇时同步直写 `Astronauts.txt`，并在每次 `WorldSave.Save` 后无条件补写；同时 Hub 每 10 秒的自动保存会先同步最新名册，避免用旧引用覆盖；场景重载时还会把内存中缺失的宇航员补回（已解雇的除外）。
- 修复：探测器 + 空座位混编舰队，重启后火箭丢失控制权（Bug 5）
根因在 `RefreshRocketControl`：旧实现把火箭上所有 `ControlModule.hasControl` 统一设成同一个值（= 任意座位有宇航员 或 允许无人控制）。只要火箭上存在任意一个 `CrewModule`（例如一个空座位）且 `allowUncrewedControl=false`，就会把探测器核心等原生控制部件的 `hasControl` 一并清零，导致火箭失去控制。首启时该空座位的 `CrewModule` 尚未注入（`crews.Length==0` 提前返回），所以仍有控制；重启后 `CrewModule` 已从存档恢复，于是被清零——与"首启有控制、重启无控制"的现象完全吻合（玩家怀疑是"按最后一个被检查的部件取状态、顺序不定"，真实机制同构：控制权不该被空座位清零）。
新实现按部件取"逻辑或"：只管理带 `CrewModule` 的部件（按其自身座位占用决定控制权）；探测器/指令舱等只带 `ControlModule`、不带 `CrewModule` 的原生控制部件保留其原生控制权，绝不被空座位规则清零。因此只要火箭上任意部件有控制权，整箭就有控制权，与遍历顺序无关。
- 性能与清理 / Performance & cleanup
移除了全局 `Debug.Log` 拦截补丁（不再对每条日志做 Harmony 前缀，也不隐藏数值型诊断日志）；删除了未使用的 `buoyancyPostfixLogged` / `persistentState` 字段与一段只计算局部变量、从不落地的"分离模块"诊断补丁。 每帧热路径改为缓存：图标相机引用、飞行信息面板数组、燃料管分类数组只扫描一次；EVA 仪表盘刷新频率可在游戏设置里自定义（默认 20Hz，对驾驶员等同实时，且比 100Hz 少 5 倍 UI 开销）。
- 新增：更多可自定义的模组设置（游戏设置 → Keybindings 内的 "Astronaut Mod" 分区）
在乘员菜单绑定之外，下列选项现在也放进游戏设置，可随时开关 / 调节并即时生效：
  - Allow control without crew（无乘员也保留控制权）：开 / 关。
  - EVA telemetry dashboard（EVA 遥测面板总开关）：开 / 关，关闭后不再显示速度 / 高度 / 氧气等面板。
  - Telemetry refresh rate（遥测刷新频率）：在 5 / 10 / 20 / 30 / 60 Hz 之间点击循环，控制速度、高度、氧气等数值的更新频率。
这些设置同样持久化到 `AstronautModSettings.json`，不再需要手动改文件。

### v3.9 更新
- 修复：外出执行 EVA 的宇航员不会再在任用菜单中被错误显示为可用人员 任用名单会同时检查持久 EVA 状态、世界中的 EVA 实体、飞行乘员与座位占用，避免同一宇航员被重复任用
- 修复：从建造进入世界、再返回建造时，EVA 任务状态会正确恢复，避免宇航员消失、座位身份冲突或状态错位
- 调整：长宇航员名单改为每页 8 人 并提供 Previous/Next Page 翻页按钮 原生隐藏的 Astronaut Seat 保持真实单座位行为；不会伪造不受存档与 EVA 系统支持的额外座位

### v3.8 更新
- 新增：按宇航员配置旗帜外观，支持六种颜色预设以及持久化 PNG/JPG 自定义图片；默认设置下仍使用原版旗帜。
- 新增：自定义图片旗帜使用无边框旗面与单根黑色竖杆；旗面保持正向，特殊比例图片会保留轮廓。
- 修复：EVA 控制交接后偶发的 “No control” 状态，以及 EVA 时顶部火箭统计显示零值的问题。
- 调整：宇航员列表点击后显示操作菜单，集中提供旗帜自定义与解雇入口。

### v3.38 更新
- 修复：传送功能在禁用作弊时仍能使用 — 现在已禁用
- 修复：宇航员可以在气态行星上行走、插旗 — 添加检查，现在禁止在无地形行星出舱和插旗
- 修复：现在宇航员的名字支持所有 Unicode 字符
- 优化：精简代码

### v3.7 重大更新
- 严重修复：进入建造场景时的渲染泄漏
- 修复：宇航员EVA无浮力 — 添加 Water_Astronaut 组件
- 修复：燃料管分离/输油问题
- 修改：航天中心hub宇航员按钮
- 新增：宇航员传送 — 支持 Astronaut_EVA 传送
- 新增：宇航员仪表盘 — 显示速度、高度、燃料

### v3.6.7 更新
- 修复：返回建造场景时部件数量为0（幽灵部件）

### v3.6.6 更新
- 修复：带有航天员的部件仍然消失（v3.6.5 修复无效）

### v3.6.5 更新
- 修复：返回建造场景时带有航天员的部件仍会消失

### v3.6.4 更新
- 修复：已解雇的航天员在列表中仍然可见（渲染问题）
- 修复：返回建造场景时带有航天员的部件消失

### v3.6 更新
1. 模组重命名为 "AstronautMod"，简介更新为 "Enables the native astronaut/crew system on PC."
2. Hub "Astronauts" 按钮动态定位到成就按钮旁边
3. "Plant Flag" 按钮移动到右下角
4. 蓝图页面修复 / Blueprint page fixes:
   - 当没有宇航员时，点击添加宇航员会自动进入创建宇航员页面
   - 当宇航员在出舱(EVA)状态时，蓝图页面不再显示空白
   - 无可用宇航员时显示提示信息和 "Create New Astronaut" 按钮

### v3.5 更新
1. 插旗功能 / Plant Flag feature
   - 修复 flagPrefab 为NULL时无法插旗的问题
   - 当旗帜预制体缺失时，自动创建简易旗帜（红色方块视觉）
   - 修复 Flag.Start 在 mapIcon 为null时崩溃的问题
   - 添加 ModGUI "Plant Flag" 按钮，仅在EVA状态时显示（右下角）
2. 捡石头功能 / Rock Collection feature
   - 创建 RockSelector 回退实例（如果World场景中缺失）
   - 确保 DynamicTerrain 能正常注册石头到 RockSelector.rockInstances
   - 宇航员EVA状态下左键点击石头即可选中
   - 选中后点击 "Collect Rock" 按钮收集石头

### v3.4 更新
1. 修复 Seat.OnStart 清除已分配座位的BUG（根本原因）
2. 添加 onPartUsed 事件未绑定的回退机制
3. 添加 AttachableStatsMenu 缺失的回退机制
4. 添加诊断日志