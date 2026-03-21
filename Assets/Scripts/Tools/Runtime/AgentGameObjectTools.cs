using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace LLMAgent.Tools
{
    /// <summary>
    /// GameObject manipulation tools (runtime-compatible).
    /// Tools: inspectGameObject, createGameObject, destroyGameObject, setActive,
    ///        setTransform, duplicateGameObject, setParent
    /// </summary>
    public class AgentGameObjectTools : MonoBehaviour
    {
        // =================================================================
        // Tool: inspectGameObject
        // =================================================================

        [AgentTool("inspectGameObject",
            "Get detailed information about a GameObject: transform, all components with their " +
            "enabled state and key properties, children, and parent.",
            ParametersType = typeof(InspectParams))]
        private IEnumerator HandleInspect(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");

            GameObject target = AgentToolHelpers.FindTarget(name, tag);
            if (target == null)
            {
                callback(AgentToolHelpers.Fail("GameObject not found."));
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,");
            sb.Append("\"name\":\"").Append(UnityAgent.EscapeJson(target.name)).Append("\",");
            sb.Append("\"active\":").Append(target.activeSelf ? "true" : "false").Append(",");
            sb.Append("\"activeInHierarchy\":").Append(target.activeInHierarchy ? "true" : "false").Append(",");
            sb.Append("\"tag\":\"").Append(UnityAgent.EscapeJson(target.tag)).Append("\",");
            sb.Append("\"layer\":\"").Append(UnityAgent.EscapeJson(LayerMask.LayerToName(target.layer))).Append("\",");
            sb.Append("\"isStatic\":").Append(target.isStatic ? "true" : "false").Append(",");

            var t = target.transform;
            sb.Append("\"transform\":{");
            sb.Append("\"position\":").Append(AgentToolHelpers.Vec3Json(t.position)).Append(",");
            sb.Append("\"rotation\":").Append(AgentToolHelpers.Vec3Json(t.eulerAngles)).Append(",");
            sb.Append("\"scale\":").Append(AgentToolHelpers.Vec3Json(t.localScale)).Append(",");
            sb.Append("\"localPosition\":").Append(AgentToolHelpers.Vec3Json(t.localPosition)).Append(",");
            sb.Append("\"localRotation\":").Append(AgentToolHelpers.Vec3Json(t.localEulerAngles));
            sb.Append("},");

            if (t.parent != null)
                sb.Append("\"parent\":\"").Append(UnityAgent.EscapeJson(t.parent.name)).Append("\",");
            else
                sb.Append("\"parent\":null,");

            sb.Append("\"children\":[");
            for (int i = 0; i < t.childCount; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(UnityAgent.EscapeJson(t.GetChild(i).name)).Append("\"");
            }
            sb.Append("],");

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
                if (comp is AudioSource audio)
                {
                    sb.Append(",\"clip\":\"").Append(audio.clip != null ? UnityAgent.EscapeJson(audio.clip.name) : "null").Append("\"");
                    sb.Append(",\"volume\":").Append(audio.volume);
                    sb.Append(",\"isPlaying\":").Append(audio.isPlaying ? "true" : "false");
                }

                sb.Append("}");
            }
            sb.Append("]}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class InspectParams
        {
            [ToolParam("Name of the GameObject to inspect.", required: true)]
            public string name;
            [ToolParam("Tag to help locate the object (optional).")]
            public string tag;
        }

        // =================================================================
        // Tool: createGameObject
        // =================================================================

        [AgentTool("createGameObject",
            "Create a new GameObject in the scene. Optionally create a primitive shape " +
            "(Cube, Sphere, Capsule, Cylinder, Plane, Quad) or an empty object.",
            ParametersType = typeof(CreateParams),
            RequiresPermission = true)]
        private IEnumerator HandleCreate(string arguments, Action<UnityAgent.ToolResult> callback)
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
                    callback(AgentToolHelpers.Fail(
                        $"Unknown primitive: '{primitiveStr}'. Use: Cube, Sphere, Capsule, Cylinder, Plane, Quad."));
                    yield break;
                }
                go = GameObject.CreatePrimitive(prim);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                    go.transform.SetParent(parent.transform, false);
            }

            Vector3? pos = AgentToolHelpers.ParseVec3FromArgs(arguments, "position");
            if (pos.HasValue) go.transform.position = pos.Value;

            Vector3? rot = AgentToolHelpers.ParseVec3FromArgs(arguments, "rotation");
            if (rot.HasValue) go.transform.eulerAngles = rot.Value;

            Vector3? scale = AgentToolHelpers.ParseVec3FromArgs(arguments, "scale");
            if (scale.HasValue) go.transform.localScale = scale.Value;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"name\":\"").Append(UnityAgent.EscapeJson(go.name)).Append("\",");
            sb.Append("\"instanceId\":").Append(go.GetInstanceID()).Append(",");
            sb.Append("\"position\":").Append(AgentToolHelpers.Vec3Json(go.transform.position)).Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class CreateParams
        {
            [ToolParam("Name for the new GameObject.", required: true)]
            public string name;
            [ToolParam("Primitive type: Cube, Sphere, Capsule, Cylinder, Plane, Quad. Omit for empty.")]
            public string primitive;
            [ToolParam("Name of parent GameObject to attach to.")]
            public string parent;
            [ToolParam("Initial position as {x,y,z} object.")]
            public string position;
            [ToolParam("Initial euler rotation as {x,y,z} object.")]
            public string rotation;
            [ToolParam("Initial local scale as {x,y,z} object.")]
            public string scale;
        }

        // =================================================================
        // Tool: destroyGameObject
        // =================================================================

        [AgentTool("destroyGameObject",
            "Destroy a GameObject and all its children.",
            ParametersType = typeof(DestroyParams),
            RequiresPermission = true)]
        private IEnumerator HandleDestroy(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");

            GameObject target = AgentToolHelpers.FindTarget(name, tag);
            if (target == null)
            {
                callback(AgentToolHelpers.Fail("GameObject not found."));
                yield break;
            }

            string targetName = target.name;
            Destroy(target);
            callback(AgentToolHelpers.Ok($"Destroyed '{targetName}'."));
            yield break;
        }

        public class DestroyParams
        {
            [ToolParam("Name of the GameObject to destroy.", required: true)]
            public string name;
            [ToolParam("Tag to help locate the object.")]
            public string tag;
        }

        // =================================================================
        // Tool: setActive
        // =================================================================

        [AgentTool("setActive",
            "Activate or deactivate a GameObject (show/hide it and all children).",
            ParametersType = typeof(SetActiveParams))]
        private IEnumerator HandleSetActive(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");
            string activeStr = UnityAgent.ExtractStringField(arguments, "active");

            GameObject target = AgentToolHelpers.FindTarget(name, tag);
            if (target == null)
            {
                callback(AgentToolHelpers.Fail("GameObject not found."));
                yield break;
            }

            bool active = activeStr != "false";
            target.SetActive(active);
            callback(AgentToolHelpers.Ok($"'{target.name}' active={active}"));
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
        // Tool: setTransform
        // =================================================================

        [AgentTool("setTransform",
            "Set the position, rotation (euler angles), or scale of a GameObject. " +
            "Specify only the fields you want to change. Uses world space by default.",
            ParametersType = typeof(SetTransformParams))]
        private IEnumerator HandleSetTransform(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string tag = UnityAgent.ExtractStringField(arguments, "tag");
            bool local = AgentToolHelpers.ParseBool(
                UnityAgent.ExtractStringField(arguments, "local"));

            GameObject target = AgentToolHelpers.FindTarget(name, tag);
            if (target == null)
            {
                callback(AgentToolHelpers.Fail("GameObject not found."));
                yield break;
            }

            var t = target.transform;

            Vector3? pos = AgentToolHelpers.ParseVec3FromArgs(arguments, "position");
            if (pos.HasValue)
            {
                if (local) t.localPosition = pos.Value;
                else t.position = pos.Value;
            }

            Vector3? rot = AgentToolHelpers.ParseVec3FromArgs(arguments, "rotation");
            if (rot.HasValue)
            {
                if (local) t.localEulerAngles = rot.Value;
                else t.eulerAngles = rot.Value;
            }

            Vector3? scale = AgentToolHelpers.ParseVec3FromArgs(arguments, "scale");
            if (scale.HasValue)
                t.localScale = scale.Value;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"name\":\"").Append(UnityAgent.EscapeJson(target.name)).Append("\",");
            sb.Append("\"position\":").Append(AgentToolHelpers.Vec3Json(t.position)).Append(",");
            sb.Append("\"rotation\":").Append(AgentToolHelpers.Vec3Json(t.eulerAngles)).Append(",");
            sb.Append("\"scale\":").Append(AgentToolHelpers.Vec3Json(t.localScale)).Append("}");

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
        // Tool: duplicateGameObject
        // =================================================================

        [AgentTool("duplicateGameObject",
            "Duplicate a GameObject (with all children and components). " +
            "The clone is placed at the same position in the hierarchy.",
            ParametersType = typeof(DuplicateParams),
            RequiresPermission = true)]
        private IEnumerator HandleDuplicate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string newName = UnityAgent.ExtractStringField(arguments, "newName");

            GameObject target = AgentToolHelpers.FindTarget(name, null);
            if (target == null)
            {
                callback(AgentToolHelpers.Fail("GameObject not found."));
                yield break;
            }

            var clone = Instantiate(target, target.transform.parent);
            clone.name = !string.IsNullOrEmpty(newName) ? newName : target.name + " (Clone)";

            Vector3? pos = AgentToolHelpers.ParseVec3FromArgs(arguments, "position");
            if (pos.HasValue) clone.transform.position = pos.Value;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"name\":\"").Append(UnityAgent.EscapeJson(clone.name)).Append("\",");
            sb.Append("\"instanceId\":").Append(clone.GetInstanceID()).Append(",");
            sb.Append("\"position\":").Append(AgentToolHelpers.Vec3Json(clone.transform.position)).Append("}");

            callback(new UnityAgent.ToolResult { content = sb.ToString() });
            yield break;
        }

        public class DuplicateParams
        {
            [ToolParam("Name of the GameObject to duplicate.", required: true)]
            public string name;
            [ToolParam("Name for the cloned object. Defaults to '<name> (Clone)'.")]
            public string newName;
            [ToolParam("Position for the clone as {x,y,z}. Defaults to same position.")]
            public string position;
        }

        // =================================================================
        // Tool: setParent
        // =================================================================

        [AgentTool("setParent",
            "Change the parent of a GameObject in the hierarchy. " +
            "Set parent to empty string or 'null' to make it a root object.",
            ParametersType = typeof(SetParentParams))]
        private IEnumerator HandleSetParent(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string name = UnityAgent.ExtractStringField(arguments, "name");
            string parentName = UnityAgent.ExtractStringField(arguments, "parent");
            bool worldPositionStays = UnityAgent.ExtractStringField(arguments, "worldPositionStays") != "false";

            GameObject target = AgentToolHelpers.FindTarget(name, null);
            if (target == null)
            {
                callback(AgentToolHelpers.Fail("GameObject not found."));
                yield break;
            }

            if (string.IsNullOrEmpty(parentName) || parentName == "null")
            {
                target.transform.SetParent(null, worldPositionStays);
                callback(AgentToolHelpers.Ok($"'{target.name}' is now a root object."));
            }
            else
            {
                var newParent = GameObject.Find(parentName);
                if (newParent == null)
                {
                    callback(AgentToolHelpers.Fail($"Parent '{parentName}' not found."));
                    yield break;
                }
                target.transform.SetParent(newParent.transform, worldPositionStays);
                callback(AgentToolHelpers.Ok($"'{target.name}' is now child of '{newParent.name}'."));
            }

            yield break;
        }

        public class SetParentParams
        {
            [ToolParam("Name of the GameObject to reparent.", required: true)]
            public string name;
            [ToolParam("Name of the new parent. Empty or 'null' to make root.", required: true)]
            public string parent;
            [ToolParam("Keep world position (default true). Set 'false' to keep local position.")]
            public string worldPositionStays;
        }
    }
}
