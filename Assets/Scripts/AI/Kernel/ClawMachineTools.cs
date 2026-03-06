using System.Collections.Generic;

/// <summary>
/// Semantic Kernel 플러그인 패턴: 크레인 게임 도구 정의.
/// 모든 도구를 구조화하여 한 곳에서 관리.
/// 새로운 도구 추가 시 이 클래스에만 추가하면 시스템 프롬프트에 자동 반영.
/// </summary>
public static class ClawMachineTools
{
    /// <summary>
    /// 기본 크레인 게임 도구 레지스트리 생성
    /// </summary>
    public static ToolRegistry CreateDefaultRegistry()
    {
        var registry = new ToolRegistry();

        // ── 수평 이동 ──
        registry.Register(new ToolDefinition
        {
            name = "move",
            description = "집게를 수평 이동 (left, right, forward, backward 중 택1)",
            parameters = new List<ToolParam>
            {
                new ToolParam("direction", ToolParamType.String, "이동 방향")
                    .WithAllowed("left", "right", "forward", "backward"),
                new ToolParam("duration", ToolParamType.Float, "이동 시간(초)")
                    .WithRange(0.1f, 5f)
            },
            jsonFormat = "수평 이동:\n{\"type\": \"move\", \"reasoning\": \"간단한 설명\", \"direction\": \"left\", \"duration\": 0.3}",
            formatNote = "direction은 left, right, forward, backward 중 하나"
        });

        // ── 집게 내리기 ──
        registry.Register(new ToolDefinition
        {
            name = "lower",
            description = "집게를 아래로 내림",
            parameters = new List<ToolParam>
            {
                new ToolParam("duration", ToolParamType.Float, "내리는 시간(초)")
                    .WithRange(0.1f, 5f)
            },
            jsonFormat = "집게 내리기:\n{\"type\": \"lower\", \"reasoning\": \"간단한 설명\", \"duration\": 0.5}"
        });

        // ── 집게 올리기 ──
        registry.Register(new ToolDefinition
        {
            name = "raise",
            description = "집게를 위로 올림",
            parameters = new List<ToolParam>
            {
                new ToolParam("duration", ToolParamType.Float, "올리는 시간(초)")
                    .WithRange(0.1f, 5f)
            },
            jsonFormat = "집게 올리기:\n{\"type\": \"raise\", \"reasoning\": \"간단한 설명\", \"duration\": 0.5}"
        });

        // ── 집게 제어 ──
        registry.Register(new ToolDefinition
        {
            name = "grip",
            description = "집게 손가락 열기 또는 닫기",
            parameters = new List<ToolParam>
            {
                new ToolParam("state", ToolParamType.String, "열기/닫기")
                    .WithAllowed("open", "close")
            },
            jsonFormat = "집게 제어:\n{\"type\": \"grip\", \"reasoning\": \"간단한 설명\", \"state\": \"open\"}\n또는\n{\"type\": \"grip\", \"reasoning\": \"간단한 설명\", \"state\": \"close\"}"
        });

        // ── 카메라 회전 ──
        registry.Register(new ToolDefinition
        {
            name = "camera",
            description = "카메라를 좌우로 회전시켜 다른 각도에서 관찰",
            parameters = new List<ToolParam>
            {
                new ToolParam("direction", ToolParamType.String, "회전 방향")
                    .WithAllowed("left", "right"),
                new ToolParam("angle", ToolParamType.Float, "회전 각도(도)")
                    .WithRange(10f, 90f)
            },
            jsonFormat = "카메라 회전 (카메라를 씬 중심 기준으로 궤도 회전):\n{\"type\": \"camera\", \"reasoning\": \"간단한 설명\", \"direction\": \"left\", \"angle\": 45}",
            formatNote = "direction은 left(반시계) 또는 right(시계), angle은 10~90도"
        });

        // ── 대기 ──
        registry.Register(new ToolDefinition
        {
            name = "wait",
            description = "대기하면서 관찰",
            parameters = new List<ToolParam>
            {
                new ToolParam("duration", ToolParamType.Float, "대기 시간(초)")
                    .WithRange(0.1f, 3f)
            },
            jsonFormat = "대기 및 관찰:\n{\"type\": \"wait\", \"reasoning\": \"간단한 설명\", \"duration\": 0.3}"
        });

        // ── 작업 완료 ──
        registry.Register(new ToolDefinition
        {
            name = "done",
            description = "작업 완료 선언",
            parameters = new List<ToolParam>(),
            jsonFormat = "작업 완료:\n{\"type\": \"done\", \"reasoning\": \"작업이 성공적으로 완료됨\"}"
        });

        // ── 작업 실패 ──
        registry.Register(new ToolDefinition
        {
            name = "error",
            description = "작업 실패 선언",
            parameters = new List<ToolParam>(),
            jsonFormat = "작업 실패:\n{\"type\": \"error\", \"reasoning\": \"작업을 완료할 수 없는 이유 설명\"}"
        });

        return registry;
    }
}
