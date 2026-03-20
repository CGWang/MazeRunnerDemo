using System;
using System.Collections.Generic;
using UnityEngine;

namespace LLMAgent
{
    /// <summary>
    /// Chat panel UI for UnityAgent. Displays conversation messages, tool call activity,
    /// and streaming text. Provides an input field for the user to send messages.
    ///
    /// Attach to any GameObject in the scene. Wire up via:
    ///   chatUI.BindAgent(agent);
    ///   chatUI.OnUserMessage += (msg) => agent.SendMessageAsync(msg, ...);
    /// </summary>
    public class AgentChatUI : MonoBehaviour
    {
        // =================================================================
        // Configuration
        // =================================================================

        [Header("Panel Layout")]
        [Tooltip("Width of the chat panel in pixels.")]
        public float panelWidth = 380f;

        [Tooltip("Panel side: true = right, false = left.")]
        public bool anchorRight = true;

        [Tooltip("Panel background opacity (0-1).")]
        [Range(0f, 1f)]
        public float panelOpacity = 0.85f;

        [Header("Appearance")]
        public int fontSize = 13;
        public int inputFontSize = 14;

        // =================================================================
        // Events
        // =================================================================

        /// <summary>Fired when the user submits a message. Wire this to send to the agent.</summary>
        public event Action<string> OnUserMessage;

        // =================================================================
        // Internal state
        // =================================================================

        private enum MsgType { User, Assistant, Tool, System }

        private struct ChatMsg
        {
            public MsgType type;
            public string text;
        }

        private readonly List<ChatMsg> messages = new List<ChatMsg>();
        private string inputText = "";
        private Vector2 scrollPos;
        private bool autoScroll = true;
        private string streamingBuffer = "";
        private bool isStreaming;

        // GUI styles
        private GUIStyle panelBgStyle;
        private GUIStyle userMsgStyle;
        private GUIStyle assistantMsgStyle;
        private GUIStyle toolMsgStyle;
        private GUIStyle systemMsgStyle;
        private GUIStyle inputStyle;
        private GUIStyle sendBtnStyle;
        private GUIStyle headerStyle;
        private bool stylesInit;

        // Textures
        private Texture2D panelBgTex;
        private Texture2D userBgTex;
        private Texture2D assistantBgTex;
        private Texture2D toolBgTex;
        private Texture2D inputBgTex;
        private Texture2D sendBtnTex;

        // =================================================================
        // Agent binding — subscribe to events
        // =================================================================

        public void BindAgent(UnityAgent agent)
        {
            agent.OnStreamToken += OnStreamToken;
            agent.OnToolCallBegin += OnToolCallBegin;
            agent.OnToolCallEnd += OnToolCallEnd;
            agent.OnGenerationStart += OnGenStart;
            agent.OnGenerationEnd += OnGenEnd;
        }

        public void UnbindAgent(UnityAgent agent)
        {
            agent.OnStreamToken -= OnStreamToken;
            agent.OnToolCallBegin -= OnToolCallBegin;
            agent.OnToolCallEnd -= OnToolCallEnd;
            agent.OnGenerationStart -= OnGenStart;
            agent.OnGenerationEnd -= OnGenEnd;
        }

        // =================================================================
        // Public API
        // =================================================================

        public void AddUserMessage(string text)
        {
            messages.Add(new ChatMsg { type = MsgType.User, text = text });
            autoScroll = true;
        }

        public void AddAssistantMessage(string text)
        {
            messages.Add(new ChatMsg { type = MsgType.Assistant, text = text });
            autoScroll = true;
        }

        public void AddSystemMessage(string text)
        {
            messages.Add(new ChatMsg { type = MsgType.System, text = text });
            autoScroll = true;
        }

        public void Clear()
        {
            messages.Clear();
            streamingBuffer = "";
            isStreaming = false;
        }

        // =================================================================
        // Agent event handlers
        // =================================================================

        private void OnStreamToken(string token)
        {
            streamingBuffer += token;
            autoScroll = true;
        }

        private void OnToolCallBegin(string toolName, string toolId)
        {
            // Flush any streaming text first
            FlushStreaming();
            messages.Add(new ChatMsg
            {
                type = MsgType.Tool,
                text = $">> {toolName}()"
            });
            autoScroll = true;
        }

