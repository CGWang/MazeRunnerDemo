using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LLMAgent
{
    /// <summary>
    /// Maze Runner demo — registers maze-specific tools with UnityAgent,
    /// wires up AgentChatUI for continuous multi-turn conversation.
    /// </summary>
    public class MazeDemoManager : MonoBehaviour
    {
        [Header("Agent Settings")]
        public string systemPromptResource = "maze-runner/system-prompt.md";
        public string apiKey = "";
        public string baseURL = "";
        public string model = "";
        public int maxSteps = 0;

        [Header("Maze Settings")]
        [TextArea(3, 5)]
        public string welcomeMessage = "AI Agent ready. Type a message to start, " +
            "or click 'Auto Explore' to let the AI navigate the maze.";

        [Tooltip("Message sent when 'Auto Explore' is clicked.")]
        [TextArea(2, 3)]
        public string autoExploreMessage = "红色标记是迷宫终点，走到终点。";

        [Header("Screenshot")]
        public int screenshotWidth = 512;
        public int screenshotHeight = 512;

        [Header("Persistence")]
        [Tooltip("Auto-save session after each agent turn for recompile resilience.")]
        public bool autoSaveSession = true;

        [Header("Memory")]
        [Tooltip("Path for long-term memory file (relative to Assets).")]
        public string memoryFileName = "agent-memory.md";

        [Header("References")]
        public AgentChatUI chatUI;
        public MazeAgentUI agentUI;

        private UnityAgent agent;

        private string SessionPath => Application.persistentDataPath + "/UnityAgent/agent-session.json";
        private string ChatSessionPath => Application.persistentDataPath + "/UnityAgent/chat-session.json";
        private string MemoryPath => Application.dataPath + "/" + memoryFileName;

        private void Awake()
        {
            Application.runInBackground = true;
            if (chatUI == null) chatUI = FindObjectOfType<AgentChatUI>();
            if (agentUI == null) agentUI = FindObjectOfType<MazeAgentUI>();
        }

        private void Start()
        {
            agent = UnityAgent.Instance;
            agent.LoadSystemPrompt(systemPromptResource);

            if (!string.IsNullOrEmpty(apiKey))
                agent.Configure(apiKey, baseURL, model, maxSteps);

            // Load long-term memory
            agent.LoadMemory(MemoryPath);

            RegisterTools();

            // Bind chat UI to agent events
            if (chatUI != null)
            {
                chatUI.BindAgent(agent);
                chatUI.OnUserMessage += HandleUserMessage;
            }

            // Wire up thinking bubble
            agent.OnGenerationStart += () => agentUI?.ShowThinking();
            agent.OnGenerationEnd += () =>
            {
                agentUI?.HideThinking();
                if (autoSaveSession) SaveAll();
            };

            // Try restore previous session
            if (agent.LoadSession(SessionPath) && chatUI != null)
            {
                LoadChatSession();
                chatUI.AddSystemMessage("[Session restored from previous run]");
            }
            else
            {
                chatUI?.AddSystemMessage(welcomeMessage);
            }

            Debug.Log("[MazeDemoManager] Initialized — chat mode active.");
        }

        private void OnDestroy()
        {
            if (chatUI != null)
            {
                chatUI.UnbindAgent(agent);
                chatUI.OnUserMessage -= HandleUserMessage;
            }
            if (agent != null) agent.Dispose();
        }

        // =================================================================
        // Chat message handling — continuous multi-turn
        // =================================================================

        private void HandleUserMessage(string message)
        {
            if (!agent.IsConfigured)
            {
                chatUI?.AddSystemMessage("Error: API not configured. Set API key in MazeDemoManager.");
                return;
            }

            if (agent.IsRunning)
            {
                chatUI?.AddSystemMessage("Agent is busy. Please wait or click Stop.");
                return;
            }

            agentUI?.SetStatus("Thinking...");

            agent.SendMessageAsync(message, null,
                (response, isError) =>
                {
                    if (isError)
                    {
                        chatUI?.AddSystemMessage($"Error: {response}");
                        agentUI?.SetStatus("Error");
                    }
                    else
                    {
                        // Check maze completion
                        var playerObj = GameObject.FindWithTag("Player");
                        bool completed = playerObj != null &&
                            playerObj.GetComponent<MazeGoalDetector>()?.HasReachedGoal == true;

                        if (completed)
                        {
                            agentUI?.ShowMazeCompleted();
                            agentUI?.SetStatus("Maze Completed!");
                        }
                        else
                        {
                            agentUI?.SetStatus("Ready");
                        }
                    }
                },
                progress =>
                {
                    agentUI?.SetStatus(progress);
                }
            );
        }

        // =================================================================
        // Tool registration
        // =================================================================

        private void RegisterTools()
        {
            agent.RegisterTool(
                "getPlayerStatus",
                "Get the player's current position and obstacle distances in all 4 cardinal " +
                "directions (north/south/east/west), measured in grid cells. Also reports whether " +
                "the goal has been reached.",
                null,
                HandleGetPlayerStatus
            );

            agent.RegisterTool(
                "movePath",
                "Move the player along a sequence of direction segments. Each segment has a " +
                "compass direction and a number of grid cells to move. Stops early if blocked " +
                "by a wall or if the goal is reached. Maximum 20 segments, 1-10 cells per step.",
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
                                        ""enum"": [""north"", ""south"", ""east"", ""west""]
                                    },
                                    ""steps"": {
                                        ""type"": ""integer"",
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
                HandleMovePath,
                requiresPermission: true
            );

            agent.RegisterTool(
                "captureScreenshot",
                "Capture a top-down screenshot of the current game view. Returns the image " +
                "for visual analysis of the maze layout, walls, corridors, and goal marker.",
                null,
                HandleCaptureScreenshot
            );

            agent.RegisterTool(
                "updateMemory",
                "Save important observations to long-term memory that persists across sessions. " +
                "Use this to remember: maze layout, dead ends, effective strategies, positions " +
                "explored. Over time this helps you navigate more efficiently.",
                @"{""type"":""object"",""properties"":{""content"":{""type"":""string""," +
                @"""description"":""The observation or note to save.""}},""required"":[""content""]}",
                HandleUpdateMemory
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
            string directionsJson, distancesJson;
            if (!ParseMovePathArgs(arguments, out directionsJson, out distancesJson))
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"Failed to parse movePath arguments.\"}"
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
                    { content = "{\"success\":false,\"error\":\"No main camera.\"}" });
                yield break;
            }

            int w = screenshotWidth, h = screenshotHeight;
            var rt = new RenderTexture(w, h, 24);
            var prev = cam.targetTexture;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            cam.targetTexture = prev;
            RenderTexture.active = null;
            Destroy(rt);

            byte[] png = tex.EncodeToPNG();
            Destroy(tex);

            callback(new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"message\":\"Screenshot captured ({w}x{h}).\"}}",
                imageBase64 = Convert.ToBase64String(png),
                imageMimeType = "image/png"
            });
        }

        private IEnumerator HandleUpdateMemory(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string content = UnityAgent.ExtractStringField(arguments, "content");
            if (string.IsNullOrEmpty(content))
            {
                callback(new UnityAgent.ToolResult
                    { content = "{\"success\":false,\"error\":\"No content provided.\"}" });
                yield break;
            }
            agent.AppendMemory(MemoryPath, content);
            callback(new UnityAgent.ToolResult
                { content = "{\"success\":true,\"message\":\"Memory updated successfully.\"}" });
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
        // OnGUI — minimal control buttons (chat replaces the old button panel)
        // =================================================================

        private void OnGUI()
        {
            float btnW = 130f, btnH = 30f, pad = 8f;
            // Position buttons above the chat panel area, top-right of game view
            float x = anchorRight() ? Screen.width - chatPanelWidth() - btnW - pad * 2 : chatPanelWidth() + pad;
            float y = pad;

            GUI.skin.button.fontSize = 13;

            if (!agent.IsRunning)
            {
                if (agent.IsConfigured && GUI.Button(new Rect(x, y, btnW, btnH), "Auto Explore"))
                {
                    chatUI?.AddUserMessage(autoExploreMessage);
                    HandleUserMessage(autoExploreMessage);
                }
                y += btnH + 4;

                if (GUI.Button(new Rect(x, y, btnW, btnH), "Reset Maze"))
                {
                    ResetMaze();
                }
            }
            else
            {
                if (GUI.Button(new Rect(x, y, btnW, btnH), "Stop"))
                {
                    agent.AbortGeneration();
                    agentUI?.SetStatus("Stopped");
                }
            }
        }

        private float chatPanelWidth()
        {
            return chatUI != null ? chatUI.panelWidth : 380f;
        }

        private bool anchorRight()
        {
            return chatUI == null || chatUI.anchorRight;
        }

        private void ResetMaze()
        {
            agent?.ClearHistory();
            chatUI?.Clear();
            agentUI?.ResetUI();

            // Delete session files
            agent?.DeleteSession(SessionPath);
            if (System.IO.File.Exists(ChatSessionPath))
                System.IO.File.Delete(ChatSessionPath);

            var playerObj = GameObject.FindWithTag("Player");
            playerObj?.GetComponent<MazeGoalDetector>()?.ResetGoal();

            chatUI?.AddSystemMessage("Maze reset. " + welcomeMessage);
            agentUI?.SetStatus("Ready");
        }

        // =================================================================
        // Session persistence
        // =================================================================

        private void SaveAll()
        {
            agent.SaveSession(SessionPath);
            if (chatUI != null)
            {
                try
                {
                    var data = chatUI.GetSaveData();
                    string dir = System.IO.Path.GetDirectoryName(ChatSessionPath);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(ChatSessionPath, JsonUtility.ToJson(data, true));
                }
                catch (Exception e) { Debug.LogError($"[MazeDemoManager] Chat save failed: {e.Message}"); }
            }
        }

        private void LoadChatSession()
        {
            if (!System.IO.File.Exists(ChatSessionPath)) return;
            try
            {
                string json = System.IO.File.ReadAllText(ChatSessionPath);
                var data = JsonUtility.FromJson<AgentChatUI.ChatSaveData>(json);
                chatUI.LoadSaveData(data);
            }
            catch (Exception e) { Debug.LogWarning($"[MazeDemoManager] Chat load failed: {e.Message}"); }
        }

        private void OnApplicationQuit()
        {
            if (autoSaveSession) SaveAll();
        }
    }
}
