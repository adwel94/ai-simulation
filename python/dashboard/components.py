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

_LLM_SETTINGS_FILE = Path(settings.data_dir) / "llm_settings.json"


def _load_llm_settings() -> dict:
    """Load saved LLM settings from disk."""
    try:
        if _LLM_SETTINGS_FILE.exists():
            return json.loads(_LLM_SETTINGS_FILE.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        pass
    return {}


def _save_llm_settings(provider: str, model_name: str, base_url: str):
    """Save LLM settings to disk for persistence across sessions."""
    _LLM_SETTINGS_FILE.parent.mkdir(parents=True, exist_ok=True)
    # Load existing to preserve other provider's model name
    existing = _load_llm_settings()
    data = {
        "llm_provider": provider,
        "gemini_model": existing.get("gemini_model", settings.model_name),
        "openai_model": existing.get("openai_model", ""),
        "openai_base_url": base_url,
    }
    # Update current provider's model
    if provider == "gemini":
        data["gemini_model"] = model_name
    else:
        data["openai_model"] = model_name
    _LLM_SETTINGS_FILE.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")


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

        # LLM settings
        st.divider()
        st.subheader("LLM")

        # Load saved settings (only on first run)
        if "_llm_loaded" not in st.session_state:
            saved = _load_llm_settings()
            if saved:
                st.session_state.setdefault("llm_provider", saved.get("llm_provider", settings.llm_provider))
                st.session_state.setdefault("gemini_model", saved.get("gemini_model", settings.model_name))
                st.session_state.setdefault("openai_model", saved.get("openai_model", ""))
                st.session_state.setdefault("openai_base_url", saved.get("openai_base_url", settings.openai_base_url))
            st.session_state["_llm_loaded"] = True

        providers = ["gemini", "openai"]
        default_provider = st.session_state.get("llm_provider", settings.llm_provider)
        provider_idx = providers.index(default_provider) if default_provider in providers else 0
        llm_provider = st.selectbox("Provider", providers, index=provider_idx, key="llm_provider_select")
        st.session_state["llm_provider"] = llm_provider

        # Provider-specific model name
        model_key = f"{llm_provider}_model"
        default_model = settings.model_name if llm_provider == "gemini" else ""
        model_name = st.text_input(
            "Model",
            value=st.session_state.get(model_key, default_model),
            key=f"{llm_provider}_model_input",
        )
        st.session_state[model_key] = model_name

        openai_base_url = ""
        if llm_provider == "openai":
            openai_base_url = st.text_input(
                "Base URL",
                value=st.session_state.get("openai_base_url", settings.openai_base_url),
                key="openai_base_url_input",
                placeholder="https://your-server/v1",
            )
            st.session_state["openai_base_url"] = openai_base_url

        # Persist LLM settings to disk
        _save_llm_settings(llm_provider, model_name, openai_base_url)

    return {
        "unity_url": unity_url,
        "scene_name": scene_name,
        "max_steps": max_steps,
        "data_dir": data_dir,
        "llm_provider": llm_provider,
        "model_name": model_name,
        "openai_base_url": openai_base_url,
    }
