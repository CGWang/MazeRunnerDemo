using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace LLMAgent.Tools
{
    /// <summary>
    /// Animation and Animator control tools (runtime-compatible).
    /// Tools: manageAnimation
    /// </summary>
    public class AgentAnimationTools : MonoBehaviour
    {
        // =================================================================
        // Tool: manageAnimation
        // =================================================================

        [AgentTool("manageAnimation",
            "Control Animator on a GameObject. Actions: 'status' (get current state/parameters), " +
            "'set_param' (set animator parameter), 'play' (play a state by name), " +
            "'set_speed' (change playback speed), 'crossfade' (blend to state).",
            ParametersType = typeof(AnimationParams))]
        private IEnumerator HandleManageAnimation(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "status";
            string targetName = UnityAgent.ExtractStringField(arguments, "target");

            if (string.IsNullOrEmpty(targetName))
            {
                callback(AgentToolHelpers.Fail("'target' (GameObject name) is required."));
                yield break;
            }

            var go = GameObject.Find(targetName);
            if (go == null)
            {
                callback(AgentToolHelpers.Fail($"GameObject '{targetName}' not found."));
                yield break;
            }

            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                callback(AgentToolHelpers.Fail($"No Animator on '{targetName}'."));
                yield break;
            }

            switch (action.ToLower())
            {
                case "status":
                    HandleStatus(animator, callback);
                    break;

                case "set_param":
                    HandleSetParam(arguments, animator, callback);
                    break;

                case "play":
                    string stateName = UnityAgent.ExtractStringField(arguments, "stateName");
                    int layer = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(arguments, "layer"), 0);
                    if (string.IsNullOrEmpty(stateName))
                    {
                        callback(AgentToolHelpers.Fail("'stateName' is required for play action."));
                        break;
                    }
                    animator.Play(stateName, layer);
                    callback(AgentToolHelpers.Ok($"Playing state '{stateName}' on layer {layer}."));
                    break;

                case "crossfade":
                    string fadeTo = UnityAgent.ExtractStringField(arguments, "stateName");
                    float duration = AgentToolHelpers.ParseFloat(
                        UnityAgent.ExtractNumberField(arguments, "duration"), 0.25f);
                    int fadeLayer = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(arguments, "layer"), 0);
                    if (string.IsNullOrEmpty(fadeTo))
                    {
                        callback(AgentToolHelpers.Fail("'stateName' is required for crossfade action."));
                        break;
                    }
                    animator.CrossFadeInFixedTime(fadeTo, duration, fadeLayer);
                    callback(AgentToolHelpers.Ok($"Crossfading to '{fadeTo}' over {duration}s."));
                    break;

                case "set_speed":
                    float speed = AgentToolHelpers.ParseFloat(
                        UnityAgent.ExtractNumberField(arguments, "value"), 1f);
                    animator.speed = speed;
                    callback(AgentToolHelpers.Ok($"Animator speed set to {speed}."));
                    break;

                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: status, set_param, play, crossfade, set_speed."));
                    break;
            }

            yield break;
        }

        public class AnimationParams
        {
            [ToolParam("Target GameObject name.", required: true)]
            public string target;
            [ToolParam("Action: status, set_param, play, crossfade, set_speed.", required: true)]
            public string action;
            [ToolParam("Parameter name (for set_param).")]
            public string paramName;
            [ToolParam("Parameter value as string (for set_param, set_speed).")]
            public string value;
            [ToolParam("Parameter type: 'float', 'int', 'bool', 'trigger' (for set_param).")]
            public string paramType;
            [ToolParam("State name (for play, crossfade).")]
            public string stateName;
            [ToolParam("Animator layer index (default 0).", SchemaType = "integer")]
            public int layer;
            [ToolParam("Crossfade duration in seconds (default 0.25).", SchemaType = "number")]
            public float duration;
        }

        private void HandleStatus(Animator animator, Action<UnityAgent.ToolResult> callback)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"enabled\":").Append(animator.enabled ? "true" : "false");
            sb.Append(",\"speed\":").Append(animator.speed);
            sb.Append(",\"layerCount\":").Append(animator.layerCount);

            // Current state info
            if (animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
            {
                sb.Append(",\"layers\":[");
                for (int i = 0; i < animator.layerCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    var stateInfo = animator.GetCurrentAnimatorStateInfo(i);
                    sb.Append("{\"index\":").Append(i);
                    sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(animator.GetLayerName(i))).Append("\"");
                    sb.Append(",\"weight\":").Append(animator.GetLayerWeight(i).ToString("F2"));
                    sb.Append(",\"normalizedTime\":").Append(stateInfo.normalizedTime.ToString("F2"));
                    sb.Append(",\"length\":").Append(stateInfo.length.ToString("F2"));
                    sb.Append(",\"isLooping\":").Append(stateInfo.loop ? "true" : "false");
                    sb.Append("}");
                }
                sb.Append("]");

                // Parameters
                sb.Append(",\"parameters\":[");
                for (int i = 0; i < animator.parameterCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    var p = animator.GetParameter(i);
                    sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(p.name)).Append("\"");
                    sb.Append(",\"type\":\"").Append(p.type.ToString()).Append("\"");
                    switch (p.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            sb.Append(",\"value\":").Append(animator.GetFloat(p.name).ToString("F3"));
                            break;
                        case AnimatorControllerParameterType.Int:
                            sb.Append(",\"value\":").Append(animator.GetInteger(p.name));
                            break;
                        case AnimatorControllerParameterType.Bool:
                            sb.Append(",\"value\":").Append(animator.GetBool(p.name) ? "true" : "false");
                            break;
                        case AnimatorControllerParameterType.Trigger:
                            sb.Append(",\"value\":\"trigger\"");
                            break;
                    }
                    sb.Append("}");
                }
                sb.Append("]");
            }

            sb.Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleSetParam(string arguments, Animator animator, Action<UnityAgent.ToolResult> callback)
        {
            string paramName = UnityAgent.ExtractStringField(arguments, "paramName");
            string value = UnityAgent.ExtractStringField(arguments, "value");
            string paramType = UnityAgent.ExtractStringField(arguments, "paramType");

            if (string.IsNullOrEmpty(paramName))
            {
                callback(AgentToolHelpers.Fail("'paramName' is required for set_param."));
                return;
            }

            // Auto-detect type if not specified
            if (string.IsNullOrEmpty(paramType))
            {
                for (int i = 0; i < animator.parameterCount; i++)
                {
                    var p = animator.GetParameter(i);
                    if (p.name == paramName)
                    {
                        paramType = p.type.ToString().ToLower();
                        break;
                    }
                }
            }

            switch (paramType?.ToLower())
            {
                case "float":
                    animator.SetFloat(paramName, AgentToolHelpers.ParseFloat(value));
                    callback(AgentToolHelpers.Ok($"Set float '{paramName}' = {value}"));
                    break;
                case "int":
                case "integer":
                    animator.SetInteger(paramName, AgentToolHelpers.ParseInt(value));
                    callback(AgentToolHelpers.Ok($"Set int '{paramName}' = {value}"));
                    break;
                case "bool":
                case "boolean":
                    animator.SetBool(paramName, AgentToolHelpers.ParseBool(value));
                    callback(AgentToolHelpers.Ok($"Set bool '{paramName}' = {value}"));
                    break;
                case "trigger":
                    animator.SetTrigger(paramName);
                    callback(AgentToolHelpers.Ok($"Triggered '{paramName}'."));
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown param type '{paramType}'. Use: float, int, bool, trigger."));
                    break;
            }
        }
    }
}
