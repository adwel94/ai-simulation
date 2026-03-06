using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json.Linq;

/// <summary>
/// Unity HTTP 시뮬레이션 서버.
/// Python LangGraph 에이전트가 REST API로 시뮬레이션을 제어.
///
/// 엔드포인트:
///   POST /reset  — 에피소드 초기화, observation 반환
///   POST /step   — 액션 실행, 새 observation 반환
///   GET  /status  — 서버 상태 확인
///   GET  /capture — 스크린샷만 캡처
/// </summary>
public class SimulationServer : MonoBehaviour
{
    [Header("Server Settings")]
    public int port = 8765;
    public bool autoStart = false;

    [Header("References")]
    public ActionExecutor actionExecutor;

    HttpListener httpListener;
    Thread listenerThread;
    bool isRunning = false;

    // Main thread dispatch queue (Unity API는 메인 스레드에서만 호출 가능)
    readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

    // Connection tracking
    int totalRequests = 0;
    string lastClientIP = "";

    public bool IsRunning => isRunning;
    public int TotalRequests => totalRequests;
    public string LastClientIP => lastClientIP;

    // Events
    public event Action OnServerStarted;
    public event Action OnServerStopped;
    public event Action<string> OnClientConnected;
    public event Action<string> OnStatusChanged;

    void Awake()
    {
        if (actionExecutor == null)
            actionExecutor = GetComponent<ActionExecutor>();
        if (actionExecutor == null)
            actionExecutor = FindObjectOfType<ActionExecutor>();
    }

    void Start()
    {
        if (autoStart)
            StartServer();
    }

