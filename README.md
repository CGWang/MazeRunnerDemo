# UnityAgent — Unity 编辑器内的 AI Agent

在 Unity Editor 内嵌一个 AI Agent，让用户可以直接在编辑器窗口中与 LLM 对话，并通过工具调用（tool calling）来操控 Unity 的场景、资产、脚本、材质等各项功能。

## 项目意图

本项目的目标是打造一个 **Unity 原生的 AI 助手**，两大能力来源：

1. **仿 Claude Code CLI** — 在 Unity 编辑器窗口内提供类似 Claude Code 的交互体验：流式对话、工具调用、会话管理、权限控制
2. **融合 Unity-MCP 的工具能力** — 移植 [unity-mcp](https://github.com/CoplayDev/unity-mcp) 项目中针对 Unity 接口的丰富工具集，让 Agent 具备全面的 Unity 编辑器操控能力

与 unity-mcp 作为外部 MCP Server 运行不同，本项目将这些能力**内嵌到 Unity Editor 中**，无需外部进程或额外的通信协议，Agent 直接在编辑器内运行。

## 参考项目

| 项目 | 说明 |
|------|------|
| [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) | Unity MCP Server，提供了 300+ 个针对 Unity 的操作接口（场景、资产、脚本、材质、动画、相机、图形、VFX、ProBuilder 等），本项目的工具能力主要参考此项目 |
| [Claude Code](https://docs.anthropic.com/en/docs/claude-code) | Anthropic 的 CLI AI 助手，本项目的交互模式和会话管理参考了其设计 |

## 架构概览

```
┌─────────────────────────────────────────────────┐
│              Unity Editor Window                 │
│         AgentChatUI / AgentThinkingUI            │
│              (对话界面 + 状态展示)                 │
└──────────────────┬──────────────────────────────┘
                   │ 用户输入 / Agent 响应
                   ▼
┌─────────────────────────────────────────────────┐
│              UnityAgent (核心)                    │
│   SSE 流式通信 · 工具调用 · 会话管理 · 权限控制     │
└──────────────────┬──────────────────────────────┘
                   │ [AgentTool] 自动发现
                   ▼
┌─────────────────────────────────────────────────┐
│              Tools 工具集                         │
│                                                  │
│  Runtime/          Editor/                       │
│  ├─ GameObject     ├─ Asset 管理                  │
│  ├─ Component      ├─ Scene 管理                  │
│  ├─ Material       ├─ Script 读写                 │
│  ├─ Animation      ├─ Prefab 操作                 │
│  ├─ Physics        ├─ Package 管理                │
│  ├─ Scene 查询     ├─ Screenshot 截图             │
│  ├─ Console        ├─ Editor 控制                 │
│  └─ Reflection     └─ Test Runner                │
└─────────────────────────────────────────────────┘
```

## 已实现的工具（41 个）

### Runtime 工具（运行时可用）

| 工具 | 说明 |
|------|------|
| `inspectGameObject` | 获取 GameObject 详细信息（Transform、组件、子对象） |
| `createGameObject` | 创建 GameObject，支持基础几何体 |
| `destroyGameObject` | 删除 GameObject 及其子对象 |
| `setActive` | 激活/隐藏 GameObject |
| `setTransform` | 设置位置、旋转、缩放 |
| `duplicateGameObject` | 复制 GameObject |
| `setParent` | 修改父子关系 |
| `manageMaterial` | 创建/修改材质 |
| `readConsole` | 读取 Unity 控制台日志 |
| `clearConsole` | 清空控制台 |
| `manageAnimation` | Animator 控制（播放、参数、混合） |
| `callMethod` | 通过反射调用组件方法 |
| `findMethods` | 反射查找组件方法 |
| `physicsQuery` | 物理查询（Raycast、Overlap） |
| `toggleComponent` | 启用/禁用组件 |
| `addComponent` | 添加组件 |
| `removeComponent` | 移除组件 |
| `setComponentProperty` | 设置组件属性 |
| `getComponentProperty` | 读取组件属性 |
| `listScene` | 列出场景中的 GameObject |
| `findGameObjects` | 按名称/Tag/Layer/组件搜索 |

### Editor 工具（仅编辑器可用）

| 工具 | 说明 |
|------|------|
| `createPrefab` | 从场景对象创建 Prefab |
| `instantiatePrefab` | 实例化 Prefab 到场景 |
| `openPrefab` / `closePrefab` / `savePrefab` | Prefab 编辑模式 |
| `readScript` | 读取脚本文件 |
| `createScript` | 创建 C# 脚本（支持模板） |
| `editScript` | 编辑脚本（替换、插入、删除） |
| `editorControl` | 控制 Play/Pause/Stop |
| `executeMenuItem` | 执行编辑器菜单项 |
| `editorSelection` | 管理编辑器选中对象 |
| `managePackage` | 包管理（安装、卸载、搜索） |
| `manageScene` | 场景管理（创建、打开、保存） |
| `captureView` | 截图（Game View / Scene View / Camera） |
| `runTests` | 运行 Unity 测试 |
| `manageAssets` | 资产管理（列表、移动、复制、删除） |
| `findAssets` | 搜索项目资产 |
| `getAssetData` | 获取资产详细信息 |
| `refreshAssets` | 刷新 AssetDatabase |
| `listShaders` | 列出可用 Shader |

## 待移植的 unity-mcp 功能

以下是 unity-mcp 中已实现但本项目尚未覆盖的功能模块：

| 模块 | 说明 | 优先级 |
|------|------|--------|
| **UI Toolkit** (`manage_ui`) | UXML/USS 创建编辑、UIDocument 管理、VisualElement 样式修改 | 高 |
| **Graphics / 渲染管线** (`manage_graphics`) | Volume 系统、光照烘焙、渲染统计、Skybox/雾效、渲染管线设置、Renderer Features | 高 |
| **Texture** (`manage_texture`) | 程序化纹理生成（棋盘、条纹、噪声、渐变） | 中 |
| **Shader** (`manage_shader`) | Shader 文件的 CRUD 和语法验证 | 中 |
| **Camera / Cinemachine** (`manage_camera`) | 相机创建配置、Cinemachine 控制、多角度截图 | 中 |
| **VFX** (`manage_vfx`) | ParticleSystem 控制、VFX Graph、LineRenderer、TrailRenderer | 中 |
| **ProBuilder** (`manage_probuilder`) | 程序化建模（挤出、倒角、细分、UV 编辑） | 低 |
| **Animation 扩展** | AnimatorController 创建、BlendTree、AnimationClip 预设 | 中 |
| **ScriptableObject** | ScriptableObject 创建和修改 | 低 |
| **Batch Execute** | 批量操作 + 原子事务回滚 | 中 |
| **Roslyn 运行时编译** | 运行时 C# 编译执行 | 低 |
| **Tag/Layer 管理** | 标签和层级的增删改 | 低 |

## 迷宫 Demo

项目附带一个 AI 自主走迷宫的 Demo，展示 Agent 通过截屏观察 + 工具调用来探索 3D 迷宫。

### 生成迷宫场景

菜单栏 **Tools → Maze Runner → Generate Maze Scene**

| 参数 | 说明 | 默认值 |
|------|------|--------|
| Maze Width | 迷宫宽度（格数） | 8 |
| Maze Height | 迷宫高度（格数） | 8 |
| Cell Size | 每格大小（米） | 2.0 |
| Wall Height | 墙壁高度（米） | 1.2 |

### 配置 API Key

在 Hierarchy 中选中 **MazeDemoManager**，在 Inspector 中配置：

| 字段 | 说明 |
|------|------|
| **Api Key** | LLM 服务的 API Key（必填） |
| **Base URL** | API 端点地址（兼容 OpenAI 格式） |
| **Model** | 模型名称 |

### 运行

1. 打开 `Assets/Scenes/MazeDemo.unity`
2. 配置好 API Key
3. 点击 **Play** 进入运行模式
4. 点击 **▶ Start Exploration** 启动 AI 探索

## License

MIT
