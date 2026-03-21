using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Prefab workflow tools (Editor-only).
    /// Tools: createPrefab, instantiatePrefab, openPrefab, closePrefab, savePrefab
    /// </summary>
    public class AgentPrefabTools : MonoBehaviour
    {
#if UNITY_EDITOR
        // =================================================================
        // Tool: createPrefab
        // =================================================================

        [AgentTool("createPrefab",
            "Create a Prefab asset from an existing scene GameObject. " +
            "The prefab is saved to the specified path (relative to Assets/).",
            ParametersType = typeof(CreatePrefabParams),
            RequiresPermission = true)]
        private IEnumerator HandleCreatePrefab(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string savePath = AgentToolHelpers.NormalizePath(
                UnityAgent.ExtractStringField(arguments, "savePath"));

            if (string.IsNullOrEmpty(name))
            {
                callback(AgentToolHelpers.Fail("'name' is required (GameObject name in scene)."));
                yield break;
            }

            var go = GameObject.Find(name);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{name}' not found in scene."));
                yield break;
            }

            if (string.IsNullOrEmpty(savePath))
                savePath = $"Assets/Prefabs/{name}.prefab";
            if (!savePath.EndsWith(".prefab"))
                savePath += ".prefab";

            string dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            bool success;
            PrefabUtility.SaveAsPrefabAssetAndConnect(go, savePath, InteractionMode.AutomatedAction, out success);

            if (success)
                callback(AgentToolHelpers.Ok($"Prefab created: {savePath}"));
            else
                callback(AgentToolHelpers.Fail($"Failed to create prefab at {savePath}."));

            yield break;
        }

        public class CreatePrefabParams
        {
            [ToolParam("Name of the scene GameObject to convert to Prefab.", required: true)]
            public string name;
            [ToolParam("Save path relative to Assets/ (e.g. 'Prefabs/MyPrefab.prefab'). Defaults to Assets/Prefabs/<name>.prefab.")]
            public string savePath;
        }

        // =================================================================
        // Tool: instantiatePrefab
        // =================================================================

        [AgentTool("instantiatePrefab",
            "Instantiate a Prefab into the current scene. Specify the prefab asset path and " +
            "optional position/parent.",
            ParametersType = typeof(InstantiatePrefabParams))]
        private IEnumerator HandleInstantiatePrefab(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string prefabPath = AgentToolHelpers.NormalizePath(
                UnityAgent.ExtractStringField(arguments, "prefabPath"));
            string parentName = UnityAgent.ExtractStringField(arguments, "parent");

            if (string.IsNullOrEmpty(prefabPath))
            {
                callback(AgentToolHelpers.Fail("'prefabPath' is required."));
                yield break;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                callback(AgentToolHelpers.Fail($"Prefab not found at: {prefabPath}"));
                yield break;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                    instance.transform.SetParent(parent.transform, false);
            }

            Vector3? pos = AgentToolHelpers.ParseVec3FromArgs(arguments, "position");
            if (pos.HasValue) instance.transform.position = pos.Value;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"name\":\"").Append(UnityAgent.EscapeJson(instance.name)).Append("\",");
            sb.Append("\"prefab\":\"").Append(UnityAgent.EscapeJson(prefabPath)).Append("\",");
            sb.Append("\"position\":").Append(AgentToolHelpers.Vec3Json(instance.transform.position)).Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class InstantiatePrefabParams
        {
            [ToolParam("Prefab asset path relative to Assets/.", required: true)]
            public string prefabPath;
            [ToolParam("Name of parent GameObject to attach to.")]
            public string parent;
            [ToolParam("Initial position as {x,y,z} object.")]
            public string position;
        }

        // =================================================================
        // Tool: openPrefab
        // =================================================================

        [AgentTool("openPrefab",
            "Open a prefab in Prefab Edit Mode (isolated editing stage). " +
            "Pass the asset path of the prefab.",
            ParametersType = typeof(OpenPrefabParams))]
        private IEnumerator HandleOpenPrefab(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required."));
                yield break;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                callback(AgentToolHelpers.Fail($"Prefab not found at: {path}"));
                yield break;
            }

            bool success = AssetDatabase.OpenAsset(prefab);
            if (success)
                callback(AgentToolHelpers.Ok($"Opened prefab: {path}"));
            else
                callback(AgentToolHelpers.Fail($"Failed to open prefab: {path}"));

            yield break;
        }

        public class OpenPrefabParams
        {
            [ToolParam("Prefab asset path relative to Assets/.", required: true)]
            public string path;
        }

        // =================================================================
        // Tool: closePrefab
        // =================================================================

        [AgentTool("closePrefab",
            "Close the current Prefab Edit Mode and return to the scene.",
            ParametersType = typeof(ClosePrefabParams))]
        private IEnumerator HandleClosePrefab(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                callback(AgentToolHelpers.Fail("No prefab is currently being edited."));
                yield break;
            }

            StageUtility.GoToMainStage();
            callback(AgentToolHelpers.Ok("Closed prefab edit mode."));
            yield break;
        }

        public class ClosePrefabParams
        {
            // No parameters
        }

        // =================================================================
        // Tool: savePrefab
        // =================================================================

        [AgentTool("savePrefab",
            "Save changes to the currently open prefab. Also supports applying overrides " +
            "from a prefab instance in the scene back to its source prefab.",
            ParametersType = typeof(SavePrefabParams),
            RequiresPermission = true)]
        private IEnumerator HandleSavePrefab(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string instanceName = UnityAgent.ExtractStringField(arguments, "instanceName");

            // If an instance name is given, apply overrides from that instance
            if (!string.IsNullOrEmpty(instanceName))
            {
                var go = GameObject.Find(instanceName);
                if (go == null)
                {
                    callback(AgentToolHelpers.Fail($"GameObject '{instanceName}' not found."));
                    yield break;
                }

                var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (prefabRoot == null)
                {
                    callback(AgentToolHelpers.Fail($"'{instanceName}' is not a prefab instance."));
                    yield break;
                }

                PrefabUtility.ApplyPrefabInstance(prefabRoot, InteractionMode.AutomatedAction);
                var source = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
                string path = source != null ? AssetDatabase.GetAssetPath(source) : "unknown";
                callback(AgentToolHelpers.Ok($"Applied overrides from '{instanceName}' to prefab: {path}"));
                yield break;
            }

            // Otherwise save the currently open prefab stage
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                callback(AgentToolHelpers.Fail("No prefab is currently being edited and no instanceName given."));
                yield break;
            }

            // Mark dirty and save
            EditorUtility.SetDirty(stage.prefabContentsRoot);
            PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
            callback(AgentToolHelpers.Ok($"Saved prefab: {stage.assetPath}"));
            yield break;
        }

        public class SavePrefabParams
        {
            [ToolParam("Name of a prefab instance in scene to apply overrides from. Omit to save current prefab stage.")]
            public string instanceName;
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentPrefabTools] Prefab tools are only available in the Unity Editor.");
        }
#endif
    }
}
