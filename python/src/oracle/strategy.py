"""Oracle strategy: coordinate-based optimal action computation.

Uses Unity world state (ball/claw positions, camera basis vectors) to compute
optimal camera-relative actions following the 2-axis alignment strategy.
Generates multi-action sequences with memo and natural language reasoning
that match real AI agent output patterns.
"""

import random

ALIGN_THRESHOLD = 0.05  # units below which alignment is considered done


def select_target_ball(world_state: dict, command: str) -> dict | None:
    """Select target ball from world state based on command text."""
    balls = world_state.get("balls", [])
    if not balls:
        return None

    # Match by color keyword in command
    color_map = {
        "빨강": "빨강", "빨간": "빨강", "red": "빨강",
        "파랑": "파랑", "파란": "파랑", "blue": "파랑",
        "초록": "초록", "초록색": "초록", "green": "초록",
    }

    for keyword, color in color_map.items():
        if keyword in command.lower():
            for ball in balls:
                if color in ball.get("name", ""):
                    return ball

    # Default: first ball
    return balls[0]


def _project_delta(world_state: dict, target_ball: dict) -> tuple[float, float]:
    """Project world delta onto camera-relative axes.

    Returns (delta_right, delta_forward) where:
      - positive delta_right means ball is to the right of claw
      - positive delta_forward means ball is in the forward direction
    """
    claw = world_state["claw"]
    dx = target_ball["x"] - claw["x"]
    dz = target_ball["z"] - claw["z"]

    cam_right = world_state["cam_right"]
    cam_fwd = world_state["cam_forward"]

    delta_right = dx * cam_right["x"] + dz * cam_right["z"]
    delta_forward = dx * cam_fwd["x"] + dz * cam_fwd["z"]

    return delta_right, delta_forward


def _ball_display_name(ball: dict) -> str:
    """Get display name for a ball (e.g. '초록색 공')."""
    name = ball.get("name", "공")
    if "빨강" in name:
        return "빨간색 공"
    elif "파랑" in name:
        return "파란색 공"
    elif "초록" in name:
        return "초록색 공"
    return "공"


def _opposite_dir(direction: str) -> str:
    return "오른쪽" if direction == "left" else "왼쪽"


def _dir_kr(direction: str) -> str:
    return "왼쪽" if direction == "left" else "오른쪽"


# Reasoning templates for variety
_MOVE_REASONING_TEMPLATES = [
    "집게가 {ball}보다 {opposite}에 있으므로 {dir_kr}으로 이동합니다.",
    "화면상에서 집게가 {ball}의 {opposite}에 위치하므로 {dir_kr}으로 이동합니다.",
    "{ball}과 좌우 위치를 맞추기 위해 {dir_kr}으로 이동합니다.",
    "집게를 {ball}의 좌우 위치에 맞추기 위해 {dir_kr}으로 소폭 이동합니다.",
]

_MOVE_FINE_REASONING_TEMPLATES = [
    "{ball}의 중심에 더 정확히 맞추기 위해 약간 더 {dir_kr}으로 이동합니다.",
    "집게가 {ball}보다 살짝 {opposite}에 위치한 것 같아 미세하게 {dir_kr}으로 조정합니다.",
    "중앙에 더 정확히 맞추기 위해 아주 조금 더 {dir_kr}으로 이동합니다.",
]

_MEMO_ALIGN_TEMPLATES = [
    "좌우 정렬 중, 집게를 {dir_kr}으로 이동하여 {ball}과 맞춥니다.",
    "{ball}을 목표로 좌우 정렬 진행 중. {dir_kr}으로 이동.",
    "좌우 정렬 중. 집게를 {dir_kr}으로 더 이동하여 {ball}과 맞춥니다.",
]

_MEMO_ALIGN2_TEMPLATES = [
    "카메라 회전 후 2차 좌우 정렬 중. {dir_kr}으로 이동합니다.",
    "2차 좌우 정렬 중. 집게를 {dir_kr}으로 이동하여 {ball}과 맞춥니다.",
    "카메라 회전 후 2차 좌우 정렬 중입니다. 집게를 {dir_kr}으로 이동하여 {ball} 위에 위치시킵니다.",
]


