"""Run a single episode of the AI simulation agent."""

import argparse
import logging
import sys
from pathlib import Path

# Add project root to path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from src.agent.graph import build_graph
from src.config import settings
from src.data.logger import EpisodeLogger
from src.scenes import get_scene


def main():
    parser = argparse.ArgumentParser(description="Run AI simulation agent")
    parser.add_argument(
        "--command", "-c", default=None, help="Command for the agent"
    )
    parser.add_argument(
        "--scene", "-s", default=None, help="Scene name (default from .env)"
    )
    parser.add_argument("--no-save", action="store_true", help="Don't save episode data")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(name)s] %(levelname)s: %(message)s",
    )
    logger = logging.getLogger("run_agent")

    scene_name = args.scene or settings.default_scene
    scene = get_scene(scene_name)
    command = args.command or scene.default_commands[0]

    logger.info(f"Scene: {scene.display_name}")
    logger.info(f"Model: {settings.llm_provider}/{settings.model_name}")
    logger.info(f"Unity server: {settings.unity_server_url}")
    logger.info(f"Command: {command}")

    graph = build_graph()
    episode_id = EpisodeLogger.generate_episode_id()

    initial_state = {
        "scene_name": scene_name,
        "episode_id": episode_id,
        "command": command,
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
            command=command,
            episode_log=result.get("episode_log", []),
            success=success,
            done_reason=done_reason,
        )
        logger.info(f"Episode saved to {ep_dir}")


if __name__ == "__main__":
    main()
