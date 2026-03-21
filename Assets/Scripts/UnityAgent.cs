using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LLMAgent
{
    /// <summary>
    /// Generic LLM agent for Unity. Calls OpenAI-compatible Chat Completions API
    /// with SSE streaming, runs tool-calling loops, and dispatches to user-registered
    /// tool handlers. No external dependencies — just C# and UnityWebRequest.
    ///
    /// Usage:
    ///   var agent = UnityAgent.Instance;
    ///   agent.Configure(apiKey, baseURL, model, maxSteps);
    ///   agent.SetSystemPrompt(prompt);
    ///   agent.RegisterTool("myTool", "description", paramsJson, MyHandler);
    ///   agent.OnStreamToken += token => Debug.Log(token);
    ///   agent.SendMessageAsync("hello", null, callback, progress);
    /// </summary>
    public class UnityAgent : MonoBehaviour
    {
        // =================================================================
        // Public types
        // =================================================================

        public struct ToolResult
        {
            public string content;
            public string imageBase64;
            public string imageMimeType;
        }

        public delegate IEnumerator ToolHandler(string arguments, Action<ToolResult> callback);

        /// <summary>Permission request: toolName, argsSummary, respond(bool approved).</summary>
        public delegate void PermissionRequestHandler(string toolName, string description, Action<bool> respond);

        [Serializable]
        public struct TokenUsage
        {
            public int inputTokens;
            public int outputTokens;
            public int totalTokens;
        }

        /// <summary>Session save data for persistence across recompiles.</summary>
        [Serializable]
        public class SessionData
        {
            public string[] history;
            public int[] charCounts;
            public int inputTokens;
            public int outputTokens;
        }

        // =================================================================
        // Events — subscribe from UI to visualize agent activity
        // =================================================================

        /// <summary>Fired for each text token during streaming.</summary>
        public event Action<string> OnStreamToken;

        /// <summary>Fired when a tool call begins. Args: toolName, toolCallId.</summary>
        public event Action<string, string> OnToolCallBegin;

        /// <summary>Fired when a tool call completes. Args: toolName, toolCallId, resultContent.</summary>
        public event Action<string, string, string> OnToolCallEnd;

        /// <summary>Fired when the agent starts processing a user message.</summary>
        public event Action OnGenerationStart;

        /// <summary>Fired when the agent finishes (text response or error).</summary>
        public event Action OnGenerationEnd;

        /// <summary>Fired when a tool requires permission. UI should show Allow/Deny.</summary>
        public event PermissionRequestHandler OnPermissionRequired;

        /// <summary>Fired when token usage is updated (cumulative session totals).</summary>
        public event Action<TokenUsage> OnTokenUsage;

        // =================================================================
        // Configuration
        // =================================================================

        private string apiKey;
        private string baseURL = "https://api.openai.com/v1";
        private string model = "gpt-4o";
        private int maxSteps = 25;
        private string systemPrompt = "";

        public int maxContextChars = 400_000;
        public int minKeepMessages = 10;
        public int maxRetries = 3;

        // =================================================================
        // Tool registry
        // =================================================================

        private struct RegisteredTool
        {
            public string name;
            public string definitionJson;
            public ToolHandler handler;
            public bool requiresPermission;
        }

        private readonly List<RegisteredTool> tools = new List<RegisteredTool>();
        private string cachedToolsJson;
        private bool toolsJsonDirty = true;

        // =================================================================
        // Conversation state
        // =================================================================

        private readonly List<string> conversationHistory = new List<string>();
        private readonly List<int> messageCharCounts = new List<int>();
        private bool isRunning;
        private bool abortRequested;

        // Token usage tracking
        private TokenUsage sessionUsage;
        public TokenUsage SessionUsage => sessionUsage;

        // Long-term memory
        private string memoryContent = "";
        public string MemoryContent => memoryContent;

        // =================================================================
        // Singleton
        // =================================================================

        private static UnityAgent _instance;

        public static UnityAgent Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[UnityAgent]");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<UnityAgent>();
                }
                return _instance;
            }
        }

        // =================================================================
        // Public API — Configuration
        // =================================================================

        public void Configure(string apiKey, string baseURL, string model, int maxSteps)
        {
            this.apiKey = apiKey;
            if (!string.IsNullOrEmpty(baseURL)) this.baseURL = baseURL.TrimEnd('/');
            if (!string.IsNullOrEmpty(model)) this.model = model;
            if (maxSteps > 0) this.maxSteps = maxSteps;
        }

        public void SetSystemPrompt(string prompt) => systemPrompt = prompt ?? "";

        public void LoadSystemPrompt(string resourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            systemPrompt = asset != null ? asset.text : "";
            Debug.Log($"[UnityAgent] System prompt loaded ({systemPrompt.Length} chars)");
        }

        public bool IsConfigured => !string.IsNullOrEmpty(apiKey);
        public bool IsRunning => isRunning;

        // =================================================================
        // Public API — Tool registration
        // =================================================================

        public void RegisterTool(string name, string description, string parametersJson,
            ToolHandler handler, bool requiresPermission = false)
        {
            if (string.IsNullOrEmpty(parametersJson))
                parametersJson = "{\"type\":\"object\",\"properties\":{},\"required\":[]}";

            string defJson = "{\"type\":\"function\",\"function\":{" +
                $"\"name\":\"{EscapeJson(name)}\"," +
                $"\"description\":\"{EscapeJson(description)}\"," +
                $"\"parameters\":{parametersJson}" +
                "}}";

            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].name == name)
                {
                    tools[i] = new RegisteredTool { name = name, definitionJson = defJson, handler = handler, requiresPermission = requiresPermission };
                    toolsJsonDirty = true;
                    return;
                }
            }

            tools.Add(new RegisteredTool { name = name, definitionJson = defJson, handler = handler, requiresPermission = requiresPermission });
            toolsJsonDirty = true;
        }

        public void UnregisterTool(string name)
        {
            tools.RemoveAll(t => t.name == name);
            toolsJsonDirty = true;
        }

        public void ClearTools()
        {
            tools.Clear();
            toolsJsonDirty = true;
        }

        // =================================================================
        // Public API — Conversation
        // =================================================================

        public void SendMessageAsync(string message, string imageBase64,
            Action<string, bool> callback, Action<string> progressCallback)
        {
            if (isRunning)
            {
                callback?.Invoke("Agent is already running.", true);
                return;
            }
            StartCoroutine(RunAgentLoop(message, imageBase64, callback, progressCallback));
        }

        public void AbortGeneration() => abortRequested = true;

        public void ClearHistory()
        {
            conversationHistory.Clear();
            messageCharCounts.Clear();
        }

        public void Dispose()
        {
            AbortGeneration();
            ClearHistory();
        }

        // =================================================================
        // Public API — Session persistence
        // =================================================================

        public void SaveSession(string path)
        {
            var data = new SessionData
            {
                history = conversationHistory.ToArray(),
                charCounts = messageCharCounts.ToArray(),
                inputTokens = sessionUsage.inputTokens,
                outputTokens = sessionUsage.outputTokens
            };
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(path, JsonUtility.ToJson(data, true));
                Debug.Log($"[UnityAgent] Session saved ({conversationHistory.Count} messages)");
            }
            catch (Exception e) { Debug.LogError($"[UnityAgent] Save failed: {e.Message}"); }
        }

        public bool LoadSession(string path)
        {
            if (!System.IO.File.Exists(path)) return false;
            try
            {
                var data = JsonUtility.FromJson<SessionData>(System.IO.File.ReadAllText(path));
                if (data?.history == null) return false;
                conversationHistory.Clear();
                conversationHistory.AddRange(data.history);
                messageCharCounts.Clear();
                messageCharCounts.AddRange(data.charCounts);
                sessionUsage.inputTokens = data.inputTokens;
                sessionUsage.outputTokens = data.outputTokens;
                sessionUsage.totalTokens = data.inputTokens + data.outputTokens;
                Debug.Log($"[UnityAgent] Session restored ({conversationHistory.Count} messages)");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityAgent] Load failed: {e.Message}");
                return false;
            }
        }

        public void DeleteSession(string path)
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }

        // =================================================================
        // Public API — Long-term memory
        // =================================================================

        public void LoadMemory(string path)
        {
            if (System.IO.File.Exists(path))
            {
                memoryContent = System.IO.File.ReadAllText(path);
                Debug.Log($"[UnityAgent] Memory loaded ({memoryContent.Length} chars)");
            }
            else
            {
                memoryContent = "";
                Debug.Log("[UnityAgent] No memory file found, starting fresh.");
            }
        }

        public void AppendMemory(string path, string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            string entry = $"\n[{DateTime.Now:yyyy-MM-dd HH:mm}] {content}";
            memoryContent += entry;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(path, entry);
            }
            catch (Exception e) { Debug.LogError($"[UnityAgent] Memory write failed: {e.Message}"); }
        }

        public void ClearMemory(string path)
        {
            memoryContent = "";
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }

        // =================================================================
        // Agent loop — with streaming
        // =================================================================

        private IEnumerator RunAgentLoop(string userMessage, string imageBase64,
            Action<string, bool> callback, Action<string> progressCallback)
        {
            isRunning = true;
            abortRequested = false;
            OnGenerationStart?.Invoke();

            AddMessage(BuildUserMessage(userMessage, imageBase64));

            int steps = 0;
            string finalResponse = "";

            while (steps < maxSteps && !abortRequested)
            {
                TrimHistory();

                bool isLastStep = (steps == maxSteps - 1);
                string requestBody = BuildRequestBody(disableTools: isLastStep, stream: true);

                // --- Streaming API call with retry ---
                StreamResult streamResult = null;
                string error = null;
                yield return CallStreamingAPIWithRetry(requestBody, (res, err) =>
                {
                    streamResult = res;
                    error = err;
                });

                if (error != null)
                {
                    isRunning = false;
                    OnGenerationEnd?.Invoke();
                    callback?.Invoke($"API error: {error}", true);
                    yield break;
                }

                // Track token usage
                if (streamResult.usage.totalTokens > 0)
                {
                    sessionUsage.inputTokens += streamResult.usage.inputTokens;
                    sessionUsage.outputTokens += streamResult.usage.outputTokens;
                    sessionUsage.totalTokens = sessionUsage.inputTokens + sessionUsage.outputTokens;
                    OnTokenUsage?.Invoke(sessionUsage);
                }

                // Build assistant message JSON for history
                string assistantMsgJson = BuildAssistantMessageJson(
                    streamResult.content, streamResult.toolCalls);
                AddMessage(assistantMsgJson);

                // Execute tool calls if any
                if (streamResult.toolCalls.Count > 0 && !isLastStep)
                {
                    foreach (var tc in streamResult.toolCalls)
                    {
                        if (abortRequested) break;

                        steps++;
                        progressCallback?.Invoke($"[{steps}/{maxSteps}] {tc.name}");
                        Debug.Log($"[UnityAgent] Step {steps}: {tc.name}({Truncate(tc.arguments, 120)})");

                        // --- Permission check ---
                        bool needsPermission = false;
                        foreach (var t in tools)
                            if (t.name == tc.name) { needsPermission = t.requiresPermission; break; }

                        if (needsPermission && OnPermissionRequired != null)
                        {
                            bool? approved = null;
                            OnPermissionRequired.Invoke(tc.name, Truncate(tc.arguments, 200),
                                result => approved = result);
                            while (approved == null) yield return null;

                            if (!approved.Value)
                            {
                                string denyMsg = "{\"error\":\"Permission denied by user.\"}";
                                AddMessage(BuildToolResultMessage(tc.id, denyMsg));
                                OnToolCallEnd?.Invoke(tc.name, tc.id, "Permission denied");
                                continue;
                            }
                        }

                        OnToolCallBegin?.Invoke(tc.name, tc.id);

                        ToolHandler handler = null;
                        foreach (var t in tools)
                        {
                            if (t.name == tc.name) { handler = t.handler; break; }
                        }

                        if (handler == null)
                        {
                            string errResult = $"{{\"error\":\"Unknown tool: {EscapeJson(tc.name)}\"}}";
                            AddMessage(BuildToolResultMessage(tc.id, errResult));
                            OnToolCallEnd?.Invoke(tc.name, tc.id, errResult);
                            continue;
                        }

                        ToolResult toolResult = default;
                        bool done = false;
                        yield return handler(tc.arguments, r => { toolResult = r; done = true; });
                        while (!done) yield return null;

                        AddMessage(BuildToolResultMessage(tc.id, toolResult.content ?? ""));
                        OnToolCallEnd?.Invoke(tc.name, tc.id, toolResult.content ?? "");

                        if (!string.IsNullOrEmpty(toolResult.imageBase64))
                        {
                            string mime = string.IsNullOrEmpty(toolResult.imageMimeType)
                                ? "image/png" : toolResult.imageMimeType;
                            AddMessage(BuildImageMessage(toolResult.imageBase64, mime));
                        }
                    }
                    continue;
                }
                else
                {
                    finalResponse = streamResult.content;
                    break;
                }
            }

            isRunning = false;
            OnGenerationEnd?.Invoke();

            if (abortRequested)
                callback?.Invoke("Generation aborted.", true);
            else if (steps >= maxSteps && string.IsNullOrEmpty(finalResponse))
                callback?.Invoke("[Reached max steps without final response]", false);
            else
                callback?.Invoke(finalResponse, false);
        }

        // =================================================================
        // SSE Streaming
        // =================================================================

        private class StreamResult
        {
            public string content = "";
            public List<ToolCallInfo> toolCalls = new List<ToolCallInfo>();
            public TokenUsage usage;
        }

        private struct ToolCallInfo
        {
            public string id;
            public string name;
            public string arguments;
        }

        /// <summary>
        /// Custom DownloadHandler that receives SSE chunks and queues parsed lines.
        /// ReceiveData is called on the main thread by Unity.
        /// </summary>
        private class SSEDownloadHandler : DownloadHandlerScript
        {
            public readonly Queue<string> lines = new Queue<string>();
            public bool isDone;
            private string buffer = "";

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                buffer += Encoding.UTF8.GetString(data, 0, dataLength);

                while (true)
                {
                    int idx = buffer.IndexOf('\n');
                    if (idx < 0) break;

                    string line = buffer.Substring(0, idx).TrimEnd('\r');
                    buffer = buffer.Substring(idx + 1);

                    if (line.StartsWith("data: "))
                    {
                        string payload = line.Substring(6);
                        if (payload == "[DONE]")
                            isDone = true;
                        else
                            lines.Enqueue(payload);
                    }
                }

                return true;
            }

            protected override void CompleteContent() { isDone = true; }
        }

        private IEnumerator CallStreamingAPIWithRetry(string requestBody, Action<StreamResult, string> callback)
        {
            int attempt = 0;

            while (true)
            {
                StreamResult result = null;
                string error = null;
                long httpCode = 0;

                yield return CallStreamingAPI(requestBody, (res, err, code) =>
                {
                    result = res;
                    error = err;
                    httpCode = code;
                });

                if (error == null)
                {
                    callback?.Invoke(result, null);
                    yield break;
                }

                attempt++;
                bool retryable = httpCode == 429 || httpCode >= 500;

                if (!retryable || attempt >= maxRetries)
                {
                    callback?.Invoke(null, error);
                    yield break;
                }

                float delay = Mathf.Pow(2f, attempt);
                Debug.LogWarning($"[UnityAgent] HTTP {httpCode}, retrying in {delay}s ({attempt}/{maxRetries})...");
                yield return new WaitForSeconds(delay);
            }
        }

        private IEnumerator CallStreamingAPI(string requestBody,
            Action<StreamResult, string, long> callback)
        {
            string url = baseURL + "/chat/completions";

            var sseHandler = new SSEDownloadHandler();
            var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestBody));
            request.downloadHandler = sseHandler;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            request.SetRequestHeader("Accept", "text/event-stream");
            request.timeout = 120;

            var op = request.SendWebRequest();

            var result = new StreamResult();
            // Accumulate tool calls by index
            var toolCallAccum = new Dictionary<int, ToolCallInfo>();

            // Process SSE chunks as they arrive
            while (!op.isDone || sseHandler.lines.Count > 0)
            {
                while (sseHandler.lines.Count > 0)
                {
                    string chunk = sseHandler.lines.Dequeue();
                    ProcessSSEChunk(chunk, result, toolCallAccum);
                }
                yield return null;
            }

            // Process any remaining lines
            while (sseHandler.lines.Count > 0)
            {
                string chunk = sseHandler.lines.Dequeue();
                ProcessSSEChunk(chunk, result, toolCallAccum);
            }

            // Collect tool calls from accumulator
            foreach (var kv in toolCallAccum)
                result.toolCalls.Add(kv.Value);

            long code = request.responseCode;
            if (request.result == UnityWebRequest.Result.Success || code == 200)
            {
                callback?.Invoke(result, null, code);
            }
            else
            {
                // For non-streaming error responses, try to read the body
                string detail = request.error ?? $"HTTP {code}";
                callback?.Invoke(null, detail, code);
            }

            request.Dispose();
        }

        private void ProcessSSEChunk(string json, StreamResult result,
            Dictionary<int, ToolCallInfo> toolCallAccum)
        {
            // Extract delta content
            int deltaIdx = json.IndexOf("\"delta\"", StringComparison.Ordinal);
            if (deltaIdx >= 0)
            {
                int deltaStart = json.IndexOf('{', deltaIdx);
                if (deltaStart >= 0)
                {
                    int deltaEnd = FindMatchingBrace(json, deltaStart);
                    if (deltaEnd >= 0)
                    {
                        string delta = json.Substring(deltaStart, deltaEnd - deltaStart + 1);

                        // Content token
                        string contentToken = ExtractStringField(delta, "content");
                        if (contentToken != null)
                        {
                            result.content += contentToken;
                            OnStreamToken?.Invoke(contentToken);
                        }

                        // Tool calls (incremental)
                        int tcIdx = delta.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
                        if (tcIdx >= 0)
                        {
                            int arrStart = delta.IndexOf('[', tcIdx);
                            if (arrStart >= 0)
                            {
                                int arrEnd = FindMatchingBracket(delta, arrStart);
                                if (arrEnd >= 0)
                                {
                                    string tcArr = delta.Substring(arrStart, arrEnd - arrStart + 1);
                                    ParseStreamingToolCalls(tcArr, toolCallAccum);
                                }
                            }
                        }
                    }
                }
            }

            // Extract usage (appears in final chunk)
            int usageIdx = json.IndexOf("\"usage\"", StringComparison.Ordinal);
            if (usageIdx >= 0)
            {
                int usageStart = json.IndexOf('{', usageIdx);
                if (usageStart >= 0)
                {
                    int usageEnd = FindMatchingBrace(json, usageStart);
                    if (usageEnd >= 0)
                    {
                        string u = json.Substring(usageStart, usageEnd - usageStart + 1);
                        string pt = ExtractNumberField(u, "prompt_tokens");
                        string ct = ExtractNumberField(u, "completion_tokens");
                        string tt = ExtractNumberField(u, "total_tokens");
                        if (pt != null) int.TryParse(pt, out result.usage.inputTokens);
                        if (ct != null) int.TryParse(ct, out result.usage.outputTokens);
                        if (tt != null) int.TryParse(tt, out result.usage.totalTokens);
                    }
                }
            }
        }

        private void ParseStreamingToolCalls(string arrayJson,
            Dictionary<int, ToolCallInfo> accum)
        {
            int pos = 0;
            while (pos < arrayJson.Length)
            {
                int objStart = arrayJson.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = FindMatchingBrace(arrayJson, objStart);
                if (objEnd < 0) break;

                string objStr = arrayJson.Substring(objStart, objEnd - objStart + 1);

                string indexStr = ExtractNumberField(objStr, "index");
                int index = 0;
                if (indexStr != null) int.TryParse(indexStr, out index);

                if (!accum.ContainsKey(index))
                    accum[index] = new ToolCallInfo { id = "", name = "", arguments = "" };

                var tc = accum[index];

                // id appears in the first chunk
                string id = ExtractStringField(objStr, "id");
                if (id != null) tc.id = id;

                // function.name and function.arguments
                int fnIdx = objStr.IndexOf("\"function\"", StringComparison.Ordinal);
                if (fnIdx >= 0)
                {
                    int fnStart = objStr.IndexOf('{', fnIdx);
                    if (fnStart >= 0)
                    {
                        int fnEnd = FindMatchingBrace(objStr, fnStart);
                        if (fnEnd >= 0)
                        {
                            string fnStr = objStr.Substring(fnStart, fnEnd - fnStart + 1);
                            string name = ExtractStringField(fnStr, "name");
                            if (name != null) tc.name = name;
                            string args = ExtractStringField(fnStr, "arguments");
                            if (args != null) tc.arguments += args;
                        }
                    }
                }

                accum[index] = tc;
                pos = objEnd + 1;
            }
        }

        // =================================================================
        // Build assistant message JSON from streaming result
        // =================================================================

        private static string BuildAssistantMessageJson(string content, List<ToolCallInfo> toolCalls)
        {
            if (toolCalls == null || toolCalls.Count == 0)
            {
                if (content == null)
                    return "{\"role\":\"assistant\",\"content\":null}";
                return $"{{\"role\":\"assistant\",\"content\":\"{EscapeJson(content)}\"}}";
            }

            var sb = new StringBuilder();
            sb.Append("{\"role\":\"assistant\"");

            if (string.IsNullOrEmpty(content))
                sb.Append(",\"content\":null");
            else
                sb.Append($",\"content\":\"{EscapeJson(content)}\"");

            sb.Append(",\"tool_calls\":[");
            for (int i = 0; i < toolCalls.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var tc = toolCalls[i];
                sb.Append($"{{\"id\":\"{EscapeJson(tc.id)}\",\"type\":\"function\",\"function\":{{");
                sb.Append($"\"name\":\"{EscapeJson(tc.name)}\",");
                sb.Append($"\"arguments\":\"{EscapeJson(tc.arguments)}\"");
                sb.Append("}}");
            }
            sb.Append("]}");

            return sb.ToString();
        }

        // =================================================================
        // Conversation history management & sliding window
        // =================================================================

        private void AddMessage(string messageJson)
        {
            conversationHistory.Add(messageJson);
            messageCharCounts.Add(messageJson.Length);
        }

        private void TrimHistory()
        {
            int totalChars = 0;
            foreach (var c in messageCharCounts) totalChars += c;

            if (totalChars <= maxContextChars) return;

            int canDrop = conversationHistory.Count - minKeepMessages;
            if (canDrop <= 0) return;

            // First pass: drop image messages (biggest)
            for (int i = 0; i < canDrop && totalChars > maxContextChars; i++)
            {
                if (conversationHistory[i].Contains("image_url"))
                {
                    totalChars -= messageCharCounts[i];
                    conversationHistory.RemoveAt(i);
                    messageCharCounts.RemoveAt(i);
                    canDrop--;
                    i--;
                }
            }

            // Second pass: drop any from front
            while (canDrop > 0 && totalChars > maxContextChars &&
                   conversationHistory.Count > minKeepMessages)
            {
                totalChars -= messageCharCounts[0];
                conversationHistory.RemoveAt(0);
                messageCharCounts.RemoveAt(0);
                canDrop--;
            }
        }

        // =================================================================
        // JSON building
        // =================================================================

        private string BuildRequestBody(bool disableTools, bool stream = false)
        {
            // Build full system prompt with memory
            string fullPrompt = systemPrompt;
            if (!string.IsNullOrEmpty(memoryContent))
                fullPrompt = "[Long-term Memory]\n" + memoryContent + "\n\n[Instructions]\n" + systemPrompt;

            var sb = new StringBuilder(4096);
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            if (stream) sb.Append("\"stream\":true,\"stream_options\":{\"include_usage\":true},");
            sb.Append("\"messages\":[");

            sb.Append($"{{\"role\":\"system\",\"content\":\"{EscapeJson(fullPrompt)}\"}}");

            if (disableTools)
            {
                sb.Append(",{\"role\":\"system\",\"content\":\"");
                sb.Append(EscapeJson("[SYSTEM] You have reached the maximum number of tool-call steps. " +
                    "Do NOT call any more tools. Summarize your progress and current state in text."));
                sb.Append("\"}");
            }

            foreach (var msg in conversationHistory)
            {
                sb.Append(",");
                sb.Append(msg);
            }

            sb.Append("]");

            if (!disableTools && tools.Count > 0)
            {
                sb.Append(",\"tools\":");
                sb.Append(GetToolsJson());
            }

            sb.Append("}");
            return sb.ToString();
        }

        private string GetToolsJson()
        {
            if (!toolsJsonDirty && cachedToolsJson != null) return cachedToolsJson;

            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < tools.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(tools[i].definitionJson);
            }
            sb.Append("]");

            cachedToolsJson = sb.ToString();
            toolsJsonDirty = false;
            return cachedToolsJson;
        }

        // =================================================================
        // Message builders
        // =================================================================

        private static string BuildUserMessage(string text, string imageBase64)
        {
            if (string.IsNullOrEmpty(imageBase64))
                return $"{{\"role\":\"user\",\"content\":\"{EscapeJson(text)}\"}}";

            return "{\"role\":\"user\",\"content\":[" +
                   $"{{\"type\":\"text\",\"text\":\"{EscapeJson(text)}\"}}," +
                   "{\"type\":\"image_url\",\"image_url\":{" +
                   $"\"url\":\"data:image/png;base64,{imageBase64}\"" +
                   "}}]}";
        }

        private static string BuildImageMessage(string base64, string mimeType)
        {
            return "{\"role\":\"user\",\"content\":[" +
                   "{\"type\":\"text\",\"text\":\"[Image from tool result]\"}," +
                   "{\"type\":\"image_url\",\"image_url\":{" +
                   $"\"url\":\"data:{EscapeJson(mimeType)};base64,{base64}\"" +
                   "}}]}";
        }

        private static string BuildToolResultMessage(string toolCallId, string content)
        {
            return $"{{\"role\":\"tool\",\"tool_call_id\":\"{EscapeJson(toolCallId)}\"," +
                   $"\"content\":\"{EscapeJson(content)}\"}}";
        }

        // =================================================================
        // JSON utility helpers
        // =================================================================

        internal static string ExtractStringField(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
            if (colonIdx < 0) return null;

            int valStart = colonIdx + 1;
            while (valStart < json.Length && char.IsWhiteSpace(json[valStart])) valStart++;
            if (valStart >= json.Length) return null;

            if (json[valStart] == 'n' && valStart + 3 < json.Length &&
                json.Substring(valStart, 4) == "null")
                return null;

            if (json[valStart] != '"') return null;

            var sb = new StringBuilder();
            int i = valStart + 1;
            while (i < json.Length)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append('\\'); sb.Append(next); break;
                    }
                    i += 2;
                }
                else if (json[i] == '"') break;
                else { sb.Append(json[i]); i++; }
            }
            return sb.ToString();
        }

        internal static string ExtractNumberField(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
            if (colonIdx < 0) return null;

            int valStart = colonIdx + 1;
            while (valStart < json.Length && char.IsWhiteSpace(json[valStart])) valStart++;

            var sb = new StringBuilder();
            int i = valStart;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == '-'))
            { sb.Append(json[i]); i++; }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        internal static int FindMatchingBrace(string json, int openIdx)
            => FindMatching(json, openIdx, '{', '}');

        internal static int FindMatchingBracket(string json, int openIdx)
            => FindMatching(json, openIdx, '[', ']');

        private static int FindMatching(string json, int openIdx, char open, char close)
        {
            int depth = 0;
            bool inString = false;
            for (int i = openIdx; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == open) depth++;
                if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        internal static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s ?? "";
            return s.Substring(0, maxLen) + "...";
        }
    }
}
