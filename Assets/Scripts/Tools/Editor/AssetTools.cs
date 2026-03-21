using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Editor-mode asset and script management tools. These tools use UnityEditor APIs and
    /// only function inside the Unity Editor (not in builds). Add this component alongside
    /// your AgentManager or assign it to the toolProviders array.
    ///
    /// Tools provided:
    /// - manageAssets: Create/move/copy/delete assets
    /// - createScript: Generate C# scripts from templates
    /// - readScript: Read script file contents
    /// - editScript: Apply text replacements to scripts
    /// - createPrefab: Turn a scene GameObject into a Prefab
    /// - instantiatePrefab: Instantiate a Prefab into the scene
    /// </summary>
    public class AssetTools : MonoBehaviour
    {
#if UNITY_EDITOR
        // =================================================================
        // Tool: manageAssets — Create/move/copy/delete assets
        // =================================================================

        [AgentTool("manageAssets",
            "Manage Unity project assets. Actions: 'list' (list folder contents), " +
            "'create_folder' (create a new folder), 'move' (move/rename asset), " +
            "'copy' (duplicate asset), 'delete' (delete asset). All paths relative to Assets/.",
            ParametersType = typeof(ManageAssetsParams))]
        private IEnumerator HandleManageAssets(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            string path = UnityAgent.ExtractStringField(arguments, "path");
            string destination = UnityAgent.ExtractStringField(arguments, "destination");

            if (string.IsNullOrEmpty(action))
            {
                callback(Fail("'action' is required."));
                yield break;
            }

            // Normalize paths to Unity format
            if (!string.IsNullOrEmpty(path) && !path.StartsWith("Assets"))
                path = "Assets/" + path;
            if (!string.IsNullOrEmpty(destination) && !destination.StartsWith("Assets"))
                destination = "Assets/" + destination;

            switch (action.ToLower())
            {
                case "list":
                    callback(ListAssets(path ?? "Assets"));
                    break;

                case "create_folder":
                    if (string.IsNullOrEmpty(path))
                    {
                        callback(Fail("'path' is required for create_folder."));
                        break;
                    }
                    string parentDir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                    string folderName = Path.GetFileName(path);
                    string guid = AssetDatabase.CreateFolder(parentDir, folderName);
                    if (string.IsNullOrEmpty(guid))
                        callback(Fail($"Failed to create folder '{path}'."));
                    else
                        callback(Ok($"Folder created: {path}"));
                    break;

                case "move":
                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(destination))
                    {
                        callback(Fail("'path' and 'destination' required for move."));
                        break;
                    }
                    string moveErr = AssetDatabase.MoveAsset(path, destination);
                    if (string.IsNullOrEmpty(moveErr))
                        callback(Ok($"Moved '{path}' to '{destination}'."));
                    else
                        callback(Fail($"Move failed: {moveErr}"));
                    break;

                case "copy":
                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(destination))
                    {
                        callback(Fail("'path' and 'destination' required for copy."));
                        break;
                    }
                    bool copied = AssetDatabase.CopyAsset(path, destination);
                    if (copied)
                        callback(Ok($"Copied '{path}' to '{destination}'."));
                    else
                        callback(Fail($"Copy failed from '{path}' to '{destination}'."));
                    break;

                case "delete":
                    if (string.IsNullOrEmpty(path))
                    {
                        callback(Fail("'path' is required for delete."));
                        break;
                    }
                    bool deleted = AssetDatabase.DeleteAsset(path);
                    if (deleted)
                        callback(Ok($"Deleted '{path}'."));
                    else
                        callback(Fail($"Failed to delete '{path}'."));
                    break;

                default:
                    callback(Fail($"Unknown action '{action}'. Use: list, create_folder, move, copy, delete."));
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
                return FailResult($"Folder not found: {folderPath}");

            string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
            var sb = new StringBuilder();
            sb.Append("{\"folder\":\"").Append(UnityAgent.EscapeJson(folderPath)).Append("\",\"items\":[");

            int count = 0;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                // Only direct children
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
        // Tool: createScript — Generate C# scripts
        // =================================================================

        [AgentTool("createScript",
            "Create a new C# script file in the project. Provide the file path (relative to Assets/) " +
            "and the full script content. The file will be created and AssetDatabase refreshed.",
            ParametersType = typeof(CreateScriptParams),
            RequiresPermission = true)]
        private IEnumerator HandleCreateScript(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = UnityAgent.ExtractStringField(arguments, "path");
            string content = UnityAgent.ExtractStringField(arguments, "content");
            string template = UnityAgent.ExtractStringField(arguments, "template");

            if (string.IsNullOrEmpty(path))
            {
                callback(Fail("'path' is required."));
                yield break;
            }

            if (!path.StartsWith("Assets"))
                path = "Assets/" + path;

            if (!path.EndsWith(".cs"))
                path += ".cs";

            // Check if file already exists
            if (File.Exists(path))
            {
                callback(Fail($"File already exists: {path}. Use editScript to modify it."));
                yield break;
            }

            // Use template if content not provided
            if (string.IsNullOrEmpty(content))
            {
                string className = Path.GetFileNameWithoutExtension(path);
                content = GenerateScriptFromTemplate(className, template ?? "monobehaviour");
            }

            // Ensure directory exists
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, content);
            AssetDatabase.Refresh();

            callback(Ok($"Script created: {path} ({content.Length} chars)."));
            yield break;
        }

        public class CreateScriptParams
        {
            [ToolParam("File path relative to Assets/ (e.g. 'Scripts/MyScript.cs').", required: true)]
            public string path;
            [ToolParam("Full C# source code content. If omitted, uses a template.")]
            public string content;
            [ToolParam("Template type: 'monobehaviour', 'scriptableobject', 'editor', 'static'. Used only if content is empty.")]
            public string template;
        }

        private static string GenerateScriptFromTemplate(string className, string template)
        {
            switch (template?.ToLower())
            {
                case "scriptableobject":
                    return $"using UnityEngine;\n\n[CreateAssetMenu(fileName = \"{className}\", menuName = \"Custom/{className}\")]\npublic class {className} : ScriptableObject\n{{\n    \n}}\n";
                case "editor":
                    return $"using UnityEngine;\nusing UnityEditor;\n\n[CustomEditor(typeof(MonoBehaviour))]\npublic class {className} : Editor\n{{\n    public override void OnInspectorGUI()\n    {{\n        base.OnInspectorGUI();\n    }}\n}}\n";
                case "static":
                    return $"using UnityEngine;\n\npublic static class {className}\n{{\n    \n}}\n";
                default: // monobehaviour
                    return $"using UnityEngine;\n\npublic class {className} : MonoBehaviour\n{{\n    void Start()\n    {{\n        \n    }}\n\n    void Update()\n    {{\n        \n    }}\n}}\n";
            }
        }

        // =================================================================
        // Tool: readScript — Read file contents
        // =================================================================

        [AgentTool("readScript",
            "Read the contents of a script or text file in the project. Returns the file content " +
            "and line count. Path is relative to Assets/.",
            ParametersType = typeof(ReadScriptParams))]
        private IEnumerator HandleReadScript(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = UnityAgent.ExtractStringField(arguments, "path");
            string lineStartStr = UnityAgent.ExtractNumberField(arguments, "lineStart");
            string lineEndStr = UnityAgent.ExtractNumberField(arguments, "lineEnd");

            if (string.IsNullOrEmpty(path))
            {
                callback(Fail("'path' is required."));
                yield break;
            }

            if (!path.StartsWith("Assets"))
                path = "Assets/" + path;

            if (!File.Exists(path))
            {
                callback(Fail($"File not found: {path}"));
                yield break;
            }

            string[] lines = File.ReadAllLines(path);
            int lineStart = 1;
            int lineEnd = lines.Length;

            if (lineStartStr != null) int.TryParse(lineStartStr, out lineStart);
            if (lineEndStr != null) int.TryParse(lineEndStr, out lineEnd);

            lineStart = Mathf.Clamp(lineStart, 1, lines.Length);
            lineEnd = Mathf.Clamp(lineEnd, lineStart, lines.Length);

            var sb = new StringBuilder();
            sb.Append("{\"path\":\"").Append(UnityAgent.EscapeJson(path)).Append("\",");
            sb.Append("\"totalLines\":").Append(lines.Length).Append(",");
            sb.Append("\"showing\":\"").Append(lineStart).Append("-").Append(lineEnd).Append("\",");
            sb.Append("\"content\":\"");

            for (int i = lineStart - 1; i < lineEnd; i++)
            {
                if (i > lineStart - 1) sb.Append("\\n");
                sb.Append(UnityAgent.EscapeJson(lines[i]));
            }

            sb.Append("\"}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class ReadScriptParams
        {
            [ToolParam("File path relative to Assets/.", required: true)]
            public string path;
            [ToolParam("Start line number (1-based). Omit to read from beginning.")]
            public int lineStart;
            [ToolParam("End line number. Omit to read to end.")]
            public int lineEnd;
        }

        // =================================================================
        // Tool: editScript — Text replacement in scripts
        // =================================================================

        [AgentTool("editScript",
            "Edit a script file by replacing a specific text pattern with new text. " +
            "The old_text must be an exact match (including whitespace). " +
            "Alternatively, use action='append' to add text at the end, or 'insert_line' to insert at a line number.",
            ParametersType = typeof(EditScriptParams),
            RequiresPermission = true)]
        private IEnumerator HandleEditScript(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = UnityAgent.ExtractStringField(arguments, "path");
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "replace";
            string oldText = UnityAgent.ExtractStringField(arguments, "old_text");
            string newText = UnityAgent.ExtractStringField(arguments, "new_text");
            string lineStr = UnityAgent.ExtractNumberField(arguments, "line");

            if (string.IsNullOrEmpty(path))
            {
                callback(Fail("'path' is required."));
                yield break;
            }

            if (!path.StartsWith("Assets"))
                path = "Assets/" + path;

            if (!File.Exists(path))
            {
                callback(Fail($"File not found: {path}"));
                yield break;
            }

            string content = File.ReadAllText(path);

            switch (action.ToLower())
            {
                case "replace":
                    if (string.IsNullOrEmpty(oldText))
                    {
                        callback(Fail("'old_text' is required for replace action."));
                        yield break;
                    }
                    if (!content.Contains(oldText))
                    {
                        callback(Fail("old_text not found in file. Ensure exact match including whitespace."));
                        yield break;
                    }
                    int occurrences = CountOccurrences(content, oldText);
                    content = content.Replace(oldText, newText ?? "");
                    File.WriteAllText(path, content);
                    AssetDatabase.Refresh();
                    callback(Ok($"Replaced {occurrences} occurrence(s) in {path}."));
                    break;

                case "append":
                    content += "\n" + (newText ?? "");
                    File.WriteAllText(path, content);
                    AssetDatabase.Refresh();
                    callback(Ok($"Appended text to {path}."));
                    break;

                case "insert_line":
                    int line = 1;
                    if (lineStr != null) int.TryParse(lineStr, out line);
                    var lines = new System.Collections.Generic.List<string>(content.Split('\n'));
                    line = Mathf.Clamp(line, 1, lines.Count + 1);
                    lines.Insert(line - 1, newText ?? "");
                    File.WriteAllText(path, string.Join("\n", lines));
                    AssetDatabase.Refresh();
                    callback(Ok($"Inserted text at line {line} in {path}."));
                    break;

                default:
                    callback(Fail($"Unknown action '{action}'. Use: replace, append, insert_line."));
                    break;
            }

            yield break;
        }

        public class EditScriptParams
        {
            [ToolParam("File path relative to Assets/.", required: true)]
            public string path;
            [ToolParam("Edit action: 'replace' (default), 'append', 'insert_line'.")]
            public string action;
            [ToolParam("Exact text to find and replace (for 'replace' action).")]
            public string old_text;
            [ToolParam("New text to insert or replacement text.")]
            public string new_text;
            [ToolParam("Line number for 'insert_line' action (1-based).", SchemaType = "integer")]
            public int line;
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }
            return count;
        }

        // =================================================================
        // Tool: createPrefab — Turn GameObject into Prefab
        // =================================================================

        [AgentTool("createPrefab",
            "Create a Prefab asset from an existing scene GameObject. " +
            "The prefab is saved to the specified path (relative to Assets/).",
            ParametersType = typeof(CreatePrefabParams),
            RequiresPermission = true)]
        private IEnumerator HandleCreatePrefab(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string savePath = UnityAgent.ExtractStringField(arguments, "savePath");

            if (string.IsNullOrEmpty(name))
            {
                callback(Fail("'name' is required (GameObject name in scene)."));
                yield break;
            }

            var go = GameObject.Find(name);
            if (go == null)
            {
                callback(Fail($"GameObject '{name}' not found in scene."));
                yield break;
            }

            if (string.IsNullOrEmpty(savePath))
                savePath = $"Assets/Prefabs/{name}.prefab";
            else if (!savePath.StartsWith("Assets"))
                savePath = "Assets/" + savePath;

            if (!savePath.EndsWith(".prefab"))
                savePath += ".prefab";

            // Ensure directory
            string dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            bool success;
            PrefabUtility.SaveAsPrefabAssetAndConnect(go, savePath, InteractionMode.AutomatedAction, out success);

            if (success)
                callback(Ok($"Prefab created: {savePath}"));
            else
                callback(Fail($"Failed to create prefab at {savePath}."));

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
        // Tool: instantiatePrefab — Add Prefab instance to scene
        // =================================================================

        [AgentTool("instantiatePrefab",
            "Instantiate a Prefab into the current scene. Specify the prefab asset path and " +
            "optional position/parent.",
            ParametersType = typeof(InstantiatePrefabParams))]
        private IEnumerator HandleInstantiatePrefab(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string prefabPath = UnityAgent.ExtractStringField(arguments, "prefabPath");
            string parentName = UnityAgent.ExtractStringField(arguments, "parent");

            if (string.IsNullOrEmpty(prefabPath))
            {
                callback(Fail("'prefabPath' is required."));
                yield break;
            }

            if (!prefabPath.StartsWith("Assets"))
                prefabPath = "Assets/" + prefabPath;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                callback(Fail($"Prefab not found at: {prefabPath}"));
                yield break;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            // Set parent
            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                    instance.transform.SetParent(parent.transform, false);
            }

            // Set position
            Vector3? pos = ParseVec3FromArgs(arguments, "position");
            if (pos.HasValue) instance.transform.position = pos.Value;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"name\":\"").Append(UnityAgent.EscapeJson(instance.name)).Append("\",");
            sb.Append("\"prefab\":\"").Append(UnityAgent.EscapeJson(prefabPath)).Append("\",");
            sb.Append("\"position\":{\"x\":").Append(instance.transform.position.x.ToString("F2"));
            sb.Append(",\"y\":").Append(instance.transform.position.y.ToString("F2"));
            sb.Append(",\"z\":").Append(instance.transform.position.z.ToString("F2")).Append("}}");

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
        // Helpers
        // =================================================================

        private static Vector3? ParseVec3FromArgs(string json, string fieldName)
        {
            int fieldIdx = json.IndexOf($"\"{fieldName}\"", StringComparison.Ordinal);
            if (fieldIdx < 0) return null;

            int braceStart = json.IndexOf('{', fieldIdx);
            if (braceStart < 0) return null;
            int braceEnd = UnityAgent.FindMatchingBrace(json, braceStart);
            if (braceEnd < 0) return null;

            string obj = json.Substring(braceStart, braceEnd - braceStart + 1);
            float x = 0, y = 0, z = 0;
            string xs = UnityAgent.ExtractNumberField(obj, "x");
            string ys = UnityAgent.ExtractNumberField(obj, "y");
            string zs = UnityAgent.ExtractNumberField(obj, "z");
            if (xs != null) float.TryParse(xs, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out x);
            if (ys != null) float.TryParse(ys, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out y);
            if (zs != null) float.TryParse(zs, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out z);
            return new Vector3(x, y, z);
        }

        private static UnityAgent.ToolResult Ok(string message)
        {
            return new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"message\":\"{UnityAgent.EscapeJson(message)}\"}}"
            };
        }

        private static UnityAgent.ToolResult Fail(string error)
        {
            return FailResult(error);
        }

        private static UnityAgent.ToolResult FailResult(string error)
        {
            return new UnityAgent.ToolResult
            {
                content = $"{{\"success\":false,\"error\":\"{UnityAgent.EscapeJson(error)}\"}}"
            };
        }

#else
        // Outside Editor: tools are not registered (no [AgentTool] attributes visible)
        void Awake()
        {
            Debug.LogWarning("[AssetTools] Asset tools are only available in the Unity Editor.");
        }
#endif
    }
}
