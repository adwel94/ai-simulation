# Ball Picker VLM Training Dataset

Unity 크레인 게임 시뮬레이션에서 생성된 Vision Language Model(VLM) 학습용 데이터셋.

스크린샷을 보고 다음 행동을 예측하는 모델을 학습시키기 위한 (이미지, 행동) 쌍으로 구성됨.

---

## 데이터셋 구조

HuggingFace Datasets 포맷. 각 샘플은 **단일 스텝** (1장의 스크린샷 -> 1개의 행동).

```python
from datasets import load_dataset

ds = load_dataset("your-username/ball-picker-vlm")
sample = ds["train"][0]

sample["images"]    # List[PIL.Image] — 512x512 JPEG 스크린샷 1장
sample["messages"]  # List[dict] — system/user/assistant 메시지
```

### messages 구조

```json
[
  {
    "role": "system",
    "content": "당신은 인형뽑기(크레인 게임)를 플레이하는 AI입니다..."
  },
  {
    "role": "user",
    "content": [
      {"type": "image"},
      {"type": "text", "text": "[사용자 명령]: 빨간 공을 집어줘\n카메라 각도: 180도\n스텝 0/7. 다음 행동을 결정하세요."}
    ]
  },
  {
    "role": "assistant",
    "content": [
      {"type": "text", "text": "[{\"type\": \"move\", \"direction\": \"right\", \"duration\": 0.382, \"reasoning\": \"좌우 정렬: right으로 1.528m 이동\"}]"}
    ]
  }
]
```

- **system**: 에이전트의 역할과 전략을 설명하는 시스템 프롬프트 (한국어)
- **user**: 스크린샷 이미지 + 현재 상태 텍스트 (명령어, 카메라 각도, 스텝 번호)
- **assistant**: JSON 형식의 행동 배열 (학습 타겟)

### TRL/Unsloth 호환

