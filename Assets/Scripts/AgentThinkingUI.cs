using UnityEngine;

namespace LLMAgent
{
    /// <summary>
    /// General-purpose agent status UI.
    /// Shows a "Thinking..." indicator and a status message on screen.
    /// Optionally tracks a world-space transform to display the bubble above it.
    /// </summary>
    public class AgentThinkingUI : MonoBehaviour
    {
        [Header("Follow Target (optional)")]
        [Tooltip("If set, the thinking bubble follows this transform in world space. Otherwise it shows at a fixed screen position.")]
        public Transform followTarget;

        [Header("Thinking Bubble")]
        [Tooltip("Vertical offset above the follow target (world units).")]
        public float bubbleOffsetY = 2.5f;

        public Color bubbleColor = new Color(0f, 0f, 0f, 0.75f);
        public Color textColor = Color.white;

        [Header("Status Panel")]
        [Tooltip("Show status panel in the top-left corner.")]
        public bool showStatusPanel = true;

        // Internal state
        private bool isThinking;
        private string statusMessage = "Ready";
        private string thinkingDots = "";
        private float dotTimer;
        private int dotCount;
        private Camera mainCam;

        // GUI styles
        private GUIStyle bubbleStyle;
        private GUIStyle statusStyle;
        private GUIStyle statusBgStyle;
        private bool stylesInitialized;

        private void Start()
        {
            mainCam = Camera.main;
        }

        private void Update()
        {
            if (isThinking)
            {
                dotTimer += Time.deltaTime;
                if (dotTimer >= 0.5f)
                {
                    dotTimer = 0f;
                    dotCount = (dotCount + 1) % 4;
                    thinkingDots = new string('.', dotCount);
                }
            }
        }

        // --- Public API ---

        public void ShowThinking()
        {
            isThinking = true;
            dotCount = 0;
            dotTimer = 0f;
            thinkingDots = "";
        }

        public void HideThinking()
        {
            isThinking = false;
        }

        public void SetStatus(string message)
        {
            statusMessage = message;
        }

        public void ResetUI()
        {
            isThinking = false;
            statusMessage = "Ready";
            dotCount = 0;
        }

        // --- GUI ---

        private void InitStyles()
        {
            if (stylesInitialized) return;

            bubbleStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            bubbleStyle.normal.textColor = textColor;
            var bgTex = MakeTex(bubbleColor);
            bubbleStyle.normal.background = bgTex;
            bubbleStyle.padding = new RectOffset(12, 12, 6, 6);

            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            statusStyle.normal.textColor = Color.white;

            statusBgStyle = new GUIStyle(GUI.skin.box);
            statusBgStyle.normal.background = MakeTex(new Color(0, 0, 0, 0.6f));

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            // Thinking bubble
            if (isThinking)
            {
                string text = $"Thinking{thinkingDots}";

                if (followTarget != null && mainCam != null)
                {
                    // World-space: float above target
                    Vector3 worldPos = followTarget.position + Vector3.up * bubbleOffsetY;
                    Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0)
                    {
                        float guiY = Screen.height - screenPos.y;
                        Vector2 size = bubbleStyle.CalcSize(new GUIContent(text));
                        size.x = Mathf.Max(size.x + 20, 120);
                        size.y = Mathf.Max(size.y + 10, 36);
                        Rect rect = new Rect(screenPos.x - size.x / 2f, guiY - size.y, size.x, size.y);
                        GUI.Box(rect, text, bubbleStyle);
                    }
                }
                else
                {
                    // Fixed screen position: top-center
                    Vector2 size = bubbleStyle.CalcSize(new GUIContent(text));
                    size.x = Mathf.Max(size.x + 20, 120);
                    size.y = Mathf.Max(size.y + 10, 36);
                    Rect rect = new Rect((Screen.width - size.x) / 2f, 50, size.x, size.y);
                    GUI.Box(rect, text, bubbleStyle);
                }
            }

            // Status panel (top-left)
            if (showStatusPanel && !string.IsNullOrEmpty(statusMessage))
            {
                Rect bgRect = new Rect(10, 10, 300, 30);
                GUI.Box(bgRect, "", statusBgStyle);
                GUI.Label(new Rect(15, 13, 290, 24), $"AI Status: {statusMessage}", statusStyle);
            }
        }

        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }
    }
}
