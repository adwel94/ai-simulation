import time

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry


class UnitySimClient:
    """REST API client for the Unity claw machine simulation server."""

    def __init__(self, base_url: str = "http://localhost:8765", timeout: float = 30.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout

        retry = Retry(
            total=3,
            backoff_factor=1,  # 1s -> 2s -> 4s
            status_forcelist=[502, 503],
            allowed_methods=["GET", "POST"],
        )
        self._session = requests.Session()
        self._session.mount("http://", HTTPAdapter(max_retries=retry))
        self._session.mount("https://", HTTPAdapter(max_retries=retry))

    def status(self) -> dict:
        """GET /status — check server health."""
        resp = self._session.get(f"{self.base_url}/status", timeout=10)
        resp.raise_for_status()
        return resp.json()

    def capture(self) -> dict:
        """GET /capture — capture screenshot without executing any action."""
        resp = self._session.get(f"{self.base_url}/capture", timeout=10)
        resp.raise_for_status()
        return resp.json()

    def reset(self) -> dict:
        """POST /reset — start a new episode, returns initial observation."""
        resp = self._session.post(f"{self.base_url}/reset", timeout=45)
        resp.raise_for_status()
        return resp.json()

    def world_state(self) -> dict:
        """GET /world_state — get ball positions, claw position, camera basis vectors."""
        resp = self._session.get(f"{self.base_url}/world_state", timeout=10)
        resp.raise_for_status()
        return resp.json()

    def step(self, action: dict) -> dict:
        """POST /step — fire-and-forget action execution. Returns immediately."""
        resp = self._session.post(
            f"{self.base_url}/step",
            json=action,
            timeout=5,
        )
        resp.raise_for_status()
        return resp.json()

    def wait_action_complete(self, timeout: float = 10.0, poll_interval: float = 0.05) -> dict:
        """Poll /status until action_in_progress == false."""
        start = time.time()
        while time.time() - start < timeout:
            status = self.status()
            if not status.get("action_in_progress", False):
                return status
            time.sleep(poll_interval)
        raise TimeoutError(f"Action not completed within {timeout}s")

    def step_and_observe(self, action: dict, settle: float = 0.3) -> dict:
        """step → wait → settle → capture. 기존 blocking step() 대체."""
        self.step(action)
        self.wait_action_complete()
        time.sleep(settle)
        return self.capture()
