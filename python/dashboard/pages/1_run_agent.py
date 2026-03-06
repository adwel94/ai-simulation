"""Real-time agent execution page."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar, show_screenshot, show_action
from src.agent.graph import build_graph
from src.data.logger import EpisodeLogger

config = setup_sidebar()

st.title("🤖 에이전트 실행")

command = st.text_input(
    "명령어",
    value=st.session_state.get("last_command", "빨간 공을 집어"),
    placeholder="예: 빨간 공을 집어",
)
st.session_state["last_command"] = command

col_run, col_save = st.columns([1, 1])
with col_run:
    run_clicked = st.button(
        "▶️ 실행",
        use_container_width=True,
        disabled=not config["api_key"],
    )
with col_save:
    auto_save = st.checkbox("에피소드 자동 저장", value=True)

if not config["api_key"]:
    st.warning("사이드바에서 Google API Key를 설정하세요.")

if run_clicked and config["api_key"] and command.strip():
    graph = build_graph()
    episode_id = EpisodeLogger.generate_episode_id()

    initial_state = {
        "unity_url": config["unity_url"],
        "api_key": config["api_key"],
        "model_name": config["model_name"],
        "episode_id": episode_id,
        "command": command.strip(),
        "step": 0,
        "max_steps": config["max_steps"],
        "done": False,
        "messages": [],
        "episode_log": [],
    }

    st.info(f"에피소드 시작: {episode_id}")

    progress_bar = st.progress(0)
    status_text = st.empty()
    img_col, info_col = st.columns([1, 1])
    image_placeholder = img_col.empty()
    action_container = info_col.container()
    log_expander = st.expander("LLM 응답 로그", expanded=False)
    log_area = log_expander.empty()

    accumulated_logs = []
    final_state = initial_state

    try:
        for event in graph.stream(initial_state):
            node_name = list(event.keys())[0]
            node_output = event[node_name]

            if not node_output:
                continue

            # Merge into running state
            final_state = {**final_state, **node_output}

            if node_name == "observe":
                step = final_state.get("step", 0)
                max_s = final_state.get("max_steps", config["max_steps"])
                status_text.markdown(f"**스텝 {step}/{max_s}** — 관찰 중...")
                if final_state.get("screenshot_base64"):
                    image_placeholder.image(
                        __import__("base64").b64decode(final_state["screenshot_base64"]),
                        caption=f"Step {step}",
                        width=450,
                    )

            elif node_name == "think":
                status_text.markdown(
                    f"**스텝 {final_state.get('step', 0)}/{final_state.get('max_steps', 0)}** — LLM 응답 수신"
                )
                with action_container:
                    action_container.empty()
                    st.markdown("**현재 액션:**")
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

                status_text.markdown(
                    f"**스텝 {step}/{max_s}** — 액션 실행 완료"
                )

                if final_state.get("screenshot_base64"):
                    image_placeholder.image(
                        __import__("base64").b64decode(final_state["screenshot_base64"]),
                        caption=f"Step {step} (실행 후)",
                        width=450,
                    )

        # Episode complete
        progress_bar.progress(1.0)
        is_done = final_state.get("done", False)
        action_type = final_state.get("action", {}).get("type", "")
        success = is_done and action_type == "done"

        if success:
            st.success(
                f"✅ 에피소드 완료! ({final_state.get('step', 0)} 스텝) — "
                f"{final_state.get('done_reason', '')}"
            )
        else:
            reason = "최대 스텝 도달" if not is_done else final_state.get("done_reason", "")
            st.warning(f"⚠️ 에피소드 종료 ({final_state.get('step', 0)} 스텝) — {reason}")

        # Auto-save
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
            st.caption(f"💾 저장됨: {ep_dir}")

    except Exception as e:
        st.error(f"에러 발생: {e}")
