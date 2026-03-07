"""Oracle runner: execute oracle episodes and collect training data."""

import random
import time

from src.data.logger import EpisodeLogger
from src.oracle.strategy import compute_next_action, select_target_ball
from src.unity.client import UnitySimClient


def run_oracle_episode(
    client: UnitySimClient,
    command: str,
    noise_level: float = 0.0,
    max_steps: int = 50,
) -> tuple[list[dict], bool]:
    """Run one oracle episode.

    Returns (episode_log, success).
    """
    obs = client.reset()
    world = client.world_state()

    target_ball = select_target_ball(world, command)
    if target_ball is None:
        return [], False

    phase = "align_lr"
    episode_log = []
    step = 0

    while step < max_steps and phase != "finished":
        # Get fresh world state
        world = client.world_state()

        # Compute next action
        action, next_phase = compute_next_action(world, phase, target_ball, noise_level)

        # Log this step (screenshot from current obs)
        episode_log.append({
            "step": step,
            "camera_angle": obs.get("camera_angle", 0.0),
            "screenshot_base64": obs.get("screenshot_base64", ""),
            "actions": [action],
            "reasoning": action.get("reasoning", ""),
        })

        # Execute action
        obs = client.step(action)
        phase = next_phase
        step += 1

    success = phase == "finished"
    return episode_log, success


def collect_oracle_episodes(
    num_episodes: int,
    unity_url: str = "http://localhost:8765",
    data_dir: str = "data",
    commands: list[str] | None = None,
    noise_level: float = 0.0,
    delay: float = 1.0,
    on_progress: callable = None,
) -> list[dict]:
    """Collect multiple oracle episodes.

    Args:
        num_episodes: Number of episodes to collect.
        unity_url: Unity server URL.
        data_dir: Base data directory.
        commands: List of commands to randomly choose from.
        noise_level: Noise level for duration (0 = perfect).
        delay: Delay between episodes in seconds.
        on_progress: Optional callback(i, num_episodes, episode_id, success).

    Returns:
        List of result dicts with episode_id, command, success.
    """
    if commands is None:
        commands = ["빨간 공을 집어줘", "파란 공을 집어줘", "초록 공을 집어줘"]

    client = UnitySimClient(base_url=unity_url)
    logger = EpisodeLogger(data_dir=f"{data_dir}/episodes")
    results = []

    for i in range(num_episodes):
        command = random.choice(commands)
        episode_id = EpisodeLogger.generate_episode_id()

        episode_log, success = run_oracle_episode(
            client, command, noise_level=noise_level,
        )

        done_reason = "oracle: 공을 성공적으로 집었습니다" if success else "oracle: 실패"

        logger.save_episode(
            episode_id=episode_id,
            command=command,
            episode_log=episode_log,
            success=success,
            done_reason=done_reason,
        )

        result = {"episode_id": episode_id, "command": command, "success": success}
        results.append(result)

        if on_progress:
            on_progress(i + 1, num_episodes, episode_id, success)

        if i < num_episodes - 1 and delay > 0:
            time.sleep(delay)

    return results


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="Oracle data collection")
    parser.add_argument("-n", "--num-episodes", type=int, default=10)
    parser.add_argument("--url", default="http://localhost:8765")
    parser.add_argument("--data-dir", default="data")
    parser.add_argument("--noise", type=float, default=0.0)
    parser.add_argument("--delay", type=float, default=1.0)
    args = parser.parse_args()

    def log_progress(i, total, ep_id, success):
        status = "OK" if success else "FAIL"
        print(f"[{i}/{total}] {ep_id} - {status}")

    results = collect_oracle_episodes(
        num_episodes=args.num_episodes,
        unity_url=args.url,
        data_dir=args.data_dir,
        noise_level=args.noise,
        delay=args.delay,
        on_progress=log_progress,
    )

    success_count = sum(1 for r in results if r["success"])
    print(f"\nComplete: {success_count}/{len(results)} successful")
