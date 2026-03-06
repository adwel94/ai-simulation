import base64
import json
import os
from datetime import datetime
from pathlib import Path


class EpisodeLogger:
    """Saves episode data (screenshots + metadata) for VLM training."""

    def __init__(self, data_dir: str = "data/episodes"):
        self.data_dir = Path(data_dir)
        self.data_dir.mkdir(parents=True, exist_ok=True)

    def save_episode(
        self,
        episode_id: str,
        command: str,
        episode_log: list,
        success: bool,
        done_reason: str = "",
    ) -> Path:
        """Save a complete episode to disk.

        Returns the episode directory path.
        """
        ep_dir = self.data_dir / episode_id
        ep_dir.mkdir(parents=True, exist_ok=True)

        steps = []
        for entry in episode_log:
            step_num = entry["step"]
            filename = f"step_{step_num:03d}.jpg"

            # Save screenshot
            if entry.get("screenshot_base64"):
                img_bytes = base64.b64decode(entry["screenshot_base64"])
                (ep_dir / filename).write_bytes(img_bytes)

            steps.append(
                {
                    "step": step_num,
                    "camera_angle": entry.get("camera_angle", 0.0),
                    "screenshot": filename,
                    "action": entry.get("action", {}),
                    "reasoning": entry.get("reasoning", ""),
                }
            )

        metadata = {
            "episode_id": episode_id,
            "command": command,
            "success": success,
            "final_reason": done_reason,
            "total_steps": len(steps),
            "timestamp": datetime.now().isoformat(),
            "steps": steps,
        }

        (ep_dir / "metadata.json").write_text(
            json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8"
        )

        return ep_dir

    @staticmethod
    def generate_episode_id() -> str:
        return f"ep_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
