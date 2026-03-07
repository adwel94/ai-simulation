"""Shared UI components for the Streamlit dashboard."""

import base64
import json
from io import BytesIO
from pathlib import Path

import requests
import streamlit as st
from PIL import Image

from src.config import settings
from src.scenes import list_scenes, get_scene


def show_screenshot(b64: str, caption: str = "", width: int = 400):
    """Render a base64 JPEG screenshot."""
    if not b64:
        st.info("스크린샷 없음")
        return
    img_bytes = base64.b64decode(b64)
    img = Image.open(BytesIO(img_bytes))
    st.image(img, caption=caption, width=width)


def show_action(action: dict):
    """Render an action info card (generic, works with any scene)."""
    if not action:
        return
    action_type = action.get("type", "?")
    reasoning = action.get("reasoning", "")

    cols = st.columns([1, 3])
    with cols[0]:
        st.markdown(f"**{action_type.upper()}**")
    with cols[1]:
        details = {k: v for k, v in action.items() if k not in ("type", "reasoning")}
        st.text(" | ".join(f"{k}: {v}" for k, v in details.items()) if details else "-")

    if reasoning:
        st.caption(f"Reasoning: {reasoning}")


def show_episode_summary(metadata: dict):
    """Render an episode summary card."""
    success = metadata.get("success", False)
    status = "SUCCESS" if success else "FAIL"
    icon = "O" if success else "X"
    st.markdown(
        f"**{metadata.get('episode_id', '?')}** — {icon} {status} "
        f"| Steps: {metadata.get('total_steps', 0)} "
        f"| Command: {metadata.get('command', '?')}"
    )


def load_episodes(data_dir: str) -> list[dict]:
    """Load all episode metadata from the data directory."""
    episodes_path = Path(data_dir) / "episodes"
    if not episodes_path.exists():
        return []

    episodes = []
    for ep_dir in sorted(episodes_path.iterdir(), reverse=True):
        meta_path = ep_dir / "metadata.json"
        if meta_path.exists():
            try:
                meta = json.loads(meta_path.read_text(encoding="utf-8"))
                meta["_dir"] = str(ep_dir)
                episodes.append(meta)
            except (json.JSONDecodeError, OSError):
                pass
    return episodes


def get_unity_status(url: str) -> dict | None:
    """Check Unity server status. Returns status dict or None on failure."""
    try:
        resp = requests.get(f"{url.rstrip('/')}/status", timeout=3)
        resp.raise_for_status()
        return resp.json()
    except Exception:
        return None


def apply_wide_content():
    """Remove Streamlit's default content width limit."""
    st.markdown(
        """
        <style>
        .block-container { max-width: 100% !important; padding-left: 2rem; padding-right: 2rem; }
        </style>
        """,
        unsafe_allow_html=True,
    )


def setup_sidebar() -> dict:
    """Render sidebar settings and return config dict."""
    apply_wide_content()
    with st.sidebar:
        st.header("Settings")

        # Scene selection
        scenes = list_scenes()
        default_idx = scenes.index(settings.default_scene) if settings.default_scene in scenes else 0
        scene_name = st.selectbox(
            "Scene",
            options=scenes,
            index=default_idx,
            format_func=lambda s: get_scene(s).display_name,
            key="scene_select",
        )
        st.session_state["scene_name"] = scene_name

        st.divider()

        # Unity connection
        unity_url = st.text_input(
            "Unity Server URL",
            value=st.session_state.get("unity_url", settings.unity_server_url),
            key="unity_url_input",
        )
        st.session_state["unity_url"] = unity_url

        if st.button("Test Connection", use_container_width=True):
            status = get_unity_status(unity_url)
            if status:
                st.success("Connected!")
            else:
                st.error("Connection failed")

        st.divider()

        # Operational settings
        max_steps = st.number_input(
            "Max Steps",
            min_value=5,
            max_value=200,
            value=st.session_state.get("max_steps", settings.max_steps),
            key="max_steps_input",
        )
        st.session_state["max_steps"] = max_steps

        data_dir = st.text_input(
            "Data Directory",
            value=st.session_state.get("data_dir", settings.data_dir),
            key="data_dir_input",
        )
        st.session_state["data_dir"] = data_dir

        # LLM info (read-only)
        st.divider()
        st.caption(f"LLM Provider: {settings.llm_provider}")
        st.caption(f"Model: {settings.model_name}")

    return {
        "unity_url": unity_url,
        "scene_name": scene_name,
        "max_steps": max_steps,
        "data_dir": data_dir,
    }
