"""AI Simulation Dashboard — Main App."""

import sys
from pathlib import Path

# Add project root to path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar, get_unity_status, load_episodes
from src.config import settings
from src.scenes import get_scene

st.set_page_config(
    page_title="AI Simulation",
    page_icon="🤖",
    layout="wide",
)

config = setup_sidebar()

# Header with scene name
scene = get_scene(config["scene_name"])
st.title(f"AI Simulation — {scene.display_name}")

st.divider()

# Status overview
col1, col2, col3 = st.columns(3)

with col1:
    st.subheader("Unity Server")
    status = get_unity_status(config["unity_url"])
    if status:
        st.success(f"Connected — {config['unity_url']}")
    else:
        st.error("Disconnected")

with col2:
    st.subheader("LLM")
    st.info(f"{settings.llm_provider} / {settings.model_name}")

with col3:
    st.subheader("Data")
    episodes = load_episodes(config["data_dir"])
    total = len(episodes)
    success_count = sum(1 for e in episodes if e.get("success", False))
    if total > 0:
        st.metric("Episodes", total)
        st.metric("Success Rate", f"{success_count/total*100:.0f}%")
    else:
        st.info("No data collected")

st.divider()
st.markdown(
    """
### Pages
- **Run Agent** — Execute agent with real-time monitoring
- **History** — Browse saved episodes
- **Batch Collect** — Collect training data at scale
- **Playground** — Manually control tools and capture screenshots
- **Scene Editor** — Edit system prompts and tool descriptions
"""
)
