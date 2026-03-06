using UnityEngine;

public class CommandInputUI : MonoBehaviour
{
    [Header("Text Mode References")]
    public GeminiService geminiService;
    public ActionSequencer actionSequencer;
    public SceneContextProvider sceneContextProvider;

    [Header("Vision Mode References")]
    public ObserveActLoop observeActLoop;
    public VisionGeminiService visionGeminiService;

    string inputText = "";
    string statusText = "Ready - API Key를 설정한 후 명령을 입력하세요.";
    bool isProcessing = false;

    // API Key UI
    string apiKeyInput = "";
    bool showApiKey = false;
    bool apiKeySet = false;

    // Mode toggle
    bool useVisionMode = true;

    // GUI
    GUIStyle inputStyle;
    GUIStyle buttonStyle;
    GUIStyle statusStyle;
    GUIStyle apiKeyStyle;
    GUIStyle smallButtonStyle;
    GUIStyle headerStyle;
    GUIStyle modeToggleOnStyle;
    GUIStyle modeToggleOffStyle;

    void Start()
    {
        // Auto-find references if not set in Inspector
        if (geminiService == null) geminiService = FindObjectOfType<GeminiService>();
        if (actionSequencer == null) actionSequencer = FindObjectOfType<ActionSequencer>();
        if (sceneContextProvider == null) sceneContextProvider = FindObjectOfType<SceneContextProvider>();
        if (observeActLoop == null) observeActLoop = FindObjectOfType<ObserveActLoop>();
        if (visionGeminiService == null) visionGeminiService = FindObjectOfType<VisionGeminiService>();

        // Load saved API key
        apiKeyInput = PlayerPrefs.GetString("GeminiApiKey", "");
        if (!string.IsNullOrEmpty(apiKeyInput))
        {
            ApplyApiKey(apiKeyInput);
            apiKeySet = true;
            statusText = "Ready - 명령을 입력하세요.";
        }

        // Subscribe text mode events
        if (actionSequencer != null)
        {
            actionSequencer.OnStatusChanged += (s) => statusText = s;
            actionSequencer.OnSequenceComplete += () => { statusText = "완료!"; isProcessing = false; };
            actionSequencer.OnSequenceError += (e) => { statusText = $"오류: {e}"; isProcessing = false; };
        }

        // Subscribe vision mode events
        if (observeActLoop != null)
        {
            observeActLoop.OnStatusChanged += (s) => statusText = s;
            observeActLoop.OnCompleted += () => { isProcessing = false; };
            observeActLoop.OnError += (e) => { statusText = $"오류: {e}"; isProcessing = false; };
        }
    }

    void ApplyApiKey(string key)
    {
        if (geminiService != null)
            geminiService.apiKey = key;
        if (visionGeminiService != null)
            visionGeminiService.apiKey = key;
    }

    void OnGUI()
    {
        if (inputStyle == null) InitStyles();

        DrawApiKeyPanel();
        DrawModeToggle();
        DrawCommandPanel();
    }

    void DrawApiKeyPanel()
    {
        // Toggle button (top right)
        float toggleW = 120;
        Rect toggleRect = new Rect(Screen.width - toggleW - 20, 20, toggleW, 30);
        string toggleText = showApiKey ? "API Key 닫기" : "API Key 설정";
        if (GUI.Button(toggleRect, toggleText, smallButtonStyle))
        {
            showApiKey = !showApiKey;
        }

        if (!showApiKey) return;

        // API Key panel
        float panelW = 450;
        float panelX = Screen.width - panelW - 20;
        float panelY = 55;

        // Background
        GUI.Box(new Rect(panelX - 10, panelY - 5, panelW + 20, 80), "");

        GUI.Label(new Rect(panelX, panelY, panelW, 25), "Gemini API Key:", headerStyle);

        // Password field
        Rect keyRect = new Rect(panelX, panelY + 25, panelW - 80, 30);
        apiKeyInput = GUI.PasswordField(keyRect, apiKeyInput, '*', 100, inputStyle);

        // Save button
        Rect saveRect = new Rect(panelX + panelW - 70, panelY + 25, 70, 30);
        if (GUI.Button(saveRect, "저장", smallButtonStyle))
        {
            ApplyApiKey(apiKeyInput);
            PlayerPrefs.SetString("GeminiApiKey", apiKeyInput);
            PlayerPrefs.Save();
            apiKeySet = true;
            showApiKey = false;
            statusText = "API Key 저장 완료! 명령을 입력하세요.";
        }
    }

