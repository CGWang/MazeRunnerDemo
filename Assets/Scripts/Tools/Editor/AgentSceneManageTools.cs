using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Scene file management tools (Editor-only).
    /// Tools: manageScene (create, open, save, list, setActive, unload, new)
    /// </summary>
    public class AgentSceneManageTools : MonoBehaviour
    {
#if UNITY_EDITOR
        // =================================================================
        // Tool: manageScene
        // =================================================================

        [AgentTool("manageScene",
            "Manage Unity scenes. Actions: 'create' (new scene file), 'open' (load scene), " +
            "'save' (save current scene), 'save_as' (save to new path), 'list' (list opened scenes), " +
            "'set_active' (set active scene), 'unload' (unload additive scene), 'new' (blank scene).",
            ParametersType = typeof(ManageSceneParams))]
        private IEnumerator HandleManageScene(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");

            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required."));
                yield break;
            }

            switch (action.ToLower())
            {
                case "create":
                    HandleCreate(arguments, callback);
                    break;
                case "open":
                    HandleOpen(arguments, callback);
                    break;
                case "save":
                    HandleSave(callback);
                    break;
                case "save_as":
                    HandleSaveAs(arguments, callback);
                    break;
                case "list":
                    HandleList(callback);
                    break;
                case "set_active":
                    HandleSetActive(arguments, callback);
                    break;
                case "unload":
                    HandleUnload(arguments, callback);
                    break;
                case "new":
                    HandleNew(callback);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: create, open, save, save_as, list, set_active, unload, new."));
                    break;
            }

            yield break;
        }

        public class ManageSceneParams
        {
            [ToolParam("Action: create, open, save, save_as, list, set_active, unload, new.", required: true)]
            public string action;
            [ToolParam("Scene path relative to Assets/ (for create, open, save_as).")]
            public string path;
            [ToolParam("Scene name (for set_active, unload - matches loaded scene by name).")]
            public string sceneName;
            [ToolParam("Open mode: 'single' (default) or 'additive' (for open action).")]
            public string mode;
        }

        private void HandleCreate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for create."));
                return;
            }

            if (!path.EndsWith(".unity"))
                path += ".unity";

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(newScene, path);

            callback(AgentToolHelpers.Ok($"Scene created and saved: {path}"));
        }

        private void HandleOpen(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for open."));
                return;
            }

            if (!path.EndsWith(".unity"))
                path += ".unity";

            if (!File.Exists(path))
            {
                callback(AgentToolHelpers.Fail($"Scene file not found: {path}"));
                return;
            }

            string mode = UnityAgent.ExtractStringField(arguments, "mode") ?? "single";
            OpenSceneMode openMode = mode == "additive" ? OpenSceneMode.Additive : OpenSceneMode.Single;

            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.OpenScene(path, openMode);
            callback(AgentToolHelpers.Ok($"Opened scene: {path} ({mode})"));
        }

        private void HandleSave(Action<UnityAgent.ToolResult> callback)
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                callback(AgentToolHelpers.Fail("Active scene has no path. Use 'save_as' with a path instead."));
                return;
            }

            EditorSceneManager.SaveScene(scene);
            callback(AgentToolHelpers.Ok($"Saved scene: {scene.path}"));
        }

        private void HandleSaveAs(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for save_as."));
                return;
            }

            if (!path.EndsWith(".unity"))
                path += ".unity";

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, path);
            callback(AgentToolHelpers.Ok($"Scene saved as: {path}"));
        }

        private void HandleList(Action<UnityAgent.ToolResult> callback)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scenes\":[");

            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(",");
                var scene = SceneManager.GetSceneAt(i);
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(scene.name)).Append("\"");
                sb.Append(",\"path\":\"").Append(UnityAgent.EscapeJson(scene.path)).Append("\"");
                sb.Append(",\"isLoaded\":").Append(scene.isLoaded ? "true" : "false");
                sb.Append(",\"isDirty\":").Append(scene.isDirty ? "true" : "false");
                sb.Append(",\"isActive\":").Append(scene == SceneManager.GetActiveScene() ? "true" : "false");
                sb.Append(",\"rootCount\":").Append(scene.rootCount);
                sb.Append("}");
            }

            sb.Append("],\"count\":").Append(count).Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleSetActive(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string sceneName = UnityAgent.ExtractStringField(arguments, "sceneName");
            if (string.IsNullOrEmpty(sceneName))
            {
                callback(AgentToolHelpers.Fail("'sceneName' is required for set_active."));
                return;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                callback(AgentToolHelpers.Fail($"Scene '{sceneName}' is not loaded."));
                return;
            }

            SceneManager.SetActiveScene(scene);
            callback(AgentToolHelpers.Ok($"Active scene set to: {sceneName}"));
        }

        private void HandleUnload(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string sceneName = UnityAgent.ExtractStringField(arguments, "sceneName");
            if (string.IsNullOrEmpty(sceneName))
            {
                callback(AgentToolHelpers.Fail("'sceneName' is required for unload."));
                return;
            }

            if (SceneManager.sceneCount <= 1)
            {
                callback(AgentToolHelpers.Fail("Cannot unload the only loaded scene."));
                return;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid())
            {
                callback(AgentToolHelpers.Fail($"Scene '{sceneName}' not found."));
                return;
            }

            EditorSceneManager.CloseScene(scene, true);
            callback(AgentToolHelpers.Ok($"Unloaded scene: {sceneName}"));
        }

        private void HandleNew(Action<UnityAgent.ToolResult> callback)
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            callback(AgentToolHelpers.Ok("Created new blank scene."));
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentSceneManageTools] Scene management tools are only available in the Unity Editor.");
        }
#endif
    }
}
