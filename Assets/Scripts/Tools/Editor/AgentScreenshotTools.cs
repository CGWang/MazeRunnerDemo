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
    /// Screenshot capture tools.
    /// Runtime: capture from specific camera.
    /// Editor: capture Scene View and Game View.
    /// Tools: captureView
    /// </summary>
    public class AgentScreenshotTools : MonoBehaviour
    {
        [SerializeField] private int defaultWidth = 800;
        [SerializeField] private int defaultHeight = 600;

        // =================================================================
        // Tool: captureView
        // =================================================================

        [AgentTool("captureView",
            "Capture a screenshot. Sources: 'game' (Game View / main camera), " +
            "'camera' (specific camera by name), 'scene' (Scene View, Editor only). " +
            "Returns the image as base64 PNG.",
            ParametersType = typeof(CaptureViewParams))]
        private IEnumerator HandleCaptureView(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string source = UnityAgent.ExtractStringField(arguments, "source") ?? "game";
            int width = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "width"), defaultWidth);
            int height = AgentToolHelpers.ParseInt(
                UnityAgent.ExtractNumberField(arguments, "height"), defaultHeight);

            width = Mathf.Clamp(width, 64, 1920);
            height = Mathf.Clamp(height, 64, 1080);

            switch (source.ToLower())
            {
                case "game":
                    yield return CaptureFromCamera(Camera.main, width, height, callback);
                    break;

                case "camera":
                    string camName = UnityAgent.ExtractStringField(arguments, "cameraName");
                    Camera cam = null;
                    if (!string.IsNullOrEmpty(camName))
                    {
                        var go = GameObject.Find(camName);
                        if (go != null) cam = go.GetComponent<Camera>();
                    }
                    if (cam == null)
                    {
                        callback(AgentToolHelpers.Fail(
                            $"Camera '{camName}' not found. Provide a valid GameObject name with a Camera component."));
                        yield break;
                    }
                    yield return CaptureFromCamera(cam, width, height, callback);
                    break;

#if UNITY_EDITOR
                case "scene":
                    yield return CaptureSceneView(width, height, callback);
                    break;
#endif

                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown source '{source}'. Use: game, camera, scene (Editor only)."));
                    break;
            }
        }

        public class CaptureViewParams
        {
            [ToolParam("Source: 'game' (main camera), 'camera' (named camera), 'scene' (Editor Scene View).")]
            public string source;
            [ToolParam("Camera GameObject name (for 'camera' source).")]
            public string cameraName;
            [ToolParam("Image width in pixels (default 800, max 1920).", SchemaType = "integer")]
            public int width;
            [ToolParam("Image height in pixels (default 600, max 1080).", SchemaType = "integer")]
            public int height;
        }

        private IEnumerator CaptureFromCamera(Camera cam, int width, int height, Action<UnityAgent.ToolResult> callback)
        {
            if (cam == null)
            {
                callback(AgentToolHelpers.Fail("No camera available for capture."));
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
                content = $"{{\"success\":true,\"width\":{width},\"height\":{height},\"size\":{png.Length}}}",
                imageBase64 = base64,
                imageMimeType = "image/png"
            });
        }

#if UNITY_EDITOR
        private IEnumerator CaptureSceneView(int width, int height, Action<UnityAgent.ToolResult> callback)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                callback(AgentToolHelpers.Fail("No active Scene View found."));
                yield break;
            }

            var cam = sceneView.camera;
            if (cam == null)
            {
                callback(AgentToolHelpers.Fail("Scene View camera not available."));
                yield break;
            }

            // Render scene view camera
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
            DestroyImmediate(rt);

            byte[] png = tex.EncodeToPNG();
            DestroyImmediate(tex);

            string base64 = Convert.ToBase64String(png);

            callback(new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"source\":\"scene\",\"width\":{width},\"height\":{height},\"size\":{png.Length}}}",
                imageBase64 = base64,
                imageMimeType = "image/png"
            });

            yield break;
        }
#endif
    }
}
