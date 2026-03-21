using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LLMAgent.Tools
{
    /// <summary>
    /// Console log reading tool (runtime-compatible).
    /// Hooks into Application.logMessageReceived and buffers recent entries.
    /// Tools: readConsole
    /// </summary>
    public class AgentConsoleTools : MonoBehaviour
    {
        private static readonly List<LogEntry> logBuffer = new List<LogEntry>();
        private const int MaxLogEntries = 200;

        private struct LogEntry
        {
            public string message;
            public string stackTrace;
            public LogType type;
            public string timestamp;
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLogMessage;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessage;
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            logBuffer.Add(new LogEntry
            {
                message = message,
                stackTrace = stackTrace,
                type = type,
                timestamp = DateTime.Now.ToString("HH:mm:ss")
            });
            if (logBuffer.Count > MaxLogEntries)
                logBuffer.RemoveAt(0);
        }

        // =================================================================
        // Tool: readConsole
        // =================================================================

        [AgentTool("readConsole",
            "Read recent Unity console log messages. Can filter by log type " +
            "(Log, Warning, Error). Returns the most recent entries.",
            ParametersType = typeof(ReadConsoleParams))]
        private IEnumerator HandleReadConsole(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string filterType = UnityAgent.ExtractStringField(arguments, "filter");
            int count = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "count"), 50);
            count = Mathf.Clamp(count, 1, 200);

            var sb = new StringBuilder();
            sb.Append("{\"entries\":[");

            int written = 0;
            for (int i = logBuffer.Count - 1; i >= 0 && written < count; i--)
            {
                var entry = logBuffer[i];

                if (!string.IsNullOrEmpty(filterType))
                {
                    if (!entry.type.ToString().Equals(filterType, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (written > 0) sb.Append(",");
                sb.Append("{\"time\":\"").Append(entry.timestamp).Append("\"");
                sb.Append(",\"type\":\"").Append(entry.type.ToString()).Append("\"");
                sb.Append(",\"message\":\"").Append(UnityAgent.EscapeJson(
                    AgentToolHelpers.Truncate(entry.message, 500))).Append("\"");
                if (entry.type == LogType.Error || entry.type == LogType.Exception)
                    sb.Append(",\"stack\":\"").Append(UnityAgent.EscapeJson(
                        AgentToolHelpers.Truncate(entry.stackTrace, 300))).Append("\"");
                sb.Append("}");
                written++;
            }

            sb.Append("],\"count\":").Append(written);
            sb.Append(",\"totalBuffered\":").Append(logBuffer.Count).Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class ReadConsoleParams
        {
            [ToolParam("Filter by type: 'Log', 'Warning', 'Error'. Omit for all.")]
            public string filter;
            [ToolParam("Number of recent entries to return (default 50, max 200).", SchemaType = "integer")]
            public int count;
        }

        // =================================================================
        // Tool: clearConsole
        // =================================================================

        [AgentTool("clearConsole",
            "Clear the buffered console log entries.",
            ParametersType = typeof(ClearConsoleParams))]
        private IEnumerator HandleClearConsole(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            int before = logBuffer.Count;
            logBuffer.Clear();
            callback(AgentToolHelpers.Ok($"Cleared {before} log entries."));
            yield break;
        }

        public class ClearConsoleParams
        {
            // No parameters
        }
    }
}