    void DrawModeToggle()
    {
        // Mode toggle button (top left)
        float btnW = 160;
        Rect modeRect = new Rect(20, 20, btnW, 30);

        GUIStyle currentStyle = useVisionMode ? modeToggleOnStyle : modeToggleOffStyle;
        string modeLabel = useVisionMode ? "Vision Mode (ON)" : "Text Mode (OFF)";

        if (GUI.Button(modeRect, modeLabel, currentStyle))
        {
            useVisionMode = !useVisionMode;
            statusText = useVisionMode ? "Vision 모드 활성화" : "Text 모드 활성화";
        }
    }

    void DrawCommandPanel()
    {
        float y = Screen.height - 75;
        float inputW = Screen.width - 170;

        // Command input field
        Rect inputRect = new Rect(20, y, inputW, 35);
        GUI.SetNextControlName("CommandInput");
        inputText = GUI.TextField(inputRect, inputText, inputStyle);

        // Send button
        Rect sendRect = new Rect(inputW + 30, y, 120, 35);
        bool isBusy = isProcessing ||
                      (actionSequencer != null && actionSequencer.IsExecuting) ||
                      (observeActLoop != null && observeActLoop.IsRunning);
        bool canSend = !isBusy && apiKeySet && inputText.Trim().Length > 0;

        GUI.enabled = canSend;
        bool sendClicked = GUI.Button(sendRect, "전송 (Enter)", buttonStyle);
        GUI.enabled = true;

        // Enter key to send
        if (canSend && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
            GUI.GetNameOfFocusedControl() == "CommandInput")
        {
            sendClicked = true;
            Event.current.Use();
        }

        if (sendClicked)
        {
            SendCommand();
        }

        // Status bar
        Rect statusRect = new Rect(20, y + 40, Screen.width - 40, 25);
        GUI.Label(statusRect, statusText, statusStyle);

        // Cancel button (visible during execution)
        if (isBusy)
        {
            Rect cancelRect = new Rect(Screen.width - 100, y + 40, 80, 25);
            if (GUI.Button(cancelRect, "중지", smallButtonStyle))
            {
                if (useVisionMode && observeActLoop != null)
                    observeActLoop.StopLoop();
                if (!useVisionMode && actionSequencer != null)
                    actionSequencer.CancelExecution();

                isProcessing = false;
                statusText = "중지됨";
            }
        }
    }

    void SendCommand()
    {
        string command = inputText.Trim();
        inputText = "";
        isProcessing = true;

        if (useVisionMode)
        {
            SendVisionCommand(command);
        }
        else
        {
            SendTextCommand(command);
        }
    }

    void SendVisionCommand(string command)
    {
        statusText = "비전 모드: 관찰-행동 루프 시작...";
        Debug.Log($"[CommandInputUI] Vision command: {command}");

        if (observeActLoop == null)
        {
            statusText = "오류: ObserveActLoop이 연결되지 않았습니다.";
            isProcessing = false;
            return;
        }

        observeActLoop.StartLoop(command);
    }

    void SendTextCommand(string command)
    {
        statusText = "Gemini에 전송 중...";

        string context = sceneContextProvider.GetSceneContext();
        Debug.Log($"[CommandInputUI] Text command: {command}\n{context}");

        geminiService.SendCommand(command, context,
            onSuccess: (response) =>
            {
                if (response.understood && response.actions != null && response.actions.Count > 0)
                {
                    statusText = $"의도: {response.intent} ({response.actions.Count}개 액션)";
                    actionSequencer.ExecuteActions(response.actions);
                }
                else
                {
                    statusText = response.error ?? "명령을 이해할 수 없습니다.";
                    isProcessing = false;
                }
            },
            onError: (error) =>
            {
                statusText = $"오류: {error}";
                isProcessing = false;
            }
        );
    }

    void InitStyles()
    {
        inputStyle = new GUIStyle(GUI.skin.textField);
        inputStyle.fontSize = 18;
        inputStyle.padding = new RectOffset(8, 8, 6, 6);

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

        apiKeyStyle = new GUIStyle(GUI.skin.textField);
        apiKeyStyle.fontSize = 14;

        // Vision mode ON style (green-ish)
        modeToggleOnStyle = new GUIStyle(GUI.skin.button);
        modeToggleOnStyle.fontSize = 13;
        modeToggleOnStyle.fontStyle = FontStyle.Bold;
        modeToggleOnStyle.normal.textColor = Color.green;

        // Vision mode OFF style (default)
        modeToggleOffStyle = new GUIStyle(GUI.skin.button);
        modeToggleOffStyle.fontSize = 13;
        modeToggleOffStyle.normal.textColor = Color.white;
    }
}
