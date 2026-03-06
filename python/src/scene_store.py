"""Local JSON persistence for scene config overrides (prompts, tool descriptions)."""

import json
from pathlib import Path


class SceneStore:
    def __init__(self, config_dir: str = "data/scene_config"):
        self.config_dir = Path(config_dir)
        self.config_dir.mkdir(parents=True, exist_ok=True)

    def _path(self, scene_name: str) -> Path:
        return self.config_dir / f"{scene_name}.json"

    def load(self, scene_name: str) -> dict:
        path = self._path(scene_name)
        if path.exists():
            return json.loads(path.read_text(encoding="utf-8"))
        return {}

    def save(self, scene_name: str, data: dict):
        path = self._path(scene_name)
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")

    def get_prompt(self, scene_name: str, default: str) -> str:
        data = self.load(scene_name)
        return data.get("system_prompt", "") or default

    def get_tool_overrides(self, scene_name: str) -> dict[str, dict]:
        data = self.load(scene_name)
        return data.get("tool_overrides", {})


scene_store = SceneStore()
