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
    /// Material creation and modification tools.
    /// Runtime: modify existing materials on GameObjects.
    /// Editor: create new material assets.
    /// Tools: manageMaterial
    /// </summary>
    public class AgentMaterialTools : MonoBehaviour
    {
        // =================================================================
        // Tool: manageMaterial
        // =================================================================

        [AgentTool("manageMaterial",
            "Create a new material or modify an existing material on a GameObject. " +
            "Supports setting color, shader, metallic, smoothness, emission. " +
            "Action 'create' (Editor only) saves a .mat asset. Action 'modify' changes a runtime material.",
            ParametersType = typeof(MaterialParams))]
        private IEnumerator HandleManageMaterial(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "modify";
            string targetName = UnityAgent.ExtractStringField(arguments, "target");
            string shader = UnityAgent.ExtractStringField(arguments, "shader");
            string colorStr = UnityAgent.ExtractStringField(arguments, "color");
            string metallicStr = UnityAgent.ExtractNumberField(arguments, "metallic");
            string smoothnessStr = UnityAgent.ExtractNumberField(arguments, "smoothness");
            string emissionStr = UnityAgent.ExtractStringField(arguments, "emission");
            string renderModeStr = UnityAgent.ExtractStringField(arguments, "renderMode");

            if (action == "create")
            {
#if UNITY_EDITOR
                string savePath = UnityAgent.ExtractStringField(arguments, "savePath");
                string matName = UnityAgent.ExtractStringField(arguments, "name") ?? "NewMaterial";

                var shaderObj = Shader.Find(shader ?? "Standard");
                if (shaderObj == null)
                {
                    callback(AgentToolHelpers.Fail($"Shader '{shader}' not found."));
                    yield break;
                }

                var mat = new Material(shaderObj);
                mat.name = matName;
                ApplyMaterialProperties(mat, colorStr, metallicStr, smoothnessStr, emissionStr, renderModeStr);

                if (string.IsNullOrEmpty(savePath))
                    savePath = $"Assets/Materials/{matName}.mat";
                savePath = AgentToolHelpers.NormalizePath(savePath);

                string dir = System.IO.Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                AssetDatabase.CreateAsset(mat, savePath);
                AssetDatabase.Refresh();
                callback(AgentToolHelpers.Ok($"Material created: {savePath}"));
#else
                callback(AgentToolHelpers.Fail("Material creation requires Unity Editor."));
#endif
                yield break;
            }

            // Modify existing material on a target
            if (action == "list_shaders")
            {
                var sb2 = new StringBuilder();
                sb2.Append("{\"success\":true,\"commonShaders\":[");
                string[] common = {
                    "Standard", "Standard (Specular setup)", "Unlit/Color", "Unlit/Texture",
                    "Unlit/Transparent", "Sprites/Default", "UI/Default",
                    "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit",
                    "Universal Render Pipeline/Simple Lit"
                };
                for (int i = 0; i < common.Length; i++)
                {
                    if (i > 0) sb2.Append(",");
                    sb2.Append("\"").Append(common[i]).Append("\"");
                }
                sb2.Append("]}");
                callback(new UnityAgent.ToolResult { content = sb2.ToString() });
                yield break;
            }

            if (string.IsNullOrEmpty(targetName))
            {
                callback(AgentToolHelpers.Fail("'target' (GameObject name) is required for modify action."));
                yield break;
            }

            var go = GameObject.Find(targetName);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{targetName}' not found."));
                yield break;
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                callback(AgentToolHelpers.Fail($"No Renderer on '{targetName}'."));
                yield break;
            }

            var material = renderer.material;

            if (!string.IsNullOrEmpty(shader))
            {
                var shaderObj = Shader.Find(shader);
                if (shaderObj != null) material.shader = shaderObj;
            }

            ApplyMaterialProperties(material, colorStr, metallicStr, smoothnessStr, emissionStr, renderModeStr);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"target\":\"").Append(UnityAgent.EscapeJson(targetName)).Append("\"");
            sb.Append(",\"shader\":\"").Append(UnityAgent.EscapeJson(material.shader.name)).Append("\"");
            sb.Append(",\"color\":\"").Append(AgentToolHelpers.ColorToHex(material.color)).Append("\"}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class MaterialParams
        {
            [ToolParam("Action: 'modify' (default), 'create' (Editor only), 'list_shaders'.")]
            public string action;
            [ToolParam("Target GameObject name (for modify action).")]
            public string target;
            [ToolParam("Material name (for create action).")]
            public string name;
            [ToolParam("Shader name (e.g. 'Standard', 'Unlit/Color', 'Universal Render Pipeline/Lit').")]
            public string shader;
            [ToolParam("Color as hex '#RRGGBB' or name (red, blue, green, white, black, yellow, etc.).")]
            public string color;
            [ToolParam("Metallic value 0-1 (Standard shader).", SchemaType = "number")]
            public float metallic;
            [ToolParam("Smoothness value 0-1 (Standard shader).", SchemaType = "number")]
            public float smoothness;
            [ToolParam("Emission color as hex. Set to enable emission.")]
            public string emission;
            [ToolParam("Render mode: 'Opaque', 'Cutout', 'Fade', 'Transparent'.")]
            public string renderMode;
            [ToolParam("Save path for create action (relative to Assets/).")]
            public string savePath;
        }

        private static void ApplyMaterialProperties(Material mat, string colorStr,
            string metallicStr, string smoothnessStr, string emissionStr, string renderModeStr)
        {
            if (!string.IsNullOrEmpty(colorStr))
            {
                Color c;
                if (AgentToolHelpers.TryParseColor(colorStr, out c))
                    mat.color = c;
            }

            if (metallicStr != null && mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", Mathf.Clamp01(AgentToolHelpers.ParseFloat(metallicStr)));

            if (smoothnessStr != null && mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", Mathf.Clamp01(AgentToolHelpers.ParseFloat(smoothnessStr)));

            if (!string.IsNullOrEmpty(emissionStr))
            {
                mat.EnableKeyword("_EMISSION");
                Color emColor;
                if (AgentToolHelpers.TryParseColor(emissionStr, out emColor))
                    mat.SetColor("_EmissionColor", emColor);
            }

            if (!string.IsNullOrEmpty(renderModeStr))
            {
                switch (renderModeStr.ToLower())
                {
                    case "opaque":
                        mat.SetFloat("_Mode", 0);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                        mat.SetInt("_ZWrite", 1);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.DisableKeyword("_ALPHABLEND_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = -1;
                        break;
                    case "transparent":
                        mat.SetFloat("_Mode", 3);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.DisableKeyword("_ALPHABLEND_ON");
                        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = 3000;
                        break;
                    case "fade":
                        mat.SetFloat("_Mode", 2);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = 3000;
                        break;
                    case "cutout":
                        mat.SetFloat("_Mode", 1);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                        mat.SetInt("_ZWrite", 1);
                        mat.EnableKeyword("_ALPHATEST_ON");
                        mat.DisableKeyword("_ALPHABLEND_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = 2450;
                        break;
                }
            }
        }
    }
}
