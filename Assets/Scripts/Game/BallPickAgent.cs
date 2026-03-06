using System;
using System.Collections;
using UnityEngine;
using Newtonsoft.Json.Linq;

/// <summary>
/// AI agent for the Ball Pick game.
/// Uses VisionGeminiService to analyze screenshots and executes tools
/// through BallPickGameController — the exact same API as player input.
/// </summary>
public class BallPickAgent : MonoBehaviour
{
    [Header("References")]
    public BallPickGameController gameController;
    public ScreenshotCapture screenshotCapture;
    public VisionGeminiService visionGeminiService;

    [Header("Loop Settings")]
    public int maxIterations = 50;
    public float settleDelay = 0.5f;
    public float rateLimitWait = 5.0f;
    public int maxRetries = 3;

    bool isRunning;
    Coroutine loopCoroutine;
    int currentStep;
    float loopStartTime;
    string userCommand = "";

    public bool IsRunning => isRunning;
    public int CurrentStep => currentStep;
    public int MaxIterations => maxIterations;
    public float ElapsedTime => isRunning ? Time.realtimeSinceStartup - loopStartTime : 0f;
    public string UserCommand => userCommand;

    // Debug data
    public string DebugSentText { get; private set; }
    public string DebugRawResponse { get; private set; }
    public string DebugActionType { get; private set; }
    public string DebugReasoning { get; private set; }

    // Events
    public event Action<string> OnStatusChanged;
    public event Action OnCompleted;
    public event Action<string> OnError;

    void Awake()
    {
        if (gameController == null) gameController = FindObjectOfType<BallPickGameController>();
        if (screenshotCapture == null) screenshotCapture = FindObjectOfType<ScreenshotCapture>();
        if (visionGeminiService == null) visionGeminiService = FindObjectOfType<VisionGeminiService>();
    }

