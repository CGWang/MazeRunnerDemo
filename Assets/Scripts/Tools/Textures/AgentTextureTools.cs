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
    /// Procedural texture generation and texture management tools.
    /// Supports creating textures, sprites, patterns (checkerboard, stripes, dots, grid),
    /// gradients (horizontal/vertical), Perlin noise, and modifying texture import settings.
    /// Tools: manageTexture
    /// </summary>
    public class AgentTextureTools : MonoBehaviour
    {
#if UNITY_EDITOR
        private const int MaxTextureDimension = 1024;
        private const int MaxTexturePixels = 1024 * 1024;

        // =================================================================
        // Tool: manageTexture
        // =================================================================

        [AgentTool("manageTexture",
            "Create, modify, or inspect textures. " +
            "Actions: 'create' (Texture2D saved as PNG), 'create_sprite' (sprite texture), " +
            "'modify' (change import settings), 'apply_pattern' (checkerboard/stripes/dots/grid), " +
            "'apply_gradient' (horizontal/vertical gradient), 'apply_noise' (Perlin noise), " +
            "'get_info' (texture info).",
            ParametersType = typeof(TextureParams))]
        private IEnumerator HandleManageTexture(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "create";
            string path = UnityAgent.ExtractStringField(arguments, "path");

            switch (action)
            {
                case "create":
                    yield return CreateTexture(arguments, path, false, callback);
                    break;
                case "create_sprite":
                    yield return CreateTexture(arguments, path, true, callback);
                    break;
                case "modify":
                    yield return ModifyTextureImportSettings(arguments, path, callback);
                    break;
                case "apply_pattern":
                    yield return ApplyPattern(arguments, path, callback);
                    break;
                case "apply_gradient":
                    yield return ApplyGradient(arguments, path, callback);
                    break;
                case "apply_noise":
                    yield return ApplyNoise(arguments, path, callback);
                    break;
                case "get_info":
                    yield return GetTextureInfo(path, callback);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Valid: create, create_sprite, modify, apply_pattern, apply_gradient, apply_noise, get_info."));
                    break;
            }
        }

        // -----------------------------------------------------------------
        // create / create_sprite
        // -----------------------------------------------------------------
        private IEnumerator CreateTexture(string arguments, string path, bool asSprite,
            Action<UnityAgent.ToolResult> callback)
        {
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for create."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);

            int width = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "width"), 64);
            int height = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "height"), 64);

            if (width <= 0 || height <= 0)
            {
                callback(AgentToolHelpers.Fail($"Invalid dimensions: {width}x{height}. Must be positive."));
                yield break;
            }
            if (width > MaxTextureDimension || height > MaxTextureDimension ||
                (long)width * height > MaxTexturePixels)
            {
                callback(AgentToolHelpers.Fail(
                    $"Dimensions {width}x{height} exceed limits (max {MaxTextureDimension}px per side, {MaxTexturePixels} total pixels)."));
                yield break;
            }

            string fillColor = UnityAgent.ExtractStringField(arguments, "fillColor");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                // Fill with color or transparent
                Color fill = Color.clear;
                if (!string.IsNullOrEmpty(fillColor))
                    AgentToolHelpers.TryParseColor(fillColor, out fill);
                FillTexture(texture, fill);

                texture.Apply();

                byte[] pngData = texture.EncodeToPNG();
                DestroyImmediate(texture);

                if (pngData == null || pngData.Length == 0)
                {
                    callback(AgentToolHelpers.Fail("Failed to encode texture to PNG."));
                    yield break;
                }

                File.WriteAllBytes(path, pngData);
                AssetDatabase.ImportAsset(ToAssetPath(path), ImportAssetOptions.ForceUpdate);

                if (asSprite)
                {
                    ConfigureAsSprite(ToAssetPath(path));
                }

                callback(AgentToolHelpers.Ok(
                    $"Texture created at '{ToAssetPath(path)}' ({width}x{height})" +
                    (asSprite ? " as sprite" : "")));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to create texture: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // modify (import settings)
        // -----------------------------------------------------------------
        private IEnumerator ModifyTextureImportSettings(string arguments, string path,
            Action<UnityAgent.ToolResult> callback)
        {
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for modify."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);
            string assetPath = ToAssetPath(path);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                callback(AgentToolHelpers.Fail($"Texture not found or not importable at: {assetPath}"));
                yield break;
            }

            try
            {
                var changes = new List<string>();

                string textureType = UnityAgent.ExtractStringField(arguments, "textureType");
                if (!string.IsNullOrEmpty(textureType))
                {
                    if (Enum.TryParse<TextureImporterType>(textureType, true, out var tt))
                    {
                        importer.textureType = tt;
                        changes.Add("textureType");
                    }
                }

                string filterMode = UnityAgent.ExtractStringField(arguments, "filterMode");
                if (!string.IsNullOrEmpty(filterMode))
                {
                    if (Enum.TryParse<FilterMode>(filterMode, true, out var fm))
                    {
                        importer.filterMode = fm;
                        changes.Add("filterMode");
                    }
                }

                string wrapMode = UnityAgent.ExtractStringField(arguments, "wrapMode");
                if (!string.IsNullOrEmpty(wrapMode))
                {
                    if (Enum.TryParse<TextureWrapMode>(wrapMode, true, out var wm))
                    {
                        importer.wrapMode = wm;
                        changes.Add("wrapMode");
                    }
                }

                string maxSizeStr = UnityAgent.ExtractNumberField(arguments, "maxTextureSize");
                if (maxSizeStr != null)
                {
                    importer.maxTextureSize = AgentToolHelpers.ParseInt(maxSizeStr, importer.maxTextureSize);
                    changes.Add("maxTextureSize");
                }

                string readableStr = UnityAgent.ExtractStringField(arguments, "isReadable");
                if (!string.IsNullOrEmpty(readableStr))
                {
                    importer.isReadable = readableStr.ToLower() == "true";
                    changes.Add("isReadable");
                }

                string mipmapStr = UnityAgent.ExtractStringField(arguments, "mipmapEnabled");
                if (!string.IsNullOrEmpty(mipmapStr))
                {
                    importer.mipmapEnabled = mipmapStr.ToLower() == "true";
                    changes.Add("mipmapEnabled");
                }

                string compression = UnityAgent.ExtractStringField(arguments, "textureCompression");
                if (!string.IsNullOrEmpty(compression))
                {
                    if (Enum.TryParse<TextureImporterCompression>(compression, true, out var tc))
                    {
                        importer.textureCompression = tc;
                        changes.Add("textureCompression");
                    }
                }

                string srgbStr = UnityAgent.ExtractStringField(arguments, "sRGBTexture");
                if (!string.IsNullOrEmpty(srgbStr))
                {
                    importer.sRGBTexture = srgbStr.ToLower() == "true";
                    changes.Add("sRGBTexture");
                }

                string anisoStr = UnityAgent.ExtractNumberField(arguments, "anisoLevel");
                if (anisoStr != null)
                {
                    importer.anisoLevel = AgentToolHelpers.ParseInt(anisoStr, importer.anisoLevel);
                    changes.Add("anisoLevel");
                }

                string ppuStr = UnityAgent.ExtractNumberField(arguments, "spritePixelsPerUnit");
                if (ppuStr != null)
                {
                    importer.spritePixelsPerUnit = AgentToolHelpers.ParseFloat(ppuStr);
                    changes.Add("spritePixelsPerUnit");
                }

                if (changes.Count == 0)
                {
                    callback(AgentToolHelpers.Fail("No valid import settings provided to modify."));
                    yield break;
                }

                importer.SaveAndReimport();

                callback(AgentToolHelpers.Ok(
                    $"Texture import settings modified at '{assetPath}'. Changed: {string.Join(", ", changes)}"));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to modify texture: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // apply_pattern
        // -----------------------------------------------------------------
        private IEnumerator ApplyPattern(string arguments, string path,
            Action<UnityAgent.ToolResult> callback)
        {
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for apply_pattern."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);

            int width = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "width"), 64);
            int height = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "height"), 64);
            string pattern = UnityAgent.ExtractStringField(arguments, "pattern") ?? "checkerboard";
            int patternSize = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "patternSize"), 8);
            string color1Str = UnityAgent.ExtractStringField(arguments, "color1");
            string color2Str = UnityAgent.ExtractStringField(arguments, "color2");

            if (ExceedsLimits(width, height))
            {
                callback(AgentToolHelpers.Fail(
                    $"Dimensions {width}x{height} exceed limits (max {MaxTextureDimension}px, {MaxTexturePixels} total)."));
                yield break;
            }
            if (patternSize <= 0)
            {
                callback(AgentToolHelpers.Fail("patternSize must be greater than 0."));
                yield break;
            }

            Color color1 = Color.white;
            Color color2 = Color.black;
            if (!string.IsNullOrEmpty(color1Str))
                AgentToolHelpers.TryParseColor(color1Str, out color1);
            if (!string.IsNullOrEmpty(color2Str))
                AgentToolHelpers.TryParseColor(color2Str, out color2);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                ApplyPatternToTexture(texture, pattern, color1, color2, patternSize);
                texture.Apply();

                byte[] pngData = texture.EncodeToPNG();
                DestroyImmediate(texture);

                File.WriteAllBytes(path, pngData);
                AssetDatabase.ImportAsset(ToAssetPath(path), ImportAssetOptions.ForceUpdate);

                callback(AgentToolHelpers.Ok(
                    $"Pattern '{pattern}' texture created at '{ToAssetPath(path)}' ({width}x{height})."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to apply pattern: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // apply_gradient
        // -----------------------------------------------------------------
        private IEnumerator ApplyGradient(string arguments, string path,
            Action<UnityAgent.ToolResult> callback)
        {
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for apply_gradient."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);

            int width = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "width"), 64);
            int height = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "height"), 64);
            string direction = UnityAgent.ExtractStringField(arguments, "direction") ?? "horizontal";
            string startColorStr = UnityAgent.ExtractStringField(arguments, "startColor");
            string endColorStr = UnityAgent.ExtractStringField(arguments, "endColor");

            if (ExceedsLimits(width, height))
            {
                callback(AgentToolHelpers.Fail(
                    $"Dimensions {width}x{height} exceed limits (max {MaxTextureDimension}px, {MaxTexturePixels} total)."));
                yield break;
            }

            Color startColor = Color.black;
            Color endColor = Color.white;
            if (!string.IsNullOrEmpty(startColorStr))
                AgentToolHelpers.TryParseColor(startColorStr, out startColor);
            if (!string.IsNullOrEmpty(endColorStr))
                AgentToolHelpers.TryParseColor(endColorStr, out endColor);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float t;
                        if (direction == "vertical")
                            t = (float)y / Mathf.Max(1, height - 1);
                        else
                            t = (float)x / Mathf.Max(1, width - 1);

                        Color c = Color.Lerp(startColor, endColor, t);
                        texture.SetPixel(x, y, c);
                    }
                }

                texture.Apply();

                byte[] pngData = texture.EncodeToPNG();
                DestroyImmediate(texture);

                File.WriteAllBytes(path, pngData);
                AssetDatabase.ImportAsset(ToAssetPath(path), ImportAssetOptions.ForceUpdate);

                callback(AgentToolHelpers.Ok(
                    $"Gradient ({direction}) texture created at '{ToAssetPath(path)}' ({width}x{height})."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to create gradient texture: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // apply_noise
        // -----------------------------------------------------------------
        private IEnumerator ApplyNoise(string arguments, string path,
            Action<UnityAgent.ToolResult> callback)
        {
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for apply_noise."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);

            int width = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "width"), 64);
            int height = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "height"), 64);
            float scale = AgentToolHelpers.ParseFloat(
                UnityAgent.ExtractNumberField(arguments, "noiseScale") ?? "0.1");
            int octaves = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "octaves"), 1);
            string color1Str = UnityAgent.ExtractStringField(arguments, "color1");
            string color2Str = UnityAgent.ExtractStringField(arguments, "color2");

            if (ExceedsLimits(width, height))
            {
                callback(AgentToolHelpers.Fail(
                    $"Dimensions {width}x{height} exceed limits (max {MaxTextureDimension}px, {MaxTexturePixels} total)."));
                yield break;
            }
            if (octaves <= 0)
            {
                callback(AgentToolHelpers.Fail("octaves must be greater than 0."));
                yield break;
            }

            Color color1 = Color.black;
            Color color2 = Color.white;
            if (!string.IsNullOrEmpty(color1Str))
                AgentToolHelpers.TryParseColor(color1Str, out color1);
            if (!string.IsNullOrEmpty(color2Str))
                AgentToolHelpers.TryParseColor(color2Str, out color2);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                float offsetX = UnityEngine.Random.Range(0f, 1000f);
                float offsetY = UnityEngine.Random.Range(0f, 1000f);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float noiseValue = 0f;
                        float amplitude = 1f;
                        float frequency = 1f;
                        float maxValue = 0f;

                        for (int o = 0; o < octaves; o++)
                        {
                            float sampleX = (x + offsetX) * scale * frequency;
                            float sampleY = (y + offsetY) * scale * frequency;
                            noiseValue += Mathf.PerlinNoise(sampleX, sampleY) * amplitude;
                            maxValue += amplitude;
                            amplitude *= 0.5f;
                            frequency *= 2f;
                        }

                        float t = Mathf.Clamp01(noiseValue / maxValue);
                        Color c = Color.Lerp(color1, color2, t);
                        texture.SetPixel(x, y, c);
                    }
                }

                texture.Apply();

                byte[] pngData = texture.EncodeToPNG();
                DestroyImmediate(texture);

                File.WriteAllBytes(path, pngData);
                AssetDatabase.ImportAsset(ToAssetPath(path), ImportAssetOptions.ForceUpdate);

                callback(AgentToolHelpers.Ok(
                    $"Noise texture created at '{ToAssetPath(path)}' ({width}x{height}, scale={scale}, octaves={octaves})."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to create noise texture: {e.Message}"));
            }

            yield break;
        }

        // -----------------------------------------------------------------
        // get_info
        // -----------------------------------------------------------------
        private IEnumerator GetTextureInfo(string path, Action<UnityAgent.ToolResult> callback)
        {
            if (string.IsNullOrEmpty(path))
            {
                callback(AgentToolHelpers.Fail("'path' is required for get_info."));
                yield break;
            }

            path = AgentToolHelpers.NormalizePath(path);
            string assetPath = ToAssetPath(path);

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                callback(AgentToolHelpers.Fail($"Texture not found at: {assetPath}"));
                yield break;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"path\":\"").Append(UnityAgent.EscapeJson(assetPath)).Append("\"");
            sb.Append(",\"width\":").Append(texture.width);
            sb.Append(",\"height\":").Append(texture.height);
            sb.Append(",\"format\":\"").Append(texture.format.ToString()).Append("\"");
            sb.Append(",\"mipmapCount\":").Append(texture.mipmapCount);
            sb.Append(",\"isReadable\":").Append(texture.isReadable ? "true" : "false");

            if (importer != null)
            {
                sb.Append(",\"textureType\":\"").Append(importer.textureType.ToString()).Append("\"");
                sb.Append(",\"filterMode\":\"").Append(importer.filterMode.ToString()).Append("\"");
                sb.Append(",\"wrapMode\":\"").Append(importer.wrapMode.ToString()).Append("\"");
                sb.Append(",\"maxTextureSize\":").Append(importer.maxTextureSize);
                sb.Append(",\"textureCompression\":\"").Append(importer.textureCompression.ToString()).Append("\"");
                sb.Append(",\"sRGBTexture\":").Append(importer.sRGBTexture ? "true" : "false");
                sb.Append(",\"spriteImportMode\":\"").Append(importer.spriteImportMode.ToString()).Append("\"");
                sb.Append(",\"spritePixelsPerUnit\":").Append(importer.spritePixelsPerUnit);
            }

            sb.Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static bool ExceedsLimits(int width, int height)
        {
            return width <= 0 || height <= 0 ||
                   width > MaxTextureDimension || height > MaxTextureDimension ||
                   (long)width * height > MaxTexturePixels;
        }

        private static void FillTexture(Texture2D texture, Color color)
        {
            var pixels = new Color[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            texture.SetPixels(pixels);
        }

        private static void ApplyPatternToTexture(Texture2D texture, string pattern,
            Color color1, Color color2, int patternSize)
        {
            int w = texture.width;
            int h = texture.height;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c;
                    switch (pattern.ToLower())
                    {
                        case "checkerboard":
                            c = ((x / patternSize) + (y / patternSize)) % 2 == 0 ? color1 : color2;
                            break;

                        case "stripes":
                        case "stripes_v":
                            c = (x / patternSize) % 2 == 0 ? color1 : color2;
                            break;

                        case "stripes_h":
                            c = (y / patternSize) % 2 == 0 ? color1 : color2;
                            break;

                        case "dots":
                            int cx = (x % (patternSize * 2)) - patternSize;
                            int cy = (y % (patternSize * 2)) - patternSize;
                            bool inDot = (cx * cx + cy * cy) < (patternSize * patternSize / 4);
                            c = inDot ? color2 : color1;
                            break;

                        case "grid":
                            bool onLine = (x % patternSize == 0) || (y % patternSize == 0);
                            c = onLine ? color2 : color1;
                            break;

                        default:
                            c = color1;
                            break;
                    }

                    texture.SetPixel(x, y, c);
                }
            }
        }

        private static void ConfigureAsSprite(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        private static string ToAssetPath(string fullPath) => AgentToolHelpers.ToAssetPath(fullPath);
#endif

        // =================================================================
        // Parameter class
        // =================================================================

        public class TextureParams
        {
            [ToolParam("Action: 'create', 'create_sprite', 'modify', 'apply_pattern', 'apply_gradient', 'apply_noise', 'get_info'.", required: true)]
            public string action;
            [ToolParam("Asset path relative to Assets/ (e.g. 'Assets/Textures/MyTex.png').", required: true)]
            public string path;
            [ToolParam("Texture width in pixels.", SchemaType = "number")]
            public int width;
            [ToolParam("Texture height in pixels.", SchemaType = "number")]
            public int height;
            [ToolParam("Fill color as hex '#RRGGBB' or name (for create action).")]
            public string fillColor;
            [ToolParam("Pattern type: 'checkerboard', 'stripes', 'stripes_h', 'stripes_v', 'dots', 'grid'.")]
            public string pattern;
            [ToolParam("Pattern cell size in pixels.", SchemaType = "number")]
            public int patternSize;
            [ToolParam("First color as hex '#RRGGBB' or name (for pattern/noise).")]
            public string color1;
            [ToolParam("Second color as hex '#RRGGBB' or name (for pattern/noise).")]
            public string color2;
            [ToolParam("Gradient direction: 'horizontal' or 'vertical'.")]
            public string direction;
            [ToolParam("Start color for gradient as hex.")]
            public string startColor;
            [ToolParam("End color for gradient as hex.")]
            public string endColor;
            [ToolParam("Perlin noise scale.", SchemaType = "number")]
            public float noiseScale;
            [ToolParam("Number of noise octaves.", SchemaType = "number")]
            public int octaves;
            [ToolParam("Texture type for modify: 'Default', 'Sprite', 'NormalMap', etc.")]
            public string textureType;
            [ToolParam("Filter mode: 'Point', 'Bilinear', 'Trilinear'.")]
            public string filterMode;
            [ToolParam("Wrap mode: 'Repeat', 'Clamp', 'Mirror', 'MirrorOnce'.")]
            public string wrapMode;
            [ToolParam("Max texture size for import.", SchemaType = "number")]
            public int maxTextureSize;
            [ToolParam("Texture compression: 'Uncompressed', 'Compressed', 'CompressedHQ', 'CompressedLQ'.")]
            public string textureCompression;
            [ToolParam("Whether the texture is readable at runtime.")]
            public string isReadable;
            [ToolParam("Whether mipmaps are enabled.")]
            public string mipmapEnabled;
            [ToolParam("Whether texture uses sRGB color space.")]
            public string sRGBTexture;
            [ToolParam("Anisotropic filtering level.", SchemaType = "number")]
            public int anisoLevel;
            [ToolParam("Sprite pixels per unit.", SchemaType = "number")]
            public float spritePixelsPerUnit;
        }
    }
}
