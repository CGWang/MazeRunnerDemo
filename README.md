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

## 工具清单

### 目录结构与 unity-mcp 映射

```
Tools/                          ← unity-mcp 对应
├── AgentBatchExecuteTools.cs   ← BatchExecute.cs
├── AgentToolHelpers.cs         ← (公共工具方法)
├── Animation/                  ← Animation/
│   ├── AgentAnimationTools.cs  ← ManageAnimation.cs (Animator 运行时控制)
│   └── AgentAnimControllerTools.cs ← ControllerCreate/ClipCreate/ClipPresets
├── Assets/                     ← ManageAsset.cs
│   └── AgentAssetTools.cs
├── Cameras/                    ← Cameras/
│   └── AgentCameraTools.cs     ← ManageCamera.cs
├── Components/                 ← ManageComponents.cs
│   └── AgentComponentTools.cs
├── Console/                    ← ReadConsole.cs
│   └── AgentConsoleTools.cs
├── GameObjects/                ← GameObjects/
│   ├── AgentGameObjectTools.cs ← ManageGameObject.cs
│   └── AgentSceneQueryTools.cs ← FindGameObjects.cs
├── Graphics/                   ← Graphics/
│   └── AgentGraphicsTools.cs   ← ManageGraphics.cs (Volume/Bake/Skybox/Pipeline)
├── Materials/                  ← ManageMaterial.cs
│   └── AgentMaterialTools.cs
├── Packages/                   ← ManagePackages.cs
│   └── AgentPackageTools.cs
├── Physics/                    ← (扩展: 物理查询)
│   └── AgentPhysicsTools.cs
├── Prefabs/                    ← Prefabs/
│   └── AgentPrefabTools.cs     ← ManagePrefabs.cs
├── ProBuilder/                 ← ProBuilder/
│   └── AgentProBuilderTools.cs ← ManageProBuilder.cs (反射调用)
├── Reflection/                 ← UnityReflect.cs
│   └── AgentReflectionTools.cs
├── Scene/                      ← ManageScene.cs + ManageEditor.cs
│   ├── AgentEditorControlTools.cs
│   ├── AgentSceneManageTools.cs
│   └── AgentScreenshotTools.cs
├── ScriptableObjects/          ← ManageScriptableObject.cs
│   └── AgentScriptableObjectTools.cs
├── Scripts/                    ← ManageScript.cs
│   └── AgentScriptTools.cs
├── Shaders/                    ← ManageShader.cs
│   └── AgentShaderTools.cs
├── Tests/                      ← RunTests.cs
│   └── AgentTestTools.cs
├── Textures/                   ← ManageTexture.cs
│   └── AgentTextureTools.cs
├── UI/                         ← ManageUI.cs
│   └── AgentUITools.cs
└── Vfx/                        ← Vfx/
    └── AgentVfxTools.cs        ← ManageVFX.cs (Particle/Line/Trail)
```

### 工具总览（27 个工具，150+ 个 action）

| 工具 | 文件 | 主要 action | unity-mcp 对应 |
|------|------|-------------|---------------|
| `inspectGameObject` | GameObjects/ | inspect, create, destroy, setActive, setTransform, duplicate, setParent | manage_gameobject |
| `listScene` / `findGameObjects` | GameObjects/ | 场景查询、搜索 | find_gameobjects |
| `manageAnimation` | Animation/ | get_status, set_parameter, play, crossfade, set_speed | manage_animation (animator) |
| `manageAnimController` | Animation/ | controller_create/add_state/add_transition/add_parameter/get_info/assign, clip_create/add_curve/create_preset/assign/add_event | manage_animation (controller/clip) |
| `manageMaterial` | Materials/ | create, modify, list_shaders | manage_material |
| `manageCamera` | Cameras/ | create, list, set_lens, set_target, set_priority, screenshot, get_info | manage_camera |
| `manageGraphics` | Graphics/ | volume_create/add_effect/get_info/list_effects, bake_start/cancel/status/clear/get_settings/set_settings, skybox_get/set_material/set_ambient/set_fog/set_sun, pipeline_get_info/set_quality, stats_get/get_memory | manage_graphics |
| `manageVfx` | Vfx/ | particle_create/get_info/set_main/set_emission/set_shape/play/stop/pause/enable_module/add_burst, line_create/set_positions/set_width/set_color/create_circle/create_arc, trail_create/set_time/set_width/set_color | manage_vfx |
| `manageTexture` | Textures/ | create, create_sprite, modify, apply_pattern, apply_gradient, apply_noise, get_info | manage_texture |
| `manageShader` | Shaders/ | create, read, update, delete, validate, list | manage_shader |
| `manageUI` | UI/ | create, read, update, delete, list, attach/detach_ui_document, create_panel_settings, get_visual_tree | manage_ui |
| `manageProBuilder` | ProBuilder/ | create_shape, extrude_faces, subdivide, get_mesh_info, set_face_material, flip_normals, merge_faces, center_pivot, validate_mesh | manage_probuilder |
| `manageScriptableObject` | ScriptableObjects/ | create, modify, get_info | manage_scriptable_object |
| `toggleComponent` / `addComponent` / etc. | Components/ | add, remove, toggle, set/get property | manage_components |
| `readConsole` / `clearConsole` | Console/ | get, clear | read_console |
| `callMethod` / `findMethods` | Reflection/ | 反射调用、方法搜索 | unity_reflect |
| `physicsQuery` | Physics/ | raycast, overlap_sphere, overlap_box, linecast | (扩展) |
| `editorControl` | Scene/ | play, pause, stop, step, status, add/remove_tag, add/remove_layer, set_active_tool | manage_editor |
| `manageScene` | Scene/ | create, open, save, save_as, list, set_active, unload, new, get_hierarchy, get_active, get_build_settings, screenshot, scene_view_frame | manage_scene |
| `captureView` | Scene/ | Game View / Scene View / Camera 截图 | manage_scene (screenshot) |
| `manageAssets` / `findAssets` / `getAssetData` / `refreshAssets` / `listShaders` | Assets/ | 资产 CRUD、搜索、刷新 | manage_asset |
| `createPrefab` / `instantiatePrefab` / `openPrefab` / `closePrefab` / `savePrefab` | Prefabs/ | Prefab 创建、实例化、编辑模式 | manage_prefabs |
| `readScript` / `createScript` / `editScript` | Scripts/ | 脚本读写编辑 | manage_script |
| `managePackage` | Packages/ | add, remove, list, search, get_info, list/add/remove_registry, embed, resolve | manage_packages |
| `runTests` | Tests/ | EditMode / PlayMode 测试 | run_tests |
| `batchExecute` | (root) | 批量执行多个工具操作 | batch_execute |

### 尚未覆盖的 unity-mcp 功能

| 模块 | 说明 |
|------|------|
| **VFX Graph** | VFX Graph 资产创建/参数设置（需要 com.unity.visualeffectgraph） |
| **ProBuilder 完整版** | 已有 9 个核心 action，unity-mcp 有 50+（顶点操作、UV、选择等） |
| **Roslyn 运行时编译** | 运行时 C# 编译执行（CustomTools/RoslynRuntimeCompilation） |
| **Script 结构化编辑** | anchor_insert/replace/delete、方法级操作、Roslyn 语法验证 |
| **Graphics Renderer Features** | URP Renderer Feature 增删配置（需 URP） |
| **Camera Cinemachine 完整版** | 已支持基础 Cinemachine 反射，缺少 Body/Aim/Noise/Blend 详细配置 |

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
