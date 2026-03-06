from typing import TypedDict, Optional


class ClawState(TypedDict, total=False):
    # Settings
    unity_url: str
    scene_name: str

    # Episode
    episode_id: str
    command: str
    step: int
    max_steps: int
    done: bool
    done_reason: str

    # Current observation from Unity
    screenshot_base64: str
    camera_angle: float

    # Actions decided by LLM (multiple tool calls per turn)
    actions: list

    # AI memo (persists across steps, shown in each step message)
    memo: str

    # LLM raw response text (for dashboard display)
    llm_response: str

    # LLM reasoning text (content alongside tool calls)
    reasoning: str

    # Conversation messages (LangChain message objects)
    messages: list

    # Episode log entries for data collection
    episode_log: list