    const string SYSTEM_PROMPT = @"당신은 로봇 팔로 공을 집는 게임을 플레이하는 AI입니다.
매 스텝마다 카메라 스크린샷 1장을 받습니다. 카메라는 사선 시점으로 장면을 보여줍니다.
camera 도구로 카메라를 회전시켜 다양한 각도에서 관찰할 수 있습니다.

사용 가능한 도구(가상 버튼):
- move: 집게를 수평 이동 (카메라 기준 left, right, forward, backward 중 택1)
- lower: 집게를 아래로 내림
- raise: 집게를 위로 올림
- grip: 집게 열기(open) 또는 닫기(close)
- camera: 카메라를 씬 중심 기준으로 좌우 궤도 회전
- wait: 대기하면서 관찰
- done: 작업 완료 선언
- error: 작업 실패 선언

중요 규칙:
- 스크린샷만 볼 수 있습니다. 좌표, 위치값, 수치 데이터에는 접근할 수 없습니다.
- 이동 방향은 카메라 기준입니다 (forward=화면 안쪽, backward=화면 바깥, left=화면 왼쪽, right=화면 오른쪽).
- 물체가 가려지거나 위치 파악이 어려우면 camera 도구로 시점을 바꿔 확인하세요.
- 사람처럼 생각하세요: 이미지를 보고, 거리를 눈으로 가늠하고, 적절한 시간만큼 버튼을 누르세요.
- 작고 점진적인 움직임을 사용하세요. 이동 후 매번 새 스크린샷을 받아 결과를 확인합니다.
- duration은 버튼을 누르는 시간(초)입니다. 미세 조정은 0.3~0.8, 큰 이동은 1.0~2.5를 사용하세요.

일반적인 전략:
1. 스크린샷을 보고 바닥의 색깔 공을 찾기
2. 필요하면 camera로 시점을 바꿔 위치를 더 정확히 파악
3. 집게를 목표 공 바로 위에 정렬 (move로 좌우/전후 이동)
4. grip open으로 집게 열기
5. lower로 집게를 공까지 내리기
6. grip close로 공 잡기
7. raise로 집게 올리기
8. done 출력

반드시 아래 형식 중 하나의 JSON 객체만 응답하세요:

수평 이동:
{""type"": ""move"", ""reasoning"": ""간단한 설명"", ""direction"": ""left"", ""duration"": 0.3}
direction은 left, right, forward, backward 중 하나

집게 내리기:
{""type"": ""lower"", ""reasoning"": ""간단한 설명"", ""duration"": 0.5}

집게 올리기:
{""type"": ""raise"", ""reasoning"": ""간단한 설명"", ""duration"": 0.5}

집게 제어:
{""type"": ""grip"", ""reasoning"": ""간단한 설명"", ""state"": ""open""}
또는
{""type"": ""grip"", ""reasoning"": ""간단한 설명"", ""state"": ""close""}

카메라 회전:
{""type"": ""camera"", ""reasoning"": ""간단한 설명"", ""direction"": ""left"", ""angle"": 45}
direction은 left(반시계) 또는 right(시계), angle은 10~90도

대기 및 관찰:
{""type"": ""wait"", ""reasoning"": ""간단한 설명"", ""duration"": 0.3}

작업 완료:
{""type"": ""done"", ""reasoning"": ""작업이 성공적으로 완료됨""}

작업 실패:
{""type"": ""error"", ""reasoning"": ""작업을 완료할 수 없는 이유 설명""}

중요: 유효한 JSON만 출력하세요. 마크다운, 설명문, 코드 블록은 사용하지 마세요.";

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
            gameController.StopAll();
            gameController.IsAIControlled = false;
            isRunning = false;
            OnStatusChanged?.Invoke("중지됨");
        }
    }

    IEnumerator RunLoop()
    {
        isRunning = true;
        currentStep = 0;
        loopStartTime = Time.realtimeSinceStartup;
        gameController.IsAIControlled = true;

        visionGeminiService.SetSystemPrompt(SYSTEM_PROMPT);
        visionGeminiService.ClearHistory();

        OnStatusChanged?.Invoke("AI 시작...");
        Debug.Log($"<color=magenta>[BallPickAgent]</color> ========== AI 시작 ==========");
        Debug.Log($"<color=magenta>[BallPickAgent]</color> 명령: {userCommand}, 최대 {maxIterations}스텝");

        for (int i = 0; i < maxIterations; i++)
        {
            currentStep = i + 1;
            float stepStart = Time.realtimeSinceStartup;

            Debug.Log($"<color=magenta>[BallPickAgent]</color> ────── Step {currentStep}/{maxIterations} ──────");
            OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] 관찰 중...");

            yield return new WaitForEndOfFrame();

            // 1. Capture screenshot (same camera the player sees)
            string screenshot = screenshotCapture.CaptureBase64();
            if (string.IsNullOrEmpty(screenshot))
            {
                OnError?.Invoke("스크린샷 캡처 실패");
                break;
            }

            // 2. Build text context
            string textContext = currentStep == 1
                ? $"[사용자 명령]: {userCommand}\n\n현재 카메라 각도: {gameController.OrbitAngle:F0}도\n스텝 {currentStep}/{maxIterations}. 스크린샷을 보고 다음 행동을 결정하세요."
                : $"[작업 계속]: {userCommand}\n\n현재 카메라 각도: {gameController.OrbitAngle:F0}도\n스텝 {currentStep}/{maxIterations}. 스크린샷을 보고 다음 행동을 결정하세요.";

            DebugSentText = textContext;

            // 3. Send to Gemini with retry for rate limits
            string geminiResponse = null;
            string geminiError = null;
            bool waiting = true;
            int retryCount = 0;

            while (retryCount <= maxRetries)
            {
                waiting = true;
                geminiResponse = null;
                geminiError = null;

                OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] Gemini 분석 중...");
                Debug.Log($"<color=magenta>[BallPickAgent]</color> Gemini 호출 (시도 {retryCount + 1}/{maxRetries + 1})");

                visionGeminiService.SendVisionRequest(screenshot, textContext,
                    r => { geminiResponse = r; waiting = false; },
                    e => { geminiError = e; waiting = false; }
                );

                while (waiting) yield return null;

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
                break;
            }

            // 4. Parse action
            Debug.Log($"<color=magenta>[BallPickAgent]</color> 응답: {geminiResponse}");
            DebugRawResponse = geminiResponse;

            BallPickAction action;
            try
            {
                action = ParseAction(geminiResponse);
                DebugActionType = action.type;
                DebugReasoning = action.reasoning;
                Debug.Log($"<color=magenta>[BallPickAgent]</color> 액션: {action.type}, 이유: {action.reasoning}");
            }
            catch (Exception e)
            {
                DebugActionType = "parse_error";
                DebugReasoning = e.Message;
                Debug.LogError($"<color=red>[BallPickAgent]</color> 파싱 실패: {e.Message}");
                OnError?.Invoke($"액션 파싱 실패: {e.Message}");
                break;
            }

            // 5. Terminal actions
            if (action.type == "done")
            {
                float total = Time.realtimeSinceStartup - loopStartTime;
                OnStatusChanged?.Invoke($"완료! ({currentStep}스텝, {total:F1}초) - {action.reasoning}");
                Debug.Log($"<color=green>[BallPickAgent]</color> 완료! {currentStep}스텝, {total:F1}초");
                gameController.IsAIControlled = false;
                isRunning = false;
                OnCompleted?.Invoke();
                yield break;
            }

            if (action.type == "error")
            {
                OnError?.Invoke($"AI 실패: {action.reasoning}");
                break;
            }

            // 6. Execute action
            OnStatusChanged?.Invoke($"[{currentStep}/{maxIterations}] {action.reasoning}");
            yield return ExecuteAction(action);

            float stepTime = Time.realtimeSinceStartup - stepStart;
            Debug.Log($"<color=magenta>[BallPickAgent]</color> Step {currentStep} 완료 ({stepTime:F1}초)");

            // 7. Settle delay
            yield return new WaitForSeconds(settleDelay);
        }

        // Loop ended
        gameController.StopAll();
        gameController.IsAIControlled = false;

        if (isRunning)
        {
            float totalElapsed = Time.realtimeSinceStartup - loopStartTime;
            OnStatusChanged?.Invoke($"최대 반복 도달 ({maxIterations}), {totalElapsed:F1}초");
            Debug.LogWarning($"<color=yellow>[BallPickAgent]</color> 최대 반복 도달, {totalElapsed:F1}초");
            isRunning = false;
        }
    }

    BallPickAction ParseAction(string jsonText)
    {
        jsonText = jsonText.Trim();

        // Strip markdown code blocks if present
        if (jsonText.StartsWith("```"))
        {
            int nl = jsonText.IndexOf('\n');
            if (nl >= 0) jsonText = jsonText.Substring(nl + 1);
            if (jsonText.EndsWith("```"))
                jsonText = jsonText.Substring(0, jsonText.Length - 3);
            jsonText = jsonText.Trim();
        }

        JToken token = JToken.Parse(jsonText);
        JObject obj = token.Type == JTokenType.Array
            ? (JObject)((JArray)token)[0]
            : (JObject)token;

        return new BallPickAction
        {
            type = obj["type"]?.ToString() ?? "",
            reasoning = obj["reasoning"]?.ToString() ?? "",
            direction = obj["direction"]?.ToString() ?? "",
            state = obj["state"]?.ToString() ?? "",
            duration = obj["duration"]?.Value<float>() ?? 0.3f,
            angle = obj["angle"]?.Value<float>() ?? 45f
        };
    }

    IEnumerator ExecuteAction(BallPickAction action)
    {
        switch (action.type)
        {
            case "move":
                Vector2 dir = Vector2.zero;
                switch (action.direction)
                {
                    case "left": dir = new Vector2(-1, 0); break;
                    case "right": dir = new Vector2(1, 0); break;
                    case "forward": dir = new Vector2(0, 1); break;
                    case "backward": dir = new Vector2(0, -1); break;
                }
                gameController.SetMoveDirection(dir);
                yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 5f));
                gameController.SetMoveDirection(Vector2.zero);
                break;

            case "lower":
                gameController.SetVerticalDirection(-1);
                yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 5f));
                gameController.SetVerticalDirection(0);
                break;

            case "raise":
                gameController.SetVerticalDirection(1);
                yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 5f));
                gameController.SetVerticalDirection(0);
                break;

            case "grip":
                gameController.SetGrip(action.state);
                if (action.state == "open")
                {
                    float elapsed = 0f;
                    while (gameController.pincherController.CurrentGrip() > 0.05f && elapsed < 3f)
                    {
                        elapsed += Time.fixedDeltaTime;
                        yield return new WaitForFixedUpdate();
                    }
                }
                else if (action.state == "close")
                {
                    float elapsed = 0f;
                    float lastGrip = 0f;
                    int stableFrames = 0;
                    while (elapsed < 3f)
                    {
                        float g = gameController.pincherController.CurrentGrip();
                        if (g > 0.95f) break;
                        if (Mathf.Abs(g - lastGrip) < 0.001f) stableFrames++;
                        else stableFrames = 0;
                        if (stableFrames > 20) break;
                        lastGrip = g;
                        elapsed += Time.fixedDeltaTime;
                        yield return new WaitForFixedUpdate();
                    }
                }
                gameController.SetGrip("fixed");
                break;

            case "camera":
                float delta = Mathf.Clamp(action.angle, 10f, 90f);
                if (action.direction == "left") delta = -delta;
                gameController.RotateCamera(delta);
                yield return null;
                break;

            case "wait":
                yield return new WaitForSeconds(Mathf.Clamp(action.duration, 0.1f, 5f));
                break;

            default:
                Debug.LogWarning($"<color=yellow>[BallPickAgent]</color> 알 수 없는 액션: {action.type}");
                break;
        }
    }
}

public class BallPickAction
{
    public string type;       // move, lower, raise, grip, camera, wait, done, error
    public string reasoning;
    public string direction;  // left, right, forward, backward (for move) / left, right (for camera)
    public string state;      // open, close (for grip)
    public float duration;    // seconds
    public float angle;       // degrees (for camera)
}
