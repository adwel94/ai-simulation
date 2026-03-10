using System;
using System.Collections;
using UnityEngine;
using Newtonsoft.Json.Linq;

/// <summary>
/// 독립 액션 실행기 (fire-and-forget + 폴링 아키텍처).
/// 코루틴 없이 Update() 상태 머신으로 액션 완료를 감지.
/// Python이 /status 폴링 → /capture로 스크린샷 획득.
/// </summary>
public class ActionExecutor : MonoBehaviour
{
    [Header("References")]
    public ClawMachineController clawController;
    public GripperDemoController gripperDemo;
    public PincherController pincherController;
    public ScreenshotCapture screenshotCapture;
    public BallSpawner ballSpawner;
    public FruitSpawner fruitSpawner;

    [Header("Settings")]
    public int maxSteps = 50;

    // Camera orbit state
    float orbitAngle = 0f;
    float orbitRadius;
    float orbitHeight;

    // Episode state
    int currentStep = 0;
    bool episodeActive = false;

    // Action state machine
    volatile bool actionInProgress = false;
    float actionStartTime;
    string currentActionType = "";
    float currentActionDuration;

    // Initial positions for reset
    Vector3 initialClawPosition;
    Quaternion initialClawRotation;
    Vector3 initialCameraPosition;
    Quaternion initialCameraRotation;

    // Cached references
    BallPickGameInput playerInput;
    BallPickGameController gameController;

    /// <summary>
    /// 활성 스포너 (FruitSpawner 우선, 없으면 BallSpawner).
    /// </summary>
    IObjectSpawner Spawner => (IObjectSpawner)fruitSpawner ?? (IObjectSpawner)ballSpawner;

    public int CurrentStep => currentStep;
    public int MaxSteps => maxSteps;
    public float OrbitAngle => orbitAngle;
    public bool EpisodeActive => episodeActive;
    public bool ActionInProgress => actionInProgress;

    /// <summary>
    /// HTTP 스레드에서 호출. 큐 처리 전 폴링 race condition 방지.
    /// </summary>
    public void MarkActionStarted() => actionInProgress = true;

    void Awake()
    {
        if (clawController == null) clawController = FindObjectOfType<ClawMachineController>();
        if (gripperDemo == null) gripperDemo = FindObjectOfType<GripperDemoController>();
        if (pincherController == null) pincherController = FindObjectOfType<PincherController>();
        if (screenshotCapture == null) screenshotCapture = FindObjectOfType<ScreenshotCapture>();
        if (ballSpawner == null) ballSpawner = FindObjectOfType<BallSpawner>();
        if (fruitSpawner == null) fruitSpawner = FindObjectOfType<FruitSpawner>();
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

            Vector3 camPos = mainCam.transform.position;
            orbitHeight = camPos.y;
            orbitRadius = Mathf.Sqrt(camPos.x * camPos.x + camPos.z * camPos.z);
            orbitAngle = Mathf.Atan2(camPos.x, camPos.z) * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// Update() 상태 머신: 액션 완료 감지.
    /// 코루틴 대신 매 프레임 체크하여 완료 시 actionInProgress = false.
    /// </summary>
    void Update()
    {
        if (!actionInProgress) return;

        float elapsed = Time.time - actionStartTime;
        bool complete = false;

        switch (currentActionType)
        {
            case "move":
                if (elapsed >= Mathf.Clamp(currentActionDuration, 0.1f, 5f))
                {
                    gameController.SetMoveDirection(Vector2.zero);
                    complete = true;
                }
                break;

            case "lower":
            case "raise":
                if (gripperDemo.moveState == BigHandState.Fixed || elapsed > 5f)
                {
                    gripperDemo.moveState = BigHandState.Fixed;
                    complete = true;
                }
                break;

            case "grip":
                float currentGrip = pincherController.CurrentGrip();
                bool reachedTarget;
                if (pincherController.gripState == GripState.Closing)
                    reachedTarget = currentGrip >= 0.19f;
                else if (pincherController.gripState == GripState.Opening)
                    reachedTarget = currentGrip <= 0.01f;
                else
                    reachedTarget = true;

                if (reachedTarget || elapsed > 3f)
                {
                    pincherController.gripState = GripState.Fixed;
                    complete = true;
                }
                break;

            case "wait":
                if (elapsed >= Mathf.Clamp(currentActionDuration, 0.1f, 3f))
                    complete = true;
                break;

            case "camera":
            case "done":
            case "error":
                complete = true; // 즉시 완료
                break;
        }

        if (complete)
        {
            currentStep++;
            actionInProgress = false;

            bool maxStepsReached = currentStep >= maxSteps;
            if (maxStepsReached)
                episodeActive = false;

            Debug.Log($"<color=cyan>[ActionExecutor]</color> Action COMPLETE: {currentActionType} ({elapsed:F1}s) step={currentStep}");
        }
    }

    /// <summary>
    /// Fire-and-forget 액션 실행. 즉시 반환, Update()에서 완료 감지.
    /// </summary>
    public void ExecuteActionDirect(ClawAction action)
    {
        actionInProgress = true;
        actionStartTime = Time.time;
        currentActionType = action.type;
        currentActionDuration = action.duration;

        Debug.Log($"<color=cyan>[ActionExecutor]</color> Action START: {action.type}");

        switch (action.type)
        {
            case "move":
                float x = 0, z = 0;
                switch (action.direction)
                {
                    case "left": x = -1; break;
                    case "right": x = 1; break;
                    case "forward": z = 1; break;
                    case "backward": z = -1; break;
                }
                gameController.SetMoveDirection(new Vector2(x, z));
                break;

            case "lower":
                gameController.LowerClaw();
                break;

            case "raise":
                gameController.RaiseClaw();
                break;

            case "grip":
                if (action.state == "open")
                    pincherController.ResetGripToOpen();
                else
                    gameController.SetGrip("close");
                break;

            case "camera":
                float delta = action.direction == "left" ? -90f : 90f;
                gameController.RotateCamera(delta);
                orbitAngle = gameController.OrbitAngle;
                break;

            case "done":
                episodeActive = false;
                clawController.isAIControlled = false;
                break;

            case "error":
                episodeActive = false;
                clawController.isAIControlled = false;
                break;

            case "wait":
                // Update()에서 시간 경과로 완료
                break;

            default:
                Debug.LogWarning($"<color=yellow>[ActionExecutor]</color> Unknown action: {action.type}");
                actionInProgress = false;
                break;
        }
    }

    /// <summary>
    /// Reset episode. 코루틴 유지 (물리 settle 대기 필요).
    /// </summary>
    public void ResetEpisodeAsync(Action<JObject> onComplete)
    {
        Debug.Log("<color=cyan>[ActionExecutor]</color> ResetEpisodeAsync called");
        StartCoroutine(ResetEpisodeCoroutine(onComplete));
    }

    IEnumerator ResetEpisodeCoroutine(Action<JObject> onComplete)
    {
        currentStep = 0;
        episodeActive = true;
        actionInProgress = false;

        // Stop all movements
        clawController.isAIControlled = true;
        clawController.StopAIMovement();
        gripperDemo.moveState = BigHandState.Fixed;
        pincherController.gripState = GripState.Fixed;

        // Disable player input
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

        // 오브젝트 재배치
        Spawner?.SpawnRandom();

        Physics.SyncTransforms();

        // Wait for balls to land
        yield return new WaitForSeconds(1.5f);
        yield return new WaitForEndOfFrame();

        Debug.Log("<color=cyan>[ActionExecutor]</color> Episode reset complete");

        try
        {
            onComplete?.Invoke(CaptureObservation());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"<color=red>[ActionExecutor]</color> CaptureObservation failed: {e.Message}");
            onComplete?.Invoke(new JObject { ["error"] = e.Message });
        }
    }

