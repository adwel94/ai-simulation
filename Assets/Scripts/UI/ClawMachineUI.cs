using UnityEngine;

public class ClawMachineUI : MonoBehaviour
{
    [Header("AI References")]
    public ClawMachineAgent clawMachineAgent;
    public VisionGeminiService visionGeminiService;
    public EpisodeLogger episodeLogger;

    [Header("Server References")]
    public SimulationServer simulationServer;

    GUIStyle helpStyle;
    GUIStyle buttonStyle;
    GUIStyle statusStyle;
    GUIStyle smallButtonStyle;
    GUIStyle headerStyle;
    GUIStyle inputStyle;
    GUIStyle toggleStyle;
    GUIStyle serverStatusStyle;

    // API Key
    string apiKeyInput = "";
    bool showApiKey = false;
    bool apiKeySet = false;

    // AI status
    string statusText = "";
    bool isAIRunning = false;

    // Command input
    string commandInput = "빨간 공을 집어";

    // Data collection toggle
    bool collectData = true;

    // Server mode
    bool serverMode = false;
    string serverStatusText = "";

    void Start()
    {
        if (clawMachineAgent == null) clawMachineAgent = FindObjectOfType<ClawMachineAgent>();
        if (visionGeminiService == null) visionGeminiService = FindObjectOfType<VisionGeminiService>();
        if (episodeLogger == null) episodeLogger = FindObjectOfType<EpisodeLogger>();
        if (simulationServer == null) simulationServer = FindObjectOfType<SimulationServer>();

        // Load saved data collection preference
        collectData = PlayerPrefs.GetInt("CollectData", 1) == 1;

        // Load saved server mode preference
        serverMode = PlayerPrefs.GetInt("ServerMode", 0) == 1;

        // Load saved API key
        apiKeyInput = PlayerPrefs.GetString("GeminiApiKey", "");
        if (!string.IsNullOrEmpty(apiKeyInput))
        {
            if (visionGeminiService != null)
                visionGeminiService.apiKey = apiKeyInput;
            apiKeySet = true;
        }

        // Subscribe AI events
        if (clawMachineAgent != null)
        {
            clawMachineAgent.OnStatusChanged += (s) => statusText = s;
            clawMachineAgent.OnCompleted += () => { isAIRunning = false; };
            clawMachineAgent.OnError += (e) => { statusText = $"오류: {e}"; isAIRunning = false; };
        }

        // Subscribe server events
        if (simulationServer != null)
        {
            simulationServer.OnStatusChanged += (s) => serverStatusText = s;
            simulationServer.OnClientConnected += (ip) => serverStatusText = $"Python 연결됨 ({ip})";
        }

        // Apply server mode on start
        if (serverMode)
            ApplyServerMode(true);
    }

    void OnGUI()
    {
        if (helpStyle == null) InitStyles();

        DrawTopRightPanel();

        if (serverMode)
            DrawServerControls();
        else
            DrawAIControls();

        DrawHelpText();
    }

    // ─────────────────────────────────
    //  Top-right panel: API Key + Server Mode
    // ─────────────────────────────────

    void DrawTopRightPanel()
    {
        float rightMargin = 20;
        float y = 20;

        // Server mode toggle
        float serverToggleW = 140;
        float serverToggleX = Screen.width - serverToggleW - rightMargin;
        bool newServerMode = GUI.Toggle(new Rect(serverToggleX, y, serverToggleW, 25), serverMode, " Server Mode", toggleStyle);
        if (newServerMode != serverMode)
        {
            serverMode = newServerMode;
            PlayerPrefs.SetInt("ServerMode", serverMode ? 1 : 0);
            PlayerPrefs.Save();
            ApplyServerMode(serverMode);
        }

        // API Key button (only show in non-server mode)
        if (!serverMode)
        {
            float apiKeyBtnW = 120;
            float apiKeyBtnX = serverToggleX - apiKeyBtnW - 10;
            string toggleText = showApiKey ? "API Key 닫기" : "API Key 설정";
            if (GUI.Button(new Rect(apiKeyBtnX, y, apiKeyBtnW, 30), toggleText, smallButtonStyle))
            {
                showApiKey = !showApiKey;
            }

            if (showApiKey)
                DrawApiKeyPanel(apiKeyBtnX);
        }
    }

