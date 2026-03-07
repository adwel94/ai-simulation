"""Oracle batch data collection page — LLM 없이 좌표 기반 최적 행동 데이터 생성."""

import random
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar
from src.config import settings
from src.data.converter import convert_all_episodes, push_dataset_to_hub
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

col_start, col_convert, col_upload = st.columns(3)

with col_start:
    start_clicked = st.button(
        "Start Oracle Collection",
        use_container_width=True,
        disabled=not commands,
    )

with col_convert:
    convert_clicked = st.button("Convert Dataset", use_container_width=True)

with col_upload:
    upload_clicked = st.button("Upload to HuggingFace", use_container_width=True)

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

if upload_clicked:
    hf_repo_id = st.session_state.get("hf_repo_id", "")
    if not hf_repo_id:
        st.warning("아래에 HuggingFace repo ID를 입력하세요.")
    elif not settings.hf_token:
        st.error("HF_TOKEN이 .env에 설정되어 있지 않습니다.")
    else:
        with st.spinner("HuggingFace에 업로드 중..."):
            try:
                episodes_dir = str(Path(config["data_dir"]) / "episodes")
                train_count, val_count = push_dataset_to_hub(
                    repo_id=hf_repo_id,
                    episodes_dir=episodes_dir,
                )
                if train_count + val_count > 0:
                    st.success(f"Upload complete: train {train_count}, val {val_count} -> huggingface.co/datasets/{hf_repo_id}")
                else:
                    st.warning("No successful episodes to upload.")
            except Exception as e:
                st.error(f"Upload failed: {e}")

# HuggingFace settings
with st.expander("HuggingFace Settings"):
    st.text_input(
        "Repo ID",
        value="",
        placeholder="username/ball-picker-vlm",
        key="hf_repo_id",
        help="HuggingFace 데이터셋 repo ID (예: username/ball-picker-vlm)",
    )
    if settings.hf_token:
        st.success("HF_TOKEN 설정됨")
    else:
        st.warning("HF_TOKEN이 .env에 설정되어 있지 않습니다.")

if start_clicked and commands:
    client = UnitySimClient(base_url=config["unity_url"])
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
            episode_log, success = run_oracle_episode(
                client, command, noise_level=noise_level,
            )

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
            results.append(f"! `{episode_id}` — Error: {e}")

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
