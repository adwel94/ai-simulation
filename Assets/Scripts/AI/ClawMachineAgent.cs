using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

public class ClawMachineAgent : MonoBehaviour
{
    [Header("References")]
    public ClawMachineController clawController;
    public GripperDemoController gripperDemo;
    public PincherController pincherController;
    public ScreenshotCapture screenshotCapture;
    public VisionGeminiService visionGeminiService;

    [Header("Data Collection")]
    public EpisodeLogger episodeLogger;

    [Header("Cameras")]
    public Camera topDownCamera;

    [Header("Loop Settings")]
    public int maxIterations = 50;
    public float settleDelay = 0.5f;
    public float rateLimitWait = 5.0f;
    public int maxRetries = 3;

    bool isRunning = false;
    Coroutine loopCoroutine;
    int currentStep;
    float loopStartTime;

    // Camera orbit state
    float orbitAngle = 0f;
    float orbitRadius;
    float orbitHeight;

    // Semantic Kernel 구성 요소
    ToolRegistry toolRegistry;
    string systemPrompt;

    public bool IsRunning => isRunning;
    public int CurrentStep => currentStep;
    public int MaxIterations => maxIterations;
    public float ElapsedTime => isRunning ? Time.realtimeSinceStartup - loopStartTime : 0f;
    public string UserCommand => userCommand;

    // Debug data
    Texture2D debugSideView;
    Texture2D debugTopView;
    public Texture2D DebugSideView => debugSideView;
    public Texture2D DebugTopView => debugTopView;
    public string DebugSentText { get; private set; }
    public string DebugRawResponse { get; private set; }
    public string DebugActionType { get; private set; }
    public string DebugReasoning { get; private set; }

    // Events
    public event Action<string> OnStatusChanged;
    public event Action OnCompleted;
    public event Action<string> OnError;

    // ───────────────────────────────────────────────
    //  Semantic Kernel 프롬프트 구성 요소 (분리됨)
    // ───────────────────────────────────────────────

    const string ROLE_DESCRIPTION =
@"당신은 인형뽑기(크레인 게임)를 플레이하는 AI입니다. 매 스텝마다 카메라 1대의 스크린샷을 받습니다.
카메라는 사선 시점으로 장면을 보여줍니다. camera 도구로 카메라를 회전시켜 다양한 각도에서 관찰할 수 있습니다.";

    const string RULES =
@"중요 규칙:
- 스크린샷만 볼 수 있습니다. 좌표, 위치값, 수치 데이터에는 접근할 수 없습니다.
- 물체가 가려지거나 위치 파악이 어려우면 camera 도구로 시점을 바꿔 확인하세요.
- 사람처럼 생각하세요: 이미지를 보고, 거리를 눈으로 가늠하고, 적절한 시간만큼 버튼을 누르세요.
- 작고 점진적인 움직임을 사용하세요. 이동 후 매번 새 스크린샷을 받아 결과를 확인합니다.
- duration은 버튼을 누르는 시간(초)입니다. 미세 조정은 0.3~0.8, 큰 이동은 1.0~2.5를 사용하세요.";

    const string STRATEGY =
@"일반적인 전략:
1. 스크린샷을 보고 바닥의 색깔 공을 찾기
2. 필요하면 camera로 시점을 바꿔 위치를 더 정확히 파악
3. 집게를 목표 공 바로 위에 정렬 (좌우/전후 이동)
4. 집게 열기
5. 집게를 공까지 내리기
6. 집게 닫아서 공 잡기
7. 집게 올리기
8. done 출력";

    string userCommand = "";

