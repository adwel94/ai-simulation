import json
from pathlib import Path

from src.config import settings
from src.scenes import get_scene


def convert_episode_to_sft(episode_dir: Path) -> dict | None:
    """Convert a single episode to Qwen3-VL SFT chat format.

    Returns None if episode was not successful.
    """
    metadata_path = episode_dir / "metadata.json"
    if not metadata_path.exists():
        return None

    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))

    if not metadata.get("success", False):
        return None

    command = metadata["command"]
    steps = metadata["steps"]

    scene_name = metadata.get("scene_name", settings.default_scene)
    scene = get_scene(scene_name)
    messages = [{"role": "system", "content": scene.system_prompt}]

    for i, step_data in enumerate(steps):
        screenshot_path = episode_dir / step_data["screenshot"]
        if not screenshot_path.exists():
            continue

        step_num = step_data["step"]
        total = metadata["total_steps"]
        camera_angle = step_data.get("camera_angle", 0)

        user_content = [
            {"type": "image", "image": f"file://{screenshot_path.as_posix()}"},
            {
                "type": "text",
                "text": (
                    f"[사용자 명령]: {command}\n"
                    f"카메라 각도: {camera_angle:.0f}도\n"
                    f"스텝 {step_num}/{total}. 다음 행동을 결정하세요."
                ),
            },
        ]
        messages.append({"role": "user", "content": user_content})

        action = step_data.get("action", {})
        messages.append(
            {
                "role": "assistant",
                "content": json.dumps(action, ensure_ascii=False),
            }
        )

    return {"messages": messages}


def convert_all_episodes(
    episodes_dir: str = "data/episodes",
    output_dir: str = "data/dataset",
    val_ratio: float = 0.1,
) -> tuple[int, int]:
    """Convert all successful episodes to train/val JSONL files.

    Returns (train_count, val_count).
    """
    episodes_path = Path(episodes_dir)
    output_path = Path(output_dir)
    output_path.mkdir(parents=True, exist_ok=True)

    samples = []
    for ep_dir in sorted(episodes_path.iterdir()):
        if not ep_dir.is_dir():
            continue
        sample = convert_episode_to_sft(ep_dir)
        if sample:
            samples.append(sample)

    if not samples:
        return 0, 0

    # Split train/val
    val_count = max(1, int(len(samples) * val_ratio))
    train_samples = samples[:-val_count]
    val_samples = samples[-val_count:]

    _write_jsonl(output_path / "train.jsonl", train_samples)
    _write_jsonl(output_path / "val.jsonl", val_samples)

    return len(train_samples), len(val_samples)


def _write_jsonl(path: Path, samples: list):
    with open(path, "w", encoding="utf-8") as f:
        for sample in samples:
            f.write(json.dumps(sample, ensure_ascii=False) + "\n")
