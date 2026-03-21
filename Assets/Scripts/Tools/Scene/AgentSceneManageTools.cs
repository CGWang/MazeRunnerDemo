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
            "'set_active' (set active scene), 'unload' (unload additive scene), 'new' (blank scene), " +
            "'get_hierarchy' (get scene hierarchy tree with pagination), 'get_active' (get active scene info), " +
            "'get_build_settings' (get build settings scenes list), 'screenshot' (capture a screenshot), " +
            "'scene_view_frame' (frame an object or entire scene in the Scene View).",
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
                case "get_hierarchy":
                    HandleGetHierarchy(arguments, callback);
                    break;
                case "get_active":
                    HandleGetActive(callback);
                    break;
                case "get_build_settings":
                    HandleGetBuildSettings(callback);
                    break;
                case "screenshot":
                    HandleScreenshot(arguments, callback);
                    break;
                case "scene_view_frame":
                    HandleSceneViewFrame(arguments, callback);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: create, open, save, save_as, list, set_active, unload, new, get_hierarchy, get_active, get_build_settings, screenshot, scene_view_frame."));
                    break;
            }

            yield break;
        }

        public class ManageSceneParams
        {
            [ToolParam("Action: create, open, save, save_as, list, set_active, unload, new, get_hierarchy, get_active, get_build_settings, screenshot, scene_view_frame.", required: true)]
            public string action;
            [ToolParam("Scene path relative to Assets/ (for create, open, save_as).")]
            public string path;
            [ToolParam("Scene name (for set_active, unload - matches loaded scene by name).")]
            public string sceneName;
            [ToolParam("Open mode: 'single' (default) or 'additive' (for open action).")]
            public string mode;
            [ToolParam("Page size for get_hierarchy (default 50, max 500).")]
            public string pageSize;
            [ToolParam("Cursor for get_hierarchy pagination (0-based index).")]
            public string cursor;
            [ToolParam("Max depth for get_hierarchy (default unlimited).")]
            public string maxDepth;
            [ToolParam("Target GameObject name (for scene_view_frame). Omit to frame entire scene.")]
            public string target;
            [ToolParam("File name for screenshot (optional, auto-generated if omitted).")]
            public string fileName;
            [ToolParam("Super size multiplier for screenshot (default 1).")]
            public string superSize;
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

        private void HandleGetHierarchy(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            try
            {
                var activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    callback(AgentToolHelpers.Fail("No valid and loaded scene is active."));
                    return;
                }

                int pageSize = Mathf.Clamp(AgentToolHelpers.ParseInt(
                    UnityAgent.ExtractStringField(arguments, "pageSize"), 50), 1, 500);
                int cursor = Mathf.Max(0, AgentToolHelpers.ParseInt(
                    UnityAgent.ExtractStringField(arguments, "cursor"), 0));
                int maxDepth = AgentToolHelpers.ParseInt(
                    UnityAgent.ExtractStringField(arguments, "maxDepth"), -1);

                var roots = activeScene.GetRootGameObjects();
                var sb = new StringBuilder();
                sb.Append("{\"scene\":\"").Append(UnityAgent.EscapeJson(activeScene.name)).Append("\"");
                sb.Append(",\"totalRoots\":").Append(roots.Length);

                int end = Mathf.Min(roots.Length, cursor + pageSize);
                sb.Append(",\"cursor\":").Append(cursor);
                sb.Append(",\"pageSize\":").Append(pageSize);
                sb.Append(",\"truncated\":").Append(end < roots.Length ? "true" : "false");
                if (end < roots.Length)
                    sb.Append(",\"nextCursor\":").Append(end);

                sb.Append(",\"items\":[");
                for (int i = cursor; i < end; i++)
                {
                    if (i > cursor) sb.Append(",");
                    AppendGameObjectNode(sb, roots[i], 0, maxDepth);
                }
                sb.Append("]}");

                callback(new UnityAgent.ToolResult { content = sb.ToString() });
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Error getting hierarchy: {e.Message}"));
            }
        }

        private void AppendGameObjectNode(StringBuilder sb, GameObject go, int depth, int maxDepth)
        {
            sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(go.name)).Append("\"");
            sb.Append(",\"instanceId\":").Append(go.GetInstanceID());
            sb.Append(",\"activeSelf\":").Append(go.activeSelf ? "true" : "false");
            sb.Append(",\"childCount\":").Append(go.transform.childCount);

            // Component types
            var components = go.GetComponents<Component>();
            sb.Append(",\"components\":[");
            for (int c = 0; c < components.Length; c++)
            {
                if (c > 0) sb.Append(",");
                sb.Append("\"").Append(components[c] != null ? UnityAgent.EscapeJson(components[c].GetType().Name) : "null").Append("\"");
            }
            sb.Append("]");

            // Recurse children if within depth limit
            if (go.transform.childCount > 0 && (maxDepth < 0 || depth < maxDepth))
            {
                sb.Append(",\"children\":[");
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendGameObjectNode(sb, go.transform.GetChild(i).gameObject, depth + 1, maxDepth);
                }
                sb.Append("]");
            }

            sb.Append("}");
        }

        private void HandleGetActive(Action<UnityAgent.ToolResult> callback)
        {
            try
            {
                var activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                {
                    callback(AgentToolHelpers.Fail("No active scene found."));
                    return;
                }

                var sb = new StringBuilder();
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(activeScene.name)).Append("\"");
                sb.Append(",\"path\":\"").Append(UnityAgent.EscapeJson(activeScene.path)).Append("\"");
                sb.Append(",\"buildIndex\":").Append(activeScene.buildIndex);
                sb.Append(",\"isDirty\":").Append(activeScene.isDirty ? "true" : "false");
                sb.Append(",\"isLoaded\":").Append(activeScene.isLoaded ? "true" : "false");
                sb.Append(",\"rootCount\":").Append(activeScene.rootCount);
                sb.Append("}");

                callback(new UnityAgent.ToolResult { content = sb.ToString() });
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Error getting active scene info: {e.Message}"));
            }
        }

        private void HandleGetBuildSettings(Action<UnityAgent.ToolResult> callback)
        {
            try
            {
                var scenes = EditorBuildSettings.scenes;
                var sb = new StringBuilder();
                sb.Append("{\"scenes\":[");

                for (int i = 0; i < scenes.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    var scene = scenes[i];
                    sb.Append("{\"path\":\"").Append(UnityAgent.EscapeJson(scene.path)).Append("\"");
                    sb.Append(",\"guid\":\"").Append(scene.guid.ToString()).Append("\"");
                    sb.Append(",\"enabled\":").Append(scene.enabled ? "true" : "false");
                    sb.Append(",\"buildIndex\":").Append(i);
                    sb.Append("}");
                }

                sb.Append("],\"count\":").Append(scenes.Length).Append("}");
                callback(new UnityAgent.ToolResult { content = sb.ToString() });
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Error getting build settings: {e.Message}"));
            }
        }

        private void HandleScreenshot(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            try
            {
                string fileName = UnityAgent.ExtractStringField(arguments, "fileName");
                int superSize = AgentToolHelpers.ParseInt(
                    UnityAgent.ExtractStringField(arguments, "superSize"), 1);
                if (superSize < 1) superSize = 1;

                if (string.IsNullOrEmpty(fileName))
                    fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                else if (!fileName.EndsWith(".png"))
                    fileName += ".png";

                string dir = System.IO.Path.Combine(Application.dataPath, "Screenshots");
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                string fullPath = System.IO.Path.Combine(dir, fileName);
                ScreenCapture.CaptureScreenshot(fullPath, superSize);

                string relativePath = "Assets/Screenshots/" + fileName;
                callback(AgentToolHelpers.Ok($"Screenshot captured to '{relativePath}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Error capturing screenshot: {e.Message}"));
            }
        }

        private void HandleSceneViewFrame(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            try
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    callback(AgentToolHelpers.Fail("No active Scene View found. Open a Scene View window first."));
                    return;
                }

                string target = UnityAgent.ExtractStringField(arguments, "target");
                if (!string.IsNullOrEmpty(target))
                {
                    var go = GameObject.Find(target);
                    if (go == null)
                    {
                        callback(AgentToolHelpers.Fail($"GameObject '{target}' not found."));
                        return;
                    }

                    // Calculate bounds from all renderers on the target
                    var renderers = go.GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 0)
                    {
                        Bounds bounds = renderers[0].bounds;
                        for (int i = 1; i < renderers.Length; i++)
                            bounds.Encapsulate(renderers[i].bounds);
                        sceneView.Frame(bounds, false);
                    }
                    else
                    {
                        // Fallback: use transform position with a small bounds
                        Bounds bounds = new Bounds(go.transform.position, Vector3.one);
                        sceneView.Frame(bounds, false);
                    }
                    callback(AgentToolHelpers.Ok($"Scene View framed on '{go.name}'."));
                }
                else
                {
                    // Frame entire scene
                    Bounds allBounds = new Bounds(Vector3.zero, Vector3.zero);
                    bool hasAny = false;
                    foreach (var r in FindObjectsOfType<Renderer>())
                    {
                        if (r == null || !r.gameObject.activeInHierarchy) continue;
                        if (!hasAny) { allBounds = r.bounds; hasAny = true; }
                        else allBounds.Encapsulate(r.bounds);
                    }
                    if (!hasAny) allBounds = new Bounds(Vector3.zero, Vector3.one * 10f);
                    sceneView.Frame(allBounds, false);
                    callback(AgentToolHelpers.Ok("Scene View framed on entire scene."));
                }
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Error framing Scene View: {e.Message}"));
            }
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentSceneManageTools] Scene management tools are only available in the Unity Editor.");
        }
#endif
    }
}
