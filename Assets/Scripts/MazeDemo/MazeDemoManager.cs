using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LLMAgent
{
    /// <summary>
    /// Maze Runner demo — extends AgentManager with maze-specific tools and UI.
    /// </summary>
    public class MazeDemoManager : AgentManager
    {
        [Header("Maze Settings")]
        [Tooltip("Message sent when 'Auto Explore' is clicked.")]
        [TextArea(2, 3)]
        public string autoExploreMessage = "红色标记是迷宫终点，走到终点。";

        [Header("Maze UI")]
        public MazeAgentUI mazeUI;

        // =================================================================
        // Lifecycle
        // =================================================================

        protected override void Awake()
        {
            base.Awake();
            if (mazeUI == null) mazeUI = FindObjectOfType<MazeAgentUI>();
        }

        // =================================================================
        // Maze-specific tools — auto-discovered via [AgentTool] attributes
        // =================================================================

        // =================================================================
        // Agent response — check maze completion
        // =================================================================

        protected override void OnAgentResponse(string response)
        {
            var playerObj = GameObject.FindWithTag("Player");
            bool completed = playerObj != null &&
                playerObj.GetComponent<MazeGoalDetector>()?.HasReachedGoal == true;

            if (completed)
            {
                mazeUI?.ShowMazeCompleted();
                thinkingUI?.SetStatus("Maze Completed!");
            }
            else
            {
                thinkingUI?.SetStatus("Ready");
            }
        }

        // =================================================================
        // Tool handlers
        // =================================================================

        [AgentTool("getPlayerStatus",
            "Get the player's current position and obstacle distances in all 4 cardinal " +
            "directions (north/south/east/west), measured in grid cells. Also reports whether " +
            "the goal has been reached.")]
        private IEnumerator HandleGetPlayerStatus(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string result = null;
            bool done = false;
            MazePlayerBridge.GetPlayerStatus(r => { result = r; done = true; });
            while (!done) yield return null;
            callback(new UnityAgent.ToolResult { content = result });
        }

        [AgentTool("movePath",
            "Move the player along a sequence of direction segments. Each segment has a " +
            "compass direction and a number of grid cells to move. Stops early if blocked " +
            "by a wall or if the goal is reached. Maximum 20 segments, 1-10 cells per step.",
            ParametersJson = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""segments"": {
                        ""type"": ""array"",
                        ""description"": ""Array of movement segments."",
                        ""items"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""dir"": {
                                    ""type"": ""string"",
                                    ""enum"": [""north"", ""south"", ""east"", ""west""]
                                },
                                ""steps"": {
                                    ""type"": ""integer"",
                                    ""minimum"": 1,
                                    ""maximum"": 10
                                }
                            },
                            ""required"": [""dir"", ""steps""]
                        }
                    }
                },
                ""required"": [""segments""]
            }",
            RequiresPermission = true)]
        private IEnumerator HandleMovePath(string arguments, Action<UnityAgent.ToolResult> callback)
        {
            string directionsJson, distancesJson;
            if (!ParseMovePathArgs(arguments, out directionsJson, out distancesJson))
            {
                callback(new UnityAgent.ToolResult
                {
                    content = "{\"success\":false,\"error\":\"Failed to parse movePath arguments.\"}"
                });
                yield break;
            }

            string result = null;
            bool done = false;
            MazePlayerBridge.MoveSequenceV2(directionsJson, distancesJson, r =>
            {
                result = r;
                done = true;
            });
            while (!done) yield return null;
            callback(new UnityAgent.ToolResult { content = result });
        }

        // =================================================================
        // movePath argument parser
        // =================================================================

        private static bool ParseMovePathArgs(string arguments, out string directionsJson, out string distancesJson)
        {
            directionsJson = null;
            distancesJson = null;

            var directions = new List<string>();
            var distances = new List<string>();

            int segIdx = arguments.IndexOf("\"segments\"", StringComparison.Ordinal);
            if (segIdx < 0) segIdx = 0;

            int arrStart = arguments.IndexOf('[', segIdx);
            if (arrStart < 0) return false;
            int arrEnd = UnityAgent.FindMatchingBracket(arguments, arrStart);
            if (arrEnd < 0) return false;

            string arrStr = arguments.Substring(arrStart, arrEnd - arrStart + 1);
            int pos = 0;
            while (pos < arrStr.Length)
            {
                int objStart = arrStr.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = UnityAgent.FindMatchingBrace(arrStr, objStart);
                if (objEnd < 0) break;

                string seg = arrStr.Substring(objStart, objEnd - objStart + 1);
                string dir = UnityAgent.ExtractStringField(seg, "dir")
                    ?? UnityAgent.ExtractStringField(seg, "direction");
                string steps = UnityAgent.ExtractNumberField(seg, "steps")
                    ?? UnityAgent.ExtractNumberField(seg, "distance");

                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(steps))
                {
                    directions.Add($"\"{dir}\"");
                    distances.Add(steps);
                }

                pos = objEnd + 1;
            }

            if (directions.Count == 0) return false;
            directionsJson = "[" + string.Join(",", directions) + "]";
            distancesJson = "[" + string.Join(",", distances) + "]";
            return true;
        }

        // =================================================================
        // OnGUI — maze-specific buttons (Auto Explore, Reset)
        // =================================================================

        protected override void OnGUI()
        {
            base.OnGUI(); // Stop button from AgentManager

            float btnW = 130f, btnH = 30f, pad = 8f;
            float x = (chatUI != null && chatUI.anchorRight)
                ? Screen.width - (chatUI?.panelWidth ?? 380f) - btnW - pad * 2
                : (chatUI?.panelWidth ?? 380f) + pad;
            float y = pad;

            GUI.skin.button.fontSize = 13;

            if (agent != null && !agent.IsRunning)
            {
                if (agent.IsConfigured && GUI.Button(new Rect(x, y, btnW, btnH), "Auto Explore"))
                {
                    chatUI?.AddUserMessage(autoExploreMessage);
                    HandleUserMessage(autoExploreMessage);
                }
                y += btnH + 4;

                if (GUI.Button(new Rect(x, y, btnW, btnH), "Reset Maze"))
                {
                    ResetMaze();
                }
            }
        }

        private void ResetMaze()
        {
            ClearSession();

            var playerObj = GameObject.FindWithTag("Player");
            playerObj?.GetComponent<MazeGoalDetector>()?.ResetGoal();
            mazeUI?.ResetUI();
        }
    }
}
