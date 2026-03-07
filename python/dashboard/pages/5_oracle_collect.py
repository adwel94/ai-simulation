"""Oracle batch data collection page — LLM 없이 좌표 기반 최적 행동 데이터 생성."""

import random
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar
from src.data.logger import EpisodeLogger
from src.oracle.runner import run_oracle_episode
from src.scenes import get_scene
from src.unity.client import UnitySimClient

config = setup_sidebar()
scene = get_scene(config["scene_name"])

st.title("Oracle Collect")
st.caption("좌표 기반 알고리즘 오라클로 최적 행동 데이터를 생성합니다. LLM 비용 없이 대량 수집 가능.")

# Settings
col1, col2 = st.columns(2)
with col1:
    num_episodes = st.number_input("Episodes", min_value=1, max_value=5000, value=10)
    delay = st.number_input("Delay (sec)", min_value=0.0, max_value=30.0, value=1.0, step=0.5)
with col2:
    noise_level = st.number_input(
        "Noise level",
        min_value=0.0, max_value=0.5, value=0.0, step=0.01,
        help="duration에 추가할 가우시안 노이즈의 표준편차. 0=완벽한 행동, 0.05=약간의 오차+보정 스텝 포함",
    )
    commands_text = st.text_area(
        "Commands (one per line)",
        value="\n".join(scene.default_commands),
        height=120,
    )

commands = [c.strip() for c in commands_text.strip().split("\n") if c.strip()]

if not commands:
    st.warning("명령어를 하나 이상 입력하세요.")

st.divider()

start_clicked = st.button(
    "Start Oracle Collection",
    use_container_width=True,
    disabled=not commands,
)

if start_clicked and commands:
    client = UnitySimClient(base_url=config["unity_url"])
    ep_logger = EpisodeLogger(data_dir=str(Path(config["data_dir"]) / "episodes"))

    progress_bar = st.progress(0)
    status_text = st.empty()
    log_container = st.container()

    successes = 0
    consecutive_errors = 0
    results = []
    stopped = False

    for i in range(num_episodes):
        command = random.choice(commands)
        episode_id = EpisodeLogger.generate_episode_id()

        status_text.markdown(f"**[{i+1}/{num_episodes}]** Episode `{episode_id}` — _{command}_")

        try:
            episode_log, success = run_oracle_episode(
                client, command, noise_level=noise_level,
            )

            consecutive_errors = 0

            if success:
                successes += 1

            done_reason = "oracle: 공을 성공적으로 집었습니다" if success else "oracle: 실패"

            ep_logger.save_episode(
                episode_id=episode_id,
                command=command,
                episode_log=episode_log,
                success=success,
                done_reason=done_reason,
            )

            steps = len(episode_log)
            icon = "O" if success else "X"
            results.append(f"{icon} `{episode_id}` — {command} ({steps} steps)")

        except Exception as e:
            consecutive_errors += 1
            results.append(f"! `{episode_id}` — Error: {e}")

            if consecutive_errors >= 10:
                results.append("!! 연속 10회 에러 — 수집 중단")
                stopped = True
                break
            elif consecutive_errors >= 5:
                results.append("! 연속 5회 에러 — 30초 대기 후 재시도...")
                time.sleep(30)

        progress_bar.progress((i + 1) / num_episodes)

        with log_container:
            for r in results:
                st.markdown(r)

        if i < num_episodes - 1 and delay > 0:
            time.sleep(delay)

    # Summary
    progress_bar.progress(1.0)
    status_text.empty()

    st.divider()
    st.subheader("Collection Complete")
    col_a, col_b = st.columns(2)
    col_a.metric("Success", f"{successes}/{num_episodes}")
    col_b.metric("Rate", f"{successes/num_episodes*100:.0f}%")
