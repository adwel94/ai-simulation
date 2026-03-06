# Claw Machine Agent

Unity 클로머신 시뮬레이션을 LLM(Gemini)으로 제어하고, 에피소드 데이터를 수집하는 Python 프로젝트입니다.

## 요구사항

- Python 3.11+
- Unity 시뮬레이션 서버 실행 중 (port 8765)
- Google API Key (Gemini)

## 설치

```bash
cd python
pip install -e .
```

## 설정

```bash
cp .env.example .env
```

`.env` 파일을 열어 값을 채웁니다:

```
GOOGLE_API_KEY=your-google-api-key
MODEL_NAME=gemini-2.5-flash
UNITY_SERVER_URL=http://localhost:8765
MAX_STEPS=50
```

> 대시보드 사용 시 `.env` 없이 UI에서 직접 입력할 수도 있습니다.

## 사용법

### 대시보드 (권장)

```bash
python -m streamlit run dashboard/app.py
```

브라우저에서 `http://localhost:8501` 이 열립니다.

- **에이전트 실행** — 명령어 입력 후 실시간 스크린샷/액션 모니터링
- **에피소드 히스토리** — 저장된 에피소드 탐색, 스텝별 스크린샷 확인
- **배치 수집** — 대량 에피소드 수집 + 데이터셋 변환

### CLI

```bash
# 단일 에피소드 실행
python scripts/run_agent.py -c "빨간 공을 집어"

# 배치 데이터 수집 (10 에피소드)
python scripts/collect_data.py -n 10

# 수집된 에피소드를 학습용 JSONL로 변환
python scripts/convert_dataset.py
```

## 프로젝트 구조

```
python/
├── dashboard/              # Streamlit 대시보드
│   ├── app.py              # 메인 앱
│   ├── components.py       # 공통 UI 컴포넌트
│   └── pages/
│       ├── 1_run_agent.py  # 실시간 에이전트 실행
│       ├── 2_history.py    # 에피소드 히스토리
│       └── 3_collect.py    # 배치 수집
├── src/
│   ├── agent/              # LangGraph 에이전트
│   │   ├── graph.py        # observe → think → act 루프
│   │   ├── nodes.py        # 각 노드 구현
│   │   ├── state.py        # 상태 스키마
│   │   └── prompts.py      # 시스템 프롬프트
│   ├── unity/
│   │   └── client.py       # Unity REST API 클라이언트
│   ├── data/
│   │   ├── logger.py       # 에피소드 저장
│   │   └── converter.py    # SFT 학습 포맷 변환
│   └── config.py           # 환경변수 설정
├── scripts/                # CLI 스크립트
├── data/                   # 수집된 데이터 (gitignore)
│   ├── episodes/           # 에피소드별 스크린샷 + metadata
│   └── dataset/            # train.jsonl, val.jsonl
├── pyproject.toml
└── .env.example
```

## Unity 서버 연동

Unity 에디터에서 시뮬레이션을 실행하고 **서버 시작** 버튼을 누르면 `http://localhost:8765`에서 REST API가 활성화됩니다.

| 엔드포인트 | 설명 |
|-----------|------|
| `GET /status` | 서버 상태 확인 |
| `POST /reset` | 새 에피소드 시작, 초기 스크린샷 반환 |
| `POST /step` | 액션 실행, 새 스크린샷 반환 |
