using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Camera management tools for creating, configuring, and capturing from cameras.
    /// Supports basic Unity cameras and optional Cinemachine integration via reflection.
    /// Tools: manageCamera
    /// </summary>
    public class AgentCameraTools : MonoBehaviour
    {
        [SerializeField] private int defaultScreenshotWidth = 800;
        [SerializeField] private int defaultScreenshotHeight = 600;

        // Cinemachine reflection cache
        private static bool _cinemachineChecked;
        private static bool _hasCinemachine;
        private static Type _cmCameraType;
        private static Type _cmBrainType;

        // =================================================================
        // Tool: manageCamera
        // =================================================================

        [AgentTool("manageCamera",
            "Manage cameras in the scene. Actions: " +
            "'create' (create a new camera), " +
            "'list' (list all cameras with properties), " +
            "'set_lens' (set FOV, near/far clip, orthographic settings), " +
            "'set_target' (set camera look-at target), " +
            "'set_priority' (set camera depth/priority), " +
            "'screenshot' (capture from a specific camera as base64 PNG), " +
            "'get_info' (get detailed camera info).",
            ParametersType = typeof(CameraParams))]
        private IEnumerator HandleManageCamera(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required. Valid actions: create, list, set_lens, set_target, set_priority, screenshot, get_info."));
                yield break;
            }

            switch (action.ToLower())
            {
                case "create":
                    yield return HandleCreate(arguments, callback);
                    break;
                case "list":
                    yield return HandleList(arguments, callback);
                    break;
                case "set_lens":
                    yield return HandleSetLens(arguments, callback);
                    break;
                case "set_target":
                    yield return HandleSetTarget(arguments, callback);
                    break;
                case "set_priority":
                    yield return HandleSetPriority(arguments, callback);
                    break;
                case "screenshot":
                    yield return HandleScreenshot(arguments, callback);
                    break;
                case "get_info":
                    yield return HandleGetInfo(arguments, callback);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Valid actions: create, list, set_lens, set_target, set_priority, screenshot, get_info."));
                    break;
            }
        }

        public class CameraParams
        {
            [ToolParam("Action to perform: 'create', 'list', 'set_lens', 'set_target', 'set_priority', 'screenshot', 'get_info'.", required: true)]
            public string action;
            [ToolParam("Name for the new camera or name of existing camera to operate on.")]
            public string cameraName;
            [ToolParam("Field of view in degrees (for set_lens).")]
            public string fieldOfView;
            [ToolParam("Near clip plane distance (for set_lens).")]
            public string nearClipPlane;
            [ToolParam("Far clip plane distance (for set_lens).")]
            public string farClipPlane;
            [ToolParam("Orthographic size (for set_lens, when using orthographic projection).")]
            public string orthographicSize;
            [ToolParam("Whether the camera is orthographic: 'true' or 'false' (for set_lens).")]
            public string orthographic;
            [ToolParam("Name of the target GameObject to look at (for set_target).")]
            public string targetName;
            [ToolParam("Camera depth/priority value (for set_priority).", SchemaType = "number")]
            public float priority;
            [ToolParam("Screenshot width in pixels.", SchemaType = "integer")]
            public int width;
            [ToolParam("Screenshot height in pixels.", SchemaType = "integer")]
            public int height;
        }

        // =================================================================
        // Cinemachine detection
        // =================================================================

        private static void EnsureCinemachineDetected()
        {
            if (_cinemachineChecked) return;
            _cinemachineChecked = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == "CinemachineCamera" && typeof(Component).IsAssignableFrom(t))
                            _cmCameraType = t;
                        if (t.Name == "CinemachineBrain" && typeof(Component).IsAssignableFrom(t))
                            _cmBrainType = t;
                    }
                }
                catch { /* skip assemblies that throw on GetTypes */ }
            }

            _hasCinemachine = _cmCameraType != null && _cmBrainType != null;
        }

        private static object GetReflectionProperty(Component component, string propertyName)
        {
            if (component == null) return null;
            var prop = component.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(component);
        }

        // =================================================================
        // Action: create
        // =================================================================

        private IEnumerator HandleCreate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "cameraName") ?? "New Camera";
            string fovStr = UnityAgent.ExtractNumberField(arguments, "fieldOfView");
            string nearStr = UnityAgent.ExtractNumberField(arguments, "nearClipPlane");
            string farStr = UnityAgent.ExtractNumberField(arguments, "farClipPlane");

            float fov = AgentToolHelpers.ParseFloat(fovStr, 60f);
            float near = AgentToolHelpers.ParseFloat(nearStr, 0.3f);
            float far = AgentToolHelpers.ParseFloat(farStr, 1000f);

            var go = new GameObject(name);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, $"Create Camera '{name}'");