이 포맷은 [TRL SFTTrainer의 VLM 학습 포맷](https://huggingface.co/docs/trl/main/en/training_vlm_sft)과 호환됨:

```python
from trl import SFTTrainer, SFTConfig

trainer = SFTTrainer(
    model=model,
    args=SFTConfig(dataset_text_field="messages"),
    train_dataset=ds["train"],
    eval_dataset=ds["validation"],
)
```

---

## 행동 타입

assistant 응답은 JSON 배열. 각 행동은 다음 타입 중 하나:

| type | 필드 | 설명 |
|------|------|------|
| `move` | direction, duration, reasoning | 집게를 이동. direction: left/right/forward/backward (카메라 기준). duration: 초 단위 |
| `lower` | reasoning | 집게를 바닥까지 내림 |
| `raise` | reasoning | 집게를 원래 높이로 올림 |
| `grip` | state, reasoning | state: "open" 또는 "close" |
| `camera` | direction, angle, reasoning | 카메라 회전. direction: left/right, angle: 도 단위 (보통 90) |
| `memo` | content | 에이전트의 내부 메모 (상태 추적용) |
| `done` | reasoning | 작업 완료 선언 |

### 행동 예시

```json
[{"type": "move", "direction": "right", "duration": 0.382, "reasoning": "좌우 정렬: right으로 1.528m 이동"}]
[{"type": "camera", "direction": "right", "angle": 90, "reasoning": "1차 좌우 정렬 완료. 카메라 90도 회전하여 깊이 축 확인"}]
[{"type": "lower", "reasoning": "2차 좌우 정렬 완료. 집게 내리기"}]
[{"type": "grip", "state": "close", "reasoning": "집게 닫기"}]
[{"type": "raise", "reasoning": "집게 올리기"}]
[{"type": "done", "reasoning": "공을 성공적으로 집어 올렸습니다"}]
```

---

## 데이터 생성 방식

### 알고리즘 오라클

LLM 대신 **좌표 기반 알고리즘 오라클**이 최적 행동을 계산하여 생성한 데이터.

1. Unity 시뮬레이션에서 공과 집게의 월드 좌표를 읽음
2. 카메라 기저벡터를 사용하여 월드 좌표 차이를 카메라 상대 방향으로 변환
3. 2축 정렬 전략 수행:
   - 현재 카메라에서 좌우(left/right)만 정렬
   - 카메라 90도 회전
   - 새 좌우로 나머지 축 정렬
   - lower -> grip close -> raise -> done
4. 각 스텝에서 스크린샷과 행동을 기록

### 노이즈 옵션

`noise_level` 파라미터로 행동에 약간의 오차를 추가할 수 있음.
- `0.0`: 완벽한 행동 (한 번에 정확히 정렬)
- `0.05`: 약간의 오버/언더슛 + 보정 스텝 포함 (데이터 다양성 증가)

---

## 시뮬레이션 환경

- **게임**: 크레인(클로) 머신. 3개의 색상 공 (빨강/파랑/초록)이 랜덤 배치
- **카메라**: 사선(isometric) 시점, Y축 중심 360도 회전 가능
- **이미지**: 512x512 JPEG
- **이동**: duration 기반 (moveSpeed=4.0 units/sec)
- **방향**: 모든 방향은 **현재 카메라 화면 기준** (카메라 회전 시 변경됨)

---

## 권장 학습 설정

| 항목 | 권장값 |
|------|--------|
| 베이스 모델 | `Qwen/Qwen3.5-9B` (네이티브 VLM) |
| 학습 방식 | bf16 LoRA (Unsloth 권장) |
| LoRA r | 16 |
| LoRA alpha | 16 |
| VRAM | ~22GB (L40S, RTX 4090/3090) |
| QLoRA | 비권장 (MoE 양자화 차이 문제) |
| Transformers | v5 이상 필수 |

### 학습 코드 예시

```python
from unsloth import FastLanguageModel
from trl import SFTTrainer, SFTConfig
from datasets import load_dataset

# 모델 로드
model, tokenizer = FastLanguageModel.from_pretrained(
    model_name="Qwen/Qwen3.5-9B",
    max_seq_length=2048,
    load_in_16bit=True,
)

# LoRA 적용
model = FastLanguageModel.get_peft_model(
    model,
    r=16,
    target_modules=["q_proj", "k_proj", "v_proj", "o_proj"],
    lora_alpha=16,
    lora_dropout=0,
)

# 데이터 로드
ds = load_dataset("your-username/ball-picker-vlm")

# 학습
trainer = SFTTrainer(
    model=model,
    tokenizer=tokenizer,
    train_dataset=ds["train"],
    eval_dataset=ds["validation"],
    args=SFTConfig(
        output_dir="outputs",
        per_device_train_batch_size=2,
        num_train_epochs=3,
        learning_rate=2e-4,
        bf16=True,
    ),
)
trainer.train()
```

---

## 시스템 프롬프트 전문

학습 데이터의 system message에 포함된 프롬프트:

```
당신은 인형뽑기(크레인 게임)를 플레이하는 AI입니다. 매 스텝마다 카메라 1대의 스크린샷을 받습니다.
카메라는 사선 시점으로 장면을 보여줍니다. camera 도구로 카메라를 회전시켜 다양한 각도에서 관찰할 수 있습니다.

중요 규칙:
- 스크린샷만 볼 수 있습니다. 좌표, 위치값, 수치 데이터에는 접근할 수 없습니다.
- 사람처럼 생각하세요: 이미지를 보고, 거리를 눈으로 가늠하고, 적절한 시간만큼 버튼을 누르세요.
- 작고 점진적인 움직임을 사용하세요. 이동 후 매번 새 스크린샷을 받아 결과를 확인합니다.

방향 규칙:
- forward/backward/left/right는 모두 현재 카메라 화면 기준입니다.
- left = 화면 왼쪽, right = 화면 오른쪽
- 카메라를 회전하면 left/right가 가리키는 월드 방향이 바뀝니다!

핵심 전략:
1. 스크린샷에서 목표 공과 집게의 위치를 파악
2. 현재 화면에서 좌우(left/right) 차이만 정렬
3. 좌우 정렬 완료 -> 카메라 90도 회전
4. 회전된 화면에서 새 좌우로 정렬
5. 양쪽 축 정렬 확인 -> lower -> grip close -> raise -> done
```
