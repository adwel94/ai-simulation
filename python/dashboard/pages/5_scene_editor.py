"""Scene Editor — edit system prompts and tool descriptions, persisted to local JSON."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar
from src.scenes import get_scene
from src.scene_store import scene_store

config = setup_sidebar()
scene = get_scene(config["scene_name"])

st.title(f"Scene Editor — {scene.display_name}")

# Load current overrides
saved = scene_store.load(config["scene_name"])

# ── System Prompt ──
st.subheader("System Prompt")

current_prompt = saved.get("system_prompt", "") or scene.system_prompt
prompt_text = st.text_area(
    "Edit the system prompt sent to the LLM",
    value=current_prompt,
    height=300,
    key="editor_prompt",
)

is_prompt_modified = prompt_text != scene.system_prompt
if is_prompt_modified:
    st.caption("(modified from default)")

st.divider()

# ── Tool Descriptions ──
st.subheader("Tool Descriptions")
st.caption("Edit tool descriptions that the LLM sees when deciding which tool to use.")

tool_overrides = dict(saved.get("tool_overrides", {}))
tool_texts = {}

for tool in scene.tools:
    tool_name = tool.name
    default_desc = tool.description
    current_desc = tool_overrides.get(tool_name, {}).get("description", "") or default_desc

    with st.expander(f"**{tool_name}**", expanded=False):
        edited = st.text_area(
            f"Description for '{tool_name}'",
            value=current_desc,
            height=120,
            key=f"tool_desc_{tool_name}",
        )
        tool_texts[tool_name] = edited

        if edited != default_desc:
            st.caption("(modified from default)")

st.divider()

# ── Save / Reset ──
col_save, col_reset = st.columns(2)

with col_save:
    if st.button("Save", use_container_width=True, type="primary"):
        # Build save data
        save_data = {}

        # Only save prompt if different from default
        if prompt_text != scene.system_prompt:
            save_data["system_prompt"] = prompt_text

        # Only save tool overrides that differ from default
        overrides = {}
        for tool in scene.tools:
            edited_desc = tool_texts.get(tool.name, "")
            if edited_desc and edited_desc != tool.description:
                overrides[tool.name] = {"description": edited_desc}
        if overrides:
            save_data["tool_overrides"] = overrides

        scene_store.save(config["scene_name"], save_data)
        st.success(f"Saved to data/scene_config/{config['scene_name']}.json")

with col_reset:
    if st.button("Reset to Default", use_container_width=True):
        scene_store.save(config["scene_name"], {})
        st.success("Reset to defaults. Refresh the page to see changes.")
        st.rerun()
