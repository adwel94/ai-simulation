"""Batch data collection: run multiple episodes and save results."""

import argparse
import logging
import random
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from src.agent.graph import build_graph
from src.config import settings
from src.data.logger import EpisodeLogger
from src.scenes import get_scene


def main():
    parser = argparse.ArgumentParser(description="Collect training data")
    parser.add_argument(
        "--episodes", "-n", type=int, default=10, help="Number of episodes"
    )
    parser.add_argument(
        "--scene", "-s", default=None, help="Scene name (default from .env)"
    )
    parser.add_argument(
        "--delay", type=float, default=2.0, help="Delay between episodes (seconds)"
    )
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(name)s] %(levelname)s: %(message)s",
    )
    logger = logging.getLogger("collect_data")

    scene_name = args.scene or settings.default_scene
    scene = get_scene(scene_name)
    commands = scene.default_commands

    logger.info(f"Scene: {scene.display_name}, Commands: {len(commands)}")

    graph = build_graph()
    ep_logger = EpisodeLogger(data_dir=str(Path(settings.data_dir) / "episodes"))

    successes = 0
    for i in range(args.episodes):
        command = random.choice(commands)
        episode_id = EpisodeLogger.generate_episode_id()

        logger.info(f"[{i+1}/{args.episodes}] Episode {episode_id}: {command}")

        try:
            result = graph.invoke(
                {
                    "scene_name": scene_name,
                    "episode_id": episode_id,
                    "command": command,
                    "step": 0,
                    "max_steps": settings.max_steps,
                    "done": False,
                    "messages": [],
                    "episode_log": [],
                }
            )

            success = (
                result.get("done", False)
                and result.get("action", {}).get("type") == "done"
            )
            if success:
                successes += 1

            ep_logger.save_episode(
                episode_id=episode_id,
                command=command,
                episode_log=result.get("episode_log", []),
                success=success,
                done_reason=result.get("done_reason", ""),
            )

            logger.info(
                f"  -> {'SUCCESS' if success else 'FAIL'} in {result.get('step', 0)} steps"
            )

        except Exception as e:
            logger.error(f"  -> ERROR: {e}")

        if i < args.episodes - 1:
            time.sleep(args.delay)

    logger.info(f"Done: {successes}/{args.episodes} successful episodes")


if __name__ == "__main__":
    main()
