"""Real-time agent execution page."""

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar, show_action
from src.agent.graph import build_graph
from src.data.logger import EpisodeLogger
from src.scenes import get_scene


_ROLE_LABEL = {
    "SystemMessage": ("SYSTEM", "gray"),
    "HumanMessage": ("USER", "blue"),
    "AIMessage": ("AI", "green"),
    "ToolMessage": ("TOOL", "orange"),
}


def _parse_debug_messages(debug_log: str) -> list[dict]:
    """Parse debug_log string into [{role, content}, ...]."""
    clean = debug_log.replace("--- LLM RESPONSE ---", "")
    blocks = re.split(r'\n*=== (\w+) ===\n', clean)
    results = []
    i = 1
    while i < len(blocks) - 1:
        results.append({"role": blocks[i].strip(), "content": blocks[i + 1].strip()})
        i += 2
    return results


def _render_chat_messages(container, messages: list[dict]):
    """Render parsed messages as styled cards in the given container."""
    with container:
        for msg in messages:
            label, color = _ROLE_LABEL.get(msg["role"], (msg["role"], "gray"))
            st.markdown(f"**:{color}[{label}]**")
            content = msg["content"]
            # System prompt is long — collapse it
            if msg["role"] == "SystemMessage" and len(content) > 300:
                with st.expander("System prompt", expanded=False):
                    st.code(content, language=None)
            else:
                st.code(content, language=None)
            st.divider()

config = setup_sidebar()
scene = get_scene(config["scene_name"])

st.title(f"Run Agent — {scene.display_name}")

default_cmd = scene.default_commands[0] if scene.default_commands else ""
command = st.text_input(
    "Command",
    value=st.session_state.get("last_command", default_cmd),
    placeholder=default_cmd,
)
st.session_state["last_command"] = command

col_run, col_save = st.columns([1, 1])
with col_run:
    run_clicked = st.button("Run", use_container_width=True)
with col_save:
    auto_save = st.checkbox("Auto-save episode", value=True)

