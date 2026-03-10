"""Default commands for Fruit Picker scene."""

import random

DEFAULT_COMMANDS = [
    "사과를 집어",
    "바나나를 집어",
    "당근을 집어",
    "아무 과일이나 집어",
    "가장 가까운 물체를 집어",
]

# 오브젝트 이름 → 한국어 매핑
OBJECT_NAME_MAP = {
    "Fruit_사과": "사과",
    "Fruit_바나나": "바나나",
    "Fruit_오렌지": "오렌지",
    "Fruit_딸기": "딸기",
    "Fruit_수박": "수박",
    "Fruit_양배추": "양배추",
    "Fruit_당근": "당근",
    "Fruit_오이": "오이",
    "Fruit_고추": "고추",
    "Fruit_토마토": "토마토",
}


def adjust_command(client, command: str) -> str:
    """리셋 후 world_state에서 스폰된 오브젝트 확인, 명령어 대상이 없으면 교체."""
    # 범용 명령("아무 과일", "가장 가까운")은 조정 불필요
    generic_keywords = ["아무", "가까운"]
    if any(kw in command for kw in generic_keywords):
        return command

    world = client.world_state()
    objects = world.get("objects", world.get("balls", []))
    spawned_names = [
        OBJECT_NAME_MAP.get(o["name"], o["name"])
        for o in objects
        if o.get("name")
    ]

    # 명령어에 스폰된 오브젝트 이름이 포함되어 있으면 그대로
    for name in spawned_names:
        if name in command:
            return command

    # 대상이 없으면 랜덤 선택하여 명령어 생성
    target = random.choice(spawned_names) if spawned_names else "과일"
    return f"{target}을 집어"
