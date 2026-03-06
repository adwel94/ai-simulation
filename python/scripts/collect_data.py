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

DEFAULT_COMMANDS = [
    "빨간 공을 집어",
    "파란 공을 집어",
    "초록색 공을 집어",
    "노란 공을 집어",
    "아무 공이나 집어",
    "가장 가까운 공을 집어",
]


def main():
    parser = argparse.ArgumentParser(description="Collect training data")
    parser.add_argument(
        "--episodes", "-n", type=int, default=10, help="Number of episodes"
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

    graph = build_graph()
    ep_logger = EpisodeLogger(data_dir=str(Path(settings.data_dir) / "episodes"))

    successes = 0
    for i in range(args.episodes):
        command = random.choice(DEFAULT_COMMANDS)
        episode_id = EpisodeLogger.generate_episode_id()

        logger.info(f"[{i+1}/{args.episodes}] Episode {episode_id}: {command}")

        try:
            result = graph.invoke(
                {
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