    void Update()
    {
        // Process main thread queue
        while (mainThreadQueue.TryDequeue(out var action))
        {
            try
            {
                action.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"<color=red>[SimulationServer]</color> Main thread action error: {e.Message}");
            }
        }
    }

    void OnDestroy()
    {
        StopServer();
    }

    void OnApplicationQuit()
    {
        StopServer();
    }

    // ─────────────────────────────────
    //  Server lifecycle
    // ─────────────────────────────────

    public void StartServer()
    {
        if (isRunning) return;

        // 서버 모드에서는 백그라운드에서도 실행되어야 함
        Application.runInBackground = true;

        try
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{port}/");
            httpListener.Prefixes.Add($"http://+:{port}/");
            httpListener.Start();

            isRunning = true;
            totalRequests = 0;

            listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "SimulationServer"
            };
            listenerThread.Start();

            Debug.Log($"<color=green>[SimulationServer]</color> ========================================");
            Debug.Log($"<color=green>[SimulationServer]</color> HTTP 서버 시작: http://localhost:{port}/");
            Debug.Log($"<color=green>[SimulationServer]</color> 엔드포인트: POST /reset, POST /step, GET /status");
            Debug.Log($"<color=green>[SimulationServer]</color> ========================================");

            OnServerStarted?.Invoke();
            OnStatusChanged?.Invoke($"서버 대기 중 (localhost:{port})");
        }
        catch (HttpListenerException e)
        {
            // If http://+:port/ fails, try localhost only
            Debug.LogWarning($"[SimulationServer] Wildcard bind failed, trying localhost only: {e.Message}");
            try
            {
                httpListener = new HttpListener();
                httpListener.Prefixes.Add($"http://localhost:{port}/");
                httpListener.Start();

                isRunning = true;
                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "SimulationServer"
                };
                listenerThread.Start();

                Debug.Log($"<color=green>[SimulationServer]</color> HTTP 서버 시작 (localhost only): http://localhost:{port}/");
                OnServerStarted?.Invoke();
                OnStatusChanged?.Invoke($"서버 대기 중 (localhost:{port})");
            }
            catch (Exception e2)
            {
                Debug.LogError($"<color=red>[SimulationServer]</color> 서버 시작 실패: {e2.Message}");
                OnStatusChanged?.Invoke($"서버 시작 실패: {e2.Message}");
            }
        }
    }

    public void StopServer()
    {
        if (!isRunning) return;

        isRunning = false;

        try
        {
            httpListener?.Stop();
            httpListener?.Close();
        }
        catch (Exception) { }

        if (listenerThread != null && listenerThread.IsAlive)
        {
            listenerThread.Join(1000);
        }

        // Clean up action executor
        if (actionExecutor != null)
            actionExecutor.Cleanup();

        Debug.Log("<color=yellow>[SimulationServer]</color> 서버 중지됨");
        OnServerStopped?.Invoke();
        OnStatusChanged?.Invoke("서버 중지됨");
    }

    // ─────────────────────────────────
    //  HTTP Listener (background thread)
    // ─────────────────────────────────

    void ListenLoop()
    {
        while (isRunning)
        {
            try
            {
                var context = httpListener.GetContext();
                totalRequests++;

                // Track client
                string clientIP = context.Request.RemoteEndPoint?.Address.ToString() ?? "unknown";
                if (clientIP != lastClientIP)
                {
                    lastClientIP = clientIP;
                    var ip = clientIP; // capture for closure
                    mainThreadQueue.Enqueue(() => OnClientConnected?.Invoke(ip));
                }

                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
            catch (HttpListenerException)
            {
                // Expected when stopping
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                if (isRunning)
                    Debug.LogError($"<color=red>[SimulationServer]</color> Listener error: {e.Message}");
            }
        }
    }

    void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 200;
            response.Close();
            return;
        }

        string path = request.Url.AbsolutePath.TrimEnd('/');
        string method = request.HttpMethod;

        Debug.Log($"<color=cyan>[SimulationServer]</color> {method} {path}");

        try
        {
            switch (path)
            {
                case "/status":
                    HandleStatus(response);
                    break;
                case "/capture":
                    HandleCapture(response);
                    break;
                case "/reset":
                    if (method == "POST")
                        HandleReset(response);
                    else
                        SendError(response, 405, "Method not allowed. Use POST.");
                    break;
                case "/step":
                    if (method == "POST")
                        HandleStep(request, response);
                    else
                        SendError(response, 405, "Method not allowed. Use POST.");
                    break;
                default:
                    SendError(response, 404, $"Unknown endpoint: {path}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>[SimulationServer]</color> Request error: {e.Message}");
            SendError(response, 500, e.Message);
        }
    }

    // ─────────────────────────────────
    //  Endpoint handlers
    // ─────────────────────────────────

    void HandleStatus(HttpListenerResponse response)
    {
        var status = new JObject
        {
            ["active"] = true,
            ["episode_active"] = actionExecutor?.EpisodeActive ?? false,
            ["current_step"] = actionExecutor?.CurrentStep ?? 0,
            ["max_steps"] = actionExecutor?.MaxSteps ?? 50,
            ["camera_angle"] = actionExecutor?.OrbitAngle ?? 0f,
            ["action_in_progress"] = actionExecutor?.ActionInProgress ?? false,
            ["total_requests"] = totalRequests,
            ["port"] = port
        };
        SendJson(response, status);
    }

    void HandleCapture(HttpListenerResponse response)
    {
        if (actionExecutor == null)
        {
            SendError(response, 500, "ActionExecutor not available");
            return;
        }

        var waitHandle = new ManualResetEventSlim(false);
        JObject result = null;

        mainThreadQueue.Enqueue(() =>
        {
            try
            {
                result = actionExecutor.CaptureObservation();
            }
            finally
            {
                waitHandle.Set();
            }
        });

        if (!waitHandle.Wait(10000))
        {
            SendError(response, 504, "Capture timed out");
            return;
        }

        SendJson(response, result);
    }

    void HandleReset(HttpListenerResponse response)
    {
        if (actionExecutor == null)
        {
            SendError(response, 500, "ActionExecutor not available");
            return;
        }

        if (actionExecutor.ActionInProgress)
        {
            SendError(response, 409, "Action currently in progress. Wait for completion.");
            return;
        }

        // Execute on main thread and wait for result
        var waitHandle = new ManualResetEventSlim(false);
        JObject result = null;
        Exception error = null;

        mainThreadQueue.Enqueue(() =>
        {
            try
            {
                result = actionExecutor.ResetEpisode();
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                waitHandle.Set();
            }
        });

        // Wait for main thread execution (timeout 30s)
        if (!waitHandle.Wait(30000))
        {
            SendError(response, 504, "Reset timed out");
            return;
        }

        if (error != null)
        {
            SendError(response, 500, $"Reset failed: {error.Message}");
            return;
        }

        mainThreadQueue.Enqueue(() => OnStatusChanged?.Invoke("에피소드 리셋 완료"));
        SendJson(response, result);
    }

    void HandleStep(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (actionExecutor == null)
        {
            SendError(response, 500, "ActionExecutor not available");
            return;
        }

        if (!actionExecutor.EpisodeActive)
        {
            SendError(response, 400, "No active episode. Call POST /reset first.");
            return;
        }

        if (actionExecutor.ActionInProgress)
        {
            SendError(response, 409, "Action currently in progress. Wait for completion.");
            return;
        }

        // Read request body
        string body;
        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
        {
            body = reader.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            SendError(response, 400, "Empty request body. Send action JSON.");
            return;
        }

        // Parse action
        ClawAction action;
        try
        {
            action = ActionExecutor.ParseActionJson(body);
        }
        catch (Exception e)
        {
            SendError(response, 400, $"Invalid action JSON: {e.Message}");
            return;
        }

        Debug.Log($"<color=cyan>[SimulationServer]</color> Action: type={action.type}, direction={action.direction}, duration={action.duration}");

        // Execute on main thread and wait for result
        var waitHandle = new ManualResetEventSlim(false);
        JObject result = null;
        Exception execError = null;

        mainThreadQueue.Enqueue(() =>
        {
            actionExecutor.ExecuteActionAsync(action, (obs) =>
            {
                result = obs;
                waitHandle.Set();
            });
        });

        // Wait for action execution (timeout 30s for long actions)
        if (!waitHandle.Wait(30000))
        {
            SendError(response, 504, "Action execution timed out");
            return;
        }

        if (result == null)
        {
            SendError(response, 500, "No observation returned");
            return;
        }

        string stepInfo = $"Step {result["step"]}, done={result["done"]}";
        mainThreadQueue.Enqueue(() => OnStatusChanged?.Invoke(stepInfo));
        SendJson(response, result);
    }

    // ─────────────────────────────────
    //  Response helpers
    // ─────────────────────────────────

    void SendJson(HttpListenerResponse response, JObject json)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None));
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SimulationServer] Failed to send response: {e.Message}");
        }
    }

    void SendError(HttpListenerResponse response, int statusCode, string message)
    {
        try
        {
            var error = new JObject
            {
                ["error"] = message,
                ["status"] = statusCode
            };
            byte[] buffer = Encoding.UTF8.GetBytes(error.ToString(Newtonsoft.Json.Formatting.None));
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = statusCode;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SimulationServer] Failed to send error: {e.Message}");
        }
    }
}
