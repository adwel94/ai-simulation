using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class GeminiService : MonoBehaviour
{
    public string apiKey = "";
    public string model = "gemini-3.1-pro-preview";

    const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models/";

    const string SYSTEM_PROMPT = @"You are a robot arm controller. You receive natural language commands (Korean or English) and convert them into a sequence of robot actions.

The robot is a UR3 arm with a 2-finger gripper. It can:
- Move its end-effector to positions in the workspace
- Open/close its gripper to pick up and release objects

The scene contains colored balls on a table. Each ball has a name like ""Ball_빨강"" (red), ""Ball_파랑"" (blue), ""Ball_초록"" (green).

You must respond with ONLY a JSON object in this format:
{
  ""understood"": true,
  ""intent"": ""brief description of the task"",
  ""actions"": [
    {""type"": ""move_above"", ""target"": ""Ball_빨강""},
    {""type"": ""gripper"", ""state"": ""open""},
    {""type"": ""move_to"", ""target"": ""Ball_빨강""},
    {""type"": ""gripper"", ""state"": ""close""},
    {""type"": ""move_above"", ""target"": ""Ball_빨강""},
    {""type"": ""move_home""}
  ]
}

Action types:
- ""move_above"": Move end-effector above the target ball (+0.1m height). Requires ""target"" (ball name).
- ""move_to"": Move end-effector to the target ball position for grasping. Requires ""target"" (ball name).
- ""move_home"": Move the arm back to the home/rest position.
- ""gripper"": Open or close the gripper. Requires ""state"": ""open"" or ""close"".
- ""wait"": Pause for a duration. Requires ""seconds"" (number).

Typical pick sequence: move_above -> gripper open -> move_to -> gripper close -> move_above -> move_home
Typical place sequence: move_above target -> move_to target -> gripper open -> move_above -> move_home

If the command is unclear or you cannot understand it, respond:
{""understood"": false, ""error"": ""explanation in the user's language""}

IMPORTANT: Only output valid JSON. No markdown, no explanation, no code blocks.";

    public void SendCommand(string userCommand, string sceneContext,
                             Action<GeminiResponse> onSuccess, Action<string> onError)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            onError?.Invoke("API Key가 설정되지 않았습니다.");
            return;
        }

        StartCoroutine(SendRequest(userCommand, sceneContext, onSuccess, onError));
    }

    IEnumerator SendRequest(string userCommand, string sceneContext,
                             Action<GeminiResponse> onSuccess, Action<string> onError)
    {
        // API Key를 URL이 아닌 헤더로 전송
        string url = $"{BASE_URL}{model}:generateContent";
        Debug.Log($"<color=cyan>[GeminiService]</color> ========== API 요청 시작 ==========");
        Debug.Log($"<color=cyan>[GeminiService]</color> URL: {url}");
        Debug.Log($"<color=cyan>[GeminiService]</color> 모델: {model}");
        Debug.Log($"<color=cyan>[GeminiService]</color> 명령어: {userCommand}");

        float requestStartTime = Time.realtimeSinceStartup;
        string userContent = $"{sceneContext}\n\n[User Command]\n{userCommand}";

        // Build request with system_instruction separated (responseJsonSchema 제거 - 호환성 문제)
        var requestObj = new Dictionary<string, object>
        {
            ["system_instruction"] = new
            {
                parts = new[] { new { text = SYSTEM_PROMPT } }
            },
            ["contents"] = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userContent } }
                }
            },
            ["generationConfig"] = new Dictionary<string, object>
            {
                ["temperature"] = 1.0,
                ["responseMimeType"] = "application/json"
            }
        };

        string requestBody = JsonConvert.SerializeObject(requestObj);
        Debug.Log($"<color=cyan>[GeminiService]</color> 요청 본문 크기: {requestBody.Length / 1024}KB");

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-goog-api-key", apiKey);
            request.timeout = 300; // 5분 타임아웃 (Gemini 3 thinking 모델은 오래 걸릴 수 있음)

            Debug.Log($"<color=cyan>[GeminiService]</color> 요청 전송 중... (타임아웃: 300초)");

            // 비동기 전송 + 진행 상황 로깅
            var operation = request.SendWebRequest();
            float lastLogTime = Time.realtimeSinceStartup;
            float logInterval = 10f; // 10초마다 진행 로그

            while (!operation.isDone)
            {
                float elapsed = Time.realtimeSinceStartup - requestStartTime;
                if (Time.realtimeSinceStartup - lastLogTime >= logInterval)
                {
                    Debug.Log($"<color=yellow>[GeminiService]</color> ⏳ 응답 대기 중... {elapsed:F1}초 경과");
                    lastLogTime = Time.realtimeSinceStartup;
                }
                yield return null;
            }

            float totalTime = Time.realtimeSinceStartup - requestStartTime;
            Debug.Log($"<color=cyan>[GeminiService]</color> 응답 수신 완료! (소요시간: {totalTime:F1}초)");

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                long code = request.responseCode;
                string errorMsg = $"HTTP {code}: {request.error}";

                if (code == 404)
                    errorMsg = $"모델을 찾을 수 없습니다 (404): {model}\n서버 응답: {request.downloadHandler.text}\ngemini-2.5-flash 등 다른 모델을 시도하세요.";
                else if (code == 400)
                    errorMsg = $"잘못된 요청 (400): 모델명을 확인하세요. 현재: {model}\n서버 응답: {request.downloadHandler.text}";
                else if (code == 401 || code == 403)
                    errorMsg = "API Key가 유효하지 않습니다.";
                else if (code == 429)
                    errorMsg = "요청 한도를 초과했습니다. 잠시 후 다시 시도하세요.";
                else if (code == 0)
                    errorMsg = $"타임아웃 또는 연결 실패 ({totalTime:F1}초 경과)\n{request.error}";

                Debug.LogError($"<color=red>[GeminiService]</color> ❌ 에러 (HTTP {code}, {totalTime:F1}초): {errorMsg}");
                onError?.Invoke(errorMsg);
                yield break;
            }

            string responseText = request.downloadHandler.text;
            Debug.Log($"<color=green>[GeminiService]</color> ✅ 성공! 응답 크기: {responseText.Length}자 ({totalTime:F1}초 소요)");

            try
            {
                GeminiResponse response = ParseResponse(responseText);
                Debug.Log($"<color=green>[GeminiService]</color> 📋 파싱 완료: understood={response.understood}, actions={response.actions?.Count ?? 0}개");
                onSuccess?.Invoke(response);
            }
            catch (Exception e)
            {
                Debug.LogError($"<color=red>[GeminiService]</color> ❌ 파싱 에러: {e.Message}\nRaw: {responseText}");
                onError?.Invoke($"응답 파싱 실패: {e.Message}");
            }
        }
    }

    GeminiResponse ParseResponse(string jsonResponse)
    {
        JObject root = JObject.Parse(jsonResponse);

        // Extract text from candidates[0].content.parts
        JArray candidates = root["candidates"] as JArray;
        if (candidates == null || candidates.Count == 0)
            throw new Exception("Gemini 응답에 candidates가 없습니다.");

        // Search through parts for text content (skip thinking/signature parts)
        JArray parts = candidates[0]["content"]["parts"] as JArray;
        if (parts == null || parts.Count == 0)
            throw new Exception("Gemini 응답에 parts가 없습니다.");

        string text = null;
        foreach (var part in parts)
        {
            if (part["text"] != null)
            {
                text = part["text"].ToString();
                break;
            }
        }

        if (text == null)
            throw new Exception("Gemini 응답에 텍스트 part가 없습니다.");

        Debug.Log($"[GeminiService] Extracted text: {text}");

        // Parse the JSON text from Gemini
        GeminiResponse response = JsonConvert.DeserializeObject<GeminiResponse>(text);
        return response;
    }
}

// Data classes
[Serializable]
public class GeminiResponse
{
    public bool understood;
    public string intent;
    public string error;
    public List<RobotAction> actions;
}

[Serializable]
public class RobotAction
{
    public string type;       // "move_above", "move_to", "move_home", "gripper", "wait"
    public string target;     // ball name
    public string state;      // "open"/"close" for gripper
    public float seconds;     // for wait
}
