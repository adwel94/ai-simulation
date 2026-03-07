# AI Simulation - Ball Picker

## 프로젝트 목적

LLM(대규모 언어 모델) 기반 AI 에이전트가 **크레인 볼 집기 게임**을 자율적으로 플레이하는 시뮬레이션 프로젝트.

사람이 직접 조작하는 것과 동일한 환경에서 AI가 **시각 정보(스크린샷)만 보고** 판단하여 크레인을 움직이고, 공을 집어 올린다.

## 핵심 아이디어

```
스크린샷 관찰 → LLM이 상황 판단 → 도구(Tool) 호출 → Unity에서 실행 → 다음 스크린샷 관찰 → ...
```

기존 강화학습(RL)과 달리, 사전 학습된 LLM의 시각 이해 능력과 추론 능력을 활용하여 별도의 학습 없이도 게임을 플레이할 수 있다.

## 기술 스택

| 영역 | 기술 | 역할 |
|------|------|------|
| 게임 환경 | **Unity** (C#) | 크레인 물리 시뮬레이션, 공 스폰, 스크린샷 캡처 |
| AI 에이전트 | **LangChain / LangGraph** (Python) | 관찰-사고-행동 루프, 도구 호출 |
| LLM | **Gemini / OpenAI 호환** | 스크린샷 분석, 행동 결정 |
| 대시보드 | **Streamlit** | 에이전트 실행/모니터링, 수동 조작, 에피소드 히스토리 |
| 통신 | **REST API** (HTTP) | Unity 서버 ↔ Python 클라이언트 |

## 아키텍처

### 전체 흐름

```
┌─────────────────┐         HTTP (REST)        ┌──────────────────┐
│   Python Agent   │ ◄─────────────────────────► │   Unity Server   │
│                  │    /reset, /step, /capture  │                  │
│  LangGraph 루프  │                             │  크레인 시뮬레이션  │
│  LLM (Gemini)   │                             │  물리 엔진        │
│  Tool Calling   │                             │  스크린샷 캡처     │
└─────────────────┘                             └──────────────────┘
        │
        ▼
┌─────────────────┐
│ Streamlit 대시보드│
│  실시간 모니터링   │
│  에피소드 히스토리  │
│  수동 Playground  │
└─────────────────┘
```

### 에이전트 루프 (LangGraph)

```
observe (스크린샷 촬영)
    ↓
think (LLM에게 스크린샷 + 이전 메모 전달 → 도구 호출 결정)
    ↓
act (선택한 도구를 Unity에서 실행 → 결과 스크린샷 수신)
    ↓
check_done (완료 여부 판단)
    ↓
observe로 반복 또는 종료
```

### Unity 게임 구조

크레인(집게 기계)은 ArticulationBody 기반의 물리 로봇으로 구성:

- **ClawMachineController** — 수평 이동 (X/Z축, TeleportRoot)
- **GripperDemoController** — 수직 이동 (올리기/내리기, xDrive)
- **PincherController** — 집게 열기/닫기 (zDrive)
- **BallSpawner** — 공 3개(빨강/파랑/초록) 랜덤 배치
- **ActionExecutor** — HTTP 요청을 받아 위 컨트롤러들을 조작

### AI 에이전트가 사용하는 도구 (Tools)

| 도구 | 설명 |
|------|------|
| `move` | 카메라 기준 좌/우/전/후 이동 (duration 지정) |
| `lower` | 크레인을 바닥까지 자동 하강 |
| `raise_claw` | 크레인을 원위치로 자동 상승 |
| `grip` | 집게 열기/닫기 |
| `camera` | 카메라 90도 단위 회전 |
| `wait` | 대기 |
| `done` | 작업 완료 선언 |

## 플레이어 vs AI

동일한 게임 환경에서 플레이어(키보드)와 AI 에이전트가 같은 API를 사용:

| 조작 | 플레이어 | AI 에이전트 |
|------|---------|------------|
| 수평 이동 | WASD / 방향키 | `move` 도구 |
| 내리기 | E | `lower` 도구 |
| 올리기 | Q | `raise_claw` 도구 |
| 집게 열기 | Z | `grip(open)` 도구 |
| 집게 닫기 | X | `grip(close)` 도구 |
| 카메라 회전 | [ / ] (90도) | `camera` 도구 |

## 데이터 파이프라인

에이전트의 플레이 데이터는 에피소드 단위로 저장:

```
data/episodes/
  ep_20260307_143000/
    metadata.json      ← 에피소드 요약 (성공/실패, 총 스텝, 명령어)
    step_001.json      ← 스텝별 상세 (액션, LLM 응답, 메시지 히스토리)
    step_001.jpg       ← 해당 스텝의 스크린샷
    ...
```

이 데이터는 향후 **지식 증류(Knowledge Distillation)**에 활용할 수 있다 — LLM의 판단 과정을 학습 데이터로 변환하여 더 작고 빠른 모델을 훈련.

## Scene 모듈 시스템

게임 환경을 플러그인처럼 교체할 수 있는 구조:

```
python/src/scenes/
  ball_picker/          ← 현재 구현된 씬
    tools.py            ← LangChain 도구 정의
    prompts.py          ← 시스템 프롬프트
    commands.py         ← 사용 가능한 명령어
    config.py           ← 씬 등록
```

새로운 게임 환경(예: 블록 쌓기, 미로 탈출 등)을 추가하려면 같은 구조로 새 씬 모듈을 만들면 된다.
