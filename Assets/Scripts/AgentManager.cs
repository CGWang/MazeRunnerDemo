using System;
using System.Collections;
using UnityEngine;

namespace LLMAgent
{
    /// <summary>
    /// General-purpose agent manager. Handles:
    /// - UnityAgent lifecycle (init, configure, dispose)
    /// - AgentChatUI + AgentThinkingUI binding
    /// - Domain Reload resilience (OnDisable/OnEnable save/restore)
    /// - Session persistence (save/load to disk)
    /// - Long-term memory
    /// - Built-in tools: captureScreenshot, updateMemory
    ///
    /// Subclass and override RegisterTools() / OnAgentResponse() to add domain-specific behavior.
    /// </summary>
    public class AgentManager : MonoBehaviour
    {
        // =================================================================
        // Configuration (Inspector)
        // =================================================================

        [Header("Agent Settings")]
        [Tooltip("Resource path (without extension) for the system prompt text file.")]
        public string systemPromptResource = "agent/system-prompt.md";

        [Tooltip("API key. Leave empty to use UnityAgent defaults or environment variable.")]
        public string apiKey = "";

        [Tooltip("Base URL for the API endpoint. Leave empty for default.")]
        public string baseURL = "";

        [Tooltip("Model name (e.g. gpt-4o, claude-sonnet-4-20250514). Leave empty for default.")]
        public string model = "";

        [Tooltip("Max tool-calling steps per generation. 0 = use UnityAgent default.")]
        public int maxSteps = 0;

        [Header("Welcome")]
        [TextArea(3, 5)]
        public string welcomeMessage = "AI Agent ready. Type a message to start.";

        [Header("Screenshot")]
        public int screenshotWidth = 512;
        public int screenshotHeight = 512;

        [Header("Persistence")]
        [Tooltip("Auto-save session after each agent turn for recompile resilience.")]
        public bool autoSaveSession = true;

        [Header("Memory")]
        [Tooltip("Agent data folder name (at project root, dot-prefixed).")]
        public string agentDataFolder = ".agent";

        [Header("Tool Providers")]
        [Tooltip("Additional MonoBehaviours with [AgentTool] methods to auto-discover.")]
        public MonoBehaviour[] toolProviders;

        [Header("References")]
        public AgentChatUI chatUI;
        public AgentThinkingUI thinkingUI;

        // =================================================================
        // Internal state
        // =================================================================

        protected UnityAgent agent;

        /// <summary>Project root / .agent/ — sits alongside Assets, avoids triggering reimport.</summary>
        protected string AgentDataPath =>
            System.IO.Path.Combine(Application.dataPath, "..", agentDataFolder);
        protected string SessionPath =>
            System.IO.Path.Combine(AgentDataPath, "session.json");
        protected string ChatSessionPath =>
            System.IO.Path.Combine(AgentDataPath, "chat-session.json");
        protected string MemoryPath =>
            System.IO.Path.Combine(AgentDataPath, "memory.md");

        [SerializeField, HideInInspector]
        private bool hasInitialized;

        // =================================================================
        // Unity lifecycle
        // =================================================================

        protected virtual void Awake()
        {
            Application.runInBackground = true;
            if (chatUI == null) chatUI = FindObjectOfType<AgentChatUI>();
            if (thinkingUI == null) thinkingUI = FindObjectOfType<AgentThinkingUI>();
        }

        protected virtual void Start()
        {
            InitAgent();

            if (!hasInitialized)
            {
                if (agent.LoadSession(SessionPath))
                {
                    chatUI?.AddSystemMessage("[Session restored]");
                }
                else
                {
                    chatUI?.AddSystemMessage(welcomeMessage);
                }
                hasInitialized = true;
            }

            Debug.Log($"[{GetType().Name}] Initialized — chat mode active.");
        }

        /// <summary>Called before Domain Reload. Save state to disk.</summary>
        protected virtual void OnDisable()
        {
            if (autoSaveSession && agent != null) SaveAll();

            if (chatUI != null && agent != null)
            {
                chatUI.UnbindAgent(agent);
                chatUI.OnUserMessage -= HandleUserMessage;
            }
        }

        /// <summary>Called after Domain Reload. Restore state from disk.</summary>
        protected virtual void OnEnable()
        {
            if (hasInitialized)
            {
                InitAgent();
                agent.LoadSession(SessionPath);
            }
        }

        protected virtual void OnDestroy()
        {
            if (chatUI != null && agent != null)
            {
                chatUI.UnbindAgent(agent);
                chatUI.OnUserMessage -= HandleUserMessage;
            }
            if (agent != null) agent.Dispose();
        }

        protected virtual void OnApplicationQuit()
        {
            if (autoSaveSession) SaveAll();
        }

        // =================================================================
        // Agent initialization
        // =================================================================

