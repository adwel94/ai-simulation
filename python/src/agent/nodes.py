import logging

from langchain_core.messages import HumanMessage, SystemMessage, ToolMessage

import copy

from src.agent.state import ClawState
from src.config import settings
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
    """Fetch observation from Unity. On first step, reset the episode."""
    if state.get("step", 0) == 0:
        logger.info("Resetting episode...")
        client = _get_client(state)
        obs = client.reset()
        scene = _get_scene(state)
        scene_name = _get_scene_name(state)
        prompt = scene_store.get_prompt(scene_name, scene.system_prompt)
        max_steps = state.get("max_steps") or obs.get("max_steps", settings.max_steps)
        return {
            "screenshot_base64": obs["screenshot_base64"],
            "camera_angle": obs.get("camera_angle", 0.0),
            "step": obs.get("step", 0),
            "max_steps": max_steps,
            "done": False,
            "messages": [SystemMessage(content=prompt)],
            "episode_log": [],
        }
    # On subsequent steps, observation was already set by the act node
    return {}


def think(state: ClawState) -> dict:
    """Send screenshot + context to LLM with bound tools, extract tool call."""
    scene = _get_scene(state)
    scene_name = _get_scene_name(state)

    llm = create_llm()
    tools = _apply_tool_overrides(scene.tools, scene_name)
    llm_with_tools = llm.bind_tools(tools)

    step_text = scene.build_step_message(
        command=state["command"],
        step=state["step"],
        max_steps=state["max_steps"],
        camera_angle=state["camera_angle"],
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

    # Strip old images from history to save tokens (keep only last 2 images)
    messages = _strip_old_images(state.get("messages", []), keep_last=2)
    messages.append(user_message)

    logger.info(f"Step {state['step']}: Sending to LLM ({settings.model_name}) with tools...")
    response = llm_with_tools.invoke(messages)

    # Extract tool call from response
    if response.tool_calls:
        tool_call = response.tool_calls[0]
        action = scene.tool_call_to_action(tool_call)
        logger.info(f"Step {state['step']}: Tool call: {tool_call['name']}({tool_call['args']})")
    else:
        # Fallback: LLM responded with text instead of tool call
        raw_text = response.content
        if isinstance(raw_text, list):
            raw_text = "".join(
                part if isinstance(part, str) else part.get("text", "")
                for part in raw_text
            )
        logger.warning(f"Step {state['step']}: No tool call, raw text: {raw_text[:200]}")
        action = {"type": "error", "reasoning": f"No tool call: {raw_text[:100]}"}

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
        tc = response.tool_calls[0]
        llm_response = f"Tool: {tc['name']}\nArgs: {tc['args']}"
    else:
        llm_response = reasoning or ""

    # Add AI response to history
    messages.append(response)

    return {
        "action": action,
        "llm_response": llm_response,
        "reasoning": reasoning,
        "messages": messages,
    }


def act(state: ClawState) -> dict:
    """Execute action in Unity and update observation."""
    action = state["action"]
    logger.info(f"Step {state['step']}: Executing {action.get('type', '?')}")

    client = _get_client(state)
    obs = client.step(action)

    # Log step data
    log_entry = {
        "step": state["step"],
        "camera_angle": state["camera_angle"],
        "action": action,
        "reasoning": state.get("reasoning", ""),
        "llm_response": state.get("llm_response", ""),
        "screenshot_base64": state["screenshot_base64"],
    }
    episode_log = list(state.get("episode_log", []))
    episode_log.append(log_entry)

    new_step = obs.get("step", state["step"] + 1)
    is_done = obs.get("done", False) or action.get("type") == "done"

    # Add ToolMessage to conversation history so LLM sees the result
    messages = list(state.get("messages", []))
    last_ai_msg = messages[-1] if messages else None
    if last_ai_msg and hasattr(last_ai_msg, "tool_calls") and last_ai_msg.tool_calls:
        tool_call_id = last_ai_msg.tool_calls[0].get("id", "")
        result_text = "액션 실행 완료. 새 스크린샷이 다음 메시지에 첨부됩니다."
        if is_done:
            result_text = "에피소드 완료."
        messages.append(
            ToolMessage(content=result_text, tool_call_id=tool_call_id)
        )

    return {
        "screenshot_base64": obs.get("screenshot_base64", ""),
        "camera_angle": obs.get("camera_angle", state["camera_angle"]),
        "step": new_step,
        "done": is_done,
        "done_reason": action.get("reasoning", "") if is_done else "",
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


def _strip_old_images(messages: list, keep_last: int = 2) -> list:
    """Remove images from older messages to save tokens."""
    result = []
    images_seen = 0
    for msg in reversed(messages):
        if isinstance(msg, HumanMessage) and isinstance(msg.content, list):
            has_image = any(
                isinstance(p, dict) and p.get("type") == "image_url"
                for p in msg.content
            )
            if has_image:
                images_seen += 1
                if images_seen > keep_last:
                    new_content = []
                    for part in msg.content:
                        if isinstance(part, dict) and part.get("type") == "image_url":
                            new_content.append(
                                {"type": "text", "text": "[이전 스크린샷]"}
                            )
                        else:
                            new_content.append(part)
                    msg = HumanMessage(content=new_content)
        result.append(msg)

    result.reverse()
    return result
