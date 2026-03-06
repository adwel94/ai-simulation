"""Episode history viewer page."""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar, load_episodes, show_action

config = setup_sidebar()

st.title("📋 에피소드 히스토리")

episodes = load_episodes(config["data_dir"])

if not episodes:
    st.info("저장된 에피소드가 없습니다. 에이전트를 실행하여 데이터를 수집하세요.")
    st.stop()

# Statistics
col1, col2, col3 = st.columns(3)
total = len(episodes)
success_count = sum(1 for e in episodes if e.get("success", False))
avg_steps = sum(e.get("total_steps", 0) for e in episodes) / total if total > 0 else 0

col1.metric("총 에피소드", total)
col2.metric("성공률", f"{success_count/total*100:.0f}%" if total > 0 else "0%")
col3.metric("평균 스텝", f"{avg_steps:.1f}")

st.divider()

# Filters
filter_col1, filter_col2 = st.columns(2)
with filter_col1:
    status_filter = st.selectbox("상태 필터", ["전체", "성공", "실패"])
with filter_col2:
    sort_order = st.selectbox("정렬", ["최신순", "오래된순"])

filtered = episodes
if status_filter == "성공":
    filtered = [e for e in filtered if e.get("success", False)]
elif status_filter == "실패":
    filtered = [e for e in filtered if not e.get("success", False)]

if sort_order == "오래된순":
    filtered = list(reversed(filtered))

# Episode list
st.subheader(f"에피소드 목록 ({len(filtered)}건)")

for i, ep in enumerate(filtered):
    success = ep.get("success", False)
    icon = "✅" if success else "❌"
    label = (
        f"{icon} **{ep.get('episode_id', '?')}** — "
        f"{ep.get('command', '?')} | "
        f"{ep.get('total_steps', 0)} 스텝"
    )
    timestamp = ep.get("timestamp", "")
    if timestamp:
        label += f" | {timestamp[:19]}"

    with st.expander(label):
        _show_episode_detail(ep)


def _show_episode_detail(metadata: dict):
    ep_dir = Path(metadata.get("_dir", ""))
    steps = metadata.get("steps", [])

    if not steps:
        st.info("스텝 데이터 없음")
        return

    # Step slider
    step_idx = st.slider(
        "스텝 선택",
        0,
        len(steps) - 1,
        0,
        key=f"slider_{metadata.get('episode_id', '')}",
    )

    step_data = steps[step_idx]

    col_img, col_info = st.columns([1, 1])

    with col_img:
        screenshot_file = ep_dir / step_data.get("screenshot", "")
        if screenshot_file.exists():
            st.image(str(screenshot_file), caption=f"Step {step_data.get('step', step_idx)}", width=400)
        else:
            st.info("스크린샷 파일 없음")

    with col_info:
        st.markdown(f"**스텝 {step_data.get('step', step_idx)}**")
        st.markdown(f"카메라 각도: {step_data.get('camera_angle', 0):.0f}도")

        action = step_data.get("action", {})
        if action:
            st.markdown("**액션:**")
            show_action(action)

    # Raw metadata
    with st.expander("metadata.json 원문"):
        display_meta = {k: v for k, v in metadata.items() if k != "_dir"}
        st.json(display_meta)
