import copy
import logging
import time
from pathlib import Path

from langchain_core.messages import HumanMessage, SystemMessage, ToolMessage

from src.agent.state import ClawState
from src.config import settings
from src.data.logger import EpisodeLogger
from src.llm_factory import create_llm
from src.scene_store import scene_store
from src.scenes import get_scene
from src.unity.client import UnitySimClient

logger = logging.getLogger(__name__)


def _get_client(state: ClawState) -> UnitySimClient:
    url = state.get("unity_url") or settings.unity_server_url
    return UnitySimClient(base_url=url)


def _get_scene(state: ClawState):
    scene_name = state.get("scene_name") or settings.default_scene
    return get_scene(scene_name)


def _get_scene_name(state: ClawState) -> str:
    return state.get("scene_name") or settings.default_scene


BALL_HELD_Y_THRESHOLD = 0.1
MAX_DONE_RETRIES = 3


def _verify_ball_held(client: UnitySimClient) -> tuple[bool, str]:
    """world_state로 공이 집혀 올라와 있는지 확인."""
    try:
        world = client.world_state()
        for ball in world.get("objects", world.get("balls", [])):
            if ball.get("y", 0) > BALL_HELD_Y_THRESHOLD:
                return True, ball.get("name", "unknown")
        return False, ""
    except Exception:
        return True, ""  # 오류 시 fail-open (done 허용)


def _apply_tool_overrides(tools: list, scene_name: str) -> list:
    """Apply custom tool descriptions from SceneStore."""
    overrides = scene_store.get_tool_overrides(scene_name)
    if not overrides:
        return tools
    result = []
    for tool in tools:
        if tool.name in overrides and "description" in overrides[tool.name]:
            tool = copy.copy(tool)
            tool.description = overrides[tool.name]["description"]
        result.append(tool)
    return result


def observe(state: ClawState) -> dict:
    """Fetch observation from Unity. step 0: reset+capture, 이후: wait+capture."""
    client = _get_client(state)

    if state.get("step", 0) == 0:
        logger.info("Resetting episode...")
        obs = client.reset()
        scene = _get_scene(state)
        scene_name = _get_scene_name(state)
        prompt = scene_store.get_prompt(scene_name, scene.system_prompt)
        max_steps = state.get("max_steps") or obs.get("max_steps", settings.max_steps)
        return {
            "screenshot_base64": obs["screenshot_base64"],
            "camera_angle": obs.get("camera_angle", 0.0),
            "grip": obs.get("grip", 0.0),
            "step": obs.get("step", 0),
            "max_steps": max_steps,
            "done": False,
            "messages": [SystemMessage(content=prompt)],
            "episode_log": [],
        }

    # 이후 스텝: 이전 액션 완료 대기 + 캡처
    try:
        client.wait_action_complete()
        time.sleep(0.2)
        obs = client.capture()
    except Exception as e:
        logger.warning(f"Observe failed: {e}")
        return {}

    return {
        "screenshot_base64": obs.get("screenshot_base64", ""),
        "camera_angle": obs.get("camera_angle", state["camera_angle"]),
        "grip": obs.get("grip", state.get("grip", 0.0)),
    }


