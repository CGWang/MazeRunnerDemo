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

#else
        void Awake()
        {
            Debug.LogWarning("[AgentPackageTools] Package tools are only available in the Unity Editor.");
        }
#endif
    }
}
