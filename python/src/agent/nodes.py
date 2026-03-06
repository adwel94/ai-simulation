import json
import logging

from langchain_core.messages import HumanMessage, SystemMessage

from src.agent.prompts import SYSTEM_PROMPT, build_step_message
from src.agent.state import ClawState
from src.config import settings
from src.unity.client import UnitySimClient

logger = logging.getLogger(__name__)


def _get_client(state: ClawState) -> UnitySimClient:
    url = state.get("unity_url") or settings.unity_server_url
    return UnitySimClient(base_url=url)


def observe(state: ClawState) -> dict:
    """Fetch observation from Unity. On first step, reset the episode."""
    if state.get("step", 0) == 0:
        logger.info("Resetting episode...")
        client = _get_client(state)
        obs = client.reset()
        max_steps = state.get("max_steps") or obs.get("max_steps", settings.max_steps)
        return {
            "screenshot_base64": obs["screenshot_base64"],
            "camera_angle": obs.get("camera_angle", 0.0),
            "step": obs.get("step", 0),
            "max_steps": max_steps,
            "done": False,
            "messages": [SystemMessage(content=SYSTEM_PROMPT)],
            "episode_log": [],
        }
    # On subsequent steps, observation was already set by the act node
    return {}


def think(state: ClawState) -> dict:
    """Send screenshot + context to LLM, parse JSON action."""
    from langchain_google_genai import ChatGoogleGenerativeAI

    api_key = state.get("api_key") or settings.google_api_key
    model_name = state.get("model_name") or settings.model_name

    llm = ChatGoogleGenerativeAI(
        model=model_name,
        google_api_key=api_key,
        temperature=1.0,
    )

    step_text = build_step_message(
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

    logger.info(f"Step {state['step']}: Sending to LLM ({model_name})...")
    response = llm.invoke(messages)
    raw_text = response.content
    if isinstance(raw_text, list):
        raw_text = "".join(
            part if isinstance(part, str) else part.get("text", "")
            for part in raw_text
        )
    logger.info(f"Step {state['step']}: LLM response: {raw_text[:200]}")

    # Parse JSON action from response
    action = _parse_action(raw_text)

    # Add assistant response to history
    messages.append(response)

    return {
        "action": action,
        "llm_response": raw_text,
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
        "llm_response": state.get("llm_response", ""),
        "screenshot_base64": state["screenshot_base64"],
    }
    episode_log = list(state.get("episode_log", []))
    episode_log.append(log_entry)

    new_step = obs.get("step", state["step"] + 1)
    is_done = obs.get("done", False) or action.get("type") == "done"

    return {
        "screenshot_base64": obs.get("screenshot_base64", ""),
        "camera_angle": obs.get("camera_angle", state["camera_angle"]),
        "step": new_step,
        "done": is_done,
        "done_reason": action.get("reasoning", "") if is_done else "",
        "episode_log": episode_log,
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
    # Strip old images
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


def _parse_action(raw_text: str) -> dict:
    """Parse JSON action from LLM response text."""
    text = raw_text.strip()
    # Handle markdown code blocks
    if text.startswith("```"):
        lines = text.split("\n")
        lines = [l for l in lines if not l.startswith("```")]
        text = "\n".join(lines).strip()

    try:
        return json.loads(text)
    except json.JSONDecodeError:
        # Try to find JSON object in text
        start = text.find("{")
        end = text.rfind("}") + 1
        if start >= 0 and end > start:
            try:
                return json.loads(text[start:end])
            except json.JSONDecodeError:
                pass
        logger.warning(f"Failed to parse action JSON: {text[:200]}")
        return {"type": "error", "reasoning": f"Failed to parse: {text[:100]}"}
