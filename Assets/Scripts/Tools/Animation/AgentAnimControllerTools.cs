using System;
using System.Collections;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// AnimatorController and AnimationClip management tools (Editor-only).
    /// Maps to unity-mcp Animation/: ControllerCreate, ControllerLayers,
    /// ControllerBlendTrees, ClipCreate, ClipPresets.
    /// </summary>
    public class AgentAnimControllerTools : MonoBehaviour
    {
#if UNITY_EDITOR

        [AgentTool("manageAnimController",
            "Manage AnimatorControllers and AnimationClips. Actions: " +
            "'controller_create', 'controller_add_state', 'controller_add_transition', " +
            "'controller_add_parameter', 'controller_get_info', 'controller_assign', " +
            "'controller_add_layer', 'controller_remove_layer', " +
            "'clip_create', 'clip_get_info', 'clip_add_curve', 'clip_create_preset', " +
            "'clip_assign', 'clip_add_event', 'clip_remove_event'.",
            ParametersType = typeof(AnimControllerParams))]
        private IEnumerator HandleManageAnimController(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required."));
                yield break;
            }

            switch (action.ToLower())
            {
                case "controller_create": HandleControllerCreate(arguments, callback); break;
                case "controller_add_state": HandleControllerAddState(arguments, callback); break;
                case "controller_add_transition": HandleControllerAddTransition(arguments, callback); break;
                case "controller_add_parameter": HandleControllerAddParameter(arguments, callback); break;
                case "controller_get_info": HandleControllerGetInfo(arguments, callback); break;
                case "controller_assign": HandleControllerAssign(arguments, callback); break;
                case "controller_add_layer": HandleControllerAddLayer(arguments, callback); break;
                case "controller_remove_layer": HandleControllerRemoveLayer(arguments, callback); break;
                case "clip_create": HandleClipCreate(arguments, callback); break;
                case "clip_get_info": HandleClipGetInfo(arguments, callback); break;
                case "clip_add_curve": HandleClipAddCurve(arguments, callback); break;
                case "clip_create_preset": HandleClipCreatePreset(arguments, callback); break;
                case "clip_assign": HandleClipAssign(arguments, callback); break;
                case "clip_add_event": HandleClipAddEvent(arguments, callback); break;
                case "clip_remove_event": HandleClipRemoveEvent(arguments, callback); break;
                default:
                    callback(AgentToolHelpers.Fail($"Unknown action '{action}'."));
                    break;
            }
            yield break;
        }

        public class AnimControllerParams
        {
            [ToolParam("Action to perform.", required: true)]
            public string action;
            [ToolParam("Asset path for controller or clip.")]
            public string path;
            [ToolParam("Name for the asset/state/parameter.")]
            public string name;
            [ToolParam("Target GameObject name (for controller_assign).")]
            public string target;
            [ToolParam("Layer index (default 0).")]
            public string layerIndex;
            [ToolParam("Layer name (for add/remove layer).")]
            public string layerName;
            [ToolParam("Source state name (for transitions).")]
            public string fromState;
            [ToolParam("Destination state name (for transitions).")]
            public string toState;
            [ToolParam("Has exit time (for transitions).")]
            public string hasExitTime;
            [ToolParam("Transition duration.")]
            public string transitionDuration;
            [ToolParam("Parameter type: float, int, bool, trigger.")]
            public string paramType;
            [ToolParam("Default value for parameter.")]
            public string defaultValue;
            [ToolParam("Clip path (for clip_assign).")]
            public string clipPath;
            [ToolParam("State name (for clip_assign).")]
            public string stateName;
            [ToolParam("Animation property path (e.g. 'localPosition.x').")]
            public string propertyPath;
            [ToolParam("Component type for curve binding.")]
            public string componentType;
            [ToolParam("Keyframes as JSON array [{time,value}, ...].")]
            public string keyframes;
            [ToolParam("Preset name: bounce, rotate, pulse, fade, shake, hover, spin.")]
            public string preset;
            [ToolParam("Duration for clip/preset.")]
            public string duration;
            [ToolParam("Event time (0-1 normalized).")]
            public string eventTime;
            [ToolParam("Event function name.")]
            public string functionName;
            [ToolParam("Event index (for remove).")]
            public string eventIndex;
            [ToolParam("Looping clip.")]
            public string loop;
        }

        // ================================================================
        // Helper: load controller
        // ================================================================

        private AnimatorController LoadController(string arguments)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }

        private AnimationClip LoadClip(string arguments, string field = "clipPath")
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, field));
            if (string.IsNullOrEmpty(path))
                path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        // ================================================================
        // Controller operations
        // ================================================================

        private void HandleControllerCreate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            if (string.IsNullOrEmpty(path))
                path = "Assets/AnimatorControllers/NewController.controller";

            if (!path.EndsWith(".controller"))
                path += ".controller";

            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            callback(AgentToolHelpers.Ok($"AnimatorController created at '{path}'."));
        }

        private void HandleControllerAddState(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var controller = LoadController(arguments);
            if (controller == null) { callback(AgentToolHelpers.Fail("Controller not found. Provide 'path'.")); return; }

            string stateName = UnityAgent.ExtractStringField(arguments, "name") ?? "NewState";
            int layerIdx = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "layerIndex"), 0);

            if (layerIdx < 0 || layerIdx >= controller.layers.Length)
            {
                callback(AgentToolHelpers.Fail($"Layer index {layerIdx} out of range."));
                return;
            }

            var sm = controller.layers[layerIdx].stateMachine;
            var state = sm.AddState(stateName);

            // Optionally assign a clip
            string clipPath = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "clipPath"));
            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip != null) state.motion = clip;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"State '{stateName}' added to layer {layerIdx}."));
        }

        private void HandleControllerAddTransition(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var controller = LoadController(arguments);
            if (controller == null) { callback(AgentToolHelpers.Fail("Controller not found.")); return; }

            string from = UnityAgent.ExtractStringField(arguments, "fromState");
            string to = UnityAgent.ExtractStringField(arguments, "toState");
            int layerIdx = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "layerIndex"), 0);

            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                callback(AgentToolHelpers.Fail("'fromState' and 'toState' are required."));
                return;
            }

            if (layerIdx < 0 || layerIdx >= controller.layers.Length)
            {
                callback(AgentToolHelpers.Fail($"Layer index {layerIdx} out of range."));
                return;
            }

            var sm = controller.layers[layerIdx].stateMachine;
            AnimatorState fromState = null, toState = null;

            foreach (var cs in sm.states)
            {
                if (cs.state.name == from) fromState = cs.state;
                if (cs.state.name == to) toState = cs.state;
            }

            if (fromState == null) { callback(AgentToolHelpers.Fail($"State '{from}' not found.")); return; }
            if (toState == null) { callback(AgentToolHelpers.Fail($"State '{to}' not found.")); return; }

            var transition = fromState.AddTransition(toState);
            transition.hasExitTime = AgentToolHelpers.ParseBool(
                UnityAgent.ExtractStringField(arguments, "hasExitTime"), true);

            string dur = UnityAgent.ExtractNumberField(arguments, "transitionDuration");
            if (dur != null) transition.duration = AgentToolHelpers.ParseFloat(dur, 0.25f);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"Transition added: {from} → {to}."));
        }

        private void HandleControllerAddParameter(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var controller = LoadController(arguments);
            if (controller == null) { callback(AgentToolHelpers.Fail("Controller not found.")); return; }

            string paramName = UnityAgent.ExtractStringField(arguments, "name");
            string paramType = UnityAgent.ExtractStringField(arguments, "paramType") ?? "float";

            if (string.IsNullOrEmpty(paramName))
            {
                callback(AgentToolHelpers.Fail("'name' is required."));
                return;
            }

            AnimatorControllerParameterType pType;
            switch (paramType.ToLower())
            {
                case "float": pType = AnimatorControllerParameterType.Float; break;
                case "int": pType = AnimatorControllerParameterType.Int; break;
                case "bool": pType = AnimatorControllerParameterType.Bool; break;
                case "trigger": pType = AnimatorControllerParameterType.Trigger; break;
                default:
                    callback(AgentToolHelpers.Fail($"Unknown param type '{paramType}'. Use: float, int, bool, trigger."));
                    return;
            }

            controller.AddParameter(paramName, pType);

            // Set default value
            string defVal = UnityAgent.ExtractStringField(arguments, "defaultValue");
            if (!string.IsNullOrEmpty(defVal))
            {
                var parameters = controller.parameters;
                var param = parameters[parameters.Length - 1];
                switch (pType)
                {
                    case AnimatorControllerParameterType.Float:
                        param.defaultFloat = AgentToolHelpers.ParseFloat(defVal);
                        break;
                    case AnimatorControllerParameterType.Int:
                        param.defaultInt = AgentToolHelpers.ParseInt(defVal);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        param.defaultBool = AgentToolHelpers.ParseBool(defVal);
                        break;
                }
                controller.parameters = parameters;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"Parameter '{paramName}' ({paramType}) added."));
        }

        private void HandleControllerGetInfo(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var controller = LoadController(arguments);
            if (controller == null) { callback(AgentToolHelpers.Fail("Controller not found.")); return; }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"controller\":{");
            sb.Append("\"name\":\"").Append(UnityAgent.EscapeJson(controller.name)).Append("\"");

            // Parameters
            sb.Append(",\"parameters\":[");
            for (int i = 0; i < controller.parameters.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var p = controller.parameters[i];
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(p.name)).Append("\"");
                sb.Append(",\"type\":\"").Append(p.type.ToString()).Append("\"}");
            }
            sb.Append("]");

            // Layers
            sb.Append(",\"layers\":[");
            for (int i = 0; i < controller.layers.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var layer = controller.layers[i];
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(layer.name)).Append("\"");
                sb.Append(",\"weight\":").Append(layer.defaultWeight.ToString("F2"));

                // States
                sb.Append(",\"states\":[");
                var states = layer.stateMachine.states;
                for (int j = 0; j < states.Length; j++)
                {
                    if (j > 0) sb.Append(",");
                    sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(states[j].state.name)).Append("\"");
                    sb.Append(",\"speed\":").Append(states[j].state.speed.ToString("F2"));
                    if (states[j].state.motion != null)
                        sb.Append(",\"motion\":\"").Append(UnityAgent.EscapeJson(states[j].state.motion.name)).Append("\"");
                    sb.Append("}");
                }
                sb.Append("]");
                sb.Append("}");
            }
            sb.Append("]");

            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleControllerAssign(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string target = UnityAgent.ExtractStringField(arguments, "target");
            if (string.IsNullOrEmpty(target))
            {
                callback(AgentToolHelpers.Fail("'target' (GameObject name) is required."));
                return;
            }

            var go = GameObject.Find(target);
            if (go == null) { callback(AgentToolHelpers.Fail($"'{target}' not found.")); return; }

            var controller = LoadController(arguments);
            if (controller == null) { callback(AgentToolHelpers.Fail("Controller not found.")); return; }

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();

            Undo.RecordObject(animator, "Assign Controller");
            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(animator);

            callback(AgentToolHelpers.Ok($"Controller '{controller.name}' assigned to '{target}'."));
        }

        private void HandleControllerAddLayer(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var controller = LoadController(arguments);
            if (controller == null) { callback(AgentToolHelpers.Fail("Controller not found.")); return; }

            string layerName = UnityAgent.ExtractStringField(arguments, "layerName") ?? "New Layer";
            controller.AddLayer(layerName);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"Layer '{layerName}' added."));
        }

        private void HandleControllerRemoveLayer(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var controller = LoadController(arguments);
            if (controller == null) { callback(AgentToolHelpers.Fail("Controller not found.")); return; }

            int layerIdx = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "layerIndex"), -1);
            string layerName = UnityAgent.ExtractStringField(arguments, "layerName");

            if (layerIdx < 0 && !string.IsNullOrEmpty(layerName))
            {
                for (int i = 0; i < controller.layers.Length; i++)
                {
                    if (controller.layers[i].name == layerName) { layerIdx = i; break; }
                }
            }

            if (layerIdx < 0 || layerIdx >= controller.layers.Length)
            {
                callback(AgentToolHelpers.Fail("Layer not found."));
                return;
            }

            controller.RemoveLayer(layerIdx);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"Layer at index {layerIdx} removed."));
        }

        // ================================================================
        // Clip operations
        // ================================================================

        private void HandleClipCreate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            if (string.IsNullOrEmpty(path))
                path = "Assets/Animations/NewClip.anim";

            if (!path.EndsWith(".anim"))
                path += ".anim";

            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var clip = new AnimationClip();
            clip.name = System.IO.Path.GetFileNameWithoutExtension(path);

            string dur = UnityAgent.ExtractNumberField(arguments, "duration");
            // Clip length is determined by curves, but we can set wrap mode
            bool loop = AgentToolHelpers.ParseBool(UnityAgent.ExtractStringField(arguments, "loop"), false);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"AnimationClip created at '{path}' (loop={loop})."));
        }

        private void HandleClipGetInfo(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var clip = LoadClip(arguments);
            if (clip == null) { callback(AgentToolHelpers.Fail("Clip not found.")); return; }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            var bindings = AnimationUtility.GetCurveBindings(clip);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"clip\":{");
            sb.Append("\"name\":\"").Append(UnityAgent.EscapeJson(clip.name)).Append("\"");
            sb.Append(",\"length\":").Append(clip.length.ToString("F3"));
            sb.Append(",\"frameRate\":").Append(clip.frameRate.ToString("F0"));
            sb.Append(",\"wrapMode\":\"").Append(clip.wrapMode.ToString()).Append("\"");
            sb.Append(",\"loopTime\":").Append(settings.loopTime ? "true" : "false");
            sb.Append(",\"curveCount\":").Append(bindings.Length);

            sb.Append(",\"curves\":[");
            int max = Mathf.Min(bindings.Length, 20); // Limit output
            for (int i = 0; i < max; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"path\":\"").Append(UnityAgent.EscapeJson(bindings[i].path)).Append("\"");
                sb.Append(",\"property\":\"").Append(UnityAgent.EscapeJson(bindings[i].propertyName)).Append("\"");
                sb.Append(",\"type\":\"").Append(UnityAgent.EscapeJson(bindings[i].type.Name)).Append("\"}");
            }
            sb.Append("]");

            // Events
            var events = AnimationUtility.GetAnimationEvents(clip);
            sb.Append(",\"events\":[");
            for (int i = 0; i < events.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"time\":").Append(events[i].time.ToString("F3"));
                sb.Append(",\"function\":\"").Append(UnityAgent.EscapeJson(events[i].functionName)).Append("\"}");
            }
            sb.Append("]");

            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleClipAddCurve(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var clip = LoadClip(arguments);
            if (clip == null) { callback(AgentToolHelpers.Fail("Clip not found.")); return; }

            string propPath = UnityAgent.ExtractStringField(arguments, "propertyPath");
            string compType = UnityAgent.ExtractStringField(arguments, "componentType") ?? "Transform";
            string kfJson = UnityAgent.ExtractStringField(arguments, "keyframes");

            if (string.IsNullOrEmpty(propPath))
            {
                callback(AgentToolHelpers.Fail("'propertyPath' is required."));
                return;
            }

            // Parse keyframes [{time:0, value:0}, {time:1, value:1}]
            var keyframes = new System.Collections.Generic.List<Keyframe>();
            if (!string.IsNullOrEmpty(kfJson))
            {
                int idx = 0;
                while (idx < kfJson.Length)
                {
                    int start = kfJson.IndexOf('{', idx);
                    if (start < 0) break;
                    int end = kfJson.IndexOf('}', start);
                    if (end < 0) break;

                    string obj = kfJson.Substring(start, end - start + 1);
                    float t = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(obj, "time"));
                    float v = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(obj, "value"));
                    keyframes.Add(new Keyframe(t, v));
                    idx = end + 1;
                }
            }

            if (keyframes.Count == 0)
            {
                // Default: two keyframes
                keyframes.Add(new Keyframe(0f, 0f));
                keyframes.Add(new Keyframe(1f, 1f));
            }

            Type type = AgentToolHelpers.ResolveComponentType(compType) ?? typeof(Transform);
            var binding = new EditorCurveBinding
            {
                path = "",
                type = type,
                propertyName = propPath
            };

            var curve = new AnimationCurve(keyframes.ToArray());
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            callback(AgentToolHelpers.Ok($"Curve added: {compType}.{propPath} with {keyframes.Count} keyframes."));
        }

        private void HandleClipCreatePreset(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string preset = UnityAgent.ExtractStringField(arguments, "preset");
            string path = AgentToolHelpers.NormalizePath(UnityAgent.ExtractStringField(arguments, "path"));
            float duration = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(arguments, "duration"), 1f);

            if (string.IsNullOrEmpty(preset))
            {
                callback(AgentToolHelpers.Fail(
                    "'preset' is required. Options: bounce, rotate, pulse, fade, shake, hover, spin."));
                return;
            }

            if (string.IsNullOrEmpty(path))
                path = $"Assets/Animations/{preset}.anim";
            if (!path.EndsWith(".anim"))
                path += ".anim";

            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var clip = new AnimationClip();
            clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            switch (preset.ToLower())
            {
                case "bounce":
                    SetCurve(clip, "localPosition.y", typeof(Transform),
                        new Keyframe(0, 0), new Keyframe(duration * 0.5f, 0.5f), new Keyframe(duration, 0));
                    break;
                case "rotate":
                    SetCurve(clip, "localEulerAnglesRaw.y", typeof(Transform),
                        new Keyframe(0, 0), new Keyframe(duration, 360));
                    break;
                case "pulse":
                    SetCurve(clip, "localScale.x", typeof(Transform),
                        new Keyframe(0, 1), new Keyframe(duration * 0.5f, 1.2f), new Keyframe(duration, 1));
                    SetCurve(clip, "localScale.y", typeof(Transform),
                        new Keyframe(0, 1), new Keyframe(duration * 0.5f, 1.2f), new Keyframe(duration, 1));
                    SetCurve(clip, "localScale.z", typeof(Transform),
                        new Keyframe(0, 1), new Keyframe(duration * 0.5f, 1.2f), new Keyframe(duration, 1));
                    break;
                case "fade":
                    SetCurve(clip, "m_Color.a", typeof(CanvasRenderer),
                        new Keyframe(0, 1), new Keyframe(duration, 0));
                    break;
                case "shake":
                    float amp = 0.1f;
                    float step = duration / 8f;
                    SetCurve(clip, "localPosition.x", typeof(Transform),
                        new Keyframe(0, 0), new Keyframe(step, amp), new Keyframe(step * 2, -amp),
                        new Keyframe(step * 3, amp), new Keyframe(step * 4, -amp),
                        new Keyframe(step * 5, amp * 0.5f), new Keyframe(step * 6, -amp * 0.5f),
                        new Keyframe(step * 7, amp * 0.25f), new Keyframe(duration, 0));
                    break;
                case "hover":
                    SetCurve(clip, "localPosition.y", typeof(Transform),
                        new Keyframe(0, 0), new Keyframe(duration * 0.5f, 0.3f), new Keyframe(duration, 0));
                    break;
                case "spin":
                    SetCurve(clip, "localEulerAnglesRaw.z", typeof(Transform),
                        new Keyframe(0, 0), new Keyframe(duration, 360));
                    break;
                default:
                    callback(AgentToolHelpers.Fail($"Unknown preset '{preset}'."));
                    return;
            }

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"Preset '{preset}' clip created at '{path}' (duration={duration:F2}s)."));
        }

        private static void SetCurve(AnimationClip clip, string property, Type type, params Keyframe[] keys)
        {
            var binding = new EditorCurveBinding { path = "", type = type, propertyName = property };
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
        }

        private void HandleClipAssign(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var controller = LoadController(arguments);
            if (controller == null) { callback(AgentToolHelpers.Fail("Controller not found.")); return; }

            var clip = LoadClip(arguments, "clipPath");
            if (clip == null) { callback(AgentToolHelpers.Fail("Clip not found. Provide 'clipPath'.")); return; }

            string stateName = UnityAgent.ExtractStringField(arguments, "stateName");
            int layerIdx = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "layerIndex"), 0);

            if (string.IsNullOrEmpty(stateName))
            {
                callback(AgentToolHelpers.Fail("'stateName' is required."));
                return;
            }

            if (layerIdx < 0 || layerIdx >= controller.layers.Length)
            {
                callback(AgentToolHelpers.Fail($"Layer {layerIdx} out of range."));
                return;
            }

            var sm = controller.layers[layerIdx].stateMachine;
            foreach (var cs in sm.states)
            {
                if (cs.state.name == stateName)
                {
                    cs.state.motion = clip;
                    EditorUtility.SetDirty(controller);
                    AssetDatabase.SaveAssets();
                    callback(AgentToolHelpers.Ok($"Clip '{clip.name}' assigned to state '{stateName}'."));
                    return;
                }
            }

            callback(AgentToolHelpers.Fail($"State '{stateName}' not found."));
        }

        private void HandleClipAddEvent(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var clip = LoadClip(arguments);
            if (clip == null) { callback(AgentToolHelpers.Fail("Clip not found.")); return; }

            float time = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(arguments, "eventTime"), 0f);
            string func = UnityAgent.ExtractStringField(arguments, "functionName");

            if (string.IsNullOrEmpty(func))
            {
                callback(AgentToolHelpers.Fail("'functionName' is required."));
                return;
            }

            var events = AnimationUtility.GetAnimationEvents(clip);
            var newEvents = new AnimationEvent[events.Length + 1];
            Array.Copy(events, newEvents, events.Length);
            newEvents[events.Length] = new AnimationEvent { time = time, functionName = func };
            AnimationUtility.SetAnimationEvents(clip, newEvents);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"Event '{func}' added at time {time:F3}."));
        }

        private void HandleClipRemoveEvent(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var clip = LoadClip(arguments);
            if (clip == null) { callback(AgentToolHelpers.Fail("Clip not found.")); return; }

            int idx = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "eventIndex"), -1);
            var events = AnimationUtility.GetAnimationEvents(clip);

            if (idx < 0 || idx >= events.Length)
            {
                callback(AgentToolHelpers.Fail($"Event index {idx} out of range (0-{events.Length - 1})."));
                return;
            }

            var newEvents = new AnimationEvent[events.Length - 1];
            int j = 0;
            for (int i = 0; i < events.Length; i++)
            {
                if (i != idx) newEvents[j++] = events[i];
            }
            AnimationUtility.SetAnimationEvents(clip, newEvents);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            callback(AgentToolHelpers.Ok($"Event at index {idx} removed."));
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentAnimControllerTools] Animation controller tools are only available in the Unity Editor.");
        }
#endif
    }
}
