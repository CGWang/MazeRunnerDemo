using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LLMAgent.Tools
{
    /// <summary>
    /// Scene hierarchy query tools (runtime-compatible).
    /// Tools: listScene, findGameObjects
    /// </summary>
    public class AgentSceneTools : MonoBehaviour
    {
        // =================================================================
        // Tool: listScene
        // =================================================================

        [AgentTool("listScene",
            "List all root GameObjects in the active scene, with optional depth for children. " +
            "Returns name, active state, and child count for each.",
            ParametersType = typeof(ListSceneParams))]
        private IEnumerator HandleListScene(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            int depth = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "depth"), 1);
            depth = Mathf.Clamp(depth, 0, 5);

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            var sb = new StringBuilder();
            sb.Append("{\"scene\":\"").Append(UnityAgent.EscapeJson(scene.name)).Append("\",");
            sb.Append("\"rootCount\":").Append(roots.Length).Append(",");
            sb.Append("\"objects\":[");

            for (int i = 0; i < roots.Length; i++)
            {
                if (i > 0) sb.Append(",");
                AgentToolHelpers.AppendGameObjectInfo(sb, roots[i], depth);
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class ListSceneParams
        {
            [ToolParam("Hierarchy depth to include (0=roots only, max 5).", required: false)]
            public int depth;
        }

        // =================================================================
        // Tool: findGameObjects
        // =================================================================

        [AgentTool("findGameObjects",
            "Find GameObjects by name pattern, tag, layer, or component type. " +
            "Returns a list of matches with position, active state, and component list. Max 50 results.",
            ParametersType = typeof(FindGameObjectsParams))]
        private IEnumerator HandleFindGameObjects(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");
            string layer = UnityAgent.ExtractStringField(arguments, "layer");
            string componentType = UnityAgent.ExtractStringField(arguments, "componentType");

            var results = new List<GameObject>();
            const int maxResults = 50;

            // Find by tag (most efficient)
            if (!string.IsNullOrEmpty(tag))
            {
                try
                {
                    var tagged = GameObject.FindGameObjectsWithTag(tag);
                    results.AddRange(tagged);
                }
                catch { /* invalid tag */ }
            }

            // Find by name, layer, or component — scan all objects
            if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(layer) || !string.IsNullOrEmpty(componentType))
            {
                int targetLayer = -1;
                if (!string.IsNullOrEmpty(layer))
                    targetLayer = LayerMask.NameToLayer(layer);

                var allObjects = FindObjectsOfType<GameObject>();
                foreach (var go in allObjects)
                {
                    if (results.Count >= maxResults) break;
                    if (results.Contains(go)) continue;

                    bool match = true;
                    if (!string.IsNullOrEmpty(name) &&
                        go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                        match = false;
                    if (targetLayer >= 0 && go.layer != targetLayer)
                        match = false;
                    if (!string.IsNullOrEmpty(componentType))
                    {
                        if (AgentToolHelpers.FindComponentByTypeName(go, componentType) == null)
                            match = false;
                    }

                    if (match) results.Add(go);
                }
            }

            // If no filter given, list all root objects
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(tag) &&
                string.IsNullOrEmpty(layer) && string.IsNullOrEmpty(componentType))
            {
                var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                foreach (var r in roots)
                {
                    if (results.Count >= maxResults) break;
                    results.Add(r);
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(results.Count).Append(",\"results\":[");
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AgentToolHelpers.AppendGameObjectDetail(sb, results[i]);
            }
            sb.Append("]}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class FindGameObjectsParams
        {
            [ToolParam("Name or partial name to search for (case-insensitive).")]
            public string name;
            [ToolParam("Tag to filter by.")]
            public string tag;
            [ToolParam("Layer name to filter by.")]
            public string layer;
            [ToolParam("Component type name to filter by (e.g. 'Rigidbody', 'Light').")]
            public string componentType;
        }
    }
}