    void DrawApiKeyPanel(float rightEdge)
    {
        float panelW = 450;
        float panelX = rightEdge - panelW + 120;
        float panelY = 55;

        GUI.Box(new Rect(panelX - 10, panelY - 5, panelW + 20, 80), "");
        GUI.Label(new Rect(panelX, panelY, panelW, 25), "Gemini API Key:", headerStyle);

        Rect keyRect = new Rect(panelX, panelY + 25, panelW - 80, 30);
        apiKeyInput = GUI.PasswordField(keyRect, apiKeyInput, '*', 100, inputStyle);

        Rect saveRect = new Rect(panelX + panelW - 70, panelY + 25, 70, 30);
        if (GUI.Button(saveRect, "저장", smallButtonStyle))
        {
            if (visionGeminiService != null)
                visionGeminiService.apiKey = apiKeyInput;
            PlayerPrefs.SetString("GeminiApiKey", apiKeyInput);
            PlayerPrefs.Save();
            apiKeySet = true;
            showApiKey = false;
            statusText = "API Key 저장 완료!";
        }
    }

    // ─────────────────────────────────
    //  Server mode controls
    // ─────────────────────────────────

    void DrawServerControls()
    {
        float x = 20;
        float y = Screen.height - 120;
        float btnW = 140;
        float btnH = 35;

        bool isServerRunning = simulationServer != null && simulationServer.IsRunning;

        if (!isServerRunning)
        {
            if (GUI.Button(new Rect(x, y, btnW, btnH), "서버 시작", buttonStyle))
            {
                if (simulationServer != null)
                {
                    simulationServer.StartServer();
                    serverStatusText = "서버 시작 중...";
                }
            }
        }
        else
        {
            if (GUI.Button(new Rect(x, y, btnW, btnH), "서버 중지", buttonStyle))
            {
                if (simulationServer != null)
                {
                    simulationServer.StopServer();
                    serverStatusText = "서버 중지됨";
                }
            }

            // Server info
            float infoX = x + btnW + 15;
            int port = simulationServer != null ? simulationServer.port : 8765;
            int requests = simulationServer != null ? simulationServer.TotalRequests : 0;
            GUI.Label(new Rect(infoX, y, 400, btnH), $"localhost:{port}  |  요청: {requests}건", serverStatusStyle);
        }

        // Server status text
        if (!string.IsNullOrEmpty(serverStatusText))
        {
            Rect statusRect = new Rect(x, y + btnH + 5, Screen.width - 40, 25);
            GUI.Label(statusRect, serverStatusText, statusStyle);
        }
    }

    // ─────────────────────────────────
    //  AI mode controls (기존)
    // ─────────────────────────────────

