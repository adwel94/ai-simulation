"""Episode history viewer page."""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar, load_episodes, show_action

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

# Episode list
st.subheader(f"Episodes ({len(filtered)})")

for i, ep in enumerate(filtered):
    success = ep.get("success", False)
    icon = "O" if success else "X"
    label = (
        f"{icon} **{ep.get('episode_id', '?')}** — "
        f"{ep.get('command', '?')} | "
        f"{ep.get('total_steps', 0)} steps"
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
        st.info("No step data")
        return

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

        action = step_data.get("action", {})
        if action:
            st.markdown("**Action:**")
            show_action(action)

    with st.expander("Raw metadata"):
        display_meta = {k: v for k, v in metadata.items() if k != "_dir"}
        st.json(display_meta)
