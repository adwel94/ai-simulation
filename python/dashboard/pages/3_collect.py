"""Batch data collection page."""

import random
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar
from src.agent.graph import build_graph
from src.data.logger import EpisodeLogger
from src.data.converter import convert_all_episodes
from src.scenes import get_scene

config = setup_sidebar()
scene = get_scene(config["scene_name"])

st.title(f"Batch Collect — {scene.display_name}")

# Settings
col1, col2 = st.columns(2)
with col1:
    num_episodes = st.number_input("Episodes", min_value=1, max_value=500, value=10)
    delay = st.number_input("Delay between episodes (sec)", min_value=0.0, max_value=30.0, value=2.0, step=0.5)
with col2:
    commands_text = st.text_area(
        "Commands (one per line)",
        value="\n".join(scene.default_commands),
        height=150,
    )

commands = [c.strip() for c in commands_text.strip().split("\n") if c.strip()]

if not commands:
    st.warning("Enter at least one command.")

st.divider()

col_start, col_convert = st.columns(2)

with col_start:
    start_clicked = st.button(
        "Start Collection",
        use_container_width=True,
        disabled=not commands,
    )

with col_convert:
    convert_clicked = st.button("Convert Dataset", use_container_width=True)

if convert_clicked:
    episodes_dir = str(Path(config["data_dir"]) / "episodes")
    output_dir = str(Path(config["data_dir"]) / "dataset")
    train_count, val_count = convert_all_episodes(
        episodes_dir=episodes_dir,
        output_dir=output_dir,
    )
    if train_count + val_count > 0:
        st.success(f"Converted: train {train_count}, val {val_count} -> {output_dir}/")
    else:
        st.warning("No successful episodes to convert.")

if start_clicked and commands:
    graph = build_graph()
    ep_logger = EpisodeLogger(data_dir=str(Path(config["data_dir"]) / "episodes"))

    progress_bar = st.progress(0)
    status_text = st.empty()
    log_container = st.container()

    successes = 0
    results = []

    for i in range(num_episodes):
        command = random.choice(commands)
        episode_id = EpisodeLogger.generate_episode_id()

        status_text.markdown(f"**[{i+1}/{num_episodes}]** Episode `{episode_id}` — _{command}_")

        try:
            result = graph.invoke(
                {
                    "unity_url": config["unity_url"],
                    "scene_name": config["scene_name"],
                    "episode_id": episode_id,
                    "command": command,
                    "step": 0,
                    "max_steps": config["max_steps"],
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

            icon = "O" if success else "X"
            results.append(f"{icon} `{episode_id}` — {command} ({result.get('step', 0)} steps)")

        except Exception as e:
            results.append(f"! `{episode_id}` — Error: {e}")

        progress_bar.progress((i + 1) / num_episodes)

        with log_container:
            for r in results:
                st.markdown(r)

        if i < num_episodes - 1:
            time.sleep(delay)

    # Summary
    progress_bar.progress(1.0)
    status_text.empty()

    st.divider()
    st.subheader("Collection Complete")
    col_a, col_b = st.columns(2)
    col_a.metric("Success", f"{successes}/{num_episodes}")
    col_b.metric("Rate", f"{successes/num_episodes*100:.0f}%")
