using System;
using System.Collections;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
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

                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: play, pause, stop, step, status."));
                    break;
            }

            yield break;
        }

        public class EditorControlParams
        {
            [ToolParam("Action: play, pause, stop, step, status.", required: true)]
            public string action;
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
