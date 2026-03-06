"""Real-time agent execution page."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar, show_action
from src.agent.graph import build_graph
from src.data.logger import EpisodeLogger
from src.scenes import get_scene

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
    }

    st.info(f"Episode: {episode_id}")

    progress_bar = st.progress(0)
    status_text = st.empty()
    img_col, info_col = st.columns([1, 1])
    image_placeholder = img_col.empty()
    action_container = info_col.container()
    log_expander = st.expander("LLM Response Log", expanded=False)
    log_area = log_expander.empty()

    accumulated_logs = []
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
                status_text.markdown(f"**Step {step}/{max_s}** — Observing...")
                if final_state.get("screenshot_base64"):
                    image_placeholder.image(
                        __import__("base64").b64decode(final_state["screenshot_base64"]),
                        caption=f"Step {step}",
                        width=450,
                    )

            elif node_name == "think":
                status_text.markdown(
                    f"**Step {final_state.get('step', 0)}/{final_state.get('max_steps', 0)}** — LLM response"
                )
                with action_container:
                    action_container.empty()
                    st.markdown("**Current Action:**")
                    show_action(final_state.get("action", {}))

                llm_resp = final_state.get("llm_response", "")
                if llm_resp:
                    accumulated_logs.append(
                        f"[Step {final_state.get('step', 0)}] {llm_resp}"
                    )
                    log_area.code("\n\n".join(accumulated_logs), language="json")

            elif node_name == "act":
                step = final_state.get("step", 0)
                max_s = final_state.get("max_steps", config["max_steps"])
                progress = min(step / max_s, 1.0) if max_s > 0 else 0
                progress_bar.progress(progress)

                status_text.markdown(f"**Step {step}/{max_s}** — Action executed")

                if final_state.get("screenshot_base64"):
                    image_placeholder.image(
                        __import__("base64").b64decode(final_state["screenshot_base64"]),
                        caption=f"Step {step} (after)",
                        width=450,
                    )

        # Episode complete
        progress_bar.progress(1.0)
        is_done = final_state.get("done", False)
        action_type = final_state.get("action", {}).get("type", "")
        success = is_done and action_type == "done"

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
