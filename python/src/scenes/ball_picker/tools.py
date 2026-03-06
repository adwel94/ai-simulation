"""LangChain tool definitions for Ball Picker scene."""

from typing import Literal

from langchain_core.tools import tool


@tool
def move(
    direction: Literal["left", "right", "forward", "backward"],
    duration: float,
    reasoning: str,
) -> str:
    """집게를 지정 방향으로 이동합니다.

    Args:
        direction: 이동 방향 (left, right, forward, backward)
        duration: 이동 시간(초). 미세 조정: 0.3~0.8, 큰 이동: 1.0~2.5
        reasoning: 이 행동을 선택한 이유
    """
    return f"move {direction} {duration}s"


@tool
def lower(duration: float, reasoning: str) -> str:
    """집게를 아래로 내립니다.

    Args:
        duration: 내리는 시간(초). 미세 조정: 0.3~0.8, 큰 이동: 1.0~2.5
        reasoning: 이 행동을 선택한 이유
    """
    return f"lower {duration}s"


@tool
def raise_claw(duration: float, reasoning: str) -> str:
    """집게를 위로 올립니다.

    Args:
        duration: 올리는 시간(초). 미세 조정: 0.3~0.8, 큰 이동: 1.0~2.5
        reasoning: 이 행동을 선택한 이유
    """
    return f"raise {duration}s"


@tool
def grip(state: Literal["open", "close"], reasoning: str) -> str:
    """집게를 열거나 닫습니다.

    Args:
        state: open(열기) 또는 close(닫기)
        reasoning: 이 행동을 선택한 이유
    """
    return f"grip {state}"


@tool
def camera(
    direction: Literal["left", "right"],
    angle: float,
    reasoning: str,
) -> str:
    """카메라를 좌/우로 회전하여 다른 각도에서 관찰합니다.
    물체가 가려지거나 위치 파악이 어려울 때 사용하세요.

    Args:
        direction: 회전 방향 (left 또는 right)
        angle: 회전 각도(도)
        reasoning: 이 행동을 선택한 이유
    """
    return f"camera {direction} {angle}deg"


@tool
def wait_action(duration: float, reasoning: str) -> str:
    """지정 시간만큼 대기합니다. 물리 시뮬레이션이 안정되길 기다릴 때 사용합니다.

    Args:
        duration: 대기 시간(초)
        reasoning: 이 행동을 선택한 이유
    """
    return f"wait {duration}s"


@tool
def memo(content: str) -> str:
    """관찰 결과, 계획, 진행 상황을 메모합니다. 다른 도구와 함께 호출할 수 있습니다.
    메모는 매 스텝 표시되므로 현재 상태를 기록하세요.

    Args:
        content: 메모 내용 (예: "좌우 정렬 완료, 카메라 회전하여 전후 확인 필요")
    """
    return content


@tool
def done(reasoning: str) -> str:
    """작업을 완료합니다. 목표를 달성했거나 더 이상 진행할 수 없을 때 호출하세요.

    Args:
        reasoning: 완료 판단 근거
    """
    return "done"


ALL_TOOLS = [move, lower, raise_claw, grip, camera, wait_action, memo, done]

_TOOL_TYPE_MAP = {
    "move": "move",
    "lower": "lower",
    "raise_claw": "raise",
    "grip": "grip",
    "camera": "camera",
    "wait_action": "wait",
    "memo": "memo",
    "done": "done",
}


def tool_call_to_action(tool_call: dict) -> dict:
    """Convert a LangChain tool_call dict to a Unity action dict."""
    name = tool_call["name"]
    args = dict(tool_call.get("args", {}))
    action_type = _TOOL_TYPE_MAP.get(name, name)
    return {"type": action_type, **args}
