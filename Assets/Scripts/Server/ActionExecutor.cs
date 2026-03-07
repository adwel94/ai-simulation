using System;
using System.Collections;
using UnityEngine;
using Newtonsoft.Json.Linq;

/// <summary>
/// 독립 액션 실행기. ClawMachineAgent에서 추출된 물리 액션 로직.
/// SimulationServer가 Python에서 받은 액션을 이 컴포넌트로 실행.
/// 기존 내부 AI 모드에서도 재사용 가능.
/// </summary>
public class ActionExecutor : MonoBehaviour
{
    [Header("References")]
    public ClawMachineController clawController;
    public GripperDemoController gripperDemo;
    public PincherController pincherController;
    public ScreenshotCapture screenshotCapture;
    public BallSpawner ballSpawner;

    [Header("Settings")]
    public int maxSteps = 50;
    public float settleDelay = 0.5f;

    // Camera orbit state
    float orbitAngle = 0f;
    float orbitRadius;
    float orbitHeight;

    // Episode state
    int currentStep = 0;
    bool episodeActive = false;
    bool actionInProgress = false;

    // Initial positions for reset
    Vector3 initialClawPosition;
    Quaternion initialClawRotation;
    Vector3 initialCameraPosition;
    Quaternion initialCameraRotation;

    // Cached references for input control
    BallPickGameInput playerInput;
    BallPickGameController gameController;

    public int CurrentStep => currentStep;
    public int MaxSteps => maxSteps;
    public float OrbitAngle => orbitAngle;
    public bool EpisodeActive => episodeActive;
    public bool ActionInProgress => actionInProgress;

    void Awake()
    {
        if (clawController == null) clawController = FindObjectOfType<ClawMachineController>();
        if (gripperDemo == null) gripperDemo = FindObjectOfType<GripperDemoController>();
        if (pincherController == null) pincherController = FindObjectOfType<PincherController>();
        if (screenshotCapture == null) screenshotCapture = FindObjectOfType<ScreenshotCapture>();
        if (ballSpawner == null) ballSpawner = FindObjectOfType<BallSpawner>();
        playerInput = FindObjectOfType<BallPickGameInput>();
        gameController = FindObjectOfType<BallPickGameController>();
    }