    void Awake()
    {
        // Auto-find references
        if (clawController == null) clawController = FindObjectOfType<ClawMachineController>();
        if (gripperDemo == null) gripperDemo = FindObjectOfType<GripperDemoController>();
        if (pincherController == null) pincherController = FindObjectOfType<PincherController>();
        if (screenshotCapture == null) screenshotCapture = FindObjectOfType<ScreenshotCapture>();
        if (visionGeminiService == null) visionGeminiService = FindObjectOfType<VisionGeminiService>();
        if (episodeLogger == null) episodeLogger = FindObjectOfType<EpisodeLogger>();

        if (topDownCamera == null)
        {
            var go = GameObject.Find("TopDownCamera");
            if (go != null) topDownCamera = go.GetComponent<Camera>();
        }

        // Main Camera 초기 위치에서 궤도 파라미터 계산
        if (screenshotCapture != null && screenshotCapture.targetCamera != null)
        {
            Camera mainCam = screenshotCapture.targetCamera;
            Vector3 camPos = mainCam.transform.position;
            orbitHeight = camPos.y;
            orbitRadius = Mathf.Sqrt(camPos.x * camPos.x + camPos.z * camPos.z);
            orbitAngle = Mathf.Atan2(camPos.x, camPos.z) * Mathf.Rad2Deg;
        }

        // ── Semantic Kernel 초기화 ──
        // 도구 레지스트리 생성 (ClawMachineTools 플러그인)
        toolRegistry = ClawMachineTools.CreateDefaultRegistry();

        // PromptBuilder로 시스템 프롬프트 자동 생성
        systemPrompt = new PromptBuilder()
            .SetRole(ROLE_DESCRIPTION)
            .SetTools(toolRegistry)
            .SetRules(RULES)
            .SetStrategy(STRATEGY)
            .Build();

        Debug.Log("<color=magenta>[ClawMachineAgent]</color> Semantic Kernel 초기화 완료");
        Debug.Log($"<color=magenta>[ClawMachineAgent]</color> 등록된 도구: {toolRegistry.Tools.Count}개");
        Debug.Log($"<color=magenta>[ClawMachineAgent]</color> LLM 프로바이더: {((IVisionLLMService)visionGeminiService).ProviderName}");
    }

    public void StartLoop(string command)
    {
        if (isRunning)
        {
            OnError?.Invoke("이미 실행 중입니다.");
            return;
        }
        userCommand = command;
        loopCoroutine = StartCoroutine(RunLoop());
    }

    public void StopLoop()
    {
        if (loopCoroutine != null)
        {
            StopCoroutine(loopCoroutine);
            clawController.isAIControlled = false;
            clawController.StopAIMovement();
            gripperDemo.moveState = BigHandState.Fixed;
            pincherController.gripState = GripState.Fixed;
            isRunning = false;
            OnStatusChanged?.Invoke("중지됨");

            // 데이터 수집: 에피소드 종료 (중지 = 실패)
            if (episodeLogger != null && episodeLogger.IsLogging)
                episodeLogger.EndEpisode(false, "사용자가 중지함");
        }
    }

