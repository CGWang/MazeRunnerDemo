using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace LLMAgent.Tools
{
    /// <summary>
    /// Shared utility methods for all Agent tool providers.
    /// Provides JSON helpers, value conversion, GameObject lookup, etc.
    /// </summary>
    public static class AgentToolHelpers
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // =================================================================
        // Response builders
        // =================================================================

        public static UnityAgent.ToolResult Ok(string message)
        {
            return new UnityAgent.ToolResult
            {
                content = $"{{\"success\":true,\"message\":\"{UnityAgent.EscapeJson(message)}\"}}"
            };
        }

        public static UnityAgent.ToolResult Fail(string error)
        {
            return new UnityAgent.ToolResult
            {
                content = $"{{\"success\":false,\"error\":\"{UnityAgent.EscapeJson(error)}\"}}"
            };
        }

        // =================================================================
        // GameObject lookup
        // =================================================================

        /// <summary>
        /// Find a GameObject by name and/or tag. Tries tag first, then exact name, then partial name.
        /// </summary>
        public static GameObject FindTarget(string name, string tag)
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
                var exact = GameObject.Find(name);
                if (exact != null) return exact;

                foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                {
                    if (go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        return go;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolve a Component type by name across all loaded assemblies.
        /// </summary>
        public static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == typeName && typeof(Component).IsAssignableFrom(t))
                            return t;
                    }
                }
                catch { /* skip assemblies that throw on GetTypes */ }
            }
            return null;
        }

        /// <summary>
        /// Find a component on a GameObject by type name string.
        /// </summary>
        public static Component FindComponentByTypeName(GameObject go, string typeName)
        {
            if (go == null || string.IsNullOrEmpty(typeName)) return null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name == typeName)
                    return c;
            }
            return null;
        }

        // =================================================================
        // Vector / JSON helpers
        // =================================================================

        public static string Vec3Json(Vector3 v)
        {
            return $"{{\"x\":{v.x:F2},\"y\":{v.y:F2},\"z\":{v.z:F2}}}";
        }

        public static string Vec2Json(Vector2 v)
        {
            return $"{{\"x\":{v.x:F2},\"y\":{v.y:F2}}}";
        }

        public static string QuatJson(Quaternion q)
        {
            return $"{{\"x\":{q.x:F4},\"y\":{q.y:F4},\"z\":{q.z:F4},\"w\":{q.w:F4}}}";
        }

        public static string ColorJson(Color c)
        {
            return $"{{\"r\":{c.r:F3},\"g\":{c.g:F3},\"b\":{c.b:F3},\"a\":{c.a:F3}}}";
        }

        public static string ColorToHex(Color c)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(c)}";
        }

        public static Vector3? ParseVec3FromArgs(string json, string fieldName)
        {
            int fieldIdx = json.IndexOf($"\"{fieldName}\"", StringComparison.Ordinal);
            if (fieldIdx < 0) return null;

            int braceStart = json.IndexOf('{', fieldIdx);
            if (braceStart < 0) return null;
            int braceEnd = UnityAgent.FindMatchingBrace(json, braceStart);
            if (braceEnd < 0) return null;

            string obj = json.Substring(braceStart, braceEnd - braceStart + 1);
            float x = ParseFloat(UnityAgent.ExtractNumberField(obj, "x"));
            float y = ParseFloat(UnityAgent.ExtractNumberField(obj, "y"));
            float z = ParseFloat(UnityAgent.ExtractNumberField(obj, "z"));
            return new Vector3(x, y, z);
        }

        // =================================================================
        // Numeric parsing
        // =================================================================

        public static float ParseFloat(string s, float fallback = 0f)
        {
            if (s == null) return fallback;
            float v;
            return float.TryParse(s, NumberStyles.Float, Inv, out v) ? v : fallback;
        }

        public static int ParseInt(string s, int fallback = 0)
        {
            if (s == null) return fallback;
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        public static bool ParseBool(string s, bool fallback = false)
        {
            if (s == null) return fallback;
            return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // =================================================================
        // Color parsing
        // =================================================================

        public static bool TryParseColor(string str, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(str)) return false;

            switch (str.ToLower())
            {
                case "red": color = Color.red; return true;
                case "green": color = Color.green; return true;
                case "blue": color = Color.blue; return true;
                case "white": color = Color.white; return true;
                case "black": color = Color.black; return true;
                case "yellow": color = Color.yellow; return true;
                case "cyan": color = Color.cyan; return true;
                case "magenta": color = Color.magenta; return true;
                case "gray": case "grey": color = Color.gray; return true;
                case "orange": color = new Color(1f, 0.5f, 0f); return true;
                case "purple": color = new Color(0.5f, 0f, 0.5f); return true;
            }

            if (ColorUtility.TryParseHtmlString(str, out color))
                return true;
            if (str.Length >= 6 && !str.StartsWith("#"))
                return ColorUtility.TryParseHtmlString("#" + str, out color);

            return false;
        }

        // =================================================================
        // Reflection value conversion
        // =================================================================

        /// <summary>
        /// Convert a string value to the specified target type.
        /// Supports: string, int, float, double, bool, Vector2, Vector3, Color, enums.
        /// </summary>
        public static object ConvertValue(string value, Type targetType, string fullArgs = null, string fieldName = null)
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int))
            {
                int v; return int.TryParse(value, out v) ? (object)v : null;
            }
            if (targetType == typeof(float))
            {
                float v; return float.TryParse(value, NumberStyles.Float, Inv, out v) ? (object)v : null;
            }
            if (targetType == typeof(double))
            {
                double v; return double.TryParse(value, NumberStyles.Float, Inv, out v) ? (object)v : null;
            }
            if (targetType == typeof(bool))
            {
                return value?.ToLower() == "true" ? (object)true :
                       value?.ToLower() == "false" ? (object)false : null;
            }
            if (targetType == typeof(Vector3))
            {
                var parts = value?.Split(',');
                if (parts != null && parts.Length == 3)
                {
                    float x, y, z;
                    if (float.TryParse(parts[0].Trim(), NumberStyles.Float, Inv, out x) &&
                        float.TryParse(parts[1].Trim(), NumberStyles.Float, Inv, out y) &&
                        float.TryParse(parts[2].Trim(), NumberStyles.Float, Inv, out z))
                        return new Vector3(x, y, z);
                }
                return null;
            }
            if (targetType == typeof(Vector2))
            {
                var parts = value?.Split(',');
                if (parts != null && parts.Length == 2)
                {
                    float x, y;
                    if (float.TryParse(parts[0].Trim(), NumberStyles.Float, Inv, out x) &&
                        float.TryParse(parts[1].Trim(), NumberStyles.Float, Inv, out y))
                        return new Vector2(x, y);
                }
                return null;
            }
            if (targetType == typeof(Color))
            {
                Color c;
                return TryParseColor(value, out c) ? (object)c : null;
            }
            if (targetType.IsEnum)
            {
                try { return Enum.Parse(targetType, value, true); }
                catch { return null; }
            }
            return null;
        }

        // =================================================================
        // Hierarchy / StringBuilder helpers
        // =================================================================

        public static void AppendGameObjectInfo(StringBuilder sb, GameObject go, int depth)
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

        public static void AppendGameObjectDetail(StringBuilder sb, GameObject go)
        {
            sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(go.name)).Append("\"");
            sb.Append(",\"active\":").Append(go.activeSelf ? "true" : "false");
            sb.Append(",\"tag\":\"").Append(UnityAgent.EscapeJson(go.tag)).Append("\"");
            sb.Append(",\"layer\":\"").Append(UnityAgent.EscapeJson(LayerMask.LayerToName(go.layer))).Append("\"");
            sb.Append(",\"position\":").Append(Vec3Json(go.transform.position));

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

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "...";
        }

        /// <summary>
        /// Normalize a Unity asset path. Prepends "Assets/" if not already present.
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (!path.StartsWith("Assets"))
                path = "Assets/" + path;
            return path;
        }
    }
}
