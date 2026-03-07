"""Manual control playground — directly operate tools and capture screenshots."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent))

import base64
import requests
import streamlit as st

from dashboard.components import setup_sidebar
from src.config import settings
from src.unity.client import UnitySimClient

config = setup_sidebar()

st.title("Playground")

client = UnitySimClient(base_url=config["unity_url"])

# Screenshot display
if "pg_screenshot" not in st.session_state:
    st.session_state["pg_screenshot"] = None
if "pg_info" not in st.session_state:
    st.session_state["pg_info"] = ""


def _update_screenshot(obs: dict):
    st.session_state["pg_screenshot"] = obs.get("screenshot_base64", "")
    step = obs.get("step", "?")
    angle = obs.get("camera_angle", 0)
    done = obs.get("done", False)
    st.session_state["pg_info"] = f"Step: {step} | Camera: {angle:.0f} | Done: {done}"


def _exec_action(action: dict):
    try:
        obs = client.step(action)
        _update_screenshot(obs)
    except requests.exceptions.HTTPError as e:
        if e.response is not None and e.response.status_code == 400:
            # Episode not active — auto-reset and retry
            try:
                client.reset()
                obs = client.step(action)
                _update_screenshot(obs)
            except Exception as retry_e:
                st.error(f"Auto-reset failed: {retry_e}")
        else:
            st.error(f"Error: {e}")
    except Exception as e:
        st.error(f"Error: {e}")


# Top controls
col_reset, col_capture = st.columns(2)
with col_reset:
    if st.button("Reset Episode", use_container_width=True):
        try:
            obs = client.reset()
            _update_screenshot(obs)
        except Exception as e:
            st.error(f"Reset error: {e}")

with col_capture:
    if st.button("Capture Screenshot", use_container_width=True):
        try:
            obs = client.capture()
            _update_screenshot(obs)
        except Exception as e:
            st.error(f"Capture error: {e}")

# Screenshot
if st.session_state["pg_screenshot"]:
    st.image(
        base64.b64decode(st.session_state["pg_screenshot"]),
        caption=st.session_state["pg_info"],
        width=500,
    )
else:
    st.info("Press 'Reset Episode' or 'Capture Screenshot' to start.")

st.divider()

# Movement controls
st.subheader("Movement")
duration = st.slider("Duration (sec)", 0.1, 3.0, 0.5, 0.1, key="pg_duration")

move_cols = st.columns(5)
with move_cols[0]:
    if st.button("Left", use_container_width=True):
        _exec_action({"type": "move", "direction": "left", "duration": duration})
with move_cols[1]:
    if st.button("Right", use_container_width=True):
        _exec_action({"type": "move", "direction": "right", "duration": duration})
with move_cols[2]:
    if st.button("Forward", use_container_width=True):
        _exec_action({"type": "move", "direction": "forward", "duration": duration})
with move_cols[3]:
    if st.button("Backward", use_container_width=True):
        _exec_action({"type": "move", "direction": "backward", "duration": duration})
with move_cols[4]:
    if st.button("Wait", use_container_width=True):
        _exec_action({"type": "wait", "duration": duration})

# Vertical controls
st.subheader("Vertical")
vert_cols = st.columns(2)
with vert_cols[0]:
    if st.button("Lower (auto)", use_container_width=True):
        _exec_action({"type": "lower"})
with vert_cols[1]:
    if st.button("Raise (auto)", use_container_width=True):
        _exec_action({"type": "raise"})

# Grip controls
st.subheader("Grip")
grip_cols = st.columns(2)
with grip_cols[0]:
    if st.button("Open Grip", use_container_width=True):
        _exec_action({"type": "grip", "state": "open"})
with grip_cols[1]:
    if st.button("Close Grip", use_container_width=True):
        _exec_action({"type": "grip", "state": "close"})

# Camera controls
st.subheader("Camera")
cam_angle = st.slider("Angle (deg)", 10, 90, 90, 5, key="pg_cam_angle")
cam_cols = st.columns(2)
with cam_cols[0]:
    if st.button("Rotate Left", use_container_width=True):
        _exec_action({"type": "camera", "direction": "left", "angle": cam_angle})
with cam_cols[1]:
    if st.button("Rotate Right", use_container_width=True):
        _exec_action({"type": "camera", "direction": "right", "angle": cam_angle})

# Done
st.divider()
if st.button("Done", use_container_width=True):
    _exec_action({"type": "done", "reasoning": "Manual done"})
