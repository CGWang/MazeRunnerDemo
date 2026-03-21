using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace LLMAgent
{
    /// <summary>
    /// Marks a method as an agent tool, enabling automatic discovery and registration.
    /// The method must match the ToolHandler signature:
    ///   IEnumerator MyHandler(string arguments, Action&lt;UnityAgent.ToolResult&gt; callback)
    ///
    /// Usage:
    ///   [AgentTool("toolName", "Tool description.")]
    ///   private IEnumerator HandleMyTool(string arguments, Action&lt;ToolResult&gt; callback) { ... }
    ///
    /// For parameter schemas, either set ParametersJson directly or use a nested Parameters class
    /// with [ToolParam] attributes — the schema is auto-generated via reflection.
    ///
    ///   [AgentTool("myTool", "Does something.", ParametersType = typeof(MyToolParams))]
    ///   ...
    ///   public class MyToolParams {
    ///       [ToolParam("Name of the target.", required: true)]
    ///       public string name;
    ///       [ToolParam("Speed multiplier.")]
    ///       public float speed;
    ///   }
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class AgentToolAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }

        /// <summary>Raw JSON schema for parameters. Takes precedence over ParametersType.</summary>
        public string ParametersJson { get; set; }

        /// <summary>Type whose public fields are used to auto-generate a JSON parameter schema.</summary>
        public Type ParametersType { get; set; }

        /// <summary>If true, the agent will ask the user for permission before executing.</summary>
        public bool RequiresPermission { get; set; }

        public AgentToolAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// Marks a public field on a parameters class for JSON schema generation.
    /// Fields without this attribute are still included but with no description.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ToolParamAttribute : Attribute
    {
        public string Description { get; }
        public bool Required { get; set; }

        /// <summary>Override the inferred JSON schema type (e.g. "integer", "array").</summary>
        public string SchemaType { get; set; }

        public ToolParamAttribute(string description = "", bool required = false)
        {
            Description = description;
            Required = required;
        }
    }

    /// <summary>
    /// Scans objects for [AgentTool] methods and registers them with a UnityAgent instance.
    /// </summary>
    public static class AgentToolDiscovery
    {
        /// <summary>
        /// Scan the given provider for methods decorated with [AgentTool] and register
        /// each one as a tool on the agent. Works with any object (MonoBehaviour, POCO, etc.).
        /// </summary>
        public static int RegisterToolsFrom(UnityAgent agent, object provider)
        {
            if (provider == null) return 0;

            int count = 0;
            var type = provider.GetType();
            var methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<AgentToolAttribute>();
                if (attr == null) continue;

                if (!ValidateHandlerSignature(method, type))
                    continue;

                // Build parameter schema
                string paramsJson = attr.ParametersJson;
                if (string.IsNullOrEmpty(paramsJson) && attr.ParametersType != null)
                    paramsJson = GenerateParameterSchema(attr.ParametersType);

                // Create delegate wrapper
                var capturedMethod = method;
                var capturedProvider = method.IsStatic ? null : provider;
                UnityAgent.ToolHandler handler = (arguments, callback) =>
                    (IEnumerator)capturedMethod.Invoke(capturedProvider, new object[] { arguments, callback });

                agent.RegisterTool(attr.Name, attr.Description, paramsJson, handler, attr.RequiresPermission);
                count++;
            }

            return count;
        }

        private static bool ValidateHandlerSignature(MethodInfo method, Type ownerType)
        {
            if (method.ReturnType != typeof(IEnumerator))
            {
                UnityEngine.Debug.LogWarning(
                    $"[AgentToolDiscovery] {ownerType.Name}.{method.Name}: " +
                    $"return type must be IEnumerator, got {method.ReturnType.Name}. Skipping.");
                return false;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2 ||
                parameters[0].ParameterType != typeof(string) ||
                parameters[1].ParameterType != typeof(Action<UnityAgent.ToolResult>))
            {
                UnityEngine.Debug.LogWarning(
                    $"[AgentToolDiscovery] {ownerType.Name}.{method.Name}: " +
                    "signature must be (string, Action<ToolResult>). Skipping.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Generate an OpenAI-compatible JSON schema from a type's public fields/properties
        /// decorated with [ToolParam].
        /// </summary>
        public static string GenerateParameterSchema(Type paramsType)
        {
            var fields = paramsType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var props = paramsType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            var sb = new StringBuilder();
            var requiredList = new List<string>();

            sb.Append("{\"type\":\"object\",\"properties\":{");

            bool first = true;

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<ToolParamAttribute>();
                AppendMember(sb, field.Name, field.FieldType, attr, ref first);
                if (attr != null && attr.Required) requiredList.Add(field.Name);
            }

            foreach (var prop in props)
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                var attr = prop.GetCustomAttribute<ToolParamAttribute>();
                AppendMember(sb, prop.Name, prop.PropertyType, attr, ref first);
                if (attr != null && attr.Required) requiredList.Add(prop.Name);
            }

            sb.Append("},\"required\":[");
            for (int i = 0; i < requiredList.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{UnityAgent.EscapeJson(requiredList[i])}\"");
            }
            sb.Append("]}");

            return sb.ToString();
        }

        private static void AppendMember(StringBuilder sb, string name, Type memberType,
            ToolParamAttribute attr, ref bool first)
        {
            if (!first) sb.Append(",");
            first = false;

            string jsonType = attr?.SchemaType ?? InferJsonType(memberType);
            sb.Append($"\"{UnityAgent.EscapeJson(name)}\":{{\"type\":\"{jsonType}\"");

            if (attr != null && !string.IsNullOrEmpty(attr.Description))
                sb.Append($",\"description\":\"{UnityAgent.EscapeJson(attr.Description)}\"");

            sb.Append("}");
        }

        private static string InferJsonType(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte))
                return "integer";
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
                return "number";
            if (t == typeof(bool)) return "boolean";
            if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)))
                return "array";
            return "object";
        }
    }
}
