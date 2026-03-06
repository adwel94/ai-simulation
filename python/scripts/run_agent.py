"""Run a single episode of the LangGraph claw machine agent."""

import argparse
import logging
import sys
from pathlib import Path

# Add project root to path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from src.agent.graph import build_graph
from src.config import settings
from src.data.logger import EpisodeLogger


def main():
    parser = argparse.ArgumentParser(description="Run claw machine agent")
    parser.add_argument(
        "--command", "-c", default="빨간 공을 집어", help="Command for the agent"
    )
    parser.add_argument("--no-save", action="store_true", help="Don't save episode data")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(name)s] %(levelname)s: %(message)s",
    )
    logger = logging.getLogger("run_agent")

    logger.info(f"Model: {settings.model_name}")
    logger.info(f"Unity server: {settings.unity_server_url}")
    logger.info(f"Command: {args.command}")

    graph = build_graph()
    episode_id = EpisodeLogger.generate_episode_id()

    initial_state = {
        "episode_id": episode_id,
        "command": args.command,
        "step": 0,
        "max_steps": settings.max_steps,
        "done": False,
        "messages": [],
        "episode_log": [],
    }

    logger.info(f"Starting episode {episode_id}...")
    result = graph.invoke(initial_state)

    success = result.get("done", False) and result.get("action", {}).get("type") == "done"
    done_reason = result.get("done_reason", "")
    total_steps = result.get("step", 0)

    logger.info(f"Episode finished: success={success}, steps={total_steps}, reason={done_reason}")

    if not args.no_save:
        ep_logger = EpisodeLogger(data_dir=str(Path(settings.data_dir) / "episodes"))
        ep_dir = ep_logger.save_episode(
            episode_id=episode_id,
            command=args.command,
            episode_log=result.get("episode_log", []),
            success=success,
            done_reason=done_reason,
        )
        logger.info(f"Episode saved to {ep_dir}")


if __name__ == "__main__":
    main()
