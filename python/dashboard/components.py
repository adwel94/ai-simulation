"""Shared UI components for the Streamlit dashboard."""

import base64
import json
from io import BytesIO
from pathlib import Path

import requests
import streamlit as st
from PIL import Image

from src.config import settings


def show_screenshot(b64: str, caption: str = "", width: int = 400):
    """Render a base64 JPEG screenshot."""
    if not b64:
        st.info("스크린샷 없음")
        return
    img_bytes = base64.b64decode(b64)
    img = Image.open(BytesIO(img_bytes))
    st.image(img, caption=caption, width=width)


def show_action(action: dict):
    """Render an action info card."""
    if not action:
        return
    action_type = action.get("type", "?")
    reasoning = action.get("reasoning", "")

    color_map = {
        "move": "blue",
        "lower": "orange",
        "raise": "green",
        "grip": "red",
        "camera": "violet",
        "wait": "gray",
        "done": "green",
        "error": "red",
    }
    color = color_map.get(action_type, "gray")

    cols = st.columns([1, 3])
    with cols[0]:
        st.markdown(f":{color}[**{action_type.upper()}**]")
    with cols[1]:
        details = []
        if action.get("direction"):
            details.append(f"방향: {action['direction']}")
        if action.get("duration"):
            details.append(f"시간: {action['duration']}초")
        if action.get("state"):
            details.append(f"상태: {action['state']}")
        if action.get("angle"):
            details.append(f"각도: {action['angle']}도")
        st.text(" | ".join(details) if details else "-")

    if reasoning:
        st.caption(f"💭 {reasoning}")


def show_episode_summary(metadata: dict):
    """Render an episode summary card."""
    success = metadata.get("success", False)
    status = "✅ 성공" if success else "❌ 실패"
    st.markdown(
        f"**{metadata.get('episode_id', '?')}** — {status} "
        f"| 스텝: {metadata.get('total_steps', 0)} "
        f"| 명령: {metadata.get('command', '?')}"
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


def setup_sidebar() -> dict:
    """Render sidebar settings and return config dict."""
    with st.sidebar:
        st.header("⚙️ 설정")

        unity_url = st.text_input(
            "Unity 서버 URL",
            value=st.session_state.get("unity_url", settings.unity_server_url),
            key="unity_url_input",
        )
        st.session_state["unity_url"] = unity_url

        # Connection test
        if st.button("연결 테스트", use_container_width=True):
            status = get_unity_status(unity_url)
            if status:
                st.success("Unity 서버 연결됨!")
            else:
                st.error("연결 실패")

        st.divider()

        api_key = st.text_input(
            "Google API Key",
            type="password",
            value=st.session_state.get("api_key", settings.google_api_key),
            key="api_key_input",
        )
        st.session_state["api_key"] = api_key

        model_name = st.text_input(
            "모델명",
            value=st.session_state.get("model_name", settings.model_name),
            key="model_name_input",
        )
        st.session_state["model_name"] = model_name

        max_steps = st.number_input(
            "최대 스텝",
            min_value=5,
            max_value=200,
            value=st.session_state.get("max_steps", settings.max_steps),
            key="max_steps_input",
        )
        st.session_state["max_steps"] = max_steps

        data_dir = st.text_input(
            "데이터 디렉토리",
            value=st.session_state.get("data_dir", settings.data_dir),
            key="data_dir_input",
        )
        st.session_state["data_dir"] = data_dir

    return {
        "unity_url": unity_url,
        "api_key": api_key,
        "model_name": model_name,
        "max_steps": max_steps,
        "data_dir": data_dir,
    }
