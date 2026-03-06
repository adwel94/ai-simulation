using UnityEngine;

/// <summary>
/// Core game controller for the Ball Pick game.
/// Wraps ClawMachineController (horizontal), GripperDemoController (vertical),
/// PincherController (grip), and camera orbit into a unified API.
/// Both player input and AI agent use this same interface.
/// </summary>
public class BallPickGameController : MonoBehaviour
{
    [Header("References")]
    public ClawMachineController clawController;
    public GripperDemoController gripperDemo;
    public PincherController pincherController;
    public Camera mainCamera;

    // Camera orbit state (computed from initial camera position)
    float orbitAngle;
    float orbitRadius;
    float orbitHeight;

    /// <summary>
    /// When true, player keyboard input is blocked (AI is controlling).
    /// </summary>
    public bool IsAIControlled { get; set; }

    public float OrbitAngle => orbitAngle;

    void Awake()
    {
        if (clawController == null) clawController = FindObjectOfType<ClawMachineController>();
        if (gripperDemo == null) gripperDemo = FindObjectOfType<GripperDemoController>();
        if (pincherController == null) pincherController = FindObjectOfType<PincherController>();
        if (mainCamera == null) mainCamera = Camera.main;

        // Compute orbit parameters from initial camera position
        if (mainCamera != null)
        {
            Vector3 camPos = mainCamera.transform.position;
            orbitHeight = camPos.y;
            orbitRadius = Mathf.Sqrt(camPos.x * camPos.x + camPos.z * camPos.z);
            orbitAngle = Mathf.Atan2(camPos.x, camPos.z) * Mathf.Rad2Deg;
        }

        // Route all movement through our API (bypass ClawMachineController's own input)
        if (clawController != null)
            clawController.isAIControlled = true;

        // Disable ALL conflicting input/UI scripts
        var artHandInput = FindObjectOfType<ArticulationHandManualInput>();
        if (artHandInput != null) artHandInput.enabled = false;

        var gripperManualInput = FindObjectOfType<GripperDemoManualInput>();
        if (gripperManualInput != null) gripperManualInput.enabled = false;

        var robotManualInput = FindObjectOfType<RobotManualInput>();
        if (robotManualInput != null) robotManualInput.enabled = false;

        // Disable old ClawMachine UI to prevent overlapping displays
        var clawUI = FindObjectOfType<ClawMachineUI>();
        if (clawUI != null) clawUI.enabled = false;

        var clawDebugUI = FindObjectOfType<ClawMachineDebugUI>();
        if (clawDebugUI != null) clawDebugUI.enabled = false;

        var clawAgent = FindObjectOfType<ClawMachineAgent>();
        if (clawAgent != null) clawAgent.enabled = false;
    }

    // ================================================================
    // Public API (shared between player keyboard input and AI agent)
    // ================================================================

    /// <summary>
    /// Camera-relative horizontal movement.
    /// x: positive=right, negative=left
    /// y: positive=forward (into screen), negative=backward
    /// </summary>
    public void SetMoveDirection(Vector2 cameraRelativeDir)
    {
        if (mainCamera == null || clawController == null) return;

        if (cameraRelativeDir.sqrMagnitude < 0.001f)
        {
            clawController.StopAIMovement();
            return;
        }

        // Project camera axes onto XZ plane for camera-relative movement
        Vector3 camForward = mainCamera.transform.forward;
        Vector3 camRight = mainCamera.transform.right;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        float worldX = camRight.x * cameraRelativeDir.x + camForward.x * cameraRelativeDir.y;
        float worldZ = camRight.z * cameraRelativeDir.x + camForward.z * cameraRelativeDir.y;

        clawController.SetAIMoveDirection(worldX, worldZ);
    }

    /// <summary>
    /// Vertical movement: positive=raise, negative=lower, zero=stop.
    /// </summary>
    public void SetVerticalDirection(float dir)
    {
        if (gripperDemo == null) return;

        if (dir > 0) gripperDemo.moveState = BigHandState.MovingUp;
        else if (dir < 0) gripperDemo.moveState = BigHandState.MovingDown;
        else gripperDemo.moveState = BigHandState.Fixed;
    }

    /// <summary>
    /// Grip control: "open", "close", or "fixed" (stop).
    /// </summary>
    public void SetGrip(string state)
    {
        if (pincherController == null) return;

        if (state == "open") pincherController.gripState = GripState.Opening;
        else if (state == "close") pincherController.gripState = GripState.Closing;
        else pincherController.gripState = GripState.Fixed;
    }

    /// <summary>
    /// Rotate the main camera around the scene center (horizontal orbit only).
    /// Positive = clockwise, negative = counter-clockwise.
    /// </summary>
    public void RotateCamera(float deltaAngle)
    {
        orbitAngle += deltaAngle;
        ApplyCameraOrbit();
    }

    /// <summary>
    /// Stop all movement and grip.
    /// </summary>
    public void StopAll()
    {
        if (clawController != null) clawController.StopAIMovement();
        if (gripperDemo != null) gripperDemo.moveState = BigHandState.Fixed;
        if (pincherController != null) pincherController.gripState = GripState.Fixed;
    }

    void ApplyCameraOrbit()
    {
        if (mainCamera == null) return;

        float rad = orbitAngle * Mathf.Deg2Rad;
        Vector3 newPos = new Vector3(
            Mathf.Sin(rad) * orbitRadius,
            orbitHeight,
            Mathf.Cos(rad) * orbitRadius
        );
        mainCamera.transform.position = newPos;
        mainCamera.transform.LookAt(Vector3.zero);
    }
}
