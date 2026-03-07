using UnityEngine;

/// <summary>
/// OnGUI-based UI for the Ball Pick game.
/// Shows controls guide for player.
/// </summary>
public class BallPickGameUI : MonoBehaviour
{
    public BallPickGameController gameController;

    [Header("Controls Panel")]
    public Vector2 controlsPanelPos = new Vector2(10, 10);
    public Vector2 controlsPanelSize = new Vector2(220, 300);

    GUIStyle boxStyle;
    GUIStyle headerStyle;
    bool stylesInitialized;

    void Awake()
    {
        if (gameController == null) gameController = FindObjectOfType<BallPickGameController>();
    }

    void OnGUI()
    {
        InitStyles();
        DrawControlsPanel();
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
        GUILayout.Label("Q : 올리기 (자동)  /  E : 내리기 (자동)");
        GUILayout.Label("Z : 집게 열기  /  X : 집게 닫기");
        GUILayout.Label("[  /  ] : 카메라 90도 회전");
        GUILayout.Label("R : 정지");

        GUILayout.Space(8);

        if (gameController != null)
        {
            GUILayout.Label($"카메라 각도: {gameController.OrbitAngle:F0}");
        }

        GUILayout.EndArea();
    }

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