def think(state: ClawState) -> dict:
    """Send screenshot + context to LLM with bound tools, extract tool call."""
    scene = _get_scene(state)
    scene_name = _get_scene_name(state)

    llm = create_llm(
        provider=state.get("llm_provider"),
        model_name=state.get("model_name"),
        base_url=state.get("openai_base_url"),
    )
    tools = _apply_tool_overrides(scene.tools, scene_name)
    llm_with_tools = llm.bind_tools(tools)

    step_text = scene.build_step_message(
        command=state["command"],
        step=state["step"],
        max_steps=state["max_steps"],
        camera_angle=state["camera_angle"],
        memo=state.get("memo", ""),
        grip=state.get("grip", 0.0),
    )

    user_message = HumanMessage(
        content=[
            {
                "type": "image_url",
                "image_url": {
                    "url": f"data:image/jpeg;base64,{state['screenshot_base64']}"
                },
            },
            {"type": "text", "text": step_text},
        ]
    )

    # 슬라이딩 윈도우: SystemMessage + 최근 3턴만 유지
    messages = _trim_history(state.get("messages", []), keep_turns=3)
    messages.append(user_message)

    logger.info(f"Step {state['step']}: Sending to LLM ({settings.model_name}) with tools...")
    response = llm_with_tools.invoke(messages)

    # Extract tool calls from response (multiple allowed)
    # If no tool calls, retry once with a nudge message
    if not response.tool_calls:
        logger.warning(f"Step {state['step']}: No tool call on first attempt, retrying...")
        messages.append(response)
        messages.append(HumanMessage(
            content="반드시 도구를 호출해야 합니다. 위 스크린샷을 보고 다음 행동을 도구로 호출하세요."
        ))
        response = llm_with_tools.invoke(messages)
        # Remove the retry messages so they don't pollute history
        messages.pop()
        messages.pop()

    if response.tool_calls:
        actions = [scene.tool_call_to_action(tc) for tc in response.tool_calls]
        for tc in response.tool_calls:
            logger.info(f"Step {state['step']}: Tool call: {tc['name']}({tc['args']})")
    else:
        # Fallback after retry: LLM still didn't call a tool
        raw_text = response.content
        if isinstance(raw_text, list):
            raw_text = "".join(
                part if isinstance(part, str) else part.get("text", "")
                for part in raw_text
            )
        logger.warning(f"Step {state['step']}: No tool call after retry, raw text: {raw_text[:200]}")
        actions = [{"type": "error", "reasoning": f"No tool call: {raw_text[:100]}"}]

    # Extract reasoning text from response.content
    reasoning = ""
    if response.content:
        if isinstance(response.content, list):
            reasoning = "".join(
                part if isinstance(part, str) else part.get("text", "")
                for part in response.content
            )
        elif isinstance(response.content, str):
            reasoning = response.content

    # Build llm_response string for dashboard display
    llm_response = ""
    if response.tool_calls:
        parts = []
        for tc in response.tool_calls:
            parts.append(f"Tool: {tc['name']}\nArgs: {tc['args']}")
        llm_response = "\n---\n".join(parts)
    else:
        llm_response = reasoning or ""

    # Build debug log: full message history + LLM response
    debug_messages = _serialize_messages_for_debug(messages)
    debug_response = _serialize_messages_for_debug([response])
    debug_log = f"{debug_messages}\n\n--- LLM RESPONSE ---\n\n{debug_response}"

    # Add AI response to history
    messages.append(response)

    return {
        "actions": actions,
        "llm_response": llm_response,
        "reasoning": reasoning,
        "debug_log": debug_log,
        "messages": messages,
    }


def act(state: ClawState) -> dict:
    """Execute actions in Unity sequentially and update observation."""
    actions = state["actions"]
    client = _get_client(state)

    # Log step data
    log_entry = {
        "step": state["step"],
        "camera_angle": state["camera_angle"],
        "actions": actions,
        "reasoning": state.get("reasoning", ""),
        "llm_response": state.get("llm_response", ""),
        "screenshot_base64": state["screenshot_base64"],
        "messages": _serialize_messages_for_log(state.get("messages", [])),
    }
    episode_log = list(state.get("episode_log", []))
    episode_log.append(log_entry)

    # Execute each action sequentially (skip memo actions)
    is_done = False
    memo_text = state.get("memo", "")
    done_attempts = state.get("done_attempts", 0)
    verification_failed = False
    for i, action in enumerate(actions):
        if action.get("type") == "memo":
            memo_text = action.get("content", "")
            logger.info(f"Step {state['step']}: Memo: {memo_text[:100]}")
            continue

        if action.get("type") == "done":
            ball_held, ball_name = _verify_ball_held(client)
            if ball_held or done_attempts >= MAX_DONE_RETRIES:
                client.step(action)
                client.wait_action_complete()
                is_done = True
                if ball_held:
                    logger.info(f"Step {state['step']}: Done verified - '{ball_name}' held")
                else:
                    logger.info(f"Step {state['step']}: Done forced after {done_attempts} retries")
            else:
                done_attempts += 1
                verification_failed = True
                is_done = False
                logger.info(f"Step {state['step']}: Done REJECTED ({done_attempts}/{MAX_DONE_RETRIES}) - no ball held")
            break

        logger.info(f"Step {state['step']}: Executing [{i+1}/{len(actions)}] {action.get('type', '?')}")
        try:
            client.step(action)
            client.wait_action_complete()
        except Exception as e:
            logger.warning(f"Step {state['step']}: Action failed: {e}")
            break

    # Build action type map for ToolMessage content
    action_type_by_name = {}
    for a in actions:
        # Map tool name back from action type
        action_type_by_name[a.get("type", "")] = True

    # Add ToolMessage for each tool_call (LangChain requires one per tool_call)
    messages = list(state.get("messages", []))
    last_ai_msg = messages[-1] if messages else None
    if last_ai_msg and hasattr(last_ai_msg, "tool_calls") and last_ai_msg.tool_calls:
        for tc in last_ai_msg.tool_calls:
            tool_call_id = tc.get("id", "")
            tc_name = tc.get("name", "")
            if tc_name == "memo":
                result_text = f"메모 저장됨: {tc.get('args', {}).get('content', '')[:100]}"
            elif tc_name == "done" and verification_failed:
                result_text = (
                    "물체를 잡지 못했습니다! 모든 물체가 바닥에 있습니다. "
                    "집게 위치를 다시 확인하고 재시도하세요. "
                    "grip open → 위치 조정 → lower → grip close → raise_claw 순서로 다시 시도하세요."
                )
            elif is_done:
                result_text = "에피소드 완료."
            else:
                result_text = "액션 실행 완료."
            messages.append(
                ToolMessage(content=result_text, tool_call_id=tool_call_id)
            )
        # 마지막 ToolMessage에 안내 추가
        if messages and isinstance(messages[-1], ToolMessage) and not is_done:
            messages[-1] = ToolMessage(
                content="모든 액션 실행 완료. 새 스크린샷이 다음 메시지에 첨부됩니다.",
                tool_call_id=messages[-1].tool_call_id,
            )

    # done_reason: 마지막 실행된 액션의 reasoning
    last_action = actions[-1] if actions else {}
    done_reason = last_action.get("reasoning", "") if is_done else ""

    # Per-step incremental save to disk
    data_dir = state.get("data_dir")
    if data_dir:
        try:
            ep_logger = EpisodeLogger(
                data_dir=str(Path(data_dir) / "episodes")
            )
            ep_logger.save_episode(
                episode_id=state["episode_id"],
                command=state["command"],
                episode_log=episode_log,
                success=is_done and last_action.get("type") == "done",
                done_reason=done_reason,
            )
        except Exception as e:
            logger.warning(f"Failed to save episode incrementally: {e}")

    return {
        "step": state["step"] + 1,
        "done": is_done,
        "done_reason": done_reason,
        "done_attempts": done_attempts,
        "memo": memo_text,
        "episode_log": episode_log,
        "messages": messages,
    }


