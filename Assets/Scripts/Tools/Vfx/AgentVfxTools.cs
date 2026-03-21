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
    /// VFX tools: ParticleSystem, LineRenderer, TrailRenderer.
    /// Maps to unity-mcp manage_vfx (Vfx/ directory).
    /// VFX Graph support uses reflection since it's an optional package.
    /// </summary>
    public class AgentVfxTools : MonoBehaviour
    {
        [AgentTool("manageVfx",
            "Manage visual effects. Actions: " +
            "particle_create, particle_get_info, particle_set_main, particle_set_emission, " +
            "particle_set_shape, particle_play, particle_stop, particle_pause, particle_restart, " +
            "particle_clear, particle_enable_module, particle_add_burst, " +
            "line_create, line_get_info, line_set_positions, line_set_width, line_set_color, " +
            "line_create_circle, line_create_arc, line_clear, " +
            "trail_create, trail_get_info, trail_set_time, trail_set_width, trail_set_color.",
            ParametersType = typeof(VfxParams))]
        private IEnumerator HandleManageVfx(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required."));
                yield break;
            }

            switch (action.ToLower())
            {
                // ── Particle System ──
                case "particle_create": HandleParticleCreate(arguments, callback); break;
                case "particle_get_info": HandleParticleGetInfo(arguments, callback); break;
                case "particle_set_main": HandleParticleSetMain(arguments, callback); break;
                case "particle_set_emission": HandleParticleSetEmission(arguments, callback); break;
                case "particle_set_shape": HandleParticleSetShape(arguments, callback); break;
                case "particle_play": HandleParticlePlayback(arguments, callback, "play"); break;
                case "particle_stop": HandleParticlePlayback(arguments, callback, "stop"); break;
                case "particle_pause": HandleParticlePlayback(arguments, callback, "pause"); break;
                case "particle_restart": HandleParticlePlayback(arguments, callback, "restart"); break;
                case "particle_clear": HandleParticlePlayback(arguments, callback, "clear"); break;
                case "particle_enable_module": HandleParticleEnableModule(arguments, callback); break;
                case "particle_add_burst": HandleParticleAddBurst(arguments, callback); break;

                // ── Line Renderer ──
                case "line_create": HandleLineCreate(arguments, callback); break;
                case "line_get_info": HandleLineGetInfo(arguments, callback); break;
                case "line_set_positions": HandleLineSetPositions(arguments, callback); break;
                case "line_set_width": HandleLineSetWidth(arguments, callback); break;
                case "line_set_color": HandleLineSetColor(arguments, callback); break;
                case "line_create_circle": HandleLineCreateCircle(arguments, callback); break;
                case "line_create_arc": HandleLineCreateArc(arguments, callback); break;
                case "line_clear": HandleLineClear(arguments, callback); break;

                // ── Trail Renderer ──
                case "trail_create": HandleTrailCreate(arguments, callback); break;
                case "trail_get_info": HandleTrailGetInfo(arguments, callback); break;
                case "trail_set_time": HandleTrailSetTime(arguments, callback); break;
                case "trail_set_width": HandleTrailSetWidth(arguments, callback); break;
                case "trail_set_color": HandleTrailSetColor(arguments, callback); break;

                default:
                    callback(AgentToolHelpers.Fail($"Unknown action '{action}'."));
                    break;
            }
            yield break;
        }

        public class VfxParams
        {
            [ToolParam("Action to perform.", required: true)]
            public string action;
            [ToolParam("Target GameObject name.")]
            public string target;
            [ToolParam("Name for new GameObject.")]
            public string name;

            // Particle main module
            [ToolParam("Duration in seconds.")]
            public string duration;
            [ToolParam("Whether looping.")]
            public string looping;
            [ToolParam("Start lifetime.")]
            public string startLifetime;
            [ToolParam("Start speed.")]
            public string startSpeed;
            [ToolParam("Start size.")]
            public string startSize;
            [ToolParam("Start color (hex or name).")]
            public string startColor;
            [ToolParam("Gravity modifier.")]
            public string gravityModifier;
            [ToolParam("Max particles.")]
            public string maxParticles;
            [ToolParam("Simulation space: 'local' or 'world'.")]
            public string simulationSpace;
            [ToolParam("Play on awake.")]
            public string playOnAwake;

            // Emission
            [ToolParam("Emission rate over time.")]
            public string rateOverTime;
            [ToolParam("Emission rate over distance.")]
            public string rateOverDistance;

            // Shape
            [ToolParam("Shape type: sphere, hemisphere, cone, box, circle, edge.")]
            public string shapeType;
            [ToolParam("Shape radius.")]
            public string radius;
            [ToolParam("Shape angle (for cone).")]
            public string angle;

            // Module control
            [ToolParam("Module name (emission, shape, colorOverLifetime, sizeOverLifetime, velocityOverLifetime, noise).")]
            public string moduleName;
            [ToolParam("Enable or disable.")]
            public string enabled;

            // Burst
            [ToolParam("Burst time.")]
            public string time;
            [ToolParam("Burst count.")]
            public string count;

            // Line/Trail
            [ToolParam("Positions as JSON array of {x,y,z} objects.")]
            public string positions;
            [ToolParam("Start width.")]
            public string startWidth;
            [ToolParam("End width.")]
            public string endWidth;
            [ToolParam("Color value.")]
            public string color;
            [ToolParam("End color.")]
            public string endColor;
            [ToolParam("Number of segments (for circle/arc).")]
            public string segments;
            [ToolParam("Arc angle in degrees.")]
            public string arcAngle;
            [ToolParam("Trail time.")]
            public string trailTime;
        }

        // ================================================================
        // Helpers
        // ================================================================

        private ParticleSystem FindPS(string arguments)
        {
            string target = UnityAgent.ExtractStringField(arguments, "target");
            if (string.IsNullOrEmpty(target)) return null;
            var go = GameObject.Find(target);
            return go != null ? go.GetComponent<ParticleSystem>() : null;
        }

        private T FindRenderer<T>(string arguments) where T : Component
        {
            string target = UnityAgent.ExtractStringField(arguments, "target");
            if (string.IsNullOrEmpty(target)) return null;
            var go = GameObject.Find(target);
            return go != null ? go.GetComponent<T>() : null;
        }

        // ================================================================
        // Particle System
        // ================================================================

        private void HandleParticleCreate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "name") ?? "ParticleSystem";
            string target = UnityAgent.ExtractStringField(arguments, "target");

            GameObject go;
            if (!string.IsNullOrEmpty(target))
            {
                go = GameObject.Find(target);
                if (go == null) { callback(AgentToolHelpers.Fail($"'{target}' not found.")); return; }
            }
            else
            {
                go = new GameObject(goName);
#if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(go, "Create ParticleSystem");
#endif
            }

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
                ps = go.AddComponent<ParticleSystem>();

            callback(AgentToolHelpers.Ok($"ParticleSystem created on '{go.name}'."));
        }

        private void HandleParticleGetInfo(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var ps = FindPS(arguments);
            if (ps == null) { callback(AgentToolHelpers.Fail("ParticleSystem not found.")); return; }

            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"particle\":{");
            sb.Append("\"gameObject\":\"").Append(UnityAgent.EscapeJson(ps.gameObject.name)).Append("\"");
            sb.Append(",\"isPlaying\":").Append(ps.isPlaying ? "true" : "false");
            sb.Append(",\"particleCount\":").Append(ps.particleCount);
            sb.Append(",\"duration\":").Append(main.duration.ToString("F2"));
            sb.Append(",\"looping\":").Append(main.loop ? "true" : "false");
            sb.Append(",\"startLifetime\":").Append(main.startLifetime.constant.ToString("F2"));
            sb.Append(",\"startSpeed\":").Append(main.startSpeed.constant.ToString("F2"));
            sb.Append(",\"startSize\":").Append(main.startSize.constant.ToString("F2"));
            sb.Append(",\"maxParticles\":").Append(main.maxParticles);
            sb.Append(",\"simulationSpace\":\"").Append(main.simulationSpace.ToString()).Append("\"");
            sb.Append(",\"playOnAwake\":").Append(main.playOnAwake ? "true" : "false");
            sb.Append(",\"gravityModifier\":").Append(main.gravityModifier.constant.ToString("F2"));
            sb.Append(",\"emissionEnabled\":").Append(emission.enabled ? "true" : "false");
            sb.Append(",\"rateOverTime\":").Append(emission.rateOverTime.constant.ToString("F1"));
            sb.Append(",\"shapeEnabled\":").Append(shape.enabled ? "true" : "false");
            sb.Append(",\"shapeType\":\"").Append(shape.shapeType.ToString()).Append("\"");
            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleParticleSetMain(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var ps = FindPS(arguments);
            if (ps == null) { callback(AgentToolHelpers.Fail("ParticleSystem not found. Provide 'target'.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(ps, "Set Particle Main");
#endif
            var main = ps.main;

            string v;
            v = UnityAgent.ExtractNumberField(arguments, "duration");
            if (v != null) main.duration = AgentToolHelpers.ParseFloat(v, 5f);

            v = UnityAgent.ExtractStringField(arguments, "looping");
            if (v != null) main.loop = AgentToolHelpers.ParseBool(v, true);

            v = UnityAgent.ExtractNumberField(arguments, "startLifetime");
            if (v != null) main.startLifetime = AgentToolHelpers.ParseFloat(v, 5f);

            v = UnityAgent.ExtractNumberField(arguments, "startSpeed");
            if (v != null) main.startSpeed = AgentToolHelpers.ParseFloat(v, 5f);

            v = UnityAgent.ExtractNumberField(arguments, "startSize");
            if (v != null) main.startSize = AgentToolHelpers.ParseFloat(v, 1f);

            v = UnityAgent.ExtractNumberField(arguments, "gravityModifier");
            if (v != null) main.gravityModifier = AgentToolHelpers.ParseFloat(v, 0f);

            v = UnityAgent.ExtractNumberField(arguments, "maxParticles");
            if (v != null) main.maxParticles = AgentToolHelpers.ParseInt(v, 1000);

            v = UnityAgent.ExtractStringField(arguments, "simulationSpace");
            if (v != null)
                main.simulationSpace = v.ToLower() == "world"
                    ? ParticleSystemSimulationSpace.World
                    : ParticleSystemSimulationSpace.Local;

            v = UnityAgent.ExtractStringField(arguments, "playOnAwake");
            if (v != null) main.playOnAwake = AgentToolHelpers.ParseBool(v, true);

            v = UnityAgent.ExtractStringField(arguments, "startColor");
            if (v != null)
            {
                Color c;
                if (AgentToolHelpers.TryParseColor(v, out c))
                    main.startColor = c;
            }

            callback(AgentToolHelpers.Ok("Particle main module updated."));
        }

        private void HandleParticleSetEmission(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var ps = FindPS(arguments);
            if (ps == null) { callback(AgentToolHelpers.Fail("ParticleSystem not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(ps, "Set Particle Emission");
#endif
            var emission = ps.emission;
            emission.enabled = true;

            string v;
            v = UnityAgent.ExtractNumberField(arguments, "rateOverTime");
            if (v != null) emission.rateOverTime = AgentToolHelpers.ParseFloat(v, 10f);

            v = UnityAgent.ExtractNumberField(arguments, "rateOverDistance");
            if (v != null) emission.rateOverDistance = AgentToolHelpers.ParseFloat(v, 0f);

            callback(AgentToolHelpers.Ok("Emission module updated."));
        }

        private void HandleParticleSetShape(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var ps = FindPS(arguments);
            if (ps == null) { callback(AgentToolHelpers.Fail("ParticleSystem not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(ps, "Set Particle Shape");
#endif
            var shape = ps.shape;
            shape.enabled = true;

            string st = UnityAgent.ExtractStringField(arguments, "shapeType");
            if (!string.IsNullOrEmpty(st))
            {
                switch (st.ToLower())
                {
                    case "sphere": shape.shapeType = ParticleSystemShapeType.Sphere; break;
                    case "hemisphere": shape.shapeType = ParticleSystemShapeType.Hemisphere; break;
                    case "cone": shape.shapeType = ParticleSystemShapeType.Cone; break;
                    case "box": shape.shapeType = ParticleSystemShapeType.Box; break;
                    case "circle": shape.shapeType = ParticleSystemShapeType.Circle; break;
                    case "edge": shape.shapeType = ParticleSystemShapeType.SingleSidedEdge; break;
                }
            }

            string r = UnityAgent.ExtractNumberField(arguments, "radius");
            if (r != null) shape.radius = AgentToolHelpers.ParseFloat(r, 1f);

            string a = UnityAgent.ExtractNumberField(arguments, "angle");
            if (a != null) shape.angle = AgentToolHelpers.ParseFloat(a, 25f);

            callback(AgentToolHelpers.Ok("Shape module updated."));
        }

        private void HandleParticlePlayback(string arguments, Action<UnityAgent.ToolResult> callback, string cmd)
        {
            var ps = FindPS(arguments);
            if (ps == null) { callback(AgentToolHelpers.Fail("ParticleSystem not found.")); return; }

            switch (cmd)
            {
                case "play": ps.Play(); break;
                case "stop": ps.Stop(); break;
                case "pause": ps.Pause(); break;
                case "restart": ps.Stop(); ps.Clear(); ps.Play(); break;
                case "clear": ps.Clear(); break;
            }
            callback(AgentToolHelpers.Ok($"ParticleSystem {cmd} on '{ps.gameObject.name}'."));
        }

        private void HandleParticleEnableModule(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var ps = FindPS(arguments);
            if (ps == null) { callback(AgentToolHelpers.Fail("ParticleSystem not found.")); return; }

            string moduleName = UnityAgent.ExtractStringField(arguments, "moduleName");
            bool enable = AgentToolHelpers.ParseBool(UnityAgent.ExtractStringField(arguments, "enabled"), true);

            if (string.IsNullOrEmpty(moduleName))
            {
                callback(AgentToolHelpers.Fail("'moduleName' is required."));
                return;
            }

#if UNITY_EDITOR
            Undo.RecordObject(ps, "Toggle Particle Module");
#endif

            switch (moduleName.ToLower())
            {
                case "emission": var em = ps.emission; em.enabled = enable; break;
                case "shape": var sh = ps.shape; sh.enabled = enable; break;
                case "coloroverlifetime": var col = ps.colorOverLifetime; col.enabled = enable; break;
                case "sizeoverlifetime": var sz = ps.sizeOverLifetime; sz.enabled = enable; break;
                case "velocityoverlifetime": var vel = ps.velocityOverLifetime; vel.enabled = enable; break;
                case "noise": var ns = ps.noise; ns.enabled = enable; break;
                case "collision": var cl = ps.collision; cl.enabled = enable; break;
                case "trails": var tr = ps.trails; tr.enabled = enable; break;
                case "renderer": break; // ParticleSystemRenderer is always present
                default:
                    callback(AgentToolHelpers.Fail($"Unknown module '{moduleName}'."));
                    return;
            }

            callback(AgentToolHelpers.Ok($"Module '{moduleName}' {(enable ? "enabled" : "disabled")}."));
        }

        private void HandleParticleAddBurst(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var ps = FindPS(arguments);
            if (ps == null) { callback(AgentToolHelpers.Fail("ParticleSystem not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(ps, "Add Particle Burst");
#endif
            var emission = ps.emission;
            float time = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(arguments, "time"), 0f);
            int count = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "count"), 30);

            emission.SetBurst(emission.burstCount, new ParticleSystem.Burst(time, (short)count));
            callback(AgentToolHelpers.Ok($"Burst added: count={count} at time={time:F2}s."));
        }

        // ================================================================
        // Line Renderer
        // ================================================================

        private void HandleLineCreate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "name") ?? "LineRenderer";
            var go = new GameObject(goName);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, "Create LineRenderer");
#endif
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 0;
            lr.startWidth = 0.1f;
            lr.endWidth = 0.1f;
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Sprites/Default"));

            callback(AgentToolHelpers.Ok($"LineRenderer '{goName}' created."));
        }

        private void HandleLineGetInfo(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var lr = FindRenderer<LineRenderer>(arguments);
            if (lr == null) { callback(AgentToolHelpers.Fail("LineRenderer not found.")); return; }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"line\":{");
            sb.Append("\"gameObject\":\"").Append(UnityAgent.EscapeJson(lr.gameObject.name)).Append("\"");
            sb.Append(",\"positionCount\":").Append(lr.positionCount);
            sb.Append(",\"startWidth\":").Append(lr.startWidth.ToString("F3"));
            sb.Append(",\"endWidth\":").Append(lr.endWidth.ToString("F3"));
            sb.Append(",\"loop\":").Append(lr.loop ? "true" : "false");
            sb.Append(",\"useWorldSpace\":").Append(lr.useWorldSpace ? "true" : "false");
            sb.Append(",\"startColor\":").Append(AgentToolHelpers.ColorJson(lr.startColor));
            sb.Append(",\"endColor\":").Append(AgentToolHelpers.ColorJson(lr.endColor));
            if (lr.positionCount > 0 && lr.positionCount <= 50)
            {
                sb.Append(",\"positions\":[");
                for (int i = 0; i < lr.positionCount; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(AgentToolHelpers.Vec3Json(lr.GetPosition(i)));
                }
                sb.Append("]");
            }
            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleLineSetPositions(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var lr = FindRenderer<LineRenderer>(arguments);
            if (lr == null) { callback(AgentToolHelpers.Fail("LineRenderer not found.")); return; }

            string posJson = UnityAgent.ExtractStringField(arguments, "positions");
            if (string.IsNullOrEmpty(posJson))
            {
                callback(AgentToolHelpers.Fail("'positions' array is required."));
                return;
            }

            // Simple parser for [{x,y,z}, ...] format
            var positions = new System.Collections.Generic.List<Vector3>();
            int idx = 0;
            while (idx < posJson.Length)
            {
                int start = posJson.IndexOf('{', idx);
                if (start < 0) break;
                int end = posJson.IndexOf('}', start);
                if (end < 0) break;

                string obj = posJson.Substring(start, end - start + 1);
                float x = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(obj, "x"));
                float y = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(obj, "y"));
                float z = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(obj, "z"));
                positions.Add(new Vector3(x, y, z));
                idx = end + 1;
            }

#if UNITY_EDITOR
            Undo.RecordObject(lr, "Set Line Positions");
#endif
            lr.positionCount = positions.Count;
            lr.SetPositions(positions.ToArray());

            callback(AgentToolHelpers.Ok($"Set {positions.Count} positions on LineRenderer."));
        }

        private void HandleLineSetWidth(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var lr = FindRenderer<LineRenderer>(arguments);
            if (lr == null) { callback(AgentToolHelpers.Fail("LineRenderer not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(lr, "Set Line Width");
#endif
            string sw = UnityAgent.ExtractNumberField(arguments, "startWidth");
            string ew = UnityAgent.ExtractNumberField(arguments, "endWidth");
            if (sw != null) lr.startWidth = AgentToolHelpers.ParseFloat(sw, 0.1f);
            if (ew != null) lr.endWidth = AgentToolHelpers.ParseFloat(ew, 0.1f);

            callback(AgentToolHelpers.Ok("Line width updated."));
        }

        private void HandleLineSetColor(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var lr = FindRenderer<LineRenderer>(arguments);
            if (lr == null) { callback(AgentToolHelpers.Fail("LineRenderer not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(lr, "Set Line Color");
#endif
            Color c;
            string cs = UnityAgent.ExtractStringField(arguments, "color");
            if (!string.IsNullOrEmpty(cs) && AgentToolHelpers.TryParseColor(cs, out c))
                lr.startColor = c;

            string ec = UnityAgent.ExtractStringField(arguments, "endColor");
            if (!string.IsNullOrEmpty(ec) && AgentToolHelpers.TryParseColor(ec, out c))
                lr.endColor = c;
            else if (!string.IsNullOrEmpty(cs) && AgentToolHelpers.TryParseColor(cs, out c))
                lr.endColor = c;

            callback(AgentToolHelpers.Ok("Line color updated."));
        }

        private void HandleLineCreateCircle(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "name") ?? "Circle";
            float radius = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(arguments, "radius"), 1f);
            int segments = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "segments"), 36);

            var go = new GameObject(goName);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, "Create Circle");
#endif
            var lr = go.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.positionCount = segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            callback(AgentToolHelpers.Ok($"Circle '{goName}' created with radius={radius}, segments={segments}."));
        }

        private void HandleLineCreateArc(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "name") ?? "Arc";
            float radius = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(arguments, "radius"), 1f);
            float arcAngle = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(arguments, "arcAngle"), 180f);
            int segments = AgentToolHelpers.ParseInt(UnityAgent.ExtractNumberField(arguments, "segments"), 18);

            var go = new GameObject(goName);
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, "Create Arc");
#endif
            var lr = go.AddComponent<LineRenderer>();
            lr.loop = false;
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.positionCount = segments + 1;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * arcAngle * Mathf.Deg2Rad;
                lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            callback(AgentToolHelpers.Ok($"Arc '{goName}' created with radius={radius}, angle={arcAngle}°."));
        }

        private void HandleLineClear(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var lr = FindRenderer<LineRenderer>(arguments);
            if (lr == null) { callback(AgentToolHelpers.Fail("LineRenderer not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(lr, "Clear Line");
#endif
            lr.positionCount = 0;
            callback(AgentToolHelpers.Ok("LineRenderer cleared."));
        }

        // ================================================================
        // Trail Renderer
        // ================================================================

        private void HandleTrailCreate(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string goName = UnityAgent.ExtractStringField(arguments, "name") ?? "TrailRenderer";
            string target = UnityAgent.ExtractStringField(arguments, "target");

            GameObject go;
            if (!string.IsNullOrEmpty(target))
            {
                go = GameObject.Find(target);
                if (go == null) { callback(AgentToolHelpers.Fail($"'{target}' not found.")); return; }
            }
            else
            {
                go = new GameObject(goName);
#if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(go, "Create TrailRenderer");
#endif
            }

            var tr = go.GetComponent<TrailRenderer>();
            if (tr == null)
                tr = go.AddComponent<TrailRenderer>();

            tr.time = 1f;
            tr.startWidth = 0.2f;
            tr.endWidth = 0.05f;
            tr.material = new Material(Shader.Find("Sprites/Default"));

            callback(AgentToolHelpers.Ok($"TrailRenderer created on '{go.name}'."));
        }

        private void HandleTrailGetInfo(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var tr = FindRenderer<TrailRenderer>(arguments);
            if (tr == null) { callback(AgentToolHelpers.Fail("TrailRenderer not found.")); return; }

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"trail\":{");
            sb.Append("\"gameObject\":\"").Append(UnityAgent.EscapeJson(tr.gameObject.name)).Append("\"");
            sb.Append(",\"time\":").Append(tr.time.ToString("F2"));
            sb.Append(",\"startWidth\":").Append(tr.startWidth.ToString("F3"));
            sb.Append(",\"endWidth\":").Append(tr.endWidth.ToString("F3"));
            sb.Append(",\"startColor\":").Append(AgentToolHelpers.ColorJson(tr.startColor));
            sb.Append(",\"endColor\":").Append(AgentToolHelpers.ColorJson(tr.endColor));
            sb.Append(",\"minVertexDistance\":").Append(tr.minVertexDistance.ToString("F3"));
            sb.Append(",\"autodestruct\":").Append(tr.autodestruct ? "true" : "false");
            sb.Append("}}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private void HandleTrailSetTime(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var tr = FindRenderer<TrailRenderer>(arguments);
            if (tr == null) { callback(AgentToolHelpers.Fail("TrailRenderer not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(tr, "Set Trail Time");
#endif
            string t = UnityAgent.ExtractNumberField(arguments, "trailTime");
            if (t != null) tr.time = AgentToolHelpers.ParseFloat(t, 1f);

            callback(AgentToolHelpers.Ok($"Trail time set to {tr.time:F2}s."));
        }

        private void HandleTrailSetWidth(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var tr = FindRenderer<TrailRenderer>(arguments);
            if (tr == null) { callback(AgentToolHelpers.Fail("TrailRenderer not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(tr, "Set Trail Width");
#endif
            string sw = UnityAgent.ExtractNumberField(arguments, "startWidth");
            string ew = UnityAgent.ExtractNumberField(arguments, "endWidth");
            if (sw != null) tr.startWidth = AgentToolHelpers.ParseFloat(sw, 0.2f);
            if (ew != null) tr.endWidth = AgentToolHelpers.ParseFloat(ew, 0.05f);

            callback(AgentToolHelpers.Ok("Trail width updated."));
        }

        private void HandleTrailSetColor(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            var tr = FindRenderer<TrailRenderer>(arguments);
            if (tr == null) { callback(AgentToolHelpers.Fail("TrailRenderer not found.")); return; }

#if UNITY_EDITOR
            Undo.RecordObject(tr, "Set Trail Color");
#endif
            Color c;
            string cs = UnityAgent.ExtractStringField(arguments, "color");
            if (!string.IsNullOrEmpty(cs) && AgentToolHelpers.TryParseColor(cs, out c))
                tr.startColor = c;

            string ec = UnityAgent.ExtractStringField(arguments, "endColor");
            if (!string.IsNullOrEmpty(ec) && AgentToolHelpers.TryParseColor(ec, out c))
                tr.endColor = c;
            else if (!string.IsNullOrEmpty(cs) && AgentToolHelpers.TryParseColor(cs, out c))
                tr.endColor = c;

            callback(AgentToolHelpers.Ok("Trail color updated."));
        }
    }
}
