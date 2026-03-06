using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class VisionGeminiService : MonoBehaviour, IVisionLLMService
{
    public string apiKey = "";
    public string model = "gemini-3.1-pro-preview";

    // IVisionLLMService 구현
    public string ProviderName => $"Gemini ({model})";

    const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models/";
    const int MAX_HISTORY_TURNS = 10;

    // Conversation history (JObject 기반으로 이미지 최적화 가능)
    List<JObject> conversationHistory = new List<JObject>();
    string systemPromptText;

    public IReadOnlyList<JObject> ConversationHistory => conversationHistory;

    /// <summary>
    /// Sets the system prompt (called once per task).
    /// </summary>
    public void SetSystemPrompt(string prompt)
    {
        systemPromptText = prompt;
    }

    /// <summary>
    /// Clears conversation history for a new task.
    /// </summary>
    public void ClearHistory()
    {
        conversationHistory.Clear();
    }

    /// <summary>
    /// IVisionLLMService 인터페이스 구현: 비전 요청 전송
    /// </summary>
    public void SendRequest(List<string> base64Images, string textContent,
                            Action<string> onSuccess, Action<string> onError)
    {
        SendVisionRequest(base64Images, textContent, onSuccess, onError);
    }

    /// <summary>
    /// Sends a vision request with image + text, maintaining conversation history.
    /// </summary>
    public void SendVisionRequest(string base64Image, string textContent,
                                   Action<string> onSuccess, Action<string> onError)
    {
        SendVisionRequest(new List<string> { base64Image }, textContent, onSuccess, onError);
    }

    /// <summary>
    /// Sends a vision request with multiple images + text.
    /// </summary>
    public void SendVisionRequest(List<string> base64Images, string textContent,
                                   Action<string> onSuccess, Action<string> onError)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            onError?.Invoke("API Key가 설정되지 않았습니다.");
            return;
        }

        StartCoroutine(SendRequestCoroutine(base64Images, textContent, onSuccess, onError));
    }

    /// <summary>
    /// 이전 user 메시지에서 이미지(inline_data)를 제거하고 텍스트 요약으로 교체.
    /// 최신(현재) 스텝의 이미지만 유지하여 토큰 사용량을 대폭 절감.
    /// </summary>
    void StripOldImages()
    {
        int strippedCount = 0;
        foreach (var msg in conversationHistory)
        {
            if (msg["role"]?.ToString() != "user") continue;

            JArray parts = msg["parts"] as JArray;
            if (parts == null) continue;

            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (parts[i]["inline_data"] != null)
                {
                    // 이미지를 텍스트 설명으로 교체
                    parts[i] = JObject.FromObject(new { text = "[이전 스텝 스크린샷 - 최적화로 제거됨]" });
                    strippedCount++;
                }
            }
        }

        if (strippedCount > 0)
            Debug.Log($"<color=cyan>[VisionGeminiService]</color> 🗜️ 이전 이미지 {strippedCount}개 제거 (토큰 최적화)");
    }

    IEnumerator SendRequestCoroutine(List<string> base64Images, string textContent,
                                      Action<string> onSuccess, Action<string> onError)
    {
        // API Key를 URL이 아닌 헤더로 전송
        string url = $"{BASE_URL}{model}:generateContent";
        Debug.Log($"<color=cyan>[VisionGeminiService]</color> ========== API 요청 시작 ==========");
        Debug.Log($"<color=cyan>[VisionGeminiService]</color> URL: {url}");
        Debug.Log($"<color=cyan>[VisionGeminiService]</color> 모델: {model}");
        Debug.Log($"<color=cyan>[VisionGeminiService]</color> 이미지 {base64Images.Count}장, 대화 기록: {conversationHistory.Count}개 메시지");

        float requestStartTime = Time.realtimeSinceStartup;

        // ── 이미지 최적화: 이전 스텝의 이미지 제거 ──
        StripOldImages();

        // Build user message parts: images + text (JObject 기반)
        var userParts = new JArray();

        // Add all images (현재 스텝만 이미지 포함)
        foreach (var base64Image in base64Images)
        {
            userParts.Add(JObject.FromObject(new
            {
                inline_data = new
                {
                    mime_type = "image/jpeg",
                    data = base64Image
                }
            }));
        }

        // Add text
        userParts.Add(JObject.FromObject(new { text = textContent }));

        // Create user message
        var userMessage = new JObject
        {
            ["role"] = "user",
            ["parts"] = userParts
        };

        // Add to history
        conversationHistory.Add(userMessage);

        // Build contents array from history
        var contents = new JArray();
        foreach (var msg in conversationHistory)
            contents.Add(msg.DeepClone());

        // Build generation config
        var generationConfig = new JObject
        {
            ["temperature"] = 1.0,
            ["responseMimeType"] = "application/json"
        };

        // Build full request body
        var requestObj = new JObject
        {
            ["contents"] = contents,
            ["generationConfig"] = generationConfig
        };

        // Add system instruction if set
        if (!string.IsNullOrEmpty(systemPromptText))
        {
            requestObj["system_instruction"] = JObject.FromObject(new
            {
                parts = new[] { new { text = systemPromptText } }
            });
        }

        string requestBody = requestObj.ToString(Formatting.None);
        int requestKB = requestBody.Length / 1024;
        Debug.Log($"<color=cyan>[VisionGeminiService]</color> 요청 본문 크기: {requestKB}KB (이미지 {base64Images.Count}장 포함)");

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-goog-api-key", apiKey);
            request.timeout = 300;

            Debug.Log($"<color=cyan>[VisionGeminiService]</color> 요청 전송 중... (타임아웃: 300초)");

            // 비동기 전송 + 진행 상황 로깅
            var operation = request.SendWebRequest();
            float lastLogTime = Time.realtimeSinceStartup;
            float logInterval = 10f;

            while (!operation.isDone)
            {
                float elapsed = Time.realtimeSinceStartup - requestStartTime;
                if (Time.realtimeSinceStartup - lastLogTime >= logInterval)
                {
                    Debug.Log($"<color=yellow>[VisionGeminiService]</color> ⏳ 응답 대기 중... {elapsed:F1}초 경과 (업로드: {operation.progress * 100:F0}%)");
                    lastLogTime = Time.realtimeSinceStartup;
                }
                yield return null;
            }

            float totalTime = Time.realtimeSinceStartup - requestStartTime;
            Debug.Log($"<color=cyan>[VisionGeminiService]</color> 응답 수신 완료! (소요시간: {totalTime:F1}초)");

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                // Remove the failed user message from history
                if (conversationHistory.Count > 0)
                    conversationHistory.RemoveAt(conversationHistory.Count - 1);

                long code = request.responseCode;
                string errorMsg;

                if (code == 429)
                    errorMsg = "RATE_LIMIT";
                else if (code == 404)
                    errorMsg = $"모델을 찾을 수 없습니다 (404): {model}\n서버 응답: {request.downloadHandler.text}\ngemini-2.5-flash 등 다른 모델을 시도하세요.";
                else if (code == 400)
                    errorMsg = $"잘못된 요청 (400): 모델명을 확인하세요. 현재: {model}\n서버 응답: {request.downloadHandler.text}";
                else if (code == 401 || code == 403)
                    errorMsg = "API Key가 유효하지 않습니다.";
                else if (code == 0)
                    errorMsg = $"타임아웃 또는 연결 실패 ({totalTime:F1}초 경과)\n{request.error}";
                else
                    errorMsg = $"HTTP {code}: {request.error}\n서버 응답: {request.downloadHandler.text}";

                Debug.LogError($"<color=red>[VisionGeminiService]</color> ❌ 에러 (HTTP {code}, {totalTime:F1}초): {errorMsg}");
                onError?.Invoke(errorMsg);
                yield break;
            }

            string responseText = request.downloadHandler.text;
            Debug.Log($"<color=green>[VisionGeminiService]</color> ✅ 성공! 응답 크기: {responseText.Length}자 ({totalTime:F1}초 소요)");

            try
            {
                string extractedText = ExtractTextFromResponse(responseText);
                Debug.Log($"<color=green>[VisionGeminiService]</color> 📋 추출된 응답: {extractedText}");

                // Add assistant response to history
                var modelMessage = new JObject
                {
                    ["role"] = "model",
                    ["parts"] = new JArray { JObject.FromObject(new { text = extractedText }) }
                };
                conversationHistory.Add(modelMessage);

                // Trim history if too long
                TrimHistory();

                onSuccess?.Invoke(extractedText);
            }
            catch (Exception e)
            {
                // Remove the failed user message from history
                if (conversationHistory.Count > 0)
                    conversationHistory.RemoveAt(conversationHistory.Count - 1);

                Debug.LogError($"<color=red>[VisionGeminiService]</color> ❌ 파싱 에러: {e.Message}\nRaw: {responseText}");
                onError?.Invoke($"응답 파싱 실패: {e.Message}");
            }
        }
    }

    string ExtractTextFromResponse(string jsonResponse)
    {
        JObject root = JObject.Parse(jsonResponse);

        JArray candidates = root["candidates"] as JArray;
        if (candidates == null || candidates.Count == 0)
            throw new Exception("Gemini 응답에 candidates가 없습니다.");

        JArray parts = candidates[0]["content"]["parts"] as JArray;
        if (parts == null || parts.Count == 0)
            throw new Exception("Gemini 응답에 parts가 없습니다.");

        foreach (var part in parts)
        {
            if (part["text"] != null)
            {
                return part["text"].ToString();
            }
        }

        throw new Exception("Gemini 응답에 텍스트 part가 없습니다.");
    }

    void TrimHistory()
    {
        int maxMessages = MAX_HISTORY_TURNS * 2;

        if (conversationHistory.Count <= maxMessages)
            return;

        int removeCount = conversationHistory.Count - maxMessages;
        conversationHistory.RemoveRange(1, removeCount);

        Debug.Log($"<color=cyan>[VisionGeminiService]</color> 🗜️ 히스토리 정리: {conversationHistory.Count}개 메시지 유지 (최대 {maxMessages})");
    }
}
