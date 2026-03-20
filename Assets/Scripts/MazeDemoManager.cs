using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LLMAgent
{
    /// <summary>
    /// Maze Runner demo — registers maze-specific tools with UnityAgent
    /// and manages the demo UI/lifecycle.
    /// </summary>
    public class MazeDemoManager : MonoBehaviour
    {
        [Header("Agent Settings")]
        [Tooltip("Resource path for the system prompt (without extension).")]
        public string systemPromptResource = "maze-runner/system-prompt.md";

        [Tooltip("API Key for the LLM service.")]
        public string apiKey = "";

        [Tooltip("Base URL for the LLM API (leave empty for default).")]
        public string baseURL = "";

        [Tooltip("Model name (leave empty for default).")]
        public string model = "";

        [Tooltip("Maximum tool-call steps per generation. 0 = default (25).")]
        public int maxSteps = 0;

        [Header("Maze Settings")]
        [TextArea(3, 5)]
        public string startMessage = "红色标记是迷宫终点，走到终点。";

        [Header("Screenshot Settings")]
        public int screenshotWidth = 512;
        public int screenshotHeight = 512;

        [Header("References")]
        public MazeAgentUI agentUI;

        // State
        private UnityAgent agent;
        private bool isExploring;
        private bool isInitialized;

        private enum DemoState
        {
            Uninitialized, Initializing, Ready, Exploring, Completed, Error
        }
        private DemoState currentState = DemoState.Uninitialized;

        private void Awake()
        {
            Application.runInBackground = true;
            if (agentUI == null) agentUI = FindObjectOfType<MazeAgentUI>();
        }

        private void Start() => InitializeAgent();

        private void OnDestroy()
        {
            if (agent != null) { agent.Dispose(); agent = null; }
        }

        // =================================================================
        // Initialization — configure agent and register maze tools
        // =================================================================

        private void InitializeAgent()
        {
            SetState(DemoState.Initializing);

            agent = UnityAgent.Instance;
            agent.LoadSystemPrompt(systemPromptResource);

            if (!string.IsNullOrEmpty(apiKey))
                agent.Configure(apiKey, baseURL, model, maxSteps);

            // Register maze-specific tools
            RegisterTools();

            isInitialized = true;
            SetState(DemoState.Ready);
            Debug.Log("[MazeDemoManager] Agent initialized with maze tools.");
        }

        private void RegisterTools()
        {
            // --- getPlayerStatus ---
            agent.RegisterTool(
                "getPlayerStatus",
                "Get the player's current position and obstacle distances in all 4 cardinal directions (north/south/east/west), measured in grid cells. Also reports whether the goal has been reached.",
                null, // no parameters
                HandleGetPlayerStatus
            );

            // --- movePath ---
            agent.RegisterTool(
                "movePath",
                "Move the player along a sequence of direction segments. Each segment has a compass direction and a number of grid cells to move. Stops early if blocked by a wall or if the goal is reached. Maximum 20 segments, 1-10 cells per step.",
                @"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""segments"": {
                            ""type"": ""array"",
                            ""description"": ""Array of movement segments."",
                            ""items"": {
                                ""type"": ""object"",
                                ""properties"": {
                                    ""dir"": {
                                        ""type"": ""string"",
                                        ""enum"": [""north"", ""south"", ""east"", ""west""],
                                        ""description"": ""Compass direction to move.""
                                    },
                                    ""steps"": {
                                        ""type"": ""integer"",
                                        ""description"": ""Number of grid cells to move (1-10)."",
                                        ""minimum"": 1,
                                        ""maximum"": 10
                                    }
                                },
                                ""required"": [""dir"", ""steps""]
                            }
                        }
                    },
                    ""required"": [""segments""]
                }",
                HandleMovePath
            );

            // --- captureScreenshot ---
            agent.RegisterTool(
                "captureScreenshot",
                "Capture a top-down screenshot of the current game view. Returns the image for visual analysis of the maze layout, walls, corridors, and the red goal marker.",
                null,
                HandleCaptureScreenshot
            );
        }

        // =================================================================
        // Tool handlers
        // =================================================================

        private IEnumerator HandleGetPlayerStatus(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string result = null;
            bool done = false;
            MazePlayerBridge.GetPlayerStatus(r => { result = r; done = true; });
            while (!done) yield return null;

            callback(new UnityAgent.ToolResult { content = result });
        }

        private IEnumerator HandleMovePath(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            // Parse {"segments":[{"dir":"north","steps":3},...]} into directions/distances arrays
            string directionsJson, distancesJson;
            if (!ParseMovePathArgs(arguments, out directionsJson, out distancesJson))
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"Failed to parse movePath arguments. " +
                        "Expected: {\\\"segments\\\":[{\\\"dir\\\":\\\"north\\\",\\\"steps\\\":3}]}\"}"
                });
                yield break;
            }

            string result = null;
            bool done = false;
            MazePlayerBridge.MoveSequenceV2(directionsJson, distancesJson, r =>
            {
                result = r;
                done = true;
            });
            while (!done) yield return null;

            callback(new UnityAgent.ToolResult { content = result });
        }

        private IEnumerator HandleCaptureScreenshot(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            yield return new WaitForEndOfFrame();

            var cam = Camera.main;
            if (cam == null)
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"No main camera found.\"}"
                });
                yield break;
            }

            int w = screenshotWidth, h = screenshotHeight;
            var rt = new RenderTexture(w, h, 24);
            var prevTarget = cam.targetTexture;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            cam.targetTexture = prevTarget;
            RenderTexture.active = null;
            Destroy(rt);

            byte[] png = tex.EncodeToPNG();
            Destroy(tex);

            string base64 = Convert.ToBase64String(png);

            callback(new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"message\":\"Screenshot captured ({w}x{h}).\"}}",
                imageBase64 = base64,
                imageMimeType = "image/png"
            });
        }

        // =================================================================
        // movePath argument parser
        // =================================================================

        private static bool ParseMovePathArgs(string arguments, out string directionsJson, out string distancesJson)
        {
            directionsJson = null;
            distancesJson = null;

            var directions = new List<string>();
            var distances = new List<string>();

            int segIdx = arguments.IndexOf("\"segments\"", StringComparison.Ordinal);
            if (segIdx < 0) segIdx = 0;

            int arrStart = arguments.IndexOf('[', segIdx);
            if (arrStart < 0) return false;
            int arrEnd = UnityAgent.FindMatchingBracket(arguments, arrStart);
            if (arrEnd < 0) return false;

            string arrStr = arguments.Substring(arrStart, arrEnd - arrStart + 1);
            int pos = 0;
            while (pos < arrStr.Length)
            {
                int objStart = arrStr.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = UnityAgent.FindMatchingBrace(arrStr, objStart);
                if (objEnd < 0) break;

                string seg = arrStr.Substring(objStart, objEnd - objStart + 1);

                string dir = UnityAgent.ExtractStringField(seg, "dir")
                    ?? UnityAgent.ExtractStringField(seg, "direction");
                string steps = UnityAgent.ExtractNumberField(seg, "steps")
                    ?? UnityAgent.ExtractNumberField(seg, "distance");

                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(steps))
                {
                    directions.Add($"\"{dir}\"");
                    distances.Add(steps);
                }

                pos = objEnd + 1;
            }

            if (directions.Count == 0) return false;

            directionsJson = "[" + string.Join(",", directions) + "]";
            distancesJson = "[" + string.Join(",", distances) + "]";
            return true;
        }

        // =================================================================
        // Demo lifecycle
        // =================================================================

        public void StartExploration()
        {
            if (!isInitialized || isExploring) return;

            if (!agent.IsConfigured)
            {
                SetState(DemoState.Error);
                agentUI?.SetStatus("Error: API not configured");
                return;
            }

            isExploring = true;
            SetState(DemoState.Exploring);
            agentUI?.ShowThinking();

            agent.SendMessageAsync(startMessage, "",
                (response, isError) =>
                {
                    agentUI?.HideThinking();
                    isExploring = false;

                    if (isError)
                    {
                        Debug.LogError($"[MazeDemoManager] Error: {response}");
                        SetState(DemoState.Error);
                        agentUI?.SetStatus($"Error: {response}");
                        return;
                    }

                    Debug.Log($"[MazeDemoManager] Response: {response}");

                    var playerObj = GameObject.FindWithTag("Player");
                    bool completed = playerObj != null &&
                        playerObj.GetComponent<MazeGoalDetector>()?.HasReachedGoal == true;

                    if (completed)
                    {
                        SetState(DemoState.Completed);
                        agentUI?.ShowMazeCompleted();
                    }
                    else
                    {
                        SetState(DemoState.Ready);
                        agentUI?.SetStatus("Exploration paused (step limit reached)");
                    }
                },
                progress => Debug.Log($"[MazeDemoManager] {progress}")
            );
        }

        public void StopExploration()
        {
            if (agent != null && isExploring)
            {
                agent.AbortGeneration();
                isExploring = false;
                agentUI?.HideThinking();
                SetState(DemoState.Ready);
            }
        }

        public void ResetMaze()
        {
            agent?.ClearHistory();
            isExploring = false;
            agentUI?.ResetUI();

            var playerObj = GameObject.FindWithTag("Player");
            playerObj?.GetComponent<MazeGoalDetector>()?.ResetGoal();

            SetState(DemoState.Ready);
        }

        private void SetState(DemoState s)
        {
            currentState = s;
            switch (s)
            {
                case DemoState.Initializing: agentUI?.SetStatus("Initializing..."); break;
                case DemoState.Ready: agentUI?.SetStatus("Ready — Press Start"); break;
                case DemoState.Exploring: agentUI?.SetStatus("Exploring..."); break;
                case DemoState.Completed: agentUI?.SetStatus("Maze Completed!"); break;
            }
        }

        // =================================================================
        // OnGUI
        // =================================================================

        private void OnGUI()
        {
            float btnW = 140f, btnH = 36f, pad = 10f;
            float x = Screen.width - btnW - pad, y = pad;
            GUI.skin.button.fontSize = 14;

            switch (currentState)
            {
                case DemoState.Ready:
                    if (GUI.Button(new Rect(x, y, btnW, btnH), "▶ Start Exploration")) StartExploration();
                    y += btnH + 5;
                    if (GUI.Button(new Rect(x, y, btnW, btnH), "🔄 Reset")) ResetMaze();
                    break;
                case DemoState.Exploring:
                    if (GUI.Button(new Rect(x, y, btnW, btnH), "⏹ Stop")) StopExploration();
                    break;
                case DemoState.Completed:
                    if (GUI.Button(new Rect(x, y, btnW, btnH), "🔄 Play Again")) ResetMaze();
                    break;
                case DemoState.Error:
                    if (GUI.Button(new Rect(x, y, btnW, btnH), "🔄 Retry")) ResetMaze();
                    break;
                case DemoState.Initializing:
                    GUI.Label(new Rect(x, y, btnW, btnH), "Loading...");
                    break;
            }
        }
    }
}
