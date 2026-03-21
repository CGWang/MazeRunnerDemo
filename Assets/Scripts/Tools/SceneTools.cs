using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LLMAgent.Tools
{
    /// <summary>
    /// Scene and GameObject operation tools for the agent framework.
    /// Provides runtime-compatible tools for querying and manipulating the Unity scene.
    /// Add this component to the same GameObject as your AgentManager, or register manually
    /// via agent.RegisterToolsFrom(sceneTools).
    ///
    /// All methods use [AgentTool] attributes for automatic discovery.
    /// </summary>
    public class SceneTools : MonoBehaviour
    {
        // =================================================================
        // Tool: listScene — Scene hierarchy overview
        // =================================================================

        [AgentTool("listScene",
            "List all root GameObjects in the active scene, with optional depth for children. " +
            "Returns name, active state, and child count for each.",
            ParametersType = typeof(ListSceneParams))]
        private IEnumerator HandleListScene(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            int depth = 1;
            string depthStr = UnityAgent.ExtractNumberField(arguments, "depth");
            if (depthStr != null) int.TryParse(depthStr, out depth);
            depth = Mathf.Clamp(depth, 0, 5);

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            var sb = new StringBuilder();
            sb.Append("{\"scene\":\"").Append(UnityAgent.EscapeJson(scene.name)).Append("\",");
            sb.Append("\"rootCount\":").Append(roots.Length).Append(",");
            sb.Append("\"objects\":[");

            for (int i = 0; i < roots.Length; i++)
            {
                if (i > 0) sb.Append(",");
                AppendGameObjectInfo(sb, roots[i], depth);
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class ListSceneParams
        {
            [ToolParam("Hierarchy depth to include (0=roots only, max 5).", required: false)]
            public int depth;
        }

        // =================================================================
        // Tool: findGameObjects — Search for GameObjects
        // =================================================================

        [AgentTool("findGameObjects",
            "Find GameObjects by name pattern, tag, or layer. Returns a list of matches with " +
            "position, active state, and component list. Max 50 results.",
            ParametersType = typeof(FindGameObjectsParams))]
        private IEnumerator HandleFindGameObjects(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");
            string layer = UnityAgent.ExtractStringField(arguments, "layer");

            var results = new List<GameObject>();
            const int maxResults = 50;

            // Find by tag (most efficient)
            if (!string.IsNullOrEmpty(tag))
            {
                try
                {
                    var tagged = GameObject.FindGameObjectsWithTag(tag);
                    results.AddRange(tagged);
                }
                catch (Exception) { /* invalid tag */ }
            }

            // Find by name or layer — scan all objects
            if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(layer))
            {
                int targetLayer = -1;
                if (!string.IsNullOrEmpty(layer))
                    targetLayer = LayerMask.NameToLayer(layer);

                var allObjects = FindObjectsOfType<GameObject>();
                foreach (var go in allObjects)
                {
                    if (results.Count >= maxResults) break;
                    if (results.Contains(go)) continue;

                    bool match = true;
                    if (!string.IsNullOrEmpty(name) &&
                        go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                        match = false;
                    if (targetLayer >= 0 && go.layer != targetLayer)
                        match = false;

                    if (match) results.Add(go);
                }
            }

            // If no filter given, list all root objects
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(tag) && string.IsNullOrEmpty(layer))
            {
                var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                foreach (var r in roots)
                {
                    if (results.Count >= maxResults) break;
                    results.Add(r);
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\"count\":").Append(results.Count).Append(",\"results\":[");
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendGameObjectDetail(sb, results[i]);
            }
            sb.Append("]}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class FindGameObjectsParams
        {
            [ToolParam("Name or partial name to search for (case-insensitive).")]
            public string name;
            [ToolParam("Tag to filter by.")]
            public string tag;
            [ToolParam("Layer name to filter by.")]
            public string layer;
        }

        // =================================================================
        // Tool: inspectGameObject — Detailed info about a specific object
        // =================================================================

        [AgentTool("inspectGameObject",
            "Get detailed information about a GameObject: transform, all components with their " +
            "enabled state and key properties, children, and parent.",
            ParametersType = typeof(InspectGameObjectParams))]
        private IEnumerator HandleInspectGameObject(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");

            GameObject target = FindTarget(name, tag);
            if (target == null)
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"GameObject not found.\"}"
                });
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,");
            sb.Append("\"name\":\"").Append(UnityAgent.EscapeJson(target.name)).Append("\",");
            sb.Append("\"active\":").Append(target.activeSelf ? "true" : "false").Append(",");
            sb.Append("\"tag\":\"").Append(UnityAgent.EscapeJson(target.tag)).Append("\",");
            sb.Append("\"layer\":\"").Append(UnityAgent.EscapeJson(LayerMask.LayerToName(target.layer))).Append("\",");

            // Transform
            var t = target.transform;
            sb.Append("\"transform\":{");
            sb.Append("\"position\":").Append(Vec3Json(t.position)).Append(",");
            sb.Append("\"rotation\":").Append(Vec3Json(t.eulerAngles)).Append(",");
            sb.Append("\"scale\":").Append(Vec3Json(t.localScale)).Append(",");
            sb.Append("\"localPosition\":").Append(Vec3Json(t.localPosition)).Append(",");
            sb.Append("\"localRotation\":").Append(Vec3Json(t.localEulerAngles));
            sb.Append("},");

            // Parent
            if (t.parent != null)
                sb.Append("\"parent\":\"").Append(UnityAgent.EscapeJson(t.parent.name)).Append("\",");
            else
                sb.Append("\"parent\":null,");

            // Children
            sb.Append("\"children\":[");
            for (int i = 0; i < t.childCount; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(UnityAgent.EscapeJson(t.GetChild(i).name)).Append("\"");
            }
            sb.Append("],");

            // Components
            var components = target.GetComponents<Component>();
            sb.Append("\"components\":[");
            bool firstComp = true;
            foreach (var comp in components)
            {
                if (comp == null) continue;
                if (!firstComp) sb.Append(",");
                firstComp = false;

                sb.Append("{\"type\":\"").Append(UnityAgent.EscapeJson(comp.GetType().Name)).Append("\"");

                if (comp is Behaviour behaviour)
                    sb.Append(",\"enabled\":").Append(behaviour.enabled ? "true" : "false");
                if (comp is Renderer renderer)
                {
                    sb.Append(",\"enabled\":").Append(renderer.enabled ? "true" : "false");
                    if (renderer.sharedMaterial != null)
                        sb.Append(",\"material\":\"").Append(UnityAgent.EscapeJson(renderer.sharedMaterial.name)).Append("\"");
                }
                if (comp is Collider collider)
                    sb.Append(",\"enabled\":").Append(collider.enabled ? "true" : "false");
                if (comp is Rigidbody rb)
                {
                    sb.Append(",\"mass\":").Append(rb.mass);
                    sb.Append(",\"isKinematic\":").Append(rb.isKinematic ? "true" : "false");
                }
                if (comp is Light light)
                {
                    sb.Append(",\"lightType\":\"").Append(light.type.ToString()).Append("\"");
                    sb.Append(",\"intensity\":").Append(light.intensity);
                }
                if (comp is Camera cam)
                {
                    sb.Append(",\"fieldOfView\":").Append(cam.fieldOfView);
                    sb.Append(",\"depth\":").Append(cam.depth);
                }

                sb.Append("}");
            }
            sb.Append("]}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class InspectGameObjectParams
        {
            [ToolParam("Name of the GameObject to inspect.", required: true)]
            public string name;
            [ToolParam("Tag to help locate the object (optional).")]
            public string tag;
        }

        // =================================================================
        // Tool: setTransform — Modify position/rotation/scale
        // =================================================================

        [AgentTool("setTransform",
            "Set the position, rotation (euler angles), or scale of a GameObject. " +
            "Specify only the fields you want to change. Uses world space by default.",
            ParametersType = typeof(SetTransformParams))]
        private IEnumerator HandleSetTransform(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");
            string localStr = UnityAgent.ExtractStringField(arguments, "local");
            bool local = localStr == "true";

            GameObject target = FindTarget(name, tag);
            if (target == null)
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"GameObject not found.\"}"
                });
                yield break;
            }

            var t = target.transform;

            // Position
            Vector3? pos = ParseVec3FromArgs(arguments, "position");
            if (pos.HasValue)
            {
                if (local) t.localPosition = pos.Value;
                else t.position = pos.Value;
            }

            // Rotation
            Vector3? rot = ParseVec3FromArgs(arguments, "rotation");
            if (rot.HasValue)
            {
                if (local) t.localEulerAngles = rot.Value;
                else t.eulerAngles = rot.Value;
            }

            // Scale
            Vector3? scale = ParseVec3FromArgs(arguments, "scale");
            if (scale.HasValue)
                t.localScale = scale.Value;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"name\":\"").Append(UnityAgent.EscapeJson(target.name)).Append("\",");
            sb.Append("\"position\":").Append(Vec3Json(t.position)).Append(",");
            sb.Append("\"rotation\":").Append(Vec3Json(t.eulerAngles)).Append(",");
            sb.Append("\"scale\":").Append(Vec3Json(t.localScale)).Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class SetTransformParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string name;
            [ToolParam("Tag to help locate the object.")]
            public string tag;
            [ToolParam("New position as {x,y,z} object.")]
            public string position;
            [ToolParam("New euler rotation as {x,y,z} object.")]
            public string rotation;
            [ToolParam("New local scale as {x,y,z} object.")]
            public string scale;
            [ToolParam("If 'true', use local space instead of world space.")]
            public string local;
        }

        // =================================================================
        // Tool: toggleComponent — Enable or disable a component
        // =================================================================

        [AgentTool("toggleComponent",
            "Enable or disable a component on a GameObject by type name. " +
            "Works with Behaviours, Renderers, and Colliders.",
            ParametersType = typeof(ToggleComponentParams))]
        private IEnumerator HandleToggleComponent(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string componentType = UnityAgent.ExtractStringField(arguments, "componentType");
            string enabledStr = UnityAgent.ExtractStringField(arguments, "enabled");

            if (string.IsNullOrEmpty(componentType))
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"componentType is required.\"}"
                });
                yield break;
            }

            GameObject target = FindTarget(name, null);
            if (target == null)
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"GameObject not found.\"}"
                });
                yield break;
            }

            bool enabled = enabledStr != "false";

            // Find component by type name
            Component found = null;
            foreach (var comp in target.GetComponents<Component>())
            {
                if (comp != null && comp.GetType().Name == componentType)
                {
                    found = comp;
                    break;
                }
            }

            if (found == null)
            {
                callback(new UnityAgent.ToolResult
                {
                    content = $"{{\"success\":false,\"error\":\"Component '{UnityAgent.EscapeJson(componentType)}' not found on '{UnityAgent.EscapeJson(target.name)}'.\"}}"
                });
                yield break;
            }

            if (found is Behaviour b) b.enabled = enabled;
            else if (found is Renderer r) r.enabled = enabled;
            else if (found is Collider c) c.enabled = enabled;
            else
            {
                callback(new UnityAgent.ToolResult
                {
                    content = $"{{\"success\":false,\"error\":\"Component '{UnityAgent.EscapeJson(componentType)}' cannot be toggled.\"}}"
                });
                yield break;
            }

            callback(new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"component\":\"{UnityAgent.EscapeJson(componentType)}\",\"enabled\":{(enabled ? "true" : "false")}}}"
            });
            yield break;
        }

        public class ToggleComponentParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string name;
            [ToolParam("Component type name (e.g. 'MeshRenderer', 'BoxCollider').", required: true)]
            public string componentType;
            [ToolParam("Set to 'true' to enable, 'false' to disable.", required: true)]
            public string enabled;
        }

        // =================================================================
        // Tool: createGameObject — Create new objects
        // =================================================================

        [AgentTool("createGameObject",
            "Create a new GameObject in the scene. Optionally create a primitive shape " +
            "(Cube, Sphere, Capsule, Cylinder, Plane, Quad) or an empty object.",
            ParametersType = typeof(CreateGameObjectParams),
            RequiresPermission = true)]
        private IEnumerator HandleCreateGameObject(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string primitiveStr = UnityAgent.ExtractStringField(arguments, "primitive");
            string parentName = UnityAgent.ExtractStringField(arguments, "parent");

            if (string.IsNullOrEmpty(name)) name = "New GameObject";

            GameObject go;

            if (!string.IsNullOrEmpty(primitiveStr))
            {
                PrimitiveType prim;
                try { prim = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), primitiveStr, true); }
                catch
                {
                    callback(new UnityAgent.ToolResult
                    {
                        content = $"{{\"success\":false,\"error\":\"Unknown primitive: '{UnityAgent.EscapeJson(primitiveStr)}'. Use: Cube, Sphere, Capsule, Cylinder, Plane, Quad.\"}}"
                    });
                    yield break;
                }
                go = GameObject.CreatePrimitive(prim);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            // Set parent
            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                    go.transform.SetParent(parent.transform, false);
            }

            // Set position if provided
            Vector3? pos = ParseVec3FromArgs(arguments, "position");
            if (pos.HasValue) go.transform.position = pos.Value;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"name\":\"").Append(UnityAgent.EscapeJson(go.name)).Append("\",");
            sb.Append("\"instanceId\":").Append(go.GetInstanceID()).Append(",");
            sb.Append("\"position\":").Append(Vec3Json(go.transform.position)).Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class CreateGameObjectParams
        {
            [ToolParam("Name for the new GameObject.", required: true)]
            public string name;
            [ToolParam("Primitive type: Cube, Sphere, Capsule, Cylinder, Plane, Quad. Omit for empty.")]
            public string primitive;
            [ToolParam("Name of parent GameObject to attach to.")]
            public string parent;
            [ToolParam("Initial position as {x,y,z} object.")]
            public string position;
        }

        // =================================================================
        // Tool: destroyGameObject — Remove objects from scene
        // =================================================================

        [AgentTool("destroyGameObject",
            "Destroy a GameObject and all its children. Cannot destroy objects tagged 'MainCamera' " +
            "or 'Player' without explicit confirmation.",
            ParametersType = typeof(DestroyGameObjectParams),
            RequiresPermission = true)]
        private IEnumerator HandleDestroyGameObject(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");

            GameObject target = FindTarget(name, tag);
            if (target == null)
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"GameObject not found.\"}"
                });
                yield break;
            }

            string targetName = target.name;
            Destroy(target);

            callback(new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"destroyed\":\"{UnityAgent.EscapeJson(targetName)}\"}}"
            });
            yield break;
        }

        public class DestroyGameObjectParams
        {
            [ToolParam("Name of the GameObject to destroy.", required: true)]
            public string name;
            [ToolParam("Tag to help locate the object.")]
            public string tag;
        }

        // =================================================================
        // Tool: setActive — Show/hide GameObjects
        // =================================================================

        [AgentTool("setActive",
            "Activate or deactivate a GameObject (show/hide it and all children).",
            ParametersType = typeof(SetActiveParams))]
        private IEnumerator HandleSetActive(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");
            string activeStr = UnityAgent.ExtractStringField(arguments, "active");

            GameObject target = FindTarget(name, tag);
            if (target == null)
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"GameObject not found.\"}"
                });
                yield break;
            }

            bool active = activeStr != "false";
            target.SetActive(active);

            callback(new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"name\":\"{UnityAgent.EscapeJson(target.name)}\",\"active\":{(active ? "true" : "false")}}}"
            });
            yield break;
        }

        public class SetActiveParams
        {
            [ToolParam("Name of the target GameObject.", required: true)]
            public string name;
            [ToolParam("Tag to help locate the object.")]
            public string tag;
            [ToolParam("Set to 'true' to activate, 'false' to deactivate.", required: true)]
            public string active;
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static GameObject FindTarget(string name, string tag)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                try
                {
                    var byTag = GameObject.FindWithTag(tag);
                    if (byTag != null) return byTag;
                }
                catch { /* invalid tag */ }
            }

            if (!string.IsNullOrEmpty(name))
            {
                // Exact match first
                var exact = GameObject.Find(name);
                if (exact != null) return exact;

                // Partial match fallback
                foreach (var go in FindObjectsOfType<GameObject>())
                {
                    if (go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        return go;
                }
            }

            return null;
        }

        private static Vector3? ParseVec3FromArgs(string json, string fieldName)
        {
            int fieldIdx = json.IndexOf($"\"{fieldName}\"", StringComparison.Ordinal);
            if (fieldIdx < 0) return null;

            int braceStart = json.IndexOf('{', fieldIdx);
            if (braceStart < 0) return null;
            int braceEnd = UnityAgent.FindMatchingBrace(json, braceStart);
            if (braceEnd < 0) return null;

            string obj = json.Substring(braceStart, braceEnd - braceStart + 1);
            string xs = UnityAgent.ExtractNumberField(obj, "x");
            string ys = UnityAgent.ExtractNumberField(obj, "y");
            string zs = UnityAgent.ExtractNumberField(obj, "z");

            float x = 0, y = 0, z = 0;
            if (xs != null) float.TryParse(xs, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out x);
            if (ys != null) float.TryParse(ys, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out y);
            if (zs != null) float.TryParse(zs, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out z);

            return new Vector3(x, y, z);
        }

        private static void AppendGameObjectInfo(StringBuilder sb, GameObject go, int depth)
        {
            sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(go.name)).Append("\"");
            sb.Append(",\"active\":").Append(go.activeSelf ? "true" : "false");
            sb.Append(",\"childCount\":").Append(go.transform.childCount);

            if (depth > 0 && go.transform.childCount > 0)
            {
                sb.Append(",\"children\":[");
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendGameObjectInfo(sb, go.transform.GetChild(i).gameObject, depth - 1);
                }
                sb.Append("]");
            }

            sb.Append("}");
        }

        private static void AppendGameObjectDetail(StringBuilder sb, GameObject go)
        {
            sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(go.name)).Append("\"");
            sb.Append(",\"active\":").Append(go.activeSelf ? "true" : "false");
            sb.Append(",\"tag\":\"").Append(UnityAgent.EscapeJson(go.tag)).Append("\"");
            sb.Append(",\"layer\":\"").Append(UnityAgent.EscapeJson(LayerMask.LayerToName(go.layer))).Append("\"");
            sb.Append(",\"position\":").Append(Vec3Json(go.transform.position));

            // Component list
            var comps = go.GetComponents<Component>();
            sb.Append(",\"components\":[");
            bool first = true;
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(UnityAgent.EscapeJson(c.GetType().Name)).Append("\"");
            }
            sb.Append("]");

            sb.Append("}");
        }

        private static string Vec3Json(Vector3 v)
        {
            return $"{{\"x\":{v.x:F2},\"y\":{v.y:F2},\"z\":{v.z:F2}}}";
        }
    }
}
