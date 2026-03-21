using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// ProBuilder mesh creation and editing tools.
    /// All ProBuilder API calls are done via reflection since ProBuilder is an optional package.
    /// Actions: create_shape, extrude_faces, subdivide, get_mesh_info, set_face_material,
    ///          flip_normals, merge_faces, center_pivot, validate_mesh.
    /// </summary>
    public class AgentProBuilderTools : MonoBehaviour
    {
        // =================================================================
        // ProBuilder types resolved via reflection (optional package)
        // =================================================================

        private static Type _pbMeshType;
        private static Type _shapeGeneratorType;
        private static Type _shapeTypeEnum;
        private static Type _extrudeMethodEnum;
        private static Type _extrudeElementsType;
        private static Type _connectElementsType;
        private static Type _mergeElementsType;
        private static Type _faceType;
        private static Type _edgeType;
        private static Type _pivotLocationType;
        private static Type _axisEnum;
        private static Type _editorMeshUtilityType;
        private static Type _meshValidationType;

        private static bool _typesResolved;
        private static bool _proBuilderAvailable;

        // =================================================================
        // Reflection bootstrap
        // =================================================================

        private static bool EnsureProBuilder()
        {
            if (_typesResolved) return _proBuilderAvailable;
            _typesResolved = true;

            _pbMeshType = Type.GetType("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder");
            if (_pbMeshType == null)
            {
                _proBuilderAvailable = false;
                return false;
            }

            _shapeGeneratorType = Type.GetType("UnityEngine.ProBuilder.ShapeGenerator, Unity.ProBuilder");
            _shapeTypeEnum = Type.GetType("UnityEngine.ProBuilder.ShapeType, Unity.ProBuilder");
            _faceType = Type.GetType("UnityEngine.ProBuilder.Face, Unity.ProBuilder");
            _edgeType = Type.GetType("UnityEngine.ProBuilder.Edge, Unity.ProBuilder");
            _extrudeElementsType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.ExtrudeElements, Unity.ProBuilder");
            _extrudeMethodEnum = Type.GetType("UnityEngine.ProBuilder.ExtrudeMethod, Unity.ProBuilder");
            _connectElementsType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.ConnectElements, Unity.ProBuilder");
            _mergeElementsType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.MergeElements, Unity.ProBuilder");
            _pivotLocationType = Type.GetType("UnityEngine.ProBuilder.PivotLocation, Unity.ProBuilder");
            _axisEnum = Type.GetType("UnityEngine.ProBuilder.Axis, Unity.ProBuilder");
            _editorMeshUtilityType = Type.GetType("UnityEditor.ProBuilder.EditorMeshUtility, Unity.ProBuilder.Editor");
            _meshValidationType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.MeshValidation, Unity.ProBuilder");

            _proBuilderAvailable = true;
            return true;
        }

        private static string ProBuilderMissingMsg =>
            "ProBuilder package is not installed. Install com.unity.probuilder via Package Manager.";

        // =================================================================
        // Tool: manageProBuilder
        // =================================================================

        [AgentTool("manageProBuilder",
            "Create and edit ProBuilder meshes. " +
            "Actions: create_shape, extrude_faces, subdivide, get_mesh_info, set_face_material, " +
            "flip_normals, merge_faces, center_pivot, validate_mesh. " +
            "Requires com.unity.probuilder package.",
            ParametersType = typeof(ProBuilderParams))]
        private IEnumerator HandleManageProBuilder(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");
            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required."));
                yield break;
            }

            if (!EnsureProBuilder())
            {
                callback(AgentToolHelpers.Fail(ProBuilderMissingMsg));
                yield break;
            }

            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "create_shape":
                        callback(CreateShape(arguments));
                        break;
                    case "extrude_faces":
                        callback(ExtrudeFaces(arguments));
                        break;
                    case "subdivide":
                        callback(Subdivide(arguments));
                        break;
                    case "get_mesh_info":
                        callback(GetMeshInfo(arguments));
                        break;
                    case "set_face_material":
                        callback(SetFaceMaterial(arguments));
                        break;
                    case "flip_normals":
                        callback(FlipNormals(arguments));
                        break;
                    case "merge_faces":
                        callback(MergeFaces(arguments));
                        break;
                    case "center_pivot":
                        callback(CenterPivot(arguments));
                        break;
                    case "validate_mesh":
                        callback(ValidateMesh(arguments));
                        break;
                    default:
                        callback(AgentToolHelpers.Fail($"Unknown action: '{action}'. " +
                            "Valid: create_shape, extrude_faces, subdivide, get_mesh_info, " +
                            "set_face_material, flip_normals, merge_faces, center_pivot, validate_mesh."));
                        break;
                }
            }
            catch (Exception ex)
            {
                callback(AgentToolHelpers.Fail($"{action} failed: {ex.Message}"));
            }

            yield break;
        }

        // =================================================================
        // Parameters
        // =================================================================

        public class ProBuilderParams
        {
            [ToolParam("Action to perform.", required: true)]
            public string action;

            [ToolParam("Target GameObject name (for operations on existing meshes).")]
            public string target;

            [ToolParam("Shape type: Cube, Sphere, Cylinder, Plane, Cone, Torus, Stair, Arch.")]
            public string shapeType;

            [ToolParam("Name for the created GameObject.")]
            public string name;

            [ToolParam("Uniform size for shape creation.", SchemaType = "number")]
            public float size;

            [ToolParam("Width dimension.", SchemaType = "number")]
            public float width;

            [ToolParam("Height dimension.", SchemaType = "number")]
            public float height;

            [ToolParam("Depth dimension.", SchemaType = "number")]
            public float depth;

            [ToolParam("Radius for cylindrical/spherical shapes.", SchemaType = "number")]
            public float radius;

            [ToolParam("Face indices array for face operations.", SchemaType = "array")]
            public string faceIndices;

            [ToolParam("Extrude distance.", SchemaType = "number")]
            public float distance;

            [ToolParam("Extrude method: FaceNormal, VertexNormal, IndividualFaces.")]
            public string method;

            [ToolParam("Material asset path (for set_face_material).")]
            public string materialPath;

            [ToolParam("Position as {x,y,z} object.", SchemaType = "object")]
            public string position;

            [ToolParam("Rotation as {x,y,z} Euler angles.", SchemaType = "object")]
            public string rotation;
        }

        // =================================================================
        // Reflection helpers
        // =================================================================

        private static GameObject FindTarget(string args)
        {
            string targetName = UnityAgent.ExtractStringField(args, "target");
            if (string.IsNullOrEmpty(targetName)) return null;
            return AgentToolHelpers.FindTarget(targetName, null);
        }

        private static Component GetPBMesh(GameObject go)
        {
            if (go == null || _pbMeshType == null) return null;
            return go.GetComponent(_pbMeshType);
        }

        private static Component RequirePBMesh(string args)
        {
            var go = FindTarget(args);
            if (go == null)
                throw new Exception("Target GameObject not found.");
            var pbMesh = GetPBMesh(go);
            if (pbMesh == null)
                throw new Exception($"GameObject '{go.name}' does not have a ProBuilderMesh component.");
            return pbMesh;
        }

        private static void RefreshMesh(Component pbMesh)
        {
            var toMesh = _pbMeshType.GetMethod("ToMesh", Type.EmptyTypes)
                ?? _pbMeshType.GetMethod("ToMesh", BindingFlags.Instance | BindingFlags.Public);
            toMesh?.Invoke(pbMesh, toMesh.GetParameters().Length > 0
                ? new object[toMesh.GetParameters().Length] : null);

            var refresh = _pbMeshType.GetMethod("Refresh", Type.EmptyTypes)
                ?? _pbMeshType.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public);
            refresh?.Invoke(pbMesh, refresh.GetParameters().Length > 0
                ? new object[refresh.GetParameters().Length] : null);

            if (_editorMeshUtilityType != null)
            {
                var optimize = _editorMeshUtilityType.GetMethod("Optimize",
                    BindingFlags.Static | BindingFlags.Public,
                    null, new[] { _pbMeshType }, null);
                optimize?.Invoke(null, new object[] { pbMesh });
            }
        }

        private static int GetFaceCount(Component pbMesh)
        {
            var prop = _pbMeshType.GetProperty("faceCount");
            return prop != null ? (int)prop.GetValue(pbMesh) : -1;
        }

        private static int GetVertexCount(Component pbMesh)
        {
            var prop = _pbMeshType.GetProperty("vertexCount");
            return prop != null ? (int)prop.GetValue(pbMesh) : -1;
        }

        private static object GetFacesArray(Component pbMesh)
        {
            var prop = _pbMeshType.GetProperty("faces");
            return prop?.GetValue(pbMesh);
        }

        private static Array GetFacesByIndices(Component pbMesh, string args)
        {
            var allFaces = GetFacesArray(pbMesh);
            if (allFaces == null)
                throw new Exception("Could not read faces from ProBuilderMesh.");
            var facesList = (System.Collections.IList)allFaces;

            string indicesStr = UnityAgent.ExtractStringField(args, "faceIndices");
            if (string.IsNullOrEmpty(indicesStr))
            {
                // Return all faces
                var all = Array.CreateInstance(_faceType, facesList.Count);
                for (int i = 0; i < facesList.Count; i++)
                    all.SetValue(facesList[i], i);
                return all;
            }

            // Parse "[0,1,2]" format
            var indices = ParseIntArray(args, "faceIndices");
            var result = Array.CreateInstance(_faceType, indices.Length);
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] < 0 || indices[i] >= facesList.Count)
                    throw new Exception($"Face index {indices[i]} out of range (0-{facesList.Count - 1}).");
                result.SetValue(facesList[indices[i]], i);
            }
            return result;
        }

        private static System.Collections.IList ToTypedFaceList(Array faces)
        {
            var listType = typeof(List<>).MakeGenericType(_faceType);
            var list = Activator.CreateInstance(listType) as System.Collections.IList;
            foreach (var f in faces) list.Add(f);
            return list;
        }

        private static object GetPivotCenter()
        {
            if (_pivotLocationType == null) return null;
            return Enum.ToObject(_pivotLocationType, 0); // PivotLocation.Center = 0
        }

        private static Component InvokeGenerator(string methodName, Type[] paramTypes, object[] args)
        {
            if (_shapeGeneratorType == null) return null;
            var method = _shapeGeneratorType.GetMethod(methodName,
                BindingFlags.Static | BindingFlags.Public, null, paramTypes, null);
            return method?.Invoke(null, args) as Component;
        }

        /// <summary>Parse an integer array from a JSON field like "faceIndices": [0,1,2]</summary>
        private static int[] ParseIntArray(string json, string fieldName)
        {
            string pattern = "\"" + fieldName + "\"";
            int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIdx < 0) return new int[0];

            int bracketStart = json.IndexOf('[', keyIdx);
            if (bracketStart < 0) return new int[0];
            int bracketEnd = UnityAgent.FindMatchingBracket(json, bracketStart);
            if (bracketEnd < 0) return new int[0];

            string arrayStr = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            var parts = arrayStr.Split(',');
            var result = new List<int>();
            foreach (var p in parts)
            {
                string trimmed = p.Trim();
                if (int.TryParse(trimmed, out int val))
                    result.Add(val);
            }
            return result.ToArray();
        }

        // =================================================================
        // Actions
        // =================================================================

        private static UnityAgent.ToolResult CreateShape(string args)
        {
            string shapeType = UnityAgent.ExtractStringField(args, "shapeType");
            if (string.IsNullOrEmpty(shapeType))
                return AgentToolHelpers.Fail("'shapeType' is required (Cube, Sphere, Cylinder, Plane, Cone, Torus, Stair, Arch).");

            if (_shapeGeneratorType == null || _shapeTypeEnum == null)
                return AgentToolHelpers.Fail("ShapeGenerator type not found in ProBuilder assembly.");

            var pivot = GetPivotCenter();
            Component pbMesh = null;

            // Parse dimension parameters
            float size = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(args, "size"));
            float width = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(args, "width"));
            float height = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(args, "height"));
            float depth = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(args, "depth"));
            float radius = AgentToolHelpers.ParseFloat(UnityAgent.ExtractNumberField(args, "radius"));

            // Try shape-specific generators
            if (pivot != null)
            {
                try
                {
                    pbMesh = CreateShapeViaGenerator(shapeType, pivot, size, width, height, depth, radius, args);
                }
                catch { /* fall through to generic */ }
            }

            // Fallback: generic CreateShape(ShapeType)
            if (pbMesh == null)
                pbMesh = CreateShapeGeneric(shapeType);

            if (pbMesh == null)
                return AgentToolHelpers.Fail($"Failed to create ProBuilder shape '{shapeType}'.");

            var go = pbMesh.gameObject;

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, $"Create ProBuilder {shapeType}");
#endif

            string objectName = UnityAgent.ExtractStringField(args, "name");
            if (!string.IsNullOrEmpty(objectName))
                go.name = objectName;

            // Apply position
            var pos = AgentToolHelpers.ParseVec3FromArgs(args, "position");
            if (pos.HasValue) go.transform.position = pos.Value;

            // Apply rotation
            var rot = AgentToolHelpers.ParseVec3FromArgs(args, "rotation");
            if (rot.HasValue) go.transform.eulerAngles = rot.Value;

            RefreshMesh(pbMesh);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"message\":\"Created ProBuilder ").Append(shapeType).Append("\"");
            sb.Append(",\"gameObjectName\":\"").Append(UnityAgent.EscapeJson(go.name)).Append("\"");
            sb.Append(",\"instanceId\":").Append(go.GetInstanceID());
            sb.Append(",\"shapeType\":\"").Append(UnityAgent.EscapeJson(shapeType)).Append("\"");
            sb.Append(",\"faceCount\":").Append(GetFaceCount(pbMesh));
            sb.Append(",\"vertexCount\":").Append(GetVertexCount(pbMesh));
            sb.Append("}");
            return new UnityAgent.ToolResult { content = sb.ToString() };
        }

        private static Component CreateShapeViaGenerator(string shapeType, object pivot,
            float size, float width, float height, float depth, float radius, string args)
        {
            switch (shapeType.ToUpperInvariant())
            {
                case "CUBE":
                {
                    float w = width > 0 ? width : (size > 0 ? size : 1f);
                    float h = height > 0 ? height : (size > 0 ? size : 1f);
                    float d = depth > 0 ? depth : (size > 0 ? size : 1f);
                    return InvokeGenerator("GenerateCube",
                        new[] { _pivotLocationType, typeof(Vector3) },
                        new object[] { pivot, new Vector3(w, h, d) });
                }

                case "CYLINDER":
                {
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    float h = height > 0 ? height : (size > 0 ? size : 2f);
                    int axisDivisions = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "segments"), 24);
                    return InvokeGenerator("GenerateCylinder",
                        new[] { _pivotLocationType, typeof(int), typeof(float), typeof(float), typeof(int), typeof(int) },
                        new object[] { pivot, axisDivisions, r, h, 0, -1 });
                }

                case "SPHERE":
                {
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    int subdivisions = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "subdivisions"), 2);
                    return InvokeGenerator("GenerateIcosahedron",
                        new[] { _pivotLocationType, typeof(float), typeof(int), typeof(bool), typeof(bool) },
                        new object[] { pivot, r, subdivisions, true, false });
                }

                case "PLANE":
                {
                    float w = width > 0 ? width : (size > 0 ? size : 1f);
                    float h = height > 0 ? height : (depth > 0 ? depth : (size > 0 ? size : 1f));
                    int widthCuts = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "widthCuts"), 0);
                    int heightCuts = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "heightCuts"), 0);
                    if (_axisEnum != null)
                    {
                        var axisObj = Enum.ToObject(_axisEnum, 2); // Y-up
                        return InvokeGenerator("GeneratePlane",
                            new[] { _pivotLocationType, typeof(float), typeof(float), typeof(int), typeof(int), _axisEnum },
                            new object[] { pivot, w, h, widthCuts, heightCuts, axisObj });
                    }
                    return InvokeGenerator("GeneratePlane",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(int), typeof(int) },
                        new object[] { pivot, w, h, widthCuts, heightCuts });
                }

                case "CONE":
                {
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    float h = height > 0 ? height : (size > 0 ? size : 1f);
                    int segments = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "segments"), 6);
                    return InvokeGenerator("GenerateCone",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(int) },
                        new object[] { pivot, r, h, segments });
                }

                case "TORUS":
                {
                    int rows = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "rows"), 8);
                    int columns = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "columns"), 16);
                    float ringRadius = radius > 0 ? radius : (size > 0 ? size / 2f : 0.5f);
                    float tubeRadius = AgentToolHelpers.ParseFloat(
                        UnityAgent.ExtractNumberField(args, "tubeRadius"), ringRadius * 0.2f);
                    return InvokeGenerator("GenerateTorus",
                        new[] { _pivotLocationType, typeof(int), typeof(int), typeof(float), typeof(float),
                                typeof(bool), typeof(float), typeof(float), typeof(bool) },
                        new object[] { pivot, rows, columns, ringRadius, tubeRadius, true, 360f, 360f, false });
                }

                case "STAIR":
                {
                    float w = width > 0 ? width : (size > 0 ? size : 2f);
                    float h = height > 0 ? height : (size > 0 ? size : 2.5f);
                    float d = depth > 0 ? depth : (size > 0 ? size : 4f);
                    int steps = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "steps"), 10);
                    return InvokeGenerator("GenerateStair",
                        new[] { _pivotLocationType, typeof(Vector3), typeof(int), typeof(bool) },
                        new object[] { pivot, new Vector3(w, h, d), steps, true });
                }

                case "ARCH":
                {
                    float angle = AgentToolHelpers.ParseFloat(
                        UnityAgent.ExtractNumberField(args, "angle"), 180f);
                    float r = radius > 0 ? radius : (size > 0 ? size / 2f : 2f);
                    float w = width > 0 ? width : 0.5f;
                    float d = depth > 0 ? depth : 0.5f;
                    int radialCuts = AgentToolHelpers.ParseInt(
                        UnityAgent.ExtractNumberField(args, "radialCuts"), 6);
                    return InvokeGenerator("GenerateArch",
                        new[] { _pivotLocationType, typeof(float), typeof(float), typeof(float), typeof(float),
                                typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) },
                        new object[] { pivot, angle, r, w, d, radialCuts, true, true, true, true, true });
                }

                default:
                    return null;
            }
        }

        private static Component CreateShapeGeneric(string shapeTypeStr)
        {
            if (_shapeTypeEnum == null || _shapeGeneratorType == null) return null;

            object shapeTypeValue;
            try
            {
                shapeTypeValue = Enum.Parse(_shapeTypeEnum, shapeTypeStr, true);
            }
            catch
            {
                var validTypes = string.Join(", ", Enum.GetNames(_shapeTypeEnum));
                throw new Exception($"Unknown shape type '{shapeTypeStr}'. Valid: {validTypes}");
            }

            var createMethod = _shapeGeneratorType.GetMethod("CreateShape",
                BindingFlags.Static | BindingFlags.Public, null,
                new[] { _shapeTypeEnum }, null);

            object[] invokeArgs;
            if (createMethod != null)
            {
                invokeArgs = new[] { shapeTypeValue };
            }
            else if (_pivotLocationType != null)
            {
                createMethod = _shapeGeneratorType.GetMethod("CreateShape",
                    BindingFlags.Static | BindingFlags.Public, null,
                    new[] { _shapeTypeEnum, _pivotLocationType }, null);
                invokeArgs = new[] { shapeTypeValue, GetPivotCenter() };
            }
            else
            {
                return null;
            }

            return createMethod?.Invoke(null, invokeArgs) as Component;
        }

        private static UnityAgent.ToolResult ExtrudeFaces(string args)
        {
            var pbMesh = RequirePBMesh(args);
            var faces = GetFacesByIndices(pbMesh, args);
            float distance = AgentToolHelpers.ParseFloat(
                UnityAgent.ExtractNumberField(args, "distance"), 0.5f);

            string methodStr = UnityAgent.ExtractStringField(args, "method") ?? "FaceNormal";
            if (_extrudeMethodEnum == null || _extrudeElementsType == null)
                return AgentToolHelpers.Fail("Extrude types not found in ProBuilder assembly.");

            object extrudeMethod;
            try { extrudeMethod = Enum.Parse(_extrudeMethodEnum, methodStr, true); }
            catch { return AgentToolHelpers.Fail($"Unknown extrude method '{methodStr}'. Valid: FaceNormal, VertexNormal, IndividualFaces"); }

#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(pbMesh, "Extrude Faces");
#endif

            var extrudeMethodInfo = _extrudeElementsType.GetMethod("Extrude",
                BindingFlags.Static | BindingFlags.Public, null,
                new[] { _pbMeshType, faces.GetType(), _extrudeMethodEnum, typeof(float) }, null);

            if (extrudeMethodInfo == null)
                return AgentToolHelpers.Fail("ExtrudeElements.Extrude method not found.");

            extrudeMethodInfo.Invoke(null, new object[] { pbMesh, faces, extrudeMethod, distance });
            RefreshMesh(pbMesh);

            return AgentToolHelpers.Ok($"Extruded {faces.Length} face(s) by {distance}");
        }

        private static UnityAgent.ToolResult Subdivide(string args)
        {
            var pbMesh = RequirePBMesh(args);

            if (_connectElementsType == null)
                return AgentToolHelpers.Fail("ConnectElements type not found in ProBuilder assembly.");

#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(pbMesh, "Subdivide");
#endif

            var faces = GetFacesByIndices(pbMesh, args);
            var faceList = ToTypedFaceList(faces);

            var connectMethods = _connectElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public);
            MethodInfo connectMethod = null;
            foreach (var m in connectMethods)
            {
                if (m.Name == "Connect" && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.IsAssignableFrom(faceList.GetType()))
                {
                    connectMethod = m;
                    break;
                }
            }

            if (connectMethod == null)
                return AgentToolHelpers.Fail("ConnectElements.Connect (faces) method not found.");

            connectMethod.Invoke(null, new object[] { pbMesh, faceList });
            RefreshMesh(pbMesh);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"message\":\"Subdivided mesh\"");
            sb.Append(",\"faceCount\":").Append(GetFaceCount(pbMesh));
            sb.Append(",\"vertexCount\":").Append(GetVertexCount(pbMesh));
            sb.Append("}");
            return new UnityAgent.ToolResult { content = sb.ToString() };
        }

        private static UnityAgent.ToolResult GetMeshInfo(string args)
        {
            var pbMesh = RequirePBMesh(args);
            var allFaces = GetFacesArray(pbMesh);
            var facesList = (System.Collections.IList)allFaces;

            var renderer = pbMesh.gameObject.GetComponent<MeshRenderer>();
            Bounds bounds = renderer != null ? renderer.bounds : new Bounds();

            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"gameObjectName\":\"").Append(UnityAgent.EscapeJson(pbMesh.gameObject.name)).Append("\"");
            sb.Append(",\"instanceId\":").Append(pbMesh.gameObject.GetInstanceID());
            sb.Append(",\"faceCount\":").Append(GetFaceCount(pbMesh));
            sb.Append(",\"vertexCount\":").Append(GetVertexCount(pbMesh));
            sb.Append(",\"bounds\":{");
            sb.Append("\"center\":").Append(AgentToolHelpers.Vec3Json(bounds.center));
            sb.Append(",\"size\":").Append(AgentToolHelpers.Vec3Json(bounds.size));
            sb.Append("}");

            // Materials
            sb.Append(",\"materials\":[");
            if (renderer != null)
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    string matName = mats[i] != null ? mats[i].name : "(none)";
                    sb.Append("\"").Append(UnityAgent.EscapeJson(matName)).Append("\"");
                }
            }
            sb.Append("]");
            sb.Append("}");

            return new UnityAgent.ToolResult { content = sb.ToString() };
        }

        private static UnityAgent.ToolResult SetFaceMaterial(string args)
        {
#if UNITY_EDITOR
            var pbMesh = RequirePBMesh(args);
            var faces = GetFacesByIndices(pbMesh, args);

            string materialPath = UnityAgent.ExtractStringField(args, "materialPath");
            if (string.IsNullOrEmpty(materialPath))
                return AgentToolHelpers.Fail("'materialPath' is required.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
                return AgentToolHelpers.Fail($"Material not found at path: {materialPath}");

            Undo.RegisterCompleteObjectUndo(pbMesh, "Set Face Material");

            var setMatMethod = _pbMeshType.GetMethod("SetMaterial",
                BindingFlags.Instance | BindingFlags.Public);
            if (setMatMethod == null)
                return AgentToolHelpers.Fail("SetMaterial method not found on ProBuilderMesh.");

            setMatMethod.Invoke(pbMesh, new object[] { faces, material });
            RefreshMesh(pbMesh);

            return AgentToolHelpers.Ok($"Set material '{material.name}' on {faces.Length} face(s).");
#else
            return AgentToolHelpers.Fail("set_face_material requires Unity Editor.");
#endif
        }

        private static UnityAgent.ToolResult FlipNormals(string args)
        {
            var pbMesh = RequirePBMesh(args);
            var faces = GetFacesByIndices(pbMesh, args);

#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(pbMesh, "Flip Normals");
#endif

            var reverseMethod = _faceType.GetMethod("Reverse");
            if (reverseMethod == null)
                return AgentToolHelpers.Fail("Face.Reverse method not found.");

            foreach (var face in faces)
                reverseMethod.Invoke(face, null);

            RefreshMesh(pbMesh);
            return AgentToolHelpers.Ok($"Flipped normals on {faces.Length} face(s).");
        }

        private static UnityAgent.ToolResult MergeFaces(string args)
        {
            var pbMesh = RequirePBMesh(args);
            var faces = GetFacesByIndices(pbMesh, args);

            if (_mergeElementsType == null)
                return AgentToolHelpers.Fail("MergeElements type not found in ProBuilder assembly.");

#if UNITY_EDITOR
            Undo.RegisterCompleteObjectUndo(pbMesh, "Merge Faces");
#endif

            var faceList = ToTypedFaceList(faces);

            var mergeMethods = _mergeElementsType.GetMethods(BindingFlags.Static | BindingFlags.Public);
            MethodInfo mergeMethod = null;
            foreach (var m in mergeMethods)
            {
                if (m.Name == "Merge" && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.IsAssignableFrom(faceList.GetType()))
                {
                    mergeMethod = m;
                    break;
                }
            }

            if (mergeMethod == null)
                return AgentToolHelpers.Fail("MergeElements.Merge method not found.");

            mergeMethod.Invoke(null, new object[] { pbMesh, faceList });
            RefreshMesh(pbMesh);

            var sb = new StringBuilder();
            sb.Append("{\"success\":true,\"message\":\"Merged ").Append(faces.Length).Append(" face(s)\"");
            sb.Append(",\"faceCount\":").Append(GetFaceCount(pbMesh));
            sb.Append("}");
            return new UnityAgent.ToolResult { content = sb.ToString() };
        }

        private static UnityAgent.ToolResult CenterPivot(string args)
        {
            var pbMesh = RequirePBMesh(args);

            var positionsProp = _pbMeshType.GetProperty("positions");
            var positions = positionsProp?.GetValue(pbMesh) as System.Collections.IList;
            if (positions == null || positions.Count == 0)
                return AgentToolHelpers.Fail("Could not read vertex positions.");

            // Compute local-space bounds center
            var min = (Vector3)positions[0];
            var max = min;
            foreach (Vector3 pos in positions)
            {
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            var localCenter = (min + max) * 0.5f;

            if (localCenter.sqrMagnitude < 0.0001f)
                return AgentToolHelpers.Ok("Pivot is already centered.");

#if UNITY_EDITOR
            Undo.RecordObject(pbMesh, "Center Pivot");
            Undo.RecordObject(pbMesh.transform, "Center Pivot");
#endif

            // Offset all vertices by -localCenter
            var newPositions = new Vector3[positions.Count];
            for (int i = 0; i < positions.Count; i++)
                newPositions[i] = (Vector3)positions[i] - localCenter;

            // Set positions via property setter
            var posSetMethod = _pbMeshType.GetProperty("positions");
            if (posSetMethod != null && posSetMethod.CanWrite)
            {
                // Convert to IList<Vector3>
                var posList = new List<Vector3>(newPositions);
                posSetMethod.SetValue(pbMesh, posList);
            }
            else
            {
                // Try RebuildWithPositionsAndFaces or similar
                var setPositions = _pbMeshType.GetMethod("SetVertices",
                    BindingFlags.Instance | BindingFlags.Public, null,
                    new[] { typeof(IList<Vector3>) }, null);
                if (setPositions != null)
                    setPositions.Invoke(pbMesh, new object[] { new List<Vector3>(newPositions) });
            }

            // Move transform to compensate
            var worldOffset = pbMesh.transform.TransformVector(localCenter);
            pbMesh.transform.position += worldOffset;

            RefreshMesh(pbMesh);
            return AgentToolHelpers.Ok("Pivot centered to mesh bounds center.");
        }

        private static UnityAgent.ToolResult ValidateMesh(string args)
        {
            var pbMesh = RequirePBMesh(args);

            var positionsProp = _pbMeshType.GetProperty("positions");
            var positions = positionsProp?.GetValue(pbMesh) as System.Collections.IList;
            var allFaces = GetFacesArray(pbMesh);
            var facesList = (System.Collections.IList)allFaces;

            int degenerateCount = 0;
            var indexesProp = _faceType.GetProperty("indexes");
            var usedVertices = new HashSet<int>();

            if (indexesProp != null && positions != null)
            {
                foreach (var face in facesList)
                {
                    var indexes = indexesProp.GetValue(face) as System.Collections.IList;
                    if (indexes == null) continue;

                    foreach (int idx in indexes)
                        usedVertices.Add(idx);

                    // Check triangles for degeneracy
                    for (int i = 0; i + 2 < indexes.Count; i += 3)
                    {
                        var p0 = (Vector3)positions[(int)indexes[i]];
                        var p1 = (Vector3)positions[(int)indexes[i + 1]];
                        var p2 = (Vector3)positions[(int)indexes[i + 2]];
                        float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                        if (area < 1e-6f) degenerateCount++;
                    }
                }
            }

            int totalVerts = positions?.Count ?? 0;
            int unusedVerts = totalVerts - usedVertices.Count;

            var issues = new List<string>();
            if (degenerateCount > 0)
                issues.Add($"{degenerateCount} degenerate triangle(s)");
            if (unusedVerts > 0)
                issues.Add($"{unusedVerts} unused vertex/vertices");

            var sb = new StringBuilder();
            sb.Append("{\"success\":true");
            sb.Append(",\"healthy\":").Append(issues.Count == 0 ? "true" : "false");
            sb.Append(",\"faceCount\":").Append(facesList.Count);
            sb.Append(",\"vertexCount\":").Append(totalVerts);
            sb.Append(",\"degenerateTriangles\":").Append(degenerateCount);
            sb.Append(",\"unusedVertices\":").Append(unusedVerts);
            sb.Append(",\"issues\":[");
            for (int i = 0; i < issues.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(UnityAgent.EscapeJson(issues[i])).Append("\"");
            }
            sb.Append("]}");
            return new UnityAgent.ToolResult { content = sb.ToString() };
        }
    }
}
