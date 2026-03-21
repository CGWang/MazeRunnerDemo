using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// UI Toolkit file management and UIDocument tools (Editor-only).
    /// Supports creating, reading, updating, deleting UXML/USS files,
    /// listing UI assets, attaching/detaching UIDocument components,
    /// creating PanelSettings, and inspecting VisualElement trees.
    /// Tools: manageUI
    /// </summary>
    public class AgentUITools : MonoBehaviour
    {
#if UNITY_EDITOR
        private static readonly HashSet<string> ValidExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".uxml", ".uss"
        };

        // =================================================================
        // Tool: manageUI
        // =================================================================

        [AgentTool("manageUI",
            "Create, read, update, or delete UXML/USS files. Attach or detach UIDocument components. " +
            "Create PanelSettings assets. Inspect VisualElement trees. " +
            "Actions: 'create', 'read', 'update', 'delete', 'list', " +
            "'attach_ui_document', 'detach_ui_document', 'create_panel_settings', 'get_visual_tree'.",
            ParametersType = typeof(UIParams))]
        private IEnumerator HandleManageUI(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "create";

            switch (action)
            {
                case "create":
                    yield return CreateUIFile(arguments, callback);
                    break;
                case "read":
                    yield return ReadUIFile(arguments, callback);
                    break;
                case "update":
                    yield return UpdateUIFile(arguments, callback);
                    break;
                case "delete":
                    yield return DeleteUIFile(arguments, callback);
                    break;
                case "list":
                    yield return ListUIAssets(arguments, callback);
                    break;
                case "attach_ui_document":
                    yield return AttachUIDocument(arguments, callback);
                    break;
                case "detach_ui_document":
                    yield return DetachUIDocument(arguments, callback);
                    break;
                case "create_panel_settings":
                    yield return CreatePanelSettings(arguments, callback);
                    break;
                case "get_visual_tree":
                    yield return GetVisualTree(arguments, callback);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Valid: create, read, update, delete, list, " +
                        "attach_ui_document, detach_ui_document, create_panel_settings, get_visual_tree."));
                    break;
            }
        }

        // -----------------------------------------------------------------
        // create
        // -----------------------------------------------------------------
        private IEnumerator CreateUIFile(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = UnityAgent.ExtractStringField(arguments, "path");
            string contents = UnityAgent.ExtractStringField(arguments, "contents");

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for create."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);
            string ext = Path.GetExtension(path);
            if (!ValidExtensions.Contains(ext))
            {
                callback(AgentToolHelpers.Fail($"Invalid file extension '{ext}'. Must be .uxml or .uss."));
                yield break;
            }

            if (string.IsNullOrEmpty(contents))
            {
                callback(AgentToolHelpers.Fail("'contents' is required for create."));
                yield break;
            }

            if (File.Exists(path))
            {
                callback(AgentToolHelpers.Fail(
                    $"File already exists at '{ToAssetPath(path)}'. Use 'update' to overwrite."));
                yield break;
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, contents, new UTF8Encoding(false));
                string assetPath = ToAssetPath(path);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                callback(AgentToolHelpers.Ok(
                    $"Created {ext.TrimStart('.')} file at '{assetPath}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to create UI file: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // read
        // -----------------------------------------------------------------
        private IEnumerator ReadUIFile(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = UnityAgent.ExtractStringField(arguments, "path");

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for read."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);

            if (!File.Exists(path))
            {
                callback(AgentToolHelpers.Fail($"File not found: '{ToAssetPath(path)}'."));
                yield break;
            }

            try
            {
                string contents = File.ReadAllText(path, Encoding.UTF8);
                int lineCount = contents.Split('\n').Length;

                var sb = new StringBuilder();
                sb.Append("{\"success\":true,\"path\":\"").Append(UnityAgent.EscapeJson(ToAssetPath(path))).Append("\"");
                sb.Append(",\"lineCount\":").Append(lineCount);
                sb.Append(",\"extension\":\"").Append(Path.GetExtension(path).TrimStart('.')).Append("\"");
                sb.Append(",\"contents\":\"").Append(UnityAgent.EscapeJson(contents)).Append("\"");
                sb.Append("}");

                callback(new UnityAgent.ToolResult { content = sb.ToString() });
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to read UI file: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // update
        // -----------------------------------------------------------------
        private IEnumerator UpdateUIFile(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = UnityAgent.ExtractStringField(arguments, "path");
            string contents = UnityAgent.ExtractStringField(arguments, "contents");

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for update."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);

            if (!File.Exists(path))
            {
                callback(AgentToolHelpers.Fail(
                    $"File not found: '{ToAssetPath(path)}'. Use 'create' for new files."));
                yield break;
            }

            if (string.IsNullOrEmpty(contents))
            {
                callback(AgentToolHelpers.Fail("'contents' is required for update."));
                yield break;
            }

            try
            {
                File.WriteAllText(path, contents, new UTF8Encoding(false));
                string assetPath = ToAssetPath(path);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                callback(AgentToolHelpers.Ok(
                    $"Updated {Path.GetExtension(path).TrimStart('.')} file at '{assetPath}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to update UI file: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // delete
        // -----------------------------------------------------------------
        private IEnumerator DeleteUIFile(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = UnityAgent.ExtractStringField(arguments, "path");

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for delete."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);
            string assetPath = ToAssetPath(path);

            if (!File.Exists(path))
            {
                callback(AgentToolHelpers.Fail($"File not found: '{assetPath}'."));
                yield break;
            }

            try
            {
                bool success = AssetDatabase.DeleteAsset(assetPath);
                if (!success)
                {
                    callback(AgentToolHelpers.Fail($"Failed to delete via AssetDatabase: '{assetPath}'."));
                    yield break;
                }

                // Fallback if file still exists
                if (File.Exists(path))
                    File.Delete(path);

                callback(AgentToolHelpers.Ok($"Deleted UI file at '{assetPath}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to delete UI file: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // list
        // -----------------------------------------------------------------
        private IEnumerator ListUIAssets(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string searchPath = UnityAgent.ExtractStringField(arguments, "path") ?? "Assets";
            searchPath = AgentToolHelpers.NormalizePath(searchPath);
            string assetFolder = ToAssetPath(searchPath);
            string filterType = UnityAgent.ExtractStringField(arguments, "filterType");

            string[] folderScope = AssetDatabase.IsValidFolder(assetFolder)
                ? new[] { assetFolder }
                : null;

            var assets = new List<object>();

            bool includeUxml = string.IsNullOrEmpty(filterType) ||
                               filterType.Equals("uxml", StringComparison.OrdinalIgnoreCase);
            bool includeUss = string.IsNullOrEmpty(filterType) ||
                              filterType.Equals("uss", StringComparison.OrdinalIgnoreCase);
            bool includePanelSettings = string.IsNullOrEmpty(filterType) ||
                                        filterType.Equals("PanelSettings", StringComparison.OrdinalIgnoreCase);

            if (includeUxml)
            {
                string[] guids = AssetDatabase.FindAssets("t:VisualTreeAsset", folderScope);
                foreach (string guid in guids)
                {
                    string ap = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(ap))
                        assets.Add(new { path = ap, type = "uxml", name = Path.GetFileName(ap) });
                }
            }

            if (includeUss)
            {
                string[] guids = AssetDatabase.FindAssets("t:StyleSheet", folderScope);
                foreach (string guid in guids)
                {
                    string ap = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(ap))
                        assets.Add(new { path = ap, type = "uss", name = Path.GetFileName(ap) });
                }
            }

            if (includePanelSettings)
            {
                string[] guids = AssetDatabase.FindAssets("t:PanelSettings", folderScope);
                foreach (string guid in guids)
                {
                    string ap = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(ap))
                        assets.Add(new { path = ap, type = "PanelSettings", name = Path.GetFileName(ap) });
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"total\":").Append(assets.Count).Append(",\"assets\":[");

            for (int i = 0; i < assets.Count; i++)
            {
                if (i > 0) sb.Append(",");
                dynamic item = assets[i];
                sb.Append("{\"path\":\"").Append(UnityAgent.EscapeJson(item.path)).Append("\"");
                sb.Append(",\"type\":\"").Append(item.type).Append("\"");
                sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(item.name)).Append("\"}");
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // -----------------------------------------------------------------
        // attach_ui_document
        // -----------------------------------------------------------------
        private IEnumerator AttachUIDocument(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string target = UnityAgent.ExtractStringField(arguments, "target");
            string sourceAsset = UnityAgent.ExtractStringField(arguments, "sourceAsset");
            string panelSettingsPath = UnityAgent.ExtractStringField(arguments, "panelSettings");
            string sortOrderStr = UnityAgent.ExtractNumberField(arguments, "sortOrder");

            if (string.IsNullOrEmpty(target))
            {
                callback(AgentToolHelpers.Fail("'target' (GameObject name) is required for attach_ui_document."));
                yield break;
            }

            if (string.IsNullOrEmpty(sourceAsset))
            {
                callback(AgentToolHelpers.Fail("'sourceAsset' (UXML path) is required for attach_ui_document."));
                yield break;
            }

            sourceAsset = AgentToolHelpers.NormalizePath(sourceAsset);
            string sourceAssetPath = ToAssetPath(sourceAsset);

            GameObject go = GameObject.Find(target);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{target}' not found."));
                yield break;
            }

            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(sourceAssetPath);
            if (vta == null)
            {
                callback(AgentToolHelpers.Fail($"VisualTreeAsset not found at: {sourceAssetPath}"));
                yield break;
            }

            // Load or find PanelSettings
            PanelSettings ps = null;
            if (!string.IsNullOrEmpty(panelSettingsPath))
            {
                panelSettingsPath = AgentToolHelpers.NormalizePath(panelSettingsPath);
                ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(ToAssetPath(panelSettingsPath));
                if (ps == null)
                {
                    callback(AgentToolHelpers.Fail($"PanelSettings not found at: {panelSettingsPath}"));
                    yield break;
                }
            }
            else
            {
                // Find existing PanelSettings
                string[] guids = AssetDatabase.FindAssets("t:PanelSettings");
                if (guids.Length > 0)
                {
                    string existingPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(existingPath);
                }
            }

            Undo.RecordObject(go, "Attach UIDocument");

            var uiDoc = go.GetComponent<UIDocument>();
            if (uiDoc == null)
                uiDoc = Undo.AddComponent<UIDocument>(go);

            uiDoc.visualTreeAsset = vta;
            if (ps != null)
                uiDoc.panelSettings = ps;

            int sortOrder = AgentToolHelpers.ParseInt(sortOrderStr, 0);
            uiDoc.sortingOrder = sortOrder;

            EditorUtility.SetDirty(go);

            callback(AgentToolHelpers.Ok(
                $"Attached UIDocument to '{go.name}' with source '{sourceAssetPath}'."));
            yield break;
        }

        // -----------------------------------------------------------------
        // detach_ui_document
        // -----------------------------------------------------------------
        private IEnumerator DetachUIDocument(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string target = UnityAgent.ExtractStringField(arguments, "target");

            if (string.IsNullOrEmpty(target))
            {
                callback(AgentToolHelpers.Fail("'target' (GameObject name) is required for detach_ui_document."));
                yield break;
            }

            GameObject go = GameObject.Find(target);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{target}' not found."));
                yield break;
            }

            var uiDoc = go.GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{go.name}' has no UIDocument component."));
                yield break;
            }

            string sourceAsset = uiDoc.visualTreeAsset != null
                ? AssetDatabase.GetAssetPath(uiDoc.visualTreeAsset)
                : null;

            Undo.DestroyObjectImmediate(uiDoc);
            EditorUtility.SetDirty(go);

            callback(AgentToolHelpers.Ok(
                $"Removed UIDocument from '{go.name}'." +
                (!string.IsNullOrEmpty(sourceAsset) ? $" Was using: {sourceAsset}" : "")));
            yield break;
        }

        // -----------------------------------------------------------------
        // create_panel_settings
        // -----------------------------------------------------------------
        private IEnumerator CreatePanelSettings(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = UnityAgent.ExtractStringField(arguments, "path");

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for create_panel_settings."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                path += ".asset";

            string assetPath = ToAssetPath(path);

            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath) != null)
            {
                callback(AgentToolHelpers.Fail($"PanelSettings already exists at '{assetPath}'."));
                yield break;
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var ps = ScriptableObject.CreateInstance<PanelSettings>();

                // Apply optional settings
                string scaleMode = UnityAgent.ExtractStringField(arguments, "scaleMode");
                if (!string.IsNullOrEmpty(scaleMode))
                {
                    if (Enum.TryParse<PanelScaleMode>(scaleMode, true, out var sm))
                        ps.scaleMode = sm;
                }

                string sortOrderStr = UnityAgent.ExtractNumberField(arguments, "sortOrder");
                if (sortOrderStr != null)
                    ps.sortingOrder = AgentToolHelpers.ParseInt(sortOrderStr, 0);

                AssetDatabase.CreateAsset(ps, assetPath);
                AssetDatabase.SaveAssets();

                callback(AgentToolHelpers.Ok($"Created PanelSettings at '{assetPath}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to create PanelSettings: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // get_visual_tree
        // -----------------------------------------------------------------
        private IEnumerator GetVisualTree(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string target = UnityAgent.ExtractStringField(arguments, "target");
            string maxDepthStr = UnityAgent.ExtractNumberField(arguments, "maxDepth");
            int maxDepth = AgentToolHelpers.ParseInt(maxDepthStr, 10);

            if (string.IsNullOrEmpty(target))
            {
                callback(AgentToolHelpers.Fail("'target' (GameObject name) is required for get_visual_tree."));
                yield break;
            }

            GameObject go = GameObject.Find(target);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{target}' not found."));
                yield break;
            }

            var uiDoc = go.GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{go.name}' has no UIDocument component."));
                yield break;
            }

            var root = uiDoc.rootVisualElement;
            if (root == null)
            {
                callback(AgentToolHelpers.Ok(
                    $"UIDocument on '{go.name}' has no visual tree (not yet built)."));
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"gameObject\":\"").Append(UnityAgent.EscapeJson(go.name)).Append("\"");

            if (uiDoc.visualTreeAsset != null)
            {
                string vtaPath = AssetDatabase.GetAssetPath(uiDoc.visualTreeAsset);
                sb.Append(",\"sourceAsset\":\"").Append(UnityAgent.EscapeJson(vtaPath)).Append("\"");
            }

            sb.Append(",\"tree\":");
            SerializeVisualElement(root, 0, maxDepth, sb);
            sb.Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static void SerializeVisualElement(VisualElement element, int depth, int maxDepth, StringBuilder sb)
        {
            sb.Append("{\"type\":\"").Append(UnityAgent.EscapeJson(element.GetType().Name)).Append("\"");
            sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(element.name ?? "")).Append("\"");

            // Classes
            var classes = element.GetClasses().ToList();
            if (classes.Count > 0)
            {
                sb.Append(",\"classes\":[");
                for (int i = 0; i < classes.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"").Append(UnityAgent.EscapeJson(classes[i])).Append("\"");
                }
                sb.Append("]");
            }

            // Text content for labels/buttons
            if (element is TextElement textEl && !string.IsNullOrEmpty(textEl.text))
            {
                sb.Append(",\"text\":\"").Append(UnityAgent.EscapeJson(textEl.text)).Append("\"");
            }

            // Children
            if (depth < maxDepth && element.childCount > 0)
            {
                sb.Append(",\"children\":[");
                bool first = true;
                foreach (var child in element.Children())
                {
                    if (!first) sb.Append(",");
                    first = false;
                    SerializeVisualElement(child, depth + 1, maxDepth, sb);
                }
                sb.Append("]");
            }
            else if (element.childCount > 0)
            {
                sb.Append(",\"childCount\":").Append(element.childCount);
            }

            sb.Append("}");
        }

        private static string ToAssetPath(string fullPath)
        {
            fullPath = fullPath.Replace('\\', '/');
            int idx = fullPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return fullPath.Substring(idx);
            return fullPath;
        }
#endif

        // =================================================================
        // Parameter class
        // =================================================================

        public class UIParams
        {
            [ToolParam("Action: 'create', 'read', 'update', 'delete', 'list', 'attach_ui_document', 'detach_ui_document', 'create_panel_settings', 'get_visual_tree'.", required: true)]
            public string action;
            [ToolParam("File path relative to Assets/ (for UXML/USS operations). Must end with .uxml or .uss.")]
            public string path;
            [ToolParam("File contents (for create/update actions).")]
            public string contents;
            [ToolParam("Target GameObject name (for attach/detach/get_visual_tree).")]
            public string target;
            [ToolParam("Path to UXML VisualTreeAsset (for attach_ui_document).")]
            public string sourceAsset;
            [ToolParam("Path to PanelSettings asset (for attach_ui_document). If omitted, finds existing one.")]
            public string panelSettings;
            [ToolParam("Sort order for UIDocument.", SchemaType = "number")]
            public int sortOrder;
            [ToolParam("Filter type for list: 'uxml', 'uss', or 'PanelSettings'. Omit for all.")]
            public string filterType;
            [ToolParam("Max depth for visual tree traversal.", SchemaType = "number")]
            public int maxDepth;
            [ToolParam("Scale mode for PanelSettings: 'ConstantPixelSize', 'ConstantPhysicalSize', 'ScaleWithScreenSize'.")]
            public string scaleMode;
        }
    }
}
