using UnityEngine;

/// <summary>
/// OnGUI-based UI for the Ball Pick game.
/// Shows controls guide, AI agent panel, and debug info.
/// </summary>
public class BallPickGameUI : MonoBehaviour
{
    public BallPickGameController gameController;
    public BallPickAgent agent;
    public VisionGeminiService visionGeminiService;
    public ScreenshotCapture screenshotCapture;

    [Header("Controls Panel")]
    public Vector2 controlsPanelPos = new Vector2(10, 10);
    public Vector2 controlsPanelSize = new Vector2(220, 500);

    [Header("AI Panel")]
    public Vector2 aiPanelOffset = new Vector2(10, 10);
    public Vector2 aiPanelSize = new Vector2(300, 210);

    [Header("Debug Panel")]
    public Vector2 debugPanelSize = new Vector2(420, 320);
    public float debugPanelBottomMargin = 10f;

    string apiKey = "";
    string commandText = "빨간 공을 집어줘";
    string statusMessage = "";
    bool showDebugPanel;

    GUIStyle boxStyle;
    GUIStyle headerStyle;
    GUIStyle statusStyle;
    bool stylesInitialized;

    void Awake()
    {
        if (gameController == null) gameController = FindObjectOfType<BallPickGameController>();
        if (agent == null) agent = FindObjectOfType<BallPickAgent>();
        if (visionGeminiService == null) visionGeminiService = FindObjectOfType<VisionGeminiService>();
        if (screenshotCapture == null) screenshotCapture = FindObjectOfType<ScreenshotCapture>();

        // Load saved API key
        apiKey = PlayerPrefs.GetString("GeminiApiKey", "");
        if (!string.IsNullOrEmpty(apiKey) && visionGeminiService != null)
            visionGeminiService.apiKey = apiKey;
    }

    void OnEnable()
    {
        if (agent != null)
        {
            agent.OnStatusChanged += OnStatus;
            agent.OnCompleted += () => statusMessage = "완료!";
            agent.OnError += e => statusMessage = $"에러: {e}";
        }
    }

    void OnDisable()
    {
        if (agent != null)
        {
            agent.OnStatusChanged -= OnStatus;
        }
    }

    void OnStatus(string s) { statusMessage = s; }

    void InitStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f)) }
        };

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = 15,
            fontStyle = FontStyle.Bold
        };

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true
        };
    }

    void OnGUI()
    {
        InitStyles();

        DrawControlsPanel();
        DrawAIPanel();

        // Tab toggle debug panel
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
        {
            showDebugPanel = !showDebugPanel;
            Event.current.Use();
        }

        if (showDebugPanel)
            DrawDebugPanel();
    }

    void DrawControlsPanel()
    {
        float w = controlsPanelSize.x, h = controlsPanelSize.y;
        Rect area = new Rect(controlsPanelPos.x, controlsPanelPos.y, w, h);
        GUI.Box(area, "", boxStyle);

        GUILayout.BeginArea(new Rect(area.x + 8, area.y + 8, w - 16, h - 16));

        GUILayout.Label("<b>Ball Pick Game</b>", headerStyle);
        GUILayout.Space(4);

        GUILayout.Label("WASD / 방향키 : 좌우/전후 이동");
        GUILayout.Label("Q : 올리기  /  E : 내리기");
        GUILayout.Label("Z : 집게 열기  /  X : 집게 닫기");
        GUILayout.Label("[  /  ] : 카메라 회전");
        GUILayout.Label("R : 정지");
        GUILayout.Label("Tab : 디버그 패널");

        GUILayout.Space(8);

        if (gameController != null)
        {
            string mode = gameController.IsAIControlled ? "<color=cyan>AI</color>" : "<color=lime>플레이어</color>";
            GUILayout.Label($"모드: {mode}", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"카메라 각도: {gameController.OrbitAngle:F0}");
        }

        GUILayout.EndArea();
    }

    void DrawAIPanel()
    {
        float w = aiPanelSize.x, h = aiPanelSize.y;
        float x = Screen.width - w - aiPanelOffset.x;
        Rect area = new Rect(x, aiPanelOffset.y, w, h);
        GUI.Box(area, "", boxStyle);

        GUILayout.BeginArea(new Rect(area.x + 8, area.y + 8, w - 16, h - 16));

        GUILayout.Label("<b>AI 에이전트</b>", headerStyle);
        GUILayout.Space(2);

        GUILayout.Label("API Key:");
        apiKey = GUILayout.TextField(apiKey, GUILayout.Width(w - 24));

        GUILayout.Space(2);
        GUILayout.Label("명령:");
        commandText = GUILayout.TextField(commandText, GUILayout.Width(w - 24));

        GUILayout.Space(4);

        bool isRunning = agent != null && agent.IsRunning;

        if (!isRunning)
        {
            if (GUILayout.Button("AI 시작", GUILayout.Height(28)))
            {
                if (visionGeminiService != null)
                    visionGeminiService.apiKey = apiKey;
                PlayerPrefs.SetString("GeminiApiKey", apiKey);
                PlayerPrefs.Save();
                agent?.StartLoop(commandText);
            }
        }
        else
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"스텝 {agent.CurrentStep}/{agent.MaxIterations} ({agent.ElapsedTime:F1}초)");
            if (GUILayout.Button("중지", GUILayout.Width(50), GUILayout.Height(22)))
                agent?.StopLoop();
            GUILayout.EndHorizontal();
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            GUILayout.Space(2);
            GUILayout.Label(statusMessage, statusStyle, GUILayout.Width(w - 24));
        }

        GUILayout.EndArea();
    }

    void DrawDebugPanel()
    {
        if (agent == null) return;

        float w = debugPanelSize.x, h = debugPanelSize.y;
        float x = (Screen.width - w) / 2f;
        float y = Screen.height - h - debugPanelBottomMargin;
        Rect area = new Rect(x, y, w, h);
        GUI.Box(area, "", boxStyle);

        GUILayout.BeginArea(new Rect(area.x + 8, area.y + 8, w - 16, h - 16));

        GUILayout.Label("<b>디버그 패널 (Tab으로 닫기)</b>", headerStyle);
        GUILayout.Space(2);

        GUILayout.Label($"액션: {agent.DebugActionType ?? "-"}");
        GUILayout.Label($"이유: {agent.DebugReasoning ?? "-"}");

        GUILayout.Space(4);
        GUILayout.Label("전송 텍스트:");
        GUILayout.TextArea(agent.DebugSentText ?? "", GUILayout.Height(50));

        GUILayout.Label("AI 응답:");
        GUILayout.TextArea(agent.DebugRawResponse ?? "", GUILayout.Height(70));

        // Last captured screenshot thumbnail
        Texture2D lastTex = screenshotCapture != null ? screenshotCapture.LastCapturedTexture : null;
        if (lastTex != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("스크린샷:");
            GUILayout.Box(lastTex, GUILayout.Width(100), GUILayout.Height(100));
            GUILayout.EndHorizontal();
        }

        GUILayout.EndArea();
    }

    static Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }
}
