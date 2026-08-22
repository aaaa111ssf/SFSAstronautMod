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
