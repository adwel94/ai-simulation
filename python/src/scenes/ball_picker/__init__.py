"""Ball Picker scene registration."""

from src.scenes import SceneConfig, register_scene
from src.scenes.ball_picker.commands import DEFAULT_COMMANDS
from src.scenes.ball_picker.prompts import SYSTEM_PROMPT, build_step_message
from src.scenes.ball_picker.tools import ALL_TOOLS, tool_call_to_action

register_scene(
    SceneConfig(
        name="ball_picker",
        display_name="Ball Picker",
        tools=ALL_TOOLS,
        system_prompt=SYSTEM_PROMPT,
        default_commands=DEFAULT_COMMANDS,
        tool_call_to_action=tool_call_to_action,
        build_step_message=build_step_message,
    )
)
