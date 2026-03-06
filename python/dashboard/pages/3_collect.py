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

config = setup_sidebar()

st.title("📦 배치 데이터 수집")

DEFAULT_COMMANDS = [
    "빨간 공을 집어",
    "파란 공을 집어",
    "초록색 공을 집어",
    "노란 공을 집어",
    "아무 공이나 집어",
    "가장 가까운 공을 집어",
]

# Settings
col1, col2 = st.columns(2)
with col1:
    num_episodes = st.number_input("에피소드 수", min_value=1, max_value=500, value=10)
    delay = st.number_input("에피소드 간 딜레이 (초)", min_value=0.0, max_value=30.0, value=2.0, step=0.5)
with col2:
    commands_text = st.text_area(
        "명령어 목록 (줄바꿈으로 구분)",
        value="\n".join(DEFAULT_COMMANDS),
        height=150,
    )

commands = [c.strip() for c in commands_text.strip().split("\n") if c.strip()]

if not commands:
    st.warning("명령어를 하나 이상 입력하세요.")

st.divider()

# Run collection
if not config["api_key"]:
    st.warning("사이드바에서 Google API Key를 설정하세요.")

col_start, col_convert = st.columns(2)

with col_start:
    start_clicked = st.button(
        "🚀 수집 시작",
        use_container_width=True,
        disabled=not config["api_key"] or not commands,
    )

with col_convert:
    convert_clicked = st.button("🔄 데이터셋 변환", use_container_width=True)

if convert_clicked:
    episodes_dir = str(Path(config["data_dir"]) / "episodes")
    output_dir = str(Path(config["data_dir"]) / "dataset")
    train_count, val_count = convert_all_episodes(
        episodes_dir=episodes_dir,
        output_dir=output_dir,
    )
    if train_count + val_count > 0:
        st.success(f"변환 완료: train {train_count}건, val {val_count}건 → {output_dir}/")
    else:
        st.warning("변환할 성공 에피소드가 없습니다.")

if start_clicked and config["api_key"] and commands:
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

        status_text.markdown(f"**[{i+1}/{num_episodes}]** 에피소드 `{episode_id}` — _{command}_")

        try:
            result = graph.invoke(
                {
                    "unity_url": config["unity_url"],
                    "api_key": config["api_key"],
                    "model_name": config["model_name"],
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

            icon = "✅" if success else "❌"
            results.append(f"{icon} `{episode_id}` — {command} ({result.get('step', 0)} 스텝)")

        except Exception as e:
            results.append(f"💥 `{episode_id}` — 에러: {e}")

        progress_bar.progress((i + 1) / num_episodes)

        # Show running log
        with log_container:
            for r in results:
                st.markdown(r)

        if i < num_episodes - 1:
            time.sleep(delay)

    # Summary
    progress_bar.progress(1.0)
    status_text.empty()

    st.divider()
    st.subheader("수집 완료")
    col_a, col_b = st.columns(2)
    col_a.metric("성공", f"{successes}/{num_episodes}")
    col_b.metric("성공률", f"{successes/num_episodes*100:.0f}%")
