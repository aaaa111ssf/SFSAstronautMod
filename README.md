【安装方法】
1. 将AstronautMod.dll 复制/移动到：
   SFS游戏目录/Mods/ 文件夹下
2. 启动游戏

【使用方法】

■ 创建宇航员（Hub页面）：
   - 点击"Astronauts"按钮（位于成就按钮旁边）创建宇航员

■ 分配宇航员到座位（Build页面）：
   - 点击载入舱(CrewModule)部件
   - 在弹出的菜单中点击"Assign"分配宇航员到座位
   - 如果没有宇航员，会自动弹出创建宇航员对话框
   - 如果所有宇航员都在出舱(EVA)状态，会显示提示并可创建新宇航员

■ 出舱EVA（World页面）：
   - 点击载入舱部件
   - 如果座位有宇航员，会显示"EVA Exit"按钮
   - 点击"EVA Exit"即可让宇航员出舱

■ 插旗（World页面，EVA状态）：
   - 宇航员出舱后，屏幕右下角出现"Plant Flag"按钮
   - 点击按钮在当前位置插旗
   - 注意：不能距已有旗帜30米以内插旗

■ 捡石头（World页面，EVA状态）：
   - 宇航员出舱后，左键点击地表上的石头
   - 选中石头后会弹出"Collect Rock"选项
   - 点击收集石头

【诊断日志】
查看游戏日志文件：
- Windows: %APPDATA%/../LocalLow/Stefo Mai Morojna/Spaceflight Simulator/Player.log
- 搜索 [AstronautMod] 或 [AstronautUnlocker] 查看模组日志

关键日志说明：
- "challengesButton found at ..." = 找到成就按钮，Astronauts按钮已放置在旁边
- "No astronauts in assign mode, auto-opening create dialog" = 蓝图中无宇航员，自动打开创建对话框
- "RockSelector.main exists, rockInstances: N" = 石头系统正常，N个石头已注册
- "Created fallback RockSelector" = RockSelector不存在，已创建回退
- "flagPrefab is NULL and no resources found" = 旗帜预制体缺失，将使用代码生成
- "Fallback flag created at ..." = 简易旗帜已创建
- "Plant Flag button created (EVA active)" = 插旗按钮已显示
- "PlantFlag called by XXX, flags now: N" = 插旗成功
- "Seat.OnStart: XXX is CrewWorld, seat PRESERVED" = 座位已保留
### 更新日志
【v3.6 更新内容】
1. 模组重命名为 "AstronautMod"，简介更新为 "Enables the native astronaut/crew system on PC."
2. Hub "Astronauts" 按钮动态定位到成就按钮旁边
3. "Plant Flag" 按钮移动到右下角
4. 蓝图页面修复：
   - 当没有宇航员时，点击添加宇航员会自动进入创建宇航员页面
   - 当宇航员在出舱(EVA)状态时，蓝图页面不再显示空白
   - 无可用宇航员时显示提示信息和"Create New Astronaut"按钮

【v3.5 更新内容】
1. 插旗功能
   - 修复 flagPrefab 为NULL时无法插旗的问题
   - 当旗帜预制体缺失时，自动创建简易旗帜（红色方块视觉）
   - 修复 Flag.Start 在 mapIcon 为null时崩溃的问题
   - 添加 ModGUI "Plant Flag" 按钮，仅在EVA状态时显示（右下角）

2. 捡石头功能
   - 创建 RockSelector 回退实例（如果World场景中缺失）
   - 确保 DynamicTerrain 能正常注册石头到 RockSelector.rockInstances
   - 宇航员EVA状态下左键点击石头即可选中
   - 选中后点击"Collect Rock"按钮收集石头

【v3.4 更新内容】
1. 修复 Seat.OnStart 清除已分配座位的BUG（根本原因）
2. 添加 onPartUsed 事件未绑定的回退机制
3. 添加 AttachableStatsMenu 缺失的回退机制
4. 添加诊断日志