    IEnumerator RunLoop()
    {
        isRunning = true;
        currentStep = 0;
        loopStartTime = Time.realtimeSinceStartup;

        // Enable AI control
        clawController.isAIControlled = true;

        // IVisionLLMService 인터페이스를 통해 LLM 서비스 접근
        IVisionLLMService llmService = visionGeminiService;
        llmService.SetSystemPrompt(systemPrompt);
        llmService.ClearHistory();

        // ── 데이터 수집 시작 ──
        if (episodeLogger != null)
        {
            string toolSchema = toolRegistry.ExportSchema();
            episodeLogger.StartEpisode(userCommand, systemPrompt, toolSchema);
        }

        OnStatusChanged?.Invoke("AI 시작...");
        Debug.Log("<color=magenta>[ClawMachineAgent]</color> ==========================================");
        Debug.Log($"<color=magenta>[ClawMachineAgent]</color> AI 인형뽑기 에이전트 시작! 명령: {userCommand}");
        Debug.Log($"<color=magenta>[ClawMachineAgent]</color> 최대 반복: {maxIterations}");
        Debug.Log($"<color=magenta>[ClawMachineAgent]</color> LLM: {llmService.ProviderName}");
        Debug.Log("<color=magenta>[ClawMachineAgent]</color> ==========================================");

        for (int i = 0; i < maxIterations; i++)
        {
            currentStep = i + 1;
            float stepStartTime = Time.realtimeSinceStartup;

            Debug.Log($"<color=magenta>[ClawMachineAgent]</color> ────── Step {currentStep}/{maxIterations} ──────");
            OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] 관찰 중...");

            // 1. Wait for rendering
            yield return new WaitForEndOfFrame();

            // 2. Capture screenshot
            var images = new List<string>();

            string screenshot = screenshotCapture.CaptureBase64();
            if (string.IsNullOrEmpty(screenshot))
            {
                OnError?.Invoke("스크린샷 캡처 실패");
                break;
            }
            images.Add(screenshot);
            CopyDebugTexture(ref debugSideView, screenshotCapture.LastCapturedTexture);

            Debug.Log($"<color=magenta>[ClawMachineAgent]</color> 스크린샷 캡처 완료");

            // 3. Build text context
            string textContext;
            if (currentStep == 1)
                textContext = $"[사용자 명령]: {userCommand}\n\n현재 카메라 각도: {orbitAngle:F0}도\n스텝 {currentStep}/{maxIterations}. 스크린샷을 보고 다음 행동을 결정하세요.";
            else
                textContext = $"[작업 계속]: {userCommand}\n\n현재 카메라 각도: {orbitAngle:F0}도\n스텝 {currentStep}/{maxIterations}. 스크린샷을 보고 다음 행동을 결정하세요.";

            DebugSentText = textContext;

            // 4. Send to LLM (via IVisionLLMService interface)
            string geminiResponse = null;
            string geminiError = null;
            bool waiting = true;
            int retryCount = 0;

            while (retryCount <= maxRetries)
            {
                waiting = true;
                geminiResponse = null;
                geminiError = null;

                OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] {llmService.ProviderName} 분석 중 ({images.Count}장)...");
                Debug.Log($"<color=magenta>[ClawMachineAgent]</color> LLM 호출 (시도 {retryCount + 1}/{maxRetries + 1}, 이미지 {images.Count}장)");

                llmService.SendRequest(images, textContext,
                    onSuccess: (response) => { geminiResponse = response; waiting = false; },
                    onError: (error) => { geminiError = error; waiting = false; }
                );

                while (waiting)
                {
                    yield return null;
                }

                if (geminiError == "RATE_LIMIT" && retryCount < maxRetries)
                {
                    retryCount++;
                    OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] Rate limit, {rateLimitWait}초 대기...");
                    yield return new WaitForSeconds(rateLimitWait);
                    continue;
                }
                break;
            }

            if (!string.IsNullOrEmpty(geminiError))
            {
                if (geminiError == "RATE_LIMIT")
                    geminiError = "요청 한도 초과. 잠시 후 다시 시도하세요.";
                OnError?.Invoke(geminiError);

                // 데이터 수집: 에피소드 종료 (에러)
                if (episodeLogger != null && episodeLogger.IsLogging)
                    episodeLogger.EndEpisode(false, $"LLM 에러: {geminiError}");
                break;
            }

            // 5. Parse action
            Debug.Log($"<color=magenta>[ClawMachineAgent]</color> 응답: {geminiResponse}");
            DebugRawResponse = geminiResponse;

            ClawAction action;
            try
            {
                action = ParseAction(geminiResponse);
                DebugActionType = action.type;
                DebugReasoning = action.reasoning;
                Debug.Log($"<color=magenta>[ClawMachineAgent]</color> 파싱: type={action.type}, reasoning={action.reasoning}");
            }
            catch (Exception e)
            {
                DebugActionType = "parse_error";
                DebugReasoning = e.Message;
                Debug.LogError($"<color=red>[ClawMachineAgent]</color> 파싱 실패: {e.Message}");
                OnError?.Invoke($"액션 파싱 실패: {e.Message}");

                // 데이터 수집: 에피소드 종료 (파싱 에러)
                if (episodeLogger != null && episodeLogger.IsLogging)
                    episodeLogger.EndEpisode(false, $"파싱 에러: {e.Message}");
                break;
            }

            // ── 데이터 수집: 스텝 기록 ──
            if (episodeLogger != null && episodeLogger.IsLogging)
            {
                byte[] jpegBytes = screenshotCapture.LastJpegBytes;
                if (jpegBytes != null)
                    episodeLogger.LogStep(jpegBytes, textContext, geminiResponse, action, orbitAngle);
            }

            // 6. Handle action
            if (action.type == "done")
            {
                float totalTime = Time.realtimeSinceStartup - loopStartTime;
                OnStatusChanged?.Invoke($"완료! ({currentStep}스텝, {totalTime:F1}초) - {action.reasoning}");
                Debug.Log($"<color=green>[ClawMachineAgent]</color> 태스크 완료! {currentStep}스텝, {totalTime:F1}초");
                clawController.isAIControlled = false;
                isRunning = false;

                // 데이터 수집: 에피소드 종료 (성공)
                if (episodeLogger != null && episodeLogger.IsLogging)
                    episodeLogger.EndEpisode(true, action.reasoning);

                OnCompleted?.Invoke();
                yield break;
            }

            if (action.type == "error")
            {
                OnError?.Invoke($"AI 판단 실패: {action.reasoning}");

                // 데이터 수집: 에피소드 종료 (AI 실패 선언)
                if (episodeLogger != null && episodeLogger.IsLogging)
                    episodeLogger.EndEpisode(false, $"AI error: {action.reasoning}");
                break;
            }

            OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] {action.reasoning}");

            // Execute action
            yield return ExecuteAction(action);

            float stepTime = Time.realtimeSinceStartup - stepStartTime;
            Debug.Log($"<color=magenta>[ClawMachineAgent]</color> Step {currentStep} 완료 ({stepTime:F1}초)");

            // 7. Settle delay
            yield return new WaitForSeconds(settleDelay);
        }

        // Loop ended (max iterations or error)
        clawController.isAIControlled = false;
        clawController.StopAIMovement();
        gripperDemo.moveState = BigHandState.Fixed;
        pincherController.gripState = GripState.Fixed;

        if (isRunning)
        {
            float totalElapsed = Time.realtimeSinceStartup - loopStartTime;
            OnStatusChanged?.Invoke($"최대 반복 횟수 도달 ({maxIterations})");
            Debug.LogWarning($"<color=yellow>[ClawMachineAgent]</color> 최대 반복 도달, {totalElapsed:F1}초 소요");
            isRunning = false;

            // 데이터 수집: 에피소드 종료 (최대 반복 도달)
            if (episodeLogger != null && episodeLogger.IsLogging)
                episodeLogger.EndEpisode(false, "최대 반복 횟수 도달");
        }
    }

    ClawAction ParseAction(string jsonText)
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
                throw new Exception("빈 배열 응답");
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

        // ToolRegistry 기반 검증
        if (toolRegistry != null)
        {
            var toolDef = toolRegistry.GetTool(action.type);
            if (toolDef == null && action.type != "done" && action.type != "error")
            {
                Debug.LogWarning($"<color=yellow>[ClawMachineAgent]</color> 미등록 도구 사용: {action.type}");
            }
        }

        return action;
    }

    IEnumerator ExecuteAction(ClawAction action)
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
                yield return new WaitForSeconds(action.duration);
                break;
            default:
                Debug.LogWarning($"<color=yellow>[ClawMachineAgent]</color> 알 수 없는 액션: {action.type}");
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
            case "forward": z = 1; break;
            case "backward": z = -1; break;
        }

        clawController.SetAIMoveDirection(x, z);
        yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 5f));
        clawController.StopAIMovement();
    }

    IEnumerator ExecuteLower(ClawAction action)
    {
        gripperDemo.moveState = BigHandState.MovingDown;
        yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 5f));
        gripperDemo.moveState = BigHandState.Fixed;
    }

    IEnumerator ExecuteRaise(ClawAction action)
    {
        gripperDemo.moveState = BigHandState.MovingUp;
        yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 5f));
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
                if (currentGrip > 0.95f) break;
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

        Debug.Log($"<color=cyan>[ClawMachineAgent]</color> 카메라 회전: {action.direction} {deltaAngle}도 → 현재 각도: {orbitAngle:F0}도");
    }

    void CopyDebugTexture(ref Texture2D target, Texture2D source)
    {
        if (source == null) return;
        if (target == null || target.width != source.width || target.height != source.height)
        {
            if (target != null) Destroy(target);
            target = new Texture2D(source.width, source.height, source.format, false);
        }
        Graphics.CopyTexture(source, target);
    }
}

public class ClawAction
{
    public string type;       // move, lower, raise, grip, camera, wait, done, error
    public string reasoning;
    public string direction;  // left, right, forward, backward (for move) / left, right (for camera)
    public string state;      // open, close (for grip)
    public float duration;    // seconds
    public float angle;       // degrees (for camera orbit)
}