    void Start()
    {
        // Save initial positions for reset
        if (clawController != null)
        {
            initialClawPosition = clawController.transform.position;
            initialClawRotation = clawController.transform.rotation;
        }

        if (screenshotCapture != null && screenshotCapture.targetCamera != null)
        {
            Camera mainCam = screenshotCapture.targetCamera;
            initialCameraPosition = mainCam.transform.position;
            initialCameraRotation = mainCam.transform.rotation;

            // Calculate orbit parameters from initial camera position
            Vector3 camPos = mainCam.transform.position;
            orbitHeight = camPos.y;
            orbitRadius = Mathf.Sqrt(camPos.x * camPos.x + camPos.z * camPos.z);
            orbitAngle = Mathf.Atan2(camPos.x, camPos.z) * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// Reset episode to initial state asynchronously.
    /// Waits a frame for physics/rendering to settle before capturing.
    /// </summary>
    public void ResetEpisodeAsync(Action<JObject> onComplete)
    {
        StartCoroutine(ResetEpisodeCoroutine(onComplete));
    }

    IEnumerator ResetEpisodeCoroutine(Action<JObject> onComplete)
    {
        currentStep = 0;
        episodeActive = true;

        // Stop all movements
        clawController.isAIControlled = true;
        clawController.StopAIMovement();
        gripperDemo.moveState = BigHandState.Fixed;
        pincherController.gripState = GripState.Fixed;

        // Disable player input to prevent overriding AI movement
        if (playerInput != null) playerInput.enabled = false;
        if (gameController != null) gameController.IsAIControlled = true;

        // Reset claw position
        var artBody = clawController.GetComponent<ArticulationBody>();
        if (artBody != null)
            artBody.TeleportRoot(initialClawPosition, initialClawRotation);
        else
            clawController.transform.SetPositionAndRotation(initialClawPosition, initialClawRotation);

        // Reset gripper to open
        pincherController.ResetGripToOpen();

        // Reset gripper vertical position
        var gripperArt = gripperDemo.GetComponent<ArticulationBody>();
        if (gripperArt != null)
        {
            var drive = gripperArt.xDrive;
            drive.target = 0f;
            gripperArt.xDrive = drive;
        }

        // Reset camera
        if (screenshotCapture != null && screenshotCapture.targetCamera != null)
        {
            Camera mainCam = screenshotCapture.targetCamera;
            mainCam.transform.position = initialCameraPosition;
            mainCam.transform.rotation = initialCameraRotation;
            orbitAngle = Mathf.Atan2(initialCameraPosition.x, initialCameraPosition.z) * Mathf.Rad2Deg;
        }

        // 공 재배치
        if (ballSpawner != null)
            ballSpawner.SpawnRandomBalls();

        Physics.SyncTransforms();

        // Wait for balls to land on the floor before capturing
        yield return new WaitForSeconds(1.5f);
        yield return new WaitForEndOfFrame();

        Debug.Log("<color=cyan>[ActionExecutor]</color> Episode reset complete");

        try
        {
            onComplete?.Invoke(CaptureObservation());
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>[ActionExecutor]</color> CaptureObservation failed: {e.Message}");
            onComplete?.Invoke(new JObject { ["error"] = e.Message });
        }
    }

    /// <summary>
    /// Execute an action and return the resulting observation.
    /// Must be called as a coroutine. Use ExecuteActionAsync for callback pattern.
    /// </summary>
    public void ExecuteActionAsync(ClawAction action, Action<JObject> onComplete)
    {
        StartCoroutine(ExecuteActionCoroutine(action, onComplete));
    }

    IEnumerator ExecuteActionCoroutine(ClawAction action, Action<JObject> onComplete)
    {
        actionInProgress = true;

        // Handle terminal actions
        if (action.type == "done")
        {
            episodeActive = false;
            clawController.isAIControlled = false;
            actionInProgress = false;

            var obs = CaptureObservation();
            obs["done"] = true;
            obs["done_reason"] = "completed";
            onComplete?.Invoke(obs);
            yield break;
        }

        if (action.type == "error")
        {
            episodeActive = false;
            clawController.isAIControlled = false;
            actionInProgress = false;

            var obs = CaptureObservation();
            obs["done"] = true;
            obs["done_reason"] = action.reasoning ?? "error";
            onComplete?.Invoke(obs);
            yield break;
        }

        // Execute physical action
        yield return ExecutePhysicalAction(action);

        // Settle delay
        yield return new WaitForSeconds(settleDelay);

        currentStep++;

        // Check max steps
        bool maxStepsReached = currentStep >= maxSteps;
        if (maxStepsReached)
            episodeActive = false;

        // Capture observation
        yield return new WaitForEndOfFrame();

        var observation = CaptureObservation();
        if (maxStepsReached)
        {
            observation["done"] = true;
            observation["done_reason"] = "max_steps";
        }

        actionInProgress = false;
        onComplete?.Invoke(observation);
    }

    IEnumerator ExecutePhysicalAction(ClawAction action)
    {
        switch (action.type)
        {
            case "move":
                yield return ExecuteMove(action);
                break;
            case "lower":
                yield return ExecuteLower(action);
                break;
            case "raise":
                yield return ExecuteRaise(action);
                break;
            case "grip":
                yield return ExecuteGrip(action);
                break;
            case "camera":
                ExecuteCamera(action);
                yield return null;
                break;
            case "wait":
                yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 3f));
                break;
            default:
                Debug.LogWarning($"<color=yellow>[ActionExecutor]</color> Unknown action: {action.type}");
                break;
        }
    }