if run_clicked and command.strip():
    graph = build_graph()
    episode_id = EpisodeLogger.generate_episode_id()

    initial_state = {
        "unity_url": config["unity_url"],
        "scene_name": config["scene_name"],
        "episode_id": episode_id,
        "command": command.strip(),
        "step": 0,
        "max_steps": config["max_steps"],
        "done": False,
        "messages": [],
        "episode_log": [],
        "data_dir": config["data_dir"],
        "llm_provider": config.get("llm_provider"),
        "model_name": config.get("model_name"),
        "openai_base_url": config.get("openai_base_url"),
    }

    _provider = config.get("llm_provider", "gemini")
    _model = config.get("model_name", "")
    _model_label = f"{_provider} / {_model}"
    if _provider == "openai" and config.get("openai_base_url"):
        _model_label += f"  ({config['openai_base_url']})"
    st.info(f"Episode: {episode_id}  |  Model: {_model_label}")

    progress_bar = st.progress(0)
    status_text = st.empty()
    img_col, info_col = st.columns([1, 1])
    image_placeholder = img_col.empty()
    action_wrapper = info_col.container(height=500)
    action_placeholder = action_wrapper.empty()
    debug_wrapper = st.container(height=500)
    debug_placeholder = debug_wrapper.empty()
    accumulated_actions = []
    final_state = initial_state

    try:
        for event in graph.stream(initial_state):
            node_name = list(event.keys())[0]
            node_output = event[node_name]

            if not node_output:
                continue

            final_state = {**final_state, **node_output}

            if node_name == "observe":
                step = final_state.get("step", 0)
                max_s = final_state.get("max_steps", config["max_steps"])
                status_text.markdown(
                    f"**Step {step}/{max_s}** — "
                    f":eye: **관측 중** — Unity에서 스크린샷 캡처"
                )
                if final_state.get("screenshot_base64"):
                    image_placeholder.image(
                        __import__("base64").b64decode(final_state["screenshot_base64"]),
                        caption=f"Step {step}",
                        width=450,
                    )

            elif node_name == "think":
                step = final_state.get("step", 0)
                max_s = final_state.get("max_steps", 0)
                actions = final_state.get("actions", [])
                tool_names = [a.get("type", "?") for a in actions if a.get("type") != "memo"]
                tool_summary = ", ".join(tool_names) if tool_names else "—"
                status_text.markdown(
                    f"**Step {step}/{max_s}** — "
                    f":brain: **추론 완료** — 호출: `{tool_summary}`"
                )
                accumulated_actions.append({
                    "step": step,
                    "actions": final_state.get("actions", []),
                })
                with action_placeholder.container():
                    for entry in accumulated_actions:
                        st.markdown(f"**Step {entry['step']}**")
                        for act in entry["actions"]:
                            show_action(act)
                        st.divider()
                    # Auto-scroll
                    st.components.v1.html(
                        "<script>"
                        "const f = window.parent.document;"
                        "const c = f.querySelectorAll('[data-testid=\"stVerticalBlock\"]');"
                        "for (const el of c) {"
                        "  if (el.parentElement && el.parentElement.style.overflow) {"
                        "    el.parentElement.scrollTop = el.parentElement.scrollHeight;"
                        "  }"
                        "}"
                        "</script>",
                        height=0,
                    )

                debug_log = final_state.get("debug_log", "")
                if debug_log:
                    parsed = _parse_debug_messages(debug_log)
                    _render_chat_messages(debug_placeholder.container(), parsed)

            elif node_name == "act":
                step = final_state.get("step", 0)
                max_s = final_state.get("max_steps", config["max_steps"])
                progress = min(step / max_s, 1.0) if max_s > 0 else 0
                progress_bar.progress(progress)

                actions = final_state.get("actions", [])
                tool_names = [a.get("type", "?") for a in actions if a.get("type") != "memo"]
                tool_summary = ", ".join(tool_names) if tool_names else "—"
                is_done = final_state.get("done", False)
                if is_done:
                    status_text.markdown(
                        f"**Step {step}/{max_s}** — "
                        f":checkered_flag: **완료** — `done` 실행됨"
                    )
                else:
                    status_text.markdown(
                        f"**Step {step}/{max_s}** — "
                        f":mechanical_arm: **실행 완료** — `{tool_summary}` Unity에 전송됨"
                    )

                if final_state.get("screenshot_base64"):
                    image_placeholder.image(
                        __import__("base64").b64decode(final_state["screenshot_base64"]),
                        caption=f"Step {step} (after)",
                        width=450,
                    )

        # Episode complete
        progress_bar.progress(1.0)
        is_done = final_state.get("done", False)
        last_actions = final_state.get("actions", [])
        last_action_type = last_actions[-1].get("type", "") if last_actions else ""
        success = is_done and last_action_type == "done"

        if success:
            st.success(
                f"Episode complete! ({final_state.get('step', 0)} steps) — "
                f"{final_state.get('done_reason', '')}"
            )
        else:
            reason = "Max steps reached" if not is_done else final_state.get("done_reason", "")
            st.warning(f"Episode ended ({final_state.get('step', 0)} steps) — {reason}")

        if auto_save:
            ep_logger = EpisodeLogger(
                data_dir=str(Path(config["data_dir"]) / "episodes")
            )
            ep_dir = ep_logger.save_episode(
                episode_id=episode_id,
                command=command.strip(),
                episode_log=final_state.get("episode_log", []),
                success=success,
                done_reason=final_state.get("done_reason", ""),
            )
            st.caption(f"Saved: {ep_dir}")

    except Exception as e:
        st.error(f"Error: {e}")
        # 에러 발생 시에도 지금까지의 데이터 저장
        if auto_save and final_state.get("episode_log"):
            try:
                ep_logger = EpisodeLogger(
                    data_dir=str(Path(config["data_dir"]) / "episodes")
                )
                ep_dir = ep_logger.save_episode(
                    episode_id=episode_id,
                    command=command.strip(),
                    episode_log=final_state.get("episode_log", []),
                    success=False,
                    done_reason=f"error: {e}",
                )
                st.caption(f"Saved (with error): {ep_dir}")
            except Exception:
                pass
