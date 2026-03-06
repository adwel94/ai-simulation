using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Semantic Kernel 스타일 Vision LLM 서비스 인터페이스.
/// Gemini, OpenAI, Qwen3-VL 등 다양한 프로바이더를 교체 가능하게 추상화.
/// </summary>
public interface IVisionLLMService
{
    /// <summary>프로바이더 이름 (예: "Gemini", "OpenAI", "Qwen3-VL-Local")</summary>
    string ProviderName { get; }

    /// <summary>시스템 프롬프트 설정 (태스크 시작 시 1회 호출)</summary>
    void SetSystemPrompt(string prompt);

    /// <summary>대화 기록 초기화 (새 태스크 시작 시)</summary>
    void ClearHistory();

    /// <summary>
    /// 비전 요청 전송 (이미지 + 텍스트 → 모델 응답)
    /// </summary>
    /// <param name="base64Images">Base64 인코딩된 JPEG 이미지 리스트</param>
    /// <param name="textContent">텍스트 컨텍스트</param>
    /// <param name="onSuccess">성공 시 콜백 (응답 텍스트)</param>
    /// <param name="onError">실패 시 콜백 (에러 메시지, "RATE_LIMIT" = 재시도 가능)</param>
    void SendRequest(List<string> base64Images, string textContent,
                     Action<string> onSuccess, Action<string> onError);

    /// <summary>대화 기록 조회 (디버그 UI용)</summary>
    IReadOnlyList<JObject> ConversationHistory { get; }
}
