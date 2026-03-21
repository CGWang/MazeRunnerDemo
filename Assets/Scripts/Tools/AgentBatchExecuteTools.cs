using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Meta-tool that executes multiple tool operations in sequence within a single call.
    /// Each command specifies a tool name and its arguments; results are collected and
    /// returned as an array. Supports fail-fast mode to stop on the first error.
    /// </summary>
    public class AgentBatchExecuteTools : MonoBehaviour
    {
        /// <summary>
        /// Reference to the UnityAgent instance. Auto-resolved via singleton on first use.
        /// Can also be set explicitly via Inspector or script.
        /// </summary>
        public UnityAgent agent;

        private UnityAgent GetAgent()
        {
            if (agent == null) agent = UnityAgent.Instance;
            return agent;
        }

        private const int DefaultMaxCommands = 25;
        private const int AbsoluteMaxCommands = 100;

        // =================================================================
        // Tool: batchExecute
        // =================================================================

        [AgentTool("batchExecute",
            "Execute multiple tool operations in sequence. " +
            "Pass a JSON array of commands, each with 'tool' (tool name) and 'arguments' (JSON string of args). " +
            "Set 'failFast' to true (default) to stop on first error.",
            ParametersType = typeof(BatchExecuteParams))]
        private IEnumerator HandleBatchExecute(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var agentRef = GetAgent();
            if (agentRef == null)
            {
                callback(AgentToolHelpers.Fail("BatchExecute: UnityAgent instance not found."));
                yield break;
            }

            // Parse failFast (default true)
            string failFastStr = UnityAgent.ExtractStringField(arguments, "failFast");
            bool failFast = failFastStr == null || !failFastStr.Equals("false", StringComparison.OrdinalIgnoreCase);

            // Extract commands array
            string commandsKey = "\"commands\"";
            int keyIdx = arguments.IndexOf(commandsKey, StringComparison.Ordinal);
            if (keyIdx < 0)
            {
                callback(AgentToolHelpers.Fail("'commands' array is required."));
                yield break;
            }

            int bracketStart = arguments.IndexOf('[', keyIdx);
            if (bracketStart < 0)
            {
                callback(AgentToolHelpers.Fail("'commands' must be a JSON array."));
                yield break;
            }
            int bracketEnd = UnityAgent.FindMatchingBracket(arguments, bracketStart);
            if (bracketEnd < 0)
            {
                callback(AgentToolHelpers.Fail("Malformed 'commands' array."));
                yield break;
            }

            string commandsArrayStr = arguments.Substring(bracketStart, bracketEnd - bracketStart + 1);

            // Parse individual command objects from the array
            var commands = ParseCommandArray(commandsArrayStr);

            if (commands.Count == 0)
            {
                callback(AgentToolHelpers.Fail("'commands' array is empty."));
                yield break;
            }

            if (commands.Count > AbsoluteMaxCommands)
            {
                callback(AgentToolHelpers.Fail(
                    $"Too many commands ({commands.Count}). Maximum is {AbsoluteMaxCommands}."));
                yield break;
            }

            // Execute commands sequentially
            var results = new StringBuilder();
            results.Append("[");
            int successCount = 0;
            int failureCount = 0;
            bool anyFailed = false;

            for (int i = 0; i < commands.Count; i++)
            {
                var cmd = commands[i];
                if (i > 0) results.Append(",");

                if (string.IsNullOrEmpty(cmd.tool))
                {
                    failureCount++;
                    anyFailed = true;
                    results.Append("{\"tool\":null,\"success\":false,\"error\":\"Command must include a 'tool' field.\"}");
                    if (failFast) break;
                    continue;
                }

                // Look up the tool handler
                var handler = agentRef.FindToolHandler(cmd.tool);
                if (handler == null)
                {
                    failureCount++;
                    anyFailed = true;
                    results.Append("{\"tool\":\"").Append(UnityAgent.EscapeJson(cmd.tool)).Append("\"");
                    results.Append(",\"success\":false");
                    results.Append(",\"error\":\"Unknown tool: ").Append(UnityAgent.EscapeJson(cmd.tool)).Append("\"}");
                    if (failFast) break;
                    continue;
                }

                // Invoke the handler
                UnityAgent.ToolResult toolResult = default;
                bool done = false;
                Exception invokeError = null;

                IEnumerator routine = null;
                try
                {
                    routine = handler(cmd.arguments ?? "{}", r => { toolResult = r; done = true; });
                }
                catch (Exception ex)
                {
                    invokeError = ex;
                }

                if (invokeError != null)
                {
                    failureCount++;
                    anyFailed = true;
                    results.Append("{\"tool\":\"").Append(UnityAgent.EscapeJson(cmd.tool)).Append("\"");
                    results.Append(",\"success\":false");
                    results.Append(",\"error\":\"").Append(UnityAgent.EscapeJson(invokeError.Message)).Append("\"}");
                    if (failFast) break;
                    continue;
                }

                // Run the coroutine to completion
                if (routine != null)
                {
                    while (!done)
                    {
                        try
                        {
                            if (!routine.MoveNext()) break;
                        }
                        catch (Exception ex)
                        {
                            invokeError = ex;
                            break;
                        }
                        yield return routine.Current;
                    }
                }

                if (invokeError != null)
                {
                    failureCount++;
                    anyFailed = true;
                    results.Append("{\"tool\":\"").Append(UnityAgent.EscapeJson(cmd.tool)).Append("\"");
                    results.Append(",\"success\":false");
                    results.Append(",\"error\":\"").Append(UnityAgent.EscapeJson(invokeError.Message)).Append("\"}");
                    if (failFast) break;
                    continue;
                }

                // Determine success from the tool result
                string content = toolResult.content ?? "";
                bool callSucceeded = !content.Contains("\"success\":false");

                if (callSucceeded)
                    successCount++;
                else
                {
                    failureCount++;
                    anyFailed = true;
                }

                results.Append("{\"tool\":\"").Append(UnityAgent.EscapeJson(cmd.tool)).Append("\"");
                results.Append(",\"success\":").Append(callSucceeded ? "true" : "false");
                results.Append(",\"result\":").Append(string.IsNullOrEmpty(content) ? "null" : content);
                results.Append("}");

                if (!callSucceeded && failFast) break;
            }

            results.Append("]");

            // Build final response
            var sb = new StringBuilder();
            if (anyFailed)
            {
                sb.Append("{\"success\":false,\"message\":\"One or more commands failed\"");
            }
            else
            {
                sb.Append("{\"success\":true,\"message\":\"Batch execution completed\"");
            }
            sb.Append(",\"successCount\":").Append(successCount);
            sb.Append(",\"failureCount\":").Append(failureCount);
            sb.Append(",\"totalCommands\":").Append(commands.Count);
            sb.Append(",\"results\":").Append(results.ToString());
            sb.Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        // =================================================================
        // Parameters
        // =================================================================

        public class BatchExecuteParams
        {
            [ToolParam("JSON array of command objects, each with 'tool' and 'arguments'.", required: true, SchemaType = "array")]
            public string commands;

            [ToolParam("Stop execution on first error (default: true).")]
            public string failFast;
        }

        // =================================================================
        // Internal types and helpers
        // =================================================================

        private struct CommandEntry
        {
            public string tool;
            public string arguments;
        }

        /// <summary>
        /// Parse the commands JSON array into a list of CommandEntry structs.
        /// Each element should be a JSON object with "tool" and "arguments" fields.
        /// </summary>
        private static List<CommandEntry> ParseCommandArray(string arrayStr)
        {
            var commands = new List<CommandEntry>();

            int pos = 0;
            while (pos < arrayStr.Length)
            {
                int objStart = arrayStr.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = UnityAgent.FindMatchingBrace(arrayStr, objStart);
                if (objEnd < 0) break;

                string objStr = arrayStr.Substring(objStart, objEnd - objStart + 1);

                string tool = UnityAgent.ExtractStringField(objStr, "tool");

                // The "arguments" field can be either a string or an embedded object.
                // Try extracting as a nested JSON object first.
                string argsContent = ExtractObjectOrStringField(objStr, "arguments");

                commands.Add(new CommandEntry { tool = tool, arguments = argsContent ?? "{}" });
                pos = objEnd + 1;
            }

            return commands;
        }

        /// <summary>
        /// Extract a field that could be either a JSON string or a nested JSON object.
        /// Returns the raw JSON string of the object, or the string value if it's a string.
        /// </summary>
        private static string ExtractObjectOrStringField(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
            if (colonIdx < 0) return null;

            int valStart = colonIdx + 1;
            while (valStart < json.Length && char.IsWhiteSpace(json[valStart])) valStart++;
            if (valStart >= json.Length) return null;

            // If it starts with '{', extract the entire object
            if (json[valStart] == '{')
            {
                int braceEnd = UnityAgent.FindMatchingBrace(json, valStart);
                if (braceEnd >= 0)
                    return json.Substring(valStart, braceEnd - valStart + 1);
            }

            // Otherwise try as a string value
            return UnityAgent.ExtractStringField(json, key);
        }
    }
}
