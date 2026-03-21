using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// ScriptableObject creation, modification, and inspection tools.
    /// Actions: create (create a .asset file), modify (patch fields via SerializedProperty),
    ///          get_info (inspect fields and values).
    /// Editor-only: all actions require the Unity Editor.
    /// </summary>
    public class AgentScriptableObjectTools : MonoBehaviour
    {
        // =================================================================
        // Tool: manageScriptableObject
        // =================================================================

        [AgentTool("manageScriptableObject",
            "Create, modify, or inspect ScriptableObject assets. " +
            "Actions: 'create' creates a new .asset file, 'modify' patches serialized fields, " +
            "'get_info' returns field names, types, and values. Editor only.",
            ParametersType = typeof(ScriptableObjectParams))]
        private IEnumerator HandleManageScriptableObject(string arguments, Action<UnityAgent.ToolResult> callback)
        {
#if UNITY_EDITOR
            string action = UnityAgent.ExtractStringField(arguments, "action");
            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required (create, modify, get_info)."));
                yield break;
            }

            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "create":
                        callback(CreateScriptableObject(arguments));
                        break;
                    case "modify":
                        callback(ModifyScriptableObject(arguments));
                        break;
                    case "get_info":
                        callback(GetScriptableObjectInfo(arguments));
                        break;
                    default:
                        callback(AgentToolHelpers.Fail($"Unknown action: '{action}'. Valid: create, modify, get_info."));
                        break;
                }
            }
            catch (Exception ex)
            {
                callback(AgentToolHelpers.Fail($"{action} failed: {ex.Message}"));
            }
#else
            callback(AgentToolHelpers.Fail("ScriptableObject tools require Unity Editor."));
#endif
            yield break;
        }

        // =================================================================
        // Parameters
        // =================================================================

        public class ScriptableObjectParams
        {
            [ToolParam("Action: 'create', 'modify', or 'get_info'.", required: true)]
            public string action;

            [ToolParam("Full type name of the ScriptableObject class (for create).")]
            public string typeName;

            [ToolParam("Folder path where the asset will be created (e.g. 'Assets/Data').")]
            public string folderPath;

            [ToolParam("Asset file name (without path, e.g. 'MyData.asset').")]
            public string assetName;

            [ToolParam("Asset path for modify/get_info (e.g. 'Assets/Data/MyData.asset').")]
            public string assetPath;

            [ToolParam("Property path for modify (e.g. 'myField', 'myList.Array.data[0]').")]
            public string propertyPath;

            [ToolParam("Value to set (for modify action).")]
            public string value;

            [ToolParam("If true, overwrite existing asset at the same path.")]
            public string overwrite;

            [ToolParam("JSON array of patches: [{propertyPath, value, op}] for batch modify.", SchemaType = "array")]
            public string patches;
        }

