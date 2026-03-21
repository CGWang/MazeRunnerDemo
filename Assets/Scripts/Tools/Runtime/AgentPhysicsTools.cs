using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace LLMAgent.Tools
{
    /// <summary>
    /// Physics query tools (runtime-compatible).
    /// Tools: physicsQuery
    /// </summary>
    public class AgentPhysicsTools : MonoBehaviour
    {
        // =================================================================
        // Tool: physicsQuery
        // =================================================================

        [AgentTool("physicsQuery",
            "Perform physics queries: 'raycast' (cast a ray), 'overlap_sphere' (find colliders in radius), " +
            "'overlap_box' (find colliders in box area), 'linecast' (test line between two points).",
            ParametersType = typeof(PhysicsQueryParams))]
        private IEnumerator HandlePhysicsQuery(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action") ?? "raycast";

            switch (action.ToLower())
            {
                case "raycast":
                    HandleRaycast(arguments, callback);
                    break;
                case "overlap_sphere":
                    HandleOverlapSphere(arguments, callback);
                    break;
                case "overlap_box":
                    HandleOverlapBox(arguments, callback);
                    break;
                case "linecast":
                    HandleLinecast(arguments, callback);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: raycast, overlap_sphere, overlap_box, linecast."));
                    break;
            }

            yield break;
        }

        public class PhysicsQueryParams
        {
            [ToolParam("Action: raycast, overlap_sphere, overlap_box, linecast.", required: true)]
            public string action;
            [ToolParam("Origin position as {x,y,z} (for raycast, overlap_sphere, overlap_box).")]
            public string origin;
            [ToolParam("Direction as {x,y,z} (for raycast). Defaults to forward (0,0,1).")]
            public string direction;
            [ToolParam("Max distance for raycast (default 100).", SchemaType = "number")]
            public float maxDistance;
            [ToolParam("Radius for overlap_sphere (default 5).", SchemaType = "number")]
            public float radius;
            [ToolParam("Half extents as {x,y,z} for overlap_box (default {1,1,1}).")]
            public string halfExtents;
            [ToolParam("End position as {x,y,z} (for linecast).")]
            public string end;
            [ToolParam("Layer mask name to filter results.")]
            public string layerMask;
        }

        private void HandleRaycast(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            Vector3 origin = AgentToolHelpers.ParseVec3FromArgs(arguments, "origin") ?? Vector3.zero;
            Vector3 direction = AgentToolHelpers.ParseVec3FromArgs(arguments, "direction") ?? Vector3.forward;
            float maxDist = AgentToolHelpers.ParseFloat(
                UnityAgent.ExtractNumberField(arguments, "maxDistance"), 100f);
            string layerName = UnityAgent.ExtractStringField(arguments, "layerMask");
            int mask = string.IsNullOrEmpty(layerName) ? ~0 : LayerMask.GetMask(layerName);

            RaycastHit[] hits = Physics.RaycastAll(origin, direction.normalized, maxDist, mask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"hitCount\":").Append(hits.Length);
            sb.Append(",\"hits\":[");

            int count = Mathf.Min(hits.Length, 20);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(",");
                var h = hits[i];
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(h.collider.gameObject.name)).Append("\"");
                sb.Append(",\"distance\":").Append(h.distance.ToString("F3"));
                sb.Append(",\"point\":").Append(AgentToolHelpers.Vec3Json(h.point));
                sb.Append(",\"normal\":").Append(AgentToolHelpers.Vec3Json(h.normal));
                sb.Append(",\"tag\":\"").Append(UnityAgent.EscapeJson(h.collider.tag)).Append("\"}");
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleOverlapSphere(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            Vector3 origin = AgentToolHelpers.ParseVec3FromArgs(arguments, "origin") ?? Vector3.zero;
            float radius = AgentToolHelpers.ParseFloat(
                UnityAgent.ExtractNumberField(arguments, "radius"), 5f);
            string layerName = UnityAgent.ExtractStringField(arguments, "layerMask");
            int mask = string.IsNullOrEmpty(layerName) ? ~0 : LayerMask.GetMask(layerName);

            Collider[] colliders = Physics.OverlapSphere(origin, radius, mask);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"count\":").Append(colliders.Length);
            sb.Append(",\"objects\":[");

            int count = Mathf.Min(colliders.Length, 50);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(",");
                var col = colliders[i];
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(col.gameObject.name)).Append("\"");
                sb.Append(",\"position\":").Append(AgentToolHelpers.Vec3Json(col.transform.position));
                sb.Append(",\"distance\":").Append(
                    Vector3.Distance(origin, col.transform.position).ToString("F2"));
                sb.Append(",\"tag\":\"").Append(UnityAgent.EscapeJson(col.tag)).Append("\"}");
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleOverlapBox(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            Vector3 origin = AgentToolHelpers.ParseVec3FromArgs(arguments, "origin") ?? Vector3.zero;
            Vector3 halfExtents = AgentToolHelpers.ParseVec3FromArgs(arguments, "halfExtents") ?? Vector3.one;
            string layerName = UnityAgent.ExtractStringField(arguments, "layerMask");
            int mask = string.IsNullOrEmpty(layerName) ? ~0 : LayerMask.GetMask(layerName);

            Collider[] colliders = Physics.OverlapBox(origin, halfExtents, Quaternion.identity, mask);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"count\":").Append(colliders.Length);
            sb.Append(",\"objects\":[");

            int count = Mathf.Min(colliders.Length, 50);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(colliders[i].gameObject.name)).Append("\"");
                sb.Append(",\"position\":").Append(AgentToolHelpers.Vec3Json(colliders[i].transform.position));
                sb.Append(",\"tag\":\"").Append(UnityAgent.EscapeJson(colliders[i].tag)).Append("\"}");
            }

            sb.Append("]}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleLinecast(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            Vector3 origin = AgentToolHelpers.ParseVec3FromArgs(arguments, "origin") ?? Vector3.zero;
            Vector3 end = AgentToolHelpers.ParseVec3FromArgs(arguments, "end") ?? Vector3.forward * 100f;
            string layerName = UnityAgent.ExtractStringField(arguments, "layerMask");
            int mask = string.IsNullOrEmpty(layerName) ? ~0 : LayerMask.GetMask(layerName);

            RaycastHit hit;
            bool didHit = Physics.Linecast(origin, end, out hit, mask);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"hit\":").Append(didHit ? "true" : "false");
            if (didHit)
            {
                sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(hit.collider.gameObject.name)).Append("\"");
                sb.Append(",\"point\":").Append(AgentToolHelpers.Vec3Json(hit.point));
                sb.Append(",\"distance\":").Append(hit.distance.ToString("F3"));
                sb.Append(",\"normal\":").Append(AgentToolHelpers.Vec3Json(hit.normal));
            }
            sb.Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }
    }
}