def compute_next_actions(
    world_state: dict,
    phase: str,
    target_ball: dict,
    noise_level: float = 0.0,
    step: int = 0,
) -> tuple[list[dict], str]:
    """Compute the next optimal actions based on current world state and phase.

    Returns (action_list, next_phase) tuple.
    Actions include memo, move, camera, grip, lower, raise, done.
    """
    move_speed = world_state.get("move_speed", 4.0)
    ball_name = _ball_display_name(target_ball)

    if phase == "align_lr":
        delta_right, _ = _project_delta(world_state, target_ball)

        if abs(delta_right) < ALIGN_THRESHOLD:
            # Aligned — rotate camera
            actions = [
                {"type": "memo", "content": "1차 좌우 정렬 완료, 카메라 회전하여 깊이 확인."},
                {"type": "camera", "direction": "right", "angle": 90,
                 "reasoning": "다른 각도에서 깊이를 확인하기 위해 카메라를 90도 회전합니다."},
            ]
            return actions, "align_lr2"

        direction = "right" if delta_right > 0 else "left"
        duration = abs(delta_right) / move_speed
        if noise_level > 0:
            duration += random.gauss(0, noise_level)
            duration = max(0.02, duration)

        is_fine = duration < 0.4
        dir_kr = _dir_kr(direction)
        opposite = _opposite_dir(direction)
        fmt = {"ball": ball_name, "dir_kr": dir_kr, "opposite": opposite}

        if step == 0:
            memo_content = f"{ball_name}을 목표로 설정. 현재 화면 기준 좌우 정렬을 위해 {dir_kr}으로 이동."
        else:
            memo_content = random.choice(_MEMO_ALIGN_TEMPLATES).format(**fmt)

        if is_fine:
            reasoning = random.choice(_MOVE_FINE_REASONING_TEMPLATES).format(**fmt)
        else:
            reasoning = random.choice(_MOVE_REASONING_TEMPLATES).format(**fmt)

        actions = [
            {"type": "memo", "content": memo_content},
            {"type": "move", "direction": direction,
             "duration": round(duration, 1),
             "reasoning": reasoning},
        ]
        return actions, "align_lr"

    elif phase == "align_lr2":
        delta_right, _ = _project_delta(world_state, target_ball)

        if abs(delta_right) < ALIGN_THRESHOLD:
            # Both axes aligned — pickup sequence
            actions = [
                {"type": "memo", "content": "양방향 정렬 완료. 집기 동작 수행."},
                {"type": "grip", "state": "open",
                 "reasoning": "공을 집기 위해 집게를 엽니다."},
                {"type": "lower",
                 "reasoning": "공을 잡기 위해 집게를 내립니다."},
                {"type": "grip", "state": "close",
                 "reasoning": "공을 고정하기 위해 집게를 닫습니다."},
                {"type": "raise",
                 "reasoning": "공을 들어올립니다."},
                {"type": "done",
                 "reasoning": f"{ball_name}을 성공적으로 집어 올렸으므로 작업을 완료합니다."},
            ]
            return actions, "finished"

        direction = "right" if delta_right > 0 else "left"
        duration = abs(delta_right) / move_speed
        if noise_level > 0:
            duration += random.gauss(0, noise_level)
            duration = max(0.02, duration)

        is_fine = duration < 0.4
        dir_kr = _dir_kr(direction)
        opposite = _opposite_dir(direction)
        fmt = {"ball": ball_name, "dir_kr": dir_kr, "opposite": opposite}

        memo_content = random.choice(_MEMO_ALIGN2_TEMPLATES).format(**fmt)

        if is_fine:
            reasoning = random.choice(_MOVE_FINE_REASONING_TEMPLATES).format(**fmt)
        else:
            reasoning = random.choice(_MOVE_REASONING_TEMPLATES).format(**fmt)

        actions = [
            {"type": "memo", "content": memo_content},
            {"type": "move", "direction": direction,
             "duration": round(duration, 1),
             "reasoning": reasoning},
        ]
        return actions, "align_lr2"

    raise ValueError(f"Unknown phase: {phase}")
