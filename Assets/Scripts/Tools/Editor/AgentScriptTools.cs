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
    /// Script file management tools (Editor-only for create/edit, read works at runtime).
    /// Tools: createScript, readScript, editScript
    /// </summary>
    public class AgentScriptTools : MonoBehaviour
    {
        // =================================================================
        // Tool: readScript (works at runtime too)
        // =================================================================

        [AgentTool("readScript",
            "Read the contents of a script or text file in the project. Returns the file content " +
            "and line count. Path is relative to Assets/.",
            ParametersType = typeof(ReadScriptParams))]
        private IEnumerator HandleReadScript(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required."));
                yield break;
            }

            if (!File.Exists(path))
            {
                callback(AgentToolHelpers.Fail($"File not found: {path}"));
                yield break;
            }

            string[] lines = File.ReadAllLines(path);
            int lineStart = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "lineStart"), 1);
            int lineEnd = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "lineEnd"), lines.Length);

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

#if UNITY_EDITOR
        // =================================================================
        // Tool: createScript
        // =================================================================

        [AgentTool("createScript",
            "Create a new C# script file in the project. Provide the file path (relative to Assets/) " +
            "and the full script content. The file will be created and AssetDatabase refreshed.",
            ParametersType = typeof(CreateScriptParams),
            RequiresPermission = true)]
        private IEnumerator HandleCreateScript(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            string content = UnityAgent.ExtractStringField(arguments, "content");
            string template = UnityAgent.ExtractStringField(arguments, "template");

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required."));
                yield break;
            }

            if (!path.EndsWith(".cs"))
                path += ".cs";

            if (File.Exists(path))
            {
                callback(AgentToolHelpers.Fail($"File already exists: {path}. Use editScript to modify it."));
                yield break;
            }

            if (string.IsNullOrEmpty(content))
            {
                string className = Path.GetFileNameWithoutExtension(path);
                content = GenerateScriptFromTemplate(className, template ?? "monobehaviour");
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, content);
            AssetDatabase.Refresh();

            callback(AgentToolHelpers.Ok($"Script created: {path} ({content.Length} chars)."));
            yield break;
        }

        public class CreateScriptParams
        {
            [ToolParam("File path relative to Assets/ (e.g. 'Scripts/MyScript.cs').", required: true)]
            public string path;
            [ToolParam("Full C# source code content. If omitted, uses a template.")]
            public string content;
            [ToolParam("Template type: 'monobehaviour', 'scriptableobject', 'editor', 'static', 'interface'. Used only if content is empty.")]
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
                case "interface":
                    return $"public interface {className}\n{{\n    \n}}\n";
                default:
                    return $"using UnityEngine;\n\npublic class {className} : MonoBehaviour\n{{\n    void Start()\n    {{\n        \n    }}\n\n    void Update()\n    {{\n        \n    }}\n}}\n";
            }
        }

        // =================================================================
        // Tool: editScript
        // =================================================================

        [AgentTool("editScript",
            "Edit a script file by replacing a specific text pattern with new text. " +
            "The old_text must be an exact match (including whitespace). " +
            "Use action='append' to add text at end, 'insert_line' to insert at a line number, " +
            "'delete_lines' to remove a range of lines.",
            ParametersType = typeof(EditScriptParams),
            RequiresPermission = true)]
        private IEnumerator HandleEditScript(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "replace";
            string oldText = UnityAgent.ExtractStringField(arguments, "old_text");
            string newText = UnityAgent.ExtractStringField(arguments, "new_text");
            int line = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "line"), 1);
            int lineEnd = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "lineEnd"), 0);

            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required."));
                yield break;
            }

            if (!File.Exists(path))
            {
                callback(AgentToolHelpers.Fail($"File not found: {path}"));
                yield break;
            }

            string content = File.ReadAllText(path);

            switch (action.ToLower())
            {
                case "replace":
                    if (string.IsNullOrEmpty(oldText))
                    {
                        callback(AgentToolHelpers.Fail("'old_text' is required for replace action."));
                        yield break;
                    }
                    if (!content.Contains(oldText))
                    {
                        callback(AgentToolHelpers.Fail("old_text not found in file. Ensure exact match including whitespace."));
                        yield break;
                    }
                    int occurrences = CountOccurrences(content, oldText);
                    content = content.Replace(oldText, newText ?? "");
                    File.WriteAllText(path, content);
                    AssetDatabase.Refresh();
                    callback(AgentToolHelpers.Ok($"Replaced {occurrences} occurrence(s) in {path}."));
                    break;

                case "append":
                    content += "\n" + (newText ?? "");
                    File.WriteAllText(path, content);
                    AssetDatabase.Refresh();
                    callback(AgentToolHelpers.Ok($"Appended text to {path}."));
                    break;

                case "insert_line":
                    var lines = new System.Collections.Generic.List<string>(content.Split('\n'));
                    line = Mathf.Clamp(line, 1, lines.Count + 1);
                    lines.Insert(line - 1, newText ?? "");
                    File.WriteAllText(path, string.Join("\n", lines));
                    AssetDatabase.Refresh();
                    callback(AgentToolHelpers.Ok($"Inserted text at line {line} in {path}."));
                    break;

                case "delete_lines":
                    var allLines = new System.Collections.Generic.List<string>(content.Split('\n'));
                    line = Mathf.Clamp(line, 1, allLines.Count);
                    if (lineEnd <= 0) lineEnd = line;
                    lineEnd = Mathf.Clamp(lineEnd, line, allLines.Count);
                    allLines.RemoveRange(line - 1, lineEnd - line + 1);
                    File.WriteAllText(path, string.Join("\n", allLines));
                    AssetDatabase.Refresh();
                    callback(AgentToolHelpers.Ok($"Deleted lines {line}-{lineEnd} in {path}."));
                    break;

                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: replace, append, insert_line, delete_lines."));
                    break;
            }

            yield break;
        }

        public class EditScriptParams
        {
            [ToolParam("File path relative to Assets/.", required: true)]
            public string path;
            [ToolParam("Edit action: 'replace' (default), 'append', 'insert_line', 'delete_lines'.")]
            public string action;
            [ToolParam("Exact text to find and replace (for 'replace' action).")]
            public string old_text;
            [ToolParam("New text to insert or replacement text.")]
            public string new_text;
            [ToolParam("Line number for insert_line / delete_lines (1-based).", SchemaType = "integer")]
            public int line;
            [ToolParam("End line number for delete_lines (inclusive).", SchemaType = "integer")]
            public int lineEnd;
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
#endif
    }
}
