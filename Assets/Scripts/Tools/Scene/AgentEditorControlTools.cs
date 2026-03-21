using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Editor control tools (Editor-only).
    /// Tools: editorControl, executeMenuItem, editorSelection
    /// </summary>
    public class AgentEditorControlTools : MonoBehaviour
    {
#if UNITY_EDITOR
        // =================================================================
        // Tool: editorControl
        // =================================================================

        private const int FirstUserLayerIndex = 8;
        private const int TotalLayerCount = 32;

        [AgentTool("editorControl",
            "Control the Unity Editor play state and manage tags/layers. Actions: 'play' (enter play mode), " +
            "'pause' (toggle pause), 'stop' (exit play mode), 'step' (advance one frame while paused), " +
            "'status' (get current state), 'add_tag' (add a project tag), 'remove_tag' (remove a tag), " +
            "'add_layer' (add a user layer), 'remove_layer' (remove a user layer), " +
            "'set_active_tool' (set Editor tool: View, Move, Rotate, Scale, Rect, Transform).",
            ParametersType = typeof(EditorControlParams))]
        private IEnumerator HandleEditorControl(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required."));
                yield break;
            }

            switch (action.ToLower())
            {
                case "play":
                    if (!EditorApplication.isPlaying)
                        EditorApplication.isPlaying = true;
                    callback(AgentToolHelpers.Ok("Entering play mode."));
                    break;

                case "stop":
                    if (EditorApplication.isPlaying)
                        EditorApplication.isPlaying = false;
                    callback(AgentToolHelpers.Ok("Exiting play mode."));
                    break;

                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    callback(AgentToolHelpers.Ok($"Paused: {EditorApplication.isPaused}"));
                    break;

                case "step":
                    EditorApplication.Step();
                    callback(AgentToolHelpers.Ok("Advanced one frame."));
                    break;

                case "status":
                    var sb = new StringBuilder();
                    sb.Append("{\"isPlaying\":").Append(EditorApplication.isPlaying ? "true" : "false");
                    sb.Append(",\"isPaused\":").Append(EditorApplication.isPaused ? "true" : "false");
                    sb.Append(",\"isCompiling\":").Append(EditorApplication.isCompiling ? "true" : "false");
                    sb.Append(",\"timeSinceStartup\":").Append(EditorApplication.timeSinceStartup.ToString("F1"));
                    sb.Append("}");
                    callback(new UnityAgent.ToolResult { content = sb.ToString() });
                    break;

                case "add_tag":
                {
                    string tagName = UnityAgent.ExtractStringField(arguments, "tagName");
                    if (string.IsNullOrEmpty(tagName))
                    {
                        callback(AgentToolHelpers.Fail("'tagName' is required for add_tag."));
                        break;
                    }
                    if (InternalEditorUtility.tags.Contains(tagName))
                    {
                        callback(AgentToolHelpers.Fail($"Tag '{tagName}' already exists."));
                        break;
                    }
                    try
                    {
                        InternalEditorUtility.AddTag(tagName);
                        AssetDatabase.SaveAssets();
                        callback(AgentToolHelpers.Ok($"Tag '{tagName}' added successfully."));
                    }
                    catch (Exception e)
                    {
                        callback(AgentToolHelpers.Fail($"Failed to add tag '{tagName}': {e.Message}"));
                    }
                    break;
                }

                case "remove_tag":
                {
                    string tagName = UnityAgent.ExtractStringField(arguments, "tagName");
                    if (string.IsNullOrEmpty(tagName))
                    {
                        callback(AgentToolHelpers.Fail("'tagName' is required for remove_tag."));
                        break;
                    }
                    if (!InternalEditorUtility.tags.Contains(tagName))
                    {
                        callback(AgentToolHelpers.Fail($"Tag '{tagName}' does not exist."));
                        break;
                    }
                    try
                    {
                        InternalEditorUtility.RemoveTag(tagName);
                        AssetDatabase.SaveAssets();
                        callback(AgentToolHelpers.Ok($"Tag '{tagName}' removed successfully."));
                    }
                    catch (Exception e)
                    {
                        callback(AgentToolHelpers.Fail($"Failed to remove tag '{tagName}': {e.Message}"));
                    }
                    break;
                }

                case "add_layer":
                {
                    string layerName = UnityAgent.ExtractStringField(arguments, "layerName");
                    if (string.IsNullOrEmpty(layerName))
                    {
                        callback(AgentToolHelpers.Fail("'layerName' is required for add_layer."));
                        break;
                    }
                    try
                    {
                        var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                        if (tagManagerAssets == null || tagManagerAssets.Length == 0)
                        {
                            callback(AgentToolHelpers.Fail("Could not access TagManager asset."));
                            break;
                        }
                        var tagManager = new SerializedObject(tagManagerAssets[0]);
                        SerializedProperty layersProp = tagManager.FindProperty("layers");
                        if (layersProp == null || !layersProp.isArray)
                        {
                            callback(AgentToolHelpers.Fail("Could not find 'layers' property in TagManager."));
                            break;
                        }
                        // Check if layer already exists
                        for (int i = 0; i < TotalLayerCount; i++)
                        {
                            SerializedProperty sp = layersProp.GetArrayElementAtIndex(i);
                            if (sp != null && layerName.Equals(sp.stringValue, StringComparison.OrdinalIgnoreCase))
                            {
                                callback(AgentToolHelpers.Fail($"Layer '{layerName}' already exists at index {i}."));
                                yield break;
                            }
                        }
                        // Find first empty user layer slot (8-31)
                        int emptySlot = -1;
                        for (int i = FirstUserLayerIndex; i < TotalLayerCount; i++)
                        {
                            SerializedProperty sp = layersProp.GetArrayElementAtIndex(i);
                            if (sp != null && string.IsNullOrEmpty(sp.stringValue))
                            {
                                emptySlot = i;
                                break;
                            }
                        }
                        if (emptySlot == -1)
                        {
                            callback(AgentToolHelpers.Fail("No empty User Layer slots available (8-31 are full)."));
                            break;
                        }
                        layersProp.GetArrayElementAtIndex(emptySlot).stringValue = layerName;
                        tagManager.ApplyModifiedProperties();
                        AssetDatabase.SaveAssets();
                        callback(AgentToolHelpers.Ok($"Layer '{layerName}' added to slot {emptySlot}."));
                    }
                    catch (Exception e)
                    {
                        callback(AgentToolHelpers.Fail($"Failed to add layer '{layerName}': {e.Message}"));
                    }
                    break;
                }

                case "remove_layer":
                {
                    string layerName = UnityAgent.ExtractStringField(arguments, "layerName");
                    if (string.IsNullOrEmpty(layerName))
                    {
                        callback(AgentToolHelpers.Fail("'layerName' is required for remove_layer."));
                        break;
                    }
                    try
                    {
                        var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                        if (tagManagerAssets == null || tagManagerAssets.Length == 0)
                        {
                            callback(AgentToolHelpers.Fail("Could not access TagManager asset."));
                            break;
                        }
                        var tagManager = new SerializedObject(tagManagerAssets[0]);
                        SerializedProperty layersProp = tagManager.FindProperty("layers");
                        if (layersProp == null || !layersProp.isArray)
                        {
                            callback(AgentToolHelpers.Fail("Could not find 'layers' property in TagManager."));
                            break;
                        }
                        int foundIndex = -1;
                        for (int i = FirstUserLayerIndex; i < TotalLayerCount; i++)
                        {
                            SerializedProperty sp = layersProp.GetArrayElementAtIndex(i);
                            if (sp != null && layerName.Equals(sp.stringValue, StringComparison.OrdinalIgnoreCase))
                            {
                                foundIndex = i;
                                break;
                            }
                        }
                        if (foundIndex == -1)
                        {
                            callback(AgentToolHelpers.Fail($"User layer '{layerName}' not found."));
                            break;
                        }
                        layersProp.GetArrayElementAtIndex(foundIndex).stringValue = string.Empty;
                        tagManager.ApplyModifiedProperties();
                        AssetDatabase.SaveAssets();
                        callback(AgentToolHelpers.Ok($"Layer '{layerName}' (slot {foundIndex}) removed."));
                    }
                    catch (Exception e)
                    {
                        callback(AgentToolHelpers.Fail($"Failed to remove layer '{layerName}': {e.Message}"));
                    }
                    break;
                }

                case "set_active_tool":
                {
                    string toolName = UnityAgent.ExtractStringField(arguments, "toolName");
                    if (string.IsNullOrEmpty(toolName))
                    {
                        callback(AgentToolHelpers.Fail("'toolName' is required for set_active_tool."));
                        break;
                    }
                    try
                    {
                        Tool targetTool;
                        if (Enum.TryParse<Tool>(toolName, true, out targetTool)
                            && targetTool != Tool.None && targetTool <= Tool.Custom)
                        {
                            UnityEditor.Tools.current = targetTool;
                            callback(AgentToolHelpers.Ok($"Set active tool to '{targetTool}'."));
                        }
                        else
                        {
                            callback(AgentToolHelpers.Fail(
                                $"Could not parse '{toolName}' as a standard Unity Tool (View, Move, Rotate, Scale, Rect, Transform)."));
                        }
                    }
                    catch (Exception e)
                    {
                        callback(AgentToolHelpers.Fail($"Error setting active tool: {e.Message}"));
                    }
                    break;
                }

                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: play, pause, stop, step, status, add_tag, remove_tag, add_layer, remove_layer, set_active_tool."));
                    break;
            }

            yield break;
        }

        public class EditorControlParams
        {
            [ToolParam("Action: play, pause, stop, step, status, add_tag, remove_tag, add_layer, remove_layer, set_active_tool.", required: true)]
            public string action;
            [ToolParam("Tag name (for add_tag, remove_tag).")]
            public string tagName;
            [ToolParam("Layer name (for add_layer, remove_layer).")]
            public string layerName;
            [ToolParam("Tool name: View, Move, Rotate, Scale, Rect, Transform (for set_active_tool).")]
            public string toolName;
        }

        // =================================================================
        // Tool: executeMenuItem
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
                callback(AgentToolHelpers.Fail("'menuPath' is required."));
                yield break;
            }

            bool result = EditorApplication.ExecuteMenuItem(menuPath);
            if (result)
                callback(AgentToolHelpers.Ok($"Executed menu item: {menuPath}"));
            else
                callback(AgentToolHelpers.Fail($"Menu item not found or failed: {menuPath}"));

            yield break;
        }

        public class ExecuteMenuItemParams
        {
            [ToolParam("Full menu path (e.g. 'File/Save', 'GameObject/3D Object/Sphere').", required: true)]
            public string menuPath;
        }

        // =================================================================
        // Tool: editorSelection
        // =================================================================

        [AgentTool("editorSelection",
            "Get or set the Editor's current selection. Actions: 'get' (return selected objects), " +
            "'set' (select a GameObject by name), 'clear' (deselect all).",
            ParametersType = typeof(SelectionParams))]
        private IEnumerator HandleSelection(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "get";

            switch (action.ToLower())
            {
                case "get":
                    var selected = Selection.gameObjects;
                    var sb = new StringBuilder();
                    sb.Append("{\"count\":").Append(selected.Length).Append(",\"selected\":[");
                    for (int i = 0; i < selected.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(selected[i].name)).Append("\"");
                        sb.Append(",\"instanceId\":").Append(selected[i].GetInstanceID()).Append("}");
                    }
                    sb.Append("]}");
                    callback(new UnityAgent.ToolResult { content = sb.ToString() });
                    break;

                case "set":
                    string goName = UnityAgent.ExtractStringField(arguments, "target");
                    if (string.IsNullOrEmpty(goName))
                    {
                        callback(AgentToolHelpers.Fail("'target' is required for set."));
                        break;
                    }
                    var go = GameObject.Find(goName);
                    if (go == null)
                    {
                        callback(AgentToolHelpers.Fail($"GameObject '{goName}' not found."));
                        break;
                    }
                    Selection.activeGameObject = go;
                    callback(AgentToolHelpers.Ok($"Selected: {goName}"));
                    break;

                case "clear":
                    Selection.activeGameObject = null;
                    callback(AgentToolHelpers.Ok("Selection cleared."));
                    break;

                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: get, set, clear."));
                    break;
            }

            yield break;
        }

        public class SelectionParams
        {
            [ToolParam("Action: 'get', 'set', 'clear'.", required: true)]
            public string action;
            [ToolParam("GameObject name to select (for 'set' action).")]
            public string target;
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentEditorControlTools] Editor control tools are only available in the Unity Editor.");
        }
#endif
    }
}
