using System;
using System.Collections;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
#endif

namespace LLMAgent.Tools
{
    /// <summary>
    /// Test runner tools (Editor-only).
    /// Tools: runTests
    /// </summary>
    public class AgentTestTools : MonoBehaviour
    {
#if UNITY_EDITOR
        // =================================================================
        // Tool: runTests
        // =================================================================

        [AgentTool("runTests",
            "Run Unity Test Runner tests. Mode: 'edit' for EditMode tests, 'play' for PlayMode tests. " +
            "Can filter by test name. Returns test results with pass/fail status.",
            ParametersType = typeof(RunTestsParams))]
        private IEnumerator HandleRunTests(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string mode = UnityAgent.ExtractStringField(arguments, "mode") ?? "edit";
            string filter = UnityAgent.ExtractStringField(arguments, "filter");

            var testMode = mode.ToLower() == "play" ? TestMode.PlayMode : TestMode.EditMode;

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var resultCollector = new TestResultCollector();
            api.RegisterCallbacks(resultCollector);

            var filterObj = new Filter
            {
                testMode = testMode
            };

            if (!string.IsNullOrEmpty(filter))
            {
                filterObj.testNames = new[] { filter };
            }

            api.Execute(new ExecutionSettings(filterObj));

            // Wait for tests to complete (with timeout)
            float timeout = 120f;
            float elapsed = 0f;
            while (!resultCollector.IsComplete && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (!resultCollector.IsComplete)
            {
                callback(AgentToolHelpers.Fail("Test execution timed out after 120 seconds."));
                yield break;
            }

            callback(new UnityAgent.ToolResult { content = resultCollector.GetResultJson() });
        }

        public class RunTestsParams
        {
            [ToolParam("Test mode: 'edit' (EditMode) or 'play' (PlayMode). Default 'edit'.")]
            public string mode;
            [ToolParam("Filter tests by name (partial match).")]
            public string filter;
        }

        private class TestResultCollector : ICallbacks
        {
            private readonly StringBuilder sb = new StringBuilder();
            private int passed, failed, skipped, total;
            public bool IsComplete { get; private set; }

            public TestResultCollector()
            {
                sb.Append("{\"results\":[");
            }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                // Remove trailing comma if any
                string s = sb.ToString();
                if (s.EndsWith(","))
                    sb.Remove(sb.Length - 1, 1);

                sb.Append("],\"summary\":{");
                sb.Append("\"total\":").Append(total);
                sb.Append(",\"passed\":").Append(passed);
                sb.Append(",\"failed\":").Append(failed);
                sb.Append(",\"skipped\":").Append(skipped);
                sb.Append(",\"duration\":").Append(result.Duration.ToString("F2"));
                sb.Append(",\"overallResult\":\"").Append(UnityAgent.EscapeJson(result.ResultState)).Append("\"");
                sb.Append("}}");

                IsComplete = true;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren)
                {
                    total++;
                    if (result.TestStatus == TestStatus.Passed) passed++;
                    else if (result.TestStatus == TestStatus.Failed) failed++;
                    else skipped++;

                    sb.Append("{\"name\":\"").Append(UnityAgent.EscapeJson(result.Name)).Append("\"");
                    sb.Append(",\"status\":\"").Append(result.TestStatus.ToString()).Append("\"");
                    sb.Append(",\"duration\":").Append(result.Duration.ToString("F3"));
                    if (result.TestStatus == TestStatus.Failed && !string.IsNullOrEmpty(result.Message))
                        sb.Append(",\"message\":\"").Append(UnityAgent.EscapeJson(
                            AgentToolHelpers.Truncate(result.Message, 500))).Append("\"");
                    sb.Append("},");
                }
            }

            public string GetResultJson() => sb.ToString();
        }

#else
        void Awake()
        {
            Debug.LogWarning("[AgentTestTools] Test tools are only available in the Unity Editor.");
        }
#endif
    }
}
