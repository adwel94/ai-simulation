using UnityEngine;

public class BallSpawner : MonoBehaviour
{
    [Header("Ball Settings")]
    public float ballRadius = 0.02f;
    public float ballMass = 0.1f;

    // 3 color options
    readonly Color[] ballColors = { Color.red, Color.blue, Color.green };
    readonly string[] colorNames = { "빨강", "파랑", "초록" };
    int selectedColorIndex = 0;

    bool isPlacingMode = false;
    bool guiClicked = false;
    GameObject previewBall;
    Camera mainCamera;

    Rect buttonRect;
    Rect[] colorButtonRects;
    Rect guiAreaRect; // combined area for click blocking

    GUIStyle buttonStyle;
    GUIStyle labelStyle;
    GUIStyle colorButtonStyle;
    GUIStyle selectedColorButtonStyle;
    Texture2D[] colorTextures;
    Texture2D[] selectedColorTextures;

    void Start()
    {
        mainCamera = Camera.main;
        buttonRect = new Rect(20, 20, 200, 50);

        // Color selection button positions
        colorButtonRects = new Rect[3];
        for (int i = 0; i < 3; i++)
        {
            colorButtonRects[i] = new Rect(20 + i * 70, 80, 60, 35);
        }

        // GUI area for click blocking (covers button + color buttons + label)
        guiAreaRect = new Rect(0, 0, 300, 160);

        // Pre-create color textures
        colorTextures = new Texture2D[3];
        selectedColorTextures = new Texture2D[3];
        for (int i = 0; i < 3; i++)
        {
            colorTextures[i] = MakeTex(2, 2, ballColors[i] * 0.8f);
            selectedColorTextures[i] = MakeTex(2, 2, ballColors[i]);
        }
    }

    Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    void OnGUI()
    {
        // Create styles once
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 20;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.yellow;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 16;
            labelStyle.normal.textColor = Color.yellow;

            colorButtonStyle = new GUIStyle(GUI.skin.button);
            colorButtonStyle.fontSize = 14;
            colorButtonStyle.fontStyle = FontStyle.Bold;
            colorButtonStyle.normal.textColor = Color.white;
            colorButtonStyle.hover.textColor = Color.white;

            selectedColorButtonStyle = new GUIStyle(GUI.skin.button);
            selectedColorButtonStyle.fontSize = 14;
            selectedColorButtonStyle.fontStyle = FontStyle.Bold;
            selectedColorButtonStyle.normal.textColor = Color.white;
            selectedColorButtonStyle.hover.textColor = Color.white;
        }

        // Main spawn button
        string btnText = isPlacingMode ? "배치 취소" : "공 생성하기";
        if (GUI.Button(buttonRect, btnText, buttonStyle))
        {
            guiClicked = true;
            if (isPlacingMode)
                CancelPlacing();
            else
                EnterPlacingMode();
        }

        // Color selection buttons
        for (int i = 0; i < 3; i++)
        {
            bool isSelected = (i == selectedColorIndex);
            GUIStyle style = new GUIStyle(GUI.skin.button);
            style.fontSize = 14;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.normal.background = isSelected ? selectedColorTextures[i] : colorTextures[i];
            style.hover.background = selectedColorTextures[i];
            style.active.background = selectedColorTextures[i];

            // Draw selection border
            if (isSelected)
            {
                Rect borderRect = new Rect(
                    colorButtonRects[i].x - 2,
                    colorButtonRects[i].y - 2,
                    colorButtonRects[i].width + 4,
                    colorButtonRects[i].height + 4
                );
                GUI.DrawTexture(borderRect, Texture2D.whiteTexture);
            }

            if (GUI.Button(colorButtonRects[i], colorNames[i], style))
            {
                guiClicked = true;
                selectedColorIndex = i;
                UpdatePreviewColor();
            }
        }

        // Show instruction when in placing mode
        if (isPlacingMode)
        {
            GUI.Label(new Rect(20, 125, 400, 30), "마우스 클릭으로 공을 배치하세요 (ESC: 취소)", labelStyle);
        }
    }

    void Update()
    {
        if (!isPlacingMode) return;

        // Cancel with Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelPlacing();
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100f))
        {
            Vector3 placePos = hit.point + hit.normal * ballRadius;

            if (previewBall != null)
            {
                previewBall.transform.position = placePos;
                previewBall.SetActive(true);
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (guiClicked)
                {
                    guiClicked = false;
                    return;
                }

                // Check if mouse is over any GUI area
                Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (guiAreaRect.Contains(mousePos))
                {
                    return;
                }

                PlaceBall(placePos);
            }
        }
        else
        {
            if (previewBall != null)
            {
                previewBall.SetActive(false);
            }
        }

        guiClicked = false;
    }

    void EnterPlacingMode()
    {
        isPlacingMode = true;
        previewBall = CreateBallObject(true);
        previewBall.SetActive(false);
    }

    void CancelPlacing()
    {
        isPlacingMode = false;

        if (previewBall != null)
        {
            Destroy(previewBall);
            previewBall = null;
        }
    }

    void PlaceBall(Vector3 position)
    {
        GameObject ball = CreateBallObject(false);
        ball.transform.position = position;
        CancelPlacing();
    }

    void UpdatePreviewColor()
    {
        if (previewBall != null)
        {
            Renderer renderer = previewBall.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color c = ballColors[selectedColorIndex];
                renderer.material.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.5f));
            }
        }
    }

    Color GetSelectedColor()
    {
        return ballColors[selectedColorIndex];
    }

    GameObject CreateBallObject(bool isPreview)
    {
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.transform.localScale = Vector3.one * ballRadius * 2f;

        Color color = GetSelectedColor();
        Renderer renderer = ball.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", isPreview ? new Color(color.r, color.g, color.b, 0.5f) : color);

        if (isPreview)
        {
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_Blend", 0);   // Alpha
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;

            Destroy(ball.GetComponent<Collider>());
            ball.name = "BallPreview";
        }
        else
        {
            Rigidbody rb = ball.AddComponent<Rigidbody>();
            rb.mass = ballMass;
            rb.useGravity = true;
            ball.name = "Ball_" + colorNames[selectedColorIndex];
        }

        renderer.material = mat;
        return ball;
    }
}
