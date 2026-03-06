using UnityEngine;

public class ClawMachineUI : MonoBehaviour
{
    [Header("Server References")]
    public SimulationServer simulationServer;

    GUIStyle helpStyle;
    GUIStyle buttonStyle;
    GUIStyle statusStyle;
    GUIStyle headerStyle;
    GUIStyle toggleStyle;
    GUIStyle serverStatusStyle;

    string serverStatusText = "";

    void Start()
    {
        if (simulationServer == null) simulationServer = FindObjectOfType<SimulationServer>();

        if (simulationServer != null)
        {
            simulationServer.OnStatusChanged += (s) => serverStatusText = s;
            simulationServer.OnClientConnected += (ip) => serverStatusText = $"Python 연결됨 ({ip})";
        }
    }

    void OnGUI()
    {
        if (helpStyle == null) InitStyles();

        DrawServerControls();
        DrawHelpText();
    }

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

            float infoX = x + btnW + 15;
            int port = simulationServer != null ? simulationServer.port : 8765;
            int requests = simulationServer != null ? simulationServer.TotalRequests : 0;
            GUI.Label(new Rect(infoX, y, 400, btnH), $"localhost:{port}  |  요청: {requests}건", serverStatusStyle);
        }

        if (!string.IsNullOrEmpty(serverStatusText))
        {
            Rect statusRect = new Rect(x, y + btnH + 5, Screen.width - 40, 25);
            GUI.Label(statusRect, serverStatusText, statusStyle);
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

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 14;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = Color.white;

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
