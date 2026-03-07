"""Oracle strategy: coordinate-based optimal action computation.

Uses Unity world state (ball/claw positions, camera basis vectors) to compute
optimal camera-relative actions following the 2-axis alignment strategy.
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


def compute_next_action(
    world_state: dict,
    phase: str,
    target_ball: dict,
    noise_level: float = 0.0,
) -> tuple[dict, str]:
    """Compute the next optimal action based on current world state and phase.

    Args:
        world_state: Current world state from Unity /world_state endpoint.
        phase: Current phase of the strategy.
        target_ball: Target ball dict with name, x, y, z.
        noise_level: Standard deviation of noise added to duration (0 = perfect).

    Returns:
        (action_dict, next_phase) tuple.
    """
    move_speed = world_state.get("move_speed", 4.0)

    if phase == "align_lr":
        delta_right, _ = _project_delta(world_state, target_ball)

        if abs(delta_right) < ALIGN_THRESHOLD:
            # Already aligned, move to camera rotation
            return (
                {"type": "camera", "direction": "right", "angle": 90,
                 "reasoning": "1차 좌우 정렬 완료. 카메라 90도 회전하여 깊이 축 확인"},
                "align_lr2",
            )

        direction = "right" if delta_right > 0 else "left"
        duration = abs(delta_right) / move_speed

        if noise_level > 0:
            duration += random.gauss(0, noise_level)
            duration = max(0.02, duration)

        return (
            {"type": "move", "direction": direction,
             "duration": round(duration, 3),
             "reasoning": f"좌우 정렬: {direction}으로 {abs(delta_right):.3f}m 이동"},
            "align_lr",  # stay in same phase to verify after move
        )

    elif phase == "align_lr2":
        delta_right, _ = _project_delta(world_state, target_ball)

        if abs(delta_right) < ALIGN_THRESHOLD:
            # Both axes aligned, proceed to lower
            return (
                {"type": "lower",
                 "reasoning": "2차 좌우 정렬 완료. 집게 내리기"},
                "grip",
            )

        direction = "right" if delta_right > 0 else "left"
        duration = abs(delta_right) / move_speed

        if noise_level > 0:
            duration += random.gauss(0, noise_level)
            duration = max(0.02, duration)

        return (
            {"type": "move", "direction": direction,
             "duration": round(duration, 3),
             "reasoning": f"2차 좌우 정렬: {direction}으로 {abs(delta_right):.3f}m 이동"},
            "align_lr2",
        )

    elif phase == "grip":
        return (
            {"type": "grip", "state": "close",
             "reasoning": "집게 닫기"},
            "raise",
        )

    elif phase == "raise":
        return (
            {"type": "raise",
             "reasoning": "집게 올리기"},
            "done",
        )

    elif phase == "done":
        return (
            {"type": "done",
             "reasoning": "공을 성공적으로 집어 올렸습니다"},
            "finished",
        )

    raise ValueError(f"Unknown phase: {phase}")
