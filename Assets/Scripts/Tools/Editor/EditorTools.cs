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
    /// Editor control and utility tools for the agent framework.
    ///
    /// Tools provided:
    /// - executeMenuItem: Run any Unity Editor menu command
    /// - editorControl: Play/Pause/Stop the game, step frame
    /// - readConsole: Read recent console log messages
    /// - manageMaterial: Create and modify materials
    /// - setComponentProperty: Set component field/property values via reflection
    /// - addComponent: Add a component to a GameObject by type name
    /// - removeComponent: Remove a component from a GameObject
    /// </summary>
    public class EditorTools : MonoBehaviour
    {
        // =================================================================
        // Console log buffer (works in Editor and Runtime)
        // =================================================================

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
        // Tool: readConsole — Read log messages
        // =================================================================

        [AgentTool("readConsole",
            "Read recent Unity console log messages. Can filter by log type " +
            "(Log, Warning, Error). Returns the most recent entries.",
            ParametersType = typeof(ReadConsoleParams))]
        private IEnumerator HandleReadConsole(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string filterType = UnityAgent.ExtractStringField(arguments, "filter");
            string countStr = UnityAgent.ExtractNumberField(arguments, "count");
            int count = 50;
            if (countStr != null) int.TryParse(countStr, out count);
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
                sb.Append(",\"message\":\"").Append(UnityAgent.EscapeJson(Truncate(entry.message, 500))).Append("\"");
                if (entry.type == LogType.Error || entry.type == LogType.Exception)
                    sb.Append(",\"stack\":\"").Append(UnityAgent.EscapeJson(Truncate(entry.stackTrace, 300))).Append("\"");
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
        // Tool: manageMaterial — Create and modify materials
        // =================================================================

        [AgentTool("manageMaterial",
            "Create a new material or modify an existing material on a GameObject. " +
            "Supports setting color, shader, and common properties (metallic, smoothness, emission).",
            ParametersType = typeof(ManageMaterialParams))]
        private IEnumerator HandleManageMaterial(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "modify";
            string targetName = UnityAgent.ExtractStringField(arguments, "target");
            string shader = UnityAgent.ExtractStringField(arguments, "shader");
            string colorStr = UnityAgent.ExtractStringField(arguments, "color");
            string metallicStr = UnityAgent.ExtractNumberField(arguments, "metallic");
            string smoothnessStr = UnityAgent.ExtractNumberField(arguments, "smoothness");
            string emissionStr = UnityAgent.ExtractStringField(arguments, "emission");

            if (action == "create")
            {
#if UNITY_EDITOR
                string savePath = UnityAgent.ExtractStringField(arguments, "savePath");
                string matName = UnityAgent.ExtractStringField(arguments, "name") ?? "NewMaterial";

                var shaderObj = Shader.Find(shader ?? "Standard");
                if (shaderObj == null)
                {
                    callback(Fail($"Shader '{shader}' not found."));
                    yield break;
                }

                var mat = new Material(shaderObj);
                mat.name = matName;

                ApplyMaterialProperties(mat, colorStr, metallicStr, smoothnessStr, emissionStr);

                if (string.IsNullOrEmpty(savePath))
                    savePath = $"Assets/Materials/{matName}.mat";
                if (!savePath.StartsWith("Assets"))
                    savePath = "Assets/" + savePath;

                string dir = System.IO.Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                AssetDatabase.CreateAsset(mat, savePath);
                AssetDatabase.Refresh();

                callback(Ok($"Material created: {savePath}"));
#else
                callback(Fail("Material creation requires Unity Editor."));
#endif
                yield break;
            }

            // Modify existing material on a target GameObject
            if (string.IsNullOrEmpty(targetName))
            {
                callback(Fail("'target' (GameObject name) is required for modify action."));
                yield break;
            }

            var go = GameObject.Find(targetName);
            if (go == null)
            {
                callback(Fail($"GameObject '{targetName}' not found."));
                yield break;
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                callback(Fail($"No Renderer on '{targetName}'."));
                yield break;
            }

            // Work on a material instance (not shared, to avoid affecting other objects)
            var material = renderer.material;

            if (!string.IsNullOrEmpty(shader))
            {
                var shaderObj = Shader.Find(shader);
                if (shaderObj != null) material.shader = shaderObj;
            }

            ApplyMaterialProperties(material, colorStr, metallicStr, smoothnessStr, emissionStr);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"target\":\"").Append(UnityAgent.EscapeJson(targetName)).Append("\"");
            sb.Append(",\"shader\":\"").Append(UnityAgent.EscapeJson(material.shader.name)).Append("\"");
            sb.Append(",\"color\":\"").Append(ColorToHex(material.color)).Append("\"}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class ManageMaterialParams
        {
            [ToolParam("Action: 'modify' (default) or 'create' (Editor only).")]
            public string action;
            [ToolParam("Target GameObject name (for modify action).")]
            public string target;
            [ToolParam("Material name (for create action).")]
            public string name;
            [ToolParam("Shader name (e.g. 'Standard', 'Unlit/Color').")]
            public string shader;
            [ToolParam("Color as hex string '#RRGGBB' or color name (red, blue, green, white, black, yellow).")]
            public string color;
            [ToolParam("Metallic value 0-1 (Standard shader).", SchemaType = "number")]
            public float metallic;
            [ToolParam("Smoothness value 0-1 (Standard shader).", SchemaType = "number")]
            public float smoothness;
            [ToolParam("Emission color as hex string. Set to enable emission.")]
            public string emission;
            [ToolParam("Save path for create action (relative to Assets/).")]
            public string savePath;
        }

        private static void ApplyMaterialProperties(Material mat, string colorStr,
            string metallicStr, string smoothnessStr, string emissionStr)
        {
            if (!string.IsNullOrEmpty(colorStr))
            {
                Color c;
                if (TryParseColor(colorStr, out c))
                    mat.color = c;
            }

            if (metallicStr != null && mat.HasProperty("_Metallic"))
            {
                float v;
                if (float.TryParse(metallicStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                    mat.SetFloat("_Metallic", Mathf.Clamp01(v));
            }

            if (smoothnessStr != null && mat.HasProperty("_Glossiness"))
            {
                float v;
                if (float.TryParse(smoothnessStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                    mat.SetFloat("_Glossiness", Mathf.Clamp01(v));
            }

            if (!string.IsNullOrEmpty(emissionStr))
            {
                mat.EnableKeyword("_EMISSION");
                Color emColor;
                if (TryParseColor(emissionStr, out emColor))
                    mat.SetColor("_EmissionColor", emColor);
            }
        }

        private static bool TryParseColor(string str, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(str)) return false;

            // Named colors
            switch (str.ToLower())
            {
                case "red": color = Color.red; return true;
                case "green": color = Color.green; return true;
                case "blue": color = Color.blue; return true;
                case "white": color = Color.white; return true;
                case "black": color = Color.black; return true;
                case "yellow": color = Color.yellow; return true;
                case "cyan": color = Color.cyan; return true;
                case "magenta": color = Color.magenta; return true;
                case "gray": case "grey": color = Color.gray; return true;
                case "orange": color = new Color(1f, 0.5f, 0f); return true;
                case "purple": color = new Color(0.5f, 0f, 0.5f); return true;
            }

            // Hex color
            if (ColorUtility.TryParseHtmlString(str, out color))
                return true;
            if (str.Length >= 6 && !str.StartsWith("#"))
                return ColorUtility.TryParseHtmlString("#" + str, out color);

            return false;
        }

        private static string ColorToHex(Color c)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(c)}";
        }

#if UNITY_EDITOR
        // =================================================================
        // Tool: executeMenuItem — Run editor menu commands
        // =================================================================

        [AgentTool("executeMenuItem",
            "Execute a Unity Editor menu item by its full path. " +
            "Examples: 'File/Save', 'GameObject/3D Object/Cube', 'Window/General/Console'.",
            ParametersType = typeof(ExecuteMenuItemParams))]
        private IEnumerator HandleExecuteMenuItem(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string menuPath = UnityAgent.ExtractStringField(arguments, "menuPath");
            if (string.IsNullOrEmpty(menuPath))
            {
                callback(Fail("'menuPath' is required."));
                yield break;
            }

            bool result = EditorApplication.ExecuteMenuItem(menuPath);
            if (result)
                callback(Ok($"Executed menu item: {menuPath}"));
            else
                callback(Fail($"Menu item not found or failed: {menuPath}"));

            yield break;
        }

        public class ExecuteMenuItemParams
        {
            [ToolParam("Full menu path (e.g. 'File/Save', 'GameObject/3D Object/Sphere').", required: true)]
            public string menuPath;
        }

        // =================================================================
        // Tool: editorControl — Play/Pause/Stop
        // =================================================================

        [AgentTool("editorControl",
            "Control the Unity Editor play state. Actions: 'play' (enter play mode), " +
            "'pause' (toggle pause), 'stop' (exit play mode), 'step' (advance one frame while paused), " +
            "'status' (get current state).",
            ParametersType = typeof(EditorControlParams))]
        private IEnumerator HandleEditorControl(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            if (string.IsNullOrEmpty(action))
            {
                callback(Fail("'action' is required."));
                yield break;
            }

            switch (action.ToLower())
            {
                case "play":
                    if (!EditorApplication.isPlaying)
                        EditorApplication.isPlaying = true;
                    callback(Ok("Entering play mode."));
                    break;

                case "stop":
                    if (EditorApplication.isPlaying)
                        EditorApplication.isPlaying = false;
                    callback(Ok("Exiting play mode."));
                    break;

                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    callback(Ok($"Paused: {EditorApplication.isPaused}"));
                    break;

                case "step":
                    EditorApplication.Step();
                    callback(Ok("Advanced one frame."));
                    break;

                case "status":
                    var sb = new StringBuilder();
                    sb.Append("{\"isPlaying\":").Append(EditorApplication.isPlaying ? "true" : "false");
                    sb.Append(",\"isPaused\":").Append(EditorApplication.isPaused ? "true" : "false");
                    sb.Append(",\"isCompiling\":").Append(EditorApplication.isCompiling ? "true" : "false");
                    sb.Append("}");
                    callback(new UnityAgent.ToolResult { content = sb.ToString() });
                    break;

                default:
                    callback(Fail($"Unknown action '{action}'. Use: play, pause, stop, step, status."));
                    break;
            }

            yield break;
        }

        public class EditorControlParams
        {
            [ToolParam("Action: play, pause, stop, step, status.", required: true)]
            public string action;
        }
#endif

        // =================================================================
        // Tool: setComponentProperty — Set values on components via reflection
        // =================================================================

        [AgentTool("setComponentProperty",
            "Set a field or property value on a component attached to a GameObject. " +
            "Uses reflection to access any public field/property. Supports string, int, float, bool, " +
            "and Vector3 (as {x,y,z}) values.",
            ParametersType = typeof(SetComponentPropertyParams))]
        private IEnumerator HandleSetComponentProperty(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string compType = UnityAgent.ExtractStringField(arguments, "componentType");
            string propName = UnityAgent.ExtractStringField(arguments, "property");
            string value = UnityAgent.ExtractStringField(arguments, "value");

            if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(compType) || string.IsNullOrEmpty(propName))
            {
                callback(Fail("'target', 'componentType', and 'property' are required."));
                yield break;
            }

            var go = GameObject.Find(goName);
            if (go == null)
            {
                callback(Fail($"GameObject '{goName}' not found."));
                yield break;
            }

            Component comp = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name == compType)
                {
                    comp = c;
                    break;
                }
            }

            if (comp == null)
            {
                callback(Fail($"Component '{compType}' not found on '{goName}'."));
                yield break;
            }

            var type = comp.GetType();

            // Try field first
            var field = type.GetField(propName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                object converted = ConvertValue(value, field.FieldType, arguments, propName);
                if (converted == null)
                {
                    callback(Fail($"Cannot convert value for field '{propName}' of type {field.FieldType.Name}."));
                    yield break;
                }
                field.SetValue(comp, converted);
                callback(Ok($"Set {compType}.{propName} = {value}"));
                yield break;
            }

            // Try property
            var prop = type.GetProperty(propName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                object converted = ConvertValue(value, prop.PropertyType, arguments, propName);
                if (converted == null)
                {
                    callback(Fail($"Cannot convert value for property '{propName}' of type {prop.PropertyType.Name}."));
                    yield break;
                }
                prop.SetValue(comp, converted);
                callback(Ok($"Set {compType}.{propName} = {value}"));
                yield break;
            }

            callback(Fail($"No writable field/property '{propName}' found on {compType}."));
            yield break;
        }

        public class SetComponentPropertyParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name (e.g. 'Light', 'Rigidbody').", required: true)]
            public string componentType;
            [ToolParam("Field or property name to set (e.g. 'intensity', 'mass').", required: true)]
            public string property;
            [ToolParam("Value to set. For Vector3, use {x,y,z} format in the 'value' field.", required: true)]
            public string value;
        }

        private static object ConvertValue(string value, Type targetType, string fullArgs, string fieldName)
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int))
            {
                int v; return int.TryParse(value, out v) ? (object)v : null;
            }
            if (targetType == typeof(float))
            {
                float v; return float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v) ? (object)v : null;
            }
            if (targetType == typeof(double))
            {
                double v; return double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v) ? (object)v : null;
            }
            if (targetType == typeof(bool))
            {
                return value?.ToLower() == "true" ? (object)true :
                       value?.ToLower() == "false" ? (object)false : null;
            }
            if (targetType == typeof(Vector3))
            {
                // Try parsing as {x,y,z} from the value field in the original arguments
                int fieldIdx = fullArgs.IndexOf($"\"{fieldName}\"", StringComparison.Ordinal);
                if (fieldIdx < 0) return null;
                // Try to parse the value string itself as "x,y,z"
                var parts = value?.Split(',');
                if (parts != null && parts.Length == 3)
                {
                    float x, y, z;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, inv, out x) &&
                        float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, inv, out y) &&
                        float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, inv, out z))
                        return new Vector3(x, y, z);
                }
                return null;
            }
            if (targetType == typeof(Vector2))
            {
                var parts = value?.Split(',');
                if (parts != null && parts.Length == 2)
                {
                    float x, y;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, inv, out x) &&
                        float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, inv, out y))
                        return new Vector2(x, y);
                }
                return null;
            }
            if (targetType == typeof(Color))
            {
                Color c;
                return TryParseColor(value, out c) ? (object)c : null;
            }
            if (targetType.IsEnum)
            {
                try { return Enum.Parse(targetType, value, true); }
                catch { return null; }
            }
            return null;
        }

        // =================================================================
        // Tool: addComponent — Add component to a GameObject
        // =================================================================

        [AgentTool("addComponent",
            "Add a component to a GameObject by type name. Supports built-in Unity components " +
            "(Rigidbody, BoxCollider, AudioSource, Light, etc.) and custom components.",
            ParametersType = typeof(AddComponentParams))]
        private IEnumerator HandleAddComponent(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string typeName = UnityAgent.ExtractStringField(arguments, "componentType");

            if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(typeName))
            {
                callback(Fail("'target' and 'componentType' are required."));
                yield break;
            }

            var go = GameObject.Find(goName);
            if (go == null)
            {
                callback(Fail($"GameObject '{goName}' not found."));
                yield break;
            }

            // Find type across all loaded assemblies
            Type compType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == typeName && typeof(Component).IsAssignableFrom(t))
                    {
                        compType = t;
                        break;
                    }
                }
                if (compType != null) break;
            }

            if (compType == null)
            {
                callback(Fail($"Component type '{typeName}' not found."));
                yield break;
            }

            go.AddComponent(compType);
            callback(Ok($"Added {typeName} to {goName}."));
            yield break;
        }

        public class AddComponentParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name (e.g. 'Rigidbody', 'BoxCollider', 'AudioSource').", required: true)]
            public string componentType;
        }

        // =================================================================
        // Tool: removeComponent — Remove a component
        // =================================================================

        [AgentTool("removeComponent",
            "Remove a component from a GameObject by type name. Cannot remove Transform.",
            ParametersType = typeof(RemoveComponentParams),
            RequiresPermission = true)]
        private IEnumerator HandleRemoveComponent(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "target");
            string typeName = UnityAgent.ExtractStringField(arguments, "componentType");

            if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(typeName))
            {
                callback(Fail("'target' and 'componentType' are required."));
                yield break;
            }

            if (typeName == "Transform")
            {
                callback(Fail("Cannot remove Transform component."));
                yield break;
            }

            var go = GameObject.Find(goName);
            if (go == null)
            {
                callback(Fail($"GameObject '{goName}' not found."));
                yield break;
            }

            Component comp = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name == typeName)
                {
                    comp = c;
                    break;
                }
            }

            if (comp == null)
            {
                callback(Fail($"Component '{typeName}' not found on '{goName}'."));
                yield break;
            }

            Destroy(comp);
            callback(Ok($"Removed {typeName} from {goName}."));
            yield break;
        }

        public class RemoveComponentParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string target;
            [ToolParam("Component type name to remove.", required: true)]
            public string componentType;
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static UnityAgent.ToolResult Ok(string message)
        {
            return new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"message\":\"{UnityAgent.EscapeJson(message)}\"}}"
            };
        }

        private static UnityAgent.ToolResult Fail(string error)
        {
            return new UnityAgent.ToolResult
            {
                content = $"{{\"success\":false,\"error\":\"{UnityAgent.EscapeJson(error)}\"}}"
            };
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "...";
        }
    }
}
