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
        """POST /step — execute an action, returns new observation.

        Action format:
            {"type": "move", "direction": "left", "duration": 0.3, "reasoning": "..."}
            {"type": "lower", "duration": 0.5}
            {"type": "raise", "duration": 0.5}
            {"type": "grip", "state": "close"}
            {"type": "camera", "direction": "left", "angle": 45}
            {"type": "done", "reasoning": "..."}
        """
        resp = self._session.post(
            f"{self.base_url}/step",
            json=action,
            timeout=self.timeout,
        )
        resp.raise_for_status()
        return resp.json()
