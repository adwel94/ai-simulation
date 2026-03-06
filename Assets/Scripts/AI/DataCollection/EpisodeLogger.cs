using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 에피소드 단위 데이터 수집 로거.
/// AI 에이전트의 매 스텝마다 스크린샷 + 액션 + 컨텍스트를 저장하여
/// VLM 파인튜닝용 학습 데이터셋을 자동 생성.
///
/// 저장 구조:
///   TrainingData/
///   ├── episodes/
///   │   ├── ep_20260304_143022/
///   │   │   ├── metadata.json        ← 에피소드 전체 요약
///   │   │   ├── system_prompt.txt    ← 사용된 시스템 프롬프트
///   │   │   ├── tool_schema.json     ← 도구 정의 스키마
///   │   │   ├── step_001.jpg         ← 스크린샷
///   │   │   ├── step_002.jpg
///   │   │   └── ...
///   │   └── ...
///   ├── dataset.jsonl                ← HuggingFace 학습용 (스텝별 한 줄)
///   └── stats.json                   ← 전체 통계
/// </summary>
public class EpisodeLogger : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("프로젝트 루트 기준 저장 디렉토리")]
    public string dataDirectory = "TrainingData";

    [Tooltip("데이터 수집 활성화")]
    public bool enableLogging = true;

    // Episode state
    string currentEpisodeId;
    string currentEpisodeDir;
    int stepCount;
    List<JObject> stepDataList = new List<JObject>();
    string currentCommand;
    string currentSystemPrompt;
    float episodeStartTime;

    // Public stats
    public int TotalEpisodes { get; private set; }
    public int TotalSteps { get; private set; }
    public string CurrentEpisodeId => currentEpisodeId;
    public bool IsLogging => enableLogging && !string.IsNullOrEmpty(currentEpisodeId);

    string BasePath => Path.Combine(Application.dataPath, "..", dataDirectory);
    string EpisodesPath => Path.Combine(BasePath, "episodes");

    void Awake()
    {
        LoadStats();

        if (enableLogging)
        {
            Debug.Log($"<color=yellow>[EpisodeLogger]</color> 데이터 수집 활성화. 저장 경로: {BasePath}");
            Debug.Log($"<color=yellow>[EpisodeLogger]</color> 누적 통계: {TotalEpisodes}개 에피소드, {TotalSteps}개 스텝");
        }
    }

    /// <summary>
    /// 새 에피소드 시작. 디렉토리 생성 및 메타데이터 초기화.
    /// </summary>
    /// <param name="command">사용자 명령 (예: "빨간 공을 집어")</param>
    /// <param name="systemPrompt">현재 시스템 프롬프트</param>
    /// <param name="toolSchema">도구 레지스트리 스키마 (JSON)</param>
    public void StartEpisode(string command, string systemPrompt, string toolSchema = null)
    {
        if (!enableLogging) return;

        currentEpisodeId = $"ep_{DateTime.Now:yyyyMMdd_HHmmss}";
        currentEpisodeDir = Path.Combine(EpisodesPath, currentEpisodeId);
        Directory.CreateDirectory(currentEpisodeDir);

        stepCount = 0;
        stepDataList.Clear();
        currentCommand = command;
        currentSystemPrompt = systemPrompt;
        episodeStartTime = Time.realtimeSinceStartup;

        // 시스템 프롬프트 저장
        File.WriteAllText(
            Path.Combine(currentEpisodeDir, "system_prompt.txt"),
            systemPrompt
        );

        // 도구 스키마 저장 (있으면)
        if (!string.IsNullOrEmpty(toolSchema))
        {
            File.WriteAllText(
                Path.Combine(currentEpisodeDir, "tool_schema.json"),
                toolSchema
            );
        }

        Debug.Log($"<color=yellow>[EpisodeLogger]</color> ▶ 에피소드 시작: {currentEpisodeId}");
        Debug.Log($"<color=yellow>[EpisodeLogger]</color>   명령: {command}");
        Debug.Log($"<color=yellow>[EpisodeLogger]</color>   저장 경로: {currentEpisodeDir}");
    }

    /// <summary>
    /// 스텝 데이터 기록. 스크린샷 + 입출력 텍스트 + 파싱된 액션 저장.
    /// </summary>
    /// <param name="screenshotJpg">JPEG 바이트 배열 (스크린샷)</param>
    /// <param name="sentText">모델에 보낸 텍스트</param>
    /// <param name="modelResponse">모델의 원본 응답</param>
    /// <param name="action">파싱된 액션</param>
    /// <param name="cameraAngle">현재 카메라 각도</param>
    public void LogStep(byte[] screenshotJpg, string sentText, string modelResponse,
                        ClawAction action, float cameraAngle)
    {
        if (!enableLogging || string.IsNullOrEmpty(currentEpisodeId)) return;

        stepCount++;

        // 스크린샷 저장
        string imgFilename = $"step_{stepCount:D3}.jpg";
        string imgPath = Path.Combine(currentEpisodeDir, imgFilename);

        try
        {
            File.WriteAllBytes(imgPath, screenshotJpg);
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>[EpisodeLogger]</color> 스크린샷 저장 실패: {e.Message}");
            return;
        }

        // 스텝 데이터 구성
        var actionData = new JObject
        {
            ["type"] = action.type ?? "",
            ["reasoning"] = action.reasoning ?? "",
        };

        // 타입별 추가 필드
        if (!string.IsNullOrEmpty(action.direction))
            actionData["direction"] = action.direction;
        if (!string.IsNullOrEmpty(action.state))
            actionData["state"] = action.state;
        if (action.type == "move" || action.type == "lower" || action.type == "raise" || action.type == "wait")
            actionData["duration"] = action.duration;
        if (action.type == "camera")
            actionData["angle"] = action.angle;

        var stepData = new JObject
        {
            ["step"] = stepCount,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
            ["camera_angle"] = Math.Round(cameraAngle, 1),
            ["screenshot"] = imgFilename,
            ["input_text"] = sentText,
            ["model_response"] = modelResponse,
            ["action"] = actionData
        };

        stepDataList.Add(stepData);

        Debug.Log($"<color=yellow>[EpisodeLogger]</color>   Step {stepCount} 기록 ({imgFilename}, {screenshotJpg.Length / 1024}KB)");
    }

    /// <summary>
    /// 에피소드 종료. 메타데이터 저장 및 JSONL 데이터셋 추가.
    /// </summary>
    /// <param name="success">성공 여부</param>
    /// <param name="finalReason">종료 사유</param>
    public void EndEpisode(bool success, string finalReason = "")
    {
        if (!enableLogging || string.IsNullOrEmpty(currentEpisodeId)) return;

        float totalTime = Time.realtimeSinceStartup - episodeStartTime;

        // 에피소드 메타데이터 저장
        var metadata = new JObject
        {
            ["episode_id"] = currentEpisodeId,
            ["command"] = currentCommand,
            ["success"] = success,
            ["final_reason"] = finalReason,
            ["total_steps"] = stepCount,
            ["total_time_seconds"] = Math.Round(totalTime, 1),
            ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["steps"] = JArray.FromObject(stepDataList)
        };

        string metadataPath = Path.Combine(currentEpisodeDir, "metadata.json");

        try
        {
            File.WriteAllText(metadataPath, metadata.ToString(Formatting.Indented));
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>[EpisodeLogger]</color> 메타데이터 저장 실패: {e.Message}");
        }

        // HuggingFace JSONL 데이터셋에 추가
        AppendToDataset(metadata);

        // 통계 업데이트
        TotalEpisodes++;
        TotalSteps += stepCount;
        SaveStats();

        Debug.Log($"<color=yellow>[EpisodeLogger]</color> ■ 에피소드 종료: {currentEpisodeId}");
        Debug.Log($"<color=yellow>[EpisodeLogger]</color>   결과: {(success ? "성공" : "실패")}, {stepCount}스텝, {totalTime:F1}초");
        Debug.Log($"<color=yellow>[EpisodeLogger]</color>   누적: {TotalEpisodes}개 에피소드, {TotalSteps}개 스텝");

        // Reset state
        currentEpisodeId = null;
        currentEpisodeDir = null;
        stepDataList.Clear();
    }

    /// <summary>
    /// HuggingFace VLM 학습용 JSONL 형식으로 추가.
    /// 각 스텝이 하나의 학습 샘플 (이미지 경로 + 입력 텍스트 → 출력 액션).
    /// </summary>
    void AppendToDataset(JObject episodeMetadata)
    {
        string datasetPath = Path.Combine(BasePath, "dataset.jsonl");

        var steps = episodeMetadata["steps"] as JArray;
        if (steps == null || steps.Count == 0) return;

        string episodeId = episodeMetadata["episode_id"]?.ToString() ?? "";
        bool episodeSuccess = episodeMetadata["success"]?.Value<bool>() ?? false;

        try
        {
            using (var writer = new StreamWriter(datasetPath, append: true))
            {
                foreach (var step in steps)
                {
                    var entry = new JObject
                    {
                        ["episode_id"] = episodeId,
                        ["command"] = episodeMetadata["command"],
                        ["episode_success"] = episodeSuccess,
                        ["step"] = step["step"],
                        ["total_steps"] = episodeMetadata["total_steps"],
                        ["image_path"] = $"episodes/{episodeId}/{step["screenshot"]}",
                        ["camera_angle"] = step["camera_angle"],
                        ["input_text"] = step["input_text"],
                        ["output"] = step["model_response"],
                        ["action_type"] = step["action"]?["type"]
                    };

                    writer.WriteLine(entry.ToString(Formatting.None));
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>[EpisodeLogger]</color> JSONL 저장 실패: {e.Message}");
        }
    }

    void LoadStats()
    {
        try
        {
            string statsPath = Path.Combine(BasePath, "stats.json");
            if (File.Exists(statsPath))
            {
                var stats = JObject.Parse(File.ReadAllText(statsPath));
                TotalEpisodes = stats["total_episodes"]?.Value<int>() ?? 0;
                TotalSteps = stats["total_steps"]?.Value<int>() ?? 0;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[EpisodeLogger] 통계 로드 실패: {e.Message}");
        }
    }

    void SaveStats()
    {
        try
        {
            Directory.CreateDirectory(BasePath);
            string statsPath = Path.Combine(BasePath, "stats.json");

            var stats = new JObject
            {
                ["total_episodes"] = TotalEpisodes,
                ["total_steps"] = TotalSteps,
                ["last_updated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            File.WriteAllText(statsPath, stats.ToString(Formatting.Indented));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[EpisodeLogger] 통계 저장 실패: {e.Message}");
        }
    }
}