#endif
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
            cam.farClipPlane = far;

            // If a target is provided, position near it and look at it
            string targetName = UnityAgent.ExtractStringField(arguments, "targetName");
            if (!string.IsNullOrEmpty(targetName))
            {
                var target = AgentToolHelpers.FindTarget(targetName, null);
                if (target != null)
                {
                    go.transform.position = target.transform.position + new Vector3(0, 5, -10);
                    go.transform.LookAt(target.transform);
                }
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(go);
#endif

            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"message\":\"Created camera '").Append(UnityAgent.EscapeJson(name)).Append("'\"");
            sb.Append(",\"instanceID\":").Append(go.GetInstanceID());
            sb.Append(",\"fieldOfView\":").Append(fov.ToString("F1"));
            sb.Append(",\"nearClipPlane\":").Append(near.ToString("F2"));
            sb.Append(",\"farClipPlane\":").Append(far.ToString("F1"));
            sb.Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // =================================================================
        // Action: list
        // =================================================================

        private IEnumerator HandleList(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            EnsureCinemachineDetected();

            var allCameras = FindObjectsOfType<Camera>();
            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"cinemachineInstalled\":").Append(_hasCinemachine ? "true" : "false");
            sb.Append(",\"cameras\":[");

            for (int i = 0; i < allCameras.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var cam = allCameras[i];
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(cam.gameObject.name)).Append("\"");
                sb.Append(",\"instanceID\":").Append(cam.gameObject.GetInstanceID());
                sb.Append(",\"enabled\":").Append(cam.enabled ? "true" : "false");
                sb.Append(",\"depth\":").Append(cam.depth.ToString("F1"));
                sb.Append(",\"fieldOfView\":").Append(cam.fieldOfView.ToString("F1"));
                sb.Append(",\"nearClipPlane\":").Append(cam.nearClipPlane.ToString("F2"));
                sb.Append(",\"farClipPlane\":").Append(cam.farClipPlane.ToString("F1"));
                sb.Append(",\"orthographic\":").Append(cam.orthographic ? "true" : "false");
                if (cam.orthographic)
                    sb.Append(",\"orthographicSize\":").Append(cam.orthographicSize.ToString("F2"));
                sb.Append(",\"isMain\":").Append(cam == Camera.main ? "true" : "false");
                sb.Append(",\"position\":").Append(AgentToolHelpers.Vec3Json(cam.transform.position));
                sb.Append(",\"rotation\":").Append(AgentToolHelpers.Vec3Json(cam.transform.eulerAngles));

                // Check for Cinemachine Brain
                if (_hasCinemachine && _cmBrainType != null)
                {
                    var brain = cam.gameObject.GetComponent(_cmBrainType);
                    sb.Append(",\"hasCinemachineBrain\":").Append(brain != null ? "true" : "false");
                }

                sb.Append("}");
            }

            sb.Append("]");

            // List Cinemachine virtual cameras if available
            if (_hasCinemachine && _cmCameraType != null)
            {
                var cmCameras = FindObjectsOfType(_cmCameraType);
                sb.Append(",\"cinemachineCameras\":[");
                for (int i = 0; i < cmCameras.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    var cmCam = cmCameras[i] as Component;
                    if (cmCam == null) continue;

                    sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(cmCam.gameObject.name)).Append("\"");
                    sb.Append(",\"instanceID\":").Append(cmCam.gameObject.GetInstanceID());

                    // Priority via reflection
                    var priorityVal = GetReflectionProperty(cmCam, "Priority");
                    if (priorityVal != null)
                        sb.Append(",\"priority\":").Append(priorityVal.ToString());

                    // Follow target
                    var follow = GetReflectionProperty(cmCam, "Follow") as Transform;
                    if (follow != null)
                        sb.Append(",\"follow\":\"").Append(UnityAgent.EscapeJson(follow.gameObject.name)).Append("\"");

                    // LookAt target
                    var lookAt = GetReflectionProperty(cmCam, "LookAt") as Transform;
                    if (lookAt != null)
                        sb.Append(",\"lookAt\":\"").Append(UnityAgent.EscapeJson(lookAt.gameObject.name)).Append("\"");

                    // IsLive
                    var isLive = GetReflectionProperty(cmCam, "IsLive");
                    if (isLive is bool live)
                        sb.Append(",\"isLive\":").Append(live ? "true" : "false");

                    sb.Append(",\"position\":").Append(AgentToolHelpers.Vec3Json(cmCam.transform.position));
                    sb.Append(",\"rotation\":").Append(AgentToolHelpers.Vec3Json(cmCam.transform.eulerAngles));
                    sb.Append("}");
                }
                sb.Append("]");
            }

            sb.Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // =================================================================
        // Action: set_lens
        // =================================================================

        private IEnumerator HandleSetLens(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string camName = UnityAgent.ExtractStringField(arguments, "cameraName");
            Camera cam = FindCameraByName(camName);
            if (cam == null)
            {
                callback(AgentToolHelpers.Fail(
                    $"Camera '{camName ?? "(null)"}' not found. Provide a valid cameraName."));
                yield break;
            }

#if UNITY_EDITOR
            Undo.RecordObject(cam, "Set Camera Lens");
#endif

            string fovStr = UnityAgent.ExtractNumberField(arguments, "fieldOfView");
            if (fovStr != null)
                cam.fieldOfView = AgentToolHelpers.ParseFloat(fovStr, cam.fieldOfView);

            string nearStr = UnityAgent.ExtractNumberField(arguments, "nearClipPlane");
            if (nearStr != null)
                cam.nearClipPlane = AgentToolHelpers.ParseFloat(nearStr, cam.nearClipPlane);

            string farStr = UnityAgent.ExtractNumberField(arguments, "farClipPlane");
            if (farStr != null)
                cam.farClipPlane = AgentToolHelpers.ParseFloat(farStr, cam.farClipPlane);

            string orthoSizeStr = UnityAgent.ExtractNumberField(arguments, "orthographicSize");
            if (orthoSizeStr != null)
                cam.orthographicSize = AgentToolHelpers.ParseFloat(orthoSizeStr, cam.orthographicSize);

            string orthoStr = UnityAgent.ExtractStringField(arguments, "orthographic");
            if (orthoStr != null)
                cam.orthographic = AgentToolHelpers.ParseBool(orthoStr, cam.orthographic);

#if UNITY_EDITOR
            EditorUtility.SetDirty(cam.gameObject);
#endif

            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"message\":\"Lens properties set on camera '").Append(UnityAgent.EscapeJson(cam.gameObject.name)).Append("'\"");
            sb.Append(",\"fieldOfView\":").Append(cam.fieldOfView.ToString("F1"));
            sb.Append(",\"nearClipPlane\":").Append(cam.nearClipPlane.ToString("F2"));
            sb.Append(",\"farClipPlane\":").Append(cam.farClipPlane.ToString("F1"));
            sb.Append(",\"orthographic\":").Append(cam.orthographic ? "true" : "false");
            if (cam.orthographic)
                sb.Append(",\"orthographicSize\":").Append(cam.orthographicSize.ToString("F2"));
            sb.Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // =================================================================
        // Action: set_target
        // =================================================================

        private IEnumerator HandleSetTarget(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string camName = UnityAgent.ExtractStringField(arguments, "cameraName");
            string targetName = UnityAgent.ExtractStringField(arguments, "targetName");

            if (string.IsNullOrEmpty(targetName))
            {
                callback(AgentToolHelpers.Fail("'targetName' is required for set_target action."));
                yield break;
            }

            // Try Cinemachine camera first
            EnsureCinemachineDetected();
            if (_hasCinemachine && _cmCameraType != null && !string.IsNullOrEmpty(camName))
            {
                var cmCam = FindCinemachineCameraByName(camName);
                if (cmCam != null)
                {
                    var target = AgentToolHelpers.FindTarget(targetName, null);
                    if (target == null)
                    {
                        callback(AgentToolHelpers.Fail($"Target GameObject '{targetName}' not found."));
                        yield break;
                    }

#if UNITY_EDITOR
                    Undo.RecordObject(cmCam, "Set Cinemachine Target");
#endif

                    // Set Follow and LookAt via reflection
                    var followProp = cmCam.GetType().GetProperty("Follow",
                        BindingFlags.Public | BindingFlags.Instance);
                    var lookAtProp = cmCam.GetType().GetProperty("LookAt",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (followProp != null && followProp.CanWrite)
                        followProp.SetValue(cmCam, target.transform);
                    if (lookAtProp != null && lookAtProp.CanWrite)
                        lookAtProp.SetValue(cmCam, target.transform);

#if UNITY_EDITOR
                    EditorUtility.SetDirty(cmCam.gameObject);
#endif

                    callback(AgentToolHelpers.Ok(
                        $"Cinemachine camera '{cmCam.gameObject.name}' now targeting '{target.name}'."));
                    yield break;
                }
            }

            // Fall back to basic Camera
            Camera cam = FindCameraByName(camName);
            if (cam == null)
            {
                callback(AgentToolHelpers.Fail(
                    $"Camera '{camName ?? "(null)"}' not found."));
                yield break;
            }

            var lookAtTarget = AgentToolHelpers.FindTarget(targetName, null);
            if (lookAtTarget == null)
            {
                callback(AgentToolHelpers.Fail($"Target GameObject '{targetName}' not found."));
                yield break;
            }

#if UNITY_EDITOR
            Undo.RecordObject(cam.transform, "Set Camera Target");
#endif

            cam.transform.LookAt(lookAtTarget.transform);

#if UNITY_EDITOR
            EditorUtility.SetDirty(cam.gameObject);
#endif

            callback(AgentToolHelpers.Ok(
                $"Camera '{cam.gameObject.name}' now looking at '{lookAtTarget.name}'."));
            yield break;
        }

        // =================================================================
        // Action: set_priority
        // =================================================================

        private IEnumerator HandleSetPriority(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string camName = UnityAgent.ExtractStringField(arguments, "cameraName");
            string priorityStr = UnityAgent.ExtractNumberField(arguments, "priority");

            if (priorityStr == null)
            {
                callback(AgentToolHelpers.Fail("'priority' is required for set_priority action."));
                yield break;
            }

            float priorityVal = AgentToolHelpers.ParseFloat(priorityStr, 0f);

            // Try Cinemachine camera first
            EnsureCinemachineDetected();
            if (_hasCinemachine && _cmCameraType != null && !string.IsNullOrEmpty(camName))
            {
                var cmCam = FindCinemachineCameraByName(camName);
                if (cmCam != null)
                {
#if UNITY_EDITOR
                    Undo.RecordObject(cmCam, "Set Cinemachine Priority");
#endif

                    var prop = cmCam.GetType().GetProperty("Priority",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanWrite)
                    {
                        try
                        {
                            prop.SetValue(cmCam, (int)priorityVal);
                        }
                        catch
                        {
                            // Priority might be a struct type; try int conversion
                            try { prop.SetValue(cmCam, Convert.ChangeType((int)priorityVal, prop.PropertyType)); }
                            catch { /* best effort */ }
                        }
                    }

#if UNITY_EDITOR
                    EditorUtility.SetDirty(cmCam.gameObject);
#endif

                    callback(AgentToolHelpers.Ok(
                        $"Cinemachine camera '{cmCam.gameObject.name}' priority set to {(int)priorityVal}."));
                    yield break;
                }
            }

            // Fall back to basic Camera depth
            Camera cam = FindCameraByName(camName);
            if (cam == null)
            {
                callback(AgentToolHelpers.Fail(
                    $"Camera '{camName ?? "(null)"}' not found."));
                yield break;
            }

#if UNITY_EDITOR
            Undo.RecordObject(cam, "Set Camera Depth");
#endif

            cam.depth = priorityVal;

#if UNITY_EDITOR
            EditorUtility.SetDirty(cam.gameObject);
#endif

            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"message\":\"Camera '").Append(UnityAgent.EscapeJson(cam.gameObject.name));
            sb.Append("' depth set to ").Append(priorityVal.ToString("F1")).Append("\"");
            sb.Append(",\"depth\":").Append(priorityVal.ToString("F1"));
            sb.Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        // =================================================================
        // Action: screenshot
        // =================================================================

        private IEnumerator HandleScreenshot(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string camName = UnityAgent.ExtractStringField(arguments, "cameraName");
            int width = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "width"), defaultScreenshotWidth);
            int height = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "height"), defaultScreenshotHeight);

            width = Mathf.Clamp(width, 64, 1920);
            height = Mathf.Clamp(height, 64, 1080);

            Camera cam = FindCameraByName(camName);
            if (cam == null)
            {
                // Fall back to main camera
                cam = Camera.main;
            }

            if (cam == null)
            {
                callback(AgentToolHelpers.Fail(
                    "No camera available for screenshot. Provide a valid cameraName or ensure a main camera exists."));
                yield break;
            }

            yield return new WaitForEndOfFrame();

            var rt = new RenderTexture(width, height, 24);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();

            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = prevTarget;
            RenderTexture.active = null;
            Destroy(rt);

            byte[] png = tex.EncodeToPNG();
            Destroy(tex);

            string base64 = Convert.ToBase64String(png);

            callback(new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"camera\":\"{UnityAgent.EscapeJson(cam.gameObject.name)}\",\"width\":{width},\"height\":{height},\"size\":{png.Length}}}",
                imageBase64 = base64,
                imageMimeType = "image/png"
            });
        }

        // =================================================================
        // Action: get_info
        // =================================================================

        private IEnumerator HandleGetInfo(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string camName = UnityAgent.ExtractStringField(arguments, "cameraName");
            EnsureCinemachineDetected();

            // Try Cinemachine camera first
            if (_hasCinemachine && _cmCameraType != null && !string.IsNullOrEmpty(camName))
            {
                var cmCam = FindCinemachineCameraByName(camName);
                if (cmCam != null)
                {
                    var sb = new StringBuilder();
                    sb.Append("{\"success\":true,\"type\":\"CinemachineCamera\"");
                    sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(cmCam.gameObject.name)).Append("\"");
                    sb.Append(",\"instanceID\":").Append(cmCam.gameObject.GetInstanceID());
                    sb.Append(",\"position\":").Append(AgentToolHelpers.Vec3Json(cmCam.transform.position));
                    sb.Append(",\"rotation\":").Append(AgentToolHelpers.Vec3Json(cmCam.transform.eulerAngles));

                    var priorityVal = GetReflectionProperty(cmCam, "Priority");
                    if (priorityVal != null)
                        sb.Append(",\"priority\":").Append(priorityVal.ToString());

                    var follow = GetReflectionProperty(cmCam, "Follow") as Transform;
                    if (follow != null)
                        sb.Append(",\"follow\":\"").Append(UnityAgent.EscapeJson(follow.gameObject.name)).Append("\"");

                    var lookAt = GetReflectionProperty(cmCam, "LookAt") as Transform;
                    if (lookAt != null)
                        sb.Append(",\"lookAt\":\"").Append(UnityAgent.EscapeJson(lookAt.gameObject.name)).Append("\"");

                    var isLive = GetReflectionProperty(cmCam, "IsLive");
                    if (isLive is bool live)
                        sb.Append(",\"isLive\":").Append(live ? "true" : "false");

                    sb.Append("}");

                    callback(new UnityAgent.ToolResult { content = sb.ToString() });
                    yield break;
                }
            }

            // Basic Camera
            Camera cam = FindCameraByName(camName);
            if (cam == null)
            {
                callback(AgentToolHelpers.Fail(
                    $"Camera '{camName ?? "(null)"}' not found."));
                yield break;
            }

            {
                var sb = new StringBuilder();
                sb.Append("{\"success\":true,\"type\":\"Camera\"");
                sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(cam.gameObject.name)).Append("\"");
                sb.Append(",\"instanceID\":").Append(cam.gameObject.GetInstanceID());
                sb.Append(",\"enabled\":").Append(cam.enabled ? "true" : "false");
                sb.Append(",\"isMain\":").Append(cam == Camera.main ? "true" : "false");
                sb.Append(",\"position\":").Append(AgentToolHelpers.Vec3Json(cam.transform.position));
                sb.Append(",\"rotation\":").Append(AgentToolHelpers.Vec3Json(cam.transform.eulerAngles));
                sb.Append(",\"fieldOfView\":").Append(cam.fieldOfView.ToString("F1"));
                sb.Append(",\"nearClipPlane\":").Append(cam.nearClipPlane.ToString("F2"));
                sb.Append(",\"farClipPlane\":").Append(cam.farClipPlane.ToString("F1"));
                sb.Append(",\"depth\":").Append(cam.depth.ToString("F1"));
                sb.Append(",\"orthographic\":").Append(cam.orthographic ? "true" : "false");
                if (cam.orthographic)
                    sb.Append(",\"orthographicSize\":").Append(cam.orthographicSize.ToString("F2"));
                sb.Append(",\"clearFlags\":\"").Append(cam.clearFlags.ToString()).Append("\"");
                sb.Append(",\"cullingMask\":").Append(cam.cullingMask);
                sb.Append(",\"rect\":{\"x\":").Append(cam.rect.x.ToString("F2"));
                sb.Append(",\"y\":").Append(cam.rect.y.ToString("F2"));
                sb.Append(",\"width\":").Append(cam.rect.width.ToString("F2"));
                sb.Append(",\"height\":").Append(cam.rect.height.ToString("F2")).Append("}");

                // Check for Cinemachine Brain
                if (_hasCinemachine && _cmBrainType != null)
                {
                    var brain = cam.gameObject.GetComponent(_cmBrainType);
                    sb.Append(",\"hasCinemachineBrain\":").Append(brain != null ? "true" : "false");
                    if (brain != null)
                    {
                        var activeCam = GetReflectionProperty(brain as Component, "ActiveVirtualCamera");
                        if (activeCam != null)
                        {
                            var nameProp = activeCam.GetType().GetProperty("Name");
                            var activeName = nameProp?.GetValue(activeCam) as string;
                            if (activeName != null)
                                sb.Append(",\"activeCinemachineCamera\":\"").Append(UnityAgent.EscapeJson(activeName)).Append("\"");
                        }

                        var isBlending = GetReflectionProperty(brain as Component, "IsBlending");
                        if (isBlending is bool blending)
                            sb.Append(",\"isBlending\":").Append(blending ? "true" : "false");
                    }
                }

                sb.Append("}");

                callback(new UnityAgent.ToolResult { content = sb.ToString() });
            }
            yield break;
        }

        // =================================================================
        // Helpers
        // =================================================================

        /// <summary>
        /// Find a Camera component by GameObject name. Falls back to main camera if name is null/empty.
        /// </summary>
        private Camera FindCameraByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return Camera.main;

            // Exact match
            var go = GameObject.Find(name);
            if (go != null)
            {
                var cam = go.GetComponent<Camera>();
                if (cam != null) return cam;
            }

            // Partial / case-insensitive match
            foreach (var cam in FindObjectsOfType<Camera>())
            {
                if (cam.gameObject.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return cam;
            }

            return null;
        }

        /// <summary>
        /// Find a Cinemachine camera component by GameObject name via reflection.
        /// </summary>
        private Component FindCinemachineCameraByName(string name)
        {
            if (_cmCameraType == null || string.IsNullOrEmpty(name)) return null;

            // Exact match
            var go = GameObject.Find(name);
            if (go != null)
            {
                var cmCam = go.GetComponent(_cmCameraType);
                if (cmCam != null) return cmCam;
            }

            // Partial / case-insensitive search
            var allCm = FindObjectsOfType(_cmCameraType);
            foreach (var obj in allCm)
            {
                var comp = obj as Component;
                if (comp != null &&
                    comp.gameObject.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return comp;
            }

            return null;
        }
    }
}
