# MazeRunnerDemo 项目分析

## 一、项目定位

这是一个 **AI 自主走迷宫的 Demo**，展示了 LLM（大语言模型）如何通过「视觉观察 + 工具调用」的闭环来自主探索 3D 迷宫。它是 Puerts.AI 框架的一个运行时 Agent 示例。

## 二、技术栈

| 层级 | 技术 | 作用 |
|------|------|------|
| 游戏引擎 | Unity 2022+ (C#) | 3D 场景渲染、物理碰撞、角色控制 |
| 脚本桥接 | PuerTS (腾讯) | 让 TypeScript 直接调用 C# API |
| AI 框架 | Puerts.Agent | Agent 生命周期管理、tool calling 协议 |
| LLM 接口 | OpenAI 兼容 API | 支持 GPT-4、Qwen 等任何兼容模型 |
| 构建工具 | esbuild | 将 TypeScript 编译为 ESM 模块 |

## 三、核心架构 — 感知-思考-行动 闭环

整个系统的运行原理：**LLM 通过截图"看"迷宫，通过 tool call "走"迷宫，不断循环直到到达终点。**

```
┌─────────────────────────────────────────────────────┐
│                   LLM (云端模型)                      │
│   接收截图 → 分析走廊/墙壁/死胡同 → 规划路径          │
│   输出 tool_call: movePath / getPlayerStatus         │
└────────────┬──────────────────────────┬──────────────┘
             │ tool call                │ 返回结果+截图
             ▼                          │
┌────────────────────────────────────────┐
│         Puerts.Agent (TS 层)           │
│   maze-control.mts  screenshot.mts    │
│   参数校验 → 调用 C# Bridge            │
└────────────┬──────────────────────────┘
             │ CS.LLMAgent.MazePlayerBridge
             ▼
┌────────────────────────────────────────┐
│         Unity C# 层                    │
│  Raycast 检测墙壁 → CharacterController│
│  移动角色 → 网格对齐 → 检测终点         │
└────────────────────────────────────────┘
```

每一轮循环：
1. **感知 (Sense)** — 调用 `getPlayerStatus()` → 四方向射线检测，返回各方向到墙壁的距离（格数）
2. **观察 (Look)** — 调用 `captureScreenshot()` → 截取俯视图发给 LLM 做视觉分析
3. **思考 (Think)** — LLM 分析截图，识别走廊、死胡同、岔路口，用右手定则规划路径
4. **行动 (Act)** — 调用 `movePath([{dir:"north", steps:3}, {dir:"east", steps:2}])` 执行多段移动
5. **确认 (Confirm)** — 检查 `reachedGoal` 字段，到达终点则停止，否则继续下一轮

## 四、迷宫生成算法 — 递归回溯 (DFS)

`MazeSceneGenerator.cs` 使用经典的 Recursive Backtracking 算法：

```
初始化: 所有墙壁 ON，所有格子未访问
从 (0,0) 开始，压入栈

while 栈非空:
    当前格子 = 栈顶
    找到未访问的随机邻居?
        YES → 打通两格之间的墙 → 邻居压栈
        NO  → 弹栈(回溯)
```

- 使用固定种子 (42) 保证可复现
- 生成完美迷宫（任意两点间有且仅有一条路径）
- 网格尺寸 4x4 ~ 16x16 可配置
- 每格 2 米，墙壁高度 1.2 米（矮墙，俯视可看到格局）

## 五、关键设计决策

### 1. 俯视固定相机 + 绝对方向

相机固定在玩家正上方 20 米处，不随玩家旋转。屏幕上方永远是北 (+Z)，右边永远是东 (+X)。LLM 不需要推理相对方向，降低了认知复杂度。

### 2. 矮墙 + 绿色网格线

墙壁只到腰部高度，地面画有绿色网格线。从俯视角度 LLM 可以看到多格走廊全貌，通过数网格线精确判断距离，识别死胡同避免浪费步数。

### 3. 网格对齐 (Snap to Cell Center)

每次移动后，角色自动吸附到最近格子中心点，避免浮点漂移导致 AI 计算距离出错。

### 4. 多段路径一次提交

`movePath()` 支持一次提交多段移动（如 L 形、Z 形路径），减少 LLM 往返次数。C# 端顺序执行每段，遇到墙壁或终点提前停止。

### 5. 右手定则 + 视觉死胡同检测

System Prompt 中教 LLM 使用右手靠墙法作为基础策略，同时要求每次移动前先看截图排除死胡同，两者结合实现高效导航。

## 六、Puerts.Agent 框架

Agent 的所有能力通过资源目录配置定义：

```
Resources/maze-runner/
├── system-prompt.md.txt   ← 告诉 AI：你是迷宫探索者，用右手定则导航
├── builtins/
│   ├── maze-control.mjs   ← 移动+感知工具
│   └── screenshot.mjs     ← 截图工具
```

不同 Agent 完全隔离，只需写不同的 system prompt + builtins 即可创建新 Agent。

## 七、文件职责速查

| 文件 | 职责 |
|------|------|
| `MazeDemoManager.cs` | 总控：Agent 初始化、API 配置、开始/停止/重置 |
| `MazePlayerBridge.cs` | C#↔TS 桥接：射线检测、角色移动、终点判断 |
| `MazeAgentUI.cs` | UI：思考气泡动画、状态面板、成功提示 |
| `MazeFollowCamera.cs` | 俯视相机跟随玩家 |
| `MazeSceneGenerator.cs` | 编辑器工具：DFS 生成迷宫场景 |
| `maze-control.mts` | TS 工具模块：movePath() + getPlayerStatus() |
| `screenshot.mts` | TS 工具模块：captureScreenshot() |
| `system-prompt.md.txt` | System Prompt：导航策略、方向映射、死胡同规则 |

## 八、总结

这个项目用极少的代码（2 个 TS 模块 + 1 个 system prompt）实现了一个完整的 AI 自主导航系统。它本质上是一个视觉-语言-行动 (VLA) 的最小可行演示：LLM 充当"大脑"，截图是"眼睛"，tool call 是"手脚"，整个闭环在 Unity 运行时中实时运转。
