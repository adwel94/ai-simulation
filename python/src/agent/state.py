from typing import TypedDict, Optional


class ClawState(TypedDict, total=False):
    # Settings (passed from dashboard or CLI)
    unity_url: str
    api_key: str
    model_name: str

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

    # Action decided by LLM
    action: Optional[dict]

    # LLM raw response text (for dashboard display)
    llm_response: str

    # Conversation messages (LangChain message objects)
    messages: list

    # Episode log entries for data collection
    episode_log: list
