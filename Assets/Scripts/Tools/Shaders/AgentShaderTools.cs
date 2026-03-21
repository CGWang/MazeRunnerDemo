using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Shader file management tools (Editor-only).
    /// Supports creating, reading, updating, deleting, validating, and listing shaders.
    /// Tools: manageShader
    /// </summary>
    public class AgentShaderTools : MonoBehaviour
    {
#if UNITY_EDITOR
        // =================================================================
        // Tool: manageShader
        // =================================================================

        [AgentTool("manageShader",
            "Create, read, update, delete, validate, or list shader files. " +
            "Actions: 'create' (new shader from template or content), 'read' (shader source), " +
            "'update' (overwrite shader), 'delete' (remove shader file), " +
            "'validate' (check compilation errors), 'list' (available shaders).",
            ParametersType = typeof(ShaderParams))]
        private IEnumerator HandleManageShader(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "create";
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string path = UnityAgent.ExtractStringField(arguments, "path");
            string contents = UnityAgent.ExtractStringField(arguments, "contents");

            switch (action)
            {
                case "create":
                    yield return CreateShader(name, path, contents, callback);
                    break;
                case "read":
                    yield return ReadShader(name, path, callback);
                    break;
                case "update":
                    yield return UpdateShader(name, path, contents, callback);
                    break;
                case "delete":
                    yield return DeleteShader(name, path, callback);
                    break;
                case "validate":
                    yield return ValidateShader(name, path, callback);
                    break;
                case "list":
                    yield return ListShaders(path, callback);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Valid: create, read, update, delete, validate, list."));
                    break;
            }
        }

        // -----------------------------------------------------------------
        // create
        // -----------------------------------------------------------------
        private IEnumerator CreateShader(string name, string path, string contents,
            Action<UnityAgent.ToolResult> callback)
        {
            if (string.IsNullOrEmpty(name))
            {
                callback(AgentToolHelpers.Fail("'name' is required for create."));
                yield break;
            }

            if (!Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_/]*$"))
            {
                callback(AgentToolHelpers.Fail(
                    $"Invalid shader name: '{name}'. Use letters, numbers, underscores, and forward slashes."));
                yield break;
            }

            string relativeDir = !string.IsNullOrEmpty(path) ? path : "Assets/Shaders";
            relativeDir = AgentToolHelpers.NormalizePath(relativeDir);
            string assetPath = ToAssetPath(relativeDir);

            // Build the safe file name from the last segment of the shader name
            string safeName = name.Replace("/", "_");
            string shaderFileName = $"{safeName}.shader";
            string fullDir = relativeDir;
            if (!fullDir.EndsWith("/") && !fullDir.EndsWith("\\"))
                fullDir += "/";
            string fullPath = Path.Combine(relativeDir, shaderFileName);
            string fullAssetPath = ToAssetPath(fullPath);

            if (File.Exists(fullPath))
            {
                callback(AgentToolHelpers.Fail(
                    $"Shader already exists at '{fullAssetPath}'. Use 'update' to modify."));
                yield break;
            }

            if (string.IsNullOrEmpty(contents))
            {
                contents = GenerateDefaultShader(name);
            }

            try
            {
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(fullPath, contents, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(fullAssetPath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                callback(AgentToolHelpers.Ok(
                    $"Shader '{name}' created at '{fullAssetPath}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to create shader: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // read
        // -----------------------------------------------------------------
        private IEnumerator ReadShader(string name, string path, Action<UnityAgent.ToolResult> callback)
        {
            string fullPath = ResolveShaderPath(name, path);
            if (fullPath == null)
            {
                callback(AgentToolHelpers.Fail("'name' or 'path' is required for read."));
                yield break;
            }

            if (!File.Exists(fullPath))
            {
                callback(AgentToolHelpers.Fail($"Shader not found at '{ToAssetPath(fullPath)}'."));
                yield break;
            }

            try
            {
                string source = File.ReadAllText(fullPath);
                int lineCount = source.Split('\n').Length;

                var sb = new StringBuilder();
                sb.Append("{\"success\":true,\"path\":\"").Append(UnityAgent.EscapeJson(ToAssetPath(fullPath))).Append("\"");
                sb.Append(",\"lineCount\":").Append(lineCount);
                sb.Append(",\"contents\":\"").Append(UnityAgent.EscapeJson(source)).Append("\"");
                sb.Append("}");

                callback(new UnityAgent.ToolResult { content = sb.ToString() });
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to read shader: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // update
        // -----------------------------------------------------------------
        private IEnumerator UpdateShader(string name, string path, string contents,
            Action<UnityAgent.ToolResult> callback)
        {
            string fullPath = ResolveShaderPath(name, path);
            if (fullPath == null)
            {
                callback(AgentToolHelpers.Fail("'name' or 'path' is required for update."));
                yield break;
            }

            if (!File.Exists(fullPath))
            {
                callback(AgentToolHelpers.Fail(
                    $"Shader not found at '{ToAssetPath(fullPath)}'. Use 'create' to add a new shader."));
                yield break;
            }

            if (string.IsNullOrEmpty(contents))
            {
                callback(AgentToolHelpers.Fail("'contents' is required for update."));
                yield break;
            }

            try
            {
                File.WriteAllText(fullPath, contents, new UTF8Encoding(false));
                string assetPath = ToAssetPath(fullPath);
                AssetDatabase.ImportAsset(assetPath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                callback(AgentToolHelpers.Ok($"Shader updated at '{assetPath}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to update shader: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // delete
        // -----------------------------------------------------------------
        private IEnumerator DeleteShader(string name, string path, Action<UnityAgent.ToolResult> callback)
        {
            string fullPath = ResolveShaderPath(name, path);
            if (fullPath == null)
            {
                callback(AgentToolHelpers.Fail("'name' or 'path' is required for delete."));
                yield break;
            }

            if (!File.Exists(fullPath))
            {
                callback(AgentToolHelpers.Fail($"Shader not found at '{ToAssetPath(fullPath)}'."));
                yield break;
            }

            try
            {
                string assetPath = ToAssetPath(fullPath);
                bool success = AssetDatabase.DeleteAsset(assetPath);
                if (!success)
                {
                    callback(AgentToolHelpers.Fail($"Failed to delete shader via AssetDatabase: '{assetPath}'."));
                    yield break;
                }

                // Fallback if file still exists
                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                callback(AgentToolHelpers.Ok($"Shader deleted: '{assetPath}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to delete shader: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // validate
        // -----------------------------------------------------------------
        private IEnumerator ValidateShader(string name, string path, Action<UnityAgent.ToolResult> callback)
        {
            string fullPath = ResolveShaderPath(name, path);
            if (fullPath == null)
            {
                callback(AgentToolHelpers.Fail("'name' or 'path' is required for validate."));
                yield break;
            }

            string assetPath = ToAssetPath(fullPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            if (shader == null)
            {
                callback(AgentToolHelpers.Fail($"Shader not found at '{assetPath}'."));
                yield break;
            }

            bool hasErrors = ShaderUtil.ShaderHasError(shader);
            int errorCount = ShaderUtil.GetShaderMessageCount(shader);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"path\":\"").Append(UnityAgent.EscapeJson(assetPath)).Append("\"");
            sb.Append(",\"shaderName\":\"").Append(UnityAgent.EscapeJson(shader.name)).Append("\"");
            sb.Append(",\"hasErrors\":").Append(hasErrors ? "true" : "false");
            sb.Append(",\"messageCount\":").Append(errorCount);
            sb.Append(",\"isSupported\":").Append(shader.isSupported ? "true" : "false");
            sb.Append(",\"renderQueue\":").Append(shader.renderQueue);
            sb.Append(",\"passCount\":").Append(shader.passCount);

            if (errorCount > 0)
            {
                sb.Append(",\"messages\":[");
                for (int i = 0; i < errorCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    string msg = ShaderUtil.GetShaderMessage(shader, i);
                    var severity = ShaderUtil.GetShaderMessageSeverity(shader, i);
                    sb.Append("{\"message\":\"").Append(UnityAgent.EscapeJson(msg)).Append("\"");
                    sb.Append(",\"severity\":\"").Append(severity.ToString()).Append("\"}");
                }
                sb.Append("]");
            }

            sb.Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // -----------------------------------------------------------------
        // list
        // -----------------------------------------------------------------
        private IEnumerator ListShaders(string path, Action<UnityAgent.ToolResult> callback)
        {
            string searchFolder = !string.IsNullOrEmpty(path) ? path : "Assets";
            searchFolder = AgentToolHelpers.NormalizePath(searchFolder);
            string assetFolder = ToAssetPath(searchFolder);

            string[] guids = AssetDatabase.FindAssets("t:Shader",
                AssetDatabase.IsValidFolder(assetFolder) ? new[] { assetFolder } : null);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"count\":").Append(guids.Length).Append(",\"shaders\":[");

            for (int i = 0; i < guids.Length; i++)
            {
                if (i > 0) sb.Append(",");
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                Shader s = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                sb.Append("{\"path\":\"").Append(UnityAgent.EscapeJson(assetPath)).Append("\"");
                if (s != null)
                    sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(s.name)).Append("\"");
                sb.Append("}");
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static string ResolveShaderPath(string name, string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                string normalized = AgentToolHelpers.NormalizePath(path);
                if (!normalized.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                {
                    // path is a directory, name provides the filename
                    if (!string.IsNullOrEmpty(name))
                    {
                        string safeName = name.Replace("/", "_");
                        return Path.Combine(normalized, $"{safeName}.shader");
                    }
                }
                return normalized;
            }

            if (!string.IsNullOrEmpty(name))
            {
                string safeName = name.Replace("/", "_");
                return Path.Combine("Assets", "Shaders", $"{safeName}.shader");
            }

            return null;
        }

        private static string ToAssetPath(string fullPath)
        {
            fullPath = fullPath.Replace('\\', '/');
            int idx = fullPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return fullPath.Substring(idx);
            return fullPath;
        }

        private static string GenerateDefaultShader(string name)
        {
            return @"Shader """ + name + @"""
{
    Properties
    {
        _MainTex (""Texture"", 2D) = ""white"" {}
        _Color (""Color"", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                return col;
            }
            ENDCG
        }
    }
}";
        }
#endif

        // =================================================================
        // Parameter class
        // =================================================================

        public class ShaderParams
        {
            [ToolParam("Action: 'create', 'read', 'update', 'delete', 'validate', 'list'.", required: true)]
            public string action;
            [ToolParam("Shader name (e.g. 'Custom/MyShader'). Used as shader identifier and filename.")]
            public string name;
            [ToolParam("Directory or file path (relative to Assets/). Defaults to 'Assets/Shaders'.")]
            public string path;
            [ToolParam("Shader source code content (for create/update). If omitted on create, a default template is used.")]
            public string contents;
        }
    }
}
