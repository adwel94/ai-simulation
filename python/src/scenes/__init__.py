"""Scene module system. Each scene defines its own tools, prompts, and commands."""

from dataclasses import dataclass, field
from typing import Callable

SCENE_REGISTRY: dict[str, "SceneConfig"] = {}


@dataclass
class SceneConfig:
    name: str
    display_name: str
    tools: list
    system_prompt: str
    default_commands: list[str]
    tool_call_to_action: Callable[[dict], dict]
    build_step_message: Callable[..., str]
    adjust_command: Callable | None = None


def register_scene(config: SceneConfig):
    SCENE_REGISTRY[config.name] = config


def get_scene(name: str) -> SceneConfig:
    if name not in SCENE_REGISTRY:
        _auto_import_scenes()
    if name not in SCENE_REGISTRY:
        available = ", ".join(SCENE_REGISTRY.keys()) or "(none)"
        raise KeyError(f"Unknown scene: '{name}'. Available: {available}")
    return SCENE_REGISTRY[name]


def list_scenes() -> list[str]:
    _auto_import_scenes()
    return list(SCENE_REGISTRY.keys())


def _auto_import_scenes():
    """Import all scene sub-packages to trigger registration."""
    if SCENE_REGISTRY:
        return
    # Import known scenes — add new scenes here
    import src.scenes.ball_picker  # noqa: F401
    import src.scenes.fruit_picker  # noqa: F401