    void DrawAIControls()
    {
        float btnW = 100;
        float btnH = 35;
        float x = 20;
        float y = Screen.height - 120;

        // Command input field + button on the same row
        float inputW = Screen.width * 0.4f;

        if (!isAIRunning)
        {
            // Command text field
            GUI.SetNextControlName("CommandInput");
            commandInput = GUI.TextField(new Rect(x, y, inputW, btnH), commandInput, inputStyle);

            // Send button
            GUI.enabled = apiKeySet && clawMachineAgent != null && commandInput.Trim().Length > 0;
            bool sendClicked = GUI.Button(new Rect(x + inputW + 10, y, btnW, btnH), "AI 시작", buttonStyle);
            GUI.enabled = true;

            // Data collection toggle (AI 시작 버튼 오른쪽)
            float toggleX = x + inputW + 10 + btnW + 15;
            bool newCollectData = GUI.Toggle(new Rect(toggleX, y + 8, 130, 20), collectData, " 데이터 수집", toggleStyle);
            if (newCollectData != collectData)
            {
                collectData = newCollectData;
                if (episodeLogger != null)
                    episodeLogger.enableLogging = collectData;
                PlayerPrefs.SetInt("CollectData", collectData ? 1 : 0);
                PlayerPrefs.Save();
            }

            // Enter key shortcut
            if (GUI.GetNameOfFocusedControl() == "CommandInput" &&
                Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
                apiKeySet && commandInput.Trim().Length > 0)
            {
                sendClicked = true;
                Event.current.Use();
            }

            if (sendClicked)
            {
                // Apply data collection setting before starting
                if (episodeLogger != null)
                    episodeLogger.enableLogging = collectData;

                isAIRunning = true;
                statusText = $"AI 시작: {commandInput}" + (collectData ? " [데이터 수집 ON]" : "");
                clawMachineAgent.StartLoop(commandInput.Trim());
            }
        }
        else
        {
            // Show current command (read-only)
            GUI.enabled = false;
            GUI.TextField(new Rect(x, y, inputW, btnH), commandInput, inputStyle);
            GUI.enabled = true;

            if (GUI.Button(new Rect(x + inputW + 10, y, btnW, btnH), "AI 중지", buttonStyle))
            {
                clawMachineAgent.StopLoop();
                isAIRunning = false;
                statusText = "중지됨";
            }
        }

        // Status text
        if (!string.IsNullOrEmpty(statusText))
        {
            Rect statusRect = new Rect(x, y + btnH + 5, Screen.width - 40, 25);
            GUI.Label(statusRect, statusText, statusStyle);
        }
    }

    void DrawHelpText()
    {
        float x = Screen.width - 250;
        float y = Screen.height - 110;
        GUI.Label(new Rect(x, y, 230, 25), "방향키: 좌우/전후 이동", helpStyle);
        GUI.Label(new Rect(x, y + 25, 230, 25), "G/H: 위/아래 이동", helpStyle);
        GUI.Label(new Rect(x, y + 50, 230, 25), "Z/X: 집게 열기/닫기", helpStyle);
    }

    // ─────────────────────────────────
    //  Server mode toggle logic
    // ─────────────────────────────────

    void ApplyServerMode(bool enabled)
    {
        if (enabled)
        {
            // Stop AI if running
            if (isAIRunning && clawMachineAgent != null)
            {
                clawMachineAgent.StopLoop();
                isAIRunning = false;
            }
            statusText = "";
            serverStatusText = "서버 모드 활성화. '서버 시작' 버튼을 눌러주세요.";
        }
        else
        {
            // Stop server if running
            if (simulationServer != null && simulationServer.IsRunning)
                simulationServer.StopServer();
            serverStatusText = "";
            statusText = "";
        }
    }

    void InitStyles()
    {
        helpStyle = new GUIStyle(GUI.skin.label);
        helpStyle.fontSize = 14;
        helpStyle.normal.textColor = Color.white;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 16;
        buttonStyle.fontStyle = FontStyle.Bold;

        statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.fontSize = 14;
        statusStyle.normal.textColor = Color.yellow;

        smallButtonStyle = new GUIStyle(GUI.skin.button);
        smallButtonStyle.fontSize = 13;

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 14;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = Color.white;

        inputStyle = new GUIStyle(GUI.skin.textField);
        inputStyle.fontSize = 14;
        inputStyle.padding = new RectOffset(8, 8, 6, 6);

        toggleStyle = new GUIStyle(GUI.skin.toggle);
        toggleStyle.fontSize = 14;
        toggleStyle.normal.textColor = Color.white;
        toggleStyle.onNormal.textColor = new Color(0.5f, 1f, 0.5f);

        serverStatusStyle = new GUIStyle(GUI.skin.label);
        serverStatusStyle.fontSize = 15;
        serverStatusStyle.fontStyle = FontStyle.Bold;
        serverStatusStyle.normal.textColor = new Color(0.5f, 1f, 0.8f);
    }
}