        private void OnToolCallEnd(string toolName, string toolId, string result)
        {
            // Show truncated result
            string display = result;
            if (display != null && display.Length > 200)
                display = display.Substring(0, 200) + "...";
            messages.Add(new ChatMsg
            {
                type = MsgType.Tool,
                text = $"<< {toolName}: {display}"
            });
            autoScroll = true;
        }

        private void OnGenStart()
        {
            streamingBuffer = "";
            isStreaming = true;
        }

        private void OnGenEnd()
        {
            FlushStreaming();
            isStreaming = false;
        }

        private void FlushStreaming()
        {
            if (!string.IsNullOrEmpty(streamingBuffer))
            {
                messages.Add(new ChatMsg { type = MsgType.Assistant, text = streamingBuffer.Trim() });
                streamingBuffer = "";
            }
        }

        // =================================================================
        // OnGUI
        // =================================================================

        private void InitStyles()
        {
            if (stylesInit) return;

            // Textures
            panelBgTex = MakeTex(new Color(0.12f, 0.12f, 0.15f, panelOpacity));
            userBgTex = MakeTex(new Color(0.2f, 0.35f, 0.55f, 0.9f));
            assistantBgTex = MakeTex(new Color(0.22f, 0.22f, 0.28f, 0.9f));
            toolBgTex = MakeTex(new Color(0.15f, 0.2f, 0.15f, 0.85f));
            inputBgTex = MakeTex(new Color(0.18f, 0.18f, 0.22f, 1f));
            sendBtnTex = MakeTex(new Color(0.25f, 0.45f, 0.7f, 1f));

            // Panel
            panelBgStyle = new GUIStyle(GUI.skin.box);
            panelBgStyle.normal.background = panelBgTex;

            // Messages
            userMsgStyle = MakeMsgStyle(userBgTex, new Color(0.9f, 0.95f, 1f));
            assistantMsgStyle = MakeMsgStyle(assistantBgTex, new Color(0.9f, 0.9f, 0.85f));
            toolMsgStyle = MakeMsgStyle(toolBgTex, new Color(0.6f, 0.8f, 0.6f));
            toolMsgStyle.fontSize = fontSize - 1;
            toolMsgStyle.fontStyle = FontStyle.Italic;

            systemMsgStyle = MakeMsgStyle(toolBgTex, new Color(0.7f, 0.7f, 0.5f));
            systemMsgStyle.fontStyle = FontStyle.Italic;
            systemMsgStyle.alignment = TextAnchor.MiddleCenter;

            // Input
            inputStyle = new GUIStyle(GUI.skin.textField);
            inputStyle.fontSize = inputFontSize;
            inputStyle.normal.background = inputBgTex;
            inputStyle.normal.textColor = Color.white;
            inputStyle.focused.background = inputBgTex;
            inputStyle.focused.textColor = Color.white;
            inputStyle.padding = new RectOffset(8, 8, 6, 6);
            inputStyle.wordWrap = true;

            // Send button
            sendBtnStyle = new GUIStyle(GUI.skin.button);
            sendBtnStyle.fontSize = inputFontSize;
            sendBtnStyle.fontStyle = FontStyle.Bold;
            sendBtnStyle.normal.background = sendBtnTex;
            sendBtnStyle.normal.textColor = Color.white;
            sendBtnStyle.hover.background = sendBtnTex;
            sendBtnStyle.hover.textColor = new Color(0.85f, 0.9f, 1f);

            // Header
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = fontSize + 2;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = new Color(0.8f, 0.8f, 0.85f);
            headerStyle.alignment = TextAnchor.MiddleCenter;

            stylesInit = true;
        }

        private GUIStyle MakeMsgStyle(Texture2D bg, Color textColor)
        {
            var style = new GUIStyle(GUI.skin.box);
            style.normal.background = bg;
            style.normal.textColor = textColor;
            style.fontSize = fontSize;
            style.wordWrap = true;
            style.alignment = TextAnchor.UpperLeft;
            style.padding = new RectOffset(8, 8, 5, 5);
            style.margin = new RectOffset(4, 4, 2, 2);
            style.richText = true;
            return style;
        }

        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }

