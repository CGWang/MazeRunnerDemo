using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LLMAgent
{
    /// <summary>
    /// Generic LLM agent for Unity. Calls OpenAI-compatible Chat Completions API,
    /// runs tool-calling loops, and dispatches to user-registered tool handlers.
    /// No external dependencies — just C# and UnityWebRequest.
    ///
    /// Usage:
    ///   var agent = UnityAgent.Instance;
    ///   agent.Configure(apiKey, baseURL, model, maxSteps);
    ///   agent.SetSystemPrompt(prompt);
    ///   agent.RegisterTool("myTool", "description", paramsJson, MyHandler);
    ///   agent.SendMessageAsync("hello", null, callback, progress);
    /// </summary>
    public class UnityAgent : MonoBehaviour
    {
        // =================================================================
        // Public types
        // =================================================================

        /// <summary>Result returned by a tool handler.</summary>
        public struct ToolResult
        {
            /// <summary>Text content sent back to the LLM as the tool result.</summary>
            public string content;

            /// <summary>
            /// Optional base64-encoded image. If set, an additional user message
            /// with the image is injected after the tool result so the LLM can see it.
            /// (Most APIs don't support images inside tool result messages.)
            /// </summary>
            public string imageBase64;

            /// <summary>MIME type of the image. Defaults to "image/png".</summary>
            public string imageMimeType;
        }

        /// <summary>
        /// Coroutine-based tool handler. Receives the arguments JSON string from the LLM,
        /// performs work (may yield), and invokes the callback with the result.
        /// </summary>
        public delegate IEnumerator ToolHandler(string arguments, Action<ToolResult> callback);

        // =================================================================
        // Configuration
        // =================================================================

        private string apiKey;
        private string baseURL = "https://api.openai.com/v1";
        private string model = "gpt-4o";
        private int maxSteps = 25;
        private string systemPrompt = "";

        /// <summary>
        /// Approximate character budget for conversation history.
        /// When exceeded, oldest messages are trimmed (sliding window).
        /// Default 400,000 chars ≈ 100K tokens.
        /// </summary>
        public int maxContextChars = 400_000;

        /// <summary>
        /// Minimum number of recent messages to always keep,
        /// even when trimming for context length.
        /// </summary>
        public int minKeepMessages = 10;

        /// <summary>Maximum retry attempts for transient API errors (429, 5xx).</summary>
        public int maxRetries = 3;

        // =================================================================
        // Tool registry
        // =================================================================

        private struct RegisteredTool
        {
            public string name;
            public string definitionJson; // Full {"type":"function","function":{...}} object
            public ToolHandler handler;
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

        public void SetSystemPrompt(string prompt)
        {
            systemPrompt = prompt ?? "";
        }

        /// <summary>Load system prompt from a TextAsset in Resources.</summary>
        public void LoadSystemPrompt(string resourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            systemPrompt = asset != null ? asset.text : "";
            Debug.Log($"[UnityAgent] System prompt loaded ({systemPrompt.Length} chars) from {resourcePath}");
        }

        public bool IsConfigured => !string.IsNullOrEmpty(apiKey);
        public bool IsRunning => isRunning;

        // =================================================================
        // Public API — Tool registration
        // =================================================================

        /// <summary>
        /// Register a tool that the LLM can call.
        /// </summary>
        /// <param name="name">Tool name (must match what the LLM will call).</param>
        /// <param name="description">Human-readable description for the LLM.</param>
        /// <param name="parametersJson">
        /// JSON Schema for the parameters object, e.g.:
        /// {"type":"object","properties":{"x":{"type":"number"}},"required":["x"]}
        /// Pass null or empty for no parameters.
        /// </param>
        /// <param name="handler">Coroutine handler that executes the tool.</param>
        public void RegisterTool(string name, string description, string parametersJson, ToolHandler handler)
        {
            if (string.IsNullOrEmpty(parametersJson))
                parametersJson = "{\"type\":\"object\",\"properties\":{},\"required\":[]}";

            string defJson = "{\"type\":\"function\",\"function\":{" +
                $"\"name\":\"{EscapeJson(name)}\"," +
                $"\"description\":\"{EscapeJson(description)}\"," +
                $"\"parameters\":{parametersJson}" +
                "}}";

            // Replace if already registered
            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].name == name)
                {
                    tools[i] = new RegisteredTool { name = name, definitionJson = defJson, handler = handler };
                    toolsJsonDirty = true;
                    return;
                }
            }

            tools.Add(new RegisteredTool { name = name, definitionJson = defJson, handler = handler });
            toolsJsonDirty = true;
        }

        /// <summary>Unregister a previously registered tool.</summary>
        public void UnregisterTool(string name)
        {
            tools.RemoveAll(t => t.name == name);
            toolsJsonDirty = true;
        }

        /// <summary>Remove all registered tools.</summary>
        public void ClearTools()
        {
            tools.Clear();
            toolsJsonDirty = true;
        }

        // =================================================================
        // Public API — Conversation
        // =================================================================

        /// <summary>
        /// Send a message to the LLM and run the full tool-calling loop until
        /// the LLM produces a final text response or limits are reached.
        /// </summary>
        /// <param name="message">User message text.</param>
        /// <param name="imageBase64">Optional base64-encoded image to attach.</param>
        /// <param name="callback">Called with (response, isError) when done.</param>
        /// <param name="progressCallback">Called after each tool execution with a status string.</param>
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
        // Agent loop
        // =================================================================

        private IEnumerator RunAgentLoop(string userMessage, string imageBase64,
            Action<string, bool> callback, Action<string> progressCallback)
        {
            isRunning = true;
            abortRequested = false;

            AddMessage(BuildUserMessage(userMessage, imageBase64));

            int steps = 0;
            string finalResponse = "";

            while (steps < maxSteps && !abortRequested)
            {
                // Trim history if over budget
                TrimHistory();

                // On the last allowed step, disable tools and ask for summary
                bool isLastStep = (steps == maxSteps - 1);
                string requestBody = BuildRequestBody(disableTools: isLastStep);

                // Call API with retry
                string responseBody = null;
                string error = null;
                yield return CallAPIWithRetry(requestBody, (res, err) =>
                {
                    responseBody = res;
                    error = err;
                });

                if (error != null)
                {
                    isRunning = false;
                    callback?.Invoke($"API error: {error}", true);
                    yield break;
                }

                // Parse response
                string assistantContent;
                string finishReason;
                List<ToolCallInfo> toolCalls;
                string assistantMessageJson;

                if (!ParseResponse(responseBody, out assistantContent, out finishReason,
                        out toolCalls, out assistantMessageJson))
                {
                    isRunning = false;
                    callback?.Invoke($"Failed to parse API response: {Truncate(responseBody, 500)}", true);
                    yield break;
                }

                AddMessage(assistantMessageJson);

                // Execute tool calls if any
                if (toolCalls != null && toolCalls.Count > 0 && !isLastStep)
                {
                    foreach (var tc in toolCalls)
                    {
                        if (abortRequested) break;

                        steps++;
                        progressCallback?.Invoke($"[{steps}/{maxSteps}] {tc.name}");
                        Debug.Log($"[UnityAgent] Step {steps}: {tc.name}({Truncate(tc.arguments, 120)})");

                        // Find handler
                        ToolHandler handler = null;
                        foreach (var t in tools)
                        {
                            if (t.name == tc.name) { handler = t.handler; break; }
                        }

                        if (handler == null)
                        {
                            AddMessage(BuildToolResultMessage(tc.id,
                                $"{{\"error\":\"Unknown tool: {EscapeJson(tc.name)}\"}}"));
                            continue;
                        }

                        // Execute handler
                        ToolResult toolResult = default;
                        bool done = false;
                        yield return handler(tc.arguments, r => { toolResult = r; done = true; });
                        if (!done)
                        {
                            // Handler didn't call callback — wait
                            while (!done) yield return null;
                        }

                        AddMessage(BuildToolResultMessage(tc.id, toolResult.content ?? ""));

                        // Inject image if provided
                        if (!string.IsNullOrEmpty(toolResult.imageBase64))
                        {
                            string mime = string.IsNullOrEmpty(toolResult.imageMimeType)
                                ? "image/png" : toolResult.imageMimeType;
                            AddMessage(BuildImageMessage(toolResult.imageBase64, mime));
                        }
                    }
                    continue; // Loop back to call LLM with tool results
                }
                else
                {
                    // No tool calls or last step — final response
                    finalResponse = assistantContent ?? "";
                    break;
                }
            }

            isRunning = false;

            if (abortRequested)
                callback?.Invoke("Generation aborted.", true);
            else if (steps >= maxSteps && string.IsNullOrEmpty(finalResponse))
                callback?.Invoke("[Reached max steps without final response]", false);
            else
                callback?.Invoke(finalResponse, false);
        }

        // =================================================================
        // Conversation history management & sliding window
        // =================================================================

        private void AddMessage(string messageJson)
        {
            conversationHistory.Add(messageJson);
            messageCharCounts.Add(messageJson.Length);
        }

        /// <summary>
        /// Sliding window: drop oldest messages when total chars exceed budget.
        /// Preserves at least <see cref="minKeepMessages"/> recent messages.
        /// Drops image-bearing messages preferentially (they're the biggest).
        /// </summary>
        private void TrimHistory()
        {
            int totalChars = 0;
            foreach (var c in messageCharCounts) totalChars += c;

            if (totalChars <= maxContextChars) return;

            int canDrop = conversationHistory.Count - minKeepMessages;
            if (canDrop <= 0) return;

            // First pass: drop image messages from the front (biggest savings)
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

            // Second pass: drop any messages from the front
            while (canDrop > 0 && totalChars > maxContextChars &&
                   conversationHistory.Count > minKeepMessages)
            {
                totalChars -= messageCharCounts[0];
                conversationHistory.RemoveAt(0);
                messageCharCounts.RemoveAt(0);
                canDrop--;
            }

            if (totalChars > maxContextChars)
            {
                Debug.LogWarning($"[UnityAgent] History still {totalChars} chars after trimming " +
                    $"(budget {maxContextChars}). Consider increasing maxContextChars or reducing minKeepMessages.");
            }
        }

        // =================================================================
        // HTTP with retry
        // =================================================================

        private IEnumerator CallAPIWithRetry(string requestBody, Action<string, string> callback)
        {
            int attempt = 0;

            while (true)
            {
                string responseBody = null;
                string error = null;
                long httpCode = 0;

                yield return CallAPI(requestBody, (res, err, code) =>
                {
                    responseBody = res;
                    error = err;
                    httpCode = code;
                });

                // Success
                if (error == null)
                {
                    callback?.Invoke(responseBody, null);
                    yield break;
                }

                // Retryable errors: 429 (rate limit), 500+ (server error)
                attempt++;
                bool retryable = httpCode == 429 || httpCode >= 500;

                if (!retryable || attempt >= maxRetries)
                {
                    callback?.Invoke(null, error);
                    yield break;
                }

                float delay = Mathf.Pow(2f, attempt); // 2s, 4s, 8s
                Debug.LogWarning($"[UnityAgent] HTTP {httpCode}, retrying in {delay}s (attempt {attempt}/{maxRetries})...");
                yield return new WaitForSeconds(delay);
            }
        }

        private IEnumerator CallAPI(string requestBody, Action<string, string, long> callback)
        {
            string url = baseURL + "/chat/completions";

            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            request.timeout = 120;

            yield return request.SendWebRequest();

            long code = request.responseCode;
            if (request.result == UnityWebRequest.Result.Success)
            {
                callback?.Invoke(request.downloadHandler.text, null, code);
            }
            else
            {
                string detail = request.downloadHandler?.text ?? request.error;
                callback?.Invoke(null, $"HTTP {code}: {detail}", code);
            }

            request.Dispose();
        }

        // =================================================================
        // JSON building
        // =================================================================

        private string BuildRequestBody(bool disableTools)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            sb.Append("\"messages\":[");

            // System prompt
            sb.Append($"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}}");

            // On last step, inject a directive to wrap up
            if (disableTools)
            {
                sb.Append(",{\"role\":\"system\",\"content\":\"");
                sb.Append(EscapeJson("[SYSTEM] You have reached the maximum number of tool-call steps. " +
                    "Do NOT call any more tools. Summarize your progress and current state in text."));
                sb.Append("\"}");
            }

            // Conversation history
            foreach (var msg in conversationHistory)
            {
                sb.Append(",");
                sb.Append(msg);
            }

            sb.Append("]");

            // Tools (omit entirely on last step to force text-only response)
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
            {
                return $"{{\"role\":\"user\",\"content\":\"{EscapeJson(text)}\"}}";
            }

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
        // Response parsing
        // =================================================================

        private struct ToolCallInfo
        {
            public string id;
            public string name;
            public string arguments;
        }

        private bool ParseResponse(string json, out string content, out string finishReason,
            out List<ToolCallInfo> toolCalls, out string assistantMessageJson)
        {
            content = null;
            finishReason = null;
            toolCalls = null;
            assistantMessageJson = null;

            int choicesIdx = json.IndexOf("\"choices\"", StringComparison.Ordinal);
            if (choicesIdx < 0) return false;

            finishReason = ExtractStringField(json, "finish_reason");

            int msgIdx = json.IndexOf("\"message\"", choicesIdx, StringComparison.Ordinal);
            if (msgIdx < 0) return false;

            int msgObjStart = json.IndexOf('{', msgIdx);
            if (msgObjStart < 0) return false;
            int msgObjEnd = FindMatchingBrace(json, msgObjStart);
            if (msgObjEnd < 0) return false;

            string messageObj = json.Substring(msgObjStart, msgObjEnd - msgObjStart + 1);
            assistantMessageJson = messageObj;

            content = ExtractStringField(messageObj, "content");

            int tcIdx = messageObj.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
            if (tcIdx >= 0)
            {
                int arrStart = messageObj.IndexOf('[', tcIdx);
                if (arrStart >= 0)
                {
                    int arrEnd = FindMatchingBracket(messageObj, arrStart);
                    if (arrEnd >= 0)
                    {
                        toolCalls = ParseToolCalls(
                            messageObj.Substring(arrStart, arrEnd - arrStart + 1));
                    }
                }
            }

            return true;
        }

        private static List<ToolCallInfo> ParseToolCalls(string arrayJson)
        {
            var result = new List<ToolCallInfo>();
            int pos = 0;

            while (pos < arrayJson.Length)
            {
                int objStart = arrayJson.IndexOf('{', pos);
                if (objStart < 0) break;

                int objEnd = FindMatchingBrace(arrayJson, objStart);
                if (objEnd < 0) break;

                string objStr = arrayJson.Substring(objStart, objEnd - objStart + 1);

                var tc = new ToolCallInfo { id = ExtractStringField(objStr, "id") ?? "" };

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
                            tc.name = ExtractStringField(fnStr, "name") ?? "";
                            tc.arguments = ExtractStringField(fnStr, "arguments") ?? "{}";
                        }
                    }
                }

                result.Add(tc);
                pos = objEnd + 1;
            }

            return result;
        }

        // =================================================================
        // JSON utility helpers — zero external dependencies
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
                else if (json[i] == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(json[i]);
                    i++;
                }
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
            {
                sb.Append(json[i]);
                i++;
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        internal static int FindMatchingBrace(string json, int openIdx)
        {
            return FindMatching(json, openIdx, '{', '}');
        }

        internal static int FindMatchingBracket(string json, int openIdx)
        {
            return FindMatching(json, openIdx, '[', ']');
        }

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
