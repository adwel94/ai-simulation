using UnityEngine;
using Newtonsoft.Json.Linq;

public class ClawMachineDebugUI : MonoBehaviour
{
    [Header("References")]
    public ClawMachineAgent clawMachineAgent;
    public VisionGeminiService visionGeminiService;

    bool showPanel = false;
    Vector2 historyScroll;
    Vector2 responseScroll;

    GUIStyle headerStyle;
    GUIStyle labelStyle;
    GUIStyle userMsgStyle;
    GUIStyle modelMsgStyle;
    GUIStyle boxStyle;
    GUIStyle wrappedStyle;

    void Start()
    {
        if (clawMachineAgent == null) clawMachineAgent = FindObjectOfType<ClawMachineAgent>();
        if (visionGeminiService == null) visionGeminiService = FindObjectOfType<VisionGeminiService>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
            showPanel = !showPanel;
    }

    void OnGUI()
    {
        // Toggle hint
        GUI.Label(new Rect(10, 10, 200, 20), showPanel ? "[Tab] 디버그 닫기" : "[Tab] 디버그 열기");

        if (!showPanel) return;
        if (headerStyle == null) InitStyles();

        float panelX = 10;
        float panelY = 35;
        float panelW = Screen.width - 20;
        float panelH = Screen.height - 80;

        GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");

        // Header bar
        DrawHeader(panelX, panelY, panelW);

        float contentY = panelY + 35;
        float contentH = panelH - 35;

        // Left column: screenshots + sent/response text
        float leftW = panelW * 0.5f;
        DrawLeftColumn(panelX + 5, contentY, leftW - 10, contentH - 5);

        // Right column: conversation history
        float rightX = panelX + leftW + 5;
        float rightW = panelW * 0.5f - 15;
        DrawHistoryColumn(rightX, contentY, rightW, contentH - 5);
    }

    void DrawHeader(float x, float y, float w)
    {
        if (clawMachineAgent == null) return;

        string stepInfo = clawMachineAgent.IsRunning
            ? $"스텝 {clawMachineAgent.CurrentStep}/{clawMachineAgent.MaxIterations}"
            : "대기 중";
        string timeInfo = clawMachineAgent.IsRunning
            ? $"{clawMachineAgent.ElapsedTime:F1}초"
            : "--";
        string cmd = clawMachineAgent.UserCommand ?? "";

        string header = $"  {stepInfo}  |  경과: {timeInfo}  |  명령: {cmd}";
        GUI.Label(new Rect(x + 5, y + 5, w - 10, 25), header, headerStyle);
    }

    void DrawLeftColumn(float x, float y, float w, float h)
    {
        if (clawMachineAgent == null) return;

        float curY = y;

        // Screenshot thumbnails
        float thumbSize = 150;
        GUI.Label(new Rect(x, curY, 80, 20), "측면 뷰:", labelStyle);
        GUI.Label(new Rect(x + thumbSize + 10, curY, 80, 20), "상단 뷰:", labelStyle);
        curY += 22;

        Texture2D side = clawMachineAgent.DebugSideView;
        Texture2D top = clawMachineAgent.DebugTopView;

        Rect sideRect = new Rect(x, curY, thumbSize, thumbSize);
        if (side != null)
            GUI.DrawTexture(sideRect, side, ScaleMode.ScaleToFit);
        else
            GUI.Box(sideRect, "이미지 없음");

        Rect topRect = new Rect(x + thumbSize + 10, curY, thumbSize, thumbSize);
        if (top != null)
            GUI.DrawTexture(topRect, top, ScaleMode.ScaleToFit);
        else
            GUI.Box(topRect, "이미지 없음");

        curY += thumbSize + 10;

        // Sent text
        GUI.Label(new Rect(x, curY, w, 20), "보낸 텍스트:", labelStyle);
        curY += 22;
        string sentText = clawMachineAgent.DebugSentText ?? "(없음)";
        float sentH = 60;
        GUI.TextArea(new Rect(x, curY, w, sentH), sentText, wrappedStyle);
        curY += sentH + 10;

        // Action info
        string actionType = clawMachineAgent.DebugActionType ?? "--";
        string reasoning = clawMachineAgent.DebugReasoning ?? "--";
        GUI.Label(new Rect(x, curY, w, 20), $"액션: {actionType}", labelStyle);
        curY += 22;
        GUI.Label(new Rect(x, curY, w, 20), $"판단 근거: {reasoning}", wrappedStyle);
        curY += 30;

        // Raw response (scrollable)
        GUI.Label(new Rect(x, curY, w, 20), "원본 응답:", labelStyle);
        curY += 22;
        string rawResp = clawMachineAgent.DebugRawResponse ?? "(없음)";
        // Pretty-print JSON if possible
        string displayResp = TryFormatJson(rawResp);
        float respH = h - (curY - y) - 5;
        if (respH < 50) respH = 50;
        responseScroll = GUI.BeginScrollView(new Rect(x, curY, w, respH), responseScroll,
            new Rect(0, 0, w - 20, Mathf.Max(respH, displayResp.Length * 0.5f)));
        GUI.TextArea(new Rect(0, 0, w - 20, Mathf.Max(respH, CalcTextHeight(displayResp, w - 20))),
            displayResp, wrappedStyle);
        GUI.EndScrollView();
    }