        private void InitAgent()
        {
            agent = UnityAgent.Instance;

            if (!string.IsNullOrEmpty(systemPromptResource))
                agent.LoadSystemPrompt(systemPromptResource);

            if (!string.IsNullOrEmpty(apiKey))
                agent.Configure(apiKey, baseURL, model, maxSteps);

            agent.LoadMemory(MemoryPath);
            RegisterBuiltinTools();
            DiscoverAttributeTools();
            RegisterTools();

            if (chatUI != null)
            {
                chatUI.BindAgent(agent);
                chatUI.OnUserMessage += HandleUserMessage;
            }

            agent.OnGenerationStart += () => thinkingUI?.ShowThinking();
            agent.OnGenerationEnd += () =>
            {
                thinkingUI?.HideThinking();
                if (autoSaveSession) SaveAll();
            };
        }

        // =================================================================
        // Built-in tools (available to all agents)
        // =================================================================

        private void RegisterBuiltinTools()
        {
            // Built-in tools use [AgentTool] attributes — discovered by DiscoverAttributeTools().
            // No manual registration needed.
        }

        /// <summary>
        /// Override to register domain-specific tools via manual RegisterTool() calls.
        /// Called during InitAgent(), after built-in tools and [AgentTool] auto-discovery.
        /// For most cases, just add [AgentTool] attributes to your handler methods instead.
        /// </summary>
        protected virtual void RegisterTools() { }

        /// <summary>
        /// Scan this object and all toolProviders for [AgentTool] methods and register them.
        /// </summary>
        private void DiscoverAttributeTools()
        {
            int count = AgentToolDiscovery.RegisterToolsFrom(agent, this);

            if (toolProviders != null)
            {
                foreach (var provider in toolProviders)
                {
                    if (provider != null)
                        count += AgentToolDiscovery.RegisterToolsFrom(agent, provider);
                }
            }

            if (count > 0)
                Debug.Log($"[{GetType().Name}] Auto-discovered {count} tools via [AgentTool] attributes.");
        }

        // =================================================================
        // User message handling
        // =================================================================

        protected virtual void HandleUserMessage(string message)
        {
            if (!agent.IsConfigured)
            {
                chatUI?.AddSystemMessage($"Error: API not configured. Set API key in {GetType().Name}.");
                return;
            }

            if (agent.IsRunning)
            {
                chatUI?.AddSystemMessage("Agent is busy. Please wait or click Stop.");
                return;
            }

            thinkingUI?.SetStatus("Thinking...");

            agent.SendMessageAsync(message, null,
                (response, isError) =>
                {
                    if (isError)
                    {
                        chatUI?.AddSystemMessage($"Error: {response}");
                        thinkingUI?.SetStatus("Error");
                    }
                    else
                    {
                        OnAgentResponse(response);
                    }
                },
                progress =>
                {
                    thinkingUI?.SetStatus(progress);
                }
            );
        }

        /// <summary>
        /// Called after a successful agent response. Override for domain-specific post-processing.
        /// Base implementation just sets status to "Ready".
        /// </summary>
        protected virtual void OnAgentResponse(string response)
        {
            thinkingUI?.SetStatus("Ready");
        }

        // =================================================================
        // Built-in tool handlers
        // =================================================================

        [AgentTool("captureScreenshot",
            "Capture a screenshot of the current game view. Returns the image for visual analysis.")]
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

        public class UpdateMemoryParams
        {
            [ToolParam("The observation or note to save.", required: true)]
            public string content;
        }

        [AgentTool("updateMemory",
            "Save important observations to long-term memory that persists across sessions.",
            ParametersType = typeof(UpdateMemoryParams))]
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
        // OnGUI — Stop button (always available)
        // =================================================================

        protected virtual void OnGUI()
        {
            if (agent != null && agent.IsRunning)
            {
                float btnW = 80f, btnH = 28f, pad = 8f;
                float x = pad;
                float y = Screen.height - btnH - pad;

                // Place stop button at bottom-left (avoid overlapping chat panel)
                if (chatUI != null && !chatUI.anchorRight)
                    x = chatUI.panelWidth + pad;

                GUI.skin.button.fontSize = 13;
                if (GUI.Button(new Rect(x, y, btnW, btnH), "Stop"))
                {
                    agent.AbortGeneration();
                    thinkingUI?.SetStatus("Stopped");
                }
            }
        }

        // =================================================================
        // Session persistence
        // =================================================================

        protected void SaveAll()
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
                catch (Exception e)
                {
                    Debug.LogError($"[{GetType().Name}] Chat save failed: {e.Message}");
                }
            }
        }

        protected void ClearSession()
        {
            agent?.ClearHistory();
            chatUI?.Clear();
            thinkingUI?.ResetUI();

            agent?.DeleteSession(SessionPath);
            if (System.IO.File.Exists(ChatSessionPath))
                System.IO.File.Delete(ChatSessionPath);

            chatUI?.AddSystemMessage(welcomeMessage);
            thinkingUI?.SetStatus("Ready");
        }
    }
}
