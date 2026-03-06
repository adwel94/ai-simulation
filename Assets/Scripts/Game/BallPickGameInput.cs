using UnityEngine;

/// <summary>
/// Player keyboard input for the Ball Pick game.
/// Maps keyboard keys to BallPickGameController API calls.
/// Automatically disabled when AI is controlling.
/// </summary>
public class BallPickGameInput : MonoBehaviour
{
    public BallPickGameController gameController;

    [Header("Camera")]
    public float cameraRotateSpeed = 90f;

    void Awake()
    {
        if (gameController == null)
            gameController = FindObjectOfType<BallPickGameController>();
    }

    void Update()
    {
        if (gameController == null) return;
        if (gameController.IsAIControlled) return;

        // === Horizontal movement (WASD / Arrow keys) ===
        float h = 0, v = 0;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v = 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v = -1;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h = -1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h = 1;
        gameController.SetMoveDirection(new Vector2(h, v));

        // === Vertical movement (Q=raise, E=lower) ===
        float vertical = 0;
        if (Input.GetKey(KeyCode.Q)) vertical = 1;
        if (Input.GetKey(KeyCode.E)) vertical = -1;
        gameController.SetVerticalDirection(vertical);

        // === Grip (Z=open, X=close) ===
        if (Input.GetKeyDown(KeyCode.Z))
            gameController.SetGrip("open");
        else if (Input.GetKeyDown(KeyCode.X))
            gameController.SetGrip("close");

        // === Camera rotation ([ = left, ] = right) ===
        if (Input.GetKey(KeyCode.LeftBracket))
            gameController.RotateCamera(-cameraRotateSpeed * Time.deltaTime);
        if (Input.GetKey(KeyCode.RightBracket))
            gameController.RotateCamera(cameraRotateSpeed * Time.deltaTime);

        // === Stop all (R) ===
        if (Input.GetKeyDown(KeyCode.R))
            gameController.StopAll();
    }
}