    void DrawHistoryColumn(float x, float y, float w, float h)
    {
        GUI.Label(new Rect(x, y, w, 20), "대화 히스토리:", labelStyle);

        if (visionGeminiService == null)
        {
            GUI.Label(new Rect(x, y + 25, w, 20), "(서비스 없음)", wrappedStyle);
            return;
        }

        var history = visionGeminiService.ConversationHistory;
        if (history == null || history.Count == 0)
        {
            GUI.Label(new Rect(x, y + 25, w, 20), "(비어 있음)", wrappedStyle);
            return;
        }

        // Build display text
        float scrollY = y + 22;
        float scrollH = h - 22;

        // Estimate total content height
        float totalHeight = 0;
        foreach (var msg in history)
            totalHeight += EstimateMessageHeight(msg, w - 30) + 10;

        historyScroll = GUI.BeginScrollView(new Rect(x, scrollY, w, scrollH), historyScroll,
            new Rect(0, 0, w - 20, Mathf.Max(scrollH, totalHeight)));

        float curY = 0;
        for (int i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            string role = msg["role"]?.ToString() ?? "?";
            bool isUser = role == "user";

            GUIStyle msgStyle = isUser ? userMsgStyle : modelMsgStyle;
            string roleLabel = isUser ? "[사용자]" : "[모델]";

            // Extract text parts only (skip images)
            string text = ExtractTextFromMessage(msg);
            if (text.Length > 500) text = text.Substring(0, 500) + "...";

            string display = $"{roleLabel} {text}";
            float msgH = CalcTextHeight(display, w - 30);
            msgH = Mathf.Max(msgH, 25);

            GUI.Label(new Rect(5, curY, w - 30, msgH), display, msgStyle);
            curY += msgH + 8;
        }

        GUI.EndScrollView();
    }

    string ExtractTextFromMessage(JObject msg)
    {
        JArray parts = msg["parts"] as JArray;
        if (parts == null) return "";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (part["text"] != null)
            {
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(part["text"].ToString());
            }
            else if (part["inline_data"] != null)
            {
                if (sb.Length > 0) sb.Append(" ");
                sb.Append("[이미지]");
            }
        }
        return sb.ToString();
    }

    string TryFormatJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        try
        {
            var obj = JToken.Parse(json);
            return obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        catch
        {
            return json;
        }
    }

    float CalcTextHeight(string text, float width)
    {
        if (string.IsNullOrEmpty(text)) return 20;
        // Rough estimate: ~7px per char, lines wrap at width
        float charsPerLine = width / 7f;
        if (charsPerLine < 1) charsPerLine = 1;
        int lines = Mathf.CeilToInt(text.Length / charsPerLine);
        lines = Mathf.Max(lines, 1);
        // Account for newlines
        foreach (char c in text)
            if (c == '\n') lines++;
        return lines * 18f;
    }

    float EstimateMessageHeight(JObject msg, float width)
    {
        string text = ExtractTextFromMessage(msg);
        if (text.Length > 500) text = text.Substring(0, 500);
        return CalcTextHeight(text, width) + 5;
    }

    void InitStyles()
    {
        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 15;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 13;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.normal.textColor = new Color(0.8f, 0.9f, 1f);

        userMsgStyle = new GUIStyle(GUI.skin.label);
        userMsgStyle.fontSize = 12;
        userMsgStyle.wordWrap = true;
        userMsgStyle.normal.textColor = new Color(0.6f, 0.9f, 1f);

        modelMsgStyle = new GUIStyle(GUI.skin.label);
        modelMsgStyle.fontSize = 12;
        modelMsgStyle.wordWrap = true;
        modelMsgStyle.normal.textColor = new Color(0.6f, 1f, 0.7f);

        boxStyle = new GUIStyle(GUI.skin.box);

        wrappedStyle = new GUIStyle(GUI.skin.label);
        wrappedStyle.fontSize = 12;
        wrappedStyle.wordWrap = true;
        wrappedStyle.normal.textColor = Color.white;
    }
}