def check_done(state: ClawState) -> str:
    """Conditional edge: route to END or back to observe."""
    if state.get("done", False):
        return "end"
    if state.get("step", 0) >= state.get("max_steps", 50):
        return "end"
    return "continue"


def _serialize_messages_for_debug(messages: list) -> str:
    """Convert LangChain messages to readable debug text (images excluded)."""
    lines = []
    for msg in messages:
        role = msg.__class__.__name__
        if isinstance(msg.content, list):
            parts = []
            for p in msg.content:
                if isinstance(p, dict) and p.get("type") == "image_url":
                    parts.append("[IMAGE]")
                elif isinstance(p, dict):
                    parts.append(p.get("text", str(p)))
                else:
                    parts.append(str(p))
            text = "\n".join(parts)
        else:
            text = str(msg.content)
        if hasattr(msg, "tool_calls") and msg.tool_calls:
            tc_text = "\n".join(f"  → {tc['name']}({tc['args']})" for tc in msg.tool_calls)
            text += f"\n[tool_calls]\n{tc_text}"
        lines.append(f"=== {role} ===\n{text}")
    return "\n\n".join(lines)


def _serialize_messages_for_log(messages: list) -> list:
    """Convert LangChain messages to JSON-serializable list for episode storage."""
    result = []
    for msg in messages:
        entry = {"role": msg.__class__.__name__}
        if isinstance(msg.content, list):
            entry["content"] = [
                "[IMAGE]" if (isinstance(p, dict) and p.get("type") == "image_url") else p
                for p in msg.content
            ]
        else:
            entry["content"] = msg.content
        if hasattr(msg, "tool_calls") and msg.tool_calls:
            entry["tool_calls"] = [{"name": tc["name"], "args": tc["args"]} for tc in msg.tool_calls]
        result.append(entry)
    return result


def _trim_history(messages: list, keep_turns: int = 3) -> list:
    """SystemMessage 유지 + 최근 N턴만 유지. 1턴 = Human+AI+Tool 묶음."""
    if not messages:
        return messages

    system_msgs = []
    rest = []
    for msg in messages:
        if isinstance(msg, SystemMessage):
            system_msgs.append(msg)
        else:
            rest.append(msg)

    # HumanMessage 기준으로 턴 경계 찾기
    turn_starts = []
    for i, msg in enumerate(rest):
        if isinstance(msg, HumanMessage):
            turn_starts.append(i)

    # 최근 keep_turns 턴만 유지
    if len(turn_starts) > keep_turns:
        cut_index = turn_starts[-keep_turns]
        rest = rest[cut_index:]

    return system_msgs + rest
