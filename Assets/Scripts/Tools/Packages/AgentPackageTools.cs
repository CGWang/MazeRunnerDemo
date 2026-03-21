using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Unity Package Manager (UPM) tools (Editor-only).
    /// Tools: managePackage (add, remove, list, search)
    /// </summary>
    public class AgentPackageTools : MonoBehaviour
    {
#if UNITY_EDITOR
        // =================================================================
        // Tool: managePackage
        // =================================================================

        [AgentTool("managePackage",
            "Manage Unity packages via the Package Manager. Actions: 'list' (installed packages), " +
            "'add' (install package by name or git URL), 'remove' (uninstall package), " +
            "'search' (search Unity registry), 'list_registries' (list scoped registries from manifest.json), " +
            "'add_registry' (add a scoped registry), 'remove_registry' (remove a scoped registry), " +
            "'embed_package' (embed a package locally), 'resolve_packages' (force resolve all packages).",
            ParametersType = typeof(ManagePackageParams))]
        private IEnumerator HandleManagePackage(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string action = UnityAgent.ExtractStringField(arguments, "action");

            if (string.IsNullOrEmpty(action))
            {
                callback(AgentToolHelpers.Fail("'action' is required."));
                yield break;
            }

            switch (action.ToLower())
            {
                case "list":
                    yield return HandleList(callback);
                    break;
                case "add":
                    yield return HandleAdd(arguments, callback);
                    break;
                case "remove":
                    yield return HandleRemove(arguments, callback);
                    break;
                case "search":
                    yield return HandleSearch(arguments, callback);
                    break;
                case "list_registries":
                    HandleListRegistries(callback);
                    break;
                case "add_registry":
                    HandleAddRegistry(arguments, callback);
                    break;
                case "remove_registry":
                    HandleRemoveRegistry(arguments, callback);
                    break;
                case "embed_package":
                    yield return HandleEmbedPackage(arguments, callback);
                    break;
                case "resolve_packages":
                    HandleResolvePackages(callback);
                    break;
                default:
                    callback(AgentToolHelpers.Fail(
                        $"Unknown action '{action}'. Use: list, add, remove, search, list_registries, add_registry, remove_registry, embed_package, resolve_packages."));
                    break;
            }
        }

        public class ManagePackageParams
        {
            [ToolParam("Action: list, add, remove, search, list_registries, add_registry, remove_registry, embed_package, resolve_packages.", required: true)]
            public string action;
            [ToolParam("Package identifier for add/embed_package (e.g. 'com.unity.textmeshpro', 'https://github.com/user/repo.git').")]
            public string packageId;
            [ToolParam("Package name for remove.")]
            public string packageName;
            [ToolParam("Search query (for search action).")]
            public string query;
            [ToolParam("Registry name (for add_registry, remove_registry).")]
            public string registryName;
            [ToolParam("Registry URL (for add_registry, remove_registry).")]
            public string registryUrl;
            [ToolParam("Comma-separated scopes (for add_registry, e.g. 'com.example,com.example.sub').")]
            public string scopes;
        }

        private IEnumerator HandleList(Action<UnityAgent.ToolResult> callback)
        {
            var request = Client.List(true);
            while (!request.IsCompleted) yield return null;

            if (request.Status == StatusCode.Failure)
            {
                callback(AgentToolHelpers.Fail($"List failed: {request.Error?.message}"));
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append("{\"packages\":[");
            int count = 0;
            foreach (var pkg in request.Result)
            {
                if (count > 0) sb.Append(",");
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(pkg.name)).Append("\"");
                sb.Append(",\"version\":\"").Append(UnityAgent.EscapeJson(pkg.version)).Append("\"");
                sb.Append(",\"displayName\":\"").Append(UnityAgent.EscapeJson(pkg.displayName)).Append("\"");
                sb.Append(",\"source\":\"").Append(pkg.source.ToString()).Append("\"}");
                count++;
            }
            sb.Append("],\"count\":").Append(count).Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private IEnumerator HandleAdd(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string packageId = UnityAgent.ExtractStringField(arguments, "packageId");
            if (string.IsNullOrEmpty(packageId))
            {
                callback(AgentToolHelpers.Fail("'packageId' is required for add."));
                yield break;
            }

            var request = Client.Add(packageId);
            while (!request.IsCompleted) yield return null;

            if (request.Status == StatusCode.Failure)
            {
                callback(AgentToolHelpers.Fail($"Add failed: {request.Error?.message}"));
                yield break;
            }

            callback(AgentToolHelpers.Ok(
                $"Installed {request.Result.displayName} ({request.Result.name}@{request.Result.version})"));
        }

        private IEnumerator HandleRemove(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string packageName = UnityAgent.ExtractStringField(arguments, "packageName");
            if (string.IsNullOrEmpty(packageName))
            {
                callback(AgentToolHelpers.Fail("'packageName' is required for remove."));
                yield break;
            }

            var request = Client.Remove(packageName);
            while (!request.IsCompleted) yield return null;

            if (request.Status == StatusCode.Failure)
            {
                callback(AgentToolHelpers.Fail($"Remove failed: {request.Error?.message}"));
                yield break;
            }

            callback(AgentToolHelpers.Ok($"Removed package: {packageName}"));
        }

        private IEnumerator HandleSearch(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string query = UnityAgent.ExtractStringField(arguments, "query");
            if (string.IsNullOrEmpty(query))
            {
                callback(AgentToolHelpers.Fail("'query' is required for search."));
                yield break;
            }

            var request = Client.SearchAll(query);
            while (!request.IsCompleted) yield return null;

            if (request.Status == StatusCode.Failure)
            {
                callback(AgentToolHelpers.Fail($"Search failed: {request.Error?.message}"));
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append("{\"results\":[");
            int count = 0;
            foreach (var pkg in request.Result)
            {
                if (count > 0) sb.Append(",");
                sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(pkg.name)).Append("\"");
                sb.Append(",\"version\":\"").Append(UnityAgent.EscapeJson(pkg.versions.latest)).Append("\"");
                sb.Append(",\"displayName\":\"").Append(UnityAgent.EscapeJson(pkg.displayName)).Append("\"");
                sb.Append(",\"description\":\"").Append(UnityAgent.EscapeJson(
                    AgentToolHelpers.Truncate(pkg.description, 200))).Append("\"}");
                count++;
                if (count >= 50) break;
            }
            sb.Append("],\"count\":").Append(count).Append("}");
            callback(new UnityAgent.ToolResult { content = sb.ToString() });
        }

        private string GetManifestPath()
        {
            return Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        }

        private void HandleListRegistries(Action<UnityAgent.ToolResult> callback)
        {
            try
            {
                string manifestPath = GetManifestPath();
                if (!File.Exists(manifestPath))
                {
                    callback(AgentToolHelpers.Fail("Packages/manifest.json not found."));
                    return;
                }

                string manifestText = File.ReadAllText(manifestPath);
                var sb = new StringBuilder();
                sb.Append("{\"registries\":[");

                // Simple JSON parsing for scopedRegistries array
                int registriesStart = manifestText.IndexOf("\"scopedRegistries\"");
                int count = 0;
                if (registriesStart >= 0)
                {
                    int arrayStart = manifestText.IndexOf('[', registriesStart);
                    if (arrayStart >= 0)
                    {
                        int depth = 0;
                        int objStart = -1;
                        for (int i = arrayStart; i < manifestText.Length; i++)
                        {
                            char c = manifestText[i];
                            if (c == '[' && depth == 0) { depth = 1; continue; }
                            if (c == '{' && depth == 1) { objStart = i; depth = 2; continue; }
                            if (c == '{') { depth++; continue; }
                            if (c == '}') { depth--; if (depth == 1 && objStart >= 0)
                            {
                                if (count > 0) sb.Append(",");
                                sb.Append(manifestText.Substring(objStart, i - objStart + 1));
                                count++;
                                objStart = -1;
                            }
                            continue; }
                            if (c == ']' && depth == 1) break;
                        }
                    }
                }

                sb.Append("],\"count\":").Append(count).Append("}");
                callback(new UnityAgent.ToolResult { content = sb.ToString() });
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to read registries: {e.Message}"));
            }
        }

        private void HandleAddRegistry(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string regName = UnityAgent.ExtractStringField(arguments, "registryName");
            string regUrl = UnityAgent.ExtractStringField(arguments, "registryUrl");
            string scopesStr = UnityAgent.ExtractStringField(arguments, "scopes");

            if (string.IsNullOrEmpty(regName))
            {
                callback(AgentToolHelpers.Fail("'registryName' is required for add_registry."));
                return;
            }
            if (string.IsNullOrEmpty(regUrl))
            {
                callback(AgentToolHelpers.Fail("'registryUrl' is required for add_registry."));
                return;
            }
            if (string.IsNullOrEmpty(scopesStr))
            {
                callback(AgentToolHelpers.Fail("'scopes' is required for add_registry (comma-separated)."));
                return;
            }

            try
            {
                string manifestPath = GetManifestPath();
                if (!File.Exists(manifestPath))
                {
                    callback(AgentToolHelpers.Fail("Packages/manifest.json not found."));
                    return;
                }

                string manifestText = File.ReadAllText(manifestPath);

                // Check for duplicate
                if (manifestText.Contains(regName) || manifestText.Contains(regUrl))
                {
                    callback(AgentToolHelpers.Fail($"A registry with name '{regName}' or URL '{regUrl}' may already exist."));
                    return;
                }

                // Build the registry JSON entry
                string[] scopes = scopesStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var scopesSb = new StringBuilder();
                for (int i = 0; i < scopes.Length; i++)
                {
                    if (i > 0) scopesSb.Append(", ");
                    scopesSb.Append("\"").Append(scopes[i].Trim()).Append("\"");
                }

                string newEntry = "{\n      \"name\": \"" + regName + "\",\n      \"url\": \"" + regUrl +
                    "\",\n      \"scopes\": [" + scopesSb + "]\n    }";

                // Insert into scopedRegistries array or create it
                int registriesIdx = manifestText.IndexOf("\"scopedRegistries\"");
                if (registriesIdx >= 0)
                {
                    // Find closing bracket of the array
                    int arrayStart = manifestText.IndexOf('[', registriesIdx);
                    int arrayEnd = -1;
                    int depth = 0;
                    for (int i = arrayStart; i < manifestText.Length; i++)
                    {
                        if (manifestText[i] == '[') depth++;
                        else if (manifestText[i] == ']') { depth--; if (depth == 0) { arrayEnd = i; break; } }
                    }
                    if (arrayEnd > arrayStart)
                    {
                        // Check if array has existing entries
                        string inside = manifestText.Substring(arrayStart + 1, arrayEnd - arrayStart - 1).Trim();
                        string prefix = string.IsNullOrEmpty(inside) ? "\n    " : ",\n    ";
                        manifestText = manifestText.Insert(arrayEnd, prefix + newEntry);
                    }
                }
                else
                {
                    // Add scopedRegistries before the closing brace
                    int lastBrace = manifestText.LastIndexOf('}');
                    string insertion = ",\n  \"scopedRegistries\": [\n    " + newEntry + "\n  ]";
                    manifestText = manifestText.Insert(lastBrace, insertion);
                }

                File.WriteAllText(manifestPath, manifestText);
                Client.Resolve();

                callback(AgentToolHelpers.Ok($"Added scoped registry '{regName}' with URL '{regUrl}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to add registry: {e.Message}"));
            }
        }

        private void HandleRemoveRegistry(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string regName = UnityAgent.ExtractStringField(arguments, "registryName");
            string regUrl = UnityAgent.ExtractStringField(arguments, "registryUrl");

            if (string.IsNullOrEmpty(regName) && string.IsNullOrEmpty(regUrl))
            {
                callback(AgentToolHelpers.Fail("Either 'registryName' or 'registryUrl' is required for remove_registry."));
                return;
            }

            try
            {
                string manifestPath = GetManifestPath();
                if (!File.Exists(manifestPath))
                {
                    callback(AgentToolHelpers.Fail("Packages/manifest.json not found."));
                    return;
                }

                string manifestText = File.ReadAllText(manifestPath);
                int registriesIdx = manifestText.IndexOf("\"scopedRegistries\"");
                if (registriesIdx < 0)
                {
                    callback(AgentToolHelpers.Fail("No scoped registries configured."));
                    return;
                }

                // Find the registry object that matches name or url
                string searchTerm = !string.IsNullOrEmpty(regName) ? regName : regUrl;
                int entryStart = manifestText.IndexOf(searchTerm);
                if (entryStart < 0)
                {
                    callback(AgentToolHelpers.Fail($"Registry matching '{searchTerm}' not found."));
                    return;
                }

                // Walk backward to find the opening brace of this registry object
                int objStart = manifestText.LastIndexOf('{', entryStart);
                // Walk forward to find the closing brace
                int depth = 0;
                int objEnd = -1;
                for (int i = objStart; i < manifestText.Length; i++)
                {
                    if (manifestText[i] == '{') depth++;
                    else if (manifestText[i] == '}') { depth--; if (depth == 0) { objEnd = i; break; } }
                }

                if (objStart < 0 || objEnd < 0)
                {
                    callback(AgentToolHelpers.Fail("Failed to parse registry entry from manifest."));
                    return;
                }

                // Remove the object including any leading comma/whitespace or trailing comma
                int removeStart = objStart;
                int removeEnd = objEnd + 1;

                // Handle comma before or after
                if (removeStart > 0 && manifestText[removeStart - 1] == ',')
                    removeStart--;
                else if (removeEnd < manifestText.Length && manifestText[removeEnd] == ',')
                    removeEnd++;

                // Trim whitespace around the removed section
                while (removeStart > 0 && (manifestText[removeStart - 1] == ' ' || manifestText[removeStart - 1] == '\n' || manifestText[removeStart - 1] == '\r'))
                    removeStart--;
                // Keep at least one newline
                if (removeStart > 0 && manifestText[removeStart - 1] != '\n')
                    removeStart++;

                manifestText = manifestText.Remove(removeStart, removeEnd - removeStart);
                File.WriteAllText(manifestPath, manifestText);
                Client.Resolve();

                callback(AgentToolHelpers.Ok($"Removed scoped registry matching '{searchTerm}'."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to remove registry: {e.Message}"));
            }
        }

        private IEnumerator HandleEmbedPackage(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string packageId = UnityAgent.ExtractStringField(arguments, "packageId");
            if (string.IsNullOrEmpty(packageId))
            {
                // Also try packageName as a fallback
                packageId = UnityAgent.ExtractStringField(arguments, "packageName");
            }
            if (string.IsNullOrEmpty(packageId))
            {
                callback(AgentToolHelpers.Fail("'packageId' is required for embed_package."));
                yield break;
            }

            var request = Client.Embed(packageId);
            while (!request.IsCompleted) yield return null;

            if (request.Status == StatusCode.Failure)
            {
                callback(AgentToolHelpers.Fail($"Embed failed: {request.Error?.message}"));
                yield break;
            }

            callback(AgentToolHelpers.Ok(
                $"Embedded {request.Result.displayName} ({request.Result.name}@{request.Result.version}) to local Packages folder."));
        }

        private void HandleResolvePackages(Action<UnityAgent.ToolResult> callback)
        {
            try
            {
                Client.Resolve();
                callback(AgentToolHelpers.Ok("Package resolution triggered. Unity will re-resolve all packages."));
            }
            catch (Exception e)
            {
                callback(AgentToolHelpers.Fail($"Failed to trigger package resolution: {e.Message}"));
            }
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentPackageTools] Package tools are only available in the Unity Editor.");
        }
#endif
    }
}
