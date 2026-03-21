using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace LLMAgent.Tools
{
    /// <summary>
    /// Reflection-based method invocation tools (runtime-compatible).
    /// Tools: callMethod, findMethods
    /// </summary>
    public class AgentReflectionTools : MonoBehaviour
    {
        // =================================================================
        // Tool: callMethod
        // =================================================================

        [AgentTool("callMethod",
            "Call a method on a component using C# reflection. Supports public and private methods. " +
            "Pass arguments as a JSON array of strings (they will be auto-converted to match parameter types). " +
            "Returns the method's return value.",
            ParametersType = typeof(CallMethodParams))]
        private IEnumerator HandleCallMethod(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string compType = UnityAgent.ExtractStringField(arguments, "componentType");
            string methodName = UnityAgent.ExtractStringField(arguments, "method");

            if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(compType) || string.IsNullOrEmpty(methodName))
            {
                callback(AgentToolHelpers.Fail("'target', 'componentType', and 'method' are required."));
                yield break;
            }

            var go = GameObject.Find(goName);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{goName}' not found."));
                yield break;
            }

            Component comp = AgentToolHelpers.FindComponentByTypeName(go, compType);
            if (comp == null)
            {
                callback(AgentToolHelpers.Fail($"Component '{compType}' not found on '{goName}'."));
                yield break;
            }

            var type = comp.GetType();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var method = type.GetMethod(methodName, flags);

            if (method == null)
            {
                callback(AgentToolHelpers.Fail($"Method '{methodName}' not found on {compType}."));
                yield break;
            }

            // Parse args array from JSON
            var parameters = method.GetParameters();
            object[] invokeArgs = new object[parameters.Length];

            // Extract args array
            int argsIdx = arguments.IndexOf("\"args\"", StringComparison.Ordinal);
            if (argsIdx >= 0)
            {
                int bracketStart = arguments.IndexOf('[', argsIdx);
                if (bracketStart >= 0)
                {
                    int bracketEnd = UnityAgent.FindMatchingBracket(arguments, bracketStart);
                    if (bracketEnd >= 0)
                    {
                        string argsStr = arguments.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                        var argValues = ParseStringArray(argsStr);
                        for (int i = 0; i < parameters.Length && i < argValues.Count; i++)
                        {
                            invokeArgs[i] = AgentToolHelpers.ConvertValue(
                                argValues[i], parameters[i].ParameterType);
                        }
                    }
                }
            }

            try
            {
                object result = method.Invoke(comp, invokeArgs);
                string resultStr = result?.ToString() ?? "null";
                callback(new UnityAgent.ToolResult
                {
                    content = $"{{\"success\":true,\"method\":\"{UnityAgent.EscapeJson(methodName)}\",\"returnType\":\"{UnityAgent.EscapeJson(method.ReturnType.Name)}\",\"result\":\"{UnityAgent.EscapeJson(AgentToolHelpers.Truncate(resultStr, 2000))}\"}}"
                });
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                callback(AgentToolHelpers.Fail($"Method invocation failed: {inner.Message}"));
            }
            yield break;
        }

        public class CallMethodParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name.", required: true)]
            public string componentType;
            [ToolParam("Method name to call.", required: true)]
            public string method;
            [ToolParam("Arguments as JSON array of strings, e.g. [\"hello\", \"42\"].")]
            public string args;
        }

        // =================================================================
        // Tool: findMethods
        // =================================================================

        [AgentTool("findMethods",
            "List methods on a component using C# reflection. Filter by name pattern. " +
            "Shows method name, return type, and parameter info.",
            ParametersType = typeof(FindMethodsParams))]
        private IEnumerator HandleFindMethods(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string compType = UnityAgent.ExtractStringField(arguments, "componentType");
            string filter = UnityAgent.ExtractStringField(arguments, "filter");
            bool includePrivate = AgentToolHelpers.ParseBool(
                UnityAgent.ExtractStringField(arguments, "includePrivate"));

            if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(compType))
            {
                callback(AgentToolHelpers.Fail("'target' and 'componentType' are required."));
                yield break;
            }

            var go = GameObject.Find(goName);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{goName}' not found."));
                yield break;
            }

            Component comp = AgentToolHelpers.FindComponentByTypeName(go, compType);
            if (comp == null)
            {
                callback(AgentToolHelpers.Fail($"Component '{compType}' not found on '{goName}'."));
                yield break;
            }

            var type = comp.GetType();
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includePrivate) flags |= BindingFlags.NonPublic;

            var methods = type.GetMethods(flags);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"component\":\"").Append(UnityAgent.EscapeJson(compType)).Append("\",");
            sb.Append("\"methods\":[");

            bool first = true;
            int count = 0;
            foreach (var m in methods)
            {
                // Skip property accessors and object base methods
                if (m.IsSpecialName) continue;
                if (m.DeclaringType == typeof(object) || m.DeclaringType == typeof(Component) ||
                    m.DeclaringType == typeof(UnityEngine.Object) || m.DeclaringType == typeof(MonoBehaviour))
                    continue;

                if (!string.IsNullOrEmpty(filter) &&
                    m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (!first) sb.Append(",");
                first = false;

                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(m.Name)).Append("\"");
                sb.Append(",\"returnType\":\"").Append(UnityAgent.EscapeJson(m.ReturnType.Name)).Append("\"");
                sb.Append(",\"isPublic\":").Append(m.IsPublic ? "true" : "false");

                var parms = m.GetParameters();
                sb.Append(",\"parameters\":[");
                for (int i = 0; i < parms.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(parms[i].Name)).Append("\"");
                    sb.Append(",\"type\":\"").Append(UnityAgent.EscapeJson(parms[i].ParameterType.Name)).Append("\"}");
                }
                sb.Append("]}");

                count++;
                if (count >= 100) break;
            }

            sb.Append("],\"count\":").Append(count).Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class FindMethodsParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name.", required: true)]
            public string componentType;
            [ToolParam("Filter methods by name (case-insensitive partial match).")]
            public string filter;
            [ToolParam("Set to 'true' to include private methods.")]
            public string includePrivate;
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static List<string> ParseStringArray(string innerJson)
        {
            var result = new List<string>();
            bool inString = false;
            var current = new StringBuilder();

            for (int i = 0; i < innerJson.Length; i++)
            {
                char c = innerJson[i];
                if (inString)
                {
                    if (c == '\\' && i + 1 < innerJson.Length)
                    {
                        current.Append(innerJson[i + 1]);
                        i++;
                    }
                    else if (c == '"')
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        inString = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inString = true;
                }
            }

            return result;
        }
    }
}
