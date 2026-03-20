using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LLMAgent
{
    /// <summary>
    /// Pure C# LLM agent that calls OpenAI-compatible Chat Completions API,
    /// handles tool calling loops, and dispatches to MazePlayerBridge / screenshot capture.
    /// No PuerTS, no V8, no TypeScript — just C# and UnityWebRequest.
    /// </summary>
    public class UnityAgent : MonoBehaviour
    {
        // --- Configuration ---
        private string apiKey;
        private string baseURL = "https://api.openai.com/v1";
        private string model = "gpt-4o";
        private int maxSteps = 25;
        private string systemPrompt;

        // --- Conversation state ---
        private readonly List<string> conversationHistory = new List<string>();
        private bool isRunning;
        private bool abortRequested;

        // --- Singleton runner (hidden, like MazePlayerRunner) ---
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

        // =====================================================================
        // Public API
        // =====================================================================

        public void Configure(string apiKey, string baseURL, string model, int maxSteps)
        {
            this.apiKey = apiKey;
            if (!string.IsNullOrEmpty(baseURL)) this.baseURL = baseURL.TrimEnd('/');
            if (!string.IsNullOrEmpty(model)) this.model = model;
            if (maxSteps > 0) this.maxSteps = maxSteps;
        }

        public void Initialize(string resourceRoot, Action onReady)
        {
            var promptAsset = Resources.Load<TextAsset>(resourceRoot + "/system-prompt.md");
            systemPrompt = promptAsset != null ? promptAsset.text : "";
            Debug.Log($"[UnityAgent] System prompt loaded ({systemPrompt.Length} chars).");
            onReady?.Invoke();
        }

        public bool IsConfigured => !string.IsNullOrEmpty(apiKey);

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

        public void ClearHistory() => conversationHistory.Clear();

        public void Dispose()
        {
            AbortGeneration();
            conversationHistory.Clear();
        }

        // =====================================================================
        // Agent loop
        // =====================================================================

        private IEnumerator RunAgentLoop(string userMessage, string imageBase64,
            Action<string, bool> callback, Action<string> progressCallback)
        {
            isRunning = true;
            abortRequested = false;

            // Build user message
            conversationHistory.Add(BuildUserMessage(userMessage, imageBase64));

            int steps = 0;
            string finalResponse = "";

            while (steps < maxSteps && !abortRequested)
            {
                // Build request JSON
                string requestBody = BuildRequestBody();

                // Call API
                string responseBody = null;
                string error = null;
                yield return CallAPI(requestBody, (res, err) =>
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

                // Parse response — extract the choice
                string assistantContent;
                string finishReason;
                List<ToolCall> toolCalls;
                string assistantMessageJson;

                if (!ParseResponse(responseBody, out assistantContent, out finishReason,
                        out toolCalls, out assistantMessageJson))
                {
                    isRunning = false;
                    callback?.Invoke($"Failed to parse API response: {responseBody}", true);
                    yield break;
                }

                // Add assistant message to history
                conversationHistory.Add(assistantMessageJson);

                // If there are tool calls, execute them
                if (toolCalls != null && toolCalls.Count > 0)
                {
                    foreach (var tc in toolCalls)
                    {
                        if (abortRequested) break;

                        steps++;
                        progressCallback?.Invoke($"[{steps}/{maxSteps}] {tc.name}");
                        Debug.Log($"[UnityAgent] Tool call #{steps}: {tc.name}({Truncate(tc.arguments, 120)})");

                        // Execute the tool
                        string toolResult = null;
                        string screenshotBase64 = null;
                        bool toolDone = false;

                        yield return ExecuteTool(tc.name, tc.arguments, (result, imgB64) =>
                        {
                            toolResult = result;
                            screenshotBase64 = imgB64;
                            toolDone = true;
                        });

                        // Add tool result message
                        conversationHistory.Add(BuildToolResultMessage(tc.id, toolResult));

                        // If there's an image from screenshot, inject as a user message
                        // (most APIs don't support images in tool results)
                        if (!string.IsNullOrEmpty(screenshotBase64))
                        {
                            conversationHistory.Add(BuildImageMessage(screenshotBase64));
                        }
                    }
                    continue; // Loop back to call API with tool results
                }
                else
                {
                    // No tool calls — final text response
                    finalResponse = assistantContent ?? "";
                    break;
                }
            }

            isRunning = false;

            if (abortRequested)
                callback?.Invoke("Generation aborted.", true);
            else if (steps >= maxSteps)
                callback?.Invoke(finalResponse + "\n[Reached max steps]", false);
            else
                callback?.Invoke(finalResponse, false);
        }

        // =====================================================================
        // Tool execution — dispatch to C# methods directly
        // =====================================================================

        private IEnumerator ExecuteTool(string toolName, string arguments,
            Action<string, string> callback)
        {
            switch (toolName)
            {
                case "getPlayerStatus":
                    yield return ExecuteGetPlayerStatus(callback);
                    break;

                case "movePath":
                    yield return ExecuteMovePath(arguments, callback);
                    break;

                case "captureScreenshot":
                    yield return ExecuteCaptureScreenshot(callback);
                    break;

                default:
                    callback?.Invoke($"{{\"error\": \"Unknown tool: {EscapeJson(toolName)}\"}}", null);
                    break;
            }
        }

        private IEnumerator ExecuteGetPlayerStatus(Action<string, string> callback)
        {
            string result = null;
            bool done = false;
            MazePlayerBridge.GetPlayerStatus(r => { result = r; done = true; });

            // GetPlayerStatus is synchronous (no coroutine), but callback pattern
            // If not done immediately, wait
            while (!done) yield return null;

            callback?.Invoke(result, null);
        }

        private IEnumerator ExecuteMovePath(string arguments, Action<string, string> callback)
        {
            // Parse arguments: {"segments": [{"dir":"north","steps":3}, ...]}
            // Extract directions and distances arrays for MoveSequenceV2
            string directionsJson;
            string distancesJson;

            if (!ParseMovePathArgs(arguments, out directionsJson, out distancesJson))
            {
                callback?.Invoke("{\"success\":false,\"error\":\"Failed to parse movePath arguments. " +
                    "Expected: {\\\"segments\\\": [{\\\"dir\\\":\\\"north\\\",\\\"steps\\\":3}]}\"}", null);
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

            callback?.Invoke(result, null);
        }

        private IEnumerator ExecuteCaptureScreenshot(Action<string, string> callback)
        {
            yield return new WaitForEndOfFrame();

            var cam = Camera.main;
            if (cam == null)
            {
                callback?.Invoke("{\"success\":false,\"error\":\"No main camera found.\"}", null);
                yield break;
            }

            int width = 512;
            int height = 512;
            var rt = new RenderTexture(width, height, 24);
            var prevTarget = cam.targetTexture;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = prevTarget;
            RenderTexture.active = null;
            Destroy(rt);

            byte[] png = tex.EncodeToPNG();
            Destroy(tex);

            string base64 = Convert.ToBase64String(png);

            string resultJson = $"{{\"success\":true,\"message\":\"Screenshot captured ({width}x{height}).\"}}";
            callback?.Invoke(resultJson, base64);
        }

        // =====================================================================
        // HTTP — UnityWebRequest to OpenAI-compatible API
        // =====================================================================

        private IEnumerator CallAPI(string requestBody, Action<string, string> callback)
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

            if (request.result == UnityWebRequest.Result.Success)
            {
                callback?.Invoke(request.downloadHandler.text, null);
            }
            else
            {
                string errorDetail = request.downloadHandler?.text ?? request.error;
                callback?.Invoke(null, $"HTTP {request.responseCode}: {errorDetail}");
            }

            request.Dispose();
        }

        // =====================================================================
        // JSON building — construct OpenAI request body
        // =====================================================================

        private string BuildRequestBody()
        {
            var sb = new StringBuilder(4096);
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            sb.Append("\"messages\":[");

            // System message
            sb.Append($"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}}");

            // Conversation history
            foreach (var msg in conversationHistory)
            {
                sb.Append(",");
                sb.Append(msg); // Already valid JSON
            }

            sb.Append("],");
            sb.Append("\"tools\":");
            sb.Append(ToolDefinitionsJson);
            sb.Append("}");

            return sb.ToString();
        }

        private string BuildUserMessage(string text, string imageBase64)
        {
            if (string.IsNullOrEmpty(imageBase64))
            {
                return $"{{\"role\":\"user\",\"content\":\"{EscapeJson(text)}\"}}";
            }

            // Multimodal message with text + image
            return "{\"role\":\"user\",\"content\":[" +
                   $"{{\"type\":\"text\",\"text\":\"{EscapeJson(text)}\"}}," +
                   "{\"type\":\"image_url\",\"image_url\":{" +
                   $"\"url\":\"data:image/png;base64,{imageBase64}\"" +
                   "}}]}";
        }

        private string BuildImageMessage(string base64)
        {
            return "{\"role\":\"user\",\"content\":[" +
                   "{\"type\":\"text\",\"text\":\"[Screenshot captured]\"}," +
                   "{\"type\":\"image_url\",\"image_url\":{" +
                   $"\"url\":\"data:image/png;base64,{base64}\"" +
                   "}}]}";
        }

        private string BuildToolResultMessage(string toolCallId, string content)
        {
            return $"{{\"role\":\"tool\",\"tool_call_id\":\"{EscapeJson(toolCallId)}\"," +
                   $"\"content\":\"{EscapeJson(content)}\"}}";
        }

        // =====================================================================
        // JSON parsing — extract fields from API response
        // =====================================================================

        private struct ToolCall
        {
            public string id;
            public string name;
            public string arguments;
        }

        private bool ParseResponse(string json, out string content, out string finishReason,
            out List<ToolCall> toolCalls, out string assistantMessageJson)
        {
            content = null;
            finishReason = null;
            toolCalls = null;
            assistantMessageJson = null;

            // Find "choices" array, extract first element
            int choicesIdx = json.IndexOf("\"choices\"", StringComparison.Ordinal);
            if (choicesIdx < 0) return false;

            // Extract finish_reason
            finishReason = ExtractStringField(json, "finish_reason");

            // Extract the "message" object from the first choice
            int msgIdx = json.IndexOf("\"message\"", choicesIdx, StringComparison.Ordinal);
            if (msgIdx < 0) return false;

            // Find the message object boundaries
            int msgObjStart = json.IndexOf('{', msgIdx);
            if (msgObjStart < 0) return false;
            int msgObjEnd = FindMatchingBrace(json, msgObjStart);
            if (msgObjEnd < 0) return false;

            string messageObj = json.Substring(msgObjStart, msgObjEnd - msgObjStart + 1);
            assistantMessageJson = messageObj;

            // Extract content (can be null)
            content = ExtractStringField(messageObj, "content");

            // Extract tool_calls array if present
            int tcIdx = messageObj.IndexOf("\"tool_calls\"", StringComparison.Ordinal);
            if (tcIdx >= 0)
            {
                int arrStart = messageObj.IndexOf('[', tcIdx);
                if (arrStart >= 0)
                {
                    int arrEnd = FindMatchingBracket(messageObj, arrStart);
                    if (arrEnd >= 0)
                    {
                        string tcArrayStr = messageObj.Substring(arrStart, arrEnd - arrStart + 1);
                        toolCalls = ParseToolCalls(tcArrayStr);
                    }
                }
            }

            return true;
        }

        private List<ToolCall> ParseToolCalls(string arrayJson)
        {
            var result = new List<ToolCall>();
            int pos = 0;

            while (pos < arrayJson.Length)
            {
                int objStart = arrayJson.IndexOf('{', pos);
                if (objStart < 0) break;

                int objEnd = FindMatchingBrace(arrayJson, objStart);
                if (objEnd < 0) break;

                string objStr = arrayJson.Substring(objStart, objEnd - objStart + 1);

                var tc = new ToolCall
                {
                    id = ExtractStringField(objStr, "id") ?? ""
                };

                // Extract function.name and function.arguments
                int fnIdx = objStr.IndexOf("\"function\"", StringComparison.Ordinal);
                if (fnIdx >= 0)
                {
                    int fnObjStart = objStr.IndexOf('{', fnIdx);
                    if (fnObjStart >= 0)
                    {
                        int fnObjEnd = FindMatchingBrace(objStr, fnObjStart);
                        if (fnObjEnd >= 0)
                        {
                            string fnStr = objStr.Substring(fnObjStart, fnObjEnd - fnObjStart + 1);
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

        /// <summary>
        /// Parse movePath arguments: {"segments":[{"dir":"north","steps":3},...]}
        /// Convert to directionsJson=["north",...] and distancesJson=[3,...]
        /// </summary>
        private bool ParseMovePathArgs(string arguments, out string directionsJson, out string distancesJson)
        {
            directionsJson = null;
            distancesJson = null;

            var directions = new List<string>();
            var distances = new List<string>();

            // Find "segments" array
            int segIdx = arguments.IndexOf("\"segments\"", StringComparison.Ordinal);
            if (segIdx < 0)
            {
                // Maybe the arguments IS the array directly
                segIdx = arguments.IndexOf('[');
                if (segIdx < 0) return false;
            }

            int arrStart = arguments.IndexOf('[', segIdx);
            if (arrStart < 0) return false;
            int arrEnd = FindMatchingBracket(arguments, arrStart);
            if (arrEnd < 0) return false;

            string arrStr = arguments.Substring(arrStart, arrEnd - arrStart + 1);

            // Parse each segment object
            int pos = 0;
            while (pos < arrStr.Length)
            {
                int objStart = arrStr.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = FindMatchingBrace(arrStr, objStart);
                if (objEnd < 0) break;

                string segStr = arrStr.Substring(objStart, objEnd - objStart + 1);

                string dir = ExtractStringField(segStr, "dir");
                if (string.IsNullOrEmpty(dir))
                    dir = ExtractStringField(segStr, "direction");
                string stepsStr = ExtractNumberField(segStr, "steps");
                if (string.IsNullOrEmpty(stepsStr))
                    stepsStr = ExtractNumberField(segStr, "distance");

                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(stepsStr))
                {
                    directions.Add($"\"{dir}\"");
                    distances.Add(stepsStr);
                }

                pos = objEnd + 1;
            }

            if (directions.Count == 0) return false;

            directionsJson = "[" + string.Join(",", directions) + "]";
            distancesJson = "[" + string.Join(",", distances) + "]";
            return true;
        }

        // =====================================================================
        // Tool definitions JSON
        // =====================================================================

        private const string ToolDefinitionsJson = @"[
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""getPlayerStatus"",
      ""description"": ""Get the player's current position and obstacle distances in all 4 cardinal directions (north/south/east/west), measured in grid cells. Also reports whether the goal has been reached."",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {},
        ""required"": []
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""movePath"",
      ""description"": ""Move the player along a sequence of direction segments. Each segment has a compass direction and a number of grid cells to move. Stops early if blocked by a wall or if the goal is reached. Maximum 20 segments, 1-10 cells per step."",
      ""parameters"": {
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
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""captureScreenshot"",
      ""description"": ""Capture a top-down screenshot of the current game view. Returns the image for visual analysis of the maze layout, walls, corridors, and the red goal marker."",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {},
        ""required"": []
      }
    }
  }
]";

        // =====================================================================
        // JSON utility helpers — no external dependencies
        // =====================================================================

        /// <summary>Extract a string field value: "key":"value"</summary>
        private static string ExtractStringField(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
            if (colonIdx < 0) return null;

            // Skip whitespace after colon
            int valStart = colonIdx + 1;
            while (valStart < json.Length && char.IsWhiteSpace(json[valStart])) valStart++;

            if (valStart >= json.Length) return null;

            // Check for null
            if (json[valStart] == 'n' && valStart + 3 < json.Length &&
                json.Substring(valStart, 4) == "null")
                return null;

            if (json[valStart] != '"') return null;

            // Read until unescaped closing quote
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

        /// <summary>Extract a number field value: "key":123</summary>
        private static string ExtractNumberField(string json, string key)
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

        /// <summary>Find the matching closing brace for an opening brace.</summary>
        private static int FindMatchingBrace(string json, int openIdx)
        {
            return FindMatching(json, openIdx, '{', '}');
        }

        /// <summary>Find the matching closing bracket for an opening bracket.</summary>
        private static int FindMatchingBracket(string json, int openIdx)
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
                if (c == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        private static string EscapeJson(string s)
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
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) + "...";
        }
    }
}