        private void OnGUI()
        {
            InitStyles();

            float panelH = Screen.height;
            float panelX = anchorRight ? Screen.width - panelWidth : 0;
            float pad = 8f;
            float headerH = 30f;
            float inputH = 36f;
            float sendBtnW = 60f;
            float bottomBarH = inputH + pad * 2;

            // Panel background
            GUI.Box(new Rect(panelX, 0, panelWidth, panelH), "", panelBgStyle);

            // Header
            GUI.Label(new Rect(panelX, pad, panelWidth, headerH), "AI Agent Chat", headerStyle);

            // Message area
            float msgAreaY = pad + headerH + pad;
            float msgAreaH = panelH - msgAreaY - bottomBarH;
            Rect scrollViewRect = new Rect(panelX + pad, msgAreaY, panelWidth - pad * 2, msgAreaH);

            // Calculate content height
            float contentW = panelWidth - pad * 4 - 16; // account for scrollbar
            float totalH = CalculateContentHeight(contentW);

            // Auto-scroll
            if (autoScroll)
            {
                scrollPos.y = Mathf.Max(0, totalH - msgAreaH);
                autoScroll = false;
            }

            scrollPos = GUI.BeginScrollView(scrollViewRect,
                scrollPos, new Rect(0, 0, contentW, totalH));

            float y = 0;
            foreach (var msg in messages)
            {
                GUIStyle style = GetStyleForType(msg.type);
                string prefix = GetPrefix(msg.type);
                string display = prefix + msg.text;

                float h = style.CalcHeight(new GUIContent(display), contentW);
                GUI.Box(new Rect(0, y, contentW, h), display, style);
                y += h + 3;
            }

            // Show streaming text (not yet committed to messages)
            if (isStreaming && !string.IsNullOrEmpty(streamingBuffer))
            {
                string streamDisplay = streamingBuffer + "...";
                float h = assistantMsgStyle.CalcHeight(new GUIContent(streamDisplay), contentW);
                GUI.Box(new Rect(0, y, contentW, h), streamDisplay, assistantMsgStyle);
                y += h + 3;
            }

            GUI.EndScrollView();

            // Input bar
            float inputY = panelH - bottomBarH + pad;
            float inputW = panelWidth - pad * 3 - sendBtnW;

            // Handle Enter key submission
            bool enterPressed = Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                !Event.current.shift;

            GUI.SetNextControlName("ChatInput");
            inputText = GUI.TextField(
                new Rect(panelX + pad, inputY, inputW, inputH),
                inputText, inputStyle);

            bool sendClicked = GUI.Button(
                new Rect(panelX + pad + inputW + pad, inputY, sendBtnW, inputH),
                "Send", sendBtnStyle);

            if ((sendClicked || (enterPressed && GUI.GetNameOfFocusedControl() == "ChatInput"))
                && !string.IsNullOrWhiteSpace(inputText))
            {
                string msg = inputText.Trim();
                inputText = "";
                AddUserMessage(msg);
                OnUserMessage?.Invoke(msg);

                if (enterPressed) Event.current.Use();
            }
        }

        private float CalculateContentHeight(float width)
        {
            float h = 0;
            foreach (var msg in messages)
            {
                GUIStyle style = GetStyleForType(msg.type);
                string display = GetPrefix(msg.type) + msg.text;
                h += style.CalcHeight(new GUIContent(display), width) + 3;
            }

            if (isStreaming && !string.IsNullOrEmpty(streamingBuffer))
            {
                h += assistantMsgStyle.CalcHeight(
                    new GUIContent(streamingBuffer + "..."), width) + 3;
            }

            return h;
        }

        private GUIStyle GetStyleForType(MsgType type)
        {
            switch (type)
            {
                case MsgType.User: return userMsgStyle;
                case MsgType.Assistant: return assistantMsgStyle;
                case MsgType.Tool: return toolMsgStyle;
                case MsgType.System: return systemMsgStyle;
                default: return assistantMsgStyle;
            }
        }

        private static string GetPrefix(MsgType type)
        {
            switch (type)
            {
                case MsgType.User: return "<b>You:</b> ";
                case MsgType.Assistant: return "<b>AI:</b> ";
                case MsgType.Tool: return "";
                case MsgType.System: return "";
                default: return "";
            }
        }

        private void OnDestroy()
        {
            // Clean up textures
            if (panelBgTex != null) Destroy(panelBgTex);
            if (userBgTex != null) Destroy(userBgTex);
            if (assistantBgTex != null) Destroy(assistantBgTex);
            if (toolBgTex != null) Destroy(toolBgTex);
            if (inputBgTex != null) Destroy(inputBgTex);
            if (sendBtnTex != null) Destroy(sendBtnTex);
        }
    }
}
