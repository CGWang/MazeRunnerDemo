using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace LLMAgent.Tools
{
    /// <summary>
    /// Component manipulation tools (runtime-compatible).
    /// Tools: toggleComponent, addComponent, removeComponent, setComponentProperty, getComponentProperty
    /// </summary>
    public class AgentComponentTools : MonoBehaviour
    {
        // =================================================================
        // Tool: toggleComponent
        // =================================================================

        [AgentTool("toggleComponent",
            "Enable or disable a component on a GameObject by type name. " +
            "Works with Behaviours, Renderers, and Colliders.",
            ParametersType = typeof(ToggleParams))]
        private IEnumerator HandleToggle(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string componentType = UnityAgent.ExtractStringField(arguments, "componentType");
            string enabledStr = UnityAgent.ExtractStringField(arguments, "enabled");

            if (string.IsNullOrEmpty(componentType))
            {
                callback(AgentToolHelpers.Fail("componentType is required."));
                yield break;
            }

            GameObject target = AgentToolHelpers.FindTarget(name, null);
            if (target == null)
            {
                callback(AgentToolHelpers.Fail("GameObject not found."));
                yield break;
            }

            bool enabled = enabledStr != "false";
            Component found = AgentToolHelpers.FindComponentByTypeName(target, componentType);
            if (found == null)
            {
                callback(AgentToolHelpers.Fail($"Component '{componentType}' not found on '{target.name}'."));
                yield break;
            }

            if (found is Behaviour b) b.enabled = enabled;
            else if (found is Renderer r) r.enabled = enabled;
            else if (found is Collider c) c.enabled = enabled;
            else
            {
                callback(AgentToolHelpers.Fail($"Component '{componentType}' cannot be toggled."));
                yield break;
            }

            callback(new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"component\":\"{UnityAgent.EscapeJson(componentType)}\",\"enabled\":{(enabled ? "true" : "false")}}}"
            });
            yield break;
        }

        public class ToggleParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string name;
            [ToolParam("Component type name (e.g. 'MeshRenderer', 'BoxCollider').", required: true)]
            public string componentType;
            [ToolParam("Set to 'true' to enable, 'false' to disable.", required: true)]
            public string enabled;
        }

        // =================================================================
        // Tool: addComponent
        // =================================================================

        [AgentTool("addComponent",
            "Add a component to a GameObject by type name. Supports built-in Unity components " +
            "(Rigidbody, BoxCollider, AudioSource, Light, etc.) and custom components.",
            ParametersType = typeof(AddParams))]
        private IEnumerator HandleAdd(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string typeName = UnityAgent.ExtractStringField(arguments, "componentType");

            if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(typeName))
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

            Type compType = AgentToolHelpers.ResolveComponentType(typeName);
            if (compType == null)
            {
                callback(AgentToolHelpers.Fail($"Component type '{typeName}' not found."));
                yield break;
            }

            go.AddComponent(compType);
            callback(AgentToolHelpers.Ok($"Added {typeName} to {goName}."));
            yield break;
        }

        public class AddParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name (e.g. 'Rigidbody', 'BoxCollider', 'AudioSource').", required: true)]
            public string componentType;
        }

        // =================================================================
        // Tool: removeComponent
        // =================================================================

        [AgentTool("removeComponent",
            "Remove a component from a GameObject by type name. Cannot remove Transform.",
            ParametersType = typeof(RemoveParams),
            RequiresPermission = true)]
        private IEnumerator HandleRemove(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string typeName = UnityAgent.ExtractStringField(arguments, "componentType");

            if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(typeName))
            {
                callback(AgentToolHelpers.Fail("'target' and 'componentType' are required."));
                yield break;
            }

            if (typeName == "Transform")
            {
                callback(AgentToolHelpers.Fail("Cannot remove Transform component."));
                yield break;
            }

            var go = GameObject.Find(goName);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{goName}' not found."));
                yield break;
            }

            Component comp = AgentToolHelpers.FindComponentByTypeName(go, typeName);
            if (comp == null)
            {
                callback(AgentToolHelpers.Fail($"Component '{typeName}' not found on '{goName}'."));
                yield break;
            }

            Destroy(comp);
            callback(AgentToolHelpers.Ok($"Removed {typeName} from {goName}."));
            yield break;
        }

        public class RemoveParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name to remove.", required: true)]
            public string componentType;
        }

        // =================================================================
        // Tool: setComponentProperty
        // =================================================================

        [AgentTool("setComponentProperty",
            "Set a field or property value on a component attached to a GameObject. " +
            "Uses reflection to access any public field/property. Supports string, int, float, bool, " +
            "Vector2, Vector3, Color, and enum values.",
            ParametersType = typeof(SetPropertyParams))]
        private IEnumerator HandleSetProperty(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string compType = UnityAgent.ExtractStringField(arguments, "componentType");
            string propName = UnityAgent.ExtractStringField(arguments, "property");
            string value = UnityAgent.ExtractStringField(arguments, "value");

            if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(compType) || string.IsNullOrEmpty(propName))
            {
                callback(AgentToolHelpers.Fail("'target', 'componentType', and 'property' are required."));
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

            // Try field
            var field = type.GetField(propName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                object converted = AgentToolHelpers.ConvertValue(value, field.FieldType);
                if (converted == null)
                {
                    callback(AgentToolHelpers.Fail($"Cannot convert value for field '{propName}' of type {field.FieldType.Name}."));
                    yield break;
                }
                field.SetValue(comp, converted);
                callback(AgentToolHelpers.Ok($"Set {compType}.{propName} = {value}"));
                yield break;
            }

            // Try property
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                object converted = AgentToolHelpers.ConvertValue(value, prop.PropertyType);
                if (converted == null)
                {
                    callback(AgentToolHelpers.Fail($"Cannot convert value for property '{propName}' of type {prop.PropertyType.Name}."));
                    yield break;
                }
                prop.SetValue(comp, converted);
                callback(AgentToolHelpers.Ok($"Set {compType}.{propName} = {value}"));
                yield break;
            }

            callback(AgentToolHelpers.Fail($"No writable field/property '{propName}' found on {compType}."));
            yield break;
        }

        public class SetPropertyParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name (e.g. 'Light', 'Rigidbody').", required: true)]
            public string componentType;
            [ToolParam("Field or property name to set (e.g. 'intensity', 'mass').", required: true)]
            public string property;
            [ToolParam("Value to set. For Vector3: 'x,y,z'. For Color: hex or name.", required: true)]
            public string value;
        }

        // =================================================================
        // Tool: getComponentProperty
        // =================================================================

        [AgentTool("getComponentProperty",
            "Read the value of a field or property on a component. " +
            "Returns the current value as a string. Can also list all public fields/properties.",
            ParametersType = typeof(GetPropertyParams))]
        private IEnumerator HandleGetProperty(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string compType = UnityAgent.ExtractStringField(arguments, "componentType");
            string propName = UnityAgent.ExtractStringField(arguments, "property");
            bool listAll = AgentToolHelpers.ParseBool(
                UnityAgent.ExtractStringField(arguments, "listAll"));

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
            var sb = new StringBuilder();

            // List all mode
            if (listAll || string.IsNullOrEmpty(propName))
            {
                sb.Append("{\"success\":true,\"component\":\"").Append(UnityAgent.EscapeJson(compType)).Append("\",");
                sb.Append("\"members\":[");
                bool first = true;

                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!first) sb.Append(",");
                    first = false;
                    object val = null;
                    try { val = f.GetValue(comp); } catch { }
                    sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(f.Name)).Append("\"");
                    sb.Append(",\"type\":\"").Append(UnityAgent.EscapeJson(f.FieldType.Name)).Append("\"");
                    sb.Append(",\"kind\":\"field\"");
                    sb.Append(",\"value\":\"").Append(UnityAgent.EscapeJson(val?.ToString() ?? "null")).Append("\"}");
                }

                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead) continue;
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    object val = null;
                    try { val = p.GetValue(comp); } catch { }
                    sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(p.Name)).Append("\"");
                    sb.Append(",\"type\":\"").Append(UnityAgent.EscapeJson(p.PropertyType.Name)).Append("\"");
                    sb.Append(",\"kind\":\"property\"");
                    sb.Append(",\"canWrite\":").Append(p.CanWrite ? "true" : "false");
                    sb.Append(",\"value\":\"").Append(UnityAgent.EscapeJson(
                        AgentToolHelpers.Truncate(val?.ToString() ?? "null", 200))).Append("\"}");
                }

                sb.Append("]}");
                callback(new UnityAgent.ToolResult { content = sb.ToString() });
                yield break;
            }

            // Single property read
            object result = null;
            string resultType = "unknown";

            var field = type.GetField(propName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                try { result = field.GetValue(comp); } catch { }
                resultType = field.FieldType.Name;
            }
            else
            {
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanRead)
                {
                    try { result = prop.GetValue(comp); } catch { }
                    resultType = prop.PropertyType.Name;
                }
                else
                {
                    callback(AgentToolHelpers.Fail($"No readable field/property '{propName}' on {compType}."));
                    yield break;
                }
            }

            sb.Append("{\"success\":true,\"property\":\"").Append(UnityAgent.EscapeJson(propName)).Append("\",");
            sb.Append("\"type\":\"").Append(UnityAgent.EscapeJson(resultType)).Append("\",");
            sb.Append("\"value\":\"").Append(UnityAgent.EscapeJson(result?.ToString() ?? "null")).Append("\"}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class GetPropertyParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name.", required: true)]
            public string componentType;
            [ToolParam("Field or property name to read. Omit to list all members.")]
            public string property;
            [ToolParam("Set to 'true' to list all public fields and properties.")]
            public string listAll;
        }
    }
}
