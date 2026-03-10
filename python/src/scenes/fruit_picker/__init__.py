"""Fruit Picker scene registration."""

from src.scenes import SceneConfig, register_scene
from src.scenes.fruit_picker.commands import DEFAULT_COMMANDS, adjust_command
from src.scenes.fruit_picker.prompts import SYSTEM_PROMPT, build_step_message
from src.scenes.fruit_picker.tools import ALL_TOOLS, tool_call_to_action

register_scene(
    SceneConfig(
        name="fruit_picker",
        display_name="Fruit Picker",
        tools=ALL_TOOLS,
        system_prompt=SYSTEM_PROMPT,
        default_commands=DEFAULT_COMMANDS,
        tool_call_to_action=tool_call_to_action,
        build_step_message=build_step_message,
        adjust_command=adjust_command,
    )
)
