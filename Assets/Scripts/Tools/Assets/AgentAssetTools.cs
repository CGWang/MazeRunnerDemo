using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Asset management tools (Editor-only).
    /// Tools: manageAssets, findAssets, getAssetData, refreshAssets, listShaders
    /// </summary>
    public class AgentAssetTools : MonoBehaviour
    {
#if UNITY_EDITOR
        // =================================================================
        // Tool: manageAssets
        // =================================================================

        [AgentTool("manageAssets",
            "Manage Unity project assets. Actions: 'list' (list folder contents), " +
            "'create_folder' (create a new folder), 'move' (move/rename asset), " +
            "'copy' (duplicate asset), 'delete' (delete asset). All paths relative to Assets/.",
            ParametersType = typeof(ManageAssetsParams))]
        private IEnumerator HandleManageAssets(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            string destination = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "destination"));

            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required."));
                yield break;
            }

            switch (action.ToLower())
            {
                case "list":
                    callback(ListAssets(path ?? "Assets"));
                    break;

                case "create_folder":
                    if (string.IsNullOrEmpty(path))
                    {
                        callback(AgentToolHelpers.Fail("'path' is required for create_folder."));
                        break;
                    }
                    string parentDir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                    string folderName = Path.GetFileName(path);
                    string guid = AssetDatabase.CreateFolder(parentDir, folderName);
                    if (string.IsNullOrEmpty(guid))
                        callback(AgentToolHelpers.Fail($"Failed to create folder '{path}'."));
                    else
                        callback(AgentToolHelpers.Ok($"Folder created: {path}"));
                    break;

                case "move":
                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(destination))
                    {
                        callback(AgentToolHelpers.Fail("'path' and 'destination' required for move."));
                        break;
                    }
                    string moveErr = AssetDatabase.MoveAsset(path, destination);
                    if (string.IsNullOrEmpty(moveErr))
                        callback(AgentToolHelpers.Ok($"Moved '{path}' to '{destination}'."));
                    else
                        callback(AgentToolHelpers.Fail($"Move failed: {moveErr}"));
                    break;

                case "copy":
                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(destination))
                    {
                        callback(AgentToolHelpers.Fail("'path' and 'destination' required for copy."));
                        break;
                    }
                    bool copied = AssetDatabase.CopyAsset(path, destination);
                    if (copied)
                        callback(AgentToolHelpers.Ok($"Copied '{path}' to '{destination}'."));
                    else
                        callback(AgentToolHelpers.Fail($"Copy failed from '{path}' to '{destination}'."));
                    break;

                case "delete":
                    if (string.IsNullOrEmpty(path))
                    {
                        callback(AgentToolHelpers.Fail("'path' is required for delete."));
                        break;
                    }
                    bool deleted = AssetDatabase.DeleteAsset(path);
                    if (deleted)
                        callback(AgentToolHelpers.Ok($"Deleted '{path}'."));
                    else
                        callback(AgentToolHelpers.Fail($"Failed to delete '{path}'."));
                    break;

                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: list, create_folder, move, copy, delete."));
                    break;
            }

            yield break;
        }

        public class ManageAssetsParams
        {
            [ToolParam("Action: list, create_folder, move, copy, delete.", required: true)]
            public string action;
            [ToolParam("Asset path relative to Assets/ (e.g. 'Scripts/MyScript.cs').", required: true)]
            public string path;
            [ToolParam("Destination path for move/copy actions.")]
            public string destination;
        }

        private UnityAgent.ToolResult ListAssets(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
                return AgentToolHelpers.Fail($"Folder not found: {folderPath}");

            string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
            var sb = new StringBuilder();
            sb.Append("{\"folder\":\"").Append(UnityAgent.EscapeJson(folderPath)).Append("\",\"items\":[");

            int count = 0;
            var seen = new HashSet<string>();
            foreach (string g in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(g);
                string relative = assetPath.Substring(folderPath.Length).TrimStart('/');
                if (relative.Contains("/")) continue;
                if (!seen.Add(assetPath)) continue;

                if (count > 0) sb.Append(",");
                bool isFolder = AssetDatabase.IsValidFolder(assetPath);
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(relative)).Append("\"");
                sb.Append(",\"type\":\"").Append(isFolder ? "folder" : Path.GetExtension(relative)).Append("\"");
                sb.Append("}");

                count++;
                if (count >= 100) break;
            }

            sb.Append("],\"count\":").Append(count).Append("}");
            return new UnityAgent.ToolResult { content = sb.ToString() };
        }

        // =================================================================
        // Tool: findAssets
        // =================================================================

        [AgentTool("findAssets",
            "Search for assets in the project using Unity's AssetDatabase.FindAssets. " +
            "Supports filter strings like 't:Material', 't:Prefab', 't:Scene', 'l:label', or name patterns.",
            ParametersType = typeof(FindAssetsParams))]
        private IEnumerator HandleFindAssets(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string filter = UnityAgent.ExtractStringField(arguments, "filter");
            string folder = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "folder"));

            if (string.IsNullOrEmpty(filter))
            {
                callback(AgentToolHelpers.Fail("'filter' is required. Examples: 't:Material', 't:Prefab myPrefab', 'PlayerController'."));
                yield break;
            }

            string[] searchFolders = null;
            if (!string.IsNullOrEmpty(folder))
                searchFolders = new[] { folder };

            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets(filter, searchFolders)
                : AssetDatabase.FindAssets(filter);

            var sb = new StringBuilder();
            sb.Append("{\"filter\":\"").Append(UnityAgent.EscapeJson(filter)).Append("\",");
            sb.Append("\"totalResults\":").Append(guids.Length).Append(",\"results\":[");

            int count = Mathf.Min(guids.Length, 100);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(",");
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                sb.Append("{\"path\":\"").Append(UnityAgent.EscapeJson(assetPath)).Append("\"");
                sb.Append(",\"type\":\"").Append(UnityAgent.EscapeJson(assetType?.Name ?? "Unknown")).Append("\"");
                sb.Append(",\"guid\":\"").Append(guids[i]).Append("\"}");
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class FindAssetsParams
        {
            [ToolParam("Search filter string. Use 't:Type' for type filters, name for name search.", required: true)]
            public string filter;
            [ToolParam("Folder to search in (relative to Assets/). Omit to search all.")]
            public string folder;
        }

        // =================================================================
        // Tool: getAssetData
        // =================================================================

        [AgentTool("getAssetData",
            "Get detailed information about a specific asset: type, file size, dependencies, " +
            "labels, and serializable field values.",
            ParametersType = typeof(GetAssetDataParams))]
        private IEnumerator HandleGetAssetData(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required."));
                yield break;
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
            {
                callback(AgentToolHelpers.Fail($"Asset not found at: {path}"));
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"path\":\"").Append(UnityAgent.EscapeJson(path)).Append("\"");
            sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(asset.name)).Append("\"");
            sb.Append(",\"type\":\"").Append(UnityAgent.EscapeJson(asset.GetType().Name)).Append("\"");

            // File info
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                sb.Append(",\"fileSize\":").Append(fi.Length);
                sb.Append(",\"lastModified\":\"").Append(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")).Append("\"");
            }

            // Labels
            var labels = AssetDatabase.GetLabels(asset);
            if (labels.Length > 0)
            {
                sb.Append(",\"labels\":[");
                for (int i = 0; i < labels.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"").Append(UnityAgent.EscapeJson(labels[i])).Append("\"");
                }
                sb.Append("]");
            }

            // Dependencies
            string[] deps = AssetDatabase.GetDependencies(path, false);
            sb.Append(",\"dependencies\":[");
            int depCount = Mathf.Min(deps.Length, 20);
            for (int i = 0; i < depCount; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(UnityAgent.EscapeJson(deps[i])).Append("\"");
            }
            sb.Append("]");

            // Sub-assets
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (subAssets.Length > 1)
            {
                sb.Append(",\"subAssets\":[");
                bool first = true;
                foreach (var sub in subAssets)
                {
                    if (sub == asset || sub == null) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(sub.name)).Append("\"");
                    sb.Append(",\"type\":\"").Append(UnityAgent.EscapeJson(sub.GetType().Name)).Append("\"}");
                }
                sb.Append("]");
            }

            sb.Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class GetAssetDataParams
        {
            [ToolParam("Asset path relative to Assets/.", required: true)]
            public string path;
        }

        // =================================================================
        // Tool: refreshAssets
        // =================================================================

        [AgentTool("refreshAssets",
            "Refresh the Unity AssetDatabase. Forces Unity to reimport changed assets " +
            "and recompile scripts if needed.",
            ParametersType = typeof(RefreshAssetsParams))]
        private IEnumerator HandleRefreshAssets(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            AssetDatabase.Refresh();
            callback(AgentToolHelpers.Ok("AssetDatabase refreshed."));
            yield break;
        }

        public class RefreshAssetsParams
        {
            // No parameters
        }

        // =================================================================
        // Tool: listShaders
        // =================================================================

        [AgentTool("listShaders",
            "List all available shaders in the project. " +
            "Can filter by name pattern. Returns shader name and property count.",
            ParametersType = typeof(ListShadersParams))]
        private IEnumerator HandleListShaders(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string filter = UnityAgent.ExtractStringField(arguments, "filter");

            // Find all shader assets
            string[] guids = AssetDatabase.FindAssets("t:Shader");
            var sb = new StringBuilder();
            sb.Append("{\"shaders\":[");

            int count = 0;
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null) continue;

                if (!string.IsNullOrEmpty(filter) &&
                    shader.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (count > 0) sb.Append(",");
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(shader.name)).Append("\"");
                sb.Append(",\"path\":\"").Append(UnityAgent.EscapeJson(path)).Append("\"");
                sb.Append(",\"propertyCount\":").Append(shader.GetPropertyCount());
                sb.Append("}");

                count++;
                if (count >= 100) break;
            }

            sb.Append("],\"count\":").Append(count).Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class ListShadersParams
        {
            [ToolParam("Filter shaders by name (case-insensitive partial match).")]
            public string filter;
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentAssetTools] Asset tools are only available in the Unity Editor.");
        }
#endif
    }
}
