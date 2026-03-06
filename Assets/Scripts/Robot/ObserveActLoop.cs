using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ObserveActLoop : MonoBehaviour
{
    [Header("References")]
    public RobotController robotController;
    public PincherController pincherController;
    public ScreenshotCapture screenshotCapture;
    public VisionGeminiService visionGeminiService;

    [Header("Loop Settings")]
    public int maxIterations = 30;
    public float settleDelay = 0.3f;
    public float jointMoveTimeout = 5.0f;
    public float angleThreshold = 3.0f;
    public float rateLimitWait = 5.0f;
    public int maxRetries = 3;

    bool isRunning = false;
    Coroutine loopCoroutine;
    int currentStep;

    public bool IsRunning => isRunning;

    void Awake()
    {
        // Auto-find references if not set in Inspector
        if (robotController == null) robotController = FindObjectOfType<RobotController>();
        if (pincherController == null) pincherController = FindObjectOfType<PincherController>();
        if (screenshotCapture == null) screenshotCapture = FindObjectOfType<ScreenshotCapture>();
        if (visionGeminiService == null) visionGeminiService = FindObjectOfType<VisionGeminiService>();
    }

    // Events
    public event Action<string> OnStatusChanged;
    public event Action OnCompleted;
    public event Action<string> OnError;

    const string SYSTEM_PROMPT = @"You are a robot arm controller with visual perception. You see the scene through a camera and control a UR3 robot arm by specifying joint angles directly.

The robot has 6 joints:
- base: rotates the entire arm left/right (0 = forward along Z axis, positive = clockwise from top view)
- shoulder: lifts/lowers the upper arm (-90 = pointing up, 0 = horizontal forward, 90 = pointing down)
- elbow: bends the forearm (0 = straight, positive = bends inward)
- wrist1: tilts the end-effector (adjusts pitch)
- wrist2: rotates the end-effector (adjusts roll)
- wrist3: spins the end-effector (adjusts yaw)

The robot also has a 2-finger gripper that can open or close.

IMPORTANT STRATEGY:
- You receive the current screenshot and current joint angles at each step.
- Make ONE action per step, then observe the result.
- Use small incremental adjustments (5-15 degrees per step) to approach targets.
- Visually verify your progress after each move.
- The camera view shows the scene from a fixed perspective.

Typical pick-up sequence:
1. Rotate base to face the target ball
2. Adjust shoulder and elbow to position above the ball
3. Open gripper
4. Lower arm to ball level (adjust shoulder/elbow/wrist1)
5. Close gripper to grasp
6. Raise arm (reverse shoulder/elbow adjustments)
7. Return to home position (all joints 0)
8. Output done

You must respond with ONLY a JSON object in one of these formats:

Move joints:
{""type"": ""move_joints"", ""reasoning"": ""brief explanation of this step"", ""joints"": {""base"": 0, ""shoulder"": 0, ""elbow"": 0, ""wrist1"": 0, ""wrist2"": 0, ""wrist3"": 0}}

Control gripper:
{""type"": ""gripper"", ""reasoning"": ""brief explanation"", ""state"": ""open""}
or
{""type"": ""gripper"", ""reasoning"": ""brief explanation"", ""state"": ""close""}

Task complete:
{""type"": ""done"", ""reasoning"": ""task completed successfully""}

Task failed:
{""type"": ""error"", ""reasoning"": ""explanation of why the task cannot be completed""}

IMPORTANT: Only output valid JSON. No markdown, no explanation, no code blocks.";

    public void StartLoop(string userCommand)
    {
        if (isRunning)
        {
            OnError?.Invoke("이미 실행 중입니다.");
            return;
        }
        loopCoroutine = StartCoroutine(RunLoop(userCommand));
    }

    public void StopLoop()
    {
        if (loopCoroutine != null)
        {
            StopCoroutine(loopCoroutine);
            robotController.StopAllJointRotations();
            pincherController.gripState = GripState.Fixed;
            isRunning = false;
            OnStatusChanged?.Invoke("중지됨");
        }
    }

    IEnumerator RunLoop(string userCommand)
    {
        isRunning = true;
        currentStep = 0;
        float loopStartTime = Time.realtimeSinceStartup;

        // Setup vision service
        visionGeminiService.SetSystemPrompt(SYSTEM_PROMPT);
        visionGeminiService.ClearHistory();

        OnStatusChanged?.Invoke("비전 루프 시작...");
        Debug.Log($"<color=magenta>[ObserveActLoop]</color> ==========================================");
        Debug.Log($"<color=magenta>[ObserveActLoop]</color> 🚀 비전 루프 시작!");
        Debug.Log($"<color=magenta>[ObserveActLoop]</color> 명령어: {userCommand}");
        Debug.Log($"<color=magenta>[ObserveActLoop]</color> 최대 반복: {maxIterations}, 타임아웃/스텝: {jointMoveTimeout}초");
        Debug.Log($"<color=magenta>[ObserveActLoop]</color> ==========================================");

        for (int i = 0; i < maxIterations; i++)
        {
            currentStep = i + 1;
            float stepStartTime = Time.realtimeSinceStartup;

            Debug.Log($"<color=magenta>[ObserveActLoop]</color> ────── Step {currentStep}/{maxIterations} 시작 ──────");

            OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] 관찰 중...");

            // 1. Wait for end of frame to ensure rendering is complete
            yield return new WaitForEndOfFrame();

            // 2. Capture screenshot
            Debug.Log($"<color=magenta>[ObserveActLoop]</color> 📸 스크린샷 캡처 중...");
            string base64Image = screenshotCapture.CaptureBase64();
            if (string.IsNullOrEmpty(base64Image))
            {
                Debug.LogError($"<color=red>[ObserveActLoop]</color> ❌ 스크린샷 캡처 실패!");
                OnError?.Invoke("스크린샷 캡처 실패");
                isRunning = false;
                yield break;
            }
            Debug.Log($"<color=magenta>[ObserveActLoop]</color> 📸 스크린샷 캡처 완료: {base64Image.Length / 1024}KB");

            // 3. Build text context (joint angles + command)
            string textContext = BuildTextContext(userCommand, i == 0);
            Debug.Log($"<color=magenta>[ObserveActLoop]</color> 📝 컨텍스트:\n{textContext}");

            // 4. Send to Gemini with retry logic
            string geminiResponse = null;
            string geminiError = null;
            bool waiting = true;
            int retryCount = 0;

            while (retryCount <= maxRetries)
            {
                waiting = true;
                geminiResponse = null;
                geminiError = null;

                float apiStartTime = Time.realtimeSinceStartup;
                OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] Gemini 분석 중... (응답 대기)");
                Debug.Log($"<color=magenta>[ObserveActLoop]</color> 🤖 Gemini API 호출 중... (시도 {retryCount + 1}/{maxRetries + 1})");

                visionGeminiService.SendVisionRequest(base64Image, textContext,
                    onSuccess: (response) => { geminiResponse = response; waiting = false; },
                    onError: (error) => { geminiError = error; waiting = false; }
                );

                // Wait for response with progress logging
                float lastLogTime = Time.realtimeSinceStartup;
                while (waiting)
                {
                    float elapsed = Time.realtimeSinceStartup - apiStartTime;
                    if (Time.realtimeSinceStartup - lastLogTime >= 15f)
                    {
                        OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] Gemini 분석 중... ({elapsed:F0}초 경과)");
                        lastLogTime = Time.realtimeSinceStartup;
                    }
                    yield return null;
                }

                float apiTime = Time.realtimeSinceStartup - apiStartTime;
                Debug.Log($"<color=magenta>[ObserveActLoop]</color> 🤖 Gemini 응답 수신 ({apiTime:F1}초 소요)");

                // Handle rate limiting
                if (geminiError == "RATE_LIMIT" && retryCount < maxRetries)
                {
                    retryCount++;
                    OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] 요청 한도 초과, {rateLimitWait}초 대기 ({retryCount}/{maxRetries})...");
                    Debug.LogWarning($"<color=yellow>[ObserveActLoop]</color> ⚠️ Rate limited, {rateLimitWait}초 대기 (재시도 {retryCount}/{maxRetries})");
                    yield return new WaitForSeconds(rateLimitWait);
                    continue;
                }
                break;
            }

            // Check for errors
            if (!string.IsNullOrEmpty(geminiError))
            {
                if (geminiError == "RATE_LIMIT")
                    geminiError = "요청 한도를 초과했습니다. 잠시 후 다시 시도하세요.";

                Debug.LogError($"<color=red>[ObserveActLoop]</color> ❌ Step {currentStep} 에러: {geminiError}");
                OnError?.Invoke(geminiError);
                isRunning = false;
                yield break;
            }

            // 5. Parse and execute action
            Debug.Log($"<color=magenta>[ObserveActLoop]</color> 📋 Step {currentStep} 응답: {geminiResponse}");

            VisionAction action;
            try
            {
                action = ParseAction(geminiResponse);
                Debug.Log($"<color=magenta>[ObserveActLoop]</color> ✅ 파싱 완료: type={action.type}, reasoning={action.reasoning}");
            }
            catch (Exception e)
            {
                Debug.LogError($"<color=red>[ObserveActLoop]</color> ❌ 액션 파싱 실패: {e.Message}\nRaw: {geminiResponse}");
                OnError?.Invoke($"액션 파싱 실패: {e.Message}");
                isRunning = false;
                yield break;
            }

            // 6. Handle action
            if (action.type == "done")
            {
                float totalTime = Time.realtimeSinceStartup - loopStartTime;
                OnStatusChanged?.Invoke($"완료! ({currentStep}스텝, {totalTime:F1}초) - {action.reasoning}");
                Debug.Log($"<color=green>[ObserveActLoop]</color> 🎉 태스크 완료! Step {currentStep}, 총 {totalTime:F1}초 소요");
                Debug.Log($"<color=green>[ObserveActLoop]</color> 이유: {action.reasoning}");
                isRunning = false;
                OnCompleted?.Invoke();
                yield break;
            }

            if (action.type == "error")
            {
                Debug.LogError($"<color=red>[ObserveActLoop]</color> ❌ Gemini 판단 실패: {action.reasoning}");
                OnError?.Invoke($"Gemini 판단 실패: {action.reasoning}");
                isRunning = false;
                yield break;
            }

            // Execute the action
            OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] 실행: {action.reasoning}");

            if (action.type == "move_joints")
            {
                string jointStr = action.joints != null ? string.Join(", ", action.joints) : "null";
                Debug.Log($"<color=magenta>[ObserveActLoop]</color> 🦾 관절 이동: [{jointStr}]");
                Debug.Log($"<color=magenta>[ObserveActLoop]</color> 이유: {action.reasoning}");
                yield return ExecuteMoveJoints(action);
            }
            else if (action.type == "gripper")
            {
                Debug.Log($"<color=magenta>[ObserveActLoop]</color> ✊ 그리퍼: {action.state} - {action.reasoning}");
                yield return ExecuteGripper(action);
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[ObserveActLoop]</color> ⚠️ 알 수 없는 액션: {action.type}");
            }

            float stepTime = Time.realtimeSinceStartup - stepStartTime;
            Debug.Log($"<color=magenta>[ObserveActLoop]</color> ────── Step {currentStep} 완료 ({stepTime:F1}초) ──────");

            // 7. Wait for physics to settle
            yield return new WaitForSeconds(settleDelay);
        }

        // Max iterations reached
        float totalElapsed = Time.realtimeSinceStartup - loopStartTime;
        Debug.LogWarning($"<color=yellow>[ObserveActLoop]</color> ⚠️ 최대 반복 횟수 도달 ({maxIterations}), 총 {totalElapsed:F1}초 소요");
        OnStatusChanged?.Invoke($"최대 반복 횟수 도달 ({maxIterations})");
        OnError?.Invoke("최대 반복 횟수를 초과했습니다.");
        isRunning = false;
    }

    string BuildTextContext(string userCommand, bool isFirstStep)
    {
        string gripperState = pincherController.CurrentGrip() < 0.1f ? "open" : "closed";

        string text = "";
        if (isFirstStep)
        {
            text += $"[User Command]: {userCommand}\n\n";
        }
        else
        {
            text += $"[Continuing task]: {userCommand}\n\n";
        }

        text += "[Current Robot State]\n";

        string[] jointNames = { "base", "shoulder", "elbow", "wrist1", "wrist2", "wrist3" };
        int count = Mathf.Min(jointNames.Length, robotController.JointCount);
        for (int j = 0; j < count; j++)
        {
            float angle = robotController.GetJointAngle(j);
            text += $"  {jointNames[j]}: {angle:F1} degrees\n";
        }
        text += $"  gripper: {gripperState}\n";
        text += $"\nStep {currentStep} of {maxIterations}. Observe the screenshot and decide the next action.";

        return text;
    }

    VisionAction ParseAction(string jsonText)
    {
        // Clean up potential markdown formatting
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

        // Gemini가 배열로 응답하는 경우 첫 번째 요소만 사용
        JToken token = JToken.Parse(jsonText);
        JObject obj;
        if (token.Type == JTokenType.Array)
        {
            JArray arr = (JArray)token;
            if (arr.Count == 0)
                throw new Exception("빈 배열 응답");
            obj = (JObject)arr[0];
            Debug.Log($"<color=yellow>[ObserveActLoop]</color> ⚠️ 배열 응답 수신 ({arr.Count}개 항목), 첫 번째 액션만 사용");
        }
        else
        {
            obj = (JObject)token;
        }

        VisionAction action = new VisionAction();
        action.type = obj["type"]?.ToString() ?? "";
        action.reasoning = obj["reasoning"]?.ToString() ?? "";
        action.state = obj["state"]?.ToString() ?? "";

        if (obj["joints"] != null)
        {
            JObject joints = obj["joints"] as JObject;
            if (joints != null)
            {
                action.joints = new float[6];
                string[] names = { "base", "shoulder", "elbow", "wrist1", "wrist2", "wrist3" };
                for (int i = 0; i < names.Length; i++)
                {
                    action.joints[i] = joints[names[i]]?.Value<float>() ?? 0f;
                }
            }
        }

        return action;
    }

    IEnumerator ExecuteMoveJoints(VisionAction action)
    {
        if (action.joints == null || action.joints.Length < 6)
        {
            Debug.LogError("[ObserveActLoop] Invalid joints data");
            yield break;
        }

        // Stop any manual rotations
        robotController.StopAllJointRotations();

        // Set all joint targets
        int count = Mathf.Min(action.joints.Length, robotController.JointCount);
        for (int i = 0; i < count; i++)
        {
            robotController.SetJointAngle(i, action.joints[i]);
        }

        // Wait until joints reach targets or timeout
        float elapsed = 0f;
        while (elapsed < jointMoveTimeout)
        {
            bool allReached = true;
            for (int i = 0; i < count; i++)
            {
                float current = robotController.GetJointAngle(i);
                if (Mathf.Abs(Mathf.DeltaAngle(current, action.joints[i])) > angleThreshold)
                {
                    allReached = false;
                    break;
                }
            }
            if (allReached) yield break;

            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        Debug.LogWarning("[ObserveActLoop] Joint move timed out");
    }

    IEnumerator ExecuteGripper(VisionAction action)
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

                if (currentGrip > 0.95f)
                    break;
                if (Mathf.Abs(currentGrip - lastGrip) < 0.001f)
                    stableFrames++;
                else
                    stableFrames = 0;

                if (stableFrames > 20)
                    break;

                lastGrip = currentGrip;
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            pincherController.gripState = GripState.Fixed;
        }
    }
}

// Data class for vision-based actions
public class VisionAction
{
    public string type;       // "move_joints", "gripper", "done", "error"
    public string reasoning;
    public float[] joints;    // 6 joint angles for move_joints
    public string state;      // "open"/"close" for gripper
}