    IEnumerator ExecuteMove(ClawAction action)
    {
        float x = 0, z = 0;
        switch (action.direction)
        {
            case "left": x = -1; break;
            case "right": x = 1; break;
            case "forward": z = -1; break;  // 카메라 쪽으로 (화면 아래)
            case "backward": z = 1; break;  // 카메라 반대쪽 (화면 위)
        }

        // 카메라 상대 방향 → 월드 좌표 변환
        Camera cam = screenshotCapture?.targetCamera;
        if (cam != null)
        {
            Vector3 camForward = cam.transform.forward;
            Vector3 camRight = cam.transform.right;
            camForward.y = 0; camRight.y = 0;
            camForward.Normalize(); camRight.Normalize();

            float worldX = camRight.x * x + camForward.x * z;
            float worldZ = camRight.z * x + camForward.z * z;
            x = worldX;
            z = worldZ;
        }

        clawController.SetAIMoveDirection(x, z);
        yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 5f));
        clawController.StopAIMovement();
    }

    IEnumerator ExecuteLower(ClawAction action)
    {
        gripperDemo.moveState = BigHandState.MovingDown;
        float elapsed = 0f;
        while (gripperDemo.moveState != BigHandState.Fixed && elapsed < 5f)
        {
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        gripperDemo.moveState = BigHandState.Fixed;
    }

    IEnumerator ExecuteRaise(ClawAction action)
    {
        gripperDemo.moveState = BigHandState.MovingUp;
        float elapsed = 0f;
        while (gripperDemo.moveState != BigHandState.Fixed && elapsed < 5f)
        {
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        gripperDemo.moveState = BigHandState.Fixed;
    }

    IEnumerator ExecuteGrip(ClawAction action)
    {
        if (action.state == "open")
        {
            pincherController.gripState = GripState.Opening;
            float elapsed = 0f;
            while (pincherController.CurrentGrip() > 0.05f && elapsed < 3f)
            {
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            pincherController.gripState = GripState.Fixed;
        }
        else if (action.state == "close")
        {
            pincherController.gripState = GripState.Closing;
            float elapsed = 0f;
            float lastGrip = 0f;
            int stableFrames = 0;

            while (elapsed < 3f)
            {
                float currentGrip = pincherController.CurrentGrip();
                if (currentGrip >= 0.19f) break;
                if (Mathf.Abs(currentGrip - lastGrip) < 0.001f)
                    stableFrames++;
                else
                    stableFrames = 0;
                if (stableFrames > 20) break;

                lastGrip = currentGrip;
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            pincherController.gripState = GripState.Fixed;
        }
    }

    void ExecuteCamera(ClawAction action)
    {
        float deltaAngle = Mathf.Clamp(action.angle, 10f, 90f);
        if (action.direction == "left")
            orbitAngle -= deltaAngle;
        else
            orbitAngle += deltaAngle;

        Camera mainCam = screenshotCapture.targetCamera;
        if (mainCam == null) return;

        float rad = orbitAngle * Mathf.Deg2Rad;
        Vector3 newPos = new Vector3(
            Mathf.Sin(rad) * orbitRadius,
            orbitHeight,
            Mathf.Cos(rad) * orbitRadius
        );
        mainCam.transform.position = newPos;
        mainCam.transform.LookAt(Vector3.zero);

        Debug.Log($"<color=cyan>[ActionExecutor]</color> Camera orbit: {action.direction} {deltaAngle}deg -> {orbitAngle:F0}deg");
    }

    /// <summary>
    /// Capture current state as observation JSON.
    /// </summary>
    public JObject CaptureObservation()
    {
        string screenshot = "";
        if (screenshotCapture != null)
        {
            // Wait is handled externally
            screenshot = screenshotCapture.CaptureBase64();
        }

        var obs = new JObject
        {
            ["screenshot_base64"] = screenshot ?? "",
            ["step"] = currentStep,
            ["max_steps"] = maxSteps,
            ["camera_angle"] = Math.Round(orbitAngle, 1),
            ["done"] = !episodeActive,
            ["done_reason"] = ""
        };

        return obs;
    }

    /// <summary>
    /// Parse action JSON string into ClawAction.
    /// </summary>
    public static ClawAction ParseActionJson(string jsonText)
    {
        jsonText = jsonText.Trim();
        if (jsonText.StartsWith("```"))
        {
            int firstNewline = jsonText.IndexOf('\n');
            if (firstNewline >= 0)
                jsonText = jsonText.Substring(firstNewline + 1);
            if (jsonText.EndsWith("```"))
                jsonText = jsonText.Substring(0, jsonText.Length - 3);
            jsonText = jsonText.Trim();
        }

        JToken token = JToken.Parse(jsonText);
        JObject obj;
        if (token.Type == JTokenType.Array)
        {
            JArray arr = (JArray)token;
            if (arr.Count == 0)
                throw new Exception("Empty array response");
            obj = (JObject)arr[0];
        }
        else
        {
            obj = (JObject)token;
        }

        ClawAction action = new ClawAction();
        action.type = obj["type"]?.ToString() ?? "";
        action.reasoning = obj["reasoning"]?.ToString() ?? "";
        action.direction = obj["direction"]?.ToString() ?? "";
        action.state = obj["state"]?.ToString() ?? "";
        action.duration = obj["duration"]?.Value<float>() ?? 0.3f;
        action.angle = obj["angle"]?.Value<float>() ?? 45f;

        return action;
    }

    /// <summary>
    /// Clean up when server mode is disabled.
    /// </summary>
    public void Cleanup()
    {
        episodeActive = false;
        actionInProgress = false;
        clawController.isAIControlled = false;
        clawController.StopAIMovement();
        gripperDemo.moveState = BigHandState.Fixed;
        pincherController.gripState = GripState.Fixed;

        // Re-enable player input
        if (playerInput != null) playerInput.enabled = true;
        if (gameController != null) gameController.IsAIControlled = false;
    }
}
