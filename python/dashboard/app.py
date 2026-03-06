"""Claw Machine Agent Dashboard — Main App."""

import sys
from pathlib import Path

# Add project root to path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import streamlit as st

from dashboard.components import setup_sidebar, get_unity_status

st.set_page_config(
    page_title="Claw Machine Agent",
    page_icon="🎮",
    layout="wide",
)

config = setup_sidebar()

st.title("🎮 Claw Machine Agent Dashboard")
st.markdown("Unity 클로머신 시뮬레이션을 LLM 에이전트로 제어하고 데이터를 수집합니다.")

st.divider()

# Status overview
col1, col2, col3 = st.columns(3)

with col1:
    st.subheader("Unity 서버")
    status = get_unity_status(config["unity_url"])
    if status:
        st.success(f"연결됨 — {config['unity_url']}")
    else:
        st.error("연결 안됨")

with col2:
    st.subheader("LLM 설정")
    if config["api_key"]:
        st.success(f"모델: {config['model_name']}")
    else:
        st.warning("API Key 미설정")

with col3:
    st.subheader("데이터")
    from dashboard.components import load_episodes

    episodes = load_episodes(config["data_dir"])
    total = len(episodes)
    success_count = sum(1 for e in episodes if e.get("success", False))
    if total > 0:
        st.metric("총 에피소드", total)
        st.metric("성공률", f"{success_count/total*100:.0f}%")
    else:
        st.info("수집된 데이터 없음")

st.divider()
st.markdown(
    """
### 페이지
- **에이전트 실행** — 명령어 입력 후 실시간 에피소드 모니터링
- **에피소드 히스토리** — 저장된 에피소드 탐색 및 상세 확인
- **배치 수집** — 대량 데이터 수집 및 데이터셋 변환
"""
)
