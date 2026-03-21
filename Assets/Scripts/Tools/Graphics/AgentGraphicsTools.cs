using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Graphics, rendering pipeline, lighting, and environment tools.
    /// Maps to unity-mcp manage_graphics: volume, bake, skybox, pipeline, stats.
    /// </summary>
    public class AgentGraphicsTools : MonoBehaviour
    {
#if UNITY_EDITOR

        [AgentTool("manageGraphics",
            "Manage Unity graphics, rendering, lighting, and environment. " +
            "Actions: 'volume_create', 'volume_add_effect', 'volume_get_info', 'volume_list_effects', " +
            "'bake_start', 'bake_cancel', 'bake_status', 'bake_clear', 'bake_get_settings', 'bake_set_settings', " +
            "'skybox_get', 'skybox_set_material', 'skybox_set_ambient', 'skybox_set_fog', 'skybox_set_sun', " +
            "'pipeline_get_info', 'pipeline_set_quality', " +
            "'stats_get', 'stats_get_memory'.",
            ParametersType = typeof(GraphicsParams))]
        private IEnumerator HandleManageGraphics(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required."));
                yield break;
            }

            switch (action.ToLower())
            {
                // ── Volume ──
                case "volume_create": HandleVolumeCreate(arguments, callback); break;
                case "volume_add_effect": HandleVolumeAddEffect(arguments, callback); break;
                case "volume_get_info": HandleVolumeGetInfo(arguments, callback); break;
                case "volume_list_effects": HandleVolumeListEffects(callback); break;

                // ── Bake ──
                case "bake_start": HandleBakeStart(callback); break;
                case "bake_cancel": HandleBakeCancel(callback); break;
                case "bake_status": HandleBakeStatus(callback); break;
                case "bake_clear": HandleBakeClear(callback); break;
                case "bake_get_settings": HandleBakeGetSettings(callback); break;
                case "bake_set_settings": HandleBakeSetSettings(arguments, callback); break;

                // ── Skybox / Environment ──
                case "skybox_get": HandleSkyboxGet(callback); break;
                case "skybox_set_material": HandleSkyboxSetMaterial(arguments, callback); break;
                case "skybox_set_ambient": HandleSkyboxSetAmbient(arguments, callback); break;
                case "skybox_set_fog": HandleSkyboxSetFog(arguments, callback); break;
                case "skybox_set_sun": HandleSkyboxSetSun(arguments, callback); break;

                // ── Pipeline ──
                case "pipeline_get_info": HandlePipelineGetInfo(callback); break;
                case "pipeline_set_quality": HandlePipelineSetQuality(arguments, callback); break;

                // ── Stats ──
                case "stats_get": HandleStatsGet(callback); break;
                case "stats_get_memory": HandleStatsGetMemory(callback); break;

                default:
                    callback(AgentToolHelpers.Fail($"Unknown action '{action}'."));
                    break;
            }
            yield break;
        }

        public class GraphicsParams
        {
            [ToolParam("Action to perform.", required: true)]
            public string action;
            [ToolParam("Target GameObject name.")]
            public string target;
            [ToolParam("Volume profile asset path.")]
            public string profilePath;
            [ToolParam("Whether the volume is global (default true).")]
            public string isGlobal;
            [ToolParam("Effect/override type name (e.g. 'Bloom', 'Vignette', 'ColorAdjustments').")]
            public string effectType;
            [ToolParam("Material path for skybox.")]
            public string materialPath;
            [ToolParam("Color value (hex or name).")]
            public string color;
            [ToolParam("Intensity or numeric value.")]
            public string value;
            [ToolParam("Quality level name or index.")]
            public string qualityLevel;
            [ToolParam("Property name to set.")]
            public string property;
            [ToolParam("Whether to enable (true/false).")]
            public string enabled;
            [ToolParam("Ambient mode: 'skybox', 'gradient', 'flat'.")]
            public string ambientMode;
            [ToolParam("Secondary color (for gradient sky/ground).")]
            public string color2;
            [ToolParam("Third color (for gradient equator).")]
            public string color3;
            [ToolParam("Fog mode: 'linear', 'exponential', 'exponentialSquared'.")]
            public string fogMode;
            [ToolParam("Fog start distance.")]
            public string fogStart;
            [ToolParam("Fog end distance.")]
            public string fogEnd;
            [ToolParam("Fog density.")]
            public string fogDensity;
            [ToolParam("Light baking settings: bounces, samples, etc.")]
            public string settingName;
            [ToolParam("Setting value.")]
            public string settingValue;
        }

        // ================================================================
        // Volume
        // ================================================================

        private void HandleVolumeCreate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string targetName = UnityAgent.ExtractStringField(arguments, "target") ?? "Volume";
            bool isGlobal = AgentToolHelpers.ParseBool(
                UnityAgent.ExtractStringField(arguments, "isGlobal"), true);

            var go = new GameObject(targetName);
            Undo.RegisterCreatedObjectUndo(go, "Create Volume");

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = isGlobal;
            volume.priority = 1f;

            // Create a new VolumeProfile asset
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            string path = $"Assets/{targetName}_Profile.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(profile, path);
            volume.profile = profile;

            callback(AgentToolHelpers.Ok(
                $"Volume '{targetName}' created (global={isGlobal}), profile saved at {path}"));
        }

        private void HandleVolumeAddEffect(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string targetName = UnityAgent.ExtractStringField(arguments, "target");
            string effectTypeName = UnityAgent.ExtractStringField(arguments, "effectType");

            if (string.IsNullOrEmpty(effectTypeName))
            {
                callback(AgentToolHelpers.Fail("'effectType' is required."));
                return;
            }

            Volume volume = null;
            if (!string.IsNullOrEmpty(targetName))
            {
                var go = GameObject.Find(targetName);
                if (go != null) volume = go.GetComponent<Volume>();
            }
            if (volume == null)
                volume = FindObjectOfType<Volume>();

            if (volume == null || volume.profile == null)
            {
                callback(AgentToolHelpers.Fail("No Volume with a profile found."));
                return;
            }

            // Find the VolumeComponent type by name
            Type effectType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == effectTypeName && typeof(VolumeComponent).IsAssignableFrom(t))
                        {
                            effectType = t;
                            break;
                        }
                    }
                }
                catch { }
                if (effectType != null) break;
            }

            if (effectType == null)
            {
                callback(AgentToolHelpers.Fail($"VolumeComponent type '{effectTypeName}' not found."));
                return;
            }

            // Check if already added
            foreach (var comp in volume.profile.components)
            {
                if (comp.GetType() == effectType)
                {
                    callback(AgentToolHelpers.Ok($"Effect '{effectTypeName}' already exists on the profile."));
                    return;
                }
            }

            var effect = (VolumeComponent)ScriptableObject.CreateInstance(effectType);
            effect.active = true;
            volume.profile.components.Add(effect);
            EditorUtility.SetDirty(volume.profile);
            AssetDatabase.SaveAssets();

            callback(AgentToolHelpers.Ok($"Added '{effectTypeName}' to Volume profile."));
        }

        private void HandleVolumeGetInfo(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string targetName = UnityAgent.ExtractStringField(arguments, "target");
            Volume volume = null;
            if (!string.IsNullOrEmpty(targetName))
            {
                var go = GameObject.Find(targetName);
                if (go != null) volume = go.GetComponent<Volume>();
            }
            if (volume == null)
                volume = FindObjectOfType<Volume>();

            if (volume == null)
            {
                callback(AgentToolHelpers.Fail("No Volume found in scene."));
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"volume\":{");
            sb.Append("\"gameObject\":\"").Append(UnityAgent.EscapeJson(volume.gameObject.name)).Append("\"");
            sb.Append(",\"isGlobal\":").Append(volume.isGlobal ? "true" : "false");
            sb.Append(",\"priority\":").Append(volume.priority);
            sb.Append(",\"weight\":").Append(volume.weight);

            if (volume.profile != null)
            {
                sb.Append(",\"profileName\":\"").Append(UnityAgent.EscapeJson(volume.profile.name)).Append("\"");
                sb.Append(",\"effects\":[");
                for (int i = 0; i < volume.profile.components.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    var comp = volume.profile.components[i];
                    sb.Append("{\"type\":\"").Append(UnityAgent.EscapeJson(comp.GetType().Name)).Append("\"");
                    sb.Append(",\"active\":").Append(comp.active ? "true" : "false");
                    sb.Append("}");
                }
                sb.Append("]");
            }
            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleVolumeListEffects(Action<UnityAgent.ToolResult> callback)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"effects\":[");
            bool first = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (typeof(VolumeComponent).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(VolumeComponent))
                        {
                            if (!first) sb.Append(",");
                            first = false;
                            sb.Append("\"").Append(UnityAgent.EscapeJson(t.Name)).Append("\"");
                        }
                    }
                }
                catch { }
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        // ================================================================
        // Light Baking
        // ================================================================

        private void HandleBakeStart(Action<UnityAgent.ToolResult> callback)
        {
            Lightmapping.BakeAsync();
            callback(AgentToolHelpers.Ok("Lightmap baking started."));
        }

        private void HandleBakeCancel(Action<UnityAgent.ToolResult> callback)
        {
            Lightmapping.Cancel();
            callback(AgentToolHelpers.Ok("Lightmap baking cancelled."));
        }

        private void HandleBakeStatus(Action<UnityAgent.ToolResult> callback)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"isRunning\":").Append(Lightmapping.isRunning ? "true" : "false");
            sb.Append(",\"buildProgress\":").Append(Lightmapping.buildProgress.ToString("F2"));
            sb.Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleBakeClear(Action<UnityAgent.ToolResult> callback)
        {
            Lightmapping.Clear();
            Lightmapping.ClearDiskCache();
            Lightmapping.ClearLightingDataAsset();
            callback(AgentToolHelpers.Ok("Lightmap data cleared."));
        }

        private void HandleBakeGetSettings(Action<UnityAgent.ToolResult> callback)
        {
            var ls = Lightmapping.lightingSettings;
            if (ls == null)
            {
                callback(AgentToolHelpers.Fail("No LightingSettings asset in scene."));
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"settings\":{");
            sb.Append("\"lightmapper\":\"").Append(ls.lightmapper.ToString()).Append("\"");
            sb.Append(",\"directSampleCount\":").Append(ls.directSampleCount);
            sb.Append(",\"indirectSampleCount\":").Append(ls.indirectSampleCount);
            sb.Append(",\"bounces\":").Append(ls.maxBounces);
            sb.Append(",\"lightmapResolution\":").Append(ls.lightmapResolution.ToString("F1"));
            sb.Append(",\"lightmapPadding\":").Append(ls.lightmapPadding);
            sb.Append(",\"ao\":").Append(ls.ao ? "true" : "false");
            sb.Append(",\"aoMaxDistance\":").Append(ls.aoMaxDistance.ToString("F2"));
            sb.Append(",\"directionalMode\":\"").Append(ls.directionalityMode.ToString()).Append("\"");
            sb.Append(",\"mixedBakeMode\":\"").Append(ls.mixedBakeMode.ToString()).Append("\"");
            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleBakeSetSettings(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var ls = Lightmapping.lightingSettings;
            if (ls == null)
            {
                ls = new LightingSettings();
                Lightmapping.lightingSettings = ls;
            }

            string name = UnityAgent.ExtractStringField(arguments, "settingName");
            string val = UnityAgent.ExtractStringField(arguments, "settingValue");
            if (string.IsNullOrEmpty(name))
            {
                callback(AgentToolHelpers.Fail("'settingName' is required."));
                return;
            }

            switch (name.ToLower())
            {
                case "bounces":
                    ls.maxBounces = AgentToolHelpers.ParseInt(val, 2);
                    break;
                case "directsamplecount":
                    ls.directSampleCount = AgentToolHelpers.ParseInt(val, 32);
                    break;
                case "indirectsamplecount":
                    ls.indirectSampleCount = AgentToolHelpers.ParseInt(val, 512);
                    break;
                case "lightmapresolution":
                    ls.lightmapResolution = AgentToolHelpers.ParseFloat(val, 40f);
                    break;
                case "lightmappadding":
                    ls.lightmapPadding = AgentToolHelpers.ParseInt(val, 2);
                    break;
                case "ao":
                    ls.ao = AgentToolHelpers.ParseBool(val, false);
                    break;
                case "aomaxdistance":
                    ls.aoMaxDistance = AgentToolHelpers.ParseFloat(val, 1f);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown setting '{name}'. Options: bounces, directSampleCount, indirectSampleCount, lightmapResolution, lightmapPadding, ao, aoMaxDistance."));
                    return;
            }

            EditorUtility.SetDirty(ls);
            callback(AgentToolHelpers.Ok($"Bake setting '{name}' set to '{val}'."));
        }

        // ================================================================
        // Skybox / Environment
        // ================================================================

        private void HandleSkyboxGet(Action<UnityAgent.ToolResult> callback)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":true");

            var sky = RenderSettings.skybox;
            sb.Append(",\"skyboxMaterial\":\"")
                .Append(sky != null ? UnityAgent.EscapeJson(sky.name) : "none").Append("\"");
            sb.Append(",\"skyboxShader\":\"")
                .Append(sky != null ? UnityAgent.EscapeJson(sky.shader.name) : "none").Append("\"");
            sb.Append(",\"ambientMode\":\"").Append(RenderSettings.ambientMode.ToString()).Append("\"");
            sb.Append(",\"ambientSkyColor\":").Append(AgentToolHelpers.ColorJson(RenderSettings.ambientSkyColor));
            sb.Append(",\"ambientEquatorColor\":").Append(AgentToolHelpers.ColorJson(RenderSettings.ambientEquatorColor));
            sb.Append(",\"ambientGroundColor\":").Append(AgentToolHelpers.ColorJson(RenderSettings.ambientGroundColor));
            sb.Append(",\"ambientIntensity\":").Append(RenderSettings.ambientIntensity.ToString("F2"));
            sb.Append(",\"fog\":").Append(RenderSettings.fog ? "true" : "false");
            sb.Append(",\"fogColor\":").Append(AgentToolHelpers.ColorJson(RenderSettings.fogColor));
            sb.Append(",\"fogMode\":\"").Append(RenderSettings.fogMode.ToString()).Append("\"");
            sb.Append(",\"fogDensity\":").Append(RenderSettings.fogDensity.ToString("F4"));
            sb.Append(",\"fogStartDistance\":").Append(RenderSettings.fogStartDistance.ToString("F1"));
            sb.Append(",\"fogEndDistance\":").Append(RenderSettings.fogEndDistance.ToString("F1"));

            var sun = RenderSettings.sun;
            sb.Append(",\"sunSource\":\"")
                .Append(sun != null ? UnityAgent.EscapeJson(sun.gameObject.name) : "none").Append("\"");

            sb.Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleSkyboxSetMaterial(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string matPath = UnityAgent.ExtractStringField(arguments, "materialPath");
            if (string.IsNullOrEmpty(matPath))
            {
                callback(AgentToolHelpers.Fail("'materialPath' is required."));
                return;
            }

            matPath = AgentToolHelpers.NormalizePath(matPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                callback(AgentToolHelpers.Fail($"Material not found at '{matPath}'."));
                return;
            }

            Undo.RecordObject(RenderSettings.GetRenderSettings(), "Set Skybox");
            RenderSettings.skybox = mat;
            EditorUtility.SetDirty(RenderSettings.GetRenderSettings());
            callback(AgentToolHelpers.Ok($"Skybox material set to '{mat.name}'."));
        }

        private void HandleSkyboxSetAmbient(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string mode = UnityAgent.ExtractStringField(arguments, "ambientMode");
            string colorStr = UnityAgent.ExtractStringField(arguments, "color");
            string color2Str = UnityAgent.ExtractStringField(arguments, "color2");
            string color3Str = UnityAgent.ExtractStringField(arguments, "color3");
            string intensityStr = UnityAgent.ExtractNumberField(arguments, "value");

            Undo.RecordObject(RenderSettings.GetRenderSettings(), "Set Ambient");

            if (!string.IsNullOrEmpty(mode))
            {
                switch (mode.ToLower())
                {
                    case "skybox": RenderSettings.ambientMode = AmbientMode.Skybox; break;
                    case "gradient": case "trilight": RenderSettings.ambientMode = AmbientMode.Trilight; break;
                    case "flat": case "color": RenderSettings.ambientMode = AmbientMode.Flat; break;
                }
            }

            Color c;
            if (!string.IsNullOrEmpty(colorStr) && AgentToolHelpers.TryParseColor(colorStr, out c))
                RenderSettings.ambientSkyColor = c;
            if (!string.IsNullOrEmpty(color2Str) && AgentToolHelpers.TryParseColor(color2Str, out c))
                RenderSettings.ambientEquatorColor = c;
            if (!string.IsNullOrEmpty(color3Str) && AgentToolHelpers.TryParseColor(color3Str, out c))
                RenderSettings.ambientGroundColor = c;
            if (intensityStr != null)
                RenderSettings.ambientIntensity = AgentToolHelpers.ParseFloat(intensityStr, 1f);

            EditorUtility.SetDirty(RenderSettings.GetRenderSettings());
            callback(AgentToolHelpers.Ok("Ambient lighting updated."));
        }

        private void HandleSkyboxSetFog(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string enabledStr = UnityAgent.ExtractStringField(arguments, "enabled");
            string colorStr = UnityAgent.ExtractStringField(arguments, "color");
            string fogModeStr = UnityAgent.ExtractStringField(arguments, "fogMode");
            string densityStr = UnityAgent.ExtractNumberField(arguments, "fogDensity");
            string startStr = UnityAgent.ExtractNumberField(arguments, "fogStart");
            string endStr = UnityAgent.ExtractNumberField(arguments, "fogEnd");

            Undo.RecordObject(RenderSettings.GetRenderSettings(), "Set Fog");

            if (enabledStr != null)
                RenderSettings.fog = AgentToolHelpers.ParseBool(enabledStr, true);

            Color c;
            if (!string.IsNullOrEmpty(colorStr) && AgentToolHelpers.TryParseColor(colorStr, out c))
                RenderSettings.fogColor = c;

            if (!string.IsNullOrEmpty(fogModeStr))
            {
                switch (fogModeStr.ToLower())
                {
                    case "linear": RenderSettings.fogMode = FogMode.Linear; break;
                    case "exponential": case "exp": RenderSettings.fogMode = FogMode.Exponential; break;
                    case "exponentialsquared": case "exp2": RenderSettings.fogMode = FogMode.ExponentialSquared; break;
                }
            }

            if (densityStr != null) RenderSettings.fogDensity = AgentToolHelpers.ParseFloat(densityStr, 0.01f);
            if (startStr != null) RenderSettings.fogStartDistance = AgentToolHelpers.ParseFloat(startStr, 0f);
            if (endStr != null) RenderSettings.fogEndDistance = AgentToolHelpers.ParseFloat(endStr, 300f);

            EditorUtility.SetDirty(RenderSettings.GetRenderSettings());
            callback(AgentToolHelpers.Ok("Fog settings updated."));
        }

        private void HandleSkyboxSetSun(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string targetName = UnityAgent.ExtractStringField(arguments, "target");
            if (string.IsNullOrEmpty(targetName))
            {
                callback(AgentToolHelpers.Fail("'target' (directional light name) is required."));
                return;
            }

            var go = GameObject.Find(targetName);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{targetName}' not found."));
                return;
            }

            var light = go.GetComponent<Light>();
            if (light == null || light.type != LightType.Directional)
            {
                callback(AgentToolHelpers.Fail($"'{targetName}' does not have a Directional Light."));
                return;
            }

            Undo.RecordObject(RenderSettings.GetRenderSettings(), "Set Sun Source");
            RenderSettings.sun = light;
            EditorUtility.SetDirty(RenderSettings.GetRenderSettings());
            callback(AgentToolHelpers.Ok($"Sun source set to '{targetName}'."));
        }

        // ================================================================
        // Pipeline
        // ================================================================

        private void HandlePipelineGetInfo(Action<UnityAgent.ToolResult> callback)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":true");

            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                sb.Append(",\"pipelineType\":\"").Append(UnityAgent.EscapeJson(pipeline.GetType().Name)).Append("\"");
                sb.Append(",\"pipelineName\":\"").Append(UnityAgent.EscapeJson(pipeline.name)).Append("\"");
            }
            else
            {
                sb.Append(",\"pipelineType\":\"BuiltIn\"");
            }

            sb.Append(",\"currentQualityLevel\":").Append(QualitySettings.GetQualityLevel());
            sb.Append(",\"qualityLevelName\":\"").Append(UnityAgent.EscapeJson(QualitySettings.names[QualitySettings.GetQualityLevel()])).Append("\"");
            sb.Append(",\"qualityLevels\":[");
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"index\":").Append(i);
                sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(names[i])).Append("\"}");
            }
            sb.Append("]");

            sb.Append(",\"vSyncCount\":").Append(QualitySettings.vSyncCount);
            sb.Append(",\"antiAliasing\":").Append(QualitySettings.antiAliasing);
            sb.Append(",\"shadowDistance\":").Append(QualitySettings.shadowDistance.ToString("F1"));

            sb.Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandlePipelineSetQuality(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string levelStr = UnityAgent.ExtractStringField(arguments, "qualityLevel");
            if (string.IsNullOrEmpty(levelStr))
            {
                callback(AgentToolHelpers.Fail("'qualityLevel' (name or index) is required."));
                return;
            }

            int idx;
            if (int.TryParse(levelStr, out idx))
            {
                if (idx < 0 || idx >= QualitySettings.names.Length)
                {
                    callback(AgentToolHelpers.Fail($"Quality level index {idx} out of range (0-{QualitySettings.names.Length - 1})."));
                    return;
                }
            }
            else
            {
                idx = -1;
                var names = QualitySettings.names;
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].Equals(levelStr, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx < 0)
                {
                    callback(AgentToolHelpers.Fail($"Quality level '{levelStr}' not found."));
                    return;
                }
            }

            QualitySettings.SetQualityLevel(idx, true);
            callback(AgentToolHelpers.Ok($"Quality level set to '{QualitySettings.names[idx]}' (index {idx})."));
        }

        // ================================================================
        // Stats
        // ================================================================

        private void HandleStatsGet(Action<UnityAgent.ToolResult> callback)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"rendering\":{");
            sb.Append("\"currentResolution\":\"").Append(Screen.currentResolution.width)
                .Append("x").Append(Screen.currentResolution.height).Append("\"");
            sb.Append(",\"targetFrameRate\":").Append(Application.targetFrameRate);
            sb.Append(",\"maxTextureSize\":").Append(QualitySettings.globalTextureMipmapLimit);
            sb.Append(",\"antiAliasing\":").Append(QualitySettings.antiAliasing);
            sb.Append(",\"shadowDistance\":").Append(QualitySettings.shadowDistance.ToString("F1"));
            sb.Append(",\"pixelLightCount\":").Append(QualitySettings.pixelLightCount);
            sb.Append(",\"realtimeReflectionProbes\":").Append(QualitySettings.realtimeReflectionProbes ? "true" : "false");
            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleStatsGetMemory(Action<UnityAgent.ToolResult> callback)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"memory\":{");
            sb.Append("\"systemMemorySize\":").Append(SystemInfo.systemMemorySize);
            sb.Append(",\"graphicsMemorySize\":").Append(SystemInfo.graphicsMemorySize);
            sb.Append(",\"graphicsDeviceName\":\"").Append(UnityAgent.EscapeJson(SystemInfo.graphicsDeviceName)).Append("\"");
            sb.Append(",\"graphicsDeviceType\":\"").Append(SystemInfo.graphicsDeviceType.ToString()).Append("\"");
            sb.Append(",\"graphicsDeviceVersion\":\"").Append(UnityAgent.EscapeJson(SystemInfo.graphicsDeviceVersion)).Append("\"");
            sb.Append(",\"maxTextureSize\":").Append(SystemInfo.maxTextureSize);
            sb.Append(",\"npotSupport\":\"").Append(SystemInfo.npotSupport.ToString()).Append("\"");
            sb.Append(",\"supportsComputeShaders\":").Append(SystemInfo.supportsComputeShaders ? "true" : "false");
            sb.Append(",\"supportsRayTracing\":").Append(SystemInfo.supportsRayTracing ? "true" : "false");
            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentGraphicsTools] Graphics tools are only available in the Unity Editor.");
        }
#endif
    }
}
