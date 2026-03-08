"""Episode history viewer page."""

import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar, load_episodes, show_action
from src.config import settings
from src.data.converter import convert_all_episodes, push_dataset_to_hub

config = setup_sidebar()

st.title("Episode History")

episodes = load_episodes(config["data_dir"])

if not episodes:
    st.info("No episodes saved yet. Run the agent to collect data.")
    st.stop()

# Statistics
col1, col2, col3 = st.columns(3)
total = len(episodes)
success_count = sum(1 for e in episodes if e.get("success", False))
avg_steps = sum(e.get("total_steps", 0) for e in episodes) / total if total > 0 else 0

col1.metric("Episodes", total)
col2.metric("Success Rate", f"{success_count/total*100:.0f}%" if total > 0 else "0%")
col3.metric("Avg Steps", f"{avg_steps:.1f}")

# Actions toolbar
btn_convert, btn_upload = st.columns(2)
with btn_convert:
    convert_clicked = st.button("변환하기", use_container_width=True,
                                help="로컬에 train.jsonl / val.jsonl 생성")
with btn_upload:
    upload_clicked = st.button("변환 및 업로드하기", use_container_width=True)

with st.expander("HuggingFace Settings"):
    st.text_input(
        "Repo ID",
        value="",
        placeholder="username/ball-picker-vlm",
        key="hf_repo_id",
        help="HuggingFace 데이터셋 repo ID (예: username/ball-picker-vlm)",
    )
    if settings.hf_token:
        st.caption("HF_TOKEN 설정됨")
    else:
        st.caption("HF_TOKEN이 .env에 설정되어 있지 않습니다.")

st.divider()

# Filters
filter_col1, filter_col2 = st.columns(2)
with filter_col1:
    status_filter = st.selectbox("Status", ["All", "Success", "Fail"])
with filter_col2:
    sort_order = st.selectbox("Sort", ["Newest", "Oldest"])

filtered = episodes
if status_filter == "Success":
    filtered = [e for e in filtered if e.get("success", False)]
elif status_filter == "Fail":
    filtered = [e for e in filtered if not e.get("success", False)]

if sort_order == "Oldest":
    filtered = list(reversed(filtered))


def _show_episode_detail(metadata: dict):
    ep_dir = Path(metadata.get("_dir", ""))
    steps = metadata.get("steps", [])

    if not steps:
        st.info("No step data")
        return

    if len(steps) == 1:
        step_idx = 0
        st.caption("Step 0 (1 step only)")
    else:
        step_idx = st.slider(
            "Step",
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
            st.info("Screenshot not found")

    with col_info:
        st.markdown(f"**Step {step_data.get('step', step_idx)}**")
        st.markdown(f"Camera angle: {step_data.get('camera_angle', 0):.0f}")

        actions = step_data.get("actions") or [step_data.get("action", {})]
        if actions:
            st.markdown("**Actions:**")
            for act in actions:
                show_action(act)

    with st.expander("Raw metadata"):
        display_meta = {k: v for k, v in metadata.items() if k != "_dir"}
        st.json(display_meta)


# Episode list
ITEMS_PER_PAGE = 20
total_pages = max(1, (len(filtered) + ITEMS_PER_PAGE - 1) // ITEMS_PER_PAGE)

page_col, info_col = st.columns([1, 2])
with page_col:
    page = st.number_input("Page", min_value=1, max_value=total_pages, value=1, key="page")
with info_col:
    st.markdown(f"**{len(filtered)}**개 에피소드 | {total_pages} 페이지")

page_start = (page - 1) * ITEMS_PER_PAGE
page_end = min(page_start + ITEMS_PER_PAGE, len(filtered))
page_items = filtered[page_start:page_end]

# Episode rows with delete button
for i, ep in enumerate(page_items):
    success = ep.get("success", False)
    icon = "O" if success else "X"
    ep_id = ep.get("episode_id", "?")
    timestamp = ep.get("timestamp", "")
    time_str = f" | {timestamp[:19]}" if timestamp else ""

    col_label, col_del = st.columns([5, 0.5])
    with col_label:
        label = (
            f"{icon} **{ep_id}** — "
            f"{ep.get('command', '?')} | "
            f"{ep.get('total_steps', 0)} steps{time_str}"
        )
        with st.expander(label):
            _show_episode_detail(ep)
    with col_del:
        if st.button("X", key=f"del_{ep_id}"):
            ep_dir = Path(ep.get("_dir", ""))
            if ep_dir.exists():
                shutil.rmtree(ep_dir)
            st.rerun()

# Handle actions
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
        st.warning("HuggingFace repo ID를 입력하세요.")
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
                    st.success(f"Upload complete: train {train_count}, val {val_count}")
                else:
                    st.warning("No successful episodes to upload.")
            except Exception as e:
                st.error(f"Upload failed: {e}")