    /// <summary>
    /// Capture current state as observation JSON.
    /// </summary>
    public JObject CaptureObservation()
    {
        string screenshot = "";
        if (screenshotCapture != null)
            screenshot = screenshotCapture.CaptureBase64();

        var obs = new JObject
        {
            ["screenshot_base64"] = screenshot ?? "",
            ["step"] = currentStep,
            ["max_steps"] = maxSteps,
            ["camera_angle"] = Math.Round(orbitAngle, 1),
            ["grip"] = Math.Round(pincherController.CurrentGrip(), 3),
            ["done"] = !episodeActive,
            ["done_reason"] = ""
        };

        return obs;
    }

    /// <summary>
    /// Get world state with coordinates for oracle agent.
    /// </summary>
    public JObject GetWorldState()
    {
        var state = new JObject();

        Vector3 clawPos = clawController.transform.position;
        state["claw"] = new JObject
        {
            ["x"] = Math.Round(clawPos.x, 4),
            ["y"] = Math.Round(clawPos.y, 4),
            ["z"] = Math.Round(clawPos.z, 4)
        };

        state["camera_angle"] = Math.Round(orbitAngle, 1);
        state["grip"] = Math.Round(pincherController.CurrentGrip(), 3);
        state["move_speed"] = clawController.moveSpeed;

        Camera cam = screenshotCapture?.targetCamera;
        if (cam != null)
        {
            Vector3 camRight = cam.transform.right;
            Vector3 camFwd = cam.transform.forward;
            state["cam_right"] = new JObject { ["x"] = Math.Round(camRight.x, 4), ["z"] = Math.Round(camRight.z, 4) };
            state["cam_forward"] = new JObject { ["x"] = Math.Round(camFwd.x, 4), ["z"] = Math.Round(camFwd.z, 4) };
        }

        var objects = new JArray();
        var spawner = Spawner;
        if (spawner != null)
        {
            foreach (var obj in spawner.SpawnedObjects)
            {
                if (obj != null)
                {
                    Vector3 p = obj.transform.position;
                    objects.Add(new JObject
                    {
                        ["name"] = obj.name,
                        ["x"] = Math.Round(p.x, 4),
                        ["y"] = Math.Round(p.y, 4),
                        ["z"] = Math.Round(p.z, 4)
                    });
                }
            }
        }
        state["objects"] = objects;
        state["balls"] = objects; // backward compatibility

        return state;
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

        if (playerInput != null) playerInput.enabled = true;
        if (gameController != null) gameController.IsAIControlled = false;
    }
}