#if UNITY_EDITOR

        // =================================================================
        // Type resolution
        // =================================================================

        private static Type ResolveSOType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            // Try direct lookup
            var t = Type.GetType(typeName);
            if (t != null && typeof(ScriptableObject).IsAssignableFrom(t)) return t;

            // Search all loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (typeof(ScriptableObject).IsAssignableFrom(type) &&
                            (type.Name == typeName || type.FullName == typeName))
                            return type;
                    }
                }
                catch { /* skip assemblies that throw on GetTypes */ }
            }
            return null;
        }

        // =================================================================
        // Create
        // =================================================================

        private static UnityAgent.ToolResult CreateScriptableObject(string args)
        {
            string typeName = UnityAgent.ExtractStringField(args, "typeName");
            string folderPath = UnityAgent.ExtractStringField(args, "folderPath");
            string assetName = UnityAgent.ExtractStringField(args, "assetName");
            bool overwrite = AgentToolHelpers.ParseBool(UnityAgent.ExtractStringField(args, "overwrite"));

            if (string.IsNullOrEmpty(typeName))
                return AgentToolHelpers.Fail("'typeName' is required.");
            if (string.IsNullOrEmpty(folderPath))
                return AgentToolHelpers.Fail("'folderPath' is required.");
            if (string.IsNullOrEmpty(assetName))
                return AgentToolHelpers.Fail("'assetName' is required.");

            var resolvedType = ResolveSOType(typeName);
            if (resolvedType == null)
                return AgentToolHelpers.Fail($"ScriptableObject type not found: '{typeName}'.");

            // Ensure folder exists
            folderPath = AgentToolHelpers.NormalizePath(folderPath);
            string fullDir = folderPath.Replace("Assets/", Application.dataPath + "/")
                                       .Replace("Assets\\", Application.dataPath + "\\");
            if (!System.IO.Directory.Exists(fullDir))
                System.IO.Directory.CreateDirectory(fullDir);

            string fileName = assetName.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                ? assetName : assetName + ".asset";
            string desiredPath = folderPath.TrimEnd('/') + "/" + fileName;
            string finalPath = overwrite ? desiredPath : AssetDatabase.GenerateUniqueAssetPath(desiredPath);

            ScriptableObject instance;
            try
            {
                instance = ScriptableObject.CreateInstance(resolvedType);
                if (instance == null)
                    return AgentToolHelpers.Fail($"CreateInstance returned null for type '{resolvedType.FullName}'.");
            }
            catch (Exception ex)
            {
                return AgentToolHelpers.Fail($"CreateInstance failed: {ex.Message}");
            }

            // Handle overwrite: preserve GUID if possible
            bool isNewAsset = true;
            if (overwrite)
            {
                var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(finalPath);
                if (existing != null && existing.GetType() == resolvedType)
                {
                    EditorUtility.CopySerialized(instance, existing);
                    existing.name = System.IO.Path.GetFileNameWithoutExtension(finalPath);
                    UnityEngine.Object.DestroyImmediate(instance);
                    instance = existing;
                    isNewAsset = false;
                    EditorUtility.SetDirty(instance);
                }
                else if (existing != null)
                {
                    AssetDatabase.DeleteAsset(finalPath);
                }
            }

            if (isNewAsset)
            {
                instance.name = System.IO.Path.GetFileNameWithoutExtension(finalPath);
                AssetDatabase.CreateAsset(instance, finalPath);
            }

            // Apply patches if provided
            string patchesStr = UnityAgent.ExtractStringField(args, "patches");
            string patchInfo = "";
            if (!string.IsNullOrEmpty(patchesStr))
            {
                patchInfo = ApplyPatchesFromJson(instance, args);
            }

            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssets();

            string guid = AssetDatabase.AssetPathToGUID(finalPath);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"message\":\"ScriptableObject created\"");
            sb.Append(",\"path\":\"").Append(UnityAgent.EscapeJson(finalPath)).Append("\"");
            sb.Append(",\"guid\":\"").Append(UnityAgent.EscapeJson(guid)).Append("\"");
            sb.Append(",\"typeName\":\"").Append(UnityAgent.EscapeJson(resolvedType.FullName)).Append("\"");
            if (!string.IsNullOrEmpty(patchInfo))
                sb.Append(",\"patchInfo\":\"").Append(UnityAgent.EscapeJson(patchInfo)).Append("\"");
            sb.Append("}");
            return new UnityAgent.ToolResult { content = sb.ToString() };
        }

        // =================================================================
        // Modify
        // =================================================================

        private static UnityAgent.ToolResult ModifyScriptableObject(string args)
        {
            string assetPath = UnityAgent.ExtractStringField(args, "assetPath");
            if (string.IsNullOrEmpty(assetPath))
                return AgentToolHelpers.Fail("'assetPath' is required for modify.");

            assetPath = AgentToolHelpers.NormalizePath(assetPath);
            var target = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (target == null)
                return AgentToolHelpers.Fail($"ScriptableObject not found at: {assetPath}");

            // Single property modification
            string propertyPath = UnityAgent.ExtractStringField(args, "propertyPath");
            string valueStr = UnityAgent.ExtractStringField(args, "value");

            if (!string.IsNullOrEmpty(propertyPath))
            {
                var so = new SerializedObject(target);
                so.Update();

                string normalizedPath = NormalizePropertyPath(propertyPath);
                var prop = so.FindProperty(normalizedPath);
                if (prop == null)
                    return AgentToolHelpers.Fail($"Property not found: '{normalizedPath}'.");

                bool ok = SetPropertyValue(prop, valueStr);
                if (!ok)
                    return AgentToolHelpers.Fail($"Failed to set property '{normalizedPath}' to '{valueStr}'.");

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();

                return AgentToolHelpers.Ok($"Set '{normalizedPath}' on {target.name}.");
            }

            // Batch patches
            string patchesStr = UnityAgent.ExtractStringField(args, "patches");
            if (!string.IsNullOrEmpty(patchesStr))
            {
                string patchResult = ApplyPatchesFromJson(target, args);
                return AgentToolHelpers.Ok($"Modified {target.name}. {patchResult}");
            }

            return AgentToolHelpers.Fail("'propertyPath' or 'patches' is required for modify action.");
        }

        // =================================================================
        // Get Info
        // =================================================================

        private static UnityAgent.ToolResult GetScriptableObjectInfo(string args)
        {
            string assetPath = UnityAgent.ExtractStringField(args, "assetPath");
            if (string.IsNullOrEmpty(assetPath))
                return AgentToolHelpers.Fail("'assetPath' is required for get_info.");

            assetPath = AgentToolHelpers.NormalizePath(assetPath);
            var target = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (target == null)
                return AgentToolHelpers.Fail($"ScriptableObject not found at: {assetPath}");

            var so = new SerializedObject(target);
            so.Update();

            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"path\":\"").Append(UnityAgent.EscapeJson(assetPath)).Append("\"");
            sb.Append(",\"typeName\":\"").Append(UnityAgent.EscapeJson(target.GetType().FullName)).Append("\"");
            sb.Append(",\"name\":\"").Append(UnityAgent.EscapeJson(target.name)).Append("\"");

            // Enumerate serialized properties
            sb.Append(",\"fields\":[");
            var iterator = so.GetIterator();
            bool first = true;
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Skip built-in properties
                if (iterator.name == "m_Script" || iterator.name == "m_ObjectHideFlags")
                    continue;

                if (!first) sb.Append(",");
                first = false;

                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(iterator.name)).Append("\"");
                sb.Append(",\"path\":\"").Append(UnityAgent.EscapeJson(iterator.propertyPath)).Append("\"");
                sb.Append(",\"type\":\"").Append(iterator.propertyType.ToString()).Append("\"");
                sb.Append(",\"isArray\":").Append(iterator.isArray ? "true" : "false");

                if (iterator.isArray)
                {
                    sb.Append(",\"arraySize\":").Append(iterator.arraySize);
                }

                // Append value for simple types
                string valStr = GetPropertyValueString(iterator);
                if (valStr != null)
                    sb.Append(",\"value\":").Append(valStr);

                sb.Append("}");
            }
            sb.Append("]");
            sb.Append("}");

            return new UnityAgent.ToolResult { content = sb.ToString() };
        }

        // =================================================================
        // Property helpers
        // =================================================================

        /// <summary>
        /// Normalize friendly array path syntax (e.g., myList[5]) to Unity's
        /// internal format (myList.Array.data[5]).
        /// </summary>
        private static string NormalizePropertyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Replace simple array indexing: fieldName[N] -> fieldName.Array.data[N]
            var result = new StringBuilder();
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '[' && i > 0 && path[i - 1] != ']')
                {
                    // Check if preceded by .Array.data already
                    string before = result.ToString();
                    if (!before.EndsWith(".Array.data"))
                        result.Append(".Array.data");
                }
                result.Append(path[i]);
            }
            return result.ToString();
        }

        private static bool SetPropertyValue(SerializedProperty prop, string value)
        {
            if (prop == null || value == null) return false;

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        if (int.TryParse(value, out int intVal))
                        { prop.intValue = intVal; return true; }
                        break;

                    case SerializedPropertyType.Float:
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
                        { prop.floatValue = floatVal; return true; }
                        break;

                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = value;
                        return true;

                    case SerializedPropertyType.Enum:
                        // Try by name first, then by index
                        for (int i = 0; i < prop.enumNames.Length; i++)
                        {
                            if (prop.enumNames[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                            { prop.enumValueIndex = i; return true; }
                        }
                        if (int.TryParse(value, out int enumIdx) && enumIdx >= 0 && enumIdx < prop.enumNames.Length)
                        { prop.enumValueIndex = enumIdx; return true; }
                        break;

                    case SerializedPropertyType.Color:
                        Color color;
                        if (AgentToolHelpers.TryParseColor(value, out color))
                        { prop.colorValue = color; return true; }
                        break;

                    case SerializedPropertyType.Vector2:
                    {
                        var parts = value.Split(',');
                        if (parts.Length == 2)
                        {
                            float x = AgentToolHelpers.ParseFloat(parts[0].Trim());
                            float y = AgentToolHelpers.ParseFloat(parts[1].Trim());
                            prop.vector2Value = new Vector2(x, y);
                            return true;
                        }
                        break;
                    }

                    case SerializedPropertyType.Vector3:
                    {
                        var parts = value.Split(',');
                        if (parts.Length == 3)
                        {
                            float x = AgentToolHelpers.ParseFloat(parts[0].Trim());
                            float y = AgentToolHelpers.ParseFloat(parts[1].Trim());
                            float z = AgentToolHelpers.ParseFloat(parts[2].Trim());
                            prop.vector3Value = new Vector3(x, y, z);
                            return true;
                        }
                        break;
                    }

                    case SerializedPropertyType.ObjectReference:
                    {
                        // Try loading by asset path
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(value);
                        if (obj != null) { prop.objectReferenceValue = obj; return true; }
                        break;
                    }

                    default:
                        // Unsupported type
                        return false;
                }
            }
            catch { /* fall through */ }

            return false;
        }

        /// <summary>
        /// Get a JSON-safe string representation of a SerializedProperty value.
        /// Returns null for complex/unsupported types.
        /// </summary>
        private static string GetPropertyValueString(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue.ToString();
                case SerializedPropertyType.Float:
                    return prop.floatValue.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return prop.boolValue ? "true" : "false";
                case SerializedPropertyType.String:
                    return $"\"{UnityAgent.EscapeJson(prop.stringValue ?? "")}\"";
                case SerializedPropertyType.Enum:
                    if (prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumNames.Length)
                        return $"\"{prop.enumNames[prop.enumValueIndex]}\"";
                    return prop.enumValueIndex.ToString();
                case SerializedPropertyType.Color:
                    return $"\"{AgentToolHelpers.ColorToHex(prop.colorValue)}\"";
                case SerializedPropertyType.Vector2:
                    return AgentToolHelpers.Vec2Json(prop.vector2Value);
                case SerializedPropertyType.Vector3:
                    return AgentToolHelpers.Vec3Json(prop.vector3Value);
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                        return $"\"{UnityAgent.EscapeJson(prop.objectReferenceValue.name)}\"";
                    return "null";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Apply patches from the "patches" field in the JSON arguments.
        /// Patches are a JSON array of {propertyPath, value, op} objects.
        /// Returns a summary string.
        /// </summary>
        private static string ApplyPatchesFromJson(UnityEngine.Object target, string args)
        {
            // Extract the patches array content
            string patchesKey = "\"patches\"";
            int keyIdx = args.IndexOf(patchesKey, StringComparison.Ordinal);
            if (keyIdx < 0) return "No patches found.";

            int bracketStart = args.IndexOf('[', keyIdx);
            if (bracketStart < 0) return "Invalid patches format.";
            int bracketEnd = UnityAgent.FindMatchingBracket(args, bracketStart);
            if (bracketEnd < 0) return "Invalid patches format.";

            string patchArrayStr = args.Substring(bracketStart, bracketEnd - bracketStart + 1);

            var so = new SerializedObject(target);
            so.Update();

            int successCount = 0;
            int failCount = 0;

            // Simple parse: split by objects in the array
            int pos = 0;
            while (pos < patchArrayStr.Length)
            {
                int objStart = patchArrayStr.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = UnityAgent.FindMatchingBrace(patchArrayStr, objStart);
                if (objEnd < 0) break;

                string patchObj = patchArrayStr.Substring(objStart, objEnd - objStart + 1);
                string propPath = UnityAgent.ExtractStringField(patchObj, "propertyPath")
                    ?? UnityAgent.ExtractStringField(patchObj, "path");
                string val = UnityAgent.ExtractStringField(patchObj, "value");
                string op = UnityAgent.ExtractStringField(patchObj, "op") ?? "set";

                if (!string.IsNullOrEmpty(propPath))
                {
                    string normalizedPath = NormalizePropertyPath(propPath);

                    if (op.Equals("array_resize", StringComparison.OrdinalIgnoreCase))
                    {
                        var arrayProp = so.FindProperty(normalizedPath);
                        if (arrayProp != null && arrayProp.isArray)
                        {
                            int newSize = AgentToolHelpers.ParseInt(val, arrayProp.arraySize);
                            arrayProp.arraySize = newSize;
                            so.ApplyModifiedProperties();
                            so.Update();
                            successCount++;
                        }
                        else failCount++;
                    }
                    else
                    {
                        var prop = so.FindProperty(normalizedPath);
                        if (prop != null && SetPropertyValue(prop, val))
                            successCount++;
                        else
                            failCount++;
                    }
                }
                else
                {
                    failCount++;
                }

                pos = objEnd + 1;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            return $"{successCount} patched, {failCount} failed";
        }

#endif
    }
}
